import importlib.util
import csv
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('reserve', Path(__file__).with_name('Analyze-ReserveSensitivity.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReserveSensitivityTests(unittest.TestCase):
    def analyze(self, rows):
        stream = io.StringIO()
        writer = csv.writer(stream, delimiter='\t')
        writer.writerow(['session', 'kind', 'owner', 'metric', 'after', 'detail'])
        writer.writerows(rows)
        stream.seek(0)
        with patch('builtins.open', return_value=stream):
            return module.analyze('fixture')

    def sample(self):
        return [
            ['s', 'STOCK_SNAPSHOT', 'town_V1', 'iron', 47, 'settlementKind=town'],
            ['s', 'WORKSHOP_PROGRESS', 'town_V1/workshop:1', 'recipe:0', 0,
             'inputs=iron:1; derivedIncrement=0.1'],
            ['s', 'WORKSHOP_PROGRESS', 'town_V1/workshop:1', 'recipe:0', 0,
             'inputs=iron:1; derivedIncrement=0.3'],
            ['s', 'STOCK_SNAPSHOT', 'town_V1', 'iron', 0, 'settlementKind=town'],
            ['s', 'SESSION_END', '', '', 0, 'reason=complete'],
        ]

    def test_opening_stock_and_maximum_progress_not_double_counted(self):
        result = self.analyze(self.sample())['rows'][0]
        self.assertEqual(result['opening_stock'], 47)
        self.assertEqual(result['observed_recipe_units'], 1)
        self.assertEqual(result['maximum_observed_daily_units'], 0.3)

    def test_unknown_stock_not_treated_as_zero(self):
        self.assertEqual(self.analyze(self.sample()[1:3]+self.sample()[-1:])['rows'], [])

    def test_live_and_mixed_sessions_rejected(self):
        with self.assertRaises(ValueError):
            self.analyze(self.sample()[:-1])
        rows = self.sample()
        rows[2][0] = 'other'
        with self.assertRaises(ValueError):
            self.analyze(rows)

    def test_changed_recipe_rejected(self):
        rows = self.sample()
        rows[2][-1] = 'inputs=iron:2; derivedIncrement=0.3'
        with self.assertRaises(ValueError):
            self.analyze(rows)

    def test_slow_industrial_recipes(self):
        self.assertEqual(module.reserves(12, 0.404, 7), (84, 10))
        self.assertEqual(module.reserves(21, 1.854, 7), (147, 13))

    def test_faster_wool_is_not_unconditionally_reduced(self):
        self.assertEqual(module.reserves(2, 2.899, 7), (14, 21))
        self.assertEqual(module.reserves(4, 6.021, 7), (28, 43))

    def test_rounding_floor_and_longer_horizon(self):
        self.assertEqual(module.reserves(0, 0, 7), (10, 10))
        self.assertEqual(module.reserves(21, 1.854, 14), (147, 26))
        self.assertEqual(module.reserves(21, 1.854, 30), (147, 56))

    def test_invalid_evidence_rejected(self):
        for value in (-1, float('nan'), float('inf')):
            with self.subTest(value=value), self.assertRaises(ValueError):
                module.reserves(12, value, 7)


if __name__ == '__main__':
    unittest.main()
