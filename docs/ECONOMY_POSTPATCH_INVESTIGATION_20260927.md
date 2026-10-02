# Post-patch economy investigation — 2026-09-27

Status: investigation only. No production code, rates, settings, save, installed
assembly, game process or capture control was changed. No new run requested.

## Evidence and comparison limits

Compared two closed captures from campaign `gHBJvLViLE7O`, both starting at
396013.39322577778 and covering approximately five campaign days:

- Before: `daeceb2d3b09444da48052c917489b33`, ending 396018.39326409722.
- Candidate: `425342f5f5934e2c976c75a8b90d9e4f`, ending 396018.39335104974.

The source directory is
`C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics`.
Before is preserved in
`acceptance-archive-93c2af57c6d54053b1897d044cc2a6d9\AocFramework-current.tsv`;
candidate is `AocFramework-current.tsv`.

SHA-256 before:
`C07D71E385E01709A22203F88A689990D2F4BB78AAA9ADABA17C930B4CCCF4B1`.
SHA-256 candidate:
`0248E3E7B5A3A5AED1826F7291DA05D4095FDC39338F64D56A69EE2A31FE4D05`.

Re-ran the existing `Analyze-FrameworkPeriods.py` against each source, preserving
the generated results under `output/diagnostics/comparison-425342f5-20260927/`.
Also traced selected raw gates, terminal cargo, evaluated ship values and native
destruction receipts. Native IL previously extracted under
`output/diagnostics/causal-daeceb2d-20260927/aoc_trace_native_20260927.txt`
has campaign MVID `886629fe-6e60-40d7-9a57-8d46017179d9`, matching this capture.

These are matching-start replays, not a deterministic controlled experiment.
Battles, trading, prices and other events diverged. Differences below are
observations, not percentages causally attributable to the patch alone.
Do not add overlapping runs together.

## Accounting and measured outcomes

Candidate: zero discarded records, no analyzer-reported reconciliation errors,
1,784 capital-affecting successful workshop batches reconciled, 43 procurement
transactions reconciled. All 2,169 tracked wallets have zero measured residual;
11 hero wallets have incomplete windows, nine also lack clan identity.
Reconciled flows do not prove prices are balanced or that missing identities are
complete. Both runs' workshop profitability reports have no coverage gaps.

Operating results below separate owner withdrawals and recognize consumed
procurement cost rather than expensing every dispatch. Native daily expense
attribution remains an inference from the captured capital boundaries.

| Measure over five days | Before | Candidate |
| --- | ---: | ---: |
| Procurement dispatches | 39 | 43 |
| Procurement arrivals | 12 | 19 |
| Wine units produced | 22 | 30 |
| Ten wine presses: operating result, gold | 877 | 1,775 |
| Wine input-rejected cycles | 19 | 12 |
| Wool consumed | 58 | 77 |
| Felt units produced | 110 | 142 |
| Wool-consuming workshops: operating result, gold | 33,805 | 39,329 |
| Wool input-rejected cycles | 140 | 130 |
| Fourteen smithies: operating result, gold | 37,630 | 36,867 |
| Twenty-two linen workshops: operating result, gold | 36,409 | 35,483 |
| Linen units produced | 106 | 104 |
| Livestock-chain meat units produced | 1,718 | 1,658 |

Gate counts count attempts, not independent shortages. Positive aggregate profit
does not make every individual workshop healthy.

## Remaining workshop causes

### Wine: the two current loss-makers are margin constrained

`town_ES2/workshop:80` improved from -115 to +172; `town_V2/workshop:126`
from -115 to +116. Both show new offers and successful production. ES2's
terminal ledger retains two delivered grapes after one batch's cost allocation.

Current loss-makers:

- `town_EW3/workshop:206`: -31, one successful cycle and two margin failures.
  Inputs were accepted in all three attempts. Failed output quotes were 94
  against required 101, then 100 against a strict threshold of 100. Town cash
  was 19,603 and 18,610 respectively. Daily market grapes remained available.
- `town_V5/workshop:224`: -115, three margin failures, inputs accepted each
  time. Output quotes/thresholds were 110/119, 80/98 and 106/120. Town cash
  was sufficient. More input supply is not the direct fix for these attempts.

The planner's prospective landed-cost check does not guarantee that market
quotes remain profitable at a later production attempt. Preserve that distinction;
do not call every failed wine cycle a grape shortage or proven weak demand.

