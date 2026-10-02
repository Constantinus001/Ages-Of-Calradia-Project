"""Observed branches are coverage, not evidence of balance or a demand to rerun."""
import collections
import csv
import json
import sys

csv.field_size_limit(24000000)


def analyze(path):
    features, counts, problems = {}, collections.Counter(), []
    with open(path,encoding='utf-8-sig',newline='') as source:
        for row in csv.DictReader(source,delimiter='\t'):
            kind, metric = row['kind'], row['metric']
            if kind == 'FEATURE_COVERAGE':
                feature = row['owner']
                if metric not in ('present','absent'):
                    problems.append('Invalid feature availability: '+feature); continue
                if feature in features and features[feature]!=metric:
                    problems.append('Feature availability changed within segment: '+feature)
                features[feature]=metric
            if kind == 'PROCUREMENT_MOVEMENT': counts['procurement/'+metric]+=1
            if kind == 'PROCUREMENT_TRANSFER' and metric in ('rolled_back','failed'):
                counts['procurement/'+metric]+=1
            if kind == 'REWARD_END':
                detail = dict(p.strip().split('=',1) for p in (row.get('detail') or '').split(';') if '=' in p)
                if detail.get('originalRan') == 'True': counts['reward/'+metric]+=1
                elif detail.get('originalRan') == 'False': counts['skipped_reward/'+metric]+=1
                else: counts['unverified_reward/'+metric]+=1
            if kind == 'SHIP_DESTRUCTION': counts['naval_rewards/destruction']+=1
    specifications = {
        'procurement':['procurement/'+stage for stage in ('dispatch','arrival','consumption','return','liquidation','rolled_back','failed')],
        'naval_rewards':['reward/RecoverGoldFromRemainingShipsAfterDistribution','naval_rewards/destruction'],
        'battle_rewards':['reward/battle_gold']}
    result, gaps = {}, []
    for feature, paths in specifications.items():
        availability = 'present' if feature=='battle_rewards' else features.get(feature,'unknown')
        observed = {p:counts[p] for p in paths if counts[p]}
        missing = [p for p in paths if not counts[p]]
        if availability=='absent' and observed: problems.append('Events recorded for absent feature: '+feature)
        if availability=='unknown': gaps.append(feature+':availability_unknown')
        if availability!='absent': gaps.extend(missing)
        result[feature]=dict(availability=availability,observed=observed,
            not_exercised=missing if availability!='absent' else [],
            not_applicable=paths if availability=='absent' else [])
    return dict(features=result,problems=problems,coverage_gaps=gaps,
                limits='Counters do not validate receipts. Missing rare branches, including failure/rollback, are explicit limitations; do not automatically rerun or induce failures in a user save.')


if __name__=='__main__': print(json.dumps(analyze(sys.argv[1]),indent=2))
