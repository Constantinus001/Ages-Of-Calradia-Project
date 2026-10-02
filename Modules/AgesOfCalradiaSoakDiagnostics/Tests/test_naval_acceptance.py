import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('acceptance', Path(__file__).with_name('Analyze-NavalAcceptance.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def row(kind, metric='', owner='', before=0, after=0, detail=''):
    return [kind, metric, owner, before, after, detail, 'session-one', 10]


class NavalAcceptanceTests(unittest.TestCase):
    def analyze(self, rows):
        stream = io.StringIO()
        writer = csv.writer(stream, delimiter='\t')
        writer.writerow(['kind', 'metric', 'owner', 'before', 'after', 'detail', 'session', 'day'])
        writer.writerows(rows)
        stream.seek(0)
        with patch('builtins.open', return_value=stream):
            return module.analyze('fixture')

    def quote(self, route, context, ship='s', amount=150):
        return row('NAVAL_POLICY_DECISION', 'quote', detail='policy_v1; revision=r1; route='+route+'; '+context+
                   '; ship='+ship+'; returnedQuote='+str(amount)+'; nativeBase=10000; effectiveBase=100; hull=10000; hullBasis=.01; alreadyDiscounted=False; basisValid=True')

    def sale(self):
        return [row('NAVAL_SALE_BEGIN', 'trade', 'hero', 100, 100, 'sale=sale1; ship=s; wallet=hero; buyer=town'),
                self.quote('sale', 'navalSale=sale1', amount=45),
                row('WALLET_CHANGE', owner='hero', before=100, after=145, detail='navalSale=sale1'),
                row('SHIP_OWNER_CHANGE', detail='navalSale=sale1; ship=s; to=settlement:town; originalRan=True'),
                row('NAVAL_SALE_END', 'trade', 'hero', 100, 145, 'sale=sale1; originalRan=True; error=none; transferred=True; owner=settlement:town')]

    def recovery(self):
        stage = 'RecoverGoldFromRemainingShipsAfterDistribution'
        return [row('REWARD_BEGIN', stage, before=100, after=100, detail='reward=r; wallet=hero'),
                self.quote('recovery', 'reward=r'),
                row('WALLET_CHANGE', owner='hero', before=100, after=250, detail='reward=r'),
                row('REWARD_END', stage, before=100, after=250, detail='reward=r; originalRan=True'),
                row('SHIP_DESTRUCTION', 'native_action_completed', detail='ship=s; ownerRemaining=False')]

    def battle(self):
        rows = [self.quote('battle', 'allocation=a', ship='b', amount=15000)]
        for party, player, effective, native in [('1', 'False', 75, 7500), ('2', 'True', 2250, 2250)]:
            rows += [row('NAVAL_POLICY_DECISION', 'allocation', detail='policy_v1; revision=r1; allocation=a; mapEvent=1; mapParty='+party+
                         '; playerClan='+player+'; applied='+('False' if player == 'True' else 'True')+
                         '; effectiveTotal='+str(effective)+'; nativeTotal='+str(native)+
                         '; effectivePool=150; priorGold=0; contribution=1; contributionSum=2; poolValid=True'),
                     row('REWARD_BEGIN', 'battle_gold', before=100, after=100, detail='reward=r'+party+'; wallet=h'+party+'; battlePaymentEligible=True; mapEvent=1; mapParty='+party),
                     row('REWARD_INPUT', 'allocated_gold', after=effective, detail='reward=r'+party),
                     row('WALLET_CHANGE', owner='h'+party, before=100, after=100+effective, detail='reward=r'+party),
                     row('REWARD_END', 'battle_gold', before=100, after=100+effective, detail='reward=r'+party+'; originalRan=True')]
        for party in ('1', '2'):
            index = next(i for i, r in enumerate(rows) if r[0] == 'REWARD_END' and 'reward=r'+party+';' in r[5])
            rows.insert(index, row('REWARD_INPUT', 'remaining_allocations', detail='reward=r'+party))
        rows += [row('SHIP_DESTRUCTION', 'native_action_completed', detail='ship=b; ownerRemaining=False')]
        return rows

    def test_sale_joins_quote_payment_and_transfer(self):
        r = self.analyze(self.sale())
        self.assertFalse(r['problems'])
        self.assertEqual(r['counts']['sale_reconciled'], 1)

    def test_wrong_wallet_not_accepted(self):
        rows = self.sale(); rows[2][2] = 'someone_else'
        self.assertTrue(self.analyze(rows)['problems'])

    def test_missing_transfer_remains_gap(self):
        rows = self.sale(); del rows[3]
        self.assertIn('sale_ship_transfer_unverified', self.analyze(rows)['coverage_gaps'])

    def test_preview_never_counted_as_payment(self):
        r = self.analyze([self.quote('sale', 'navalSale=none')])
        self.assertNotIn('sale_reconciled', r['counts'])

    def test_recovery_nested_outer_not_double_counted(self):
        rows = [row('REWARD_BEGIN', 'DistributePartyShipsAndRecoverGold', before=100, after=100, detail='reward=outer; wallet=hero')]
        rows += self.recovery()
        rows += [row('REWARD_END', 'DistributePartyShipsAndRecoverGold', before=100, after=250, detail='reward=outer; originalRan=True')]
        r = self.analyze(rows)
        self.assertFalse(r['problems'])
        self.assertEqual(r['counts']['recovery_reconciled'], 1)

    def test_recovery_cleanup_gap(self):
        self.assertIn('recovery_ship_cleanup_unverified', self.analyze(self.recovery()[:-1])['coverage_gaps'])

    def test_mixed_player_battle(self):
        r = self.analyze(self.battle())
        self.assertFalse(r['problems'])
        self.assertEqual(r['counts']['battle_reconciled'], 2)
        self.assertEqual(r['counts']['mixed_player_battles_reconciled'], 1)

    def test_player_change_is_failure(self):
        rows = self.battle()
        for r in rows:
            r[5] = r[5].replace('effectiveTotal=2250', 'effectiveTotal=2249')
        self.assertTrue(self.analyze(rows)['problems'])

    def patrol(self):
        return [row('NAVAL_POLICY_DECISION', 'allocation', detail='revision=r1; allocation=a; mapEvent=662; mapParty=1592; playerClan=False; applied=False; nativeTotal=17663; effectiveTotal=17663'),
                row('REWARD_BEGIN', 'battle_gold', detail='reward=p; wallet=patrol; recipient=none; battlePaymentEligible=False; mapEvent=662; mapParty=1592'),
                row('REWARD_INPUT', 'allocated_gold', after=17663, detail='reward=p'),
                row('REWARD_INPUT', 'remaining_allocations', detail='reward=p'),
                row('REWARD_END', 'battle_gold', detail='reward=p; originalRan=True')]

    def test_native_ineligible_patrol_is_zero_effect_not_payment(self):
        r = self.analyze(self.patrol())
        self.assertFalse(r['problems'])
        self.assertEqual(r['counts']['battle_ineligible_zero_effect_reconciled'], 1)
        self.assertNotIn('battle_reconciled', r['counts'])
        self.assertIn('battle_payment_not_reconciled', r['coverage_gaps'])

    def test_ineligible_unexpected_cash_is_failure(self):
        rows = self.patrol(); rows[-1][4] = 10
        rows.insert(-1, row('WALLET_CHANGE', owner='patrol', after=10, detail='reward=p'))
        self.assertIn('battle_ineligible_wallet_activity', self.analyze(rows)['problems'])

    def test_ineligible_offsetting_cash_is_not_zero_effect(self):
        rows = self.patrol()
        rows[-1:-1] = [row('WALLET_CHANGE', owner='patrol', after=10, detail='reward=p'),
                       row('WALLET_CHANGE', owner='patrol', before=10, detail='reward=p')]
        self.assertIn('battle_ineligible_wallet_activity', self.analyze(rows)['problems'])

    def test_missing_or_invalid_eligibility_is_gap(self):
        for value in ('', 'unknown'):
            rows = self.patrol(); rows[1][5] = rows[1][5].replace('battlePaymentEligible=False', 'battlePaymentEligible='+value)
            r = self.analyze(rows)
            self.assertIn('battle_payment_eligibility_missing', r['coverage_gaps'])
            self.assertNotIn('battle_ineligible_zero_effect_reconciled', r['counts'])

    def test_allocation_reset_required(self):
        rows = self.patrol(); del rows[3]
        self.assertIn('battle_allocation_reset_missing', self.analyze(rows)['coverage_gaps'])
        rows = self.patrol(); rows[3][4] = 17663
        self.assertIn('battle_allocations_not_reset', self.analyze(rows)['problems'])

    def test_skipped_native_commit_not_accepted(self):
        rows = self.patrol(); rows[-1][5] = 'reward=p; originalRan=False'
        r = self.analyze(rows)
        self.assertIn('battle_original_not_executed', r['coverage_gaps'])
        self.assertNotIn('battle_ineligible_zero_effect_reconciled', r['counts'])

    def test_eligible_trade_payment_requires_cash(self):
        rows = self.patrol(); rows[1][5] = rows[1][5].replace('Eligible=False', 'Eligible=True')
        self.assertIn('battle_policy_wallet_reconciliation_mismatch', self.analyze(rows)['problems'])
        rows[-1][4] = 17663
        rows.insert(-1, row('WALLET_CHANGE', owner='patrol', after=17663, detail='reward=p'))
        self.assertEqual(self.analyze(rows)['counts']['battle_reconciled'], 1)

    def test_negative_eligible_input_not_guessed(self):
        rows = self.battle(); rows[3][3] = -10
        self.assertIn('battle_negative_allocation_requires_native_review', self.analyze(rows)['coverage_gaps'])

    def test_missing_wallet_identity_not_accepted(self):
        rows = self.patrol(); rows[1][5] = rows[1][5].replace('wallet=patrol', 'wallet=none')
        self.assertIn('battle_canonical_wallet_identity_missing', self.analyze(rows)['coverage_gaps'])

    def test_native_exception_after_partial_payment(self):
        rows = self.sale(); rows[-1][5] = rows[-1][5].replace('error=none', 'error=InjectedFailure')
        self.assertTrue(self.analyze(rows)['problems'])

    def test_unfinished_active_call_gap_not_failure(self):
        r = self.analyze(self.sale()[:-1])
        self.assertFalse(r['problems'])
        self.assertIn('sale_operation_unfinished', r['coverage_gaps'])

    def test_closed_unfinished_call_failure(self):
        self.assertTrue(self.analyze(self.sale()[:-1]+[row('SESSION_END')])['problems'])

    def test_missing_receipts_never_pass(self):
        self.assertEqual(self.analyze([])['status'], 'COVERAGE_GAPS')

    def test_mixed_sessions_rejected(self):
        rows = self.sale(); rows[-1][6] = 'another-session'
        with self.assertRaises(ValueError): self.analyze(rows)

    def test_scenarios_do_not_change_settings_or_claim_cash(self):
        result = self.analyze(self.sale())['tuning_comparison']
        self.assertFalse(result['settings_changed'])
        self.assertEqual(result['basis_scenarios']['0.01']['sale_base_sum'], 100)
        self.assertEqual(result['basis_scenarios']['1.0']['sale_base_sum'], 10000)

    def test_complete_combined_receipts(self):
        rows = [row('SESSION_START'), row('NAVAL_POLICY_STATUS', detail='policy_v1; enabled; revision=r1')]
        rows += self.sale()+self.recovery()+self.battle()
        rows += [row('DIAGNOSTIC_COST', after=1, detail='discardedRows=0; pendingRows=0; bytes=1000; wallSeconds=100'), row('SESSION_END')]
        result = self.analyze(rows)
        self.assertFalse(result['problems'])
        self.assertFalse(result['coverage_gaps'])
        self.assertEqual(result['logging']['writer_wall_fraction_lower_bound'], .01)

    def test_bad_shadow_pool_is_detected(self):
        rows = self.battle(); rows[1][5] = rows[1][5].replace('effectivePool=150', 'effectivePool=151')
        self.assertIn('battle_ship_quotes_to_pool_mismatch', self.analyze(rows)['problems'])

    def test_duplicate_recovery_ship_is_detected(self):
        rows = self.recovery(); rows.insert(2, rows[1])
        self.assertTrue(self.analyze(rows)['problems'])

    def test_late_conflict_prevents_acceptance(self):
        r = self.analyze([row('NAVAL_POLICY_STATUS', detail='policy_v1; enabled;'), row('NAVAL_POLICY_STATUS', detail='policy_v1; rejected_late: conflict')])
        self.assertIn('policy_not_continuously_enabled', r['coverage_gaps'])

    def test_rows_after_end_rejected(self):
        self.assertTrue(self.analyze([row('SESSION_END')]+self.sale())['problems'])
