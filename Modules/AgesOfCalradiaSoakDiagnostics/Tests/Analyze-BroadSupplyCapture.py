"""Read-only v6/v7 coverage and decision audit. Never rearms or starts a game.

Run against ONE capture; missing branches and nonzero residuals are explicit.
Existing supply analyzer owns sequence/native/workshop/market integrity checks.
"""
import collections
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import importlib.util
import json
from pathlib import Path
import sys
from EvidenceWalletLedger import WalletLedger


def fields(text):
    return dict(x.strip().split("=", 1) for x in text.split(";") if "=" in x)


def analyze(path):
    wallet_ledger = WalletLedger()
    counts = collections.Counter()
    decisions = {}
    candidate_open = set()
    sell_open = set()
    wallet_residuals = {}
    recipes = collections.defaultdict(collections.Counter)
    declared = set()
    problems = []
    routes = collections.Counter()
    purchase_outcomes = collections.Counter()
    cash_sources = collections.defaultdict(lambda: [0, 0.0])
    transfers = {}
    transfer_problems = []
    stocks = {}
    stock_flows = collections.Counter()
    stock_causes = collections.defaultdict(collections.Counter)
    workshop_demand = collections.defaultdict(collections.Counter)
    procurement_receipts = set()
    scopes = {}
    start_day = end_day = None
    schema = None
    closed = False
    with open(path, encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            wallet_ledger.observe(row)
            kind, metric, owner = row["kind"], row["metric"], row["owner"]
            d = fields(row["detail"])
            counts[kind] += 1
            before, after = float(row["before"]), float(row["after"])
            decision = d.get("decision")
            day = float(row.get("day", "0"))
            if kind == "SESSION_START": schema, start_day = metric, day
            if kind == "SESSION_END": closed, end_day = True, day
            if kind == "STOCK_SNAPSHOT" and d.get("settlementKind") == "town" and metric != "total_all":
                key = owner, metric
                if key not in stocks: stocks[key] = [after, after, day, day]
                else: stocks[key][1], stocks[key][3] = after, day
            if kind == "BEGIN": scopes[row["scope"]] = metric, d
            if kind == "SCOPE_BALANCE" and metric.startswith("town/") and metric not in ("town/gold", "town/total_all"):
                operation, context = scopes.get(row["scope"], (None, {}))
                if operation == "ApplyInternal":
                    key = context.get("entered"), metric[5:]
                    stock_flows[key] += after-before
                    stock_causes[key]['villager_sale_scope_net'] += after-before
            if kind == "END": scopes.pop(row["scope"], None)
            if kind == "PROCUREMENT_MOVEMENT":
                receipt=d.get('receipt')
                try:
                    import math
                    import uuid
                    uuid.UUID(receipt)
                    cargo_before, cargo_after = float(d['cargoBefore']), float(d['cargoAfter'])
                    values=(before,after,cargo_before,cargo_after)
                    if not all(math.isfinite(v) and v >= 0 and v == int(v) for v in values):
                        raise ValueError('invalid balances')
                    if receipt in procurement_receipts or not d.get('order') or d.get('committed') != 'true':
                        raise ValueError('duplicate or uncommitted receipt')
                    market_delta, cargo_delta = after-before, cargo_after-cargo_before
                    if metric == 'dispatch': valid=market_delta < 0 and cargo_delta == -market_delta
                    elif metric in ('return','liquidation'): valid=market_delta > 0 and cargo_delta == -market_delta
                    elif metric == 'arrival': valid=market_delta == 0 and cargo_delta == 0
                    elif metric == 'consumption': valid=market_delta == 0 and cargo_delta < 0
                    else: valid=False
                    if not valid or not d.get('category') or not d.get('item'):
                        raise ValueError('invalid stage conservation')
                    if market_delta and not d.get('settlement'):
                        raise ValueError('missing market identity')
                    procurement_receipts.add(receipt)
                    if market_delta:
                        stock_flows[d['settlement'],d['category']] += market_delta
                        stock_causes[d['settlement'],d['category']]['procurement_'+metric] += market_delta
                except (ValueError, KeyError, TypeError, AttributeError) as ex:
                    problems.append('Invalid procurement receipt: '+str(ex))
            if kind == "MARKET_DELTA" and d.get("parentMarket") == "0" and "/" not in metric and metric not in ("town_gold", "party_gold", "trade_tax_accrued"):
                if "settlement" not in d: problems.append("Missing market settlement mapping")
                else:
                    key = d['settlement'], metric
                    stock_flows[key] += after-before
                    stock_causes[key][d.get('operation', 'unattributed_market_operation')] += after-before
            if kind == "WORKSHOP_STOCK_DELTA" and metric.startswith("market/"):
                stock_flows[owner.split("/")[0], metric[7:]] += after-before
                stock_causes[owner.split('/')[0], metric[7:]]['workshop_market_net'] += after-before
            if kind == 'WORKSHOP_CONSUMED':
                workshop_demand[owner, d.get('recipe', 'unknown')][metric] += after-before
            if kind == "CARAVAN_BEGIN":
                if decision in decisions: problems.append("Duplicate caravan decision " + str(decision))
                parent = d.get("parentDecision", "0")
                if parent != "0" and parent not in decisions: problems.append("Missing caravan parent " + parent)
                decisions[decision] = {"kind": metric, "scores": {}, "values": {}, "begin": d}
            if kind == "SELL_BEGIN":
                if decision in sell_open: problems.append("Duplicate selling decision " + str(decision))
                sell_open.add(decision)
            if kind.startswith("SELL_") and kind != "SELL_BEGIN":
                if decision not in sell_open: problems.append("Unmatched selling observation " + str(decision))
                if kind == "SELL_END": sell_open.discard(decision)
            if kind == "ROUTE_CANDIDATE_BEGIN":
                key = decision, d.get("town")
                if key in candidate_open: problems.append("Duplicate route candidate " + str(key))
                candidate_open.add(key)
            if kind == "ROUTE_SCORE":
                key = decision, d.get("town")
                if key not in candidate_open: problems.append("Unmatched route score " + str(key))
                candidate_open.discard(key)
                if decision not in decisions: problems.append("Route score outside decision")
                else: decisions[decision]["scores"][d.get("town")] = after
            if kind == "BUY_VALUE" and decision in decisions:
                decisions[decision]["values"][metric] = after
            if kind == "CARAVAN_END":
                call = decisions.pop(decision, None)
                if call is None:
                    problems.append("Unmatched caravan end " + str(decision))
                    continue
                if call["kind"] == "route":
                    selected = metric.removeprefix("selected=")
                    scores = call["scores"]
                    if selected != "none" and selected not in scores:
                        problems.append("Selected route has no observed score " + selected)
                    elif selected != "none" and scores[selected] + 1e-5 < max(scores.values()):
                        routes["selected_below_observed_max_investigate_patches"] += 1
                    else: routes["selection_consistent_with_observed_scores"] += 1
                elif call["kind"].startswith("category:"):
                    category = call["kind"].split(":", 1)[1]
                    opening = cargo(call["begin"].get("cargo", ""))
                    ending = cargo(d.get("cargo", ""))
                    delta = ending.get(category, 0) - opening.get(category, 0)
                    purchase_outcomes["bought" if delta > 0 else "no_purchase_inspect_value_capacity_stock_budget"] += 1
            if kind == "WALLET_CHECK":
                wallet_residuals[owner] = after - before
            if kind == "WALLET_DORMANT":
                wallet_residuals.pop(owner, None)
            if kind == "WALLET_CHANGE":
                # Caller chains are evidence, not automatically categorized income.
                key = metric + "/" + d.get("callers", d.get("source", "unattributed"))
                cash_sources[key][0] += 1
                cash_sources[key][1] += after - before
            if kind == "GOLD_TRANSFER_ENDPOINT":
                transfer = d.get("transfer")
                if not transfer:
                    transfer_problems.append("Transfer receipt missing id")
                else:
                    contract = {key: d.get(key) for key in (
                        "requestedTransfer", "giverHero", "recipientHero", "expectedEndpoints", "giverPresent", "recipientPresent")}
                    expected = contract["expectedEndpoints"]
                    invalid = False
                    try:
                        expected = int(expected) if expected is not None else None
                        invalid = expected is not None and expected < 1
                    except ValueError:
                        expected, invalid = None, True
                    receipt = transfers.setdefault(transfer, {"endpoints": {}, "caller": d.get("callers", "unattributed"),
                        "requested": d.get("requestedTransfer"), "giverHero": d.get("giverHero"), "recipientHero": d.get("recipientHero"),
                        "expected_endpoints": expected, "contract": contract, "invalid_contract": False,
                        "giver_present": d.get("giverPresent"), "recipient_present": d.get("recipientPresent")})
                    if invalid or receipt["contract"] != contract or any(
                            contract[key] not in (None, "True", "False") for key in ("giverPresent", "recipientPresent")):
                        receipt["invalid_contract"] = True
                        transfer_problems.append("Invalid or inconsistent endpoint contract for transfer " + transfer)
                    if owner in receipt["endpoints"]:
                        receipt["invalid_contract"] = True
                        transfer_problems.append("Duplicate endpoint for transfer " + transfer)
                    else:
                        receipt["endpoints"][owner] = after - before
            if kind in ("WORKSHOP_RECIPE", "WORKSHOP_CYCLE"):
                key = owner + "/recipe:" + d.get("recipe", "unknown")
                if kind == "WORKSHOP_RECIPE": declared.add(key)
                else: recipes[key][metric] += 1
    if schema not in ("supply_v6", "supply_v7"): problems.append("Broad coverage requires supply_v6 or supply_v7")
    if not closed: problems.append("Session not closed; live scopes may still be in progress")
    if decisions: problems.append(f"{len(decisions)} open caravan decisions")
    if candidate_open: problems.append(f"{len(candidate_open)} open route candidates")
    if sell_open: problems.append(f"{len(sell_open)} open selling decisions")
    groups = {
        "production_shipments": ("ITEM_PRODUCED", "TRANSFER_CHECK", "CASH_CHECK", "TRANSIT_SNAPSHOT"),
        "purchasing": ("BUY_VALUE", "BUY_INDEX", "BUY_CATEGORY_PRICE", "BUY_BUDGET_CAP", "BUY_QUANTITY", "CARAVAN_ITEM_PRICE"),
        "routing": ("ROUTE_CONTEXT", "ROUTE_PERMISSION", "ROUTE_NAVIGATION", "ROUTE_SCORE", "CARAVAN_SNAPSHOT", "CARAVAN_ARRIVAL"),
        "selling": ("SELL_INDEX", "SELL_FACTOR", "SELL_QUANTITY", "SELL_PRICE", "MARKET_DELTA"),
        "workshops": ("WORKSHOP_RECIPE", "WORKSHOP_GATE", "WORKSHOP_PROGRESS", "WORKSHOP_FLOW_CHECK"),
        "consumption_prices": ("MARKET_STATE", "MARKET_QUOTE", "MARKET_MODELS"),
        "cash": ("WALLET_BASELINE", "WALLET_CHANGE", "WALLET_CHECK", "GOLD_TRANSFER_ENDPOINT"),
        "timing": ("DAY_TIMING",),
    }
    return {
        "path": str(path), "problems": problems,
        "wallet_semantics": "canonical_net_of_nested" if schema == "supply_v7" else "legacy_endpoints_may_alias_do_not_sum_as_currency",
        "wallet_independent_replay": wallet_ledger.report(),
        "wallet_final_residual_limit": "Latest snapshot only; consult independent replay for duplicate identities and historical contradictions.",
        "coverage": {name: {"observed": {k: counts[k] for k in keys}, "not_exercised": [k for k in keys if not counts[k]]} for name, keys in groups.items()},
        "routing": dict(routes), "purchasing": dict(purchase_outcomes),
        "rare_events_not_required_to_occur": {"caravan_destruction_callbacks": counts["CARAVAN_DESTROYED"]},
        "wallet_unexplained_final_residuals": {k: v for k, v in wallet_residuals.items() if abs(v) > 0.0001},
        "stock_unexplained_residuals": {"/".join(k): v[1]-v[0]-stock_flows[k] for k, v in stocks.items() if abs(v[1]-v[0]-stock_flows[k]) > 0.0001},
        "stock_balances_checked": len(stocks),
        "stock_change_causes": {'/'.join(str(part) for part in key): dict(value)
                                for key, value in stock_causes.items()},
        "workshop_input_events_by_recipe_not_additive_market_flows": {
            '/'.join(key): dict(value) for key, value in workshop_demand.items()},
        "competing_demand_limits": "Top-level market operations are net stock changes; nested scopes are excluded. Workshop input events may consume warehouse/private cargo, so do not add them to market deltas. Party exports are transfers, not proof of food consumption. Zero stock and repeated gate failures do not prove independent shortages.",
        "stock_windows_unaligned": ["/".join(k) for k, v in stocks.items() if v[2] != start_day or v[3] != end_day],
        "cash_caller_chains": dict(cash_sources),
        "transfer_receipts_not_additive_income": {
            "count": len(transfers), "problems": transfer_problems,
            "by_id": {key: dict(value, endpoint_net=sum(value["endpoints"].values()),
                interpretation=transfer_status(value))
                for key, value in transfers.items()},
        },
        "recipes_without_attempts": sorted(declared - recipes.keys()),
        "recipe_cycle_outcomes": dict(recipes),
        "next_actions": [
            "Reconcile stock and wallet residuals before balance claims; a caller chain is not proof of a matched transfer.",
            "For idle recipes correlate actual input gate failures with stock, production cadence, warehouse and cash evidence.",
            "For no-cargo arrivals trace prior category ranking, attempted purchases, budget/capacity and route candidate scores.",
            "For retained wool evaluate native index/factor gates, then quantities, price and cash caps; distinguish horse pass and repeated calls.",
            "Replay candidate policy calculations offline against captured decisions before considering a gameplay patch.",
            "A missing rare branch is NOT_EXERCISED, not a defect or an automatic request for another run.",
        ],
        "verdict": "DIAGNOSTIC_COVERAGE_ONLY_NOT_BALANCE_OR_PATCH_VALIDATION",
    }


def cargo(value):
    return {key: float(amount) for key, amount in (part.split(":", 1) for part in value.split(",") if ":" in part)}


def transfer_status(receipt):
    if receipt.get("invalid_contract"):
        return "invalid_endpoint_contract_requires_investigation"
    if receipt["expected_endpoints"] is None:
        return "legacy_receipt_missing_endpoint_contract"
    if receipt["giver_present"] is None or receipt["recipient_present"] is None:
        return "incomplete_endpoint_contract_unknown_source_or_sink"
    if len(receipt["endpoints"]) != receipt["expected_endpoints"]:
        return "incomplete_endpoints_live_or_truncated"
    if receipt["giver_present"] != "True" or receipt["recipient_present"] != "True":
        return "external_source_or_sink_requires_caller_attribution_not_inflation_proof"
    return ("balanced_gross_endpoints_not_additive_income" if abs(sum(receipt["endpoints"].values())) <= 0.0001
            else "unbalanced_gross_endpoints_investigate_nested_callbacks")


if __name__ == "__main__":
    path = sys.argv[1]
    spec = importlib.util.spec_from_file_location("supply", Path(__file__).with_name("Analyze-SupplyCapture.py"))
    supply = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(supply)
    result = analyze(path)
    base = supply.analyze(path)
    base.pop("town_stock_last_sample_per_day", None)
    result["base_audit"] = base
    print(json.dumps(result, indent=2))
