# Iron supplier reserve candidate

Status: implemented locally, not deployed or activated in the user's game.

Superseded as a standalone handoff by `COMBINED_ECONOMY_CANDIDATE_20260927.md`.
Do not request a separate iron-only campaign run or deploy its preset alone.

## Boundary and compatibility

This changes new-order eligibility in the procurement sidecar only. The separate
Campaign Systems assembly owns the editable setting. Protected Core and World
Events assets are unchanged. No new Harmony targets, calendar formulas, item
production changes, income changes or save-schema changes are introduced.

`IronSupplierReserveDays` accepts integer 0..365. Missing/zero preserves the
existing batch-reserve behavior. The candidate configuration sets 14. Activation
must merge this single attribute into existing configuration, not replace other
user overrides. The configuration fingerprint includes the new setting; old
orders keep their original quote, policy receipt, ETA, cost basis and lifecycle.

For non-food category `iron` only, the candidate reserves:

`max(MinimumSupplierStock, ceil(sum(local recipe input units * authoritative daily rate) * days))`

The sum includes hidden artisans and uses the same calendar-aware/native model
bridge already used for buyer cadence. It does not infer demand from successful
consumption, which would underestimate a workshop starved of inputs. Zero known
demand retains the stock floor. Invalid, unavailable or overflowing demand
rejects a new offer; existing cargo remains serviced. Rates are cached only
inside a single planner decision, not across days or settings changes.

Wool, food-tagged categories, other resources, route safety, war restrictions,
item quotes, capital reserves, freight, profitability, town cash and duplicate
orders keep their existing rules. Candidate rejection counts are not shortages.
Plan diagnostics expose `ironSupplierReserveDays`, `supplierCadenceRejected`
and the policy revision alongside existing reserve and other gate counters.

## Evidence and limitations

The existing closed-capture sensitivity study found 16 of 57 observed iron towns
stock-qualified under a 14-day reserve versus zero under the batch formula.
Seven days qualified 18, whereas 30 days qualified seven. Fourteen days is a
conservative candidate, not a demonstrated optimum or promise of deliveries.
The offline study used opening stocks and later per-recipe maximum increments;
the implementation uses current model rates at each actual decision. These are
different evidence contexts. Local recipe demand excludes other town consumers,
future arrivals and competing buyers; the minimum floor remains important.

## Verification

- Release builds: Campaign Systems, Procurement and diagnostics, zero warnings
  and errors in the candidate verifier.
- 109 procurement state/policy/lifecycle assertions and 53 framework assertions.
- Native fixture includes hidden artisan demand, exact 14-day iron reserve
  acceptance/rejection, unavailable cadence, and known absence of recipes.
- Existing native cash, multi-input, warehouse, cargo conservation, save/load,
  duplicate-order, refunds, partial failure and optional coexistence checks pass.
- 142 offline Python analyzer tests pass.
- Protected artifact verification and Protected560 calendar math pass.
- Strategic-map coverage passes; the complete `Verify-Candidate.ps1` finishes
  with its undeployed-candidate PASS (not public release certification).

Native fixtures run outside the game with deterministic prices/environment;
they do not certify live economic outcomes or logging overhead.

## Focused acceptance after an authorized deployment

Do not start another unchanged soak or force game restarts during this work.
At the next planned load, first verify the deployed assembly identities and
loaded policy revision with `ironSupplierReserveDays=14` in plan evidence.
Observe actual iron orders through dispatch, arrival and consumption, retaining
supplier reserve, cash/stock conservation and cost-basis checks. Compare failed
input gates and operating profit against the saved baseline, separating owner
withdrawals. Check planner/logging overhead and confirm unrelated category
policies are unchanged. A run with no eligible iron order is inconclusive,
not proof of success. Duration must cover the observed route and next recipe
attempt, not an arbitrary repeated multi-hour soak.

Deployment/public release gates and live acceptance remain separate from these
local checks. No game, save, launcher or installed configuration was modified.
