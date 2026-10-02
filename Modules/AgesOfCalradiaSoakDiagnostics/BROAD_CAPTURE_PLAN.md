# Broad economy investigation: one capture, preplanned follow-up analysis

## Status and boundary

September 20: v6 completed its 15-day capture. v7 corrects wallet
aliasing/nested observations and buy-factor coverage; it is deployed through
the scanned scoped release `C:\Users\fpicc\AocRelease\1e995262a92a`.
See SUPPLY_CAPTURE.md and Builds/Approved560CalendarFixes/WORKSHOP_PAYMENT.md.
Previous deployment/runtime-pending notes below describe historical states.

Deployment update, September 19 at 12:45 local: verified candidate installed and
one-shot supply marker armed. See SUPPLY_CAPTURE.md for the receipt. Runtime
verification remains pending. Candidate-stage notes below are historical.

September 19, 2026: supply_v6 candidate, diagnostics sidecar only. Not deployed,
armed or live-validated. No Core/UI/border rebuild, save write, speed change,
game launch/close or checkpoint. Native version: installed Bannerlord 1.4.8.
The scope is the current economy investigation, not every unrelated game system.
No finite passive capture can guarantee that every rare event occurs.

## Important observer correction

The v5 workshop input gate used Harmony `object[] __args` on a native `out int`.
The new regression fixture reproduces a caller cost of 0 instead of native 37.
The actual replacement InputGate reads the typed int BY VALUE, not by reference;
tests preserve result, cost, call count and native exception. The corrected v5
wool lookup is retained. Historical v5 traces remain records of what happened,
but must NOT be certified as an unaltered economic baseline. In particular,
   their workshop cost/profit conclusions may have been affected by the observer.
Do not deploy or restore the preceding DLL as a safe economic baseline.

## Questions, evidence and next branches (planned BEFORE the run)

| Question | Captured evidence | Follow-up using the SAME capture |
| --- | --- | --- |
| Are resources produced at intended rates? | All registered categories; native village production; every workshop recipe manifest, progress, attempts, inputs, outputs and market/warehouse deltas | Compare per-recipe cadence and successful cycles; separate lack of attempts from failed gates; preserve food/industrial distinctions |
| Why did a caravan not buy wool or another input? | Every native BuyGoods category valuation; chosen BuyCategory calls; cached average/minimum index, stock/demand, native local price index, budget/capacity factors, effective spending cap, item quote, randomized quantity and before/after cargo/gold | Distinguish ranking exclusion, low value, empty stock, full cargo, cash/category cap and actual purchase; inspect repeated calls rather than count them as transactions |
| Why did it choose another town? | Actual route candidate set, native trade permission results, siege/port/faction context, native navigation type/distance, per-category buy/sell contributions, final score, distance-cut pass and selected town | Compare selected vs scored alternatives; trace whether wool was aboard; unscored towns are NOT automatically unreachable; scores include native multipliers and randomness, not a recomputed model |
| Did cargo arrive or disappear? | Villager load/trip IDs, arrival/sale scopes, destruction callbacks and transit snapshots; caravan arrival/destruction callbacks, shared identity, cargo/current/target/home snapshots and transaction endpoints | Follow same-session identities across route/buy/sell/market records and reconcile endpoint flows; a missing later sale is not lost cargo; snapshots do not prove completed journeys |
| Why did goods remain unsold? | All-category native price indices/factors, horse/non-horse pass, threshold args, quantity before caps, BOTH ItemObject and EquipmentElement price overloads, town cash and before/after cargo | Price rejection first, then quantity/cash/weight/livestock caps. Wool-specific rows are retained for backward analysis; repeated quote/index calls are not transactions |
| Why does a workshop fail? | All recipes, not only livestock/wool; actual input gate result and typed native input cost, profit/cash/capital context, progress, warehouse flows and production events | Link repeated failures to the corresponding shop, input chain, shipments and purchases; no equation of zero stock with proven shortage |
| Where is money going? | Hero, party, settlement, workshop, clan tribute/debt and kingdom tribute/budget endpoint changes; requested values, actual values and caller chains; baseline and periodic/final reconciliation | Distinguish grants/trading/finance/expenses using callers and deltas; report unexplained bypassed changes; do not equate capital growth with profit or endpoint sums with transfer conservation |
| Are consumption and prices consistent? | Native market consumption/food-removal/deletion boundaries, all-category deltas; traded-category price/supply/demand state; model/patch provenance | Reconcile town/category opening + observed net flows = closing; evaluate policy alternatives offline before changing balance |
| Is the test itself trustworthy? | Sequence/closure, conservation checks, native MVID/patch owners, wallet residuals, schema, stop reason, elapsed-day timing, bytes/rows/managed-memory samples | Reject corrupt/incomplete captures; distinguish measured pace including overhead/pauses from configured speed |

