"""Read-only, session-specific triage. Evidence is not a balance certification.

Each issue retains exact row references, alternatives and missing proof. Bounded
examples do not truncate counts. No interpretation treats missing events as zero.
Existing accounting analyzers remain the authority for full ledger reconciliation.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
from EvidenceTimelines import Timelines
from EvidenceHealth import Health
from EvidenceUnknowns import Unknowns
from EvidenceReview import Review, update_impact
from EvidenceWalletLedger import WalletLedger


COVERAGE = {
    'wallet_reconciliation': ['WALLET_CHECK', 'WALLET_CHANGE'],
    'town_cash_model_application': ['TOWN_CASH_BEGIN', 'TOWN_CASH_END'],
    'market_cash_and_stock': ['MARKET_BEGIN', 'MARKET_DELTA', 'MARKET_END'],
    'workshop_inputs': ['WORKSHOP_INPUT_WITNESS', 'WORKSHOP_GATE'],
    'workshop_production': ['WORKSHOP_CYCLE', 'WORKSHOP_FLOW_CHECK'],
    'workshop_profitability': ['WORKSHOP_CASH_BOUNDARY', 'WORKSHOP_STATE'],
    'cash_purpose': ['CASH_PURPOSE_BEGIN', 'CASH_PURPOSE_END'],
    'supplier_decisions': ['PROCUREMENT_PLAN', 'PROCUREMENT_CANDIDATE'],
    'procurement_accounting': ['PROCUREMENT_LEDGER', 'PROCUREMENT_TRANSFER', 'PROCUREMENT_ACCOUNTING'],
    'procurement_delivery': ['PROCUREMENT_MOVEMENT'],
    'quest_deadlines': ['QUEST_COVERAGE', 'QUEST_DEADLINE'],
    'quest_lifecycle': ['QUEST_LIFECYCLE_BEGIN', 'QUEST_LIFECYCLE_END'],
    'logistics_lifecycle': ['LOGISTICS_BEGIN', 'LOGISTICS_END'],
    'ship_lifecycle': ['SHIP_OWNER_CHANGE', 'SHIP_MEMBERSHIP', 'SHIP_DESTRUCTION'],
    'framework_configuration': ['CORE_SYSTEMS'],
    'naval_rewards': ['REWARD_END', 'REWARD_VALUATION'],
    'cost_and_loss': ['DIAGNOSTIC_COST', 'DAY_TIMING'],
}


def details(text):
    return dict(part.strip().split('=', 1) for part in text.split(';') if '=' in part)


def number(value):
    try:
        result = float(value)
    except (TypeError, ValueError):
        return None
    return result if math.isfinite(result) else None


class Evidence:
    def __init__(self, session):
        self.session = session
        self.events = Counter()
        self.first_last = {}
        self.findings = {}
        self.links = Counter()
        self.last_sequence = None
        self.last_day = None
        self.started = self.closed = False
        self.stop_reason = None
        self.town_operations = {}
        self.pending_tail = False
        self.integrity = []
        self.other_session_rows = 0
        self.timelines = Timelines()
        self.health = Health()
        self.unknowns = Unknowns()
        self.open_lifecycle = {}
        self.review = Review()
        self.wallet_ledger = WalletLedger()
        self.first_day = None

    def flag(self, code, row, finding, attribution, explanation, alternatives, missing):
        key = code + '|' + row.get('owner', 'capture')
        entry = self.findings.setdefault(key, dict(
            issue=code, entity=row.get('owner'), occurrences=0,
            finding=finding, attribution=attribution,
            explanation=explanation, false_alarm_checks=alternatives,
            false_positive_status='not_determined',
            missing_evidence=missing, evidence=[]))
        entry['occurrences'] += 1
        update_impact(entry, row)
        if len(entry['evidence']) < 3:
            entry['evidence'].append({k: row.get(k) for k in
                                     ('line', 'sequence', 'day', 'kind', 'metric', 'before', 'after', 'detail')})

    def observe(self, row):
        if row['session'] != self.session:
            self.other_session_rows += 1
            return
        seq = int(row['sequence'])
        if self.last_sequence is None and seq != 1:
            self.integrity.append('Selected session does not begin at sequence 1')
        day, before, after = (number(row[x]) for x in ('day', 'before', 'after'))
        if None in (day, before, after):
            raise ValueError('Nonfinite or invalid numeric evidence at line ' + str(row['line']))
        if self.last_sequence is not None and seq != self.last_sequence + 1:
            self.integrity.append('Nonconsecutive sequence at line ' + str(row['line']))
        if self.last_day is not None and day < self.last_day:
            self.integrity.append('Backward campaign time at line ' + str(row['line']))
        if self.closed:
            self.integrity.append('Records after session closure at line ' + str(row['line']))
        self.last_sequence, self.last_day = seq, day
        if self.first_day is None: self.first_day = day
        self.review.observe(row)
        self.wallet_ledger.observe(row)
        kind, metric, d = row['kind'], row['metric'], details(row['detail'])
        self.timelines.observe(row, d)
        self.health.observe(row, d, number)
        self.unknowns.observe(row, d, number)
        if kind in ('QUEST_LIFECYCLE_BEGIN', 'LOGISTICS_BEGIN', 'CASH_PURPOSE_BEGIN'):
            key = (kind.split('_')[0], d.get('operation'))
            if key in self.open_lifecycle: self.integrity.append('Duplicate lifecycle operation at line ' + str(row['line']))
            self.open_lifecycle[key] = row
        if kind in ('QUEST_LIFECYCLE_END', 'LOGISTICS_END', 'CASH_PURPOSE_END'):
            start = self.open_lifecycle.pop((kind.split('_')[0], d.get('operation')), None)
            if (start is None or start['owner'] != row['owner'] or start['metric'] != metric
                    or details(start['detail']).get('parentOperation') != d.get('parentOperation')):
                self.integrity.append('Unmatched lifecycle end at line ' + str(row['line']))
            if d.get('error') not in (None, 'none', ''):
                self.flag('lifecycle_exception', row, 'observed_exception', 'witnessed',
                          'The observed lifecycle boundary reported an exception.',
                          ['A throwing call is not proof of successful completion or lost inventory.'],
                          ['Inspect the exception, operation endpoints and diagnostic stop reason.'])
        self.events[kind] += 1
        span = self.first_last.setdefault(kind, [seq, seq]); span[1] = seq
        if kind == 'SESSION_START':
            if self.started: self.integrity.append('Repeated SESSION_START')
            self.started = True
        if kind == 'SESSION_END':
            self.closed = True; self.stop_reason = metric
        if kind == 'WALLET_CHANGE':
            found = False
            for field in ('market', 'workshopCycle', 'townCashOperation', 'procurementTransfer', 'reward', 'cashPurposeOperation', 'logisticsOperation'):
                if d.get(field) not in (None, '', 'none', '0'):
                    self.links[field] += 1; found = True
            if not found: self.links['no_structured_operation_link'] += 1
        if kind in ('WALLET_CHECK', 'WORKSHOP_FLOW_CHECK') and before != after:
            self.flag('wallet_residual' if kind == 'WALLET_CHECK' else 'workshop_flow_residual', row,
                      'observed_discrepancy', 'unknown', 'Expected and observed balances differ.',
                      ['Nested cash must be netted, not added twice.', 'Event coverage or capture failure can explain a residual.'],
                      ['Full accounting analyzer and first divergent operation; not proof of created money or lost stock.'])
        if kind == 'OBSERVER_COVERAGE' and metric == 'outer_boundary_recovered_activity':
            self.flag('observer_missed_nested_activity', row, 'observed_discrepancy', 'witnessed',
                      'Independent outer wallet readings found activity missing from nested receipts; the outer boundary recovered the net amount.',
                      ['Direct writes and inlining are alternatives, not proven causes.',
                       'Recovered flow is already in WALLET_CHANGE; never add this gross witness again.'],
                      ['Locate the bypassed execution path; offsetting hidden writes may still be invisible.'])
        if kind == 'WALLET_CHECK':
            finding = self.findings.get('wallet_residual|' + row['owner'])
            if finding:
                finding['latest_wallet_check'] = {k: row[k] for k in ('line', 'sequence', 'before', 'after')}
                finding['false_positive_status'] = ('not_present_at_latest_snapshot_historical_difference_retained'
                                                    if before == after else 'present_at_latest_snapshot_cause_unknown')
        if kind == 'TOWN_CASH_BEGIN':
            key = d.get('townCashOperation')
            if key in self.town_operations: self.integrity.append('Duplicate open town cash operation ' + str(key))
            self.town_operations[key] = row
        if kind == 'TOWN_CASH_END':
            start = self.town_operations.pop(d.get('townCashOperation'), None)
            valid = (start is not None and start['owner'] == row['owner'] and number(start['before']) == before
                     and d.get('originalRan') == 'True' and d.get('modelCalls') == '1'
                     and d.get('error') == 'none' and number(d.get('modelResult')) is not None)
            if not valid:
                self.flag('town_cash_incomplete', row, 'unresolved', 'unknown', 'Town cash operation lacks an intact native/model witness.',
                          ['A skipped original or exception is not a native subsidy.'], ['Matched begin/end, actual result and original execution.'])
            else:
                matches = number(d['modelResult']) == after - before
                self.flag('town_cash_matches_model' if matches else 'town_cash_application_difference', row,
                          'reconciled' if matches else 'observed_discrepancy', 'witnessed',
                          'Actual model result compared with the enclosing town cash change.',
                          ['Zero and negative model changes are valid.', 'Boundary includes other patches; a match is not proof of balance.'],
                          [] if matches else ['Inspect ChangeGold requests, wallet net deltas and patch owners.'])
        if kind == 'WORKSHOP_INPUT_WITNESS' and d.get('nativeAccepted') == 'False':
            self.flag('input_gate_rejected', row, 'observed_rejection', 'witnessed',
                      'Native input check rejected this attempt; required units and market stock are recorded.',
                      ['Repeated attempts are not independent shortages.', 'Private stock can be prepaid, blocked, or unavailable to this recipe.'],
                      ['Join private ledger, deliveries and next successful attempt before diagnosing sustained shortage.'])
        if kind == 'PROCUREMENT_CANDIDATE' and metric != 'witness_budget':
            self.flag('supplier_' + metric, row, 'observed_rejection', 'witnessed',
                      'Planner rejected this candidate at the recorded predicate.',
                      ['Other candidates can succeed.', 'Invalid distance is not proof of an unreachable route.', 'Rejected quotes are not realized profit.'],
                      ['Final selected offer and actual transaction outcome.'])
        if kind == 'DIAGNOSTIC_COST' and (number(d.get('discardedRows')) or 0) > 0:
            self.flag('discarded_records', row, 'observed_discrepancy', 'witnessed', 'Capture explicitly reports discarded records.',
                      ['Explanation sampling is separate from dropped accounting.'], ['Do not certify accounting completeness.'])

    def report(self):
        ledger = self.wallet_ledger.report()
        conflicts = {x['owner'] for x in ledger['issues'] if x['code'] == 'conflicting_same_snapshot'}
        for finding in self.findings.values():
            if finding['issue'] == 'wallet_residual' and finding['entity'] in conflicts:
                finding['false_positive_status'] = 'conflicting_same_snapshot_requires_identity_review'
        coverage = {}
        for area, required in COVERAGE.items():
            missing = [event for event in required if not self.events[event]]
            coverage[area] = dict(evidence='events_observed_not_certified' if not missing else
                                 'incomplete' if len(missing) < len(required) else 'not_exercised_or_unsupported',
                                 missing_events=missing)
        if not self.started: self.integrity.append('SESSION_START not observed')
        if self.closed and self.town_operations: self.integrity.append('Closed session has unmatched town cash operations')
        if self.closed and self.open_lifecycle: self.integrity.append('Closed session has unmatched lifecycle operations')
        return dict(schema='aoc_causal_report_v1', session=self.session,
                    campaign_window=dict(first_day=self.first_day, last_day=self.last_day),
                    review_metrics=self.review.report(),
                    wallet_ledger=ledger,
                    status='integrity_review_required' if self.integrity else 'closed_observed' if self.closed else 'open_or_interrupted',
                    stop_reason=self.stop_reason, integrity=self.integrity, pending_last_line=self.pending_tail,
                    excluded_other_session_rows=self.other_session_rows,
                    open_town_operations=len(self.town_operations), coverage=coverage,
                    execution={k: dict(count=v, first_sequence=self.first_last[k][0], last_sequence=self.first_last[k][1])
                               for k, v in sorted(self.events.items())},
                    causal_context_links=dict(self.links),
                    timelines=self.timelines.report(self.closed),
                    diagnostic_health=self.health.report(coverage),
                    unknown_investigations=self.unknowns.report(),
                    finding_counts=dict(Counter(x['finding'] for x in self.findings.values())),
                    false_positive_review_counts=dict(Counter(x['false_positive_status'] for x in self.findings.values())),
                    findings=sorted(self.findings.values(), key=lambda x: (x['finding'] != 'observed_discrepancy', x['issue'], x['entity'])),
                    limits=['Context links show nesting, not exclusive cause.',
                            'No events does not mean no issue; older builds cannot supply new witnesses.',
                            'This triage does not replace wallet, stock, profitability, procurement or naval accounting analyzers.',
                            'Quest lifecycle, player-only penalties and logistics need their specific acceptance evidence.',
                            'An open scope or partial last line while writing is pending, not a confirmed integrity failure.'])


def analyze(path, session):
    path = Path(path)
    evidence = Evidence(session)
    digest = hashlib.sha256()
    # Fixed readable prefix: later appends are deliberately excluded from this report.
    remaining = path.stat().st_size
    size = remaining
    with path.open('rb') as stream:
        header_bytes = stream.readline(remaining); remaining -= len(header_bytes); digest.update(header_bytes)
        header = header_bytes.decode('utf-8-sig').rstrip('\r\n').split('\t')
        required = {'session', 'sequence', 'day', 'kind', 'owner', 'metric', 'before', 'after', 'detail'}
        if not required.issubset(header): raise ValueError('Missing required log columns')
        line = 1
        while remaining:
            raw = stream.readline(remaining)
            if not raw: raise ValueError('Log shrank while reading; retry against preserved evidence')
            remaining -= len(raw); digest.update(raw); line += 1
            if not raw.endswith(b'\n') and not remaining:
                evidence.pending_tail = True; break
            fields = raw.decode('utf-8').rstrip('\r\n').split('\t')
            if len(fields) != len(header): raise ValueError('Malformed complete line ' + str(line))
            row = dict(zip(header, fields)); row['line'] = line
            evidence.observe(row)
    if not evidence.events: raise ValueError('Requested session not found; refusing to follow another session')
    result = evidence.report()
    result['source'] = dict(path=str(path.resolve()), captured_prefix_bytes=size, prefix_sha256=digest.hexdigest())
    return result


def markdown(report):
    findings = report['findings']
    historical = [f for f in findings if f.get('false_positive_status', '').startswith('not_present_at_latest')]
    active = [f for f in findings if f not in historical and f['finding'] != 'reconciled']
    lines = ['# Diagnostic evidence report', '', 'Session: ' + report['session'], '',
             'Status: ' + report['status'] + '. This is not balance certification.', '',
             'Historical discrepancy groups absent at the latest snapshot: ' + str(len(historical)) + '.',
             'Other groups requiring review: ' + str(len(active)) + '.',
             'Historical observations are retained in JSON. Disappearing at a later snapshot does not explain the earlier cause.', '',
             '## Findings requiring review', '']
    if not active: lines += ['None in these narrow triage rules. Check independent audits and coverage gaps below.', '']
    for finding in active[:20]:
        lines += ['### ' + finding['issue'] + ' — ' + str(finding['entity']), '',
                  finding['explanation'], '', 'Finding: ' + finding['finding'] + '. Attribution: ' + finding['attribution'] + '.',
                  'Occurrences: ' + str(finding['occurrences']) + '.',
                  'Measured impact: ' + json.dumps(finding['impact']) + '.',
                  'Confidence: ' + json.dumps(finding['confidence']) + '.',
                  'False-positive review: ' + finding.get('false_positive_status', 'not_determined') + '.',
                  'Evidence lines: ' + ', '.join(str(x['line']) for x in finding['evidence']) + '.',
                  'False-alarm checks: ' + ' '.join(finding['false_alarm_checks']),
                  'Missing proof: ' + (' '.join(finding['missing_evidence']) or 'None for this narrow comparison.'), '']
    if len(active) > 20: lines += [str(len(active) - 20) + ' additional groups are retained in the matching JSON.', '']
    lines += ['## Coverage gaps', '']
    lines += ['- ' + k + ': ' + v['evidence'] + '; missing ' + ', '.join(v['missing_events'])
              for k, v in report['coverage'].items() if v['missing_events']]
    lines += ['', '## Limits', ''] + ['- ' + x for x in report['limits']]
    health = report.get('diagnostic_health', {})
    timelines = report.get('timelines', {})
    unknowns = report.get('unknown_investigations', {}).get('cases', {})
    lines += ['', '## Connected evidence', '',
              'Order timelines: ' + str(len(timelines.get('orders', {}))) + '; duplicate receipts: ' + str(timelines.get('duplicate_receipts', 0)) + '.',
              'Residual lifecycle: ' + json.dumps(health.get('lifecycle_counts', {})) + '.',
              'Unknown-cause investigations: ' + str(len(unknowns)) + '. Each retains evidence, hypotheses and missing proof in JSON.',
              'Wallet purposes are grouped per wallet; do not add overlapping wallet views or confuse withdrawals with profit.',
              'Capture cost and 30-day growth estimates: ' + json.dumps(health.get('watchdog', {}).get('cost', {})) + '.', '']
    for key, case in list(unknowns.items())[:8]:
        lines += ['- ' + key + ': ' + case['status'] + '. Next: ' + case['next_investigation']]
    if 'independent_audits' in report:
        lines += ['', '## Independent accounting audits', '',
                  'Full results are in the matching JSON under independent_audits. Completion is not a pass verdict.', '']
        for area, audit in report['independent_audits'].items():
            lines += ['- ' + area + ': ' + audit['status'] + (': ' + audit['error'] if 'error' in audit else '')]
            summary = audit.get('summary', {})
            for key in ('problems', 'coverage_gaps'):
                value = summary.get(key)
                if isinstance(value, dict):
                    lines += ['  - ' + key + ': ' + str(value['entries']) + '. ' + '; '.join(map(str, value.get('examples', [])))]
    return '\n'.join(lines) + '\n'


def write_report(result, base):
    from EvidenceAuditBundle import summarize
    from EvidenceAcceptance import checklist
    result['acceptance_checklist'] = checklist(result)
    base = Path(base)
    destinations = [Path(str(base) + suffix) for suffix in ('.json', '.md', '.details')]
    if any(p.exists() for p in destinations): raise FileExistsError('Report output already exists: ' + str(base))
    audits = result.get('independent_audits', {})
    extended = ('timelines', 'diagnostic_health', 'unknown_investigations')
    if audits or any(section in result for section in extended):
        destinations[2].mkdir()
    if audits:
        for area, audit in audits.items():
            if 'evidence' not in audit: continue
            evidence = audit.pop('evidence')
            path = destinations[2] / (area + '.json')
            with path.open('x', encoding='utf-8') as out: json.dump(evidence, out, indent=2, allow_nan=False)
            audit['summary'] = summarize(evidence)
            audit['detail_path'] = str(path.resolve())
    readable = markdown(result)
    readable += '\n## Evidence acceptance checklist\n\n'
    for name, item in result['acceptance_checklist']['scenarios'].items():
        readable += '- ' + name + ': ' + item['status'] + ' — ' + item['basis'] + '\n'
    for section in extended:
        if section not in result: continue
        evidence = result[section]
        path = destinations[2] / (section + '.json')
        with path.open('x', encoding='utf-8') as out: json.dump(evidence, out, indent=2, allow_nan=False)
        result[section] = dict(summary=summarize(evidence), detail_path=str(path.resolve()))
        readable += '\nDetailed ' + section.replace('_', ' ') + ': [' + path.name + '](<' + str(path.resolve()) + '>).\n'
    with destinations[0].open('x', encoding='utf-8') as out: json.dump(result, out, indent=2, allow_nan=False)
    with destinations[1].open('x', encoding='utf-8') as out: out.write(readable)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture'); parser.add_argument('--session', required=True)
    parser.add_argument('--output', required=True, help='New report basename; never overwrites evidence')
    parser.add_argument('--full', action='store_true', help='Also run every independent accounting audit; requires closed single-session evidence')
    args = parser.parse_args()
    result = analyze(args.capture, args.session)
    if args.full:
        if result['status'] != 'closed_observed' or result['pending_last_line'] or result['excluded_other_session_rows']:
            raise ValueError('Full audit requires intact closed single-session evidence; use triage for active/mixed logs')
        from EvidenceAuditBundle import audit
        result['independent_audits'] = audit(args.capture, expected_sha256=result['source']['prefix_sha256'])
    write_report(result, args.output)
    print(json.dumps({'status': result['status'], 'findings': len(result['findings']), 'report': args.output}))
