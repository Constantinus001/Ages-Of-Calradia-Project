import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('broad_movements',Path(__file__).with_name('Analyze-BroadSupplyCapture.py'))
broad=importlib.util.module_from_spec(spec)
spec.loader.exec_module(broad)


class MovementTests(unittest.TestCase):
    def analyze(self, stage='dispatch', before=100, after=97, cb=0, ca=3, duplicate=False):
        rows=[]
        def add(kind, metric='', owner='town', start=0, end=0, detail='', day=1):
            rows.append(dict(utc='2026-09-20T00:00:00Z',session='fixture',sequence=len(rows)+1,day=day,
                             kind=kind,scope=0,parent=0,owner=owner,metric=metric,before=start,after=end,detail=detail))
        add('SESSION_START','supply_v7')
        add('STOCK_SNAPSHOT','wool',start=before,end=before,detail='settlementKind=town')
        detail=('receipt=11111111111111111111111111111111; order=order1; settlement=town; item=wool; category=wool; '
                f'cargoBefore={cb}; cargoAfter={ca}; committed=true')
        add('PROCUREMENT_MOVEMENT',stage,'campaign',before,after,detail)
        if duplicate: add('PROCUREMENT_MOVEMENT',stage,'campaign',before,after,detail)
        add('STOCK_SNAPSHOT','wool',start=after,end=after,detail='settlementKind=town',day=2)
        add('SESSION_END',day=2)
        stream=io.StringIO();writer=csv.DictWriter(stream,fieldnames=rows[0],delimiter='\t');writer.writeheader();writer.writerows(rows)
        with patch('builtins.open',return_value=io.StringIO(stream.getvalue())):
            return broad.analyze('fixture')

    def test_dispatch(self):
        result=self.analyze();self.assertFalse(result['stock_unexplained_residuals']);self.assertFalse(result['problems'])

    def test_return_and_liquidation(self):
        for stage in ('return','liquidation'):
            r=self.analyze(stage,97,100,3,0)
            self.assertFalse(r['stock_unexplained_residuals']);self.assertFalse(r['problems'])

    def test_arrival_and_consumption_do_not_mint_market_stock(self):
        for stage,after in [('arrival',3),('consumption',2)]:
            r=self.analyze(stage,97,97,3,after)
            self.assertFalse(r['stock_unexplained_residuals']);self.assertFalse(r['problems'])

    def test_duplicate_rejected_not_counted_twice(self):
        r=self.analyze(duplicate=True)
        self.assertTrue(r['problems']);self.assertFalse(r['stock_unexplained_residuals'])

    def test_unbalanced_does_not_hide_residual(self):
        r=self.analyze(ca=4)
        self.assertTrue(r['problems']);self.assertEqual(r['stock_unexplained_residuals']['town/wool'],-3)
