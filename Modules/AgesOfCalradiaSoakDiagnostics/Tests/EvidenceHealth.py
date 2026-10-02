"""Explicit observation lifecycle, wallet purpose and lower-bound capture costs."""
from collections import Counter
import json
from EvidenceCashPurpose import resolve


class Health:
    def __init__(self):
        self.residuals = {}
        self.money = {}
        self.cost = None
        self.first_day = None
        self.last_day = None
        self.day_seconds = self.day_span = 0.0
        self.stock = {}
        self.optional = {}
        self.hooks = {}
        self.lifecycle_execution = Counter()
        self.cash_outcomes = {}
        self.cash_starts = {}
        self.cash_pending = {}
        self.first_cost = None
        self.cost_day = None

    @staticmethod
    def record_money(target, wallet, purpose, basis, totals):
        group = target.setdefault(wallet + '|' + purpose, dict(wallet=wallet, purpose=purpose, evidence=basis,
                                                               inflow=0, outflow=0, net=0, count=0))
        for key in ('inflow', 'outflow', 'net', 'count'): group[key] += totals[key]

    def observe(self, row, d, number):
        day = number(row['day'])
        if self.first_day is None:
            self.first_day = day
        self.last_day = day
        kind = row['kind']
        if kind == 'CASH_PURPOSE_BEGIN': self.cash_starts[d.get('operation')] = (row, d.get('parentOperation'))
        if kind == 'CASH_PURPOSE_END':
            operation = d.get('operation')
            begin = self.cash_starts.pop(operation, None)
            valid = (begin is not None and begin[0]['owner'] == row['owner'] and begin[0]['metric'] == row['metric']
                     and begin[1] == d.get('parentOperation')
                     and d.get('originalRan') == 'True' and d.get('error') == 'none')
            self.cash_outcomes[operation] = dict(purpose=row['metric'], valid=valid, parent=d.get('parentOperation'))
        if kind == 'CAPTURE_HOOK' and ('QuestBase' in row['owner'] or 'AgesOfCalradiaLogistics' in row['owner']):
            self.hooks[row['owner'] + ':' + row['metric'] + ':' + str(d.get('token'))] = dict(
                method=row['metric'], owner=row['owner'], mvid=d.get('mvid'), token=d.get('token'))
        if kind in ('QUEST_LIFECYCLE_END', 'LOGISTICS_END'):
            outcome = 'executed' if d.get('originalRan') == 'True' and d.get('error') == 'none' else 'skipped_or_failed'
            self.lifecycle_execution[(kind, row['metric'], outcome)] += 1
        if kind in ('WALLET_CHECK', 'WORKSHOP_FLOW_CHECK'):
            key = kind + ':' + row['owner'] + ':' + row['metric']
            failed = number(row['before']) != number(row['after'])
            if failed or key in self.residuals:
                state = self.residuals.setdefault(key, dict(episodes=0, observations=0, consecutive=0,
                    active=False, first_sequence=row['sequence'], first_day=day))
                was_active = state['active']
                state['observations'] += 1
                if failed and not was_active:
                    state['episodes'] += 1
                state['consecutive'] = state['consecutive'] + 1 if failed else 0
                state['active'] = failed
                state['last_sequence'], state['last_day'] = row['sequence'], day
                state['state'] = ('no_longer_observed_not_explained' if not failed else
                                  'recurring' if state['episodes'] > 1 else
                                  'persistent_across_samples' if state['consecutive'] > 1 else 'new')
                state['cause'] = 'unknown'
        if kind == 'WALLET_CHANGE':
            # Explicit contexts outrank stack hints, but do not prove sole cause.
            labels = [('procurementTransfer', 'procurement'), ('townCashOperation', 'town_cash_model'),
                      ('market', 'market_trade'), ('workshopCycle', 'workshop_cycle'), ('reward', 'reward')]
            matches = [label for field, label in labels if d.get(field) not in (None, '', '0', 'none')]
            purpose = matches[0] if matches else 'unexplained'
            basis = 'structured_context_not_exclusive_cause' if matches else 'no_structured_purpose'
            # Only exact witnessed financial boundary fields, never keyword guesses from a call stack.
            boundary = d.get('cashCategory')
            if boundary in ('owner_payout', 'operating_expense', 'capital_reset', 'finance_unclassified'):
                purpose, basis = boundary, 'observed_boundary_not_complete_operating_profit'
            delta = number(row['after']) - number(row['before'])
            totals = dict(count=1, net=delta, inflow=max(0, delta), outflow=max(0, -delta))
            operation = d.get('cashPurposeOperation')
            if operation not in (None, 'none', ''):
                pending = self.cash_pending.setdefault(operation, {}).setdefault(row['owner'], dict(count=0, net=0, inflow=0, outflow=0))
                for key in totals: pending[key] += totals[key]
                return
            self.record_money(self.money, row['owner'], purpose, basis, totals)
        if kind == 'DIAGNOSTIC_COST':
            self.cost = dict(writer_seconds_lower_bound=number(row['after']), bytes=number(d.get('bytes')),
                             wall_seconds=number(d.get('wallSeconds')), discarded_rows=number(d.get('discardedRows')),
                             pending_rows=number(d.get('pendingRows')), observer_costs=None)
            self.cost_day = day
            if self.first_cost is None and self.cost['bytes'] is not None:
                self.first_cost = (day, self.cost['bytes'])
            try:
                self.cost['observer_costs'] = json.loads(d.get('measuredObserverCosts', 'null'))
            except (ValueError, TypeError):
                self.cost['observer_costs_status'] = 'invalid_evidence'
            try:
                self.cost['incident_recorder'] = json.loads(d.get('incidentRecorder', 'null'))
            except (ValueError, TypeError):
                self.cost['incident_recorder'] = {'status': 'invalid_evidence'}
        if kind == 'DAY_TIMING':
            span = number(row['after']) - number(row['before'])
            seconds = number(d.get('wallSeconds'))
            if seconds is not None and seconds >= 0 and span > 0:
                self.day_seconds += seconds
                self.day_span += span
        if kind == 'WORKSHOP_INPUT_WITNESS':
            key = row['owner'] + ':' + row['metric']
            self.stock[key] = dict(required=number(row['before']), market=number(row['after']),
                private_total=number(d.get('privateStock')), private_state=d.get('privateState', 'unsupported'),
                eligible_this_attempt=number(d.get('eligiblePrivate')), reserved=number(d.get('reservedPrivate')),
                in_transit=number(d.get('inTransitPrivate')), blocked=number(d.get('blockedPrivate')),
                unknown=number(d.get('unknownPrivate')), order=d.get('order'),
                sequence=row['sequence'], native_accepted=d.get('nativeAccepted'),
                verdict='attempt_evidence_not_sustained_shortage')
        if kind == 'FEATURE_COVERAGE':
            self.optional[row['owner']] = row['metric']

    def report(self, coverage):
        hooks = {}
        for key, hook in self.hooks.items():
            family = 'QUEST_LIFECYCLE_END' if 'QuestBase' in hook['owner'] else 'LOGISTICS_END'
            count = self.lifecycle_execution[(family, hook['method'], 'executed')]
            hooks[key] = dict(hook, method_execution_count=count,
                             status='method_events_observed_overload_not_certified' if count else 'installed_but_execution_not_witnessed')
        cost = dict(self.cost or {})
        if self.first_cost and cost.get('bytes') is not None:
            days = self.cost_day - self.first_cost[0]
            growth = cost['bytes'] - self.first_cost[1]
            if days > 0 and growth >= 0:
                cost['bytes_per_campaign_day_average'] = growth / days
                cost['projected_30_day_bytes_if_rate_continues'] = growth / days * 30
                cost['growth_basis'] = 'difference_between_cost_witnesses_not_preexisting_file_bytes'
        wall = cost.get('wall_seconds')
        if wall and wall > 0:
            cost['writer_fraction_lower_bound'] = cost['writer_seconds_lower_bound'] / wall
        cost['seconds_per_day_including_pauses'] = self.day_seconds / self.day_span if self.day_span else None
        money = {key: dict(value) for key, value in self.money.items()}
        for operation, pending in self.cash_pending.items():
            for wallet, totals in pending.items():
                self.record_money(money, wallet, resolve(operation, self.cash_outcomes), 'matched_scope_required_not_exclusive_cause', totals)
        return dict(residual_lifecycle=self.residuals, lifecycle_counts=dict(Counter(x['state'] for x in self.residuals.values())),
                    wallet_purposes=list(money.values()), latest_input_evidence=self.stock,
                    watchdog=dict(cost=cost, optional_features=self.optional,
                                  lifecycle_hook_execution=hooks,
                                  missing_execution={area: item['missing_events'] for area, item in coverage.items() if item['missing_events']}),
                    limits=['Persistent means consecutive sampled discrepancies, not proof of continuous failure between samples.',
                            'No discrepancy is marked explained without causal proof. Disappearance alone is not resolution.',
                            'Wallet totals must not be summed across hero/clan/kingdom views; overlapping identities are possible.',
                            'Grants, tribute, trade and finance labels require matched executed scopes; unsupported routes remain unexplained.',
                            'Wage assessments are calculated amounts, not extra cash. Party finance may combine wages and funding.',
                            'Owner withdrawals and capital resets are separate from operating profit.',
                            'Writer and observer timings overlap and cannot be added; uninstrumented cost remains unknown.',
                            'Missing execution is a coverage gap, not automatically a broken hook.'])
