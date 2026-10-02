"""Read-only closed-capture batch joins. Sequential cycle attribution is checked, not assumed nested-safe."""
import collections
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import json
import math
import sys


def number(value):
    result = float(value)
    if not math.isfinite(result):
        raise ValueError('Non-finite payment capture value: ' + str(value))
    return result


def fields(text):
    return dict(x.strip().split('=', 1) for x in text.split(';') if '=' in x)


def capital_source(chain):
    # Native 1.4.8: owner withdrawals are distributions, not operating costs.
    # HandleDailyExpense can be inlined into DailyTickTown. Keep that attribution
    # explicitly inferred rather than calling every negative capital delta wages.
    if 'CalculateHeroIncomeFromWorkshops' in chain or 'WithdrawMoney' in chain:
        return 'owner_payout'
    if 'HandleDailyExpense' in chain or 'HandleNotableWorkshopExpense' in chain or 'HandlePlayerWorkshopExpense' in chain:
        return 'operating_expense'
    if 'WorkshopsCampaignBehavior.DailyTickTown' in chain:
        return 'daily_tick_expense_inferred_native'
    return 'other'


def reconcile(cycle):
    for key in ('paid', 'cost', 'town_output', 'town_input'):
        number(cycle[key])
    gate = cycle.get('gate')
    if not gate or gate.get('effectCapital') != 'True' or not cycle['success']:
        return None
    prepaid = number(gate.get('prepaidInputCost', 0))
    quoted = number(gate['inputCost'])
    approved = number(gate['outputIncome'])
    if min(prepaid, quoted, approved) < 0:
        raise ValueError('Negative payment approval or prepaid basis')
    expected_cash = 0 if 'prepaidInputCost' in gate else quoted
    return {'owner': cycle['owner'], 'cycle': cycle['id'],
            'approved': approved, 'paid': cycle['paid'],
            'approval_difference': cycle['paid'] - approved,
            'output_cash_difference': cycle['paid'] + cycle['town_output'],
            'input_cash_difference': cycle['cost'] - cycle['town_input'],
            'input_quote_difference': cycle['cost'] - expected_cash,
            'prepaid_basis_difference': prepaid - quoted if 'prepaidInputCost' in gate else 0,
            'recognized_prepaid_inputs': prepaid}


