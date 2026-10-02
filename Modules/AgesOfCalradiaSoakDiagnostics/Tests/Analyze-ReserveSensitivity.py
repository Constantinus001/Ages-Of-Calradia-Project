"""Read-only iron/wool reserve sensitivity, NOT a dispatch or profit forecast."""
import csv
import json
import math
import sys

csv.field_size_limit(24000000)
CATEGORIES = ('iron', 'wool')


def reserves(recipe_units, daily_units, days, minimum=10, batches=7):
    values = (recipe_units, daily_units, days, minimum, batches)
    if any(not math.isfinite(v) or v < 0 for v in values):
        raise ValueError('Reserve inputs must be finite and nonnegative')
    return max(minimum, recipe_units * batches), max(minimum, math.ceil(daily_units * days))


def analyze(path):
    stocks, recipes, sessions = {}, {}, set()
    closed = False
    with open(path, encoding='utf-8-sig', newline='') as source:
        for row in csv.DictReader(source, delimiter='\t'):
            if not row.get('detail'):
                continue
            sessions.add(row['session'])
            if len(sessions) > 1:
                raise ValueError('Separate sessions before reserve sensitivity analysis')
            kind = row['kind']
            closed |= kind == 'SESSION_END'
            detail = dict(p.strip().split('=', 1) for p in row['detail'].split(';') if '=' in p)
            if kind == 'STOCK_SNAPSHOT' and row['metric'] in CATEGORIES and detail.get('settlementKind') == 'town':
                stocks.setdefault((row['owner'], row['metric']), float(row['after']))
            elif kind == 'WORKSHOP_PROGRESS':
                rate = float(detail['derivedIncrement'])
                if not math.isfinite(rate) or rate < 0:
                    raise ValueError('Invalid derived recipe increment')
                for entry in detail.get('inputs', '').split(','):
                    if ':' not in entry:
                        continue
                    category, units = entry.rsplit(':', 1)
                    if category not in CATEGORIES:
                        continue
                    units = int(units)
                    if units <= 0:
                        raise ValueError('Invalid recipe input units')
                    key = (row['owner'], row['metric'], category)
                    previous = recipes.get(key)
                    if previous and previous[0] != units:
                        raise ValueError('Recipe definition changed during capture')
                    # Use the maximum observed increment, not successful consumption:
                    # failed input gates must not make local demand appear to vanish.
                    recipes[key] = (units, max(rate, previous[1] if previous else 0))
    if not closed:
        raise ValueError('Use a closed capture; live sensitivity is not supported')
    totals = {}
    for (owner, recipe, category), (units, rate) in recipes.items():
        key = (owner.split('/')[0], category)
        count, daily = totals.get(key, (0, 0))
        totals[key] = count + units, daily + units * rate
    results = []
    for (town, category), (units, daily) in sorted(totals.items()):
        stock = stocks.get((town, category))
        if stock is None:
            continue  # Missing snapshot is unknown, never zero stock.
        alternatives = {}
        for days in (7, 14, 30):
            baseline, candidate = reserves(units, daily, days)
            alternatives[str(days)] = dict(reserve=candidate, stock_qualified=stock-3 >= candidate)
        results.append(dict(town=town, category=category, opening_stock=stock,
                            observed_recipe_units=units, maximum_observed_daily_units=daily,
                            baseline_reserve=baseline, baseline_stock_qualified=stock-3 >= baseline,
                            day_reserves=alternatives))
    return dict(session=next(iter(sessions), None), closed=closed, rows=results,
                assumptions='Three-unit offer; minimum 10; baseline seven batches. Iron/wool only. '
                'Opening stocks versus summed per-recipe maximum later increments; stationary sensitivity only. '
                'Unobserved recipes, simultaneous peaks, route, quotes, competition, capital and food safety '
                'are not simulated. Qualified stock does not prove an eligible shipment or future profit.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
