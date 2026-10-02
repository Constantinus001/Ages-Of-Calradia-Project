import importlib.util
from pathlib import Path
import unittest
import csv
import tempfile

spec = importlib.util.spec_from_file_location('payments', Path(__file__).with_name('Analyze-WorkshopPayments.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PaymentJoinTests(unittest.TestCase):
    def test_wallet_window_and_zero_expense_receipts_survive_parsing(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'wallet.tsv'
            with path.open('w',newline='') as stream:
                writer=csv.writer(stream,delimiter='\t')
                writer.writerow(['kind','owner','metric','day','before','after','detail'])
                writer.writerows([
                    ['SESSION_START','s','supply_v7',0,0,0,''],
                    ['WALLET_BASELINE','Workshop/1','Capital',0,100,100,''],
                    ['WORKSHOP_STATE','town/shop','capital',0,0,100,'type=wine_press; wallet=Workshop/1; workshopTag=w'],
                    ['WALLET_CHECK','Workshop/1','Capital',1,100,77,''],
                    ['WORKSHOP_CASH_BOUNDARY','Workshop/1','operating_expense',1,77,77,'originalRan=True'],
                    ['WORKSHOP_CASH_BOUNDARY','Workshop/1','owner_payout',1,77,77,'originalRan=False'],
                    ['WALLET_CHECK','Workshop/1','Capital',2,100,100,''],
                    ['SESSION_END','s','done',2,0,2,'']])
            result=module.analyze(path)
        evidence=result['workshop_wallet_evidence']['town/shop']
        self.assertEqual(evidence['maximum_absolute_residual'],23)
        self.assertEqual(evidence['final_residual'],0)
        self.assertEqual(evidence['executed_boundaries'],{'operating_expense':1})
        self.assertEqual(evidence['final_check_day'],2)
        self.assertIn('town/shop',result['by_workshop'])

    def test_expense_only_workshop_maps_without_successful_cycle(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.tsv'
            with path.open('w', newline='') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['kind','owner','metric','day','before','after','detail'])
                writer.writerows([
                    ['SESSION_START','s','supply_v7',0,0,0,''],
                    ['WORKSHOP_STATE','town/workshop:1','capital',0,0,100,'type=wine_press; wallet=Workshop/instance:2/Capital'],
                    ['WALLET_CHANGE','Workshop/instance:2/Capital','Capital',1,100,77,'callers=WorkshopsCampaignBehavior.HandleNotableWorkshopExpense'],
                    ['SESSION_END','s','done',1,0,1,''],
                ])
            result = module.analyze(str(path))
        self.assertEqual(result['unmapped_nonproduction_wallets'], {})
        self.assertEqual(result['by_workshop_type']['wine_press']['operating_result_with_native_daily_tick_inference'], -23)
        self.assertEqual(result['by_workshop']['town/workshop:1']['successful_cycles'], 0)

    def test_distributions_are_not_operating_cost(self):
        self.assertEqual(module.capital_source('DefaultClanFinanceModel.CalculateHeroIncomeFromWorkshops'), 'owner_payout')
        self.assertEqual(module.capital_source('WorkshopsCampaignBehavior.DailyTickTown'), 'daily_tick_expense_inferred_native')
        self.assertEqual(module.capital_source('unknown'), 'other')

    def cycle(self):
        return dict(owner='shop', id='1', gate=dict(effectCapital='True', outputIncome='100', inputCost='20'),
                    success=True, paid=100, town_output=-100, cost=20, town_input=20)

    def test_matched_batch(self):
        result = module.reconcile(self.cycle())
        self.assertTrue(all(result[k] == 0 for k in result if k.endswith('difference')))

    def test_invalid_payment_evidence_fails_closed(self):
        for key in ('prepaidInputCost', 'inputCost', 'outputIncome'):
            for value in ('NaN', 'Infinity', '-Infinity', '-1'):
                with self.subTest(key=key, value=value):
                    cycle = self.cycle(); cycle['gate'][key] = value
                    with self.assertRaises(ValueError): module.reconcile(cycle)
        for key in ('paid', 'cost', 'town_output', 'town_input'):
            cycle = self.cycle(); cycle[key] = float('nan')
            with self.assertRaises(ValueError): module.reconcile(cycle)

    def test_prepaid_inputs_not_double_charged_or_free_profit(self):
        cycle = self.cycle(); cycle.update(cost=0, town_input=0)
        cycle['gate']['prepaidInputCost'] = '20'
        result = module.reconcile(cycle)
        self.assertTrue(all(result[k] == 0 for k in result if k.endswith('difference')))
        self.assertEqual(result['recognized_prepaid_inputs'], 20)
        cycle.update(cost=20, town_input=20)
        self.assertEqual(module.reconcile(cycle)['input_quote_difference'], 20)
        cycle['gate']['prepaidInputCost'] = '15'
        self.assertEqual(module.reconcile(cycle)['prepaid_basis_difference'], -5)

    def test_conserved_underpayment_is_not_full_payment(self):
        cycle = self.cycle(); cycle.update(paid=80, town_output=-80)
        result = module.reconcile(cycle)
        self.assertEqual(result['approval_difference'], -20)
        self.assertEqual(result['output_cash_difference'], 0)

    def test_unpaired_cash_and_wrong_input_cost(self):
        cycle = self.cycle(); cycle.update(town_output=-90, cost=25)
        result = module.reconcile(cycle)
        self.assertEqual(result['output_cash_difference'], 10)
        self.assertEqual(result['input_cash_difference'], 5)
        self.assertEqual(result['input_quote_difference'], 5)

    def test_noncapital_or_failed_cycle_not_certified(self):
        cycle = self.cycle(); cycle['success'] = False
        self.assertIsNone(module.reconcile(cycle))

    def test_failed_cycle_cannot_hide_invalid_cash(self):
        cycle = self.cycle(); cycle.update(success=False, paid=float('nan'))
        with self.assertRaises(ValueError): module.reconcile(cycle)
        cycle = self.cycle(); cycle.pop('gate'); cycle['cost'] = float('inf')
        with self.assertRaises(ValueError): module.reconcile(cycle)
        cycle = self.cycle(); cycle['gate']['effectCapital'] = 'False'
        self.assertIsNone(module.reconcile(cycle))

    def test_prepaid_stream_profit_counts_acquisition_cost_once(self):
        rows = [
            ['SESSION_START', 'session', 'supply_v7', 0, 0, ''],
            ['WORKSHOP_ATTEMPT', 'town_V3/workshop:1', 'begin', 0, 0, 'type=wine_press; cycle=2; recipe=0'],
            ['WORKSHOP_GATE', 'town_V3/workshop:1', 'CanNotable:accepted', 0, 1,
             'cycle=2; recipe=0; effectCapital=True; outputIncome=100; inputCost=20; prepaidInputCost=20'],
            ['WALLET_CHANGE', 'Workshop/1/Capital', 'Capital', 1000, 1100, 'callers=ProduceAnOutputToTown'],
            ['WALLET_CHANGE', 'Town/1/Gold', 'Gold', 500, 400, 'callers=ProduceAnOutputToTown'],
            ['WORKSHOP_CYCLE', 'town_V3/workshop:1', 'succeeded', 0, 1, 'cycle=2'],
            ['SESSION_END', 'capture', 'target', 0, 1, '']]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'capture.tsv'
            with path.open('w', newline='', encoding='utf-8') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['kind', 'owner', 'metric', 'before', 'after', 'detail', 'day', 'scope'])
                for row in rows:
                    writer.writerow(row + [1, 0])
            result = module.analyze(path)
        self.assertEqual(result['batch_discrepancies'], [])
        self.assertEqual(result['by_workshop_type']['wine_press']['production_margin_before_expenses'], 80)
        self.assertEqual(result['by_workshop_type']['wine_press']['paid_inputs'], 0)
        self.assertEqual(result['by_workshop_type']['wine_press']['recognized_prepaid_inputs'], 20)
        self.assertEqual(result['by_workshop']['town_V3/workshop:1']['operating_result_with_native_daily_tick_inference'], 80)
    def test_stream_join_and_delivery(self):
        rows = [
            ['SESSION_START','session','supply_v7',0,0,''],
            ['WORKSHOP_ATTEMPT','town_V3/workshop:1','begin',0,0,'type=wine_press; cycle=2; recipe=0'],
            ['WORKSHOP_GATE','town_V3/workshop:1','CanNotable:accepted',0,1,'cycle=2; recipe=0; effectCapital=True; outputIncome=100; inputCost=20'],
            ['WALLET_CHANGE','Workshop/1/Capital','Capital',1000,980,'callers=ConsumeInputFromTownMarket'],
            ['WALLET_CHANGE','Town/1/Gold','Gold',500,520,'callers=ConsumeInputFromTownMarket'],
            ['WALLET_CHANGE','Workshop/1/Capital','Capital',980,1080,'callers=ProduceAnOutputToTown'],
            ['WALLET_CHANGE','Town/1/Gold','Gold',520,420,'callers=ProduceAnOutputToTown'],
            ['WORKSHOP_CYCLE','town_V3/workshop:1','succeeded',0,1,'cycle=2'],
            ['MARKET_DELTA','town_comp_V3','wool',0,4,'settlement=town_V3; parentMarket=0; operation=SellGoods'],
            ['MARKET_DELTA','town_comp_V3','wool',0,4,'settlement=town_V3; parentMarket=1; operation=SellGoods'],
            ['SESSION_END','capture','target',0,1,'']]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'capture.tsv'
            with path.open('w', newline='', encoding='utf-8') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['kind','owner','metric','before','after','detail','day','scope'])
                for row in rows: writer.writerow(row + [1,0])
            result = module.analyze(path)
        self.assertEqual(result['problems'], [])
        self.assertEqual(result['matched_capital_affecting_successful_batches'], 1)
        self.assertEqual(result['batch_discrepancies'], [])
        self.assertEqual(result['by_workshop_type']['wine_press']['production_margin_before_expenses'], 80)
        self.assertEqual(result['focused_stock_flows']['town_V3/wool'], [[1.0,'SellGoods',4.0]])
