"""Bounded receipt timelines. Missing observations are gaps, never invented loss."""
from collections import Counter


def reference(row):
    return {k: row.get(k) for k in ('line', 'sequence', 'day', 'kind', 'metric', 'owner')}


class Timelines:
    def __init__(self):
        self.orders = {}
        self.receipts = set()
        self.duplicates = 0
        self.duplicate_examples = []
        self.unlinked_plans = Counter()
        self.lifecycle = {}
        self.plans = {}
        self.cycles = {}
        self.conflicting_cycle_links = 0

    def order_entry(self, order, row):
        return self.orders.setdefault(order, {'stages': {}, 'transactions': {}, 'holds': {},
            'transaction_event_counts': {}, 'related_wallet_flows_not_profit': {},
            'first': reference(row), 'last': reference(row)})

    @staticmethod
    def stage(group, stage, row):
        item = group.setdefault(stage, {'count': 0, 'first': reference(row), 'last': reference(row)})
        item['count'] += 1
        item['last'] = reference(row)

    def observe(self, row, d):
        kind = row['kind']
        cycle = d.get('workshopCycle') if kind == 'WALLET_CHANGE' else d.get('cycle')
        if kind == 'WORKSHOP_INPUT_WITNESS' and d.get('privateState') == 'accepted_current_attempt' and d.get('order') not in (None, 'none'):
            if cycle not in (None, '0', 'none'):
                if cycle in self.cycles and self.cycles[cycle] != d['order']:
                    self.conflicting_cycle_links += 1
                    self.cycles[cycle] = None
                else:
                    self.cycles[cycle] = d['order']
        linked = self.cycles.get(cycle)
        if linked:
            entry = self.order_entry(linked, row)
            entry['last'] = reference(row)
            if kind in ('WORKSHOP_INPUT_WITNESS', 'WORKSHOP_PRODUCED', 'WORKSHOP_CONSUMED', 'WORKSHOP_CYCLE'):
                self.stage(entry['stages'], kind.lower() + ':' + row['metric'], row)
            if kind == 'WALLET_CHANGE':
                cash = entry['related_wallet_flows_not_profit'].setdefault(row['owner'], dict(count=0, net=0))
                cash['count'] += 1
                cash['net'] += float(row['after']) - float(row['before'])
        if kind == 'WORKSHOP_CYCLE': self.cycles.pop(cycle, None)
        if kind == 'PROCUREMENT_PLAN':
            if d.get('plan') not in (None, 'unobserved', 'none') and not d.get('order'):
                self.plans[d['plan']] = dict(evidence=reference(row), detail=row['detail'])
            if not d.get('order'): self.unlinked_plans[row['metric']] += 1
        order = d.get('order')
        if order and kind.startswith('PROCUREMENT_'):
            receipt = d.get('receipt')
            if receipt:
                key = (kind, receipt)
                if key in self.receipts:
                    self.duplicates += 1
                    if len(self.duplicate_examples) < 8:
                        self.duplicate_examples.append(reference(row))
                    return
                self.receipts.add(key)
            entry = self.order_entry(order, row)
            entry['last'] = reference(row)
            if kind == 'PROCUREMENT_PLAN' and d.get('plan') in self.plans:
                entry['selection'] = self.plans[d['plan']]
                if self.unlinked_plans['offer'] > 0: self.unlinked_plans['offer'] -= 1
            if kind == 'PROCUREMENT_TRANSFER':
                transaction = d.get('transaction')
                if transaction:
                    entry['transaction_event_counts'][transaction] = entry['transaction_event_counts'].get(transaction, 0) + 1
                    trace = entry['transactions'].setdefault(transaction, [])
                    # Two terminal records are retained as a discrepancy, not replaced.
                    if len(trace) < 4:
                        trace.append(dict(outcome=row['metric'], stage=d.get('stage'), **reference(row)))
                return
            group = entry['holds'] if kind == 'PROCUREMENT_HOLD' else entry['stages']
            stage = kind.removeprefix('PROCUREMENT_').lower() + ':' + row['metric']
            self.stage(group, stage, row)
        if kind in ('SHIP_OWNER_CHANGE', 'SHIP_DESTRUCTION', 'SHIP_MEMBERSHIP', 'QUEST_LIFECYCLE_END', 'LOGISTICS_END'):
            identity = ('ship:' + d['ship']) if d.get('ship') else row['owner']
            family = 'ship' if kind.startswith('SHIP_') else 'quest' if kind.startswith('QUEST_') else 'logistics'
            item = self.lifecycle.setdefault(family + ':' + identity,
                                             {'events': {}, 'first': reference(row), 'last': reference(row), 'examples': []})
            item['last'] = reference(row)
            item['events'][kind + ':' + row['metric']] = item['events'].get(kind + ':' + row['metric'], 0) + 1
            if len(item['examples']) < 8:
                item['examples'].append(dict(detail=row['detail'], **reference(row)))

    def report(self, closed):
        orders = {}
        for order, original in self.orders.items():
            entry = dict(original)
            stages = entry['stages']
            gaps = []
            dispatch = 'movement:dispatch' in stages
            arrival = 'movement:arrival' in stages
            consumption = 'movement:consumption' in stages
            # A capture may start with cargo already on the saved ledger.
            if (arrival or consumption) and not dispatch:
                gaps.append('dispatch_not_observed_check_opening_ledger')
            if consumption and not arrival:
                gaps.append('arrival_not_observed_check_opening_ledger')
            if dispatch and 'accounting:dispatch' not in stages:
                gaps.append('dispatch_accounting_receipt_missing')
            if any(s.startswith('workshop_produced:') for s in stages) and not consumption:
                gaps.append('production_event_without_order_consumption_receipt')
            for tx, trace in entry['transactions'].items():
                outcomes = [x['outcome'] for x in trace]
                if entry['transaction_event_counts'][tx] > len(trace):
                    gaps.append('transaction:' + tx + ':excess_records:' + str(entry['transaction_event_counts'][tx]))
                if outcomes != ['begin', 'committed'] and outcomes != ['begin', 'rolled_back']:
                    if outcomes == ['begin'] and not closed:
                        continue
                    gaps.append('transaction:' + tx + ':' + ','.join(outcomes))
            terminal = any(s in stages for s in ('movement:return', 'movement:liquidation'))
            entry.update(first_missing_or_inconsistent_step=gaps[0] if gaps else None,
                         review_gaps=gaps,
                         status='review_required' if gaps else 'terminal_receipt_observed' if terminal else
                         'consumption_observed_quantity_remaining_requires_ledger' if consumption else
                         'in_progress_or_outside_capture_not_lost')
            orders[order] = entry
        return dict(orders=orders, lifecycle=self.lifecycle, duplicate_receipts=self.duplicates,
                    conflicting_cycle_links=self.conflicting_cycle_links,
                    duplicate_examples=self.duplicate_examples, unlinked_plan_counts=dict(self.unlinked_plans),
                    limits=['Selection joins require an explicit plan ID at dispatch; old unlinked plans are never joined by proximity.',
                            'Output events and wallet deltas join only through accepted-order cycle IDs; related cash is not operating profit.',
                            'Opening ledger may explain missing early steps. Missing delivery is not lost cargo.',
                            'Lifecycle examples are capped at eight per entity; event counts and endpoints are complete.',
                            'Ship ownership and roster removal do not by themselves prove destruction.'])
