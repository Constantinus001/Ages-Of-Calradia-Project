import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('four', Path(__file__).with_name('Analyze-FourEconomyCases.py'))
four = importlib.util.module_from_spec(spec)
spec.loader.exec_module(four)


class FourCasesTest(unittest.TestCase):
    def test_harmony_frames_preserve_native_attribution(self):
        self.assertEqual(four.observed_caller('.TaleWorlds.CampaignSystem.Hero.set_Gold_Patch4|.TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel.AddPartyExpense_Patch3'),
                         'TaleWorlds.CampaignSystem.GameComponents.DefaultClanFinanceModel.AddPartyExpense_Patch3')

    def test_naval_caller_not_masked_by_dispatch(self):
        self.assertEqual(four.observed_caller('.TaleWorlds.CampaignSystem.Hero.ChangeHeroGold_Patch1|.TaleWorlds.CampaignSystem.Actions.GiveGoldAction.ApplyInternal_Patch4|TaleWorlds.CampaignSystem.MbEvent`1.InvokeList|.NavalDLC.Behavior.RecoverShips'),
                         'NavalDLC.Behavior.RecoverShips')

    def test_missing_cause_is_not_invented(self):
        chain = '.TaleWorlds.CampaignSystem.Hero.set_Gold_Patch4|TaleWorlds.CampaignSystem.MbEvent`1.InvokeList'
        self.assertEqual(four.observed_caller(chain), 'unattributed_chain:' + chain)

    def test_signed_margins_navigation_and_missing_evidence(self):
        rows = [
            ['SESSION_START', 'supply_v6', 's', 0, 0, ''],
            ['CARAVAN_BEGIN', 'route', 'p', 0, 0, 'decision=1; cargo=wool:2'],
            ['ROUTE_CANDIDATE_BEGIN', 'native', 'p', 0, 0, 'decision=1; town=town_V7; distanceCut=True'],
            ['ROUTE_NAVIGATION', 'Default', 'p', 0, 500, 'decision=1; town=town_V7; fromPort=False'],
            ['ROUTE_SCORE', 'native_final', 'p', 0, -1, 'decision=1; town=town_V7'],
            ['CARAVAN_END', 'selected=t', 'p', 0, 0, 'decision=1'],
            ['WORKSHOP_GATE', 'CanNotable:rejected', 'a', 0, 0, 'inputs=sheep:1; inputCost=80; outputIncome=20; nativeProfitHurdle=80; effectCapital=True; townGold=0'],
            ['WORKSHOP_GATE', 'CanNotable:accepted', 'b', 0, 1, 'inputs=sheep:1; inputCost=10; outputIncome=20; nativeProfitHurdle=10; effectCapital=True; townGold=50'],
            ['WALLET_CHECK', 'Gold', 'Hero/1', 0, 42, ''],
            ['SESSION_END', 'done', 's', 0, 0, ''],
        ]
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'fixture.tsv'
            with path.open('w', newline='') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['kind', 'metric', 'owner', 'before', 'after', 'detail'])
                writer.writerows(rows)
            result = four.analyze(str(path))
        self.assertEqual(result['sheep_totals']['quoted_margin_sum'], -50)
        self.assertEqual(result['sheep_totals']['profit'], 1)
        self.assertEqual(result['charas']['negative/navigation/Default'], 1)
        self.assertEqual(result['charas']['negative/distanceCut=True'], 1)
        self.assertEqual(result['hero_residual_sum_not_inflation_proof'], 42)
        self.assertEqual(result['hero_observed_sources_not_missing_source_proof'], {})
        self.assertEqual(result['open_routes'], 0)


if __name__ == '__main__': unittest.main()
