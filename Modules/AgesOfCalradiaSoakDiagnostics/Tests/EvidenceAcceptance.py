"""One explicit checklist; a passing narrow check is never balance certification."""
from EvidenceAuditBundle import AUDITS


def checklist(report):
    scenarios = {}
    def put(name, status, basis, evidence=None):
        scenarios[name] = dict(status=status, basis=basis, evidence=evidence)
    intact = report['status'] == 'closed_observed' and not report['pending_last_line']
    put('capture_integrity', 'failed' if report['integrity'] or any(f['issue'] == 'discarded_records' for f in report['findings']) else 'passed' if intact else 'not_exercised',
        'Closed sequential evidence required; an active tail is not corruption.', report.get('source'))
    events, findings = report['execution'], report['findings']
    wallet = report.get('review_metrics', {}).get('wallets', {})
    ledger = report.get('wallet_ledger', {})
    for name, field in (('wallet_independent_ledger', 'accounting_status'),
                        ('wallet_identity_integrity', 'identity_status')):
        status = ledger.get(field, 'unsupported')
        if status == 'passed' and not intact: status = 'not_exercised'
        put(name, status, 'First baseline plus net recorded changes; duplicate identities remain errors.',
            dict(wallets=ledger.get('wallets'), counts=ledger.get('counts')))
    put('wallet_snapshot_reconciliation', 'failed' if any(x['discrepant_checks'] for x in wallet.values())
        else 'passed' if wallet and intact else 'not_exercised',
        'All observed wallet checks, retaining historical residuals; not proof all mutations were captured.',
        dict(wallet_count=len(wallet)))
    gaps = [f for f in findings if f['issue'] == 'observer_missed_nested_activity']
    put('nested_wallet_observer_coverage', 'failed' if gaps else
        'passed' if intact and report.get('review_metrics', {}).get('observer_coverage', {}).get('nested_activity_reconciled') else 'not_exercised',
        'Failed means missing nested observation recovered by an outer boundary, not an economy defect.',
        dict(gap_groups=len(gaps), execution=events.get('OBSERVER_COVERAGE')))
    health = report.get('diagnostic_health', {})
    for purpose in ('native_kingdom_budget_grant', 'native_daily_finance_settlement',
                    'tribute_wallet_settlement', 'kingdom_budget_distribution', 'native_party_wage_payment', 'native_party_funding', 'market_trade'):
        linked = [p for p in health.get('wallet_purposes', []) if p['purpose'] == purpose]
        put('money_source/' + purpose, 'passed' if intact and linked else 'not_exercised',
            'Matched executed scope plus wallet movement; nesting is not exclusive cause. Wages/funding is a combined boundary.',
            dict(wallet_groups=len(linked)))
    recorder = health.get('watchdog', {}).get('cost', {}).get('incident_recorder')
    put('incident_retention', 'unsupported' if recorder is None else
        'failed' if recorder.get('status') != 'ready' else 'not_exercised',
        'Runtime health only; package contents require the offline recorder fixture or a preserved incident. Zero incidents is not a failure.', recorder)
    audits = report.get('independent_audits', {})
    # Only explicit failure/gap contracts plus observed activity can pass.
    contracts = {
        'procurement_transactions': 'transactions',
        'quest_and_framework_state': 'finite_quest_observations',
        'naval_rewards_and_cleanup': 'counts',
        'optional_and_rare_lifecycle_branches': 'features',
    }
    for area in AUDITS:
        audit = audits.get(area)
        if audit is None:
            put(area, 'not_exercised', 'Run the independent audit over preserved closed evidence; no new gameplay implied.')
            continue
        evidence = audit.get('evidence')
        if audit.get('status') == 'analysis_failed':
            put(area, 'failed', 'Independent analyzer failed: ' + audit.get('error', 'unknown'))
            continue
        if not isinstance(evidence, dict):
            put(area, 'unsupported', 'Detailed independent evidence unavailable; summary is not certification.')
            continue
        errors = [key for key in ('problems', 'batch_discrepancies', 'discrepancies') if evidence.get(key)]
        replay = evidence.get('wallet_independent_replay', {})
        if replay.get('accounting_status') == 'failed' or replay.get('identity_status') == 'failed':
            errors.append('wallet_independent_replay')
        gaps = evidence.get('coverage_gaps', [])
        if errors:
            put(area, 'failed', 'Independent evidence requires review: ' + ', '.join(errors), audit.get('analyzer'))
        elif gaps:
            put(area, 'not_exercised', 'Required evidence missing.', dict(gap_count=len(gaps)))
        elif area in contracts and 'problems' in evidence and 'coverage_gaps' in evidence and evidence.get(contracts[area]):
            put(area, 'passed' if intact else 'not_exercised', 'Only the independent analyzer contract for observed scenarios passed.', audit.get('analyzer'))
        else:
            put(area, 'unsupported', 'No supported automatic pass contract; retain full audit for review. Completion alone is not a pass.')
    return dict(schema=1, scenarios=scenarios,
        status='failed_checks' if any(s['status'] == 'failed' for s in scenarios.values()) else
               'coverage_gaps' if any(s['status'] != 'passed' for s in scenarios.values()) else 'narrow_checks_passed',
        limits=['This is not a balance or Nexus release certificate.',
                'Unsupported or unexercised branches do not require another run automatically.',
                'Offline broken-observer fixtures remain separate from campaign evidence.'])
