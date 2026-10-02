"""Analyze closed reload segments separately; never add overlapping wallet baselines."""
import csv
import importlib.util
import json
import math
from pathlib import Path
import sys
import tempfile

csv.field_size_limit(24000000)


def evidence_summary(evidence):
    """Surface named findings without converting missing evidence into a pass."""
    errors, gaps = [], []
    error_keys = {'problems', 'capture_blockers', 'batch_discrepancies', 'receipt_problems'}
    gap_keys = {'coverage_gaps', 'missing_evidence', 'stock_residuals', 'wallet_residuals',
                'unaligned_stock_windows', 'unmapped_nonproduction_wallets', 'recipes_without_attempts'}
    def visit(value, path=''):
        if not isinstance(value, dict): return
        for key, child in value.items():
            location = path+'.'+key if path else key
            if key in error_keys | gap_keys and child:
                target = errors if key in error_keys else gaps
                target.append(dict(path=location, count=len(child)))
            elif isinstance(child, dict): visit(child, location)
    visit(evidence)
    return dict(status='FINDINGS_REQUIRE_REVIEW' if errors else 'EVIDENCE_GAPS_REMAIN' if gaps else 'MANUAL_REVIEW_REQUIRED',
                errors=errors, evidence_gaps=gaps,
                limit='An empty finding list does not certify economic balance or unexercised branches.')


def compact_verdict(evidence):
    """Give operators a short status without hiding raw evidence or coverage gaps."""
    findings = evidence['combined_findings']
    workshop = evidence.get('2_workshop_payments', {}).get('batch_evidence') or {}
    matched = workshop.get('matched_capital_affecting_successful_batches', 0)
    workshop_status = ('RECONCILED_FOR_CAPTURED_BATCHES'
                       if workshop.get('closed') and not workshop.get('problems')
                       and not workshop.get('batch_discrepancies') and matched
                       else 'NOT_CERTIFIED')
    procurement = evidence.get('procurement_transactions', {})
    reward = evidence.get('reward_accounting', {})
    if findings['errors']:
        status = 'FINDINGS_REQUIRE_REVIEW'
    elif findings['evidence_gaps']:
        status = 'EVIDENCE_GAPS_REMAIN'
    else:
        status = 'MANUAL_REVIEW_REQUIRED'
    return dict(
        status=status,
        workshop_accounting=dict(status=workshop_status, matched_batches=matched),
        procurement_accounting=dict(problems=len(procurement.get('problems', [])),
                                    coverage_gaps=len(procurement.get('coverage_gaps', []))),
        naval=reward.get('naval_coverage', dict(
            naval_scopes_observed=0,
            player_naval_scopes_observed=0,
            player_penalty_status='NOT_REPORTED',
            valued_ship_count=0,
            ship_valuation_status='NOT_REPORTED')),
        naval_policy_acceptance={key: evidence.get('naval_acceptance', {}).get(key)
                                 for key in ('status', 'counts', 'logging', 'stop_reason')},
        reported_errors=len(findings['errors']),
        reported_coverage_gaps=len(findings['evidence_gaps']),
        limit='This is a navigation summary. Raw evidence and named coverage gaps remain authoritative.')


def evaluate(path):
    def load(name):
        spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + '.py'))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    result = load('Analyze-EconomyAcceptance').analyze(path)
    result['procurement_accrual'] = load('Analyze-ProcurementAccrual').analyze(path)
    result['procurement_transactions'] = load('Analyze-ProcurementTransactions').analyze(path)
    result['reward_accounting'] = load('Analyze-RewardAccounting').analyze(path)
    result['battle_allocations'] = load('Analyze-BattleAllocations').analyze(path)
    result['naval_policy'] = load('Analyze-NavalPolicy').analyze(path)
    result['naval_acceptance'] = load('Analyze-NavalAcceptance').report(path)
    result['lifecycle_coverage'] = load('Analyze-LifecycleCoverage').analyze(path)
    result['workshop_profitability'] = load('WorkshopProfitability').assess(
        result.get('2_workshop_payments',{}).get('batch_evidence'),
        result['procurement_accrual'],result['procurement_transactions'])
    result['acceptance_details'] = load('Analyze-AcceptanceDetails').analyze(path)
    result['combined_findings'] = evidence_summary(result)
    result['compact_verdict'] = compact_verdict(result)
    return result


