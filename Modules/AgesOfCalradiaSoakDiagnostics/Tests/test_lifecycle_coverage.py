import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('coverage',Path(__file__).with_name('Analyze-LifecycleCoverage.py'))
module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)


class LifecycleTests(unittest.TestCase):
    def analyze(self,rows):
        stream=io.StringIO();writer=csv.writer(stream,delimiter='\t')
        writer.writerow(['kind','metric','owner','detail']);writer.writerows(rows)
        with patch('builtins.open',return_value=io.StringIO(stream.getvalue())):
            return module.analyze('fixture')

    def test_dispatch_only_does_not_cover_procurement_lifecycle(self):
        result=self.analyze([['FEATURE_COVERAGE','present','procurement'],['PROCUREMENT_MOVEMENT','dispatch','s']])
        missing=result['features']['procurement']['not_exercised']
        for stage in ('arrival','consumption','return','liquidation'):
            self.assertIn('procurement/'+stage,missing)
        self.assertNotIn('procurement/dispatch',missing)

    def test_battle_does_not_cover_enabled_naval_and_absence_not_failure(self):
        rows=[['FEATURE_COVERAGE','present','naval_rewards'],['REWARD_END','battle_gold','party']]
        result=self.analyze(rows)
        self.assertTrue(result['features']['naval_rewards']['not_exercised'])
        rows[0][1]='absent'
        result=self.analyze(rows)
        self.assertFalse(result['features']['naval_rewards']['not_exercised'])
        self.assertFalse(result['problems'])

    def test_unknown_and_contradictory_availability_are_visible(self):
        self.assertIn('procurement:availability_unknown',self.analyze([])['coverage_gaps'])
        result=self.analyze([['FEATURE_COVERAGE','absent','procurement'],['PROCUREMENT_MOVEMENT','arrival','s']])
        self.assertTrue(result['problems'])

    def test_skipped_and_legacy_rewards_do_not_cover_executed_branch(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['FEATURE_COVERAGE','present','naval_rewards',''],
              ['REWARD_END',stage,'party','originalRan=False'],
              ['REWARD_END',stage,'party','']]
        self.assertIn('reward/'+stage,self.analyze(rows)['features']['naval_rewards']['not_exercised'])
        rows.append(['REWARD_END',stage,'party','originalRan=True'])
        self.assertEqual(self.analyze(rows)['features']['naval_rewards']['observed']['reward/'+stage],1)
