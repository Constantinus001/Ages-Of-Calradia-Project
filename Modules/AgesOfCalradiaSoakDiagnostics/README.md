# Ages of Calradia Soak Diagnostics

## One-shot short capture

`AocEconomyShortCapture.enabled` arms a raw capture on the next campaign session.
It bypasses the completed long-soak control and consumes the marker on startup.
It stops recording after three campaign days, 180 wall seconds (including pauses),
or 256 MiB, whichever occurs first. It does not select speed, save, quit or restart
the game. Use 2x manually. Inspect SHORT_CAPTURE_STARTED, SHORT_CAPTURE_STOPPED,
SESSION_END and any FAILURE before accepting evidence. Capture limits are not a
coverage guarantee; an idle/paused capture may exercise no relevant transactions.
Startup/file/native-boundary failures are logged and stop capture safely.

## Bounded economy instrumentation candidate

The active run path records bounded daily campaign-level evidence: every town's
food, prosperity, gold and staple market signals; every workshop's speed/capital/
profit; every clan's treasury, tribute, debt and non-mutating expense model;
every party with a wage; plus monthly hero ages, siege state, wars, peace,
tournaments, save/reload and diagnostic failures. It is the evidence needed to
audit a campaign run without recording every internal method call.

The September 12 raw-logger overflow is addressed by a separate bounded daily
aggregation sink. This candidate installs observer hooks at an armed campaign's
session launch and defaults to `AocEconomyDaily-<session>.tsv`, NOT a raw ledger.
It has been built locally; deployment, runtime overhead and real flow coverage
must be verified separately. Building does not start a run or change game files.

Daily aggregates retain count, net, positive and negative flows, first/last
values and last context. Inventory rows group by item and instrumented caller,
not every roster/modifier; this deliberately cannot establish full transaction
attribution. Financial rows retain owner and up to five instrumented parent
methods. Daily-summary mode does not unwind stacks. Opt-in raw mode now adds
up to twelve caller method names to root money-action/mutation scopes, helping
identify grants or trading above the instrumented action. No file paths or local
values are captured. Inlined/missing frames remain a limitation; caller evidence
is not automatic economic classification. Native models are not repeated.

For an explicitly chosen SHORT transaction validation, create
`AocEconomyTransactions.enabled` in the diagnostics directory before loading
the campaign. This selects `AocEconomy-<session>.tsv` instead. Never put synthetic
fixtures in that directory. No marker is created by the build. The default
summary does not satisfy the raw-ledger runtime coverage gate.

Added evidence: per-recipe attempts/successes, input and profitability predicate
results, native-candidate failure reasons, fractional progress and derived
cadence, observed workshop/village model results, bankruptcy handler context,
owner/type/capital state, all recipe input/output market categories with explicit
zero stock, town rebellion/siege/loyalty, village production definitions and trade
destination, clan/kingdom balances, and wall seconds per campaign day including
pauses/stalls. Recipe model calls without identity remain explicitly unresolved;
the native per-run progress plus attempt count supplies the derived increment.
Food-category properties (including felt and livestock) are recorded unchanged.

Completed clan settlements are recorded even with zero net change. The raw gate
accepts a no-cash zero-net root only with a completed zero settlement AND an
observed zero `CalculateClanGoldChange` result. This is not individual wage
affordability proof. `capitalSurplusOverInitial` is the accurate profit-field
label; the old field remains as a compatibility alias in snapshot events.

Bounds: 50,000 aggregate keys per day, 5 GiB per summary session, 1 GiB per raw
session, 500,000 reconciled accounts. Exceeding these or observing IO/native
exceptions invalidates coverage and stops capture, never gameplay. Inventory
reconciliation uses an owner index rather than scanning every account for each
roster. Summaries flush at daily rollover/snapshots and session closure; raw
buffers flush at most once a second. Partial files without SESSION_END are not
accepted. No claim of year-long runtime overhead/volume readiness is made yet.

Checks: `Tests/Verify-EconomyDailySummary.ps1` tests gross/net offsets, zero-net
records, day rollover, inventory grouping, invalid numbers and actual native IL
patch installation in an isolated verifier process. `Verify-EconomyDailyCoverage.ps1`
checks completed summary evidence and explicitly does not certify transactions.
The existing transaction and gate verifiers cover raw evidence separately.

The launcher accepts `-TargetCalendarDays` (1 through 731; default 731) and
writes a fresh `AocSoakTargetDays.txt` only after backing up the prior control,
deadline and target files. A 365-day run is therefore a fresh year target, not
a reused prior-run control receipt. A wall-clock deadline is optional and is
never inherited into a fresh run.

An armed run has no intermediate checkpoint save or automatic relaunch. It
continues until its final campaign-day target, then makes one final dedicated
save and exits. Existing `NextCheckpointDay` fields remain in old control
receipts only for backward-compatible parsing and are ignored.

