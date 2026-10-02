"""Read-only drilldown of one capture; never reconstructs missing transactions."""
import collections
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import json
import sys


def fields(text):
    return dict(part.strip().split('=', 1) for part in text.split(';') if '=' in part)


def observed_caller(chain):
    # Harmony-generated frames may have a leading dot. Dispatch infrastructure
    # is not an economic cause; keep the original chain if no domain frame exists.
    for frame in chain.split('|'):
        name = frame.strip().lstrip('.')
        if (name.startswith(('TaleWorlds.', 'NavalDLC.'))
                and not name.startswith('TaleWorlds.CampaignSystem.MbEvent')
                and '.Hero.ChangeHeroGold' not in name
                and '.Actions.GiveGoldAction.' not in name
                and 'set_Gold' not in name and 'set_PartyTradeGold' not in name):
            return name
    return 'unattributed_chain:' + chain


def analyze(path):
    routes = {}
    charas = collections.Counter()
    sheep = collections.defaultdict(collections.Counter)
    hero_sources = collections.defaultdict(lambda: [0, 0])
    residuals = {}
    samples = []
    schema = None
    closed = False
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            kind, owner = row['kind'], row['owner']
            d = fields(row['detail'])
            decision = d.get('decision')
            if kind == 'SESSION_START': schema = row['metric']
            if kind == 'SESSION_END': closed = True
            if kind == 'CARAVAN_BEGIN' and row['metric'] == 'route':
                cargo = fields(d.get('cargo', '').replace(':', '=').replace(',', ';'))
                routes[decision] = {'wool': float(cargo.get('wool', 0)), 'nav': [], 'score': None, 'distanceCut': None}
            if decision in routes and d.get('town') == 'town_V7':
                route = routes[decision]
                if kind == 'ROUTE_CANDIDATE_BEGIN': route['distanceCut'] = d.get('distanceCut')
                if kind == 'ROUTE_NAVIGATION': route['nav'].append({'type': row['metric'], 'distance': float(row['after']), 'fromPort': d.get('fromPort')})
                if kind == 'ROUTE_SCORE': route['score'] = float(row['after'])
            if kind == 'CARAVAN_END' and decision in routes:
                route = routes.pop(decision)
                if route['wool'] > 0 and route['score'] is not None:
                    outcome = 'negative' if route['score'] < 0 else 'nonnegative'
                    charas[outcome] += 1
                    charas[outcome + '/distanceCut=' + str(route['distanceCut'])] += 1
                    if row['metric'] == 'selected=town_V7': charas['selected'] += 1
                    for nav in route['nav']:
                        charas[outcome + '/navigation/' + nav['type']] += 1
                    if not route['nav']: charas[outcome + '/no_navigation_observation'] += 1
                    if len(samples) < 3: samples.append(route)
            if kind == 'WORKSHOP_GATE' and row['metric'].startswith('Can') and d.get('inputs') == 'sheep:1':
                outcome = ('accepted' if float(row['after']) else 'profit' if float(d['outputIncome']) <= float(d['nativeProfitHurdle'])
                           else 'cash' if d['effectCapital'] == 'True' and float(d['townGold']) < float(d['outputIncome']) else 'other')
                group = sheep[owner]
                group[outcome] += 1
                group['quoted_margin_sum'] += float(d['outputIncome']) - float(d['inputCost'])
                group['evaluations'] += 1
            if kind == 'WALLET_CHANGE' and owner.startswith('Hero/'):
                chain = d.get('callers', d.get('source', 'unattributed'))
                source = observed_caller(chain)
                hero_sources[source][0] += 1
                hero_sources[source][1] += float(row['after']) - float(row['before'])
            if kind == 'WALLET_CHECK' and owner.startswith('Hero/'):
                residuals[owner] = float(row['after']) - float(row['before'])
    totals = collections.Counter()
    for group in sheep.values(): totals.update(group)
    return {'source': path, 'schema': schema, 'closed': closed, 'charas': dict(charas),
            'charas_samples': samples, 'sheep_totals': dict(totals),
            'sheep_workshops': len(sheep), 'sheep_with_no_accepted_gate': sum(not x['accepted'] for x in sheep.values()),
            'hero_observed_sources_not_missing_source_proof': dict(hero_sources),
            'hero_residual_sum_not_inflation_proof': sum(residuals.values()),
            'hero_residual_records': sum(v != 0 for v in residuals.values()),
            'open_routes': len(routes)}


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
