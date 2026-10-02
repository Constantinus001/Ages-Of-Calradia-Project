import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('wealth', Path(__file__).with_name('Analyze-HistoricalWealth.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class HistoricalWealthTests(unittest.TestCase):
    def analyze(self, rows):
        stream = io.StringIO()
        columns = ['day', 'kind', 'owner', 'source', 'count', 'positive', 'negative', 'net', 'last', 'lastDetail']
        writer = csv.DictWriter(stream, columns, delimiter='\t')
        writer.writeheader()
        writer.writerows(rows)
        stream.seek(0)
        with patch('builtins.open', return_value=stream):
            return module.analyze('fixture')

    def sample(self):
        return [
            dict(kind='SESSION_START'),
            dict(kind='CLAN_BALANCE', day=1, owner='clan', last=100, lastDetail='leader=hero'),
            dict(kind='HERO_GOLD', day=1, owner='hero', source='generic-transfer', count=2,
                 positive=50, negative=-10, net=40),
            dict(kind='HERO_GOLD', day=2, owner='hero', source='daily-finance', count=1,
                 positive=0, negative=-5, net=-5),
            dict(kind='CLAN_BALANCE', day=2, owner='clan', last=135, lastDetail='leader=hero'),
            dict(kind='SESSION_END'),
        ]

    def test_cohort_reconciles_without_relabeling_caller_purpose(self):
        result = self.analyze(self.sample())
        self.assertEqual(result['stable_cohort'], 1)
        self.assertEqual(result['snapshot_increase'], 35)
        self.assertEqual(result['observed_setter_net'], 35)
        self.assertEqual(result['endpoint_minus_summary_flows'], 0)
        self.assertEqual(result['edge_day_gross_ambiguity_bound'], 65)
        self.assertEqual(result['sources']['generic-transfer']['net'], 40)
        self.assertIn('not complete economic purposes', result['limit'])

    def test_outsiders_and_mirrored_wallets_not_counted(self):
        rows = self.sample()
        rows.insert(-1, dict(rows[2], owner='outsider', net=900, positive=900, negative=0))
        rows.insert(-1, dict(rows[2], kind='PARTY_GOLD'))
        self.assertEqual(self.analyze(rows)['observed_setter_net'], 35)

    def test_changed_leader_and_partial_window_excluded(self):
        rows = self.sample()
        rows[1:1] = [dict(rows[1], owner='changed'), dict(rows[4], owner='changed', lastDetail='leader=other'),
                     dict(rows[4], owner='partial')]
        result = self.analyze(rows)
        self.assertEqual(result['excluded_clans'], ['changed', 'partial'])
        self.assertEqual(result['stable_cohort'], 1)

    def test_duplicate_leader_rejected(self):
        rows = self.sample()
        rows[1:1] = [dict(rows[1], owner='alias'), dict(rows[4], owner='alias')]
        with self.assertRaises(ValueError): self.analyze(rows)

    def test_open_mixed_and_outside_session_rejected(self):
        rows = self.sample()
        for invalid in (rows[:-1], rows[1:], rows+rows, rows+[rows[2]], [rows[2]]+rows):
            with self.subTest(rows=invalid), self.assertRaises(ValueError): self.analyze(invalid)

    def test_invalid_aggregates_rejected(self):
        for change in ({'count': 0}, {'count': -1}, {'count': '1.5'}, {'positive': 'NaN'},
                       {'negative': 1}, {'net': 999}, {'source': ''}, {'owner': ''}, {'day': -1}):
            rows = self.sample()
            rows[2].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError): self.analyze(rows)

    def test_residual_is_reported_not_hidden(self):
        rows = self.sample()
        rows[4]['last'] = 140
        self.assertEqual(self.analyze(rows)['endpoint_minus_summary_flows'], 5)

    def test_no_cohort_or_single_day_rejected(self):
        rows = self.sample()
        for invalid in ([rows[0], rows[-1]], [dict(r, day=1) for r in rows]):
            with self.assertRaises(ValueError): self.analyze(invalid)


if __name__ == '__main__':
    unittest.main()
