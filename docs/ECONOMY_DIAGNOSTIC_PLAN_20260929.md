# Targeted economic diagnostics research

Status: implementation underway, not deployed. Research date: 29 September 2026 UTC.
See [causal diagnostics implementation](ECONOMY_CAUSAL_DIAGNOSTICS.md) for the
implemented subset, verification and explicit remaining coverage limits. The
ordered additions below remain a research specification, not a claim that every
item has shipped.

## Finding

The missing evidence is causal attribution and decision detail, not an absence
of transaction logging. Preserve the reconciled wallet/cargo accounting and add
small, linked observations to the actual combined SupplyCapture path.

CalendarSoakBehavior.OnSessionLaunched selects supply-only mode and returns
before the older EconomyTransactionDiagnostics startup when the supply marker
exists. EconomyRecipeDiagnostics WORKSHOP_ELIGIBILITY and EconomyStateDiagnostics
TOWN_CONTEXT belong to that other path. Their absence from the completed combined
capture cannot be attributed solely to an old DLL. Verify the active mode and
event families, not just assembly installation. Do not enable both observers
blindly: overlapping hooks and independent accounting would require coexistence
tests and would increase cost.

Installed diagnostics hash checked in this research:
`B0299CA4CA2ED5E0634846E3D34F0DC8062F71AF4AD37222A0232C54194DD068`.
The last deployment deliberately updated only procurement.

## Ordered additions

### P0: town cash recovery and trade settlement attribution

Observe the effective runtime SettlementEconomyModel.GetTownGoldChange result
at its original invocation, and the caller that applies the result. Record town,
campaign day, prosperity, cash before, observed model result, requested change,
actual net wallet delta, cash after, effective method/MVID and patch ownership.
Resolve and preflight the installed 1.4.8 caller before patching; a method name
in an older API page is not a verified runtime target. Preserve unknown attribution
when a model call is absent, skipped, inlined or cannot be joined.

Give each town purchase/sale an operation ID linking existing MARKET_BEGIN/END,
MARKET_GOLD_CALL, WALLET_CHANGE, inventory changes and trade-tax accrual. Include
seller/buyer endpoint identities, quantity, observed price direction, gross
payment, every secondary debit/credit and any tax-accrual delta. Tax accrual is
not automatically a spendable wallet or a second transfer. Distinguish native
explicit deductions, transfers, rounding/clamping and unexplained differences.

Why: Lageta's prior town cash fell 25,663 to 118 with zero final residual, yet
some daily credits remained an unnamed callback. Caravan purchasing also had
both incoming and outgoing town changes. Stack text does not establish the
economic meaning of those secondary changes. Do not declare a missing-money
bug or call the daily credit a subsidy from timing alone.

### P0: failed workshop attempt witnesses

Extend existing SupplyWorkshopObserver events instead of creating a second
workshop ledger. At the actual input check record each required category and
quantity, market stock, private eligible stock, private blocked stock with reason,
and arrived/in-transit distinction. At native approval record input cost,
capital, town cash, output quote, effective margin and actual boolean result.

Produce derived labels cash/input/capital/margin/unknown from those observations.
Keep the native result separate from reconstructed predicates; multiple predicates
can fail. Record original-ran/exception evidence where supported. Unknown or
conflicting patch behavior must not be mislabeled a native condition. Add daily
eligibility context (rebellion, siege, food stores) to the supply path itself.

Why: Askar and Qasira failed inputs, while Lageta and Galend also failed cash.
Daily zero stock is not equivalent to insufficient stock at the actual attempt.
Existing gate values already permit much cash/margin classification offline;
improve that analyzer without requiring another run for old evidence.

### P1: supplier rejection witnesses

Extend ProcurementPlanner decision observations with bounded, deterministic
examples per workshop/recipe/day, using values already computed by the planner.
Record source/destination IDs, lot size, distance and configured limit, stock,
calculated reserve, exportable units, actual item-stack eligibility, food buffer,
freight, required capital and margin. Split invalid/nonfinite/unreachable route
from an ordinary over-limit route when the native result allows that distinction.
Do not infer physical unreachability merely from an invalid result. Split trade
reasons (war, siege, food, missing faction) and category reserve versus missing
eligible item stack/invalid quote. Preserve the original aggregate fields.

