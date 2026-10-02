"""Attribute pre-wallet battle allocations without treating valuations as cash."""
import csv
import json
import math
import sys

csv.field_size_limit(24000000)


def valid_identity(value):
    # Observer-assigned event/party identities are positive ASCII integers.
    return bool(value and value.isascii() and value.isdecimal() and int(value) > 0)


def analyze(path):
    calls, latest, commits = {}, {}, {}
    problems, gaps = [], set()
    closed = False
    joined = unjoined = values = penalties = skipped_without_scope = 0
    stage_totals = {}
    sessions = set()
    def complete_lines(source):
        for line in source:
            if not line.endswith('\n'):
                gaps.add('unfinished_last_line_not_evaluated')
                break
            yield line
    with open(path, encoding='utf-8-sig', newline='') as source:
        for row in csv.DictReader(complete_lines(source), delimiter='\t'):
            if row.get('session'):
                sessions.add(row['session'])
                if len(sessions) > 1:
                    raise ValueError('Separate sessions before battle allocation analysis')
            kind = row.get('kind', '')
            if kind == 'SESSION_END':
                closed = True
            if not kind.startswith('BATTLE_ALLOCATION_') and kind not in ('REWARD_BEGIN', 'REWARD_INPUT', 'REWARD_END'):
                continue
            detail = dict(p.strip().split('=', 1) for p in row.get('detail', '').split(';') if '=' in p)
            before, after = float(row['before']), float(row['after'])
            if not all(math.isfinite(v) for v in (before, after)):
                raise ValueError('Nonfinite battle allocation evidence')
            if kind == 'BATTLE_ALLOCATION_SKIPPED':
                if detail.get('originalRan') != 'False':
                    problems.append('Invalid skipped allocation evidence')
                else:
                    skipped_without_scope += 1
                continue
            if kind == 'REWARD_BEGIN':
                if row['metric'] == 'battle_gold':
                    key = (detail.get('mapEvent'), detail.get('mapParty'))
                    if not all(valid_identity(value) for value in key):
                        key = None
                    commits[detail.get('reward')] = dict(key=key, expected=latest.get(key), actual=None)
                continue
            if kind == 'REWARD_INPUT':
                commit = commits.get(detail.get('reward'))
                if commit is not None and row['metric'] == 'allocated_gold':
                    commit['actual'] = (before, after)
                elif commit is not None and row['metric'] == 'remaining_allocations':
                    # A reset may update observed evidence, never manufacture it.
                    if commit['key'] in latest:
                        latest[commit['key']] = (before, after)
                continue
            if kind == 'REWARD_END':
                commit = commits.get(detail.get('reward'))
                if commit is not None and detail.get('originalRan') == 'True':
                    if commit['expected'] is None or commit['actual'] is None:
                        unjoined += 1
                        gaps.add('battle_commit_without_complete_allocation_observation')
                    elif commit['expected'] != commit['actual']:
                        problems.append('Allocation-to-commit mismatch: ' + str(commit['key']))
                    else:
                        joined += 1
                continue
            identity = detail.get('allocation')
            if kind == 'BATTLE_ALLOCATION_BEGIN':
                if not identity or identity in calls or not valid_identity(detail.get('mapEvent')):
                    problems.append('Missing or duplicate allocation identity')
                    continue
                calls[identity] = dict(event=detail['mapEvent'], stage=detail.get('stage'),
                                       before={}, after={}, ships={}, values=[], penalties=[], ended=False, ran=None)
                continue
            call = calls.get(identity)
            if call is None or call['ended']:
                problems.append('Allocation observation outside open scope')
                continue
            if detail.get('mapEvent') != call['event'] or detail.get('stage') != call['stage']:
                problems.append('Allocation scope identity changed')
                continue
            if kind == 'BATTLE_ALLOCATION_PARTY':
                phase, party = row['metric'], detail.get('mapParty')
                if phase not in ('before', 'after') or not valid_identity(party) or party in call[phase]:
                    problems.append('Invalid or duplicate party allocation snapshot')
                elif before < 0 or after < 0 or int(before) != before or int(after) != after:
                    problems.append('Invalid allocated gold amount')
                else:
                    call[phase][party] = (before, after)
            elif kind == 'BATTLE_ALLOCATION_VALUE':
                if not detail.get('ship') or after < 0:
                    problems.append('Invalid evaluated ship value')
                else:
                    call['values'].append(dict(ship=detail['ship'], value=after))
                    values += 1
            elif kind == 'BATTLE_ALLOCATION_PENALTY':
                call['penalties'].append(after)
                penalties += 1
            elif kind == 'BATTLE_ALLOCATION_SHIP':
                phase, ship = row['metric'], detail.get('ship')
                if not ship or phase not in ('before', 'after'):
                    problems.append('Invalid allocation ship snapshot')
                else:
                    call['ships'].setdefault(ship, {})[phase] = detail.get('ownerIdentity')
            elif kind == 'BATTLE_ALLOCATION_END':
                call['ended'], call['ran'] = True, detail.get('originalRan')
                if call['before'].keys() != call['after'].keys():
                    problems.append('Incomplete allocation party snapshots')
                elif call['ran'] == 'True':
                    totals = stage_totals.setdefault(call['stage'], dict(scopes=0, allocated_loss_delta=0, allocated_gain_delta=0))
                    totals['scopes'] += 1
                    for party, end in call['after'].items():
                        start = call['before'][party]
                        totals['allocated_loss_delta'] += end[0] - start[0]
                        totals['allocated_gain_delta'] += end[1] - start[1]
                        latest[(call['event'], party)] = end
                elif call['ran'] != 'False':
                    gaps.add('allocation_execution_unconfirmed')
            else:
                problems.append('Unknown allocation record: ' + kind)
    opened = sum(not c['ended'] for c in calls.values())
    if opened:
        if closed:
            problems.append('Unclosed allocation scopes')
        else:
            gaps.add('allocation_scope_still_open')
    if not calls:
        gaps.add('battle_allocation_not_exercised_or_older_capture')
    if not values:
        gaps.add('ship_trade_values_not_exercised')
    if not penalties:
        gaps.add('ship_selling_penalty_not_exercised')
    return dict(closed=closed, allocation_scopes=len(calls), open_scopes=opened, skipped_without_scope=skipped_without_scope, joined_battle_commits=joined,
                unjoined_battle_commits=unjoined, evaluated_ship_values=values, evaluated_penalties=penalties,
                allocation_deltas_by_stage=stage_totals, problems=problems, coverage_gaps=sorted(gaps),
                limit='Allocations and valuations are not additive wallet income. Model formulas and full ship lifecycle require separate evidence.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
