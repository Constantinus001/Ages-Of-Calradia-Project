import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('allocation', Path(__file__).with_name('Analyze-BattleAllocations.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class BattleAllocationTests(unittest.TestCase):
    def analyze(self, rows):
        stream = io.StringIO()
        writer = csv.writer(stream, delimiter='\t')
        writer.writerow(['kind', 'metric', 'before', 'after', 'detail'])
        writer.writerows(rows)
        stream.seek(0)
        with patch('builtins.open', return_value=stream):
            return module.analyze('fixture')

    def sample(self):
        context = 'allocation=a; mapEvent=1; stage=LootDefeatedPartyShips; '
        return [
            ['BATTLE_ALLOCATION_BEGIN', 'scope', 0, 0, context],
            ['BATTLE_ALLOCATION_PARTY', 'before', 0, 10, context+'mapParty=1; role=winner'],
            ['BATTLE_ALLOCATION_VALUE', 'evaluated_trade_value', 0, 100, context+'ship=1; originalRan=True'],
            ['BATTLE_ALLOCATION_PARTY', 'after', 0, 110, context+'mapParty=1; role=winner'],
            ['BATTLE_ALLOCATION_END', 'scope', 0, 0, context+'originalRan=True'],
            ['REWARD_BEGIN', 'battle_gold', 500, 500, 'reward=r; mapEvent=1; mapParty=1'],
            ['REWARD_INPUT', 'allocated_gold', 0, 110, 'reward=r'],
            ['REWARD_INPUT', 'remaining_allocations', 0, 0, 'reward=r'],
            ['REWARD_END', 'battle_gold', 500, 610, 'reward=r; originalRan=True'],
            ['SESSION_END', '', 0, 0, ''],
        ]

    def test_delta_separate_from_full_payout(self):
        result = self.analyze(self.sample())
        self.assertFalse(result['problems'])
        self.assertEqual(result['joined_battle_commits'], 1)
        self.assertEqual(result['allocation_deltas_by_stage']['LootDefeatedPartyShips']['allocated_gain_delta'], 100)
        self.assertEqual(result['evaluated_ship_values'], 1)

    def test_mismatch_detected(self):
        rows = self.sample()
        rows[6][3] = 111
        self.assertTrue(self.analyze(rows)['problems'])

    def test_older_capture_is_gap_not_pass(self):
        result = self.analyze(self.sample()[5:])
        self.assertEqual(result['unjoined_battle_commits'], 1)
        self.assertTrue(result['coverage_gaps'])
        self.assertFalse(result['problems'])

    def test_live_open_scope_not_integrity_failure(self):
        result = self.analyze(self.sample()[:3])
        self.assertFalse(result['problems'])
        self.assertEqual(result['open_scopes'], 1)
        self.assertIn('allocation_scope_still_open', result['coverage_gaps'])

    def test_closed_open_scope_is_failure(self):
        self.assertTrue(self.analyze(self.sample()[:3]+[self.sample()[-1]])['problems'])

    def test_duplicate_snapshot_rejected(self):
        rows = self.sample()
        rows.insert(2, rows[1])
        self.assertTrue(self.analyze(rows)['problems'])

    def test_skipped_allocation_is_not_proven(self):
        rows = self.sample()
        rows[4][-1] = rows[4][-1].replace('True', 'False')
        result = self.analyze(rows)
        self.assertFalse(result['allocation_deltas_by_stage'])
        self.assertEqual(result['unjoined_battle_commits'], 1)

    def test_nonfinite_rejected(self):
        rows = self.sample()
        rows[2][3] = 'NaN'
        with self.assertRaises(ValueError):
            self.analyze(rows)

    def test_unfinished_last_line_is_not_integrity_failure(self):
        stream = io.StringIO('kind\tmetric\tbefore\tafter\tdetail\nBATTLE_ALLOCATION_BEGIN\tscope\t0\t')
        with patch('builtins.open', return_value=stream):
            result = module.analyze('fixture')
        self.assertFalse(result['problems'])
        self.assertIn('unfinished_last_line_not_evaluated', result['coverage_gaps'])

    def test_skipped_without_prefix_is_not_an_executed_scope(self):
        result = self.analyze([['BATTLE_ALLOCATION_SKIPPED', 'stage', 0, 0, 'originalRan=False']])
        self.assertFalse(result['problems'])
        self.assertEqual(result['skipped_without_scope'], 1)
        self.assertEqual(result['allocation_scopes'], 0)

    def test_reset_allows_later_zero_commit_without_double_counting(self):
        rows = self.sample()
        rows[-1:-1] = [
            ['REWARD_BEGIN', 'battle_gold', 610, 610, 'reward=s; mapEvent=1; mapParty=1'],
            ['REWARD_INPUT', 'allocated_gold', 0, 0, 'reward=s'],
            ['REWARD_END', 'battle_gold', 610, 610, 'reward=s; originalRan=True'],
        ]
        result = self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['joined_battle_commits'], 2)

    def test_resets_cannot_create_missing_upstream_evidence(self):
        contexts = ['', 'mapEvent=1', 'mapParty=1',
                    'mapEvent=unobserved; mapParty=1', 'mapEvent=0; mapParty=1',
                    'mapEvent=1; mapParty=1']
        for context in contexts:
            with self.subTest(context=context):
                rows = []
                for index, amount in enumerate([5, 0, 9]):
                    reward = 'reward=r' + str(index)
                    rows.extend([
                        ['REWARD_BEGIN', 'battle_gold', 0, 0, reward+'; '+context],
                        ['REWARD_INPUT', 'allocated_gold', 0, amount, reward],
                        ['REWARD_INPUT', 'remaining_allocations', 0, 0, reward],
                        ['REWARD_END', 'battle_gold', 0, amount, reward+'; originalRan=True'],
                    ])
                rows.append(['SESSION_END', '', 0, 0, ''])
                result = self.analyze(rows)
                self.assertFalse(result['problems'])
                self.assertEqual(result['joined_battle_commits'], 0)
                self.assertEqual(result['unjoined_battle_commits'], 3)
                self.assertIn('battle_commit_without_complete_allocation_observation', result['coverage_gaps'])

    def test_invalid_upstream_identities_do_not_verify_commits(self):
        for field in ['mapEvent', 'mapParty']:
            for value in ['0', '-1', 'unobserved', '1.0', '١']:
                with self.subTest(field=field, value=value):
                    rows = self.sample()
                    for row in rows:
                        row[-1] = row[-1].replace(field+'=1', field+'='+value)
                    result = self.analyze(rows)
                    self.assertTrue(result['problems'])
                    self.assertEqual(result['joined_battle_commits'], 0)

    def test_valid_mismatch_still_detected_after_reset(self):
        rows = self.sample()
        rows[-1:-1] = [
            ['REWARD_BEGIN', 'battle_gold', 610, 610, 'reward=s; mapEvent=1; mapParty=1'],
            ['REWARD_INPUT', 'allocated_gold', 0, 9, 'reward=s'],
            ['REWARD_END', 'battle_gold', 610, 619, 'reward=s; originalRan=True'],
        ]
        result = self.analyze(rows)
        self.assertEqual(result['joined_battle_commits'], 1)
        self.assertEqual(len(result['problems']), 1)


if __name__ == '__main__':
    unittest.main()
