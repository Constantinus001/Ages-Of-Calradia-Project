"""Synthetic v6 coverage contracts; no claim of live acceptance."""
import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("broad", Path(__file__).with_name("Analyze-BroadSupplyCapture.py"))
broad = importlib.util.module_from_spec(spec)
spec.loader.exec_module(broad)


class BroadTest(unittest.TestCase):
    def test_competing_demand_separates_causes_without_nested_double_count(self):
        result = self.run_rows([
            ['SESSION_START','supply_v7','s',0,0,''],
            ['MARKET_DELTA','wool','town',20,17,'parentMarket=0; settlement=town; operation=MakeConsumption'],
            ['MARKET_DELTA','wool','town',20,17,'parentMarket=1; settlement=town; operation=GetFoodFromMarketInternal'],
            ['MARKET_DELTA','wool','town',17,15,'parentMarket=0; settlement=town; operation=other_party_export'],
            ['WORKSHOP_STOCK_DELTA','market/wool','town/shop',15,11,'recipe=1'],
            ['WORKSHOP_CONSUMED','wool','town/shop',0,6,'recipe=1'],
            ['SESSION_END','done','s',0,15,''],
        ])
        causes=result['stock_change_causes']['town/wool']
        self.assertEqual(causes, {'MakeConsumption':-3,'other_party_export':-2,'workshop_market_net':-4})
        self.assertEqual(sum(causes.values()),-9)
        self.assertEqual(result['workshop_input_events_by_recipe_not_additive_market_flows']['town/shop/1']['wool'],6)

    def test_malformed_transfer_contract_is_not_balanced(self):
        for count in ('oops', '0', '-1'):
            result = self.run_rows([
                ['SESSION_START', 'supply_v7', 's', 0, 0, ''],
                ['GOLD_TRANSFER_ENDPOINT', 'Gold', 'a', 10, 10,
                 f'transfer=1; expectedEndpoints={count}; giverPresent=True; recipientPresent=True'],
                ['SESSION_END', 'done', 's', 0, 15, ''],
            ])
            receipt = result['transfer_receipts_not_additive_income']
            self.assertTrue(receipt['problems'])
            self.assertEqual(receipt['by_id']['1']['interpretation'], 'invalid_endpoint_contract_requires_investigation')

    def test_conflicting_transfer_contract_is_not_balanced(self):
        first = 'transfer=1; expectedEndpoints=2; giverPresent=True; recipientPresent=True; requestedTransfer=30'
        for second in (first.replace('30', '40'), first.replace('giverPresent=True', 'giverPresent=False')):
            result = self.run_rows([
                ['SESSION_START', 'supply_v7', 's', 0, 0, ''],
                ['GOLD_TRANSFER_ENDPOINT', 'Gold', 'a', 30, 0, first],
                ['GOLD_TRANSFER_ENDPOINT', 'Gold', 'b', 0, 30, second],
                ['SESSION_END', 'done', 's', 0, 15, ''],
            ])
            receipt = result['transfer_receipts_not_additive_income']['by_id']['1']
            self.assertEqual(receipt['endpoint_net'], 0)
            self.assertEqual(receipt['interpretation'], 'invalid_endpoint_contract_requires_investigation')

    def test_missing_presence_is_unknown_not_external_finance(self):
        result = self.run_rows([
            ['SESSION_START', 'supply_v7', 's', 0, 0, ''],
            ['GOLD_TRANSFER_ENDPOINT', 'Gold', 'a', 0, 10, 'transfer=1; expectedEndpoints=1'],
            ['SESSION_END', 'done', 's', 0, 15, ''],
        ])
        self.assertEqual(result['transfer_receipts_not_additive_income']['by_id']['1']['interpretation'],
                         'incomplete_endpoint_contract_unknown_source_or_sink')

    def test_transfer_context_is_not_added_to_income(self):
        detail = "transfer=1; requestedTransfer=30; giverHero=a; recipientHero=b; callers=native"
        result = self.run_rows([
            ["SESSION_START", "supply_v7", "s", 0, 0, ""],
            ["WALLET_CHANGE", "Gold", "a", 100, 70, "source=native"],
            ["WALLET_CHANGE", "Gold", "b", 0, 30, "source=native"],
            ["GOLD_TRANSFER_ENDPOINT", "Gold", "a", 100, 70, detail],
            ["GOLD_TRANSFER_ENDPOINT", "Gold", "b", 0, 30, detail],
            ["SESSION_END", "done", "s", 0, 15, ""],
        ])
        self.assertEqual(result["cash_caller_chains"]["Gold/native"], [2, 0])
        self.assertEqual(result["transfer_receipts_not_additive_income"]["by_id"]["1"]["endpoint_net"], 0)
        self.assertEqual(result["transfer_receipts_not_additive_income"]["problems"], [])

    def test_transfer_duplicate_and_missing_id_are_visible(self):
        result = self.run_rows([
            ["SESSION_START", "supply_v7", "s", 0, 0, ""],
            ["GOLD_TRANSFER_ENDPOINT", "Gold", "a", 0, 10, "transfer=1"],
            ["GOLD_TRANSFER_ENDPOINT", "Gold", "a", 0, 10, "transfer=1"],
            ["GOLD_TRANSFER_ENDPOINT", "Gold", "b", 0, 10, ""],
            ["SESSION_END", "done", "s", 0, 15, ""],
        ])
        receipts = result["transfer_receipts_not_additive_income"]
        self.assertEqual(len(receipts["problems"]), 2)
        self.assertEqual(receipts["by_id"]["1"]["endpoint_net"], 10)

    def test_receipt_completeness_and_external_finance_are_not_conflated(self):
        for expected, giver, net, status in [
            (2, 'True', 10, 'incomplete_endpoints_live_or_truncated'),
            (1, 'False', 10, 'external_source_or_sink_requires_caller_attribution_not_inflation_proof'),
            (1, 'True', 0, 'balanced_gross_endpoints_not_additive_income'),
            (1, 'True', 10, 'unbalanced_gross_endpoints_investigate_nested_callbacks'),
        ]:
            result = self.run_rows([
                ['SESSION_START', 'supply_v7', 's', 0, 0, ''],
                ['GOLD_TRANSFER_ENDPOINT', 'Gold', 'a', 0, net,
                 f'transfer=1; expectedEndpoints={expected}; giverPresent={giver}; recipientPresent=True'],
                ['SESSION_END', 'done', 's', 0, 15, ''],
            ])
            self.assertEqual(result['transfer_receipts_not_additive_income']['by_id']['1']['interpretation'], status)

    def test_canonical_wallet_dormancy_and_reactivation(self):
        rows = [
            ["SESSION_START", "supply_v7", "s", 0, 0, ""],
            ["WALLET_CHECK", "PartyTradeGold", "party", 100, 120, ""],
            ["WALLET_ALIAS", "canonical", "party", 0, 0, "target=hero"],
            ["WALLET_DORMANT", "PartyTradeGold", "party", 0, 0, ""],
            ["WALLET_CHECK", "Gold", "hero", 100, 100, ""],
        ]
        end = ["SESSION_END", "done", "s", 0, 15, ""]
        result = self.run_rows(rows + [end])
        self.assertEqual(result["problems"], [])
        self.assertEqual(result["wallet_unexplained_final_residuals"], {})
        self.assertEqual(result["wallet_semantics"], "canonical_net_of_nested")
        result = self.run_rows(rows + [["WALLET_CHECK", "PartyTradeGold", "party", 100, 130, ""]] + [end])
        self.assertEqual(result["wallet_unexplained_final_residuals"], {"party": 30})

    def run_rows(self, rows):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.tsv"
            with path.open("w", newline="", encoding="utf-8") as stream:
                writer = csv.writer(stream, delimiter="\t")
                writer.writerow(["kind", "metric", "owner", "before", "after", "detail"])
                writer.writerows(rows)
            return broad.analyze(path)

    def test_route_selection_and_wallet_residual(self):
        result = self.run_rows([
            ["SESSION_START", "supply_v6", "s", 0, 0, ""],
            ["CARAVAN_BEGIN", "route", "p", 0, 0, "decision=1"],
            ["ROUTE_CANDIDATE_BEGIN", "native", "p", 0, 0, "decision=1; town=t"],
            ["ROUTE_SCORE", "native_final", "p", 0, 10, "decision=1; town=t"],
            ["CARAVAN_END", "selected=t", "p", 0, 0, "decision=1"],
            ["WALLET_CHECK", "Gold", "hero", 100, 120, ""],
            ["SESSION_END", "done", "s", 0, 15, ""],
        ])
        self.assertEqual(result["problems"], [])
        self.assertEqual(result["wallet_unexplained_final_residuals"], {"hero": 20})
        self.assertEqual(result["routing"]["selection_consistent_with_observed_scores"], 1)
        self.assertIn("BUY_VALUE", result["coverage"]["purchasing"]["not_exercised"])

    def test_unknown_schema_and_open_scope_fail(self):
        result = self.run_rows([
            ["SESSION_START", "supply_v5", "s", 0, 0, ""],
            ["CARAVAN_BEGIN", "buy", "p", 0, 0, "decision=1"],
        ])
        self.assertEqual(len(result["problems"]), 3)

    def test_stock_residual_not_hidden_by_transactions(self):
        result = self.run_rows([
            ["SESSION_START", "supply_v6", "s", 0, 0, ""],
            ["STOCK_SNAPSHOT", "wool", "town", 0, 10, "settlementKind=town"],
            ["MARKET_DELTA", "wool", "town_component", 10, 5, "parentMarket=0; settlement=town"],
            ["STOCK_SNAPSHOT", "wool", "town", 0, 8, "settlementKind=town"],
            ["SELL_BEGIN", "cargo", "p", 0, 0, "decision=1"],
            ["SESSION_END", "done", "s", 0, 15, ""],
        ])
        self.assertEqual(result["stock_unexplained_residuals"], {"town/wool": 3})
        self.assertIn("1 open selling decisions", result["problems"])

    def test_category_purchase_and_unattempted_recipe(self):
        result = self.run_rows([
            ["SESSION_START", "supply_v6", "s", 0, 0, ""],
            ["CARAVAN_BEGIN", "category:wool", "p", 0, 0, "decision=1; cargo=wool:2,grain:5"],
            ["CARAVAN_END", "category", "p", 0, 0, "decision=1; cargo=wool:7,grain:5"],
            ["WORKSHOP_RECIPE", "declared", "shop", 0, 0, "recipe=0"],
            ["SESSION_END", "done", "s", 0, 15, ""],
        ])
        self.assertEqual(result["purchasing"], {"bought": 1})
        self.assertEqual(result["recipes_without_attempts"], ["shop/recipe:0"])


if __name__ == "__main__":
    unittest.main()
