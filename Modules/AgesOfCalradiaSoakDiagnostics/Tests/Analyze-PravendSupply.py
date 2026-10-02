"""Read-only wool route/sale evidence for one capture, without recomputing models."""
import collections
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import json
import sys


def fields(text):
    return dict(x.strip().split('=', 1) for x in text.split(';') if '=' in x)


def wool(detail):
    cargo = dict(x.split(':', 1) for x in detail.get('cargo', '').split(',') if ':' in x)
    return float(cargo.get('wool', 0))


def analyze(path):
    routes, sales = {}, {}
    counts = collections.Counter()
    samples, arrivals, sale_details = [], [], []
    production = collections.Counter()
    closed = False
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            kind, metric = row['kind'], row['metric']
            if kind == 'SESSION_END': closed = True
            if kind not in ('CARAVAN_BEGIN','CARAVAN_END','ROUTE_CANDIDATE_BEGIN','ROUTE_SCORE',
                            'ROUTE_PERMISSION','ROUTE_NAVIGATION','CARAVAN_ARRIVAL','SELL_BEGIN',
                            'SELL_INDEX','SELL_FACTOR','SELL_QUANTITY','SELL_END','ITEM_PRODUCED'):
                continue
            d = fields(row['detail']); decision = d.get('decision')
            if kind == 'ITEM_PRODUCED' and metric == 'wool':
                production[row['owner']] += float(row['after']) - float(row['before'])
            if kind == 'CARAVAN_BEGIN' and metric == 'route':
                routes[decision] = {'wool': wool(d), 'party': row['owner'], 'scores': {}, 'target': {}, 'day': row['day']}
            if decision in routes:
                route = routes[decision]
                town = d.get('town')
                if kind == 'ROUTE_SCORE': route['scores'][town] = float(row['after'])
                if town == 'town_V3' and kind.startswith('ROUTE_'):
                    route['target'][kind + '/' + metric] = dict(before=row['before'], after=row['after'], detail=d)
                if kind == 'CARAVAN_END':
                    route = routes.pop(decision)
                    if route['wool'] > 0:
                        counts['wool_carrying_route_decisions'] += 1
                        score = route['scores'].get('town_V3')
                        counts['pravend_not_scored' if score is None else 'pravend_negative' if score < 0 else 'pravend_nonnegative'] += 1
                        if metric == 'selected=town_V3': counts['pravend_selected'] += 1
                        route['selected'] = metric
                        route['best_score'] = max(route['scores'].values(), default=None)
                        route.pop('scores')
                        samples.append(route)
            if kind == 'CARAVAN_ARRIVAL' and metric == 'town_V3':
                arrivals.append(dict(party=row['owner'], day=row['day'], wool=wool(d)))
            if row['owner'] == 'town_V3' and kind.startswith('SELL_'):
                if kind == 'SELL_BEGIN': sales[decision] = dict(start=d, wool=wool(d), evidence=[])
                elif decision in sales:
                    sale = sales[decision]
                    if metric == 'wool': sale['evidence'].append(dict(kind=kind, before=row['before'], after=row['after'], detail=d))
                    if kind == 'SELL_END':
                        sale['end'] = d; sale['ending_wool'] = wool(d)
                        sale_details.append(sales.pop(decision))
    return dict(closed=closed, counts=dict(counts), pravend_arrivals=arrivals,
                pravend_sales=sale_details, wool_route_evidence=samples,
                wool_production_by_owner=dict(production), open_routes=len(routes), open_sales=len(sales),
                limit='Repeated decisions and two sale passes are not independent caravans; no route score or price model is rerun.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
