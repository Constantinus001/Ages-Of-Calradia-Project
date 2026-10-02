# Economic diagnostic evidence and causality

## Scope

An undeployed diagnostics/procurement-sidecar change. No production rates,
income, settings, save schemas, protected Core, calendar or UI changes. No game
restart is required to verify these offline mechanics. Future runtime evidence
requires actually loading the new sidecars; historical logs cannot acquire
missing causal observations retroactively.

The goal is broad, inspectable evidence, not a promise to detect every possible
bug or infer a unique cause from correlations. A completed analyzer is not a
passing economy. Do not rebalance income or production to hide unexplained logs.

## Implemented

- Wallet changes carry market/parent-market, workshop-run/cycle and town-cash
  operation IDs, alongside existing procurement, reward and naval contexts.
  IDs are interpreted within the capture session and their own namespace.
  Context is not exclusive causality. Net-of-nested wallet accounting is intact.
- Native 1.4.8 `ItemConsumptionBehavior.UpdateTownGold(Town)` is signature/IL
  checked. The single virtual `SettlementEconomyModel.GetTownGoldChange` call
  is observed with the existing DUP/void-tap mechanism. It executes once.
  Begin/end records include cash, prosperity, actual model result/count,
  original-executed flag and exception type. Effective model/MVID/patch owners
  appear in provenance. Actual cash requests remain in wallet records.
  No formula is re-evaluated and no result is replaced. Missing/changed IL blocks
  capture; original exceptions propagate. End cash is gross contextual evidence,
  not a second ledger entry. Zero/negative changes are legitimate observations.
- Combined workshop input receipts include required category units, observed
  market quantity, private stock and actual native gate outcome. The additive
  read-only ABI partitions private cargo into accepted-current-attempt, reserved,
  in-transit, blocked/returning and unknown/partial quantities. Acceptance comes
  from the already selected native procurement context, not another eligibility
  or model query. Old modules report unsupported evidence, not invented zeros.
  Workshop state now includes siege and food along with rebellion/capital/owner.
  Snapshot eligibility is not a continuously observed daily decision.
- Optional additive procurement candidate ABI records deterministic examples of
  cash, distance, reserve, food, stock/quote, margin and capacity predicates using
  values already computed by the planner. Maximum 32 examples per decision,
  one per recipe/reason, with exact explanation-omission count. Original aggregate
  rejection counters and exact transaction accounting remain. No second price,
  model or RNG call is added. Invalid distance is not proof of disconnection.
- Readiness reports capture mode, causal schema and committed event-family
  counts/first/last sequences. Hook installation is separate from execution.
  Session resets clear counters; uncommitted rows are excluded.
- Snapshot and readiness-publication costs have call counts, exclusive measured
  duration and maxima. Nested measured boundaries are subtracted from parents.
  Writer cost overlaps these measurements: DO NOT ADD THEM. Uninstrumented
  callbacks remain unknown; total gameplay overhead is not certified.
- One read-only report provides machine-readable JSON and a readable Markdown
  summary. Issues include observed finding, attribution, evidence row references,
  occurrence count, bounded examples, alternative explanations and missing proof.
  Source path, selected session and SHA-256 of the read prefix are retained.
  It refuses absent sessions and does not automatically follow a new session.

## Coordinated seven-part extension

All changes below are an **offline candidate, not deployed or live-certified**.

