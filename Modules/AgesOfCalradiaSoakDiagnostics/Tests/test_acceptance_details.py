import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('details', Path(__file__).with_name('Analyze-AcceptanceDetails.py'))
module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)


class AcceptanceDetailsTests(unittest.TestCase):
    def test_combined_planning_forecast_is_not_realized_profit(self):
        result = self.analyze([('PROCUREMENT_PLAN','town/shop','offer',0,0,
            'selectedDailyRate=2; selectedBatches=10; selectedSource=source; selectedLandedCost=300; '
            'selectedScoreEstimate=34; selectedDailyExpense=23; maximumAdaptiveBatches=12; wineExpenseCoverage=1.25; selectedRequiredMargin=48; supplierCadenceRejected=1; plannerComputeMilliseconds=2.5',11)])
        plan = result['procurement_planning']['workshops']['town/shop']
        self.assertEqual(plan['last_forecast']['selectedBatches'], '10')
        self.assertEqual(plan['last_forecast']['selectedLandedCost'], '300')
        self.assertEqual(plan['last_forecast']['selectedRequiredMargin'], '48')
        self.assertEqual(plan['last_forecast']['wineExpenseCoverage'], '1.25')
        self.assertEqual(plan['candidate_rejections']['supplierCadenceRejected'], 1)
        self.assertEqual(plan['compute_cost_excludes_logging']['maximum_ms'], 2.5)
        self.assertEqual(result['procurement_outcomes'], {})

    def test_invalid_planner_timing_rejected(self):
        for value in ('NaN', '-1', 'Infinity'):
            with self.subTest(value=value), self.assertRaises(ValueError):
                self.analyze([('PROCUREMENT_PLAN','town/shop','offer',0,0,'plannerComputeMilliseconds='+value,11)])

    def test_planner_candidates_are_separate_from_shipments(self):
        result = self.analyze([
            ('PROCUREMENT_PLAN','town/shop','offer',0,0,'categoryReserveRejected=8; foodSafetyRejected=2; policy=p; selectedDailyRate=0.75; selectedThresholdBatches=3; selectedLeadDays=2',11),
            ('PROCUREMENT_PLAN','town/shop','capital_reserve',0,0,'capitalRejected=1; policy=p',12)])
        plan = result['procurement_planning']['workshops']['town/shop']
        self.assertEqual(plan['decisions'], 2)
        self.assertEqual(plan['candidate_rejections']['categoryReserveRejected'], 8)
        self.assertEqual(plan['last_forecast']['selectedThresholdBatches'], '3')
        self.assertEqual(result['procurement_outcomes'], {})

    def analyze(self, records, close=True):
        rows = [('SESSION_START', 'campaign', 'supply_v7', 0, 0, '', 10)] + records
        if close: rows += [('SESSION_END', 'capture', 'complete', 10, 15, '', 15)]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.tsv'
            with path.open('w', newline='') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['session','day','kind','owner','metric','before','after','detail'])
                for kind, owner, metric, before, after, detail, day in rows:
                    writer.writerow(['one', day, kind, owner, metric, before, after, detail])
            return module.analyze(path)

    def test_missing_evidence_is_not_pass(self):
        result = self.analyze([])
        self.assertIn('runtime_readiness_hooks_not_recorded', result['coverage_gaps'])
        self.assertIn('naval_removal_not_exercised', result['coverage_gaps'])
        self.assertIn('writer_cost_not_measured', result['coverage_gaps'])

    def test_null_giver_is_not_automatically_a_grant(self):
        self.assertEqual(module.category({'source':'GiveGoldAction.ApplyInternal','giverPresent':'False'}), 'unclassified')

    def test_gross_transfer_not_double_counted_and_offsetting_residuals_kept(self):
        result = self.analyze([
            ('WALLET_BASELINE','Hero/1','Gold',100,100,'',10),
            ('WALLET_CHANGE','Hero/1','Gold',100,130,'source=DailyTickHero',11),
            ('GOLD_TRANSFER_ENDPOINT','Hero/1','Gold',100,130,'',11),
            ('WALLET_CHECK','Hero/1','Gold',130,125,'',12),
            ('WALLET_CHECK','Hero/1','Gold',130,130,'',15)])
        wallet = result['wealth']['wallets']['Hero/1']
        self.assertEqual(wallet['observed_in'], 30)
        self.assertEqual(wallet['final_residual'], 0)
        self.assertIn('unexplained_wallet_change', wallet['coverage_gaps'])

    def test_duplicate_gate_is_one_cycle_not_two_shortages(self):
        detail = 'recipe=0; cycle=1; type=weavery; inputs=wool:1; outputs=cloth:1'
        gate = ('WORKSHOP_GATE','t/workshop:1','DetermineItemRosterHasSufficientInputs:inputs_rejected',0,0,detail,11)
        result = self.analyze([gate, gate, ('WORKSHOP_CYCLE','t/workshop:1','failed_see_gates',0,0,detail,11)])
        recipe = result['supply_chains']['wool_outputs']['t/workshop:1/recipe:0']
        self.assertEqual(recipe['gate_cycles']['input_gate_rejected_not_independent_shortage'], 1)
        self.assertEqual(recipe['cycle_outcomes']['failed_see_gates'], 1)

    def test_low_margin_is_not_weak_demand_proof(self):
        detail = 'recipe=0; cycle=1; type=wine_press; inputs=grape:1; outputs=wine:1; outputIncome=10; nativeProfitHurdle=20'
        result = self.analyze([('WORKSHOP_GATE','t/workshop:1','CanNotable:rejected',0,0,detail,11)])
        r = result['focus_workshops']['t/workshop:1/recipe:0']
        self.assertIn('native_margin_hurdle_candidate_not_weak_demand_proof', r['gate_cycles'])

    def test_effective_margin_overrides_native_reference(self):
        detail = 'recipe=0; cycle=1; type=wine_press; inputs=grape:1; outputs=wine:1; outputIncome=70; nativeProfitHurdle=100; effectiveProfitHurdle=68; effectCapital=True; townGold=40'
        result = self.analyze([('WORKSHOP_GATE','t/workshop:1','CanNotable:rejected',0,0,detail,11)])
        gates = result['focus_workshops']['t/workshop:1/recipe:0']['gate_cycles']
        self.assertIn('town_cash_gate_candidate', gates)
        self.assertNotIn('native_margin_hurdle_candidate_not_weak_demand_proof', gates)

    def test_unknown_effective_margin_is_not_native_fallback(self):
        detail = 'recipe=0; cycle=1; type=wine_press; inputs=grape:1; outputs=wine:1; outputIncome=70; nativeProfitHurdle=100; effectiveProfitHurdle=NaN; townGold=1000'
        result = self.analyze([('WORKSHOP_GATE','t/workshop:1','CanNotable:rejected',0,0,detail,11)])
        self.assertIn('unclassified_gate_rejection', result['focus_workshops']['t/workshop:1/recipe:0']['gate_cycles'])

    def test_effective_margin_failure_named_separately(self):
        detail = 'recipe=0; cycle=1; type=wine_press; inputs=grape:1; outputs=wine:1; outputIncome=60; nativeProfitHurdle=100; effectiveProfitHurdle=68'
        result = self.analyze([('WORKSHOP_GATE','t/workshop:1','CanNotable:rejected',0,0,detail,11)])
        self.assertIn('effective_margin_hurdle_candidate_not_weak_demand_proof', result['focus_workshops']['t/workshop:1/recipe:0']['gate_cycles'])

    def test_skipped_removal_is_not_executed_or_destroyed(self):
        rows = [('SHIP_LIFECYCLE_BEGIN','p','RemoveParty',0,1,'removal=r',11),
                ('SHIP_MEMBERSHIP','p','before',0,1,'removal=r; ship=1; owner=party:p',11),
                ('SHIP_MEMBERSHIP','p','after',0,1,'removal=r; ship=1; owner=party:p',11),
                ('SHIP_LIFECYCLE_END','p','RemoveParty',1,1,'removal=r; originalRan=False; partyActive=True',11)]
        result = self.analyze(rows)
        self.assertEqual(result['naval']['counts']['skipped_removal'], 1)
        self.assertNotIn('executed_removal', result['naval']['counts'])
        self.assertNotIn('removed_party_retains_ship_requires_review', result['coverage_gaps'])

    def test_open_removal_only_becomes_problem_at_closure(self):
        rows = [('SHIP_LIFECYCLE_BEGIN','p','RemoveParty',0,1,'removal=r',11)]
        self.assertFalse(self.analyze(rows, close=False)['problems'])
        self.assertIn('Unclosed ship removal', self.analyze(rows)['problems'])

    def test_cost_and_growth_are_measured_and_drops_flagged(self):
        result = self.analyze([
            ('DAY_TIMING','campaign','sample',10,11,'wallSeconds=30; bytes=1000',11),
            ('DAY_TIMING','campaign','sample',11,12,'wallSeconds=40; bytes=3000',12),
            ('DIAGNOSTIC_COST','capture','cumulative_lower_bound',0,0.2,'discardedRows=4',12)])
        rates = result['diagnostic_cost']['measured_growth'][0]
        self.assertEqual(rates['bytes_per_day'], 2000)
        self.assertEqual(rates['seconds_per_day_including_pauses'], 40)
        self.assertIn('discarded_diagnostic_records', result['coverage_gaps'])