### Linen: delivery timing, not demonstrated missing cargo

`town_A6/workshop:218` lost 115. Its two production attempts at elapsed days
1.9652 and 3.9650 failed input checks. Its three-flax shipment actually arrived
at elapsed day 4.2714, after the last attempt. The closing ledger contains all
three flax units. There was no subsequent production attempt inside the window.
This is not evidence that delivered private stock cannot be consumed.

### Wool and iron: viable supply remains geographically constrained

Eleven wool-consuming workshops lost 115 each. Five had no order throughout:
`town_S1/workshop_3`, `town_V1/workshop_2`, `town_A3/workshop_3`,
`town_A8/workshop_1`, `town_V6/workshop_3`. Their recorded rejected candidates
were exclusively route and category-reserve filters, not capital or profit.
These filter totals are sequential rejected candidates, not separate shortages.

Of the other six loss-makers, five had wool in transit and one had delivered
unused wool at closure. Across all workshops ten wool shipments remained in
transit, with ETA 0.789–2.992 days after closure. No terminal in-transit order
of any category was past its recorded ETA.

Smithy input-rejected cycles remained 38; margin rejections rose from 8 to 16.
Two town_S2 smithies repeatedly had no viable supplier because of route/reserve
filters. Aggregate smithy profit is positive, so a universal smithy subsidy or
production increase is not supported.

Livestock successful cycles fell 781 to 750; input rejections rose 626 to 641.
The comparison does not prove a production-rate mismatch. It warrants examining
the same route/reserve constraints and observed livestock supply, not scaling all
resource output because the meat total fell in one replay.

### Architectural limitation confirmed in source

`ProcurementBehavior.DailyTown` rejects any new order while an existing order
remains, including arrived private stock. `ProcurementPlanner` uses a lead-time
trigger capped at three batches and chooses the cheapest landed offer, not the
earliest economically useful delivery. This improves early ordering but is not
a continuous replenishment pipeline. Cadence-aware reserves and route/cost/time
trade-offs should be replayed offline before changing these rules. Existing
receipts identify filter classes, not each rejected supplier's contemporaneous
inventory, so they cannot establish an optimal reserve or route radius alone.

## Noble wealth: major native sources now attributed

The same 97 stable noble-clan groups netted +263,149 before and +395,999 in the
candidate replay. These are canonical wallet movements, not summed gross
allocations. The historical 28.19-million increase is still not reconstructed.

Current stable-noble wallet contexts include:

- Battle commitments: 504,114 credited and 961 debited, net +503,153.
- Horse-sale context: 232,773 credited; retain transfer accounting rather than
  treating sales as created money.
- Separate remaining-ship recovery: 61,560 credited in two payments.
- Casualty-loot context: 74,862 credited.

Ordinary expenses and other transfers offset these inflows. Battle net rose
81,653 versus the prior capture; separate ship recovery added 61,560 where the
prior top-level trace had none. These event differences explain much of the
132,850 larger total net, without implying procurement caused it.

### Upstream allocation and cleanup

- 592 allocation scopes (296 wallet allocation, 296 ship-loot allocation).
- 689 battle commit calls matched their latest event/party allocations; none
  unmatched and no mismatch. These include zero/debit calls, not 689 payouts.
- Defeated-wallet stage allocated 86,892 losses and 83,276 winner gains.
- Ship-loot stage evaluated 24 distinct ships once each, sum 444,000. Winner
  allocation increments total 443,999. Native per-winner flooring accounts for
  the one-gold aggregate difference. Every one of these ships has a native
  destruction receipt with ownership cleared; no overlap among their ship IDs.
- The separate recovery path paid 25,513 and 36,047 for four other ship IDs.
  Its accounting report confirms later native destruction and owner cleanup for
  all four, with zero requested-payment or wallet residual.

Thus ship-to-gold conversion is a demonstrated large native wealth source, not
an observed duplicate-payout bug. Allocation totals are upstream of payments:
never add 443,999 to the same battle cash again. A balance decision could target
AI ship monetization narrowly, but this evidence does not establish the desired
multiplier or justify an economy-wide income reduction.

## Confirmed offline analyzer compatibility defect