| Improvement | Implemented evidence | Important limit |
|---|---|---|
| Connected timelines | Ephemeral planner ID linked explicitly to order at dispatch; transfer/accounting/movement receipts; accepted-order cycle joins to output events and per-wallet net flows; first missing step | No guessed temporal joins; related cash is not profit, consumption is not sale, opening ledger can explain missing dispatch |
| False-alarm lifecycle | New, consecutive sampled discrepancies, recurrence after a clean observation, and no-longer-observed states; exact counts and bounded examples | Disappearance never proves the old cause harmless; consecutive snapshots do not prove continuous failure |
| Usable inventory | Actual current-attempt acceptance and saved cargo partition; optional once-per-workshop/reason/day hold receipts | Reserved stock is not automatically eligible; quarantine is unknown, not zero |
| Money purpose | Parent-linked executed cash boundaries separate owner distributions, operating expense and capital reset; nested gold-transfer calls retain their enclosing purpose | Unclassified finance/grants/wages/tribute remain unresolved unless a real purpose witness exists; gross boundary totals are not added to net wallet rows |
| Offline failure replay | Duplicate/missing receipts, open and closed unfinished operations, rollback, missing/changed parents, skipped originals, native exceptions and misleading stock snapshots | Synthetic fixtures are clearly separated from campaign evidence |
| Execution and cost watchdog | Installed-but-unexercised lifecycle methods, missing event families, drops, measured writer/observer costs, weighted seconds/day, growth between two cost witnesses | Costs overlap; no claim of complete overhead measurement; growth projection assumes the measured rate continues |
| Lifecycle coverage | Native quest creation/start/deadline/completion/finalization boundaries; optional logistics market/reserve boundaries; existing ship ownership/removal/destruction receipts grouped by identity | All rare branches and third-party inlining still need actual receipts; installation alone does not certify execution |

### Evidence-led identification of unknowns

`unknown_investigations` retains the entity identity evidence, exact source rows,
observed mutation boundary/callers, linked operation IDs, alternative explanations,
conflicting evidence and the next missing proof. It keeps a bounded window of
nearby wallet changes with exact omission counts. Nearby transactions are not
automatically blamed for a residual.

An initially generic `set_Gold` observation can be identified as occurring inside
an owner payout when matching begin/end receipts prove the same operation,
purpose and parent chain executed without an exception. A skipped, missing,
changed-parent or failed boundary cannot promote it. Nested generic gold-transfer
calls use the proven parent scope. This identifies the financial boundary, not
exclusive causation or economic balance.

Town-model witnesses separately distinguish an exact model-return/cash-delta
match from conflicting amounts. Neither resolves an unrelated wallet residual.
Unknowns stay unknown when their required evidence was never captured; the report
states which observation is missing rather than manufacturing a diagnosis.

### Native and optional integration contracts

- Native 1.4.8 `QuestBase`: `InitializeQuestOnCreation()`, `StartQuest()`,
  `ChangeQuestDueTime(CampaignTime)`, `CompleteQuestWithSuccess()`,
  `CompleteQuestWithTimeOut/Fail/Betrayal/Cancel(TextObject)`, `FinalizeQuest()`.
  Read-only prefix/finalizer snapshots record finite/Never deadlines, ongoing and
  finalized flags, execution/exception status, parent operations and weak object
  identity plus native StringId. They do not use the potentially inlined setter
  as the only source. Original exceptions propagate.
- Optional `AgesOfCalradiaLogistics`: exact signatures for market `TryBuy` and
  `Produce`, reserve `ProcessDailySupply`, `TryProcureAiSupply`, `LoadSupplyCrates`,
  `AddPurchasedSupplyCrates`, single-party and array/ref-cursor `TryConsumeReserve`,
  and `RemovePartyReserve`. Observation uses `ReadExistingReserve`, never the
  mutating `GetReserve`. The ref-cursor overload has typed by-value argument
  binding, not Harmony `__args`. Nested reserve/market endpoints are contextual
  evidence, not additional accounting entries. No logistics assembly is changed.
- Financial purpose IDs augment already installed wallet/workshop/finance
  boundaries; no new finance native target is introduced. Invocation completion
  controls interpretation of nested wallet receipts. Context is restored after
  skipped originals and exceptions.
- Exact signatures are preflighted; a present incompatible integration fails the
  capture, not gameplay. Missing optional logistics is reported absent. Installed
  hooks, assembly identities and patch-owner signatures enter readiness checks.
  Diagnostic close/rotation waits for the added synchronous contexts to finish.

## Broad report

Run `Modules/AgesOfCalradiaSoakDiagnostics/Tests/Analyze-CausalEvidence.py` with
`CAPTURE_PATH --session EXACT_SESSION --output NEW_REPORT_BASENAME`.

