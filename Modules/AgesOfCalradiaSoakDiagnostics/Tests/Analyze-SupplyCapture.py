"""Streaming shipment evidence review. Closure/coverage are not balance approval."""
import argparse
import collections
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import json
import math
import re


def analyze(path):
    counts = collections.Counter()
    decisions = collections.Counter()
    produced = collections.Counter()
    loaded = collections.Counter()
    sold = collections.Counter()
    scopes = {}
    starts, ends, sessions = [], [], set()
    problems = []
    trips, arrivals = set(), {}
    last_sequence = 0
    last_day = -math.inf
    stocks = collections.defaultdict(dict)
    timing_seconds = timing_days = 0.0
    workshop_counts = collections.Counter()
    workshop_flows = collections.Counter()
    workshop_open = set()
    workshop_required = False
    market_required = False
    market_open = set()
    market_flows = collections.Counter()
    wool_required = False
    wool_open = set()
    wool_outcomes = collections.Counter()

    def problem(message):
        if len(problems) < 50:
            problems.append(message)

    with open(path, encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            try:
                seq = int(row["sequence"])
                scope, parent = int(row["scope"]), int(row["parent"])
                day, before, after = (float(row[k]) for k in ("day", "before", "after"))
                if not all(math.isfinite(x) for x in (day, before, after)):
                    raise ValueError("nonfinite number")
            except (KeyError, ValueError, TypeError) as error:
                problem("Malformed row: " + str(error))
                continue
            if seq != last_sequence + 1:
                problem(f"Sequence gap at {seq}")
            if day < last_day:
                problem(f"Backward clock at {seq}")
            last_sequence, last_day = seq, day
            sessions.add(row["session"])
            kind, metric, detail = row["kind"], row["metric"], row["detail"]
            if ends:
                problem("Rows after SESSION_END")
            counts[kind] += 1
            if kind == "SESSION_START":
                starts.append(day)
                workshop_required = metric in ("supply_v3", "supply_v4", "supply_v5", "supply_v6", "supply_v7")
                market_required = metric in ("supply_v4", "supply_v5", "supply_v6", "supply_v7")
                wool_required = metric in ("supply_v5", "supply_v6", "supply_v7")
                if metric not in ("supply_v1", "supply_v2", "supply_v3", "supply_v4", "supply_v5", "supply_v6", "supply_v7"):
                    problem("Unsupported supply schema: " + metric)
            if kind.startswith("WOOL_SELL_"):
                if kind == "WOOL_SELL_QUANTITY" and after < 0:
                    problem("Invalid negative native wool sale quantity")
                if kind == "WOOL_SELL_INDEX" and metric == "available":
                    index = re.search(r"\baverage=([^;]+)", detail)
                    try:
                        if index is None or not math.isfinite(float(index[1])) or float(index[1]) <= 0:
                            problem("Invalid native wool price index")
                    except ValueError:
                        problem("Malformed native wool price index")
                match = re.search(r"\bdecision=(\d+)", detail)
                decision = match[1] if match else None
                if kind == "WOOL_SELL_BEGIN":
                    if decision is None or decision in wool_open:
                        problem("Invalid wool decision begin")
                    wool_open.add(decision)
                else:
                    if decision not in wool_open:
                        problem("Unmatched wool decision observation")
                    if kind == "WOOL_SELL_END":
                        wool_open.discard(decision)
                        wool_outcomes["no_cargo" if before == 0 else "sold_some" if after < before else "retained_cargo_inspect_values"] += 1
            if kind in ("MARKET_BEGIN", "MARKET_END", "MARKET_DELTA", "MARKET_STATE"):
                match = re.search(r"\bmarket=(\d+)", detail)
                market = match[1] if match else None
                if kind == "MARKET_BEGIN":
                    if market is None or market in market_open:
                        problem("Invalid market begin")
                    market_open.add(market)
                else:
                    if market not in market_open:
                        problem("Unmatched market observation")
                    if kind == "MARKET_END":
                        market_open.discard(market)
                    if kind == "MARKET_DELTA":
                        operation = re.search(r"\boperation=([^;]+)", detail)
                        parent_market = re.search(r"\bparentMarket=(\d+)", detail)
                        if parent_market is None:
                            problem("Missing market lineage")
                        elif parent_market[1] == "0":
                            market_flows[(operation[1] if operation else "unknown") + "/" + metric] += after-before
            if kind.startswith("WORKSHOP_"):
                workshop_counts[kind] += 1
                match = re.search(r"\bcycle=(\d+)", detail)
                cycle = match[1] if match else None
                if kind == "WORKSHOP_ATTEMPT":
                    if cycle is None or cycle in workshop_open:
                        problem("Invalid workshop cycle begin")
                    workshop_open.add(cycle)
                if kind == "WORKSHOP_CYCLE":
                    if cycle not in workshop_open:
                        problem("Unmatched workshop cycle result")
                    workshop_open.discard(cycle)
                if kind in ("WORKSHOP_CONSUMED", "WORKSHOP_PRODUCED"):
                    workshop_flows[kind + "/" + metric] += after-before
            if kind == "SESSION_END":
                ends.append((day, metric))
            if kind == "BEGIN":
                if scope in scopes or (parent and parent not in scopes):
                    problem(f"Bad scope lineage at {seq}")
                scopes[scope] = (metric, parent)
            elif kind == "END":
                if scopes.pop(scope, None) != (metric, parent):
                    problem(f"Unmatched END at {seq}")
            elif scope and (scope not in scopes or scopes[scope][1] != parent):
                problem(f"Unmatched observation at {seq}")
            if kind == "DISPATCH_DECISION":
                decisions[metric] += 1
            if kind == "ITEM_PRODUCED":
                produced[metric] += after - before
            if kind == "STOCK_SNAPSHOT" and "settlementKind=town" in detail and metric != "total_all":
                stocks[int(day)][row["owner"], metric] = after
            if kind in ("TRANSFER_CHECK", "CASH_CHECK", "WORKSHOP_FLOW_CHECK") and abs(after - before) > 0.0001:
                problem(f"Nonzero {kind} at {seq}: {after-before}")
            method = scopes.get(scope, (None, 0))[0]
            if kind == "SCOPE_BALANCE" and metric != "party/total_all":
                if method == "MoveItemsToVillagerParty" and metric.startswith("party/") and metric != "party/gold":
                    loaded[metric[6:]] += after - before
                if method in ("ApplyByVillagerTrade", "ApplyInternal") and metric.startswith("town/") and metric not in ("town/gold", "town/total_all"):
                    sold[metric[5:]] += after - before
            match = re.search(r"\btrip=(\d+); sinceLoadDays=([-\d.Ee+]+)", detail or "")
            if match:
                trip, duration = int(match[1]), float(match[2])
                if kind == "END" and metric == "MoveItemsToVillagerParty":
                    trips.add(trip)
                if kind == "BEGIN" and metric in ("ApplyByVillagerTrade", "ApplyInternal"):
                    arrivals.setdefault(trip, duration)
            if kind == "DAY_TIMING":
                match = re.search(r"wallSeconds=([-\d.Ee+]+)", detail)
                if match and after > before:
                    timing_seconds += float(match[1])
                    timing_days += after - before
    if len(starts) != 1 or len(ends) != 1 or len(sessions) != 1:
        problem("Need exactly one complete single-session capture")
    if scopes:
        problem(f"{len(scopes)} unclosed native scopes")
    if workshop_open:
        problem(f"{len(workshop_open)} unclosed workshop cycles")
    if market_open:
        problem(f"{len(market_open)} unclosed market scopes")
    if wool_open:
        problem(f"{len(wool_open)} unclosed wool decisions")
    observed_arrivals = {k: v for k, v in arrivals.items() if k in trips}
    missing = [name for name in ("ITEM_PRODUCED", "TRANSFER_CHECK", "CASH_CHECK", "SALE_QUOTE") if not counts[name]]
    if workshop_required:
        missing += [name for name in ("WORKSHOP_PROGRESS", "WORKSHOP_CYCLE", "WORKSHOP_GATE", "WORKSHOP_CONSUMED", "WORKSHOP_PRODUCED") if not counts[name]]
    if market_required:
        missing += [name for name in ("MARKET_HOOK", "MARKET_MODELS", "MARKET_BEGIN", "MARKET_END", "MARKET_STATE", "MARKET_DELTA") if not counts[name]]
    if wool_required:
        missing += [name for name in ("WOOL_SELL_BEGIN", "WOOL_SELL_END", "WOOL_SELL_INDEX", "WOOL_SELL_FACTOR", "WOOL_SELL_QUANTITY", "WOOL_SELL_PRICE") if not counts[name]]
    stock_summary = {}
    for day, entries in stocks.items():
        summary = collections.defaultdict(lambda: {"units": 0, "zero_stock_towns": 0, "towns_observed": 0})
        for (_, item), amount in entries.items():
            summary[item]["units"] += amount
            summary[item]["zero_stock_towns"] += amount == 0
            summary[item]["towns_observed"] += 1
        stock_summary[day] = dict(summary)
    return {
        "path": str(path),
        "integrity": "FAILED_OR_INCOMPLETE" if problems else "CLOSED_NO_DETECTED_INTEGRITY_ERRORS",
        "problems": problems,
        "stop_reason": ends[0][1] if len(ends) == 1 else None,
        "campaign_days": ends[0][0] - starts[0] if len(starts) == len(ends) == 1 else None,
        "not_exercised": missing,
        "workshop_record_counts": dict(workshop_counts),
        "market_root_endpoint_deltas_not_cash_conservation": dict(market_flows),
        "wool_sale_decision_outcomes": dict(wool_outcomes),
        "workshop_event_units_compare_roster_deltas": dict(workshop_flows),
        "town_stock_last_sample_per_day": stock_summary,
        "decision_counts_not_independent_trials": dict(decisions),
        "actual_production_events_units": dict(produced),
        "loaded_endpoint_net_units": dict(loaded),
        "sold_town_endpoint_net_units": dict(sold),
        "observed_loads": len(trips),
        "loads_with_observed_town_sale_entry": len(observed_arrivals),
        "mean_load_to_sale_entry_campaign_days": sum(observed_arrivals.values()) / len(observed_arrivals) if observed_arrivals else None,
        "loads_without_observed_sale_entry_not_proven_lost": len(trips - observed_arrivals.keys()),
        "measured_seconds_per_day_including_pauses": timing_seconds / timing_days if timing_days else None,
        "balance_verdict": "NOT_CERTIFIED: compare matched runs, prices, shortages and multiple routes; totals include transfers, not just creation",
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", nargs="+", help="AocSupply-*.tsv paths; one result per independent capture")
    args = parser.parse_args()
    print(json.dumps([analyze(path) for path in args.capture], indent=2))
