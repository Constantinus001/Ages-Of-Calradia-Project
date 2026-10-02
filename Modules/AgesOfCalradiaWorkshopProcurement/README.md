# Workshop procurement candidate v0.1

Status: implemented and offline-verified, **not deployed or live-economy accepted**.
This is an optional new economic policy, not proof that the old regional market
was broken. No protected Core, UI, border, clock, settings or save was replaced.

## Boundary and coverage

An independent .NET Framework 4.7.2 Bannerlord 1.4.8 module owns procurement,
private inventory and its save ledger. Existing calendar fixes retain recipe
cadence and cash-conserving output payment. The diagnostics module observes
private stock and prepaid cost through a read-only optional ABI.

The implementation supports visible AI/notable workshops and single- or
multi-input trade-good recipes, rather than hard-coding wool or wine. Orders
contain a complete recipe bundle. Existing player workshop/warehouse decisions,
hidden artisan production, equipment recipes and world initialization stay
native. This is broad lifecycle coverage, not a promise of universal supply.

## Economic rules (candidate policy, not calibrated native constants)

- Order when local market recipe coverage falls below the route lead-time plus
  one-day buffer forecast, using the authoritative calendar per-recipe base and
  active workshop model. Trigger is rounded up and bounded to 1–3 batches (the
  configured order size); no production rate changes. Missing/failed cadence
  integration blocks new offers with diagnostics, not delivery of saved cargo.
- One outstanding order per stable settlement ID/workshop tag, surviving reload.
- Purchase three batches from one eligible town with the entire bundle. No
  partial assembly or multi-supplier consolidation in v0.1.
  When the optional `MaximumAdaptiveBatches` policy is enabled, evaluate every
  integer lot from the bounded lead-time request down to `BatchesPerOrder`,
  selecting by the existing landed-cost/delay score (larger lot wins a tie).
  Checking only the large request and minimum can miss a viable intermediate
  lot: the large lot may violate reserves while the minimum fails the strict
  margin after rounded freight. With adaptive ordering disabled, only the
  configured fixed lot is evaluated, as before. The settings bound is 100.
  All existing safety gates remain; supplier item quotes are reused only within
  one read-only supplier/recipe search. No persisted price cache or schema change.
  Diagnostic rejection counts describe evaluated candidates, not shortages;
  enabling this complete search can increase those counts without more failures
  in the economy. `Verify-NativeProcurement.ps1` exercises the actual planner,
  intermediate reserve/margin/capital boundaries and the maximum search bound.
- Suppliers may be in another peaceful faction. Both towns must be out of siege
  and have positive food stocks. Use the native land-route distance cache;
  reject disconnected/nonfinite routes and distances over 300 map units.
- Keep at least seven summed local recipe-input batches (minimum ten units) in
  each source category. Food categories keep at least 100 units except `grape`,
  whose configurable category floor is ten; the local recipe reserve still wins
  when larger. This grape policy is an acceptance candidate, not calibrated proof.
  These are
  conservative floors, **not** proof of seven days of household food security.
  Food exports also require 100 aggregate food stores and, when the current
  food balance is negative, a buffer covering 30 days of that measured deficit.
- Preserve seven daily workshop expenses in capital after paying the contract.
- A conservative, unmodified output-price floor must exceed landed input cost
  plus the native `200 / conversionSpeed` profitability hurdle. Actual native
  production approval and town affordability still apply on arrival.
- Input prices are fixed at dispatch quotes. Freight is `2 + ceil(units *
  distance / 100)`; arrival is `1 + distance / 100` campaign days later, checked
  hourly. These are merchant-service abstractions, not measured party speeds.
- Workshop capital funds goods and freight. The source town's merchant account
  receives both. Actual source items are removed into the saved cargo ledger.
  Nothing is spawned, and freight is not an untracked money sink.
- Delivered stock is private. Native production recognizes the original landed
  cost for its profit gate but does not buy those same inputs a second time.
  No town-market donation, extra output, subsidy or guaranteed profit.