Add `--full` for stable, closed, single-session evidence. It runs all thirteen
independent audits: capture/shipments, stock/wallets, livestock/wool/town cash,
workshop payment/profitability, procurement transactions, cost basis, reward
wallet attribution, planning outcomes, naval rewards/cleanup, naval policy/player sales,
battle allocation, quest/framework state and lifecycle coverage. Results and
errors are retained under `independent_audits`; errors never become zero issues.
Wealth remains covered by canonical wallet/caller/residual reports, not the
legacy HERO_GOLD analyzer: that analyzer requires a different capture mode.
The index retains scalar results, exact collection counts and bounded examples;
complete independent outputs and the extended timeline/health/unknown evidence
go into a sibling `.details` folder. The compact JSON and Markdown link to them. Report
filenames must be new. Raw capture files are never changed.

For an active writer use triage without `--full`. The reader fixes the byte
boundary before reading. An unfinished final line or open synchronous operation
is pending, not proof of corruption. Full audits reject mixed sessions and
changes to the source during analysis. The full-audit input hash must also match
the earlier triage hash, preventing mixed investigations if the file was replaced
between stages. Older logs stay usable, with new event
families explicitly unavailable/not exercised rather than fabricated.

## False-alarm safeguards

| Observation | Do not conclude | Required follow-up |
|---|---|---|
| Failed input gate or zero stock | Sustained supply shortage | Join deliveries, usable private stock and later attempts |
| Many rejected suppliers | That many independent shortages | Read selected offer and committed outcome |
| Town credit matches observed model | Economy is balanced or all credits are subsidies | Compare actual call, wallet delta and patch provenance |
| Wallet residual | Created gold | Find first divergent window and missing/inlined writes |
| Capital decreases | Workshop operating loss | Separate inputs, expenses, owner withdrawals and resets |
| Missing sale/arrival | Lost cargo | Examine open order/journey and saved expiry policy |
| No player naval events | Player penalty passed | Mark branch not exercised |
| Hook installed | Feature executed correctly | Require committed receipts and reconciliation |
| Explanation omitted by budget | Accounting dropped | Read separate witness omission and writer discard counters |
| Analyzer completed | No bugs | Read findings and coverage gaps in its result |

## Remaining limits

This does not prove every cause. Inlined/bypassed writes can still yield unknown
attribution. Quest lifecycle hooks supplement snapshots, but their runtime
execution and rare completion paths are not certified by offline binding tests.
Stalled orders and player-only naval behavior still need their specific
ledger/policy/branch evidence. Supplier witnesses are bounded examples, not an
exhaustive record of every evaluated candidate. Exact accounting is not sampled.
Full callback/stack-trace overhead attribution and universal cross-system
causal graphs are not implemented. The report exposes these limits instead of
claiming an all-systems pass.

## Verification

Required checks: Release builds of changed sidecars; procurement pure/native
fixtures; `Verify-CausalDiagnostics.ps1`; `Verify-DiagnosticExtensions.ps1`;
`Verify-WorkshopCashBoundaries.ps1`; `Verify-CaptureReadiness.ps1`; rolling
log checks; Python analyzer regressions; protected baseline, calendar and
strategic-map contracts. `Tests/Verify-CombinedEconomyCandidate.ps1` includes
the new causal check. The broader Campaign Systems candidate gate also includes
it. Offline fixtures are not live campaign acceptance or a publication gate.

The final seven-part extension passed the combined gate at
`output/combined-economy-check-20260929-025101-9a3a757e/receipt.json`: zero-warning
Release builds, native quest finite/Never/skip/exception behavior, logistics
unknown/known-zero reserve reads and preserved ref cursor, nested cash-purpose
links with unchanged net accounting, planner correlation with unchanged quote
counts, cargo partitions with unchanged serialized ledger, and protected
baseline/calendar/strategic-map checks, including the report-compaction and
parent-integrity refinements in all 237 Python tests. The 30-day rotation,
reload/rollback, diagnostic-close, ship-lifecycle and runtime-summary fixtures
also passed separately. This is local
verification only; no installed files, settings, saves or game processes changed.

Verified 29 September 2026 UTC: both changed sidecars built in Release with zero
warnings/errors. The combined candidate gate passed, including 117 procurement
assertions, native planner execution with the candidate observer attached and
unchanged quote count, 67 framework assertions, causal/readiness/native quest
fixtures and 211 Python tests. Rolling-log, workshop cash-boundary and runtime
summary checks separately passed. Protected political/calendar/strategic-map
checks passed. Receipt:
`output/combined-economy-check-20260929-021020-a4a96db0/receipt.json`.
Installed procurement and diagnostics hashes were rechecked and are unchanged.

