"""Joins reward context to canonical wallet deltas, without adding gross scopes."""
import csv
import json
import math
import sys

csv.field_size_limit(24000000)

NAVAL_REWARD_STAGES = {
    'DistributePartyShipsAndRecoverGold',
    'RecoverGoldFromRemainingShipsAfterDistribution',
}


def naval_coverage(calls, gaps):
    """Return compact, non-balancing coverage markers for native naval rewards."""
    naval = [call for call in calls.values() if call['stage'] in NAVAL_REWARD_STAGES and call.get('original_ran') != 'False']
    player = [call for call in naval if call['player_clan'] == 'True']
    valuations = sum(len(call['values']) for call in naval)
    valuation_gaps = {
        'naval_ship_valuation_coverage_incomplete',
        'naval_requested_payout_not_observed',
        'naval_ship_cleanup_missing',
        'naval_ship_owner_retained_requires_review',
    }
    if not naval:
        valuation_status = 'NOT_EXERCISED'
    elif not valuations:
        valuation_status = 'NO_SHIPS_VALUED'
    elif gaps.intersection(valuation_gaps):
        valuation_status = 'INCOMPLETE'
    else:
        valuation_status = 'COMPLETE_FOR_OBSERVED_SCOPES'
    return dict(
        naval_scopes_observed=len(naval),
        naval_original_executions_confirmed=sum(c.get('original_ran') == 'True' for c in naval),
        player_naval_scopes_observed=len(player),
        player_penalty_status=('NOT_EXERCISED' if not player
                               else 'NATIVE_RESULT_OBSERVED_FORMULA_NOT_INDEPENDENTLY_CERTIFIED'),
        valued_ship_count=valuations,
        evaluated_penalty_count=sum(len(call.get('penalty_values', [])) for call in naval),
        ship_valuation_status=valuation_status,
    )


