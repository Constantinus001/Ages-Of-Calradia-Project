import unittest
from EvidenceWalletLedger import WalletLedger
from EvidenceAcceptance import checklist
import test_causal_evidence as causal_tests


class WalletLedgerTests(unittest.TestCase):
    def replay(self, records):
        ledger = WalletLedger()
        for n, (kind, before, after) in enumerate(records, 1):
            ledger.observe(dict(kind='WALLET_' + kind, owner='wallet', metric='Gold',
                                before=before, after=after, sequence=n, day=1))
        return ledger.report()

    def test_continuous_ledger(self):
        r = self.replay([('BASELINE', 100, 100), ('CHANGE', 100, 90), ('CHECK', 90, 90)])
        self.assertEqual(r['accounting_status'], 'passed')
        self.assertEqual(r['identity_status'], 'passed')

    def test_missing_change_cannot_hide_behind_expected_value(self):
        r = self.replay([('BASELINE', 100, 100), ('CHECK', 110, 110)])
        self.assertEqual(r['accounting_status'], 'failed')
        self.assertEqual(r['counts']['actual_balance_mismatch'], 1)

    def test_duplicate_baseline_never_rebases(self):
        r = self.replay([('BASELINE', 100, 100), ('BASELINE', 110, 110), ('CHECK', 110, 110)])
        self.assertEqual(r['accounting_status'], 'failed')
        self.assertEqual(r['identity_status'], 'failed')

    def test_old_run_signature_accounting_pass_identity_fail(self):
        r = self.replay([('BASELINE', 0, 0), ('BASELINE', 0, 0), ('CHANGE', 0, 10),
                         ('CHECK', 0, 10), ('CHECK', 10, 10)])
        self.assertEqual(r['accounting_status'], 'passed')
        self.assertEqual(r['identity_status'], 'failed')
        self.assertEqual(r['counts']['conflicting_same_snapshot'], 1)

    def test_real_change_between_checks_is_not_snapshot_conflict(self):
        r = self.replay([('BASELINE', 0, 0), ('CHECK', 0, 0), ('CHANGE', 0, 10), ('CHECK', 10, 10)])
        self.assertEqual(r['identity_status'], 'passed')

    def test_duplicate_change_is_not_invisible(self):
        r = self.replay([('BASELINE', 0, 0), ('CHANGE', 0, 10), ('CHANGE', 0, 10), ('CHECK', 10, 10)])
        self.assertEqual(r['counts']['transaction_discontinuity'], 1)
        self.assertEqual(r['accounting_status'], 'failed')

    def test_missing_baseline_does_not_infer_zero(self):
        r = self.replay([('CHANGE', 0, 10), ('CHECK', 10, 10)])
        self.assertEqual(r['counts']['missing_baseline'], 2)
        self.assertEqual(r['accounting_status'], 'failed')

    def test_baseline_only_is_not_validation(self):
        self.assertEqual(self.replay([('BASELINE', 0, 0)])['accounting_status'], 'not_exercised')

    def test_combined_report_rejects_false_pass(self):
        r = causal_tests.CausalEvidenceTests.run_rows(self, [
            ('WALLET_BASELINE', 'w', 'Gold', 100, 100, ''),
            ('WALLET_CHECK', 'w', 'Gold', 110, 110, '')])
        self.assertEqual(checklist(r)['scenarios']['wallet_independent_ledger']['status'], 'failed')

    def test_report_preserves_contradiction_not_false_recovery(self):
        r = causal_tests.CausalEvidenceTests.run_rows(self, [
            ('WALLET_BASELINE', 'w', 'Gold', 0, 0, ''),
            ('WALLET_CHANGE', 'w', 'Gold', 0, 10, ''),
            ('WALLET_CHECK', 'w', 'Gold', 0, 10, ''),
            ('WALLET_CHECK', 'w', 'Gold', 10, 10, '')])
        self.assertEqual(r['findings'][0]['false_positive_status'],
                         'conflicting_same_snapshot_requires_identity_review')

    def test_metrics_are_not_merged(self):
        ledger = WalletLedger()
        for metric in ('Gold', 'Debt'):
            ledger.observe(dict(kind='WALLET_BASELINE', owner='same', metric=metric,
                                before=0, after=0, sequence=1, day=1))
        self.assertEqual(ledger.report()['wallets'], 2)