The completed historical session `7f1cb08aba0c49518d8ed0605ef61516` was replayed
without changing its source. All thirteen independent analyzers completed. The
readable index is `output/economy-evidence-index-20260929.md`, with full links
in its matching JSON. All 361 historical wallet-residual groups were absent at
their latest snapshot; this does not explain their earlier cause or imply 361
confirmed bugs. Old-build town-model/input/candidate witnesses are explicitly
missing. Player naval/finite-quest and rare lifecycle gaps remain. The final
report formatting and false-flag tests passed in the 211-test analyzer suite.

## Offline review safeguards (29 September 2026)

Boundary: offline Python reports only. No new native hooks, dependencies,
settings, saves, balance changes or deployment. Main compatibility risk is a
misleading comparison of unlike captures; missing context fails closed.

`EvidenceReview.py` adds measured impact separately from causal confidence.
Each finding retains its entity, full sampled day span (not continuous failure
duration), and per-metric peak residual. Repeated snapshots are never summed.
Large amounts do not increase causal confidence. Hero/clan/kingdom wallet views
remain separate. Native `succeeded`, `failed_see_gates`, and legacy `failed`
cycles are recognized; unknown metrics suppress the attempt-rate calculation.

`Tests/fixtures/review-incidents.json` contains hand-calculated, explicitly
synthetic reproductions of known reporting failure modes, not original campaign
receipts. New confirmed failures should gain a minimal fixture, source/provenance
note, expected outcome and negative test. These tests validate reporting only.

`Tests/Compare-CausalEvidence.py` takes `--before`, `--after`,
`--before-session`, `--after-session`, `--before-context`, `--after-context`,
and `--output`. Both inputs are analyzed directly; it does not follow another
session or overwrite reports. Context files are JSON objects with:

- `capture_sha256`: exact capture hash, matching the analyzer's prefix hash;
- `build_sha256`: reviewed build identity (may differ between candidates);
- `settings_sha256`, `campaign_id`, `conditions_id`: reviewed configuration,
  campaign and matched scenario identifiers, equal between captures;
- `window_start_day`, `window_end_day`: numeric boundaries matching the actual
  captures and each other, with positive observed duration.

Contexts are operator declarations, not automated proof of equivalent campaign
conditions. Do not invent them to bypass a blocked comparison. Intact closed
single-session evidence, equal coverage and wallet cohorts are required. Exit
code 2 means blocked; the report explains why and contains no trend metrics.
Different snapshot counts are explicitly flagged. Outputs are descriptive,
never causal claims that a candidate fixed the economy. Missing profitability
evidence stays `not_comparable`: capital movement is not operating profit.

Verification: `test_evidence_review.py`, included automatically in the combined
candidate's analyzer discovery, covers hand-derived regressions, separated
impact/confidence, unknown cycle metrics, preserved input objects, incompatible
contexts, changed bytes, active/mixed/incomplete captures, coverage/cohort gaps,
and spans beyond bounded examples. The combined Release gate remains required;
offline success is not live acceptance.

Verification receipt: `output/combined-economy-check-20260929-111636-ea4d868e/receipt.json`
passed the combined Release/native/protected contracts with zero compiler
warnings and 249 analyzer tests. The final dropped-record guard, unequal-sampling
guard and CLI output-preservation checks passed in the expanded 252-test suite.
No deployment or live campaign test was performed for these offline additions.

## Bounded hardening package

The five previously proposed additions are now implemented in the diagnostic
companion and offline report layer. Core/calendar/UI/persistence policy remain
unchanged. This is private diagnostic readiness, not economy or public-release
certification. The package deliberately does not promise universal causality.

1. `IncidentRecorder`: a 128 KiB / 256-row recent ring, at most 8 KiB per extra
   retained row, 64 subsequent rows per incident, 32 incident groups per session,
   and 32 MiB total owned incident storage. Grouping is by kind/entity/metric
   within a session. Accounting TSV records remain unsampled and unchanged.
   Oversized extra rows and exhausted budgets are counted, never called healthy.
