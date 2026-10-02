# Rolling framework diagnostics — implementation status

## Current status

As of 2 October 2026, use `ECONOMY_GOAL_COMPLETION_AUDIT.md` for the current
requirement-by-requirement verdict. The complete offline gate passes 14 checks
and 274 analyzer tests, including the wallet-identity and incident-retention
correction. The completed 16-day campaign provides transaction evidence but does
not prove a live 30-day rollover. No deployment occurred during this audit.

## Earlier implementation checkpoint

The following status predates the September completed-run replay. Its remaining
work and test counts are historical; the current audit above supersedes them.

Implemented, offline verified, **not deployed or live-certified**:

- Opt-in 30-campaign-day rolling log, persistent reload activation, owned-file
  replacement, checkpoint continuity and explicit safe diagnostic closure.
- Current cargo/cost-basis snapshots, committed movement/accounting receipts,
  transaction-scoped independent wallet changes and workshop/town identity joins.
- Observed workshop operating-result calculation without double-expensing prepaid
  inventory, with owner withdrawals and unclassified capital reported separately.
- Battle allocation/wallet checks and naval valuation, post-penalty requested
  payout, wallet and later ship-destruction observations.
- Per-segment combined findings and module-aware lifecycle coverage inventory.
  Missing arrival/consumption/return/liquidation, rollback/failure, battle or enabled
  naval paths remain visible. Optional absence is distinct from unknown availability.
- A compact per-segment verdict now summarizes captured workshop reconciliation,
  procurement findings and explicit naval player-penalty/ship-valuation coverage.
  It is navigation only: raw records and named gaps remain authoritative.
- At a healthy rolling-capture closure, the sidecar writes
  `AocFramework-current.summary.json` atomically beside the current log. It is
  an observed-counter receipt for the just-closed segment, not a balance pass or
  an aggregate across reload segments; the offline analyzers remain authoritative.

Remaining: live reload/closure/overhead/rollover validation; coverage of actual
campaign transactions and reward outcomes; resolution of old stock residuals;
independent player-penalty formula validation; economic conclusions based on matched
cohorts. No blanket income or production-rate changes are justified by these tests.
Current analyzer suite: 99 tests pass; diagnostics Release build is clean. Earlier
test counts below describe prior checkpoints, not current certification.

## Safe diagnostic closure

September 26 summary correction: the runtime workshop failure counter now accepts
the observer's actual `failed_see_gates` event (and retains `failed` compatibility).
These are unsuccessful production attempts; inspect gates before diagnosing input
shortages. Earlier zero-failure summaries are unreliable. The September 25
17.024-day log records 13,123 attempts: 9,377 successful and 3,746 unsuccessful.
Passing batch/payment joins does not resolve unexplained whole-wallet residuals
or certify economy-wide balance. Existing raw captures remain the evidence source.

An explicit `AocFramework.close.request` file in the diagnostics directory asks
the active capture to close at the next safe application update while Campaign
still exists. No request was placed in the user's directory during development.
The handler defers while a native root, procurement transfer or reward scope is
open, then takes terminal stock/framework/current-ledger snapshots before the
SESSION_END row. It closes diagnostics only: no pause, save, checkpoint or game
shutdown. Normal game/module teardown remains explicitly incomplete; do not
substitute it for this evidence-close path. The close request is acknowledged by
removal after handling. Check SESSION_END/checkpoint and analyzer coverage rather
than treating disappearance of a request as proof of a valid log. An IO/snapshot
failure is incomplete evidence; inspect any remaining request before another run.
Fixture verification covers deferral, terminal-before-end ordering and acknowledgement.
The native rolling/capture suite and Release build pass; in-game acceptance remains
required. This protocol is not yet a user-facing control or deployment approval.

## Development record (2026-09-20; historical intermediate states)

The following chronological notes preserve rationale and intermediate limitations.
Statements that work was unfinished describe that checkpoint; use Current status
above for present implementation and remaining work.

The opt-in supply/framework capture now targets 30 campaign days. In rolling
mode it writes only `AocFramework-current.tsv` in the existing Bannerlord
Documents diagnostics directory, then truncates that same owned file and starts
a fresh period automatically. Prior AocSupply-*.tsv soak files are untouched.
Overwritten current-period data is not recoverable from this logger. Export it
before rollover if it is needed for an audit.

Campaign days, not calendar month names or wall time, determine rollover. Pauses
do not expire rolling mode. A 5 GiB cap or observer/IO failure stops logging with
an incomplete/failure indication; it never changes gameplay, saves or speed.
One-shot fixture capture retains its 60-minute wall limit.

