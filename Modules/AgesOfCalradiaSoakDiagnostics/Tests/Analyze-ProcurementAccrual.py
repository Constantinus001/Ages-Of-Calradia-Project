"""Read-only cost-basis snapshots; not a claim that cash outflow equals expense."""
import base64
import collections
import csv
import hashlib
import json
import sys
import uuid

csv.field_size_limit(24000000)


def summarize(payload):
    state = json.loads(payload)
    if state.get('Schema') != 1:
        raise ValueError('Unsupported procurement schema')
    shops = {}
    for order in state['Orders']:
        key = order['Town'] + '/' + order['Workshop']
        if key in shops:
            raise ValueError('Duplicate workshop obligation')
        quantity, original = order['Quantity'], order['OriginalQuantity']
        goods, freight = order['GoodsCost'], order['FreightCost']
        if any(type(v) is not int for v in (quantity, original, goods, freight)):
            raise ValueError('Invalid cost-basis integer')
        if not 0 < quantity <= original or min(goods, freight) <= 0:
            raise ValueError('Invalid cost basis')
        if not order.get('TransferComplete'):
            raise ValueError('Uncommitted order is not valued inventory')
        cargo = {}
        for line in order['Lines']:
            if line['Remaining'] != quantity * line['UnitsPerBatch']:
                raise ValueError('Partial batch cannot be certified')
            cargo[line['Item']] = cargo.get(line['Item'], 0) + line['Remaining']
        total = goods + freight
        recognized = total * (original - quantity) // original
        shops[key] = dict(order=order.get('OrderId'), goods_paid=goods, freight_paid=freight,
                          original_cost=total, remaining_cost_basis=total-recognized,
                          allocated_cost_since_dispatch=recognized, remaining_batches=quantity,
                          arrived=order['Arrived'], cargo=cargo)
    return shops


def accounting(row, detail):
    uuid.UUID(detail['receipt'])
    if not detail.get('order') or detail.get('committed') != 'true':
        raise ValueError('Unidentified or uncommitted accounting receipt')
    before, after = int(row['before']), int(row['after'])
    cash, goods, freight = (int(detail[k]) for k in ('cashDelta', 'originalGoods', 'originalFreight'))
    if min(before, after) < 0 or min(goods, freight) <= 0 or max(before, after) > goods + freight:
        raise ValueError('Invalid accounting basis')
    stage = row['metric']
    if stage == 'dispatch':
        if before != 0 or after != goods + freight or cash != -after:
            raise ValueError('Dispatch basis/cash mismatch')
        expense = 0
    elif stage == 'consumption':
        if cash != 0 or after > before:
            raise ValueError('Consumption basis/cash mismatch')
        expense = before - after
    elif stage in ('return', 'liquidation'):
        if after != 0 or cash < 0:
            raise ValueError('Return basis/cash mismatch')
        expense = before - cash  # Negative is a realized liquidation gain.
    else:
        raise ValueError('Unknown accounting stage')
    return dict(order=detail['order'], shop=row['owner'], stage=stage,
                cash_delta=cash, recognized_cost_or_loss=expense,
                basis_before=before, basis_after=after)


