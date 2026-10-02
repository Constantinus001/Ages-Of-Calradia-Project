"""Join legacy daily HERO_GOLD flows to stable clan-leader identities.

Summary rows preserve caller labels, not full transaction ancestry. Snapshot-day
boundaries are coarse; edge-day gross movement bounds are reported, never hidden.
"""
import collections
import csv
import json
import math
import sys

csv.field_size_limit(24000000)


def analyze(path):
    clans, flows = {}, collections.defaultdict(lambda: [0, 0, 0, 0])
    edge = {}
    first_day, last_day = None, None
    starts = ends = rows = 0
    for_csv = open(path, encoding='utf-8-sig', newline='')
    with for_csv as source:
        for row in csv.DictReader(source, delimiter='\t'):
            rows += 1
            kind = row['kind']
            if ends or (not starts and kind != 'SESSION_START'):
                raise ValueError('Records outside the closed session boundary')
            if kind == 'SESSION_START': starts += 1
            if kind == 'SESSION_END': ends += 1
            if starts > 1:
                raise ValueError('Multiple legacy sessions')
            if kind not in ('HERO_GOLD', 'CLAN_BALANCE'):
                continue
            day = int(row['day'])
            if day < 0 or not row['owner']:
                raise ValueError('Invalid day or missing owner')
            if first_day is None or day < first_day: first_day = day
            if last_day is None or day > last_day: last_day = day
            if kind == 'CLAN_BALANCE':
                detail = dict(p.strip().split('=', 1) for p in row['lastDetail'].split(';') if '=' in p)
                leader = detail.get('leader')
                value = float(row['last'])
                if not leader or not math.isfinite(value):
                    raise ValueError('Missing leader identity or invalid balance')
                record = clans.setdefault(row['owner'], dict(first_day=day, last_day=day,
                    first=value, last=value, leaders=set()))
                record['leaders'].add(leader)
                if day < record['first_day']: record['first_day'], record['first'] = day, value
                if day >= record['last_day']: record['last_day'], record['last'] = day, value
                continue
            positive, negative, net = (float(row[k]) for k in ('positive', 'negative', 'net'))
            count = int(row['count'])
            if count <= 0 or not row['source']:
                raise ValueError('Invalid aggregate count or missing caller label')
            if not all(math.isfinite(v) for v in (positive, negative, net)) or positive < 0 or negative > 0 or abs(positive+negative-net) > 1e-6:
                raise ValueError('Invalid gold aggregate')
            key = (row['owner'], row['source'])
            values = flows[key]
            for i, value in enumerate((count, positive, negative, net)): values[i] += value
            # Keep earliest/latest flow-day totals per hero, including gross movement.
            hero_edges = edge.setdefault(row['owner'], {})
            for side, compare in (('first', lambda a, b: a < b), ('last', lambda a, b: a > b)):
                entry = hero_edges.get(side)
                if entry is None or compare(day, entry['day']):
                    hero_edges[side] = entry = dict(day=day, net=0, gross=0)
                if entry['day'] == day:
                    entry['net'] += net
                    entry['gross'] += positive-negative
    if starts != 1 or ends != 1:
        raise ValueError('Require exactly one closed legacy session')
    eligible = {k:v for k,v in clans.items() if len(v['leaders']) == 1
                and v['first_day'] == first_day and v['last_day'] == last_day}
    if not eligible or first_day == last_day:
        raise ValueError('No full-window stable observed cohort spanning multiple days')
    owners = [next(iter(v['leaders'])) for v in eligible.values()]
    if len(set(owners)) != len(owners):
        raise ValueError('One leader belongs to multiple cohort clans')
    owners = set(owners)
    labels = collections.defaultdict(lambda: dict(count=0, credit=0, debit=0, net=0))
    for (owner, label), values in flows.items():
        if owner not in owners: continue
        for key, value in zip(('count', 'credit', 'debit', 'net'), values): labels[label][key] += value
    start = sum(v['first'] for v in eligible.values())
    end = sum(v['last'] for v in eligible.values())
    total = sum(v['net'] for v in labels.values())
    boundary_gross = 0
    for owner in owners:
        seen = set()
        for entry in edge.get(owner, {}).values():
            if entry['day'] in (first_day, last_day) and entry['day'] not in seen:
                seen.add(entry['day']); boundary_gross += entry['gross']
    return dict(rows=rows, day_range=[first_day, last_day], stable_cohort=len(eligible),
                excluded_clans=sorted(set(clans)-set(eligible)), start=start, end=end,
                snapshot_increase=end-start, observed_setter_net=total,
                endpoint_minus_summary_flows=end-start-total,
                edge_day_gross_ambiguity_bound=boundary_gross,
                sources=dict(sorted(labels.items(), key=lambda p: -abs(p[1]['net']))),
                limit='Recorded caller labels are not complete economic purposes. Edge-day snapshot timing and summary aggregation prohibit exact transaction ancestry. Never add mirrored party wallets or CLAN_SETTLEMENT to these HERO_GOLD flows.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2, allow_nan=False))