Running the new battle-allocation analyzer on the older capture reports 465
false `(None, None)` allocation-to-commit mismatches and 197 spurious joins.
Old `REWARD_BEGIN` records lack `mapEvent`/`mapParty`; the analyzer nevertheless
stores/reset-compares their shared `(None, None)` key. No upstream allocation
scopes exist in that older capture. This is missing historical coverage, not
corrupted gameplay accounting. Other before-run analyzers remain usable.

Fix priority: require complete, valid event and party identity before storing or
joining allocations. Mark older records unavailable. Add fixtures for multiple
legacy commits/resets and partially missing IDs. Do not weaken current-session
mismatch checks. No deployed DLL or game restart is needed for this offline fix.

## Recommended order, not implemented by this investigation

1. Fix and regression-test the legacy analyzer identity collision above.
2. Replay bounded supplier-reserve and replenishment alternatives offline,
   focusing on the five wool workshops and blocked iron routes. Preserve route,
   food, affordability, owned/in-transit inventory and duplicate-order safeguards.
3. Audit wine margin policy separately: both remaining losers already had inputs.
   Compare cadence, native hurdle and daily expenses before any targeted change.
4. Treat AI ship monetization as the leading demonstrated noble-wealth balance
   lever, separate from transfers, finance and workshop payouts. Establish the
   desired economy target before selecting a multiplier.

Player-clan naval penalties, finite quest deadlines and rare procurement
return/liquidation/failure branches remain unexercised. Missing rare branches do
not automatically justify another soak or forced failures in the user's save.
No new capture is required to complete the offline analyzer correction and
policy investigation. Changed gameplay would still need proportionate acceptance.

## Offline follow-up: regression and policy investigation

This follow-up did not change production code, installed binaries, settings,
saves, capture state or the running game. Analyzer changes below were evaluated
in memory only; they are not committed or deployed fixes.

### Analyzer correction experimentally verified

The legacy identity collision has a second cause: a `remaining_allocations`
receipt currently seeds an allocation key even when no upstream allocation was
observed. A subsequent commit can then appear verified without that evidence.

The in-memory candidate requires complete positive numeric event/party IDs at
commit lookup and permits a remaining-allocation reset only for an already
observed allocation key. It passed all 11 existing battle-allocation tests plus
six additional cases: missing both IDs, either ID missing, an unobserved event,
zero event identity, and valid IDs without upstream allocation evidence.
Each additional case used three commits; the candidate reported three unjoined
commits without manufacturing a mismatch or a successful join.

Replaying the two closed original captures with that candidate produced:

| Capture | Verified commits | Unjoined commits | Problems |
| --- | ---: | ---: | ---: |
| Older capture | 0 | 663 | 0 |
| Current capture | 689 | 0 | 0 |

The current capture retained all 592 allocation scopes. Missing older coverage
remains explicitly unavailable, not certified. Before making this a permanent
change, persist the new regression cases and review validation of upstream
allocation identities too. No game restart is required for an analyzer fix.

### Supplier reserves: useful sensitivity evidence, not a dispatch replay

The current reserve sums input units across local recipes and multiplies by
seven batches, irrespective of their calendar-adjusted daily throughput. Hidden
artisans contribute twelve iron-input recipes: their iron reserve alone is 84.
A town with a nine-recipe smithy can reserve 147 iron.

Using opening stock and subsequently observed recipe progress as a stationary
sensitivity estimate, a seven-day throughput reserve with the existing minimum
of ten would permit a three-unit offer at several currently excluded examples:
town_V1 (47 iron, reserve 84 to 10), town_EN1 (75, 147 to 13), town_B1 (42,
147 to 13), and town_EW6 (27, 147 to 13). These are stock eligibility examples,
not proof of affordable, reachable or profitable dispatches. The capture does
not contain every candidate quote and inventory at every decision instant.

Do not apply this formula blindly to every input. The same sensitivity estimate
raises wool reserves at town_EW4 from 14 to 21 against stock 20, and town_N4 from
28 to 43 against stock 37. Both would lose a three-unit stock-qualified offer.
Thus a uniform reserve replacement can worsen some wool routes. Replay and
evaluate category-specific effects before selecting any production policy.

### Felt cadence checked: not a new defect

