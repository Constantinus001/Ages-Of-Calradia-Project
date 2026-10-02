import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('pravend', Path(__file__).with_name('Analyze-PravendSupply.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PravendTests(unittest.TestCase):
    def test_empty_cargo_is_not_a_wool_delivery(self):
        self.assertEqual(module.wool({'cargo': 'grain:10,wool:0'}), 0)
        self.assertEqual(module.wool({'cargo': 'wool:3,grain:10'}), 3)

    def test_routing_and_sale_evidence_are_separate(self):
        rows = [
            ['CARAVAN_BEGIN','party','route',0,0,'decision=1; cargo=wool:3'],
            ['ROUTE_SCORE','party','native_final',0,10,'decision=1; town=town_V3'],
            ['ROUTE_SCORE','party','native_final',0,20,'decision=1; town=town_V7'],
            ['CARAVAN_END','party','selected=town_V7',0,0,'decision=1'],
            ['CARAVAN_ARRIVAL','other','town_V3',0,0,'cargo=grain:5'],
            ['SELL_BEGIN','town_V3','cargo',0,0,'decision=2; cargo=grain:5'],
            ['SELL_END','town_V3','cargo',0,0,'decision=2; cargo=grain:4'],
            ['SESSION_END','capture','target',0,1,'']]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'capture.tsv'
            with path.open('w',newline='',encoding='utf-8') as stream:
                writer = csv.writer(stream,delimiter='\t')
                writer.writerow(['kind','owner','metric','before','after','detail','day'])
                for row in rows: writer.writerow(row + [1])
            result = module.analyze(path)
        self.assertEqual(result['counts']['pravend_nonnegative'], 1)
        self.assertNotIn('pravend_selected', result['counts'])
        self.assertEqual(result['pravend_arrivals'][0]['wool'], 0)
        self.assertEqual(result['open_routes'], 0)
        self.assertEqual(result['open_sales'], 0)