Transport is virtual: there is no visible or raidable courier, escort or sea
shipping. Intervening hostile territory is not a military route-risk model.
No town/village population or income rebalance is bundled with this feature.

## Lifecycle and failures

- Pending/arrived cargo, remaining quantities, cost basis and operational stop
  state are serialized under `aoc_workshop_procurement_v1` as a versioned string.
  No new native save-type IDs or persisted game-object references.
- Missing ledger on an old save starts empty. Corrupt/unknown ledgers disable
  mutation and preserve their original bytes; they are not silently reset.
- Dispatch/return uses checked native wallet and roster transfers with exact
  snapshots, postconditions and compensating rollback. Failed rollback and
  interrupted transfers persist a quarantine. Reloading cannot replay them.
- Native consumption exceptions quarantine the remaining ledger rather than
  silently recreating consumed inputs. No command clears financial faults.
- War/siege/food blockade delays transport. Fourteen continuous blocked days
  after the expected arrival trigger a return with another travel delay.
  The supplier must fund a remaining-goods-basis refund; freight is spent.
- Workshop ownership transfers include the private inventory asset. A change
  to player ownership or incompatible recipe/type causes cash-funded liquidation
  into the destination market. Thirty-day-old remaining cargo is also offered
  for liquidation. An insolvent buyer cannot mint a refund.
- Missing towns/workshop slots/items retain quarantinable assets rather than
  discarding them. Such cases can require explicit save investigation.

Console controls (implemented; in-game console discovery not yet checked):

- `aoc.procurement_status`: ledger counts, fault and removal readiness.
- `aoc.procurement_stop`: persistently stop new orders; existing shipments finish
  or return, and available inventory is liquidated against real town cash.
- `aoc.procurement_resume`: resume new ordering only if healthy.

Do **not** remove the module with outstanding cargo. Stop ordering, wait until
status reports `safeToRemove=True`, then save normally before disabling it.
No automatic save or game restart is performed by these commands.

## Native targets and compatibility

All hooks target `WorkshopsCampaignBehavior` in installed Bannerlord 1.4.8:

1. `TickOneProductionCycleForNotableWorkshop(Production, Workshop, bool)`:
   prefix scopes a matching private bundle, postfix reconciles consumption and
   output cash, finalizer restores nested context and records exceptions.
2. `DetermineItemRosterHasSufficientInputs(Production, ItemRoster, Town, out int)`:
   prefix supplies the matching bundle's availability and recognized cost only.
3. `ConsumeInputFromTownMarket(ItemCategory, int, Town, Workshop, bool)`:
   prefix consumes private inputs and emits the native consumption event without
   another input payment. Unscoped calls are untouched.
4. `CanNotableWorkshopProduceThisCycle(Production, Workshop, int, int, bool)`:
   transpiler replaces exactly one Capital getter with a scoped prepaid-capital
   allowance. The profit threshold and town-cash checks remain native. This
   allowance does not change the wallet or pay anything.

Exact signatures, return types, input-call topology and capital getter pattern
are checked before installation. The approved calendar sidecar's output-payment
patch must already be installed. Failure unpatches this module only; its behavior
still preserves existing save data with mutations disabled. Mods rewriting
these same methods or mutating balances in callbacks remain a compatibility risk.

## Diagnostics

Bounded log: Documents/Mount and Blade II Bannerlord/AgesOfCalradiaProcurement/
procurement.log, 8 MiB active plus one previous file. Records dispatch, arrivals,
daily pending capital, last no-order decision, consumed units/cost, cycle receipts,
town debits, contribution before daily expenses, returns and faults.

The optional diagnostic ABI exposes current prepaid input basis and private
category stocks without re-querying models/RNG. Supply capture now includes
`private/` inventory in WORKSHOP_FLOW_CHECK and `prepaidInputCost` in gate rows.
The workshop analyzer expects zero new input cash for prepaid batches but still
deducts their recognized acquisition/freight cost from production contribution.
It flags an extra charge or a mismatch between declared input cost and cost basis.
Legacy captures without the field retain their existing cash interpretation.

