# Cash-limited AI settlement sales

## Implemented scope

Boundary: game integration in the optional approved-calendar-fixes sidecar.
Native target: Bannerlord 1.4.8 SellItemsAction.ApplyInternal(PartyBase,
PartyBase, ItemRosterElement, int, Settlement).

Native trading moves every requested item before making payment. Capping the
settlement's cash transfer fixes gold creation but can leave the seller unpaid
for already-transferred goods. The new guard breaks the native per-item loop
before adding a price or moving an item when accumulated price plus that next
native quote exceeds town cash. Previously sold items are still paid normally.
Remaining goods stay with the seller. There are no extra market-model calls,
new price formulas, refunds, escrow, saved fields or persistent counters.

Scope is funded AI lord-party sales to towns only. Player parties, caravans,
villages, purchases, non-lord parties and world initialization retain their
existing contracts. This addresses the demonstrated PartiesBuyHorse entry path,
which sells non-food loot as well as mounts. It is not a universal rewrite of
all trade paths. The settlement-payment cap remains a final cash safeguard.

Compatibility: six IL instructions inserted after the existing item quote,
before accumulation/roster mutation, branching to the native post-loop payment.
The exact quote, accumulator and loop-exit pattern is checked before patching.
Absent/changed targets, exception blocks in that region, or duplicate guards
abort preflight through the existing logged sidecar rollback path. Other mods
that rewrite this loop or mutate cash during roster callbacks need live review.
No guarded-loop fallback guesses at new native layouts.

## Verification

- Release build with warnings treated as errors.
- Existing native payment baseline reproduces the defect; eight corrected
  settlement transfers check cash and native event amounts.
- Nine native sales verify partial/empty/exact/abundant cash, changing quotes,
  retained inventory, payment, and initialization/player/non-lord/zero-price
  bypasses, with all supply observer hooks installed but recording inactive.
- Tests reject empty/changed/duplicate IL, preserve every original opcode and
  operand, and check subtraction-based affordability against integer overflow.
- Deterministic test boundaries replace price values, party roles, notification
  lookup and event delivery. Native sale/roster/payment bodies execute.
  Offline success is not a claim of live campaign acceptance.
- Verify-AiSettlementSale.ps1 is part of the scoped release gate.

## Supply and profit investigation: avoid unsupported balance patches

Same completed capture `763434d247474f25a264015c3f75594f`:
- 2,301 wool produced by 17 recorded village sources. Pravend had five caravan
  arrivals, all carrying zero wool. No wool sale was attempted there with wool
  aboard. Of 246 wool-carrying route decisions, Pravend scored -1 in 144 and
  nonnegative in 102; every one of those 102 lost to a higher observed score.
  These are repeated decisions, not independent caravans. Route rejection is
  not proof of a navigation bug. Do not bypass native distance/permission gates.
- Owner distributions were separated from operating expenses. The latter are
  inferred where native HandleDailyExpense is inlined into DailyTickTown;
  native code and caller chains support that inference, which is labeled in
  the output rather than hidden. Eight expense-only wallets remain unmapped
  by the production-cycle identity join.
- Among mapped wallets, operating results using that inference: wine +4,020,
  smithy +66,235, linen +48,664. Two mapped wine workshops lose 17 and 155,
  with six and seven failed grape input checks respectively. Class-wide profit
  boosts are not justified by these observations. The eight unmapped wallets
  each lose 345 in daily expense; they must not disappear from the report.

Read-only analyzers: Analyze-WorkshopPayments.py and Analyze-PravendSupply.py.
Evidence: output/workshop-payments-763434.json and output/pravend-supply-763434.json.
The supply/profit work is analysis and accounting correction, not a deployed
logistics redesign. No new capture is required to review this evidence. Any
route-priority or production rebalance remains unimplemented until its rule is
supported and its economic tradeoffs are tested.

## Deployment receipt, 2026-09-20

Scoped release `C:\Users\fpicc\AocRelease\db65bf20ea20\verification.json`
passed the native fixtures, analyzer checks, protected baseline, Defender scan
and ten-minute security hold. The gate deployed only the two allowed sidecars.
Installed files were independently hashed afterward; local candidate binaries
were synchronized from that exact scanned package, not rebuilt.

- Calendar fixes: `67E1A0DF5C61995FE78940F391952A7D178403ECFA237BADBE5F744317B852D6`
- Diagnostics: `1D5B6D18FC32C96C61D7791FC1C075FB520968266A8398CAF00229D51EB4BEB9`

No live campaign acceptance is claimed. No game launch, capture rearm, save,
checkpoint or speed change was performed by this deployment.

Further native inspection: TownMarketData inventory callbacks update stored
quantities/value; its AddDemand method separately applies the economy model's
demand change. The household consumption behavior also uses daily category
demand. Consequently increasing generic wool demand is not a delivery-only
correction: it can increase consumption too. This inspection has not established
a broken workshop-demand callback or a double-scaling defect. Adding workshop
procurement or changing caravan priorities would be a new economic policy,
not a demonstrated correction to the cash bug. Regional shortages and a loss-
making individual workshop must not be treated as proof that every workshop
requires guaranteed supply or profit.
