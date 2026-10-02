# Deep economy mechanism research — 28 September 2026

## Result

The deeper review identifies a reproducible order-size search gap, a separate
restriction on access to delivered private stock, and previously unreported
outgoing trade in the loss-making towns. These are distinct mechanisms. None is
permission to claim that all three idle workshops have one proven cause.

Research only: no gameplay implementation, deployment, settings change, save
mutation or new campaign run. This supplements ECONOMY_RESEARCH_APPLIED_20260928.md
and the full review of session 7f1cb08aba0c49518d8ed0605ef61516.

## 1. Reproduced: order-size search misses feasible intermediate lots

ProcurementPlanner.Find examines only `{requested, BatchesPerOrder}.Distinct()`.
The minimum/default lot is three. The adaptive request can be larger, but lots
between those two values are never evaluated. Freight rounding and the fixed
freight charge mean the smaller lot can fail the per-batch profit hurdle while
the larger lot exceeds available surplus. An intermediate lot can satisfy both.

I compiled the existing pure policy source through Verify-Procurement.ps1 in
Windows PowerShell, ran its 117 assertions, and invoked the actual internal
OrderBatches, Freight, HasSurplus and Profitable methods using reflection. No
campaign objects, game process or live logging were involved.

Controlled inputs (synthetic, not a replay of a named town):

- MaximumAdaptiveBatches=12; remaining settings at defaults.
- Daily rate=2, lead time=2.5 days, one-day reorder buffer: requested lot=7.
- Route distance=150, within the 300 limit; source category stock=16.
- One unit per batch, one local recipe input; reserve=max(10, 7)=10.
- Input quote=10 gold/unit, conservative output=93 gold/batch, margin=80.
- Other endpoint, capital and cash constraints assumed permissive for this
  isolated feasibility demonstration. This is not a full native Find fixture.

| Batches | Planner examines? | Reserve passes? | Freight | Profit passes? | Both pass? |
| ---: | --- | --- | ---: | --- | --- |
| 3 | Yes | Yes | 7 | No | No |
| 4 | No | Yes | 8 | Yes | Yes |
| 5 | No | Yes | 10 | Yes | Yes |
| 6 | No | Yes | 11 | Yes | Yes |
| 7 | Yes | No | 13 | Yes | No |

For three batches, ceil((30+7)/3)+80=93; strict output >93 fails.
For four, ceil((40+8)/4)+80=92; output 93 passes and stock left is 12.

**Conclusion:** the actual predicates permit lots the planner's enumerator
cannot select. This is a demonstrated search limitation, not just speculation
about market balance. Existing fixtures test requested-size success and
three-batch fallback, but not this gap between them.

**Correction specification:** when adaptive sizing is enabled, consider every
integer lot between the configured base lot and the bounded request, keeping
every existing reserve, route, cash, cost and profitability gate. Preserve
fixed-lot behavior when disabled and existing saved orders. Reuse price/roster
snapshots within a decision to avoid multiplying native quote calls; item
availability can differ by lot size and must still be evaluated correctly.
Use deterministic tie-breaking and the existing score, not guaranteed profit.

**Required tests:** reproduce this gap through the actual Find path; test the
base and upper boundaries, multiple inputs, invalid quotes, cash/reserves,
unchanged disabled mode and serialization. Profile the bounded worst case:
settings allow sizes up to 100, so blindly repeating all native queries for
every quantity is not an acceptable implementation.

**Important disconfirmation:** Lageta, Askar and Qasira each recorded zero
procurement profitability and capital rejections. The particular counterexample
above requires a rejected small-lot profit check. It is therefore NOT established
as the explanation for those three workshops. Fixing it must not be sold as a
guaranteed cure for their losses.

## 2. Source-confirmed: shipment safety also controls delivered-stock access

The current call chain is:

`Cycle.Prefix -> ProcurementBehavior.Ready -> ProcurementPlanner.Eligible -> Safe`

Safe requires positive aggregate town food stocks and no siege. Ready applies
this before looking up an arrived, paid-for order. If a town later starves or
comes under siege, private stock is no longer made available to that native
production cycle. The stock is preserved, not deleted; native market inputs
may still be tried instead.