Emit evaluated lots and selected lot, plus a bounded example where both endpoints
fail but an intermediate lot passes, to assess the deployed search fix. Count
unique workshop-days separately from candidate rejections; searching more lots
must not appear to worsen shortages. Rejected paths not reached remain unknown,
not assumed passed. Keep total counts plus omitted-detail counts.

### P1: per-town/category supply reconciliation

Use existing production, consumption, MARKET_DELTA and procurement movements to
build daily opening + inflows - outflows = closing reports, with residuals.
Separate local production, villager deliveries, caravan imports/exports,
workshop consumption/output, household consumption, overproduction deletion and
private cargo. Add only missing endpoint observations discovered by those joins.
Explicitly preserve net-of-nested accounting; do not sum parent and child market
deltas or mix native events with roster deltas as independent item flows.

Prioritize olives/oil, silver/jewelry, wool/felt/garments and grapes/wine in the
four weak towns. This distinguishes distribution from production and weak demand.
Do not estimate continuous stockout hours from sparse daily snapshots.

### P0 prerequisite: coverage contract and P1 cost measurement

Extend readiness with capture mode/schema, supported event families and per-family
execution counts/first-last occurrence. Existing readiness already checks loaded
MVIDs, hook identity, writer flush, PID/session and dropped records. Preserve it.
Report installed, recording and exercised as separate states. Zero executions
is NOT_EXERCISED, not PASS. Daily-summary obligations can be checked after a full
observed day; condition-dependent events must not be required to occur artificially.

Existing DIAGNOSTIC_COST explicitly excludes snapshots, stack traces and status
IO. Add observer-exclusive stopwatch timings, invocation counts and maxima,
serialization/flush/status times, queue peaks, bytes/day and dropped/suppressed
counts. Avoid adding nested durations together as total overhead. Whole-game
performance effects still require a controlled comparison; instrumentation time
alone does not measure all allocation/GC effects.

## Implementation and acceptance design

- Reuse current TSV writer, session IDs, market/run/cycle/order IDs and optional
  procurement diagnostics ABI. Add explicit cross-links instead of guessing
  joins from timestamps. Namespace IDs by session and operation family.
- Diagnostics remains opt-in and separate from production policy. No protected
  Core rebuild, game-speed change, save mutation or model/RNG re-evaluation.
- Prefix snapshots plus observing postfix/finalizer hooks; never replace results
  or suppress exceptions. Preflight exact signatures and account for competing
  prefixes/skips. Failure invalidates the capture, not gameplay rules.
- Keep exact mutation totals/reconciliation unsampled within the declared scope.
  Bound repetitive explanation rows, not arbitrary individual transfer rows.
  Optional focused captures must include every inflow/outflow of selected wallets,
  not just selected goods, and report their coverage boundary explicitly.
- Start with the existing 30-day rotation and 5-GB limit unchanged. Proposed
  witness caps are policy choices to benchmark, not guaranteed file-size savings.
  No promise that the new logs will be smaller or faster until measured.
- Offline fixtures before requesting gameplay: positive/negative/zero cash recovery;
  nested trade/tax changes; skipped originals/exceptions; market versus private
  input failure; reserve/distance/margin boundaries; intermediate lots; missing
  event-family rejection; serialization/rotation/session rollback; observer-on/off
  equality for money, stock, game results and call counts.
- Replay the existing capture to verify all previously matched money/cargo totals
  stay matched, with absent new fields labeled unavailable. Package-level checks
  must exercise SupplyCapture mode specifically, not only EconomyTrace mode.
- A subsequent planned live run can cover all these questions together. No new
  run is requested by this research. Offline tests cannot supply historical events
  that were never captured or certify live balance.

## Primary research sources

- Microsoft: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts
  supports operation/parent IDs and linked units of work.
- Microsoft: https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/log-sampling
  supports consistent trace-level volume control. These modern APIs are design
  references, not a proposal to import a new telemetry stack into .NET Framework 4.7.2.
- Harmony: https://harmony.pardeike.net/v2/articles/patching-postfix.html
  and https://harmony.pardeike.net/v2/articles/patching-finalizer.html describe
  observation and exception boundaries. Installed Harmony 2.4.2 fixtures remain
  authoritative for compatibility.