## Safety and volume

One capture stops recording after 15 campaign days OR 60 wall minutes OR 5 GiB.
It leaves the game running and never changes speed. Five GiB is a ceiling, not
a predicted file size. Arming checks 6 GiB free, local/installed DLL equality,
closed game/launcher and native/out-parameter tests. No marker is armed here.

To reduce volume, individual market trades emit state only for their traded
category; bulk consumption keeps category coverage. Every workshop category is
checked but matching zero flow rows are omitted, with checked-category count in
the cycle receipt. Candidate category contributions are compacted into one row.
Actual live overhead and final file size remain unmeasured for v6.

All hooks are observation-only; never rerun score/model/RNG methods to explain a
decision. The supply owner unpatches/resets the new observers with its lifecycle.
Private-target or IL mismatch fails capture instead of substituting behavior.
Exception-preserving finalizers never return a replacement native exception.

## Analyze once, then follow the decision tree

`Tests/Analyze-EconomyAcceptance.py <one capture path>` combines the integrity,
wallet/transfer, Charas/sheep and coverage analyzers into the four-priority review.
It never labels generic cash checks as workshop full-payment proof, capital growth
as profit, external finance as inflation, or missing coverage as success. Review
the same capture for all four questions before considering another game run.
This offline reporting addition does not change either deployed DLL.

Run `Tests/Analyze-BroadSupplyCapture.py <one capture path>` for the combined
base integrity, category stock roll-forward, wallet residual, routing, purchase,
recipe and coverage report. `Tests/Classify-SupplyJourneys.py` classifies the
remaining villager loads; it does not infer loss. Preserve the raw capture.

1. Integrity/residual failure: locate the exact sequence/owner and inspect the
   existing rows and native code before any economic change.
2. No-cargo visits: purchase ranking and prior cargo, then route scoring.
3. Cargo present but no sale: recorded native price/quantity/cash constraints.
4. Inputs arrive but no production: input gate, recipe progress, warehouse,
   profit/capital/town cash; do not apply a blanket production multiplier.
5. Cash pressure: group actual wallet deltas by caller, reconcile endpoint
   residuals and separate requested payments from clamped payments.
6. Candidate fixes: replay their deterministic decision effects on captured
   cases, write focused regression tests, then implement the smallest sidecar fix.
7. Only a changed gameplay policy needs a matched validation comparison.
   A rare branch not occurring is NOT_EXERCISED, not an automatic new soak.

## Explicit limits

This records native decisions, not a counterfactual simulation or proof that a
route change will improve balance. Routing contributions precede native weights;
negative final scores require navigation/distance context, and third-party patch
provenance matters. Cash caller stacks may include wrappers or inlined gaps;
snapshot residuals must stay visible. Existing objects get initial baselines;
newly first-seen wallets are labelled separately. A caravan destruction callback
does not quantify cargo transferred to a victor; no cargo-loss claim may be made
from absent caravans alone. Sea/war/warehouse branches cannot be forced by
a passive capture. These limits are declared now, not after asking for a run.

## Verification required before a user run

Release sidecar build; Verify-SupplyCapture (native install + IL + actual typed
input-cost hook + writer limits/closure), Verify-WoolOutParameter, analyzer
fixtures, Verify-EconomyTransactions, Verify-SoakDiagnostics, CalendarMath,
StrategicMapCoverage and protected political baseline. Live v6 validation is
still required; static/synthetic success is not advertised as runtime success.

### Local verification receipt — September 19

Candidate DLL SHA-256:
`3BDF05AF6A9BB2266EA09C505A637DBABD12CD8AE21FDED608CD5E5A927A5212`.
Release build completed without compiler warnings. Supply/native-hook/writer
checks, actual typed workshop input-cost fixture, wool private-out-struct
regression, 12 Python analyzer tests, economy transaction regression, soak
contracts, protected CalendarMath, StrategicMapCoverage and protected political
baseline all passed. Arm script parsed without errors; it was NOT executed.

Installed DLL remains `387F50B0...` (v5, containing the old workshop observer).
No deployment, arming or live v6 run occurred. Full release packaging was not
run: this is a diagnostics candidate in an unrelated-dirty project, not a clean
production release. No runtime performance or exhaustive branch coverage claim.