For this candidate, 57 audited static Bannerlord 1.4.8
methods receive observer-only Harmony prefixes/finalizers. The exact target
manifest is `EconomyDiagnosticPatches.Targets()`: hero and independent party
gold, settlement/workshop ChangeGold, food stock, kingdom/clan tribute wallets,
kingdom budget, clan debt, ItemRoster AddToCounts(EquipmentElement,int)/Clear,
GiveGoldAction transfers and SellItemsAction settlement trades, clan/notable
daily finance, selected native finance components and workshop
input/output/expense operations. No prefix skips originals; arguments, results,
and native exceptions are not replaced. Lord-party gold aliases leader gold and
is not counted twice. Finance components are explicitly **not cash**: follow their
parent daily transaction to actual balance mutations and CLAN_SETTLEMENT net.
Wage authorization and tribute obligations must not be presented as transfers
without that linked evidence. Components nested inside other components are not
additive; the scope tree preserves this distinction.

Each loaded campaign creates `AocEconomy-<session>.tsv` alongside existing logs.
Rows contain UTC/campaign day, session/sequence, transaction/parent IDs, stable
entity IDs, metric, before/after/actual delta, native caller, requested arguments,
and native exception status. Item IDs include modifiers; ROSTER_BIND maps a
session-local roster ID to its owning settlement/party. Unmapped temporary or
warehouse rosters remain identifiable but must not be called a known owner.
Workshop scopes identify their workshop even for warehouse inventory changes.

Daily snapshots now reconcile **all** living heroes, mobile parties, settlements,
workshops and kingdom/clan wallets, with inventories including disappearance to
zero. Stock deltas are separate from the food model's named contribution lines,
stock cap, and current stock. Item mutations record supplies/consumption actually
observed at this boundary, including callers such as villagers, caravans and
workshops; they do not assume every positive stock change is a delivery.
Deleted entities and unidentified temporary rosters are not independently
reconciled after removal; net-zero mutations that bypass every hook may evade
snapshot checks. This is explicit scoped coverage, not a claim to log every mod
or every possible game action. Other mods' model overrides/inlining may bypass
hooks: missing categories or UNEXPLAINED_DELTA must be investigated.

Compatibility and failure: native target assembly MVID and other Harmony owners
are logged; missing targets unpatch only this observer and invalidate coverage.
Original methods continue. IO/nonfinite/native-observed exceptions and unexpected
thread access log ECONOMY_FAILURE and disable transaction capture, not gameplay.
Buffers flush each second, at snapshots and before saves/unload. A session file
over 1 GiB or 500,000 accounting keys invalidates coverage instead of silently
dropping data. These bounds and detailed caller capture require short-run runtime
overhead measurement before any long run. Only the sidecar DLL changes; protected
Core, UI, borders, saves and economy rules are untouched.

Verification:

- Release build and `Tests/Verify-EconomyTransactions.ps1` (Windows PowerShell):
  installed native targets, synthetic real Harmony calls, clamping/actual deltas,
  nested correlation, missing-mutation reconciliation and exception preservation.
  Ref-struct finance results and non-withdrawing previews are tested explicitly:
  finalizers read `goldChange` directly, never stale boxed `__args` that Harmony
  could write back over a native result. Hooks install at campaign session launch
  after game-dependent model initialization, not during module loading.
- Existing soak and peace verifiers retain their previous contracts.
- `Tests/Verify-EconomyRuntimeCoverage.ps1 -DiagnosticsDirectory <completed short-run logs>`
  rejects old snapshots, missing/nonzero-unexercised categories, unclosed scopes,
  unexplained balance changes, diagnostic failures and finance components without
  linked daily cash-settlement evidence. A coverage PASS is not proof of balance,
  nor of individual component affordability when native net settlement is clamped.
  `Tests/Verify-EconomyCoverageGate.ps1` tests rejection of malformed numeric data,
  inconsistent deltas, mixed sessions, bad sequence/parent IDs and trailing rows.
  Category coverage alone does not prove every workshop branch or delivery route;
  inspect operation sources and nested inventory/cash rows in the short-run audit.
- The active launcher verifies bounded snapshot collection, named economic event
  coverage, save/reload, and diagnostic failures. It does not claim a transaction-
  level cash-ledger proof or individual affordability verdict.

## Peace diagnostics

During an armed diagnostic run, peace records are written to both
`Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/AocPeaceDiagnostics.tsv`
and the existing `AocSoakEvents.tsv`. No test is started by building or deploying
this addition. Earlier logs cannot retrospectively provide caller stacks.

`WAR_DIAGNOSTIC` and `PEACE_DIAGNOSTIC` include campaign day, stable faction IDs,
names/leaders/strength, reported native reason, session/war correlation IDs,
observed stance dates/state, casualties, tribute and remaining payments, plus
up to 24 caller method/assembly frames without local file paths or arguments.
Reason `Default` remains `Default`; call stacks are evidence, not guaranteed
causal attribution (inlining and other event listeners can obscure the caller).