This is different from stopping a new export or an incoming shipment. The native
WorkshopsCampaignBehavior.DailyTickTown examined here suppresses production for
InRebelliousState and still charges expenses; it does not put these same food/
siege conditions on RunTownWorkshop. The AOC gate is an additional restriction.

The delivery service itself already recognizes that arrived cargo no longer
depends on supplier diplomacy/food. Access to local stock should receive the
same explicit lifecycle distinction rather than inheriting all dispatch rules.

**Correction specification:** split new-order/transport eligibility from access
to already-arrived private inputs. Keep validated ownership, recipe identity,
quarantine, committed transfer and return-state checks. Preserve native output,
town-cash, margin and rebellion behavior. Do not make in-flight goods available
early, bypass player ownership handling or automatically liquidate unpaid cargo.

**Evidence limit:** this is a source-confirmed mechanism, not a counted incidence
in the closed run. No-order workshops cannot be explained by inaccessible
delivered cargo unless an actual matching order is established. A native fixture
must demonstrate starvation/siege after arrival versus before arrival.

## 3. Existing capture: trade competes with workshop access

Read the existing full-acceptance JSON, acceptance_details.market_causes, rather
than starting another recording. The analyzer aggregates top-level MARKET_DELTA
records only (`parentMarket=0`), using after-before to avoid nested double-counts.

| Town/category | Recorded top-level market movements in this window |
| --- | --- |
| Lageta / olives | Caravan export -149; caravan import +5; consumption -8 |
| Askar / silver | Caravan export -8 |
| Qasira / wool | No entry in this report bucket; not proof of no wool supply |
| Galend / grapes | Caravan import +17; other-party export -27; caravan export -110; consumption -15 |
| Galend / wine | Caravan export -12; consumption -1 |

These are selected operation subtotals, not complete opening-to-closing stock
equations. They do not by themselves include every village shipment, production
event, private ledger change or starting balance, nor establish event ordering.

**Implication:** Lageta and Galend were not simply disconnected from trade.
Their inputs were also bought away. Galend had some recorded wine removal, so
"no output demand" is not established either. The next explanatory comparison
is timing and availability at workshop attempts versus outgoing purchases,
not a global production increase or an export ban.

Planner counts add important limits:

- Lageta: 16 no-offer decisions, 149 reserve rejections, 217 route rejections,
  nine recipe-level town-cash rejections and 25 lead-stock-satisfied evaluations.
- Askar: 16 no-offer decisions, 70 reserve, 816 route, ten lead-stock-satisfied.
- Qasira: 16 no-offer decisions, 192 reserve, 800 route, zero lead-stock-satisfied.
- Galend: eight decisions, one offer; three profitability rejections, one
  recipe-level town-cash rejection, 84 lead-stock-satisfied evaluations.

Candidate counts are NOT independent shortages or directly comparable exposure
rates. Earlier gates prevent later gates being evaluated. A lead-stock-satisfied
count also shows some local stock was sufficient for a particular forecast;
it does not prove that stock remained for the workshop to consume.

## 4. Forecast and item-pool differences: do not overstate conservative quotes

ProcurementPlanner.Catalog considers all registered items in a category. Native
FillItemsInAllCategories excludes multiplayer-only, non-merchandise and
player-crafted items. Native output selection also prefers suitable cultures,
falls back when needed, and can apply production item modifiers.

Consequently the planner's minimum unmodified quote is a policy estimate, not
an exact prediction of native proceeds. An unavailable cheap category member
can make the estimate unnecessarily pessimistic; item modifiers can also mean
an unmodified estimate is not a guaranteed lower bound on actual output value.

No observed loss is attributed to this difference here. The three idle shops
had zero recipe-output-quote rejections, and the loaded catalog for every mod
was not reconstructed. This is a compatibility/forecast issue, not evidence
that native prices should be overwritten.

**Correction specification:** use a read-only eligible item pool matching native
eligibility/culture fallback, without calling RNG or generating output twice.
Keep actual native approval authoritative. Label forecast uncertainty honestly;
test excluded items, no eligible output and modifier-sensitive categories before
changing the quote contract. Do not add multi-item cargo aggregation to this
patch: current one-item-per-category orders have separate persistence contracts.

