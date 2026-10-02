"""Offline impact measurements; confidence never follows magnitude."""
from collections import Counter


class Review:
    def __init__(self):
        self.wallets = {}
        self.cycles = Counter()
        self.unknown_cycles = Counter()
        self.observer_coverage = Counter()

    def observe(self, row):
        if row['kind'] == 'OBSERVER_COVERAGE': self.observer_coverage[row['metric']] += 1
        if row['kind'] == 'WORKSHOP_CYCLE':
            metric = row['metric']
            if metric in ('failed', 'failed_see_gates'):
                self.cycles['unsuccessful'] += 1
            elif metric == 'succeeded':
                self.cycles['successful'] += 1
            else:
                self.unknown_cycles[metric] += 1
        if row['kind'] == 'WALLET_CHECK':
            # Keep wallet views AND metrics separate: they may overlap identities.
            key = row['owner'] + '|' + row['metric']
            item = self.wallets.setdefault(key, dict(checks=0, discrepant_checks=0,
                peak_absolute_residual=0, latest_residual=0))
            residual = float(row['after']) - float(row['before'])
            item['checks'] += 1
            item['discrepant_checks'] += int(residual != 0)
            item['peak_absolute_residual'] = max(item['peak_absolute_residual'], abs(residual))
            item['latest_residual'] = residual

    def report(self):
        attempts = sum(self.cycles.values())
        return dict(wallets=self.wallets, workshop_cycles=dict(self.cycles),
            observer_coverage=dict(self.observer_coverage),
            unrecognized_cycle_metrics=dict(self.unknown_cycles),
            unsuccessful_attempt_fraction=(self.cycles['unsuccessful'] / attempts
                if attempts and not self.unknown_cycles else None),
            profitability=dict(status='requires_independent_operating_ledger',
                reason='Capital changes and owner withdrawals are not operating profit.'),
            limits=['Unsuccessful attempts are not independent shortages.',
                    'Residuals are snapshot differences, not cumulative lost or created gold.',
                    'Wallet views must not be summed across overlapping identities.'])


def update_impact(finding, row):
    day = float(row['day'])
    impact = finding.setdefault('impact', dict(affected_entities=[row['owner']],
        first_observed_day=day, last_observed_day=day, observed_span_days=0,
        magnitude_by_metric={}, duration_basis='sample_span_not_continuous_failure'))
    impact['last_observed_day'] = day
    impact['observed_span_days'] = day - impact['first_observed_day']
    if row['kind'] in ('WALLET_CHECK', 'WORKSHOP_FLOW_CHECK'):
        magnitude = impact['magnitude_by_metric'].setdefault(row['metric'], dict(
            unit='gold' if row['kind'] == 'WALLET_CHECK' else 'native_metric_units',
            peak_absolute_residual=0, latest_discrepant_residual=0, aggregation='never_sum_snapshots'))
        delta = float(row['after']) - float(row['before'])
        magnitude['peak_absolute_residual'] = max(magnitude['peak_absolute_residual'], abs(delta))
        magnitude['latest_discrepant_residual'] = delta
    finding['confidence'] = dict(observation='recorded_evidence',
        causal_attribution=('unknown' if finding['attribution'] == 'unknown'
                           else 'witnessed_boundary_not_exclusive_cause'),
        severity='not_inferred_from_magnitude')