New workshop snapshots include their canonical cash-wallet identity and workshop
tag. Expense-only workshops can therefore be mapped without a successful
production cycle. Wallet baselines reset on rollover, while native hook bindings
remain installed. Old logs without these IDs retain explicit unmapped expenses.

Procurement also exposes a separate read-only CurrentLedger ABI. Capture opening,
daily and terminal snapshots contain remaining cargo, goods cost, freight cost,
arrival state and persisted policy/identity. This does not overwrite the raw loaded
save payload, execute a save or repair a partial batch. Invalid/partial ledgers
produce unavailable evidence rather than a fabricated empty inventory.
Analyze-ProcurementAccrual.py verifies payload hashes and extracts remaining
cost basis using the same integer allocation rule as production. It is not full
net-profit certification: returns/refunds and removed orders still need cash joins.

PROCUREMENT_ACCOUNTING now records one committed order-level receipt for dispatch,
each completed input batch, return and liquidation. It includes stable order/shop
identity, original goods/freight costs, remaining basis before/after and workshop
cash delta. Multi-input batches recognize cost once. Dispatch is prepaid inventory,
not an immediate duplicate expense; returns recognize unrecovered basis, while
liquidation can yield a gain. The analyzer rejects duplicate/malformed receipts.
Receipts do not replace independent native wallet reconciliation or prove full
coverage when observation starts midway through an order.

The accrual analyzer now replays committed accounting and item movements between
current ledger snapshots. It detects basis discontinuities, cargo residuals,
unexplained order additions/removals and identity changes. Loaded-save payloads
are provenance only, never substituted for current inventory. Missing opening or
closing evidence and legacy orders without joinable IDs remain explicit gaps.
This closes snapshot-to-receipt checks, not the independent native cash-wallet
join or complete workshop net-profit calculation.

An additional aggregate cash check now maps canonical workshop wallets through
the sampled workshop tag to procurement ledger keys. It compares committed
receipt cash with independently observed capital changes whose captured caller
chain identifies ProcurementBehavior or ProcurementTransfer. Unrelated output
income is excluded. Missing wallet mappings and nonzero residuals are explicit
coverage gaps: inlining/truncated caller chains can remove attribution. Even zero
aggregate residual is not transaction-level proof, cannot exclude offsetting
errors and does not reconcile the paying/receiving town endpoint. Full cash joins
and net-profit certification therefore remain unfinished. The updated analyzer
regression suite passes 84 tests; diagnostics Release build is clean.

Procurement now publishes a transaction boundary around dispatch/return/liquidation
transfers: a unique ID, order, workshop key, stage and begin/committed/rolled_back/
failed outcome. Independent WALLET_CHANGE rows carry that ID while the transfer
and any rollback execute, covering both workshop and town mutations. The optional
observer callback cannot change transfer results; the transfer helper remains
independent of diagnostics/game APIs. The bridge detaches and clears context on
capture close, rejects nested or mismatched boundaries, and does not affect saves.
The native candidate suite passes, with 91 focused procurement assertions including
stable commit identity and six rollback cases. Transaction-level analyzer joins
still need to consume these new records before they can certify endpoint coverage.

Analyze-ProcurementTransactions.py now consumes those boundaries and independent
wallet rows. It joins each committed transfer to its following order/stage/shop
accounting receipt, checks workshop debit/refund against town credit/payment,
checks mapped workshop identity and requires zero net change at each wallet for
reported rollbacks. It rejects nested/reused IDs, mismatched ends and out-of-scope
cash, and exposes missing receipts/endpoints as gaps. The combined period report
includes its findings. Four regressions cover matching endpoints, missing town,
wrong workshop, rollback residuals and unexercised captures. The suite passes 92
tests and Release builds cleanly. Town endpoint identity still needs a source/
destination mapping check; sums alone cannot certify the correct town was paid.

Town wallet baselines now expose their settlement ID. The transaction analyzer
joins that identity to the market on committed dispatch/return/liquidation cargo
receipts, rejecting a balanced payment to a different town. Missing mappings or
movement evidence remain coverage gaps. This checks actual cargo/cash agreement,
not whether supplier selection itself chose the intended policy-optimal town.
The regression suite passes 93 tests. The cash-native verifier now accepts the
exact candidate DLL path and is included in the main candidate gate; its nested
cash/alias/restoration checks pass against the new build. An initial standalone
run failed because its default loaded an older DLL; no old artifact was replaced
to resolve that test-path mismatch. Diagnostics Release build is clean.

