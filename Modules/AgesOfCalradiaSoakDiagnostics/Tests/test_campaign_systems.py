import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('systems', Path(__file__).with_name('Analyze-CampaignSystems.py'))
systems = importlib.util.module_from_spec(spec)
spec.loader.exec_module(systems)


class CampaignSystemsTests(unittest.TestCase):
    def analyze(self, edits=None):
        def row(kind, metric='', before=10, after=10, detail=''):
            return dict(session='fixture', day=10, kind=kind, owner='quest', metric=metric,
                        before=before, after=after, detail=detail)
        rows = [row('SESSION_START'), row('CORE_SYSTEMS', 'valid', detail='coreMvid=11111111-1111-1111-1111-111111111111; policyRevision=' + 'a'*64),
                row('QUEST_DEADLINE', 'finite', after=13, detail='remainingDays=3'),
                row('QUEST_DEADLINE', 'never'), row('QUEST_COVERAGE', 'observed', 2, 2), row('SESSION_END')]
        if edits:
            edits(rows)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.tsv'
            with path.open('w', newline='', encoding='utf-8') as stream:
                writer = csv.DictWriter(stream, fieldnames=rows[0], delimiter='\t')
                writer.writeheader()
                writer.writerows(rows)
            return systems.analyze(path)

    def test_observed(self):
        result = self.analyze()
        self.assertEqual(result['problems'], [])
        self.assertEqual(result['coverage_gaps'], [])
        self.assertEqual(result['never_quest_observations'], 1)

    def test_rejected(self):
        self.assertIn('Core status: configuration_rejected', self.analyze(lambda r: r[1].update(metric='configuration_rejected'))['problems'])

    def test_remaining_mismatch(self):
        self.assertTrue(self.analyze(lambda r: r[2].update(detail='remainingDays=6'))['problems'])

    def test_nonfinite(self):
        with self.assertRaises(ValueError):
            self.analyze(lambda r: r[2].update(after='NaN'))

    def test_no_quests_not_pass(self):
        def change(rows):
            del rows[2:4]
            rows[2].update(metric='none_active', before=0, after=0)
        self.assertIn('finite_quest_deadlines_not_exercised', self.analyze(change)['coverage_gaps'])

    def test_incomplete(self):
        self.assertTrue(self.analyze(lambda r: r.pop())['problems'])

    def test_policy_changed(self):
        def change(rows):
            newer = dict(rows[1])
            newer['detail'] = newer['detail'].replace('a'*64, 'b'*64)
            rows.insert(2, newer)
        self.assertTrue(self.analyze(change)['problems'])

    def test_deadline_change_reported_not_invented_bug(self):
        def change(rows):
            newer = dict(rows[2], after=15, detail='remainingDays=5')
            rows.insert(3, newer)
        result = self.analyze(change)
        self.assertEqual(result['problems'], [])
        self.assertEqual(len(result['deadline_changes_require_context_not_automatic_bugs']), 1)
