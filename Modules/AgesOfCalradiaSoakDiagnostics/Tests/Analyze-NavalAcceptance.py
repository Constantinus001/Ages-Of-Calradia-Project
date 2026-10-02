"""Read-only, operation-identity joins. No pricing changes or game control."""
import collections
import csv
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

csv.field_size_limit(24000000)


def single(n):
    return struct.unpack('f', struct.pack('f', n))[0]


def fields(row):
    return dict(p.strip().split('=', 1) for p in (row.get('detail') or '').split(';') if '=' in p)


def analyze(path):
    sales, rewards, allocations, battle_quotes = {}, {}, {}, collections.defaultdict(list)
    destruct, ownership, pending = {}, set(), {}
    problems, gaps = [], set()
    counts = collections.Counter()
    statuses, sessions, revisions = [], set(), set()
    closed, stop_reason, cost = False, None, None
    first_day = last_day = None
    comparisons = {str(f): collections.Counter() for f in (.01, .03, .1, .3, 1.)}
    def problem(message):
        if len(problems) < 100:
            problems.append(message)
    def lines(source):
        for line in source:
            if not line.endswith('\n'):
                gaps.add('unfinished_tail_not_evaluated')
                break
            yield line
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for pos, row in enumerate(csv.DictReader(lines(stream), delimiter='\t')):
            kind, metric, d = row.get('kind'), row.get('metric'), fields(row)
            if closed:
                problem('Rows after SESSION_END')
            if row.get('session'):
                sessions.add(row['session'])
                if len(sessions) > 1:
                    raise ValueError('Separate sessions before naval acceptance analysis')
            if kind == 'SESSION_START':
                first_day = float(row.get('day', 0))
            if kind == 'SESSION_END':
                closed, stop_reason = True, metric
                last_day = float(row.get('day', 0))
                if int(d.get('discardedUncommittedRows', 0)):
                    problem('Capture discarded records')
            if kind == 'DIAGNOSTIC_COST':
                cost = dict(writer_seconds_lower_bound=float(row['after']), discarded_records=int(d['discardedRows']),
                            pending_records=int(d['pendingRows']), bytes=int(d['bytes']) if 'bytes' in d else None,
                            wall_seconds=float(d['wallSeconds']) if 'wallSeconds' in d else None,
                            limit='Writer cost excludes observer snapshots, stack traces and status IO; not total diagnostic overhead.')
                if not math.isfinite(cost['writer_seconds_lower_bound']) or cost['writer_seconds_lower_bound'] < 0:
                    problem('Invalid diagnostic cost')
                if cost['discarded_records']:
                    problem('Diagnostic records dropped')
            if kind == 'NAVAL_POLICY_STATUS':
                statuses.append(row.get('detail', ''))
            if kind == 'NAVAL_SALE_BEGIN':
                key = d.get('sale')
                if not key or key in sales:
                    problem('Duplicate/missing sale identity')
                else:
                    sales[key] = dict(start=d, before=float(row['before']), end=None, quotes=[], cash=0., mutations=0, pos=pos)
            if kind == 'REWARD_BEGIN':
                key = d.get('reward')
                if not key or key in rewards:
                    problem('Duplicate/missing reward identity')
                else:
                    a = pending.pop((d.get('mapEvent'), d.get('mapParty')), None) if metric == 'battle_gold' else None
                    rewards[key] = dict(start=d, before=float(row['before']), stage=metric, end=None,
                                        quotes=[], cash=0., mutations=0, cash_activity=0., allocation=a, inputs=None, reset=None, pos=pos)
            if kind == 'NAVAL_POLICY_DECISION':
                revisions.add(d.get('revision'))
                if metric == 'allocation':
                    key = d.get('allocation')
                    if not key or key == 'none':
                        gaps.add('allocation_identity_missing')
                    else:
                        party = (d.get('mapEvent'), d.get('mapParty'))
                        entry = dict(detail=d, pos=pos)
                        if party in pending:
                            problem('Policy allocation overwritten before commit')
                        pending[party] = entry
                        allocations.setdefault(key, []).append(entry)
                        if d.get('playerClan') == 'True' and (d.get('applied') != 'False' or d['effectiveTotal'] != d['nativeTotal']):
                            problem('Player allocation changed by policy')
                elif metric == 'quote':
                    q = dict(detail=d, pos=pos)
                    route = d.get('route')
                    if route == 'sale' and d.get('navalSale') in sales:
                        sales[d['navalSale']]['quotes'].append(q)
                    elif route == 'recovery' and d.get('reward') in rewards:
                        rewards[d['reward']]['quotes'].append(q)
                    elif route == 'battle' and d.get('allocation') not in (None, 'none'):
                        battle_quotes[d['allocation']].append(q)
                    else:
                        counts['preview_or_unjoined_quotes'] += 1
                elif metric == 'rejected':
                    gaps.add('policy_quote_rejected')
            if kind == 'WALLET_CHANGE':
                for call in (sales.get(d.get('navalSale')), rewards.get(d.get('reward'))):
                    if call is not None and row.get('owner') == call['start'].get('wallet'):
                        delta = float(row['after']) - float(row['before'])
                        if not math.isfinite(delta):
                            problem('Nonfinite wallet mutation')
                        call['cash'] += delta
                        call['mutations'] += 1
                        if 'cash_activity' in call:
                            call['cash_activity'] += abs(delta)
            if kind == 'REWARD_INPUT' and metric == 'allocated_gold' and d.get('reward') in rewards:
                rewards[d['reward']]['inputs'] = (int(float(row['before'])), int(float(row['after'])))
            if kind == 'REWARD_INPUT' and metric == 'remaining_allocations' and d.get('reward') in rewards:
                rewards[d['reward']]['reset'] = (float(row['before']), float(row['after']))
            if kind in ('NAVAL_SALE_END', 'REWARD_END'):
                table, key = (sales, d.get('sale')) if kind == 'NAVAL_SALE_END' else (rewards, d.get('reward'))
                if key in table:
                    if table[key]['end'] is not None:
                        problem('Duplicate operation end')
                    table[key]['end'] = dict(detail=d, after=float(row['after']), pos=pos)
            if kind == 'SHIP_DESTRUCTION' and metric == 'native_action_completed' and d.get('ownerRemaining') == 'False':
                destruct[d.get('ship')] = pos
            if kind == 'SHIP_OWNER_CHANGE' and d.get('originalRan') == 'True':
                ownership.add((d.get('navalSale'), d.get('ship'), d.get('to')))

    def completed(call, label):
        if call['end'] is None:
            (problem if closed else gaps.add)(label + '_operation_unfinished')
            return False
        end = call['end']['detail']
        if end.get('error', 'none') != 'none':
            problem(label + '_native_exception_partial_effects_require_review')
            return False
        if end.get('originalRan') != 'True':
            gaps.add(label + '_original_not_executed')
            return False
        return True

    def payment(call, expected, label):
        if not completed(call, label):
            return False
        actual = call['end']['after'] - call['before']
        if not math.isfinite(expected) or actual != expected or call['cash'] != actual:
            problem(label + '_policy_wallet_reconciliation_mismatch')
            return False
        if actual != 0 and not call['mutations']:
            gaps.add(label + '_canonical_wallet_receipt_missing')
            return False
        return True

    def cleanup(quotes, label):
        ok = True
        if len({q['detail'].get('ship') for q in quotes}) != len(quotes):
            problem(label + '_duplicate_ship_valuation')
            ok = False
        for q in quotes:
            if destruct.get(q['detail'].get('ship'), -1) <= q['pos']:
                gaps.add(label + '_ship_cleanup_unverified')
                ok = False
        return ok

    def compare(quotes, route):
        # Price components only, not a simulated future treasury or realized sale
        # price. Native sale perks/repair cannot be inferred from a base alone.
        for q in quotes:
            d = q['detail']
            try:
                native, hull = float(d['nativeBase']), float(d['hull'])
                if not math.isfinite(native) or not math.isfinite(hull) or native < hull or hull < 0:
                    raise ValueError('invalid components')
                for f, totals in comparisons.items():
                    totals[route + '_base_sum'] += native if d.get('alreadyDiscounted') == 'True' else single(single(native-hull) + single(hull*float(f)))
                    totals[route + '_quotes'] += 1
            except (KeyError, ValueError, OverflowError):
                gaps.add('counterfactual_components_unavailable')

    for sale in sales.values():
        if not sale['quotes']:
            continue  # Player/native-only operation is not policy acceptance.
        if len(sale['quotes']) != 1:
            problem('sale_requires_one_evaluated_policy_quote')
            continue
        q = sale['quotes'][0]
        amount = float(q['detail']['returnedQuote'])
        if not math.isfinite(amount) or amount < 0:
            problem('Invalid sale quote')
            continue
        if payment(sale, int(amount), 'sale'):
            d = sale['start']
            destination = sale['end']['detail'].get('owner')
            if sale['end']['detail'].get('transferred') != 'True' or not any(s == d['sale'] and ship == d['ship'] and to == destination for s, ship, to in ownership):
                gaps.add('sale_ship_transfer_unverified')
            else:
                counts['sale_reconciled'] += 1
                compare(sale['quotes'], 'sale')
    for reward in rewards.values():
        if reward['stage'] == 'battle_gold' and reward['allocation'] is not None:
            d = reward['allocation']['detail']
            if not completed(reward, 'battle'):
                continue
            inputs = reward['inputs']
            if inputs is None or inputs[1] != int(d['effectiveTotal']):
                problem('battle_policy_to_commit_mismatch')
                continue
            eligible = reward['start'].get('battlePaymentEligible')
            if eligible not in ('True', 'False'):
                gaps.add('battle_payment_eligibility_missing')
                continue
            if reward['reset'] is None:
                gaps.add('battle_allocation_reset_missing')
                continue
            if reward['reset'] != (0, 0):
                problem('battle_allocations_not_reset')
                continue
            if reward['start'].get('wallet') in (None, 'none'):
                gaps.add('battle_canonical_wallet_identity_missing')
                continue
            # Native CommitGoldChanges pays a hero, or a trade-active mobile
            # party. Other parties only clear their allocations. Policy
            # eligibility is a different question and cannot replace this flag.
            if eligible == 'False':
                if reward['cash_activity'] != 0:
                    problem('battle_ineligible_wallet_activity')
                elif payment(reward, 0, 'battle'):
                    counts['battle_ineligible_zero_effect_reconciled'] += 1
                    d['outcomeJoined'] = True
                continue
            # Negative inputs have different hero/trade semantics; do not
            # guess a native clamp or call a speculative expected value a pass.
            if min(inputs) < 0:
                gaps.add('battle_negative_allocation_requires_native_review')
                continue
            if payment(reward, inputs[1]-inputs[0], 'battle'):
                counts['battle_reconciled'] += 1
                d['paymentJoined'] = True
                d['outcomeJoined'] = True
        elif reward['stage'] == 'RecoverGoldFromRemainingShipsAfterDistribution' and reward['quotes']:
            total = 0.
            for q in reward['quotes']:
                total = single(total + float(q['detail']['returnedQuote']))
            if math.isfinite(total) and total >= 0 and payment(reward, int(total), 'recovery') and cleanup(reward['quotes'], 'recovery'):
                counts['recovery_reconciled'] += 1
                compare(reward['quotes'], 'recovery')
    for key, entries in allocations.items():
        qs = battle_quotes.get(key, [])
        if not qs:
            gaps.add('battle_policy_ship_quotes_missing')
        else:
            cleanup(qs, 'battle')
            try:
                expected_pool = sum(int(single(float(q['detail']['effectiveBase'])*single(1.5))) for q in qs)
                for entry in entries:
                    d = entry['detail']
                    if d.get('applied') == 'True' and int(d['effectivePool']) != expected_pool:
                        problem('battle_ship_quotes_to_pool_mismatch')
            except (KeyError, ValueError, OverflowError):
                gaps.add('battle_shadow_pool_components_missing')
            if all(e['detail'].get('outcomeJoined') for e in entries):
                compare(qs, 'battle')
        if (any(e['detail'].get('playerClan') == 'True' and e['detail'].get('paymentJoined') for e in entries)
                and any(e['detail'].get('applied') == 'True' and e['detail'].get('paymentJoined') for e in entries)):
            counts['mixed_player_battles_reconciled'] += 1
    if pending:
        gaps.add('policy_allocations_without_commit')
    for route in ('battle', 'sale', 'recovery'):
        if not counts[route+'_reconciled']:
            gaps.add(route + '_payment_not_reconciled')
    if not counts['mixed_player_battles_reconciled']:
        gaps.add('mixed_player_battle_not_reconciled')
    if not statuses or not all('enabled;' in status for status in statuses):
        gaps.add('policy_not_continuously_enabled')
    if not closed:
        gaps.add('capture_still_open_or_aborted')
    if cost is None:
        gaps.add('diagnostic_cost_unmeasured')
    if len(revisions) > 1 or None in revisions:
        problem('Policy revision changed or missing')
    if cost and cost['wall_seconds'] is not None and cost['wall_seconds'] > 0:
        cost['writer_wall_fraction_lower_bound'] = cost['writer_seconds_lower_bound'] / cost['wall_seconds']
    return dict(status='ISSUES' if problems else 'COVERAGE_GAPS' if gaps else 'OBSERVED_ROUTES_RECONCILED_NOT_BALANCE_CERTIFICATION',
                closed=closed, stop_reason=stop_reason, campaign_days=None if first_day is None or last_day is None else last_day-first_day,
                counts=dict(counts), problems=problems, coverage_gaps=sorted(gaps), logging=cost,
                tuning_comparison=dict(basis_scenarios={k: dict(v) for k, v in comparisons.items()},
                    settings_changed=False, limit='Observed executed-operation base components only; not wallet savings, fleet demand or future clan balance.'),
                limit='Actual canonical wallet changes reconciled, not summed with gross nested scopes. Campaign behavior and tuning still require review.')


