# Full economy capture review — 28 September 2026

## Conclusion

The completed capture shows working accounting and functioning supply chains,
not a demonstrated global economy failure. The remaining performance concerns
are local: three workshops produced nothing while incurring expenses, and one
wine press narrowly lost money. Do not apply blanket income cuts or production
boosts from this evidence. The prior naval mismatch was a corrected report bug.
Player-specific naval behavior and finite quest deadlines remain unobserved.

This is a review of everything represented by the combined economic capture,
not certification of every game system, every campaign stage or every mod.

## Provenance and method

- Capture: `C:/Users/fpicc/Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/AocFramework-current.tsv`.
- Session: `7f1cb08aba0c49518d8ed0605ef61516`.
- Deployed candidate: `C:/Users/fpicc/AocRelease/framework-2f2d21230697`.
- Full machine-readable analysis: `C:/Users/fpicc/AocRelease/framework-2f2d21230697/full-acceptance-7f1cb08.json`.
- Ran `Analyze-FrameworkPeriods.py`, including supply integrity, broad supply,
  four economic cases, workshop payments, Core/quests, procurement accrual and
  transactions, rewards, battle allocation, naval policy/acceptance, lifecycle,
  profitability and detailed evidence. Independently checked noble opening and
  closing balances, production totals and the terminal procurement ledger.
- One closed session, 16.0002967 campaign days, no segment integrity errors,
  no sequence/reload interval gaps, zero reported accounting error groups.
- The combined report has 24 coverage-gap locations, not 24 gameplay bugs:
  several repeat the same naval limit, and sixteen concern partial hero windows.

No game settings, saves, binaries, speed or diagnostic activation were changed.

## 1. Noble wealth and wallets

The 687 aligned noble-hero wallets, grouped into 97 stable clans, opened at
15,491,361 gold and closed at 15,340,427: **down 150,934 (about 0.97%)**.
These groups have no membership/window coverage gaps in the detailed report.
This is hero cash grouped by clan, not total asset value or every clan account.

Observed net changes by caller context, using canonical wallet changes once:

| Caller context | Net gold |
| --- | ---: |
| Finance settlement | +303,064 |
| Rewards, including ordinary battles | +163,153 |
| Trade | -43,571 |
| Expense calculation, chiefly party/garrison expense chain | -562,993 |
| Other recorded caller chains | -10,587 |
| Total | -150,934 |

Context buckets are not independent economic models. Finance settlement already
contains native income/expense decisions; do not add workshop distributions to
these totals again. Other caller chains remain recorded, not missing wallet cash.
Largest clan gain in this cohort: `clan_empire_south_2`, +36,363, while the
aggregate fell. A profitable individual clan does not establish global inflation.

Final unexplained wallet residuals are zero for observed hero, clan, kingdom,
workshop and town wallets. Sixteen other hero wallets lack a full window; nine
also lack clan identity. They are not silently included in the aligned noble
cohort or treated as verified new wealth.

Verdict: no evidence for a blanket noble-income reduction from this run. The
historical 28.19-million increase remains a different capture and is not
retroactively explained by these results. This is not a controlled before/after
experiment proving the naval factor caused the entire wealth change.

## 2. Procurement and payment conservation

- 109 committed transactions, all reconciled against workshop/town endpoints.
- 539 accounting receipts, 16 cost-basis intervals, no accrual or transaction
  problems and no coverage gaps in these checks.
- 94 arrivals and 430 consumption receipts. These are different event units,
  not quantities that should equal the order count.
- The terminal ledger contains 42 remaining orders: 27 arrived with remaining
  stock and 15 not yet arrived. Thus the 109-minus-94 arrival difference is
  accounted for as outstanding orders, not demonstrated lost cargo.
- Terminal ledger fault is null and new orders remain accepted.
- Return, liquidation, rollback and failed-transaction branches did not occur.
  Their absence is missing live coverage, not a fault requiring forced failures.

## 3. Workshop accounting and operating results

There were 12,135 attempts: 8,739 successful and 3,396 unsuccessful. The narrower
capital-affecting payment check reconciled 6,184 successful batches with no
batch discrepancy or unmapped nonproduction wallet. These denominators differ
because not every production cycle is a capital-affecting batch.

All 228 workshop operating-result records reconcile for the observed period,
with no remaining profitability cash-evidence gaps. Inputs purchased into cargo
are recognized when consumed, rather than expensed twice. Owner withdrawals
are distributions, not operating costs. The report retains its explicit native
daily-expense inference limitation; this is not a forecast of future profit.

| Focus type | Shops | 16-day operating result | Gold/shop/day, rounded | Owner withdrawals, separate |
| --- | ---: | ---: | ---: | ---: |
| Wine press | 10 | +6,030 | 37.7 | 1,685 |
| Smithy | 14 | +76,384 | 341.0 | 26,502 |
| Linen weavery | 22 | +75,790 | 215.3 | 26,579 |
| Wool weavery | 25 | +195,799 | 489.5 | 48,739 |

Smithies and linen workshops are not generally unprofitable in this run. Wine
is substantially weaker, but nine of ten wine presses are nonnegative here.
There is no evidence-backed reason to buff all three categories together.

## 4. Four loss-making workshops: cause and impact