Source reviewed: CalendarSoakBehavior, SupplyMarketObserver, SupplyCashObserver,
SupplyWorkshopObserver, SupplyCapture.WriteCost, CaptureReadiness,
EconomyCallerDiagnostics, EconomyRecipeDiagnostics, ProcurementObservation and
ProcurementPlanner. No implementation or deployment performed in this turn.

## Extended potential-issue coverage and audit

The user's follow-up requests add potential issues and a deeper audit. This is
still research/design, not installation of diagnostic hooks. The scope is
economics and connected framework/logistics/quest/naval boundaries, not unrelated
UI, borders, religion or every possible engine bug.

### Broader issue matrix

| Area | Additional evidence or derived report | False alarm to prevent |
|---|---|---|
| Noble wealth | Daily canonical wallet inflow/outflow by attributed grants, trade, workshop distributions, finance settlement, expenses and unknown; stable cohort and leader/owner changes | Rising wealth is not automatically minted money; a transfer between watched wallets is not aggregate income |
| Town insolvency | Attempt-time cash deficits, daily recovery result/application and transaction-linked secondary debits; first/last observed low-cash states | Snapshot lows do not prove continuous insolvency or exact hours below a threshold |
| Workshop distress | Operating result, expenses, owner withdrawals, prepaid inventory changes, observed capital, successful/failed attempts and no-attempt days | Withdrawal is not an operating loss; cumulative procurement payment is not all consumed-input expense |
| Supply-chain imbalance | Per-category stock conservation plus actual conversion events for livestock/meat/hides and wool/felt/garments; model rate beside realized attempts/output | Category labels and potential model rate do not prove actual production or demand |
| Market saturation | Native consumption/deletion, stock trajectory, executed sale quantity and actual quote; observed failed profit gates | High stock alone does not prove weak demand; deletion may be a native policy rather than item loss |
| Shipment stalls | Stable order ID, captured policy, expected arrival, arrived/blocked/return/quarantine state, last real transition, remaining units and cost basis | An in-transit order at capture end is not lost cargo; being due alone does not bypass hourly processing or blockade rules |
| Duplicate payments/cargo | Unique committed receipt IDs and complete dispatch/arrival/consumption/return equations, reconciled with opening and terminal ledgers | Duplicate observation of one event is not automatically duplicate gameplay execution |
| Price/quote disagreement | Planner quote item/category/modifier/direction and decision-time value versus separately observed execution-time batch quote | Prices can legitimately change during transit; a changed quote is not by itself a stale-price bug |
| Naval wealth/cleanup | Connect battle allocation, reward execution, ownership transfer, cleanup and sale by stable ship/reward IDs; player and AI coverage separately | Owner=null is not destruction; NPC coverage does not test player selling penalties; prefix-skipped callbacks are not executed rewards |
| Quest timing | Existing saved finite deadlines, lifecycle ID, creation/change/expiry observation, calendar policy and campaign-day reference | Never deadlines and no active quests are not successful expiry tests; quest disappearance may mean completion rather than timeout |
| Logistics integration | Loaded provider/version, read-only bridge status, observed reserve and transaction ownership/ID where exposed | Current bridge is not physical workshop shipping; unavailable provider is not a zero reserve |
| Calendar/reload | Campaign identity, session, day progression, policy/build IDs, observed per-system cadence and reload boundaries | Wall time includes pauses; a reload/rollback is not unexplained economic reversal within one continuous session |
| Diagnostic integrity | Missing execution families, gaps/duplicates, incomplete scopes, writer closure, discarded rows, hook changes and sample omissions | An open final scope while recording is not a completed-capture integrity failure |
| Diagnostic cost | Exclusive observer timing and calls, serialized bytes, queue/flush cost, allocation/GC context and size projections | Writer-only time is not total overhead; overlapping nested timers cannot be added naively |

These are possible failure classes, not findings that all these problems exist.
Use existing event families wherever they already answer the question. In
particular SupplyCategories.Initialize already includes registered mod-added
categories: a second hard-coded whole-economy stock logger is unnecessary.
CampaignSystemsObservation already records finite/Never deadlines and no-quest
coverage. SupplyShipLifecycleObserver already distinguishes executed/skipped
removal and ownership change from destruction. Extend joins/coverage before
adding duplicate hooks.

### Audit pass 1: causal completeness

**Passed design principle:** every proposed alert must link to original evidence,
not merely a guessed reason string. Transaction identity should connect decision,
native execution, actual cash/items and resulting ledger state.