The combined segment report now includes WorkshopProfitability.py. It joins
sampled workshop tags to procurement ledger keys and calculates observed operating
result from paid outputs, native input cash, committed consumed goods/freight
basis, realized return/liquidation losses or gains, and observed operating costs.
Dispatch cash is prepaid inventory, not a second expense; owner withdrawals are
reported separately as distributions. Production quote estimates are not added
again after committed basis is recognized. Unclassified capital movements and
incomplete transaction/accrual/payment evidence remain named gaps. Daily native
expense attribution is still explicitly inferred, and the output is not certified
total profit. The 96-test suite covers double-charge prevention, owner draws,
liquidation gains, unknown capital and missing identity; Release build is clean.

End-to-end transaction observation verification also passes: the actual optional
ABI has one listener after repeated subscription, sets/clears cash context at
begin/commit, and clears/detaches on capture closure during an unfinished transfer.
The actual cash observer serializes the ID on nested mutations but not subsequent
unrelated cash. These checks used temporary fixture packages/logs, not game saves.
The full offline candidate gate was rerun successfully after these assertions,
including clean Release builds, 96 analyzer tests and protected baseline checks.
This does not replace live campaign/overhead validation.

Reload-boundary correction: each written observation now validates its campaign
time and advances the checkpoint's last-observed day, rather than waiting for
the next campaign tick. Previously a reload between the last tick and a later
written row could be admitted by the checkpoint and subsequently produce a
backward-time log. The fixture now writes between ticks, verifies the persisted
day and rejects a reload specifically inside that interval without changing the
old file. Nonfinite/backward observation clocks fail capture closed.

Reward diagnostics now observe native MapEventParty.CommitGoldChanges allocations
before payout and after reset, recipient cash and a reward ID attached to wallet
rows. Optional NavalDLC distribution/recovery boundaries record original ship
identities/hulls/health, actual evaluated trade values and post-call membership/
owner evidence. Nested reward contexts are linked, not summed as extra income.
Ship removal from one party is not proof of destruction; a recovery payout can
precede cleanup in another method. The value tap reads the native lambda result
once and never re-evaluates the model/RNG. Player penalty interpretation and
asset-lifecycle joins remain required before certifying reward balance.

Native IL follow-up: NavalDLC calls distribution/recovery from
OnMobilePartyDestroyed (clan party, not at sea) and OnPartyDisbanded (non-bandit
clan). DestroyPartyAction.ApplyInternal dispatches OnMobilePartyDestroyed before
MobileParty.RemoveParty. RemoveParty then iterates remaining ships and calls
DestroyShipAction.Apply. Thus a ship still present immediately after recovery is
not itself evidence of duplicate assets: cleanup is later in the native path.
Inspect-RewardTargets.ps1 now checks the event-before-removal ordering and presence
of the ship-destruction call. This proves installed-code intent, not successful
runtime cleanup or absence of third-party interference. Live observation must
join paid ship IDs to the later destruction action, outside the reward scope.

That observation is now implemented: when NavalDLC is present, the diagnostics
sidecar adds a finalizer to native 1.4.8 DestroyShipAction.Apply(Ship). Successful
completion emits SHIP_DESTRUCTION with the same weak object identity as valuation
and the remaining owner state. It does not invoke destruction, retain ships or
suppress native exceptions. Signature changes fail capture preflight; native or
observer exceptions close the capture safely. The reward analyzer joins later
cleanup outside the reward stack, rejects duplicate destruction/valuation evidence
and reports missing cleanup or retained ownership separately. A completed action
is not an independent global inventory audit. The native fixture checks target
installation and stable post-reward identity; the analyzer fixture exercises
missing cleanup, later completion and retained ownership. 85 Python tests pass,
the native reward fixture passes, and diagnostics Release builds without warnings.
No live ship destruction was performed or deployment made during these checks.

Naval reward starts now identify player-clan ownership. The analyzer joins the
canonical recipient's GOLD_TRANSFER_ENDPOINT request to that exact recovery
scope and compares requested gold (after native rounding/player penalty) with
actual wallet change. Gross endpoints are not added to wallet cash or propagated
as duplicate parent income. Missing request evidence, invalid endpoint contracts
and request/cash differences remain visible. This observes the native result;
it does not independently validate the selling-penalty formula or certify game
balance. The updated 86-test analyzer suite, native reward fixture and clean
Release build pass; live reward acceptance remains outstanding.