2. `MoneyPurposeObserver`: actual native kingdom-budget grants and daily clan/
   notable finance settlement callsites; tribute and kingdom-distribution
   executed scopes; exact party wage deductions distinct from funding top-ups.
   Existing town trade scopes now carry executed cash-purpose links. Uncovered
   routes stay unknown. Wage model returns are separate non-cash assessments.
3. `OBSERVER_COVERAGE`: compares independent outer wallet readings to nested
   recorded deltas. Missing nested movement is recovered once by the existing
   accounting boundary and explicitly flagged. No-net activity is not proof
   nothing happened; offsetting unseen writes can remain invisible. A gap is
   not proof of inlining or an economy bug.
4. Incident JSON is written immediately with first divergence and before rows,
   then finalized with bounded after rows. Identity rows retain session, native
   hook MVIDs and Core policy revision where observed. First/latest evidence,
   recurrence count, alternatives and missing proof survive capture closure and
   monthly replacement. A close before 64 later rows is explicitly incomplete.
   Storage is under the diagnostics directory's `AocIncidents`; historical files
   are never automatically deleted. A full budget needs deliberate archiving;
   the main accounting stream continues. IO failure is recorded separately.
5. `EvidenceAcceptance`: one report checklist with passed/failed/not_exercised/
   unsupported for capture, wallet observations, observer coverage, money
   sources, incident retention and every independent audit. Only supported
   evidence contracts can pass. Analyzer completion, installed hooks, a quiet
   log, or absence of incidents is not acceptance. Unsupported broad automatic
   pass contracts remain explicit manual-review limits; do not invent a pass.

### Native target and compatibility contract

Bannerlord 1.4.8 `ClanVariablesCampaignBehavior.DailyTickClan(Clan)` and
`DailyTickHero(Hero)` each must contain exactly one
`GiveGoldAction.ApplyBetweenCharacters(Hero,Hero,int,bool)` call. Clan tick must
also contain the single additive `Kingdom.set_KingdomBudgetWallet(int)` site.
Transpilers replace just those calls with same-signature wrappers that execute
the original once, preserving arguments and exceptions. No second model/RNG
query, arithmetic change, or native-call suppression is introduced.

`DefaultClanFinanceModel.AddPartyExpense(MobileParty,Clan,ExplainedNumber,bool)`
must contain one hero-gold deduction and two party-gold setters (deduction plus
funding), with the validated DUP/getter/local/SUB-or-ADD sequence. Wrappers tag
the executed deduction/top-up independently, not the entire finance boundary.
`CalculatePartyWage(MobileParty,int,bool)` records its actual returned assessment
without treating it as another payment. Tribute prefixes bind only the actual
`applyWithdrawals` bool, never marshal or modify ref ExplainedNumber arguments:
`AddIncomeFromTribute(Clan,ref ExplainedNumber,bool,bool)`,
`AddExpensesForTributes(Clan,ref ExplainedNumber,bool)`, and
`AddIncomeFromKingdomBudget(Clan,ref ExplainedNumber,bool)`.

All exact targets join readiness signature checks. Unsupported signatures/IL
fail capture setup safely; native exceptions propagate. A skipped or throwing
scope cannot certify money attribution. Other mods replacing/inlining bodies
remain a runtime compatibility risk, surfaced through gaps rather than guessed
causes. Context proves an executed boundary, not exclusive causation.

`Verify-DiagnosticHardening.ps1` removes a real inner Harmony observer, executes
the fixture, then feeds its emitted evidence through the actual report tool.
It distinguishes covered/no-net/missed activity and verifies no double counting.
It also checks recorder bounds, recurrence, disk exhaustion, IO failure, exact
native grant/wage IL rejection, and actual native wallet wrapper execution.
The rolling-log fixture verifies incident survival across real log rotation.
Both are mandatory combined-gate steps. Offline fixtures do not certify live
third-party compatibility, performance, all financial routes or economic balance.

Private deployment is narrowed to the diagnostics DLL and procurement's optional
diagnostic ABI, with exact combined-receipt hashes, existing-framework match,
process guard, verified backups, rollback and protected baseline checks.
`Tests/Deploy-DiagnosticHardeningPrivate.ps1` defaults to read-only preflight;
`-Apply` is explicit. No manifests, settings, capture markers or saves change.
The separate public-release/security gate remains required for Nexus release.

