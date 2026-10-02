import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('reward',Path(__file__).with_name('Analyze-RewardAccounting.py'))
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class RewardTests(unittest.TestCase):
    def test_evaluated_penalty_is_observation_not_extra_cash(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['REWARD_BEGIN',stage,'party',100,100,'reward=a; parentReward=none; wallet=hero; playerClan=True'],
              ['REWARD_PENALTY','evaluated_selling_penalty','party',0,0.8,'reward=a; originalRan=True'],
              ['REWARD_END',stage,'party',100,100,'reward=a; originalRan=True']]
        result=self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['naval_coverage']['evaluated_penalty_count'],1)
        self.assertEqual(result['rewards']['a']['wallet_delta'],0)

    def test_skipped_calls_are_not_executed_rewards(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['REWARD_BEGIN',stage,'party',100,100,'reward=a; parentReward=none; wallet=hero; playerClan=True'],
              ['REWARD_END',stage,'party',100,100,'reward=a; originalRan=False'],
              ['REWARD_SKIPPED',stage,'party',0,0,'originalRan=False']]
        result=self.analyze(rows)
        self.assertEqual(result['problems'],[])
        self.assertEqual(result['naval_coverage']['naval_scopes_observed'],0)
        self.assertEqual(result['skipped_without_scope'],1)
        self.assertEqual(result['rewards']['a']['execution_status'],'SKIPPED_NOT_A_NATIVE_REWARD')

    def test_native_ineligible_return_is_not_missing_valuation(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['REWARD_BEGIN',stage,'party',100,100,'reward=a; parentReward=none; wallet=hero; recoveryEligibility=inactive_leader'],
              ['REWARD_SHIP','before','party',1,1,'reward=a; ship=s'],
              ['REWARD_END',stage,'party',100,100,'reward=a; originalRan=True']]
        result=self.analyze(rows)
        self.assertNotIn('naval_ship_valuation_coverage_incomplete',result['coverage_gaps'])
        self.assertEqual(result['naval_coverage']['naval_original_executions_confirmed'],1)
        rows[0][-1]=rows[0][-1].replace('inactive_leader','eligible')
        self.assertIn('naval_ship_valuation_coverage_incomplete',self.analyze(rows)['coverage_gaps'])

    def test_legacy_execution_status_remains_unknown(self):
        self.assertIn('reward_original_execution_unrecorded',self.analyze(self.battle())['coverage_gaps'])

    def test_native_net_payout_request_reconciles_without_double_counting(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['REWARD_BEGIN',stage,'party',100,100,'reward=a; parentReward=none; wallet=hero; playerClan=True'],
              ['WALLET_CHANGE','Gold','hero',100,115,'reward=a'],
              ['GOLD_TRANSFER_ENDPOINT','Gold','hero',100,115,'reward=a; transfer=1; requestedTransfer=15; giverPresent=False; recipientPresent=True'],
              ['REWARD_END',stage,'party',100,115,'reward=a']]
        result=self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['rewards']['a']['wallet_delta'],15)
        self.assertEqual(result['rewards']['a']['requested_payout_cash_difference'],0)
        self.assertEqual(result['naval_coverage']['player_naval_scopes_observed'], 1)
        self.assertEqual(result['naval_coverage']['player_penalty_status'],
                         'NATIVE_RESULT_OBSERVED_FORMULA_NOT_INDEPENDENTLY_CERTIFIED')
        rows[2][-1]=rows[2][-1].replace('requestedTransfer=15','requestedTransfer=20')
        self.assertIn('naval_requested_payout_cash_difference',self.analyze(rows)['coverage_gaps'])

    def test_naval_coverage_marks_unexercised_player_and_ship_paths(self):
        result=self.analyze(self.battle())
        self.assertEqual(result['naval_coverage']['player_penalty_status'], 'NOT_EXERCISED')
        self.assertEqual(result['naval_coverage']['ship_valuation_status'], 'NOT_EXERCISED')

    def test_ship_cleanup_after_reward_scope_is_joined(self):
        stage='RecoverGoldFromRemainingShipsAfterDistribution'
        rows=[['REWARD_BEGIN',stage,'party',100,100,'reward=a; parentReward=none; wallet=hero'],
              ['REWARD_SHIP','before','party',1,1,'reward=a; ship=s'],
              ['REWARD_VALUATION','native_ship_value','party',0,20,'reward=a; ship=s'],
              ['WALLET_CHANGE','Gold','hero',100,120,'reward=a'],
              ['REWARD_END',stage,'party',100,120,'reward=a']]
        self.assertIn('naval_ship_cleanup_missing',self.analyze(rows)['coverage_gaps'])
        rows.append(['SHIP_DESTRUCTION','native_action_completed','ship:s',0,1,'ship=s; ownerRemaining=False; reward=none'])
        result=self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['rewards']['a']['valued_ship_cleanup']['s'],'later_native_destruction_completed_owner_cleared')
        rows[-1][-1]='ship=s; ownerRemaining=True'
        self.assertIn('naval_ship_owner_retained_requires_review',self.analyze(rows)['coverage_gaps'])

    def analyze(self, rows):
        stream=io.StringIO()
        writer=csv.writer(stream,delimiter='\t')
        writer.writerow(['kind','metric','owner','before','after','detail'])
        writer.writerows(rows)
        with patch('builtins.open',return_value=io.StringIO(stream.getvalue())):
            return module.analyze('fixture')

    def battle(self):
        return [
            ['REWARD_BEGIN','battle_gold','party',100,100,'reward=a; parentReward=none; wallet=hero; battlePaymentEligible=True'],
            ['REWARD_INPUT','allocated_gold','party',20,50,'reward=a'],
            ['WALLET_CHANGE','Gold','hero',100,130,'reward=a'],
            ['REWARD_INPUT','remaining_allocations','party',0,0,'reward=a'],
            ['REWARD_END','battle_gold','party',100,130,'reward=a'],
        ]

    def test_battle_allocation_and_wallet_reconcile(self):
        result=self.analyze(self.battle())
        self.assertFalse(result['problems'])
        self.assertEqual(result['rewards']['a']['wallet_residual'],0)
        self.assertEqual(result['rewards']['a']['allocation_difference_requires_clamp_review'],0)

    def test_missing_wallet_change_not_hidden(self):
        rows=self.battle();del rows[2]
        self.assertIn('Reward wallet residual: a',self.analyze(rows)['problems'])

    def test_nested_context_not_double_income(self):
        rows=[
            ['REWARD_BEGIN','outer','party',100,100,'reward=a; parentReward=none; wallet=hero'],
            ['REWARD_BEGIN','inner','party',100,100,'reward=b; parentReward=a; wallet=hero'],
            ['WALLET_CHANGE','Gold','hero',100,120,'reward=b'],
            ['REWARD_END','inner','party',100,120,'reward=b'],
            ['REWARD_END','outer','party',100,120,'reward=a'],
        ]
        result=self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['rewards']['a']['wallet_delta'],20)
        self.assertEqual(result['rewards']['b']['wallet_delta'],20)
        self.assertTrue(result['coverage_gaps'])

    def test_unclosed_and_no_events_are_not_coverage(self):
        self.assertTrue(self.analyze(self.battle()[:-1])['problems'])
        self.assertIn('reward_events_not_exercised',self.analyze([])['coverage_gaps'])

    def test_unknown_input_cannot_substitute_for_reset(self):
        rows=self.battle(); rows[3][1]='unexpected'
        result=self.analyze(rows)
        self.assertIn('Unknown battle input metric',result['problems'])
        self.assertIn('battle_allocation_or_eligibility_missing',result['coverage_gaps'])

    def test_missing_ship_and_unknown_records_are_rejected(self):
        for kind,metric,detail in [('REWARD_SHIP','before','reward=a'),
                                   ('REWARD_SHIP','unknown','reward=a; ship=s'),
                                   ('REWARD_UNKNOWN','unknown','reward=a')]:
            rows=self.battle(); rows.insert(1,[kind,metric,'party',0,0,detail])
            self.assertTrue(self.analyze(rows)['problems'])

    def test_duplicate_valuation_does_not_overwrite_first_evidence(self):
        rows=self.battle()
        rows[1:1]=[['REWARD_VALUATION','value','party',0,10,'reward=a; ship=s'],
                   ['REWARD_VALUATION','value','party',0,99,'reward=a; ship=s']]
        result=self.analyze(rows)
        self.assertTrue(result['problems'])
        self.assertEqual(result['rewards']['a']['values']['s'],10)

    def test_native_membership_metric_is_accepted(self):
        rows=self.battle()
        rows.insert(1,['REWARD_SHIP','after_membership','party',1,0,'reward=a; ship=s'])
        result=self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertEqual(result['rewards']['a']['ships']['s']['after_membership']['after'],0)
