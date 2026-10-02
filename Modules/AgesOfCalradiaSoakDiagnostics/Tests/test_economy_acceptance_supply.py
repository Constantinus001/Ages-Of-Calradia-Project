import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('acceptance', Path(__file__).with_name('Analyze-EconomyAcceptance.py'))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class AcceptanceTest(unittest.TestCase):
    def test_absent_coverage_never_passes(self):
        result = acceptance.assess({}, {}, {})
        self.assertEqual(len(result['capture_blockers']), 2)
        for key in ('1_clan_hero_gold', '2_workshop_payments', '3_input_shortages', '4_workshop_profitability'):
            self.assertTrue(result[key]['missing_evidence'])
            self.assertNotIn('PASS', result[key]['status'])

    def test_closed_zero_residuals_do_not_certify_payments_or_profit(self):
        result = acceptance.assess({'integrity': 'CLOSED_NO_DETECTED_INTEGRITY_ERRORS'},
                                  {'wallet_unexplained_final_residuals': {}}, {'schema': 'supply_v7'})
        self.assertEqual(result['capture_blockers'], [])
        self.assertEqual(result['2_workshop_payments']['status'], 'LIVE_BATCH_RECONCILIATION_REQUIRED')
        self.assertEqual(result['4_workshop_profitability']['status'], 'REALIZED_MARGIN_AND_EXPENSE_REVIEW_REQUIRED')

    def test_external_finance_and_residuals_remain_visible(self):
        broad = {'wallet_unexplained_final_residuals': {'Hero/1': 42},
                 'transfer_receipts_not_additive_income': {'by_id': {
                     '1': {'interpretation': 'external_source_or_sink_requires_caller_attribution_not_inflation_proof'}}}}
        result = acceptance.assess({}, broad, {'schema': 'supply_v6'})
        gold = result['1_clan_hero_gold']
        self.assertEqual(gold['wallet_residuals']['Hero/1'], 42)
        self.assertIn('1', gold['receipts_requiring_caller_review'])
        self.assertTrue(any('v7' in x for x in result['capture_blockers']))


if __name__ == '__main__':
    unittest.main()