Verified and privately deployed 29 September 2026: combined receipt
`output/combined-economy-check-20260929-113712-7eda0491/receipt.json` passed all
checks, including 263 Python tests and zero-warning Release builds. Exact
two-DLL backup/deployment receipt:
`output/diagnostic-hardening-deployment-4f85eae3aaa24c6c8ac8a12b1c56d680/deployment.json`.
Installed diagnostic SHA-256:
`8FCF3AFB56D7816E299667ABDEA3DA69B074F5EAACB270FDB318821AC16DDB2F`;
procurement SHA-256:
`BE4B9FAEE58108C6D187165F38CFFD8DC1DD53D53A59F515FD41E23B14A329E8`.
Protected Core and World Events prefab were verified unchanged before and
after deployment. No security scan, game launch/restart, save, setting or
capture-activation change was made. Live execution on this installed build is
not yet verified; this is not a new soak-test request.

### Preparing the combined-receipt acceptance run

`Get-CaptureReadiness.ps1` and `Prepare-AcceptanceCapture.ps1` now accept
`-VerificationReceipt` for the combined gate's `receipt.json`. This is mutually
exclusive with `-CandidateRun`. It binds the three exact built/installed DLLs
to their recorded hashes, requires the hardening/rotation/analyzer/native/
readiness checks, and rejects missing, failed, duplicate or changed evidence.
It does not compare a deployed isolated build against a stale module-bin DLL.
Without either explicit authority, the legacy local module-bin comparison is
unchanged; use the combined receipt for this deployment's subsequent checks.

The 16-day run prepared on 29 September uses
`output/combined-economy-check-20260929-113712-7eda0491/receipt.json`.
Preparation archives prior evidence with verified hashes, preserves saves and
speed settings, and starts no game process. Verify `RECORDING` after the user
loads their existing save; `READY_TO_LOAD_NOT_RECORDING` is only preflight.
The capture closes itself after 16 campaign days, leaving the game running.
`Verify-ReadinessBuildReceipt.ps1 -VerificationReceipt <receipt>` covers exact
deployed-build matching and rejection of failed/changed/missing/duplicate/
ambiguous authorities. It and `Verify-ReadinessPolicy.ps1` passed; the diagnostic
Release build remained zero-warning and its deployed hash unchanged.

### Wallet identity and independent replay correction (29 September)

This change is diagnostics-only, with no new Harmony targets, economic rates,
save changes, or protected Core changes. Native registration changes
`MBObjectBase.GetHashCode`; wallet and alias indexes now use immutable
session-local IDs supplied by the existing reference-identity table. Lord-party
hero canonicalization and monthly resets remain unchanged. The registration
transition is exercised against real native objects by `Verify-SupplyCash.ps1`,
now also an explicit combined-candidate gate.

`EvidenceWalletLedger.py` independently reconstructs balances from the FIRST
baseline plus net changes. Duplicate baselines never rebase the ledger. It
separates accounting continuity from identity integrity, detects missing and
duplicated changes, and retains contradictory same-day checks when no observed
mutation separates them. Both causal and broad reports expose this result;
latest residuals alone are not certification. Old captures can pass money
reconstruction while failing identity integrity. No old evidence is rewritten.

The recorder retains at most 28 ordinary and 4 critical incident packages,
with at most 8 ordinary representatives of a kind/owner-type/metric family.
One MiB of its existing 32 MiB disk budget is reserved for critical packages.
Prior archives can still exhaust total storage: reservation is not an unlimited
guarantee. Repeated admitted incidents update their count; omitted occurrences
have separate family-cap/issue-cap counters and failed writes a disk-cap counter.
These are occurrence counts, not inferred numbers of unique missing issues.

Each package can now include recent evidence from its wallet independently of
the global snapshot ring. This extra history is bounded to 1 MiB, 512 wallets,
8 rows per wallet and the existing 8 KiB row limit. LRU evictions are reported;
an empty or evicted history is not proof nothing happened. Full unsampled raw
accounting remains authoritative. Native tests cover snapshot-sweep survival,
family/count limits, critical reservations, history bounds, IO failures and
the original recorder/observer contracts. These changes are a built candidate,
not proof of deployment or live acceptance.