Installed native `DefaultItemCategories.InitializeAll` assigns Felt property
value 2, `BonusToFoodStores`. The calendar sidecar deliberately retains native
cadence for this property in recipes, settlement demand and market smoothing.
`RECIPE_CADENCE.md` explicitly documents preserving felt's native semantics.
`INPUT_SUPPLY_CANDIDATE.md` and `UsesNativeCadence` also explicitly preserve
native wool village output. Therefore the initial suspicion that felt is at
native speed while its wool source is calendar-scaled is incorrect for this
candidate.

Captured felt increments near 2 per tick versus garment increments near 0.23
are consistent with that policy and native recipe base speeds of 2 and 1.
Native workshop IL also confirms the observer's increment calculation:
after minus min(1, before) plus attempted cycles. Failed attempts still consume
one progress unit. The high felt increment is not an observer arithmetic error.
Reclassifying felt would be a balance-policy change affecting demand as well as
production, not a justified repair from this evidence. Preserve it for now.

### Refined implementation order

1. Land the offline analyzer correction with permanent regression coverage.
2. Prototype bounded, cadence-aware supplier reserves, starting with iron;
   preserve food protection, route checks, capital, cost basis and order identity.
3. Treat overlapping replenishment separately: the current single-order state
   and `SingleOrDefault` lookup cannot safely support another order simply by
   deleting the pending-order guard. Model persistence and accounting first.
4. Evaluate the two loss-making wine presses against native strict margin
   thresholds and daily expenses. Their input availability is not the failure.
5. Keep noble-wealth tuning separate from bug repair. Native ship monetization
   is measured; a target multiplier and the historical 28.19-million attribution
   are not established by these five-day captures.

These investigation steps do not require another user-run soak. Any eventual
gameplay acceptance must target the changed behavior; offline evidence cannot
honestly certify future campaign balance or currently unexercised branches.

## Implementation follow-up: offline tools completed

The user subsequently authorized the next step. This section supersedes the
in-memory-only status for the analyzer correction above; gameplay policy is
still unchanged and nothing was deployed.

`Analyze-BattleAllocations.py` now validates positive ASCII event/party identities
at both upstream scope/snapshot and commit boundaries. A reset can update only
an existing observed allocation key. Historical commits lacking identities or
upstream observations remain coverage gaps, while malformed upstream receipts
and real allocation mismatches remain failures. Permanent tests cover the six
legacy/reset cases, malformed upstream IDs, and a genuine mismatch after reset.

`Analyze-ReserveSensitivity.py` is a new read-only, closed-single-session tool.
It compares opening iron/wool stocks with reserves based on summed maximum
observed per-recipe increments for 7, 14 and 30 days. It retains a minimum of ten
and models a three-unit offer. It does not alter configuration, import gameplay
assemblies or claim to simulate shipments. Missing stock is not assumed zero;
changed input units and invalid rates fail instead of silently becoming demand.

Current-capture sensitivity results:

| Category | Observed towns | Current stock-qualified | 7-day | 14-day | 30-day |
| --- | ---: | ---: | ---: | ---: | ---: |
| Iron | 57 | 0 | 18 | 16 | 7 |
| Wool | 22 | 9 | 7 | 3 | 1 |

These counts are stock-qualified towns, not viable trade routes. Opening stock
and later maximum increments are not contemporaneous; unknown recipes, price,
competition, cash, route and replenishment effects remain outside this model.
A 14-day iron-only candidate merits further offline planner verification:
it protects twice the modeled local demand of seven days while retaining 16 of
the 18 stock-qualified examples. This is a conservative candidate, not a proven
optimal setting. Keep the existing wool reserve policy; uniform replacement
would remove six of its nine stock-qualified examples at 14 days.

Verification completed:

- All 142 Python tests in the diagnostics Tests directory passed.
- Current capture replay: 592 allocation scopes, 689 joined commits, zero
  unjoined commits and zero problems. Older replay: zero joined, 663 unjoined,
  zero problems; missing upstream coverage remains explicit.
- Diagnostics sidecar Release rebuild succeeded without reported compiler
  warnings/errors, with output isolated under
  `D:/AocOfflineAudit/20260927-analyzer/`; it was not installed.
- Protected political renderer and World Events verification passed.
- Current capture SHA-256 remains
  `0248E3E7B5A3A5AED1826F7291DA05D4095FDC39338F64D56A69EE2A31FE4D05`.

Boundary: offline diagnostics only. No Harmony targets, save schema, production
rates, income, runtime settings or protected assets were modified. Full public
release certification and future gameplay balance are not claimed.
