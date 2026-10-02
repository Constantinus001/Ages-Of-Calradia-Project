import importlib.util
import base64
import csv
import hashlib
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('accrual', Path(__file__).with_name('Analyze-ProcurementAccrual.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class AccrualTests(unittest.TestCase):
    def snapshot(self, orders, stage='current_daily'):
        raw=json.dumps(dict(Schema=1, Orders=orders)).encode()
        detail='payloadBase64='+base64.b64encode(raw).decode()+'; sha256='+hashlib.sha256(raw).hexdigest()
        return ['PROCUREMENT_LEDGER',stage,'campaign',0,0,detail,'s','1']

    def analyze(self, rows):
        stream=io.StringIO()
        writer=csv.writer(stream,delimiter='\t')
        writer.writerow(['kind','metric','owner','before','after','detail','session','day'])
        writer.writerows(rows)
        with patch('builtins.open',return_value=io.StringIO(stream.getvalue())):
            return module.analyze('fixture')

    def consume(self, before=45):
        return ['PROCUREMENT_ACCOUNTING','consumption','t/w',before,23,
                'receipt=00112233445566778899aabbccddeeff; order=o; committed=true; cashDelta=0; originalGoods=60; originalFreight=7','s','1']

    def movement(self):
        return ['PROCUREMENT_MOVEMENT','consumption','campaign',0,0,
                'receipt=11112233445566778899aabbccddeeff; order=o; item=wool; committed=true; cargoBefore=4; cargoAfter=2','s','1']

    def wallet_map(self):
        return ['WORKSHOP_STATE','capital','t/workshop:1',0,100,'wallet=Workshop/1; workshopTag=w','s','1']

    def test_current_basis_reconciles_consumption(self):
        opening=self.order(); opening['OrderId']='o'
        closing=self.order(); closing.update(OrderId='o',Quantity=1)
        closing['Lines'][0]['Remaining']=2
        result=self.analyze([self.wallet_map(),self.snapshot([opening],'current_opening'),self.movement(),self.consume(),self.snapshot([closing])])
        self.assertFalse(result['problems'])
        self.assertFalse(result['coverage_gaps'])
        self.assertEqual(result['basis_intervals_checked'],1)

    def test_missing_receipt_and_wrong_basis_are_detected(self):
        opening=self.order(); opening['OrderId']='o'
        closing=self.order(); closing.update(OrderId='o',Quantity=1)
        closing['Lines'][0]['Remaining']=2
        for receipts in ([],[self.consume(44)]):
            result=self.analyze([self.snapshot([opening],'current_opening'),*receipts,self.snapshot([closing])])
            self.assertTrue(result['problems'])

    def test_loaded_payload_is_not_current_snapshot(self):
        result=self.analyze([self.snapshot([self.order()],'loaded_payload'),self.consume()])
        self.assertIn('current_snapshot_missing',result['coverage_gaps'])
        self.assertIn('accounting_before_current_snapshot',result['coverage_gaps'])

    def test_unexplained_order_disappearance_is_detected(self):
        result=self.analyze([self.snapshot([self.order()],'current_opening'),self.snapshot([])])
        self.assertIn('Unexplained current-ledger order change: t/w',result['problems'])

    def test_balanced_basis_does_not_hide_missing_cargo_receipt(self):
        opening=self.order(); opening['OrderId']='o'
        closing=self.order(); closing.update(OrderId='o',Quantity=1)
        closing['Lines'][0]['Remaining']=2
        result=self.analyze([self.snapshot([opening],'current_opening'),self.consume(),self.snapshot([closing])])
        self.assertIn('Current-ledger cargo residual',result['problems'])

    def test_duplicate_movement_is_not_applied_twice(self):
        opening=self.order(); opening['OrderId']='o'
        result=self.analyze([self.snapshot([opening],'current_opening'),self.movement(),self.movement()])
        self.assertIn('Duplicate cargo receipt',result['problems'])
        self.assertIn('receipts_after_last_current_snapshot',result['coverage_gaps'])

    def test_cash_matches_independent_workshop_wallet_not_unrelated_income(self):
        receipt=['PROCUREMENT_ACCOUNTING','dispatch','t/w',0,67,
                 'receipt=00112233445566778899aabbccddeeff; order=o; committed=true; cashDelta=-67; originalGoods=60; originalFreight=7','s','1']
        payment=['WALLET_CHANGE','Capital','Workshop/1',100,33,
                 'callers=AgesOfCalradia.WorkshopProcurement.ProcurementBehavior.Dispatch','s','1']
        unrelated=['WALLET_CHANGE','Capital','Workshop/1',33,103,'callers=Native.ProduceAnOutputToTown','s','1']
        result=self.analyze([self.wallet_map(),payment,unrelated,receipt])
        self.assertEqual(result['cash_checks']['t/w']['residual'],0)
        self.assertEqual(result['cash_checks']['t/w']['observed_wallet_cash'],-67)
        missing=self.analyze([self.wallet_map(),unrelated,receipt])
        self.assertEqual(missing['cash_checks']['t/w']['residual'],67)
        self.assertIn('procurement_cash_residual_or_missing_caller_coverage',missing['coverage_gaps'])

    def test_return_recognizes_unrecovered_freight_not_whole_refund_as_profit(self):
        row = dict(before='67', after='0', metric='return', owner='shop')
        detail = dict(receipt='00112233-4455-6677-8899-aabbccddeeff', order='order', committed='true',
                      cashDelta='60', originalGoods='60', originalFreight='7')
        self.assertEqual(module.accounting(row, detail)['recognized_cost_or_loss'], 7)
        row['metric'] = 'liquidation'; detail['cashDelta'] = '80'
        self.assertEqual(module.accounting(row, detail)['recognized_cost_or_loss'], -13)

    def test_consumption_never_charges_cash_and_allows_rounding_to_zero(self):
        row = dict(before='2', after='2', metric='consumption', owner='shop')
        detail = dict(receipt='00112233-4455-6677-8899-aabbccddeeff', order='order', committed='true',
                      cashDelta='0', originalGoods='1', originalFreight='1')
        self.assertEqual(module.accounting(row, detail)['recognized_cost_or_loss'], 0)
        detail['cashDelta'] = '-2'
        with self.assertRaises(ValueError): module.accounting(row, detail)

    def order(self):
        return dict(Town='t', Workshop='w', Quantity=2, OriginalQuantity=3,
                    GoodsCost=60, FreightCost=7, TransferComplete=True, Arrived=False,
                    Lines=[dict(Item='wool', UnitsPerBatch=2, Remaining=4)])

    def test_rounding_and_freight_retained_without_double_expense(self):
        result = module.summarize(json.dumps(dict(Schema=1, Orders=[self.order()])))['t/w']
        self.assertEqual(result['allocated_cost_since_dispatch'], 22)
        self.assertEqual(result['remaining_cost_basis'], 45)
        self.assertEqual(result['cargo'], {'wool': 4})

    def test_partial_or_uncommitted_not_valued_as_complete(self):
        for change in ('partial', 'uncommitted'):
            order = self.order()
            if change == 'partial': order['Lines'][0]['Remaining'] = 3
            else: order['TransferComplete'] = False
            with self.assertRaises(ValueError):
                module.summarize(json.dumps(dict(Schema=1, Orders=[order])))

    def test_duplicate_not_double_counted(self):
        with self.assertRaises(ValueError):
            module.summarize(json.dumps(dict(Schema=1, Orders=[self.order(), self.order()])))