def analyze(path):
    snapshots, unavailable, problems = [], 0, []
    receipts, receipt_ids = [], set()
    expected, gaps, comparisons = None, set(), 0
    cargo_expected, movement_ids = None, set()
    wallet_shops, wallet_cash = {}, collections.Counter()
    pending = False
    with open(path, encoding='utf-8-sig', newline='') as source:
        for row in csv.DictReader(source, delimiter='\t'):
            if row['kind'] not in ('PROCUREMENT_LEDGER', 'PROCUREMENT_ACCOUNTING', 'PROCUREMENT_MOVEMENT', 'WORKSHOP_STATE', 'WALLET_CHANGE'):
                continue
            detail = dict(p.strip().split('=', 1) for p in row['detail'].split(';') if '=' in p)
            if row['kind'] == 'WORKSHOP_STATE':
                wallet, tag = detail.get('wallet'), detail.get('workshopTag')
                if wallet and tag:
                    shop = row['owner'].split('/')[0]+'/'+tag
                    if wallet in wallet_shops and wallet_shops[wallet] != shop:
                        problems.append('Procurement wallet identity conflict: '+wallet)
                    else: wallet_shops[wallet] = shop
                continue
            if row['kind'] == 'WALLET_CHANGE':
                chain = detail.get('callers', detail.get('source', ''))
                if row['owner'].startswith('Workshop/') and any(name in chain for name in (
                        'AgesOfCalradia.WorkshopProcurement.ProcurementBehavior.',
                        'AgesOfCalradia.WorkshopProcurement.ProcurementTransfer.')):
                    wallet_cash[row['owner']] += int(row['after'])-int(row['before'])
                continue
            if row['kind'] == 'PROCUREMENT_MOVEMENT':
                try:
                    identity = str(uuid.UUID(detail['receipt']))
                    if identity in movement_ids: raise ValueError('Duplicate cargo receipt')
                    movement_ids.add(identity)
                    order, item = detail['order'], detail['item']
                    before, after = int(detail['cargoBefore']), int(detail['cargoAfter'])
                    if not order or not item or detail.get('committed') != 'true' or min(before, after) < 0:
                        raise ValueError('Invalid committed cargo receipt')
                    stage = row['metric']
                    if (stage == 'dispatch' and (before != 0 or after <= 0)
                            or stage == 'arrival' and before != after
                            or stage == 'consumption' and after >= before
                            or stage in ('return', 'liquidation') and after != 0
                            or stage not in ('dispatch', 'arrival', 'consumption', 'return', 'liquidation')):
                        raise ValueError('Invalid cargo stage transition')
                    pending = True
                    if cargo_expected is None:
                        gaps.add('movement_before_current_snapshot')
                    else:
                        key = (order, item)
                        if cargo_expected.get(key, 0) != before:
                            problems.append('Cargo quantity discontinuity: '+order+'/'+item)
                        if after: cargo_expected[key] = after
                        else: cargo_expected.pop(key, None)
                except (ValueError, KeyError, TypeError) as error:
                    problems.append(str(error))
                continue
            if row['kind'] == 'PROCUREMENT_ACCOUNTING':
                try:
                    identity = str(uuid.UUID(detail['receipt']))
                    if identity in receipt_ids:
                        raise ValueError('Duplicate accounting receipt')
                    receipt_ids.add(identity)
                    receipt = accounting(row, detail)
                    receipts.append(receipt)
                    pending = True
                    if expected is None:
                        gaps.add('accounting_before_current_snapshot')
                    else:
                        shop = receipt['shop']
                        previous = expected.get(shop)
                        if receipt['stage'] == 'dispatch':
                            if previous is not None:
                                problems.append('Dispatch replaces outstanding basis: '+shop)
                        elif previous is None:
                            problems.append('Accounting order absent from current ledger: '+shop)
                        else:
                            if previous['order'] is None:
                                gaps.add('legacy_order_identity_not_joined')
                            elif previous['order'] != receipt['order']:
                                problems.append('Accounting order identity mismatch: '+shop)
                            if previous['basis'] != receipt['basis_before']:
                                problems.append('Accounting basis discontinuity: '+shop)
                        if receipt['basis_after'] == 0:
                            expected.pop(shop, None)
                        else:
                            expected[shop] = dict(order=receipt['order'], basis=receipt['basis_after'])
                except (ValueError, KeyError, TypeError) as error:
                    problems.append(str(error))
                continue
            if 'payloadBase64' not in detail:
                unavailable += 1
                continue
            try:
                raw = base64.b64decode(detail['payloadBase64'], validate=True)
                if hashlib.sha256(raw).hexdigest().lower() != detail.get('sha256', '').lower():
                    raise ValueError('Ledger payload hash mismatch')
                shops = summarize(raw)
                snapshots.append(dict(session=row['session'], day=row['day'], stage=row['metric'], shops=shops))
                # Loaded-save payloads are provenance, not current inventory.
                if row['metric'] in ('current_opening', 'current_daily', 'current_terminal'):
                    observed = {shop: dict(order=value['order'], basis=value['remaining_cost_basis'])
                                for shop, value in shops.items()}
                    cargo_observed = {}
                    legacy = any(value['order'] is None for value in shops.values())
                    if legacy:
                        gaps.add('legacy_order_identity_not_joined')
                    else:
                        for value in shops.values():
                            for item, quantity in value['cargo'].items():
                                key = (value['order'], item)
                                if key in cargo_observed: raise ValueError('Duplicate cargo order identity')
                                cargo_observed[key] = quantity
                        if cargo_expected is not None and cargo_expected != cargo_observed:
                            problems.append('Current-ledger cargo residual')
                    cargo_expected = None if legacy else cargo_observed
                    if expected is not None:
                        comparisons += 1
                        for shop in expected.keys() | observed.keys():
                            left, right = expected.get(shop), observed.get(shop)
                            if left is None or right is None:
                                problems.append('Unexplained current-ledger order change: '+shop)
                            else:
                                if left['basis'] != right['basis']:
                                    problems.append('Current-ledger basis residual: '+shop)
                                if left['order'] is None or right['order'] is None:
                                    gaps.add('legacy_order_identity_not_joined')
                                elif left['order'] != right['order']:
                                    problems.append('Current-ledger order identity mismatch: '+shop)
                    expected = observed
                    pending = False
            except (ValueError, KeyError, TypeError) as error:
                problems.append(str(error))
    if expected is None: gaps.add('current_snapshot_missing')
    if pending: gaps.add('receipts_after_last_current_snapshot')
    if comparisons == 0: gaps.add('snapshot_interval_not_exercised')
    if unavailable: gaps.add('ledger_unavailable')
    expected_cash, observed_cash = collections.Counter(), collections.Counter()
    for receipt in receipts: expected_cash[receipt['shop']] += receipt['cash_delta']
    for wallet, cash in wallet_cash.items():
        if wallet not in wallet_shops: gaps.add('procurement_cash_wallet_unmapped')
        else: observed_cash[wallet_shops[wallet]] += cash
    cash_checks = {}
    for shop in expected_cash.keys() | observed_cash.keys():
        mapped = shop in wallet_shops.values()
        residual = observed_cash[shop]-expected_cash[shop]
        cash_checks[shop] = dict(receipt_cash=expected_cash[shop], observed_wallet_cash=observed_cash[shop],
                                residual=residual, wallet_mapped=mapped)
        if not mapped: gaps.add('procurement_cash_wallet_unmapped')
        if residual: gaps.add('procurement_cash_residual_or_missing_caller_coverage')
    return dict(snapshots=snapshots, accounting_receipts=receipts, unavailable=unavailable, problems=problems,
                basis_intervals_checked=comparisons, coverage_gaps=sorted(gaps),
                cash_checks=cash_checks,
                limits='Cash checks are aggregate caller-attributed workshop deltas, not transaction-level proof: omitted/inlined callers or offsetting errors remain possible. Counterparty cash and full net profit certification remain required.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
