"""Synthetic contract fixtures, not in-game acceptance."""
import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("supply_analysis", Path(__file__).with_name("Analyze-SupplyCapture.py"))
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)
journey_spec = importlib.util.spec_from_file_location("supply_journeys", Path(__file__).with_name("Classify-SupplyJourneys.py"))
journeys = importlib.util.module_from_spec(journey_spec)
journey_spec.loader.exec_module(journeys)


class SupplyAnalysisTest(unittest.TestCase):
    def test_corrupt_wool_values_cannot_pass(self):
        result=self.run_rows([
            ('SESSION_START',0,'supply_v5',0,0,''),
            ('WOOL_SELL_BEGIN',0,'cargo',30,30,'decision=1'),
            ('WOOL_SELL_INDEX',0,'available',0,960,'decision=1; average=0; minimum=0'),
            ('WOOL_SELL_QUANTITY',0,'native_randomized_before_caps',0,-2147483647,'decision=1'),
            ('WOOL_SELL_END',0,'cargo',30,30,'decision=1'),
            ('SESSION_END',0,'fixture_completed',0,0,'')])
        self.assertEqual(result['integrity'],'FAILED_OR_INCOMPLETE')
        self.assertTrue(any('price index' in p for p in result['problems']))
        self.assertTrue(any('negative' in p for p in result['problems']))

    def test_wool_decision_outcomes_and_closure(self):
        result = self.run_rows([
            ("SESSION_START", 0, "supply_v5", 0, 0, ""),
            ("WOOL_SELL_BEGIN", 0, "cargo", 8, 8, "decision=1"),
            ("WOOL_SELL_END", 0, "cargo", 8, 8, "decision=1"),
            ("WOOL_SELL_BEGIN", 0, "cargo", 0, 0, "decision=2"),
            ("WOOL_SELL_END", 0, "cargo", 0, 0, "decision=2"),
            ("WOOL_SELL_BEGIN", 0, "cargo", 8, 8, "decision=3"),
            ("WOOL_SELL_END", 0, "cargo", 8, 3, "decision=3"),
            ("SESSION_END", 0, "fixture_completed", 0, 0, ""),
        ])
        self.assertEqual(result['wool_sale_decision_outcomes'], {'retained_cargo_inspect_values':1,'no_cargo':1,'sold_some':1})
        self.assertIn('WOOL_SELL_QUANTITY', result['not_exercised'])
        result=self.run_rows([('SESSION_START',0,'supply_v5',0,0,''),('WOOL_SELL_BEGIN',0,'cargo',1,1,'decision=1')])
        self.assertTrue(any('unclosed wool' in p for p in result['problems']))

    def test_market_lineage_and_unknown_schema(self):
        result = self.run_rows([
            ("SESSION_START", 0, "supply_v4", 0, 0, ""),
            ("MARKET_BEGIN", 0, "caravan_export", 0, 0, "market=1; parentMarket=0"),
            ("MARKET_DELTA", 0, "wool", 20, 12, "market=1; parentMarket=0; operation=caravan_export"),
            ("MARKET_END", 0, "caravan_export", 0, 0, "market=1; parentMarket=0"),
            ("SESSION_END", 0, "fixture_completed", 0, 0, ""),
        ])
        self.assertEqual(result["market_root_endpoint_deltas_not_cash_conservation"], {"caravan_export/wool": -8})
        self.assertIn("MARKET_MODELS", result["not_exercised"])
        result = self.run_rows([("SESSION_START", 0, "supply_v99", 0, 0, ""), ("SESSION_END", 0, "fixture_completed", 0, 0, "")])
        self.assertTrue(any("Unsupported supply schema" in p for p in result["problems"]))

    def test_unclosed_market_is_incomplete(self):
        result = self.run_rows([("SESSION_START", 0, "supply_v4", 0, 0, ""), ("MARKET_BEGIN", 0, "MakeConsumption", 0, 0, "market=1; parentMarket=0")])
        self.assertTrue(any("unclosed market" in p for p in result["problems"]))

    def run_rows(self, records):
        with tempfile.TemporaryDirectory(prefix="aoc-supply-analysis-") as folder:
            path = Path(folder) / "fixture.tsv"
            with path.open("w", newline="", encoding="utf-8") as stream:
                writer = csv.writer(stream, delimiter="\t")
                writer.writerow("utc session sequence day kind scope parent owner metric before after detail".split())
                for i, (kind, scope, metric, before, after, detail) in enumerate(records, 1):
                    writer.writerow(["fixture", "one", i, 42.5, kind, scope, 0, "village", metric, before, after, detail])
            self.journey_result = journeys.classify(path)
            return analysis.analyze(path)

    def test_journey_classes_exclusive_and_instance_specific(self):
        self.run_rows([
            ("END", 1, "MoveItemsToVillagerParty", 0, 0, "party=p/instance:1; trip=1"),
            ("END", 2, "MoveItemsToVillagerParty", 0, 0, "party=p/instance:1; trip=2"),
            ("BEGIN", 2, "OnMobilePartyDestroyed", 0, 0, "party=p/instance:1; trip=2"),
            ("END", 3, "MoveItemsToVillagerParty", 0, 0, "party=p/instance:2; trip=3"),
            ("SESSION_END", 0, "fixture_completed", 0, 0, ""),
        ])
        self.assertEqual(self.journey_result["unmatched_exclusive_counts"], {
            "subsequent_load_same_party": 1, "destruction_callback": 1, "recent_load_under_two_days": 1})

    def test_workshop_missing_and_unclosed_cycles_are_visible(self):
        result = self.run_rows([
            ("SESSION_START", 0, "supply_v3", 0, 0, ""),
            ("WORKSHOP_ATTEMPT", 0, "begin", 0, 0, "cycle=4"),
            ("WORKSHOP_CONSUMED", 0, "wool", 0, 2, "cycle=4"),
            ("WORKSHOP_FLOW_CHECK", 0, "wool", -2, -1, "cycle=4"),
            ("SESSION_END", 0, "byte_limit_incomplete", 0, 0, ""),
        ])
        self.assertIn("WORKSHOP_PRODUCED", result["not_exercised"])
        self.assertTrue(any("unclosed workshop" in x for x in result["problems"]))
        self.assertTrue(any("Nonzero WORKSHOP_FLOW_CHECK" in x for x in result["problems"]))
        self.assertEqual(result["workshop_event_units_compare_roster_deltas"], {"WORKSHOP_CONSUMED/wool": 2})

    def test_no_double_count_of_nested_inventory_or_totals(self):
        result = self.run_rows([
            ("SESSION_START", 0, "supply_v1", 0, 0, ""),
            ("BEGIN", 1, "MoveItemsToVillagerParty", 0, 0, "trip=1; sinceLoadDays=0"),
            ("SCOPE_BALANCE", 1, "party/wool", 0, 10, ""),
            ("SCOPE_BALANCE", 1, "party/total_all", 0, 10, ""),
            ("TRANSFER_CHECK", 1, "wool", 0, 0, ""),
            ("END", 1, "MoveItemsToVillagerParty", 0, 0, "trip=1; sinceLoadDays=0"),
            ("BEGIN", 2, "ApplyInternal", 0, 0, "trip=1; sinceLoadDays=1.5"),
            ("SCOPE_BALANCE", 2, "town/wool", 4, 14, ""),
            ("SCOPE_BALANCE", 2, "party/wool", 10, 0, ""),
            ("CASH_CHECK", 2, "gold", 0, 0, ""),
            ("END", 2, "ApplyInternal", 0, 0, ""),
            ("SESSION_END", 0, "fixture_completed", 0, 0, ""),
        ])
        self.assertEqual(result["loaded_endpoint_net_units"], {"wool": 10})
        self.assertEqual(result["sold_town_endpoint_net_units"], {"wool": 10})
        self.assertEqual(result["mean_load_to_sale_entry_campaign_days"], 1.5)
        self.assertEqual(result["problems"], [])
        self.assertIn("ITEM_PRODUCED", result["not_exercised"])

    def test_open_capture_and_residual_cannot_pass(self):
        result = self.run_rows([
            ("SESSION_START", 0, "supply_v1", 0, 0, ""),
            ("BEGIN", 1, "MoveItemsToVillagerParty", 0, 0, ""),
            ("TRANSFER_CHECK", 1, "hog", 0, 3, ""),
        ])
        self.assertEqual(result["integrity"], "FAILED_OR_INCOMPLETE")
        self.assertTrue(any("Nonzero" in x for x in result["problems"]))
        self.assertTrue(any("unclosed" in x for x in result["problems"]))


if __name__ == "__main__":
    unittest.main()