def report(path):
    result = analyze(path)
    spec = importlib.util.spec_from_file_location('policy', Path(__file__).with_name('Analyze-NavalPolicy.py'))
    policy = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(policy)
    result['arithmetic'] = policy.analyze(path)
    result['problems'] += ['arithmetic: '+p for p in result['arithmetic']['problems']]
    result['coverage_gaps'] = sorted(set(result['coverage_gaps']) | {
        'arithmetic: '+g for g in result['arithmetic']['coverage_gaps']
        if g != 'payment_lifecycle_and_player_isolation_require_companion_evidence'})
    if result['problems']:
        result['status'] = 'ISSUES'
    elif result['coverage_gaps']:
        result['status'] = 'COVERAGE_GAPS'
    return result


def markdown(result):
    lines = ['# Naval acceptance', '', '**'+result['status']+'**', '',
             'Campaign days: '+str(result['campaign_days'])+'; stop reason: '+str(result['stop_reason']), '',
             '| Check | Observed reconciled operations |', '| --- | ---: |']
    for label, key in [('Battle payments', 'battle_reconciled'),
                       ('Ineligible battle commits (no payment)', 'battle_ineligible_zero_effect_reconciled'), ('Sales and transfers', 'sale_reconciled'),
                       ('Recovery and cleanup', 'recovery_reconciled'), ('Mixed-player battles', 'mixed_player_battles_reconciled')]:
        lines.append('| '+label+' | '+str(result['counts'].get(key, 0))+' |')
    lines += ['', '## Issues', ''] + (['- '+p for p in result['problems']] or ['None detected in the observed receipts.'])
    lines += ['', '## Missing coverage', ''] + (['- '+g for g in result['coverage_gaps']] or ['None for these receipt checks.'])
    lines += ['', '## Logging cost', '', json.dumps(result['logging'], indent=2), '',
              '## Tuning preparation', '', 'Component comparisons: '+', '.join(result['tuning_comparison']['basis_scenarios'])+'. Settings unchanged.',
              '', result['tuning_comparison']['limit'], '', result['limit']]
    return '\n'.join(lines)


if __name__ == '__main__':
    result = report(sys.argv[1])
    print(markdown(result) if '--markdown' in sys.argv[2:] else json.dumps(result, indent=2))