## 5. Wealth: grant frequency is a different question from reconciliation

Version-matched native DailyTickClan adds kingdom support after calling the
finance model. Below the relevant wallet ceiling it adds a regular amount;
below lower thresholds a random larger grant can replace that amount. This
does not pass through the finance wrapper's calculation scope.

If that native daily frequency were preserved unchanged over a 365.25-day year,
it would have approximately 4.35 times as many opportunities as an 84-day year.
Thresholds, spending, ownership and stochastic outcomes mean this is NOT a
prediction of a 4.35-fold wallet increase. Current deployed patch ownership for
that entire path remains to be certified before proposing a rate correction.

Trade agreements also distribute accumulated visit-generated income and update
their own state. Reducing a final clan-income number without reconciling these
side effects can change transfer accounting. Current noble cash declined in the
observed window; that does not resolve the older wealth spike or prove every
source has the intended annual behavior. No broad income cut is recommended.

## 6. Research sources and verification limits

- [TaleWorlds workshop design](https://www.taleworlds.com/en/Games/Bannerlord/Blog/59)
  explicitly connects input supply, output demand and production halts. Historical
  design context only; current numeric claims above come from local code.
- [Official map-distance API](https://apidoc.bannerlord.com/v/1.3.4/class_tale_worlds_1_1_campaign_system_1_1_game_components_1_1_default_map_distance_model.html)
  distinguishes navigation capabilities and port endpoints. Its 1.3.4 signature
  is context, not proof of every 1.4.8 route result or the active model override.
- Native campaign DLL rehashed this turn: 1F8E33E2ED73E6EC653D7629180AFB70649DDC6E5BD1657A802A264EFDA1C3AE,
  matching the prior private native-code research manifest.
- Current source inspected: ProcurementPlanner, ProcurementState,
  ProcurementBehavior, ProcurementPatches, native procurement fixtures,
  EconomyRecipeDiagnostics and Analyze-AcceptanceDetails.
- Current diagnostic source contains WORKSHOP_ELIGIBILITY, including rebellion.
  Follow-up raw inspection below found no such records in this captured build;
  source presence must not be mistaken for captured runtime coverage.
- Existing 117 pure procurement assertions passed in Windows PowerShell; the
  first probe ran under an incompatible PowerShell runtime and was rerun correctly.
- The new counterexample invokes actual policy functions but not full native
  Find; no claim of a new end-to-end fixture or patched Release build.

## Recommended next implementation, now narrower and evidence-backed

1. Complete bounded adaptive lot search and add the missing actual-planner
   regression case. No lower food reserves, extra money or new save schema.
2. Separate delivered-input eligibility from shipment eligibility; prove the
   lifecycle/native-production contract with fixtures before deployment.
3. In the same bounded patch, preserve the route aggregate while splitting
   invalid/unreachable versus over-limit reasons and reuse existing eligibility
   events. Cache quote data rather than expanding routine logs or model calls.
4. Treat native output-pool alignment as a separately verified forecast change.
5. Keep category reserve tuning and grant-frequency policy conditional on their
   specific evidence. Do not claim steps 1–4 necessarily cure Lageta/Askar/Qasira.

These are implementable, independently testable mechanisms. Another unchanged
16-day soak is not needed to reproduce the order-search gap. A later live
comparison may still be necessary to establish the size of any economic benefit.

## Follow-up: raw-capture falsification and town liquidity

This section refines the implementation priorities above. Read-only streaming
analysis of the completed raw capture used session
`7f1cb08aba0c49518d8ed0605ef61516` only. The source was
`C:/Users/fpicc/Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/AocFramework-current.tsv`.
No new game run, deployment, balance changes or source-log edits occurred.

### Actual failed production attempts, not supplier-candidate counts

| Workshop | Actual attempts | Successful | Input rejection | Cash rejection with sufficient margin/capital | Strict margin rejection |
|---|---:|---:|---:|---:|---:|
| Lageta olive press, instance 59 / workshop_2 | 9 | 0 | 7 | 2 | 0 |
| Askar silversmithy, instance 104 / workshop_3 | 5 | 0 | 5 | 0 | 0 |
| Qasira wool weavery, instance 106 / workshop_1 | 20 | 0 | 20 | 0 | 0 |
| Galend wine press, instance 224 / workshop_3 | 10 | 5 | 2 | 2 | 1 |

Input rejection is the observed input gate result at that attempt, not proof
of continuous town-wide shortage. Cash/margin subdivisions are reconstructed
from the recorded gate values and the inspected native strict comparison.
The underlying combined rejection event alone does not give that subdivision.

Concrete witnesses:

- Sequence 1052650: Lageta had capital 9,885, input cost 29, output income
  2,000, effective hurdle 129, but town gold only 1,357. Sequence 1391364
  repeats the cash limitation with town gold 5, capital 9,839 and hurdle 140.
  Procuring more olives cannot by itself remove these cash failures.
- Galend instance 224: sequences 1827624 and 2323660 show town cash 5 and 2,
  respectively, versus output payments 130 and 86. Margins and workshop
  capital were sufficient. Sequence 2817993 instead shows output 62 exactly
  equal to effective hurdle 62, with town cash 4,986 and capital 9,994:
  rejection is consistent with the native requirement to exceed the hurdle.
- Galend also has a second wine press (instance 222 / workshop_1). It had
  the same five-success/two-input/two-cash/one-margin attempt split. Do not
  combine its profit/capital with instance 224 or confuse town with workshop.

### Cadence was advancing; missing attempts are not all missing production

Each listed recipe has 16 WORKSHOP_PROGRESS observations. Lageta attempted
production on nine days and had seven zero-attempt days; Askar had five and
eleven; Galend had ten and six. Qasira's garment recipe attempted four times,
while its felt recipe attempted on all sixteen days. All twenty Qasira attempts
failed the input gate. Fractional cadence and native failure handling matter:
do not count every non-producing day as a separate input failure, nor treat
the felt progress accumulator as twenty-plus successfully executed batches.

### Evidence absent from this build must not be inferred from today's source

The complete record-kind inventory contains zero WORKSHOP_ELIGIBILITY,
TOWN_CONTEXT, RECIPE_PROGRESS or RECIPE_CADENCE rows. It does contain the older
WORKSHOP_PROGRESS and WORKSHOP_STATE records. Source code containing newer
events is not evidence they executed in this captured build.

All four target workshops have seventeen state snapshots with rebellion=False,
and their recipes have daily progress observations. This weighs against
rebellion explaining these failures, but snapshots are not continuous history.
The capture does not establish the entire starvation/siege timeline.

The opening procurement ledger decodes to an empty Orders list. The three
fully idle workshops had no offers/orders in the previously reconciled planner
and transfer evidence. Therefore blocked access to previously delivered private
stock does NOT explain those three shops in this run. The delivered-stock
eligibility coupling remains a separate code-level issue, not a demonstrated
cause of these losses. The intermediate-lot example likewise remains synthetic;
this follow-up did not execute a new full-Find native regression fixture.

### Town cash, reconciled by actual wallet identity

The wallet-baseline settlement fields map Lageta to Town/instance:3634/Gold
and Galend to Town/instance:3675/Gold. Summing net-of-nested WALLET_CHANGE
records reproduces their terminal wallet checks exactly:

| Town | Opening | Recorded inflows | Recorded outflows | Closing | Residual |
|---|---:|---:|---:|---:|---:|
| Lageta | 25,663 | 232,598 | 258,143 | 118 | 0 |
| Galend | 19,935 | 66,655 | 79,305 | 7,285 | 0 |

Selected caller-context totals (not proof that the underlying policy is wrong):

| Recorded context | Lageta incoming / outgoing | Galend incoming / outgoing |
|---|---:|---:|
| Caravan BuyCategory | 120,446 / 84,314 | 23,405 / 16,381 |
| Caravan SellGoodsInternal | 0 / 125,994 | 0 / 27,831 |
| Villager OnSettlementEntered | 0 / 30,415 | 0 / 19,030 |
| Workshop input purchase | 1,702 / 0 | 237 / 0 |
| Workshop output payment | 0 / 11,928 | 0 / 5,522 |
| MakeConsumption | 39,440 / 0 | 9,615 / 0 |
| Unresolved daily callback | 63,418 / 0 | 27,759 / 0 |

The selected table excludes food/horse/loot/ship-upgrade contexts; the full
inflow/outflow totals include them. Both incoming and outgoing changes inside
BuyCategory are real recorded wallet changes, not independent purchases and
not automatically double counting. Their economic purpose must be established
before changing them. The unnamed daily callback must NOT be relabeled a
particular subsidy solely because its frequency is daily.

The inspected native DefaultSettlementEconomyModel.GetTownGoldChange computes
Round(0.25 * (10000 + 12 * prosperity - town gold)). This is a native reference,
not proof that this exact model and call path caused the unresolved callbacks
in the captured session. Changing that coefficient or injecting gold is not
justified by the reconciled balances alone.

### Revised best path

1. Correct the missing intermediate lot search with a full-planner regression,
   retaining reserve, food, capital and profitability safeguards. This is a
   bounded algorithm defect, not a promise to cure these four losses.
2. Investigate town liquidity alongside supply access: trace the actual
   purchase debit/retention rules and daily cash-recovery callback; compare
   their timing with the witnessed cash failures. Reuse this capture first.
   A general output multiplier or extra-input shipment does not solve a town
   unable to pay its existing profitable workshop batch.
3. For Askar and Qasira, prioritize reachable suppliers and actual exportable
   stock. Current combined route/reserve counters cannot distinguish a valid
   protective rejection from a bad route policy. Do not lower reserves blindly.
4. Verify delivered-stock eligibility and output-pool compatibility as separate
   regression contracts. Neither is established as the cause of the three idle
   workshops in this capture.
5. Preserve strict margin behavior until an explicit balance decision changes
   it. Galend's equality rejection is not a floating-point bug demonstrated by
   these records. Reassess profitability only after supply and liquidity.

Official TaleWorlds workshop design links supply and demand to production
halts ([design article](https://www.taleworlds.com/en/Games/Bannerlord/Blog/59));
its historical discussion does not establish current numeric rates. These
new numerical findings come from the recorded campaign and local native code.
Research-only verification: raw stream inventory, per-workshop attempt joins,
opening-ledger decoding, identity-mapped wallet sums and terminal reconciliation.
No Release build was claimed or needed for this documentation-only refinement.

## Implementation receipt: bounded lot-search fix

Following the user's subsequent request to implement a fix, the procurement
planner now evaluates every integer lot from its bounded request down to the
configured minimum, rather than only the two endpoints. Existing score selection
and every reserve, food, trade, route, cash, capacity and profitability gate remain.
Supplier item quotes are cached for one supplier/recipe comparison only, including
invalid quotes, to avoid multiplying native quote calls. No new Harmony targets,
save fields, settings defaults or economic rates changed. Protected Core and UI
artifacts were not rebuilt or replaced. This receipt supersedes research-only
status for this specific algorithm change, not for the other proposed changes.

Verification on 28 September 2026:

- Added a regression invoking actual ProcurementPlanner.Find. The old Release
  build failed with `Intermediate-lot gap: Find rejected feasible batches four
  through six`, establishing a red test before changing production code.
- Fixed build selects six batches, goods cost 60, freight 11 and arrival day
  22.5; rejects the reserve/margin-infeasible case; selects five at the lower
  capital boundary; leaves supplier inventory and wallets unchanged.
- Maximum configured bound (100) still selects the viable six-batch lot and
  calls the supplier's item quote only once. Existing fixed-lot cases pass.
- Two older adaptive cases now correctly select intermediate lots four and five
  instead of falling straight to three; test expectations were updated to the
  new explicit selection contract, not removed.
- Verify-Candidate.ps1 passed: Release builds with zero warnings/errors, 117
  pure assertions, native procurement/lifecycle/save-load fixtures, six native
  AI and four player cycle fixtures, protected baseline, calendar mathematics
  and strategic-map checks.
- Candidate procurement DLL SHA-256:
  `B3712DDCFC3DA40500724183A0B8EE8E99703D8574536D803DA2B900E899DEF9`.

Not deployed, not a public-release/security certification, and not proof of a
measured campaign profitability improvement. Town liquidity and inaccessible
regional inputs remain separate unresolved economic mechanisms. Candidate
rejection counters may increase because more lot sizes are evaluated; they
must not be compared as counts of independent shortages.
