"""Independent wallet endpoint reconciliation inside explicit procurement scopes."""
import collections
import csv
import json
import sys

csv.field_size_limit(24000000)


def analyze(path):
    transactions, mappings, pending = {}, {}, {}
    town_mappings, completed = {}, {}
    active = None
    problems, gaps = [], set()
    with open(path, encoding='utf-8-sig', newline='') as source:
        for row in csv.DictReader(source, delimiter='\t'):
            kind = row['kind']
            if kind not in ('PROCUREMENT_TRANSFER','PROCUREMENT_ACCOUNTING','PROCUREMENT_MOVEMENT','WORKSHOP_STATE','WALLET_CHANGE','WALLET_BASELINE'):
                continue
            d = dict(p.strip().split('=',1) for p in row['detail'].split(';') if '=' in p)
            if kind == 'WALLET_BASELINE':
                if row['owner'].startswith('Town/') and d.get('settlement'):
                    if row['owner'] in town_mappings and town_mappings[row['owner']] != d['settlement']:
                        problems.append('Conflicting town wallet mapping')
                    town_mappings[row['owner']] = d['settlement']
            elif kind == 'PROCUREMENT_MOVEMENT':
                if row['metric'] not in ('dispatch','return','liquidation'): continue
                identity = completed.get((d['order'],row['metric']))
                if identity is None:
                    gaps.add('movement_without_committed_transfer'); continue
                call = transactions[identity]
                settlement = d.get('settlement')
                if not settlement or d.get('committed') != 'true':
                    problems.append('Missing committed movement market')
                elif call['market'] is not None and call['market'] != settlement:
                    problems.append('Transaction movements use different markets')
                else: call['market'] = settlement
            elif kind == 'WORKSHOP_STATE':
                if d.get('wallet') and d.get('workshopTag'):
                    shop = row['owner'].split('/')[0]+'/'+d['workshopTag']
                    if d['wallet'] in mappings and mappings[d['wallet']] != shop:
                        problems.append('Conflicting workshop wallet mapping')
                    mappings[d['wallet']] = shop
            elif kind == 'PROCUREMENT_TRANSFER':
                identity = d['transaction']
                key = (d['order'],d['stage'],row['owner'])
                outcome = row['metric']
                if outcome == 'begin':
                    if active is not None or identity in transactions:
                        problems.append('Nested or reused procurement transaction'); continue
                    transactions[identity] = dict(order=key[0],stage=key[1],shop=key[2],
                                                 outcome=None,wallets=collections.Counter(),receipt_cash=None,market=None)
                    active = identity
                else:
                    if active != identity or identity not in transactions:
                        problems.append('Unmatched procurement transaction end'); continue
                    call = transactions[identity]
                    if key != (call['order'],call['stage'],call['shop']):
                        problems.append('Transaction end identity changed')
                    if outcome not in ('committed','rolled_back','failed'):
                        problems.append('Unknown transfer outcome')
                    call['outcome'] = outcome
                    active = None
                    if outcome == 'committed':
                        if key in pending: problems.append('Prior committed transaction lacks accounting receipt')
                        pending[key] = identity
                        completed[key[:2]] = identity
            elif kind == 'WALLET_CHANGE':
                identity = d.get('procurementTransfer')
                if identity in (None,'none'): continue
                if identity != active:
                    problems.append('Cash outside its procurement transaction'); continue
                transactions[identity]['wallets'][row['owner']] += int(row['after'])-int(row['before'])
            elif row['metric'] in ('dispatch','return','liquidation'):
                key = (d['order'],row['metric'],row['owner'])
                identity = pending.pop(key,None)
                if identity is None:
                    gaps.add('accounting_without_committed_transfer'); continue
                if d.get('committed') != 'true': problems.append('Uncommitted accounting receipt')
                transactions[identity]['receipt_cash'] = int(d['cashDelta'])
    if active is not None: problems.append('Unclosed procurement transaction')
    if pending: gaps.add('committed_transfer_without_accounting')
    for identity, call in transactions.items():
        wallets = call['wallets']
        if call['outcome'] == 'rolled_back':
            if any(wallets.values()): problems.append('Rollback cash residual: '+identity)
            continue
        if call['outcome'] != 'committed':
            problems.append('Failed or incomplete transfer: '+identity); continue
        expected = call['receipt_cash']
        if expected is None: continue
        shops = {w:v for w,v in wallets.items() if w.startswith('Workshop/')}
        towns = {w:v for w,v in wallets.items() if w.startswith('Town/')}
        if len(shops)+len(towns) != len(wallets): problems.append('Unexpected cash endpoint: '+identity)
        if expected != 0 and (len(shops)!=1 or len(towns)!=1):
            gaps.add('missing_or_ambiguous_cash_endpoints')
        for wallet in shops:
            if wallet not in mappings: gaps.add('workshop_wallet_mapping_missing')
            elif mappings[wallet] != call['shop']: problems.append('Wrong workshop cash endpoint: '+identity)
        if call['market'] is None: gaps.add('committed_transfer_market_movement_missing')
        for wallet in towns:
            if wallet not in town_mappings: gaps.add('town_wallet_mapping_missing')
            elif call['market'] is not None and town_mappings[wallet] != call['market']:
                problems.append('Wrong town cash endpoint: '+identity)
        call['workshop_cash_residual'] = sum(shops.values())-expected
        call['town_cash_residual'] = sum(towns.values())+expected
        if call['workshop_cash_residual'] or call['town_cash_residual']:
            problems.append('Transaction cash residual: '+identity)
    if not transactions: gaps.add('procurement_transactions_not_exercised')
    return dict(transactions=transactions,problems=problems,coverage_gaps=sorted(gaps),
                limits='Checks cash endpoints against the committed movement market; intended supplier policy, cargo quantities and cost basis remain separate requirements.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]),indent=2,allow_nan=False))
