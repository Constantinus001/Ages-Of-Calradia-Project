"""Independent first-baseline replay; never trusts snapshot expected balances.

Duplicate baselines fail identity integrity even when their amounts reconcile.
No automatic rebasing, alias merging, discarded contradictions, or balance verdict.
"""
from collections import Counter
import math


class WalletLedger:
    def __init__(self):
        self.wallets = {}
        self.counts = Counter()
        self.errors = {}

    def error(self, code, row):
        self.counts[code] += 1
        item = self.errors.setdefault(code + '|' + row['owner'],
                                      dict(code=code, owner=row['owner'], count=0, examples=[]))
        item['count'] += 1
        if len(item['examples']) < 3:
            item['examples'].append({k: row.get(k) for k in
                                    ('sequence', 'line', 'day', 'kind', 'before', 'after')})

    def observe(self, row):
        kind = row['kind']
        if kind not in ('WALLET_BASELINE', 'WALLET_CHANGE', 'WALLET_CHECK'):
            return
        key = row['owner'] + '|' + row['metric']
        before, after = float(row['before']), float(row['after'])
        if not math.isfinite(before) or not math.isfinite(after):
            raise ValueError('Nonfinite wallet evidence')
        self.counts[kind] += 1
        wallet = self.wallets.get(key)
        if kind == 'WALLET_BASELINE':
            if before != after:
                self.error('invalid_baseline', row)
            if wallet is not None:
                self.error('duplicate_baseline', row)
                if before != wallet['balance']:
                    self.error('conflicting_baseline', row)
                return
            self.wallets[key] = dict(balance=before, changes=0, last_check=None,
                                     first_sequence=row.get('sequence'))
            return
        if wallet is None:
            self.error('missing_baseline', row)
            return
        if kind == 'WALLET_CHANGE':
            if before != wallet['balance']:
                self.error('transaction_discontinuity', row)
            wallet['balance'] += after - before
            wallet['changes'] += 1
            return
        if after != wallet['balance']:
            self.error('actual_balance_mismatch', row)
        if before != wallet['balance']:
            self.error('expected_balance_mismatch', row)
        previous = wallet['last_check']
        now = (row['day'], wallet['changes'], before, after)
        if previous and previous[:2] == now[:2] and previous[2:] != now[2:]:
            self.error('conflicting_same_snapshot', row)
        wallet['last_check'] = now

    def report(self):
        accounting = ('invalid_baseline', 'missing_baseline', 'conflicting_baseline',
                      'transaction_discontinuity', 'actual_balance_mismatch')
        identity = ('duplicate_baseline', 'conflicting_same_snapshot')
        exercised = bool(self.wallets and self.counts['WALLET_CHECK'])
        return dict(
            schema=1, wallets=len(self.wallets), counts=dict(self.counts),
            accounting_status='failed' if any(self.counts[k] for k in accounting) else
                              'passed' if exercised else 'not_exercised',
            identity_status='failed' if any(self.counts[k] for k in identity) else
                            'passed' if exercised else 'not_exercised',
            issues=list(self.errors.values()),
            limits=['Pass applies only to observed endpoints and net transactions.',
                    'Snapshots cannot exclude unobserved offsetting mutations.',
                    'Identities are session-local; aliases are not additional money.',
                    'Missing baseline evidence never permits inferred zero balances.'])