| Workshop | Observed result | Evidence-backed explanation |
| --- | ---: | --- |
| Lageta olive press, town_EW1/workshop:59 | -368 | No successful paid production; 16 days of 23-gold expenses. Seven failed input checks; input-available checks did not result in output. Planner found no viable supplier on all 16 decisions; reserve, route and destination cash filters occurred. |
| Askar silversmithy, town_A7/workshop:104 | -368 | No production; five failed silver input checks spanning approximately 15 days. No viable supplier on 16 planner decisions. Supplier reserve and route filters, not capital/profitability filters, blocked candidates. |
| Qasira wool weavery, town_A8/workshop:106 | -368 | Neither wool recipe produced. Twenty failed input checks across its two recipes, zero successful input checks. No viable supplier on 16 decisions; 800 route-filter and 192 reserve-filter counts, with no capital/profitability rejection. |
| Galend wine press, town_V5/workshop:224 | -23 | Five successful cycles, 474 output receipts minus 54 local input cost, 75 recognized procurement cost and 368 operating expense. Six gold withdrawn by owner is separate. Five failed attempts: two input, two town-cash candidates and one margin-hurdle candidate. |

Repeated filter counts are candidate evaluations, not independent shortages.
Source inspection of `ProcurementPlanner.cs` shows `routeRejected` combines
invalid/nonpositive land distance and distance above the configured maximum.
The capture cannot split those cases or identify every rejected supplier. Do
not conclude that a route is broken or expand all routes from this aggregate.
Reserve rejections show a safety restriction was reached, not that the reserve
is wrong. Missing between-attempt stock samples also prevent a claim of
continuous shortage throughout each failure span.

Galend's arithmetic is fully explained; no payment repair is indicated. Its
low throughput and fixed expense explain the small loss. More grape supply
alone would not address its town-cash and margin gates.

## 5. Supply chains and production gates

Observed recipes ran, with all broad stock windows aligned and zero stock
reconciliation residuals:

| Chain | Inputs consumed | Outputs recorded |
| --- | --- | --- |
| Cattle | 587 cattle | 2,348 meat + 1,174 hides |
| Hogs | 1,623 hogs | 3,246 meat |
| Sheep | 580 sheep | 580 meat |
| Wool recipes | 462 wool | 864 felt + 60 garments |

Livestock recipes: 2,790 successful and 2,352 failed attempts. Failed-gate
classification: 1,782 input, 536 effective-margin and 34 town-cash candidates.
Wool recipes: 462 successes and 301 failures; 276 input, 16 town-cash and nine
margin candidates. These observed failures support local access/throughput
review, not a global conversion-rate defect or automatic production multiplier.

Focus workshop failed-gate classifications:

| Type | Input | Town cash candidate | Margin candidate |
| --- | ---: | ---: | ---: |
| Wine | 23 | 15 | 3 |
| Smithy | 98 | 5 | 25 |
| Linen | 17 | 4 | 0 |

Town payments and stock movements reconcile; that does not mean every town has
sufficient cash at every production attempt. Nor do unsold stocks or margin
rejections alone prove weak consumer demand. 880 declared recipe identities had
no observed attempt; slow cadence/inactivity must remain separate from failure.

## 6. Naval, lifecycle, Core and quests

- Naval policy enabled at 0.01. Reconciled: 29 battle payments, 17 sale/transfer
  operations, six recovery/cleanup operations, one valid zero-payment patrol.
- The patrol's 17,663 allocation was not a payment entitlement; the report bug
  is fixed and replayed. No naval discrepancy remains in observed receipts.
- 1,182 native naval reward scopes, 16 recovery-valued ships; no player scopes.
- Detailed lifecycle observed 846 executed party-removal calls and 2,248
  ownership-change calls without reported lifecycle integrity problems. These
  include calls with no ships and are not counts of ships destroyed.
- Mixed player/AI naval battles and player selling penalties remain unverified.
- Core valid across 17 snapshots, one policy revision and one companion build.
  No finite or never-ending quest was active, so deadline creation/completion
  and player quest timing cannot be certified from this run.

## 7. Diagnostics and performance

- Capture ended normally at its 16-day bound; game was left running.
- 2,856,916 records, about 1.04 GB, zero dropped or pending records at closure.
- 734.91 seconds total elapsed, 45.93 seconds/day including pauses.
- Last eight recorded daily intervals: about 40.05 seconds/day.
- Writer/flush work: 13.33 seconds, 1.81% of elapsed time, a lower bound only.
  Snapshot/stack/status overhead and FPS impact are not measured by that number.
- Procurement planner compute samples total 1.315 seconds; largest decision
  29.54 ms. This excludes logging and does not cover all diagnostic cost.
- Observed daily growth extrapolates to roughly 1.65–2.33 GB per 30 days,
  below the 5 GiB cap at this run's rate; it is not a guaranteed future maximum.

## Recommended priorities

1. **No blanket money or production changes.** Preserve the working payment and
   procurement accounting. The current evidence does not justify global cuts,
   smithy/linen buffs or a new hull factor.
2. **Target the three idle loss-makers.** Replay supplier-route and reserve
   decisions against the recorded locations and inspect the map/route policy.
   Split the overloaded route-rejection reason before claiming unreachable
   routes versus intentionally excessive distance. Any policy exception should
   be bounded and tested against exporter reserve and transport ownership.
3. **Tune wine locally, if desired, after those constraints.** Galend needs only
   23 more operating gold to break even over this window. Do not ignore the
   cash/margin failures or double-charge the 75-gold procurement inventory.
4. **Close targeted certification gaps, not another generic soak.** Player naval
   branches and quest deadlines need appropriate scenarios only if they are in
   the release's acceptance scope. Rare procurement failures should remain
   fixture-tested rather than deliberately damaging a user's campaign.
5. **Reduce routine diagnostic volume after acceptance.** Keep summaries and
   targeted detailed capture for unresolved cases. No logging settings changed
   during this review.

The full capture analysis is complete. Broad accounting errors are not currently
demonstrated; local supply reach and wine throughput are the actionable tuning
areas. Remaining uncertainty is explicit rather than a reason for another
unchanged 16-day test.