**Gaps found and corrections:**

1. A town-gold model result does not prove its application. Capture and link both,
   with model-call context distinguishing authoritative update from preview calls.
2. Supplier rejection counts cannot identify a particular blocked supplier.
   Preserve bounded witnesses and denominators; a skipped later gate is unknown.
3. Daily quest snapshots cannot establish exact expiry execution. Lifecycle
   observation is conditional scope work; do not claim snapshots test timeouts.
4. Existing read-only logistics ABI cannot certify transport conservation.
   Report the capability boundary rather than pretend procurement cargo is a
   physical caravan shipment.
5. Full yearly wealth balance is not proven by a short stable cohort. Report
   entry/exit and death/leader changes separately from economic flows.

### Audit pass 2: non-interference and compatibility

**Risks found:** re-running prices/models can change execution or RNG consumption;
Harmony object-array argument handling can write values back; a postfix can run
even when another prefix skips the original; JIT inlining can hide a target;
turning on both capture pipelines can overlap observations.

**Required controls:** observe original arguments/results with typed bindings,
use explicit original-ran and exception handling, preserve native exceptions,
never alter time/saves or claim runtime coverage from installation alone. Verify
the effective model override, exact signature and installed patch ownership on
1.4.8. Where inlining prevents observation, report missing evidence or use a
verified enclosing boundary; do not force a balance change to compensate.
The current combined mode is the integration target. Additive schema fields and
optional diagnostic ABI calls must remain compatible with diagnostics disabled.

Relevant primary sources:

- https://harmony.pardeike.net/v2/articles/patching-injections.html documents
  __state, observing results, __runOriginal, and argument-array writeback.
- https://harmony.pardeike.net/v2/articles/patching-edgecases.html explains
  inlining and runtime patch limitations. No workaround is assumed tested here.

### Audit pass 3: useful warnings, bounded volume and acceptance honesty

Use three independent dimensions, not one green/red flag:

1. **Evidence:** complete / incomplete / not exercised / unsupported.
2. **Finding:** reconciled / observed discrepancy / potential balance concern.
3. **Attribution:** witnessed cause / inferred predicate / unknown.

For example, a wallet residual is an observed accounting discrepancy, not yet
proof that gameplay created money. Three consecutive failed workshop attempts
could be a configurable triage trigger, not a universal bug threshold. Pending
cargo warnings must use its own saved ETA/block/return policy rather than a
hard-coded elapsed-day threshold. A suspicious price difference needs both
quote identities and times. Recovered warnings should retain a first/last
witness and resolution, not disappear from the period summary.

Routine output should be a compact daily issue index with entity, severity,
first/last observation, occurrence count, evidence IDs, coverage and next check.
Keep exact ledger totals within scope. Bound repetitive context by per-entity
caps and report suppressed-detail counts. If using a memory ring buffer for
pre-warning context, declare its limits; it cannot retroactively reconstruct
omitted historical transactions. No separate archive or rotation change is
authorized by this plan: retain the user's 30-day replacement policy.

The acceptance fixture matrix must inject each failure class and its ordinary
lookalike: valid blockade, low cash, legal transfer, owner withdrawal, changed
quote, Never quest, skipped reward, open live scope, resumed session and writer
failure. It must demonstrate that invalid evidence does not receive a clean
bill of health and normal gameplay cases do not become confirmed bugs.

### Recommended delivery order

One coordinated diagnostic update, verified offline before requesting play:

1. Combined-mode coverage manifest and linked town cash/attempt witnesses.
2. Bounded supplier explanations and per-day issue index using existing money,
   stock, cargo, wealth and naval records.
3. Missing lifecycle observations only where native targets and capability
   boundaries are verified; explicit unavailable labels elsewhere.
4. Per-observer cost accounting and volume caps, then full fixture/replay gate.

Do not deploy an untested 'log everything' build. No fixed number of campaign
days can guarantee a player naval sale, an expiring quest or every rare event.
If a later run is needed, define its event-based acceptance conditions upfront
and keep unexercised areas visible rather than silently scheduling another run.

Assessment: targeted, correlated diagnostics are a strong fit for this problem;
the design remains unimplemented and cannot honestly be rated 10/10 readiness.
The three passes above are document/source audits, not three independent agents
or three runtime tests. No game was launched, no settings changed and no new
diagnostic code or DLL was installed.