`PEACE_RAPID_REVERSAL` flags a first observed peace within one monotonic wall
second AND one campaign hour of that pair's latest observed declaration. It
does not establish a diplomacy bug. Faction order is normalized, redeclarations
reset timing, duplicate peace does not flag twice, and missing/reloaded/evicted
history or invalid time is explicitly unknown rather than zero duration.
History is bounded to 4096 pairs and starts fresh per campaign session.

Native boundary: Bannerlord 1.4.8 `CampaignEvents.WarDeclared` / `MakePeace`.
This is a synchronous observer, not a Harmony patch. `GetStanceWith` can create
a new stance, so contextual reads instead use the audited private backing
collection's read-only `GetStance`. API drift logs a diagnostic failure or
unavailable context; it never falls back to mutating faction state. Existing
legacy event records remain unchanged. Verify with the Release build,
`Tests/Verify-PeaceDiagnostics.ps1` and the existing soak verifier; live caller
attribution requires a new peace event during an explicitly started run.

## Focused 25-minute run (2026-09-08)

`Tests/Start-AocNativeSoak.ps1 -SeedSave <dedicated seed> -WallClockHours 0.4166666667 -CheckpointAfterMinutes 10`

This mode retains normal AOC 2x (40 active seconds/day), adds daily town food,
prosperity/gold, selected market stock/quotes, stable town/workshop slot keys,
workshop capital/profit-above-initial, and bounded nonzero AI wage observations.
Player and AI wage records are observations, not proof of actual deductions.
Missing stock rows do not mean zero stock; market sampling covers present items.

The module remains inert without `AocSoakRun.enabled`. The user fixed Alt-Tab
and Escape independently; this module does not change those settings, close
Escape, unregister pause requests, or add background patches. Native pause
owners, inquiries, campaign menus, missions, conversations, battle simulation
and saving are respected. No extra simulation ticks are introduced.

The save controller uses Bannerlord 1.4.8 `OnSaveOverEvent(bool,string)`, matching
its own requested filename and requiring success before exiting on a later
application tick. `MapState.OnTick` calls SaveTick and returns while saving,
so the previous CampaignEvents.TickEvent/IsSaving polling could never reliably
observe completion. A reload counts only after `OnSessionLaunchedEvent` verifies
saved campaign identity, day, player gold and birth date against an external
receipt. No campaign save types are added. Final-save completion is not itself
a reload check; the timed runner separately requires the checkpoint reload.

The runner backs up prior diagnostics/test saves and any pacing marker, leaves
user seed content recoverable, copies only the diagnostic DLL/manifest, and
does not change launcher selection or production Core/borders/UI. The approved
August 28 border configuration must pass its gate before launch. PASS means
diagnostic collection and checkpoint verification passed, not economy balance.
Verify with the Release build and `Tests/Verify-SoakDiagnostics.ps1`; live Escape,
Alt-Tab, reload and economy samples remain required for each actual run.

Test-only module for the native two-calendar-year regression soak. It contains
no gameplay-altering patches, production assets, or saveable types. The current
candidate adds the observation-only hooks documented above. It records hourly
campaign state and all relevant campaign events, checkpoints a dedicated save
four times, and exits after each completed save for stock Bannerlord reload.

`Tests/Start-AocNativeSoak.ps1 -SeedSave <save path> -WallClockHours 4`
runs for four elapsed hours, including loading/reloading, then requests a final
dedicated save and exits. The UTC deadline is persisted across reloads. Without
this option the existing 731-calendar-day target remains active. Final saving
has a five-minute grace period; a stalled game is left running with a failure
report rather than force-killed during a possible save.

Hourly observations cover wages, hero age, tournaments, wars and sieges. Every
30 campaign days, snapshots record town prosperity, food and gold, up to twenty
workshop conversion speeds, player-clan expenses, hero ages and siege progress.
These are observations for economy trend review, not assertions of balance.
The runner rejects diagnostic FAILURE events even if the final save completed.
UI overlap, MCM interactions, Better Time compatibility and reload freezes still
need their relevant live scenarios; absent mods and unvisited interfaces are not
covered. No run proves that every reported Nexus issue is fixed.

Do not ship this module in a player release.

For a short pacing measurement, place `AocPacingCalibration.enabled` in the
diagnostics output directory and launch the stock game directly with this module.
Do not use the soak runner for this mode. The marker is read at module load.
It disables the ordinary soak behavior completely and measures one campaign day
at each of 1x, 2x and 4x with a stopwatch. `PACING_END` records elapsed seconds,
actual day span and normalized seconds/day. Loading/stalls during a measurement
are included in elapsed time. Completion pauses without saving or exiting.
Remove the marker before launching an ordinary soak. The default mode is
unchanged when the marker is absent.
## Complete solution diagnostic matrix

See [SOLUTION_DIAGNOSTICS.md](SOLUTION_DIAGNOSTICS.md) for all seven solution packages, native-hook compatibility, evidence limitations and the coverage-report command. The local candidate adds transfer endpoints, active finance results, resource/warehouse flows, shortage durations, workshop lifecycle, treaty state, save identity and observer-health measurements. This is instrumentation, not deployment or a successful live-run verdict.