Live reload evidence later confirmed a native duplicate naval recovery payout:
the same two ship identities were valued and paid twice (31,513 gold each time)
through the adjacent destruction and disband callbacks before either ship was
removed. The Core Campaign Systems **source candidate** contains an optional,
version-locked Harmony prefix/finalizer on the one native distribution method.
It allows the first native call unchanged, blocks only a later call for that
same live party after successful completion, and removes the gate when native
code throws so a native retry remains possible. A missing/changed NavalDLC
target leaves native behavior unchanged and traces the reason. This candidate
is not installed or live-certified until a clean game-closed deployment and
fresh naval reward acceptance run verify the first-only payment result.

Analyze-RewardAccounting.py independently joins reward contexts to canonical
wallet changes, checks battle allocation/reset observations, and reports missing
naval valuations. Nested contexts are alternative checks of the same cash flow,
not additive income. Unknown records, missing ship identities and duplicate
valuations are rejected rather than silently interpreted or overwritten. This
analyzer is included in the closed-period segment report; an unexercised reward
path remains a coverage gap, not a passing balance result.

Exact 1.4.8 target signatures and valuation call IL were inspected locally with
Inspect-RewardTargets.ps1. Verify-RewardObservation.ps1 checks native hook binding,
single observed values, nested restoration and fail-closed exception handling.
It does not certify rewards against live ship/battle outcomes. Optional absence
is reported explicitly; changed signatures block capture rather than gameplay.

Verification: isolated Release build; native supply/quest/lifecycle suite;
automatic repeated rollover, no early rollover, fresh session/sequence, continued
capture and paused-wall-time tests; Python expense-only attribution regression.

Reload continuity now uses a diagnostics-only checkpoint beside the current log.
Normal reload of the same campaign appends a new identified capture segment while
retaining the original 30-day anchor. The opt-in marker becomes persistent as
`AocFrameworkDiagnostics.enabled`; removing it disables automatic activation at
the next campaign load. No campaign save data is added. Different campaigns,
backward time, interrupted capture and mismatched file length are rejected with
the old log preserved. Rollover uses atomic file replacement. A failure between
log replacement and checkpoint publication is rejected, not silently repaired.

Analyze-FrameworkPeriods.py now streams a closed current-cycle log into separate
temporary capture segments and invokes the existing economy/framework/accrual
analyzers for each. It rejects mixed campaigns, reused session IDs, overlaps,
sequence gaps and incomplete rows. Time between segments is reported as unobserved,
not invented production or elapsed economic coverage. Wallet baselines are never
summed across reloads. Active/interrupted checkpoints return no integrity verdict
and are not held open during full analysis, avoiding interference with rotation.
Missing coverage remains present in each segment's report. Temporary segments are
removed by the analyzer; original logs are never edited.

Each real segment evaluation now includes `combined_findings`: paths and counts
for reported integrity/payment problems and evidence gaps across framework,
workshop, stock, accrual and reward reports. It never emits an economic PASS;
an empty finding list still requires review. The economy report also includes
the competing-demand breakdown, not just residual totals. Empty captures are
rejected, and malformed checkpoint lengths produce an explicit invalid-checkpoint
result. Regression coverage includes nested findings and absent evidence; the
88-test Python suite and diagnostics Release build pass.

Not yet complete or deployed: full reward/asset lifecycle joins, complete
cargo/freight accrual reconciliation and the final combined coverage contract.
Single-session analyzers must not be run directly on a resumed multi-segment log
and treated as a valid aggregate. In-game reload acceptance remains untested; fixture coverage
does not certify live crash recovery or disk failures.
No existing user log was overwritten during tests; fixtures used temporary paths.

Competing demand is now exposed by Analyze-BroadSupplyCapture.py as per-town,
per-category net stock changes grouped by native market operation, workshop
market changes, villager sale scope and committed procurement movements. Nested
market scopes are excluded from the additive totals. Workshop consumption events
are separately grouped by recipe because they can draw from private/warehouse
cargo, not just the town market. Party exports do not prove party consumption.

The Campaign Systems `Tests/Verify-Candidate.ps1` offline gate now runs the
rolling-log and reward-observation fixtures against its freshly built diagnostics
assembly, in addition to native capture and analyzer regression checks. The
combined run passed: 83 Python tests, 84 procurement assertions, 40 framework
assertions, 11 native configuration/lifecycle assertions, Logistics bridge/supply
checks, rolling reload/rotation checks and reward hook/nesting checks. Release
builds had zero warnings/errors; protected baseline/calendar/map checks passed.
Expected invalid/locked configuration messages came from temporary negative-test
fixtures, not a running campaign. This is not live acceptance or release/security
certification; no deployment or game launch was performed.
