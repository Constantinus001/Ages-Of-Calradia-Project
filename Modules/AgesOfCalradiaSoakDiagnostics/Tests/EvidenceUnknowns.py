"""Evidence-led investigation queue: identify a boundary without inventing cause."""
from collections import deque, Counter
from EvidenceTimelines import reference
from EvidenceCashPurpose import resolve, KNOWN_PURPOSES


class Unknowns:
    def __init__(self):
        self.identities = {}
        self.recent = {}
        self.transactions = {}
        self.cases = {}
        self.town_operations = {}
        self.identified = {}
        self.cash_starts = {}
        self.cash_outcomes = {}

    def observe(self, row, d, number):
        kind, owner = row['kind'], row['owner']
        if kind == 'CASH_PURPOSE_BEGIN': self.cash_starts[d.get('operation')] = (row, d.get('parentOperation'))
        if kind == 'CASH_PURPOSE_END':
            begin = self.cash_starts.pop(d.get('operation'), None)
            valid = (begin is not None and begin[0]['metric'] == row['metric'] and begin[0]['owner'] == owner
                     and begin[1] == d.get('parentOperation')
                     and d.get('originalRan') == 'True' and d.get('error') == 'none')
            self.cash_outcomes[d.get('operation')] = dict(valid=valid, purpose=row['metric'], evidence=reference(row), parent=d.get('parentOperation'))
        if kind == 'TOWN_CASH_BEGIN':
            self.town_operations[d.get('townCashOperation')] = row
        if kind == 'TOWN_CASH_END':
            begin = self.town_operations.pop(d.get('townCashOperation'), None)
            model = number(d.get('modelResult'))
            intact = (begin is not None and begin['owner'] == owner and number(begin['before']) == number(row['before'])
                      and d.get('originalRan') == 'True' and d.get('modelCalls') == '1' and d.get('error') == 'none'
                      and model is not None)
            if intact:
                delta = number(row['after']) - number(row['before'])
                matches = model == delta
                key = owner + ':town_cash_model:' + ('matched' if matches else 'conflict')
                explanation = self.identified.setdefault(key, dict(entity=owner, observations=0,
                    status='model_application_witnessed' if matches else 'model_alone_does_not_explain_delta',
                    explanation='Actual native model return compared with the same operation cash endpoints.',
                    supporting_evidence=[], conflicting_evidence=[],
                    limit='Boundary equality identifies the witnessed mechanism, not exclusive cause, economic balance, or a separate wallet residual.'))
                explanation['observations'] += 1
                target = explanation['supporting_evidence'] if matches else explanation['conflicting_evidence']
                if len(target) < 3:
                    target.append(dict(begin=reference(begin), end=reference(row), model_return=model, actual_delta=delta))
        if kind in ('WALLET_BASELINE', 'WALLET_IDENTITY', 'WALLET_ALIAS'):
            self.identities[owner] = dict(evidence=reference(row), identity=d)
        if kind == 'PROCUREMENT_TRANSFER' and d.get('transaction'):
            self.transactions[d['transaction']] = dict(order=d.get('order'), stage=d.get('stage'),
                                                       outcome=row['metric'], evidence=reference(row))
        if kind == 'WALLET_CHANGE':
            recent = self.recent.setdefault(owner, dict(rows=deque(maxlen=8), count=0))
            recent['count'] += 1
            recent['rows'].append(dict(evidence=reference(row), source=d.get('source'), callers=d.get('callers'),
                                       context={k: d[k] for k in ('procurementTransfer', 'market', 'workshopCycle',
                                           'townCashOperation', 'logisticsOperation', 'reward', 'transfer', 'cashPurposeOperation')
                                                if d.get(k) not in (None, 'none', '', '0')},
                                       delta=number(row['after']) - number(row['before'])))
            contexts = recent['rows'][-1]['context']
            if (not contexts or set(contexts) == {'cashPurposeOperation'}) and d.get('cashCategory') not in ('owner_payout', 'operating_expense', 'capital_reset'):
                key = 'unclassified_cash:' + owner + ':' + str(d.get('source'))
                case = self.cases.setdefault(key, dict(entity=owner, question='What purpose produced this wallet change?',
                    status='unresolved', observations=0, supporting_evidence=[], conflicting_evidence=[],
                    candidates=[], operation_counts=Counter(), missing_proof=['Actual transaction purpose or an enclosing witnessed operation.'],
                    next_investigation='Inspect the recorded call path and patch provenance at this exact boundary; no income tuning.'))
                case['observations'] += 1
                if contexts.get('cashPurposeOperation'): case['operation_counts'][contexts['cashPurposeOperation']] += 1
                if len(case['supporting_evidence']) < 3:
                    case['supporting_evidence'].append(recent['rows'][-1])
                if d.get('source'):
                    case['status'] = 'mutation_boundary_identified_purpose_unresolved'
                    case['candidates'] = [dict(explanation='Observed mutation boundary: ' + d['source'],
                                               confidence='boundary_witnessed_not_economic_cause')]
        if kind == 'WALLET_CHECK':
            if number(row['before']) != number(row['after']):
                recent = self.recent.get(owner, dict(rows=[], count=0))
                key = 'wallet_residual:' + owner
                case = self.cases.setdefault(key, dict(entity=owner, question='Why do observed flows not match this wallet?',
                    status='unresolved', observations=0, supporting_evidence=[], conflicting_evidence=[], candidates=[],
                    missing_proof=['A missing or misattributed mutation that explains the full residual with sign and amount.'],
                    next_investigation='Inspect the first divergent interval, identity mapping, nested netting and capture-loss evidence.'))
                case['observations'] += 1
                if len(case['supporting_evidence']) < 3:
                    case['supporting_evidence'].append(dict(evidence=reference(row),
                        residual=number(row['after']) - number(row['before']), nearby_changes=list(recent['rows']),
                        interval_change_count=recent['count'], omitted_changes=max(0, recent['count'] - 8)))
                case['candidates'] = [dict(explanation=x, confidence='hypothesis_not_proven') for x in
                    ('A native or mod mutation bypassed an observer.', 'A wallet identity or nested accounting boundary differs.',
                     'Evidence was dropped, interrupted, or began after the relevant operation.')]
            self.recent.pop(owner, None)

    def report(self):
        cases = {}
        for key, original in self.cases.items():
            case = dict(original)
            known = Counter()
            conflicts = []
            for operation, count in case.get('operation_counts', {}).items():
                outcome = self.cash_outcomes.get(operation)
                purpose = resolve(operation, self.cash_outcomes)
                if purpose in KNOWN_PURPOSES:
                    known[purpose] += count
                elif outcome and not outcome['valid'] and len(conflicts) < 3:
                    conflicts.append(outcome['evidence'])
            case['identified_purpose_counts'] = dict(known)
            case['conflicting_evidence'] = conflicts
            if sum(known.values()) == case['observations']:
                case['status'] = 'purpose_boundary_identified_not_exclusive_cause'
                case['missing_proof'] = ['Boundary purpose identified; broader profitability or balance still requires accounting.']
            elif known:
                case['status'] = 'some_purposes_identified_remaining_unknown'
            case['identity_evidence'] = self.identities.get(case['entity'])
            # Context-linked receipts strengthen identification, never infer that
            # a nearby transaction is the explanation for an unrelated residual.
            links = []
            for evidence in case['supporting_evidence']:
                for change in evidence.get('nearby_changes', [evidence]):
                    transaction = change.get('context', {}).get('procurementTransfer')
                    if transaction in self.transactions:
                        links.append(self.transactions[transaction])
            case['linked_transactions'] = links
            case['promotion_rule'] = 'Only explain a discrepancy after matched identity, operation, amount and intact coverage; proximity is insufficient.'
            cases[key] = case
        return dict(cases=cases, identified_mechanisms=self.identified, limits=['Examples are bounded; omitted counts are explicit.',
                                         'Stack frames identify a call path, not automatically a grant, wage or exploit.',
                                         'Absent conflicting evidence does not prove a hypothesis.',
                                         'Old captures cannot reveal an event they never recorded.'])