def analyze(path):
    active = None
    problems, discrepancies = [], []
    totals = collections.defaultdict(collections.Counter)
    shop_totals = collections.defaultdict(collections.Counter)
    recipes = collections.defaultdict(collections.Counter)
    windows = {}
    wallet_shop, outside = {}, collections.defaultdict(collections.Counter)
    wallet_baselines, wallet_checks = {}, {}
    wallet_max_residual = collections.defaultdict(float)
    cash_boundaries = collections.defaultdict(collections.Counter)
    procurement_shop_keys = {}
    matched = 0
    stocks, scopes = {}, {}
    stock_flows = collections.defaultdict(list)
    outside_callers = collections.Counter()
    closed = False
    start = end = None
    with open(path, encoding='utf-8-sig', newline='') as stream:
        for row in csv.DictReader(stream, delimiter='\t'):
            kind, owner = row['kind'], row['owner']
            metric = row['metric']
            if kind == 'SESSION_START': start = number(row['day'])
            if kind == 'SESSION_END': closed = True; end = number(row['day'])
            if kind == 'WALLET_BASELINE' and owner.startswith('Workshop/'):
                if owner in wallet_baselines: problems.append('Repeated workshop wallet baseline: ' + owner)
                wallet_baselines[owner] = number(row['day'])
            if kind == 'WALLET_CHECK' and owner.startswith('Workshop/'):
                residual = number(row['after']) - number(row['before'])
                wallet_checks[owner] = (number(row['day']), residual)
                wallet_max_residual[owner] = max(wallet_max_residual[owner], abs(residual))
            if kind == 'WORKSHOP_CASH_BOUNDARY':
                context = fields(row['detail'])
                if context.get('originalRan') == 'True': cash_boundaries[owner][metric] += 1
            if kind not in ('WORKSHOP_STATE', 'WORKSHOP_ATTEMPT', 'WORKSHOP_GATE', 'WORKSHOP_CYCLE', 'WALLET_CHANGE',
                            'STOCK_SNAPSHOT', 'MARKET_DELTA', 'WORKSHOP_STOCK_DELTA', 'BEGIN', 'END', 'SCOPE_BALANCE'):
                continue
            d = fields(row['detail'])
            if kind == 'WORKSHOP_STATE' and d.get('wallet'):
                if d.get('workshopTag'):
                    procurement_shop_keys[owner] = owner.split('/')[0]+'/'+d['workshopTag']
                wallet, identity = d['wallet'], (owner, d['type'])
                if wallet in wallet_shop and wallet_shop[wallet] != identity:
                    problems.append('Workshop wallet maps to multiple shops: ' + wallet)
                else:
                    wallet_shop[wallet] = identity
                continue
            town = d.get('settlement', owner.split('/')[0])
            item = metric.removeprefix('market/')
            operation = d.get('operation', kind)
            if kind == 'BEGIN': scopes[row['scope']] = (metric, d)
            if kind == 'END': scopes.pop(row['scope'], None)
            if kind == 'SCOPE_BALANCE':
                scope_operation, context = scopes.get(row['scope'], (None, {}))
                if scope_operation == 'ApplyInternal':
                    town, item, operation = context.get('entered'), metric.removeprefix('town/'), 'villager_sale_scope'
            focused = town in ('town_V3', 'town_V7') and item in ('wool', 'cow', 'sheep', 'hog', 'grape', 'flax', 'iron')
            if focused and kind == 'STOCK_SNAPSHOT':
                stocks.setdefault(town + '/' + item, []).append([number(row['day']), number(row['after'])])
            flow = ((kind == 'MARKET_DELTA' and d.get('parentMarket') == '0' and '/' not in metric)
                    or (kind == 'WORKSHOP_STOCK_DELTA' and metric.startswith('market/'))
                    or (kind == 'SCOPE_BALANCE' and operation == 'villager_sale_scope' and metric.startswith('town/')))
            if focused and flow:
                delta = number(row['after']) - number(row['before'])
                if delta: stock_flows[town + '/' + item].append([number(row['day']), operation, delta])
            if kind == 'WORKSHOP_ATTEMPT':
                if active is not None: problems.append('Nested cycle cannot be attributed: ' + d['cycle'])
                active = {'id': d['cycle'], 'owner': owner, 'type': d['type'], 'recipe': d['recipe'],
                          'paid': 0, 'cost': 0, 'town_output': 0, 'town_input': 0, 'success': False}
            elif kind == 'WORKSHOP_GATE' and active is not None:
                if d['cycle'] != active['id'] or owner != active['owner']:
                    problems.append('Gate outside matching cycle')
                    continue
                key = owner + '/recipe:' + d['recipe']
                recipes[key]['attempt_gates'] += 1
                if row['metric'].startswith('Determine'):
                    window = windows.setdefault(key, {'type': d['type'], 'inputs': d['inputs'], 'failures': 0,
                        'successes': 0, 'first_failure': None, 'last_failure': None, 'longest_failed_span_days': 0, 'open_since': None})
                    day = number(row['day'])
                    if number(row['after']) == 0:
                        window['failures'] += 1
                        if window['first_failure'] is None: window['first_failure'] = day
                        window['last_failure'] = day
                        if window['open_since'] is None: window['open_since'] = day
                        window['longest_failed_span_days'] = max(window['longest_failed_span_days'], day - window['open_since'])
                    else:
                        window['successes'] += 1
                        window['open_since'] = None
                elif row['metric'].startswith('Can') and number(row['after']) != 0:
                    active['gate'] = d
            elif kind == 'WALLET_CHANGE':
                delta = number(row['after']) - number(row['before'])
                is_shop = owner.startswith('Workshop/')
                chain = d.get('callers', d.get('source', ''))
                output = 'ProduceAnOutputToTown' in chain
                consumed = 'ConsumeInputFromTownMarket' in chain
                if active is not None and (output or consumed):
                    if is_shop:
                        identity = (active['owner'], active['type'])
                        if owner in wallet_shop and wallet_shop[owner] != identity:
                            problems.append('Workshop wallet maps to multiple shops: ' + owner)
                        wallet_shop[owner] = identity
                        active['paid' if output else 'cost'] += delta if output else -delta
                    elif owner.startswith('Town/'):
                        active['town_output' if output else 'town_input'] += delta
                elif is_shop:
                    outside_callers[chain] += delta
                    category = d.get('cashCategory')
                    source = (category if category in ('owner_payout', 'operating_expense') else 'other') if category else capital_source(chain)
                    outside[owner][source] += delta
            elif kind == 'WORKSHOP_CYCLE':
                if active is None or active['id'] != d['cycle'] or active['owner'] != owner:
                    problems.append('Unmatched cycle end')
                    continue
                active['success'] = row['metric'] == 'succeeded'
                result = reconcile(active)
                if result is not None:
                    matched += 1
                    if any(abs(result[k]) > 0.0001 for k in ('approval_difference', 'output_cash_difference', 'input_cash_difference', 'input_quote_difference', 'prepaid_basis_difference')):
                        discrepancies.append(result)
                group = totals[active['type']]
                group['attempts'] += 1
                group['successful_cycles'] += active['success']
                group['paid_outputs'] += active['paid']
                group['paid_inputs'] += active['cost']
                prepaid = result['recognized_prepaid_inputs'] if result else 0
                group['recognized_prepaid_inputs'] += prepaid
                group['production_margin_before_expenses'] += active['paid'] - active['cost'] - prepaid
                shop = shop_totals[owner]
                shop['paid_outputs'] += active['paid']
                shop['paid_inputs'] += active['cost']
                shop['recognized_prepaid_inputs'] += prepaid
                shop['successful_cycles'] += active['success']
                active = None
    if active is not None: problems.append('Open final cycle')
    if not closed: problems.append('Closed capture required')
    unmatched_wallets = {}
    wallet_evidence = {}
    for wallet, (shop, _) in wallet_shop.items():
        shop_totals[shop]  # Include idle shops as well as shops with production.
        check_day, residual = wallet_checks.get(wallet, (None, None))
        wallet_evidence[shop] = dict(wallet=wallet, baseline_day=wallet_baselines.get(wallet),
            final_check_day=check_day, capture_start=start, capture_end=end,
            final_residual=residual, maximum_absolute_residual=wallet_max_residual.get(wallet),
            executed_boundaries=dict(cash_boundaries[wallet]))
    for wallet, costs in outside.items():
        if wallet in wallet_shop:
            group = totals[wallet_shop[wallet][1]]
            for name, value in costs.items():
                group[name + '_capital_delta'] += value
                shop_totals[wallet_shop[wallet][0]][name + '_capital_delta'] += value
        else: unmatched_wallets[wallet] = dict(costs)
    for group in list(totals.values()) + list(shop_totals.values()):
        group['operating_result_with_native_daily_tick_inference'] = (group['paid_outputs'] - group['paid_inputs'] - group['recognized_prepaid_inputs']
            + group['operating_expense_capital_delta'] + group['daily_tick_expense_inferred_native_capital_delta'])
    return {'closed': closed, 'days': end-start if closed else None, 'problems': problems,
            'matched_capital_affecting_successful_batches': matched,
            'batch_discrepancies': discrepancies, 'by_workshop_type': dict(totals),
            'by_workshop': dict(shop_totals),
            'workshop_wallet_evidence': wallet_evidence,
            'procurement_shop_keys': procurement_shop_keys,
            'unmapped_nonproduction_wallets': unmatched_wallets,
            'nonproduction_capital_caller_deltas_not_profit': dict(outside_callers),
            'focused_stock_samples': stocks, 'focused_stock_flows': dict(stock_flows),
            'input_gate_windows': windows,
            'limits': ['Gate failures are observations, not independent shortage events.',
                       'Failure spans do not prove inventory was continuously absent between attempts.',
                       'Other capital changes and unmapped wallets prevent treating production margin as net profit.',
                       'No live behavior or game state is changed by this analyzer.']}


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2, allow_nan=False))
