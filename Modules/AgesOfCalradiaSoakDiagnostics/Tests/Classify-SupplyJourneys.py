"""Classify recorded journeys without treating missing sale entry as cargo loss."""
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import json
import re
import sys
from collections import Counter


def classify(path):
    loads, sold, destroyed, latest, snapshots = {}, set(), set(), {}, {}
    end = 0
    closed = False
    with open(path, encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream, delimiter="\t"):
            day = float(row["day"])
            end = max(end, day)
            closed |= row["kind"] == "SESSION_END"
            fields = dict(re.findall(r"(?:^|; )([^=;]+)=([^;]*)", row["detail"]))
            trip = fields.get("trip", "")
            if not trip.isdigit():
                continue
            trip = int(trip)
            if row["kind"] == "END" and row["metric"] == "MoveItemsToVillagerParty":
                loads[trip] = (day, fields.get("party"))
                if fields.get("party"):
                    latest[fields["party"]] = trip
            if row["kind"] == "BEGIN" and row["metric"] in ("ApplyInternal", "ApplyByVillagerTrade"):
                sold.add(trip)
            if row["kind"] == "BEGIN" and row["metric"] == "OnMobilePartyDestroyed":
                destroyed.add(trip)
            if row["kind"] == "TRANSIT_SNAPSHOT":
                snapshots[trip] = (day, fields)
    result = []
    for trip, (day, party) in loads.items():
        if trip in sold:
            continue
        age = end - day
        # Exclusive precedence; destruction is a callback, not quantified loss.
        status = ("destruction_callback" if trip in destroyed else
                  "subsequent_load_same_party" if party and latest.get(party) != trip else
                  "recent_load_under_two_days" if age < 2 else
                  "unresolved_older_load")
        snap = snapshots.get(trip)
        result.append({"trip": trip, "classification": status, "age_days": age,
                       "party": party, "last_snapshot_age_days": end-snap[0] if snap else None,
                       "last_state_not_current": snap[1] if snap else None})
    return {"closed": closed, "loads": len(loads), "sale_entry_matched": len(set(loads) & sold),
            "unmatched_exclusive_counts": dict(Counter(x["classification"] for x in result)),
            "unmatched": result, "caution": "No sale entry is not proof of loss; snapshots can be stale; no causal loss amount inferred."}


if __name__ == "__main__":
    print(json.dumps(classify(sys.argv[1]), indent=2))