def reload_continuity(boundaries):
    """Compare adjacent exact save/reload boundaries without retaining tick rows."""
    spec = importlib.util.spec_from_file_location(
        'procurement_reload', Path(__file__).with_name('Compare-ProcurementReload.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    results = []
    for before, after in zip(boundaries, boundaries[1:]):
        if abs(before['end'] - after['start']) > 1e-8:
            results.append(dict(status='UNOBSERVED_INTERVAL', before_session=before['session'],
                                after_session=after['session'],
                                limit='No exact save/reload boundary: payload continuity was not assessed.'))
            continue
        try:
            result = module.compare(before['rows'], after['rows'])
            result['boundary_day'] = before['end']
            results.append(result)
        except ValueError as error:
            results.append(dict(status='EVIDENCE_GAP', before_session=before['session'],
                                after_session=after['session'], boundary_day=before['end'],
                                limit='Reload continuity was not certified: ' + str(error)))
    return results


def analyze(path, evaluator=evaluate):
    path = Path(path)
    checkpoint = Path(str(path) + '.state')
    if checkpoint.exists():
        fields = checkpoint.read_text(encoding='utf-8-sig').splitlines()
        if len(fields) != 6 or fields[0] != 'AOC_FRAMEWORK_LOG_V1':
            return dict(status='CHECKPOINT_INVALID', segments=[], problems=['Invalid checkpoint'])
        if fields[5] != 'closed':
            return dict(status='ACTIVE_OR_INTERRUPTED', segments=[], problems=[],
                        note='No integrity verdict. Do not hold the live rotating file open; export/close before full analysis.')
        try:
            length = int(fields[4])
            if length < 0: raise ValueError('Negative checkpoint length')
        except ValueError:
            return dict(status='CHECKPOINT_INVALID', segments=[], problems=['Invalid checkpoint length'])
        if length != path.stat().st_size:
            return dict(status='CHECKPOINT_MISMATCH', segments=[], problems=['Log length differs from checkpoint'])
    initial = path.stat()
    results, problems, gaps, seen, boundaries = [], [], [], set(), []
    current = writer = output = None
    previous_end = None
    campaign = None
    with tempfile.TemporaryDirectory(prefix='aoc-period-analysis-') as directory:
        segment = Path(directory) / 'segment.tsv'
        try:
            with path.open(encoding='utf-8-sig', newline='') as source:
                reader = csv.DictReader(source, delimiter='\t')
                required = {'session', 'sequence', 'kind', 'day', 'owner'}
                if not reader.fieldnames or not required.issubset(reader.fieldnames):
                    raise ValueError('Missing framework log columns')
                for row in reader:
                    if None in row or any(v is None for v in row.values()):
                        raise ValueError('Incomplete row in closed log')
                    day = float(row['day'])
                    if not math.isfinite(day) or day < 0:
                        raise ValueError('Invalid campaign day')
                    if row['kind'] == 'SESSION_START':
                        if current is not None or row['session'] in seen:
                            raise ValueError('Unclosed or repeated session')
                        if campaign is not None and campaign != row['owner']:
                            raise ValueError('Mixed campaigns')
                        campaign = row['owner']
                        if previous_end is not None:
                            if day < previous_end:
                                raise ValueError('Overlapping/reverted campaign intervals')
                            if day > previous_end:
                                gaps.append(dict(from_day=previous_end, to_day=day, days=day-previous_end))
                        current = dict(session=row['session'], start=day, last=day, sequence=0,
                                       boundary_rows=[])
                        seen.add(row['session'])
                        output = segment.open('w', encoding='utf-8', newline='')
                        writer = csv.DictWriter(output, fieldnames=reader.fieldnames, delimiter='\t')
                        writer.writeheader()
                    if current is None or row['session'] != current['session']:
                        raise ValueError('Row outside its capture session')
                    if int(row['sequence']) != current['sequence'] + 1 or day < current['last']:
                        raise ValueError('Sequence gap or backward time within segment')
                    current['sequence'] += 1
                    current['last'] = day
                    writer.writerow(row)
                    if row['kind'] in ('SESSION_START', 'SESSION_END', 'CORE_SYSTEMS', 'PROCUREMENT_LEDGER'):
                        current['boundary_rows'].append(row)
                    if row['kind'] == 'SESSION_END':
                        output.close(); output = None
                        results.append(dict(session=current['session'], start=current['start'], end=day,
                                            evidence=evaluator(str(segment))))
                        boundaries.append(dict(session=current['session'], start=current['start'], end=day,
                                               rows=current['boundary_rows']))
                        previous_end = day
                        current = None
                if current is not None:
                    problems.append('Unfinished last segment; not certified')
                if not results:
                    problems.append('No closed capture segments; no economic evidence')
        except (ValueError, KeyError, csv.Error) as error:
            problems.append(str(error))
        finally:
            if output is not None:
                output.close()
    final = path.stat()
    if (initial.st_size, initial.st_mtime_ns) != (final.st_size, final.st_mtime_ns):
        problems.append('Log changed during analysis; results are not a stable snapshot')
    continuity = reload_continuity(boundaries) if len(boundaries) > 1 else []
    return dict(status='SEGMENT_REVIEW_REQUIRED' if not problems else 'INVALID_OR_INCOMPLETE',
                segments=results, problems=problems, unobserved_intervals=gaps,
                reload_continuity=continuity,
                observed_days=sum(s['end']-s['start'] for s in results),
                limits='Each segment retains its own coverage/verdict. No summed wallet baselines, no inferred events across reload gaps, no balance certification.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2, allow_nan=False))
