import importlib.util
from pathlib import Path
import unittest

spec=importlib.util.spec_from_file_location('profit',Path(__file__).with_name('WorkshopProfitability.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ProfitabilityTests(unittest.TestCase):
    def evidence(self):
        return dict(baseline_day=0, capture_start=0, final_check_day=2, capture_end=2,
                    final_residual=0, maximum_absolute_residual=0,
                    executed_boundaries=dict(operating_expense=2, owner_payout=2))

    def assess_evidence(self, evidence):
        return module.assess(dict(procurement_shop_keys={'s':'town/tag'},
            by_workshop={'s':dict(paid_outputs=100, operating_expense_capital_delta=-10)},
            workshop_wallet_evidence={'s':evidence}), {}, {})

    def test_known_wallet_residual_blocks_reconciled_profit(self):
        evidence=self.evidence(); evidence['final_residual']=-407923
        result=self.assess_evidence(evidence)
        self.assertIn('workshop_wallet_unexplained_change', result['coverage_gaps'])
        self.assertIsNone(result['by_workshop']['s']['reconciled_operating_result'])

    def test_missing_window_receipts_and_offsetting_residuals_are_gaps(self):
        for change in ({'baseline_day':1}, {'final_check_day':1}, {'executed_boundaries':{}},
                       {'maximum_absolute_residual':25}, {'final_residual':None}, {'final_residual':float('nan')}):
            evidence=self.evidence(); evidence.update(change)
            with self.subTest(change=change):
                result=self.assess_evidence(evidence)
                self.assertTrue(result['coverage_gaps'])
                self.assertIsNone(result['by_workshop']['s']['reconciled_operating_result'])

    def test_complete_observations_allow_reconciled_not_balanced_result(self):
        result=self.assess_evidence(self.evidence())
        self.assertEqual(result['coverage_gaps'], [])
        self.assertEqual(result['by_workshop']['s']['reconciled_operating_result'],90)
        self.assertEqual(result['by_workshop']['s']['status'],'OBSERVED_RECONCILED_NOT_BALANCE_CERTIFIED')

    def test_dispatch_not_expensed_twice_and_owner_draw_excluded(self):
        workshops=dict(procurement_shop_keys={'s':'town/tag'},by_workshop={'s':dict(
            paid_outputs=100,paid_inputs=5,recognized_prepaid_inputs=999,
            daily_tick_expense_inferred_native_capital_delta=-10,
            other_capital_delta=-60,owner_payout_capital_delta=-25)})
        accrual=dict(accounting_receipts=[
            dict(shop='town/tag',recognized_cost_or_loss=0,cash_delta=-60),
            dict(shop='town/tag',recognized_cost_or_loss=20,cash_delta=0)])
        result=module.assess(workshops,accrual,{})['by_workshop']['s']
        self.assertEqual(result['observed_operating_result_with_native_daily_expense_inference'],65)
        self.assertEqual(result['unclassified_capital_delta'],0)
        self.assertEqual(result['owner_payout_capital_delta_not_expense'],-25)

    def test_liquidation_gain_and_unknown_capital_are_distinct(self):
        workshops=dict(procurement_shop_keys={'s':'town/tag'},by_workshop={'s':dict(other_capital_delta=90)})
        accrual=dict(accounting_receipts=[dict(shop='town/tag',recognized_cost_or_loss=-13,cash_delta=80)])
        result=module.assess(workshops,accrual,{})
        self.assertEqual(result['by_workshop']['s']['observed_operating_result_with_native_daily_expense_inference'],13)
        self.assertEqual(result['by_workshop']['s']['unclassified_capital_delta'],10)
        self.assertIn('unclassified_capital_change',result['coverage_gaps'])

    def test_missing_identity_and_failed_reconciliation_never_certify_profit(self):
        result=module.assess(dict(by_workshop={'s':{}}),dict(problems=['bad basis']),dict(problems=['missing cash']))
        self.assertEqual(result['by_workshop']['s']['status'],'IDENTITY_MISSING')
        self.assertIn('accrual_evidence_incomplete',result['coverage_gaps'])
        self.assertIn('transaction_evidence_incomplete',result['coverage_gaps'])
