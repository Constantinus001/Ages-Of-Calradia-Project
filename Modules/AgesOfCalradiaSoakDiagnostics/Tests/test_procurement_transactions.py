import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('transactions',Path(__file__).with_name('Analyze-ProcurementTransactions.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class TransactionTests(unittest.TestCase):
    def analyze(self,rows):
        stream=io.StringIO()
        writer=csv.writer(stream,delimiter='\t')
        writer.writerow(['kind','metric','owner','before','after','detail'])
        writer.writerows(rows)
        with patch('builtins.open',return_value=io.StringIO(stream.getvalue())):
            return module.analyze('fixture')

    def rows(self):
        context='transaction=x; order=o; stage=dispatch'
        return [
            ['WORKSHOP_STATE','capital','town/workshop:1',0,100,'wallet=Workshop/1; workshopTag=shop'],
            ['PROCUREMENT_TRANSFER','begin','town/shop',0,0,context],
            ['WALLET_CHANGE','Capital','Workshop/1',100,70,'procurementTransfer=x'],
            ['WALLET_CHANGE','Gold','Town/2',10,40,'procurementTransfer=x'],
            ['PROCUREMENT_TRANSFER','committed','town/shop',0,0,context],
            ['PROCUREMENT_ACCOUNTING','dispatch','town/shop',0,30,'order=o; cashDelta=-30; committed=true'],
            ['WALLET_BASELINE','Gold','Town/2',10,10,'settlement=source'],
            ['PROCUREMENT_MOVEMENT','dispatch','campaign',10,7,'order=o; settlement=source; committed=true'],
        ]

    def test_independent_endpoints_match_receipt(self):
        result=self.analyze(self.rows())
        self.assertFalse(result['problems'])
        self.assertFalse(result['coverage_gaps'])
        self.assertEqual(result['transactions']['x']['town_cash_residual'],0)

    def test_missing_town_and_wrong_workshop_not_hidden_by_net_cash(self):
        rows=self.rows();del rows[3]
        self.assertTrue(self.analyze(rows)['problems'])
        rows=self.rows();rows[0][-1]='wallet=Workshop/1; workshopTag=wrong'
        self.assertIn('Wrong workshop cash endpoint: x',self.analyze(rows)['problems'])

    def test_restored_rollback_requires_zero_at_each_endpoint(self):
        rows=self.rows()[:5];rows[-1][1]='rolled_back'
        self.assertIn('Rollback cash residual: x',self.analyze(rows)['problems'])
        rows[-1:-1]=[['WALLET_CHANGE','Gold','Town/2',40,10,'procurementTransfer=x'],
                     ['WALLET_CHANGE','Capital','Workshop/1',70,100,'procurementTransfer=x']]
        self.assertFalse(self.analyze(rows)['problems'])

    def test_no_events_and_missing_receipt_are_gaps(self):
        self.assertIn('procurement_transactions_not_exercised',self.analyze([])['coverage_gaps'])
        self.assertIn('committed_transfer_without_accounting',self.analyze([r for r in self.rows() if r[0]!='PROCUREMENT_ACCOUNTING'])['coverage_gaps'])

    def test_balanced_payment_to_wrong_town_is_detected(self):
        rows=self.rows();rows[-2][-1]='settlement=wrong'
        self.assertIn('Wrong town cash endpoint: x',self.analyze(rows)['problems'])
        rows=self.rows();rows[-2][-1]=''
        self.assertIn('town_wallet_mapping_missing',self.analyze(rows)['coverage_gaps'])