Production contribution is **not net profit**. Daily operating expense, owner
distributions, acquisition cash flow, stranded inventory and liquidation gains/
losses remain distinct. Existing comprehensive diagnostics remain necessary.

## Verification completed 2026-09-20

`Tests/Verify-Candidate.ps1` builds this candidate and a separate diagnostics
candidate under output/procurement-diagnostics, without overwriting installed or
local deployed diagnostics binaries. It runs:

- Release build, zero warnings/errors.
- 84 pure-state, multi-input, reserves, distance/freight, cost allocation,
  save-schema and rollback assertions.
- Actual native wallet/roster dispatch, duplicate rejection, cash-limited return
  and refund-replay checks.
- Actual hourly handler: no early arrival, delayed arrival, later supplier war,
  missing supplier, and cash/stock-conserving local expiry liquidation. Campaign
  time, settlement lookup, safety, diplomacy and player ownership are fixtures.
- Seven supplier-planner scenarios: editable Core policy, valid multi-input quote, insufficient surplus,
  working-capital reserve, unprofitable freight, invalid route and existing local
  inputs. Native catalog, navigation and faction eligibility are fixture inputs.
- 16 native production cases including unarrived cargo, low town cash, noncapital
  bypass, prepaid low-capital production, and behavior save/load on every case.
- Partial native failure quarantine and corrupt/missing save payload checks.
- Existing six AI/four player native production and warehouse cases, with supply
  diagnostic hooks installed. Test boundaries stub prices, RNG, object lookup,
  campaign registration and event delivery; no live game is loaded.
- Protected renderer, protected calendar contract and strategic-map checks.

The diagnostics Python suite also passes (43 tests at this revision).

## Follow-up audit corrections

- Delivered cargo is local inventory: later supplier war or disappearance no
  longer starts a return or blocks destination liquidation. Existing committed
  returns retain their original behavior.
- The 30-day expiry starts at actual delivery, persisted as `DeliveredDay`.
  Older candidate ledgers without that field retain their ETA-based fallback.
- Incomplete transfers and returning cargo cannot be consumed even through the
  lower-level state API. Invalid clock values cannot poison blockage state.
- Native read/preflight failures before any transfer mutation report a clean
  failure, allowing the caller to remove its pending order instead of retaining
  phantom cargo. Unrestored mutations still require persisted quarantine.
- Payment reconciliation rejects non-finite numbers and negative quoted/prepaid
  costs rather than silently accepting NaN comparisons. Invalid evidence raises
  an error, not a completed clean report; negative actual wallet changes remain
  available for discrepancy analysis.

Ledger encode/decode now share a 4,000,000-character bound. Admission reserves
lifecycle/fault growth before debit; readable existing ledgers without headroom
are held with their raw save text preserved. Pure and native tests cover size
rejection, escaped fault growth and unchanged cash/stock on rejected dispatch.
Supplier quotes are read once per input candidate, and invalid quotes cannot hide
a valid alternative. These are bounded correctness fixes, not economic balance
or live performance certification.

## Remaining release/acceptance gates

This candidate has **not** gone through the clean committed release snapshot,
Defender scan/security hold, packaged-module installation, or live acceptance.
The existing economy-sidecar release allowlist intentionally does not publish
this new optional module. Do not bypass that gate by copying DLLs manually.

Still required before claiming the supply/profit problem solved: installed-world
planner coverage and performance, actual dispatch/arrival and funded liquidation,
native save/reload and console discovery, shortage reduction, realized profit
after expenses and freight, and whole-capture reconciliation with private cargo.
The same broad capture should cover those outcomes; offline fixture success is
not a reason to declare regional scarcity fixed or request isolated repeat runs.