def analyze(path):
    calls, stack, problems, gaps = {}, [], [], set()
    destructions, valuations = {}, {}
    skipped_without_scope = 0
    with open(path, encoding='utf-8-sig', newline='') as source:
        for position, row in enumerate(csv.DictReader(source, delimiter='\t')):
            kind = row['kind']
            if not kind.startswith('REWARD_') and kind not in ('WALLET_CHANGE','SHIP_DESTRUCTION','GOLD_TRANSFER_ENDPOINT'):
                continue
            detail = dict(p.strip().split('=',1) for p in row['detail'].split(';') if '=' in p)
            if kind == 'REWARD_SKIPPED':
                if detail.get('originalRan') != 'False': problems.append('Invalid skipped reward evidence')
                else: skipped_without_scope += 1
                continue
            if kind == 'SHIP_DESTRUCTION':
                ship = detail.get('ship')
                if not ship or ship in destructions or row['metric'] != 'native_action_completed':
                    problems.append('Invalid or duplicate ship destruction'); continue
                if detail.get('ownerRemaining') not in ('True','False'):
                    problems.append('Missing ship destruction owner state'); continue
                destructions[ship] = dict(position=position, owner_remaining=detail['ownerRemaining']=='True')
                continue
            identity = detail.get('reward')
            if kind in ('WALLET_CHANGE','GOLD_TRANSFER_ENDPOINT') and identity in (None, 'none'):
                continue
            before, after = float(row['before']), float(row['after'])
            if not all(math.isfinite(v) for v in (before, after)):
                raise ValueError('Nonfinite reward value')
            if kind == 'REWARD_BEGIN':
                if not identity or identity in calls:
                    problems.append('Missing or duplicate reward identity'); continue
                parent = stack[-1] if stack else 'none'
                if detail.get('parentReward') != parent:
                    problems.append('Reward parent mismatch')
                calls[identity] = dict(stage=row['metric'], wallet=detail.get('wallet'),
                    eligible=detail.get('battlePaymentEligible'), begin=before, end=None,
                    wallet_delta=0, allocation=None, reset=None, ships={}, values={}, parent=parent,
                    player_clan=detail.get('playerClan'), requested_payments={}, original_ran=None,
                    penalty_values=[],
                    recovery_eligibility=detail.get('recoveryEligibility'))
                stack.append(identity)
                continue
            if identity not in calls or not stack or stack[-1] != identity:
                problems.append('Observation outside active reward'); continue
            call = calls[identity]
            if kind == 'GOLD_TRANSFER_ENDPOINT':
                # Only the recipient endpoint in this exact reward; never sum
                # both gross endpoints or propagate requests to parent scopes.
                if row['owner'] != call['wallet'] or call['stage'] != 'RecoverGoldFromRemainingShipsAfterDistribution':
                    continue
                transfer = detail.get('transfer')
                if (not transfer or transfer in call['requested_payments']
                        or detail.get('giverPresent') != 'False' or detail.get('recipientPresent') != 'True'):
                    problems.append('Invalid naval payout request endpoint'); continue
                requested = float(detail['requestedTransfer'])
                if not math.isfinite(requested) or requested < 0 or requested != int(requested):
                    problems.append('Invalid naval requested gold'); continue
                call['requested_payments'][transfer] = requested
            elif kind == 'WALLET_CHANGE':
                # Propagate only to scopes observing this same canonical wallet.
                # These are alternative context checks, never additive income.
                for ancestor in stack:
                    if calls[ancestor]['wallet'] == row['owner']:
                        calls[ancestor]['wallet_delta'] += after-before
            elif kind == 'REWARD_INPUT':
                key = {'allocated_gold': 'allocation', 'remaining_allocations': 'reset'}.get(row['metric'])
                if key is None:
                    problems.append('Unknown battle input metric'); continue
                if call[key] is not None: problems.append('Repeated battle input')
                call[key] = (before, after)
            elif kind == 'REWARD_PENALTY':
                if after < 0 or detail.get('originalRan') not in ('True', 'False'):
                    problems.append('Invalid evaluated ship penalty')
                call['penalty_values'].append(dict(value=after, original_ran=detail.get('originalRan')))
            elif kind == 'REWARD_VALUATION':
                ship = detail.get('ship')
                if not ship or ship in call['values']:
                    problems.append('Duplicate or missing valued ship'); continue
                if after < 0: problems.append('Negative ship valuation')
                call['values'][ship] = after
                if ship in valuations: problems.append('Ship valued in multiple reward calls: '+ship)
                valuations[ship] = position
            elif kind == 'REWARD_SHIP':
                ship = detail.get('ship')
                if not ship:
                    problems.append('Missing observed ship identity'); continue
                if row['metric'] not in ('before', 'after_membership'):
                    problems.append('Unknown ship observation metric'); continue
                entry = call['ships'].setdefault(ship, {})
                if row['metric'] in entry: problems.append('Duplicate ship observation')
                entry[row['metric']] = dict(before=before, after=after, owner=detail.get('ownerParty'),
                    owner_identity=detail.get('ownerIdentity'), owner_contains_ship=detail.get('ownerContainsShip'))
            elif kind == 'REWARD_END':
                if row['metric'] != call['stage'] or before != call['begin']:
                    problems.append('Reward end context mismatch')
                call['end'] = after
                call['original_ran'] = detail.get('originalRan')
                if call['original_ran'] not in (None, 'True', 'False'):
                    problems.append('Invalid original-run evidence')
                stack.pop()
            else:
                problems.append('Unknown reward record kind')
    if stack: problems.append('Unclosed reward scope')
    for identity, call in calls.items():
        if call['end'] is None: continue
        delta = call['end']-call['begin']
        call['wallet_residual'] = delta-call['wallet_delta']
        if call['wallet'] in (None, 'none'):
            gaps.add('reward_without_canonical_wallet')
        elif call['wallet_residual'] != 0:
            problems.append('Reward wallet residual: '+identity)
        if call['original_ran'] == 'False':
            call['execution_status'] = 'SKIPPED_NOT_A_NATIVE_REWARD'
            if delta or call['values'] or call['requested_payments']:
                gaps.add('skipped_reward_has_patch_side_effects')
            continue
        if call['original_ran'] is None: gaps.add('reward_original_execution_unrecorded')
        if call['stage'] == 'battle_gold':
            if call['allocation'] is None or call['reset'] is None or call['eligible'] not in ('True','False'):
                gaps.add('battle_allocation_or_eligibility_missing'); continue
            loss, gain = call['allocation']
            expected = max(0,gain)-max(0,loss) if call['eligible']=='True' else 0
            call['allocation_difference_requires_clamp_review'] = delta-expected
            if call['reset'] != (0,0): problems.append('Battle allocations not reset: '+identity)
            if delta != expected: gaps.add('battle_allocation_cash_difference_requires_clamp_review')
        else:
            gaps.add('naval_player_penalty_not_certified')
            if call['stage']=='RecoverGoldFromRemainingShipsAfterDistribution':
                if call['player_clan'] == 'True' and call['values'] and not call['penalty_values']:
                    gaps.add('player_penalty_evaluated_value_missing')
                if call['requested_payments']:
                    requested = sum(call['requested_payments'].values())
                    call['requested_gold_after_native_penalty'] = requested
                    call['requested_payout_cash_difference'] = delta-requested
                    if delta != requested: gaps.add('naval_requested_payout_cash_difference')
                elif call['values']:
                    gaps.add('naval_requested_payout_not_observed')
                before_ships={s for s,e in call['ships'].items() if 'before' in e}
                eligibility = call['recovery_eligibility']
                early_return = eligibility in ('no_clan', 'bandit_clan', 'no_leader', 'inactive_leader', 'no_ships')
                if early_return:
                    call['execution_status'] = 'NATIVE_INELIGIBLE_AT_ENTRY'
                    if call['values'] or call['requested_payments'] or delta:
                        gaps.add('ineligible_reward_has_observed_effects')
                elif before_ships != set(call['values']):
                    gaps.add('naval_ship_valuation_coverage_incomplete')
                call['valued_ship_cleanup'] = {}
                for ship in call['values']:
                    event = destructions.get(ship)
                    if event is None:
                        status='destruction_not_observed'; gaps.add('naval_ship_cleanup_missing')
                    elif event['position'] <= valuations[ship]:
                        status='destruction_precedes_valuation'; problems.append('Ship valued after recorded destruction: '+ship)
                    elif event['owner_remaining']:
                        status='destruction_completed_owner_retained'; gaps.add('naval_ship_owner_retained_requires_review')
                    else: status='later_native_destruction_completed_owner_cleared'
                    call['valued_ship_cleanup'][ship] = status
    if not calls: gaps.add('reward_events_not_exercised')
    return dict(rewards=calls, problems=problems, coverage_gaps=sorted(gaps),
                skipped_without_scope=skipped_without_scope,
                naval_coverage=naval_coverage(calls, gaps),
                limits='Gross nested rewards are not additive cash flow. Allocation differences are not automatic inflation; naval removal is not destruction proof. No balance certification.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2, allow_nan=False))
