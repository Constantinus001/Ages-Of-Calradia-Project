# Workshop payment conservation — September 20 local candidate

Affected boundary: native workshop output cash transfer, existing calendar sidecar.
Protected Core DLL and WorldCalendar prefab are unchanged. No persistence change.
Deployed through the scoped release gate on September 20; no game run started.

## Approval quote follow-up

`WorkshopApprovalQuoteFix` now replaces the single native GetItemPrice call in
GetItemsToProduce with the payment-direction quote (`isSelling:false`, per-item
cap 1000), only during a capital-affecting notable/AI workshop cycle after game
start. Random output selection is unchanged, with exactly one price-model call
per selected item. Player/warehouse and estimate calls retain native prices.
Prefix/void-finalizer context restores its prior value on return and exceptions.
Native call is declared on SettlementComponent, not the Town override; the IL
contract verifies the precise declaration. Missing/modified calls fail preflight.

AI batches now retain the selected item sequence and payment quotes. Approval
and settlement use that same quote, including when inputs or earlier outputs
change market prices. This intentionally changes AI batch pricing to a locked
selection-time quote; player prices remain native. Nested collection is isolated
by workshop and invocation, and finalizers restore the previous context.
This does NOT reserve cash: external callbacks can still drain it. The cash cap
remains the safety net. A changed output sequence invalidates the remaining plan
and logs a cash-conserving native-price fallback. Do not advertise cash escrow
or atomic third-party transactions.
`Verify-WorkshopApprovalQuote.ps1` covers native hook installation, one-call
replacement, guards, unchanged control flow, duplicate rejection, nested context
and exception propagation. Release and protected/calendar/map checks pass.

Evidence: completed v6 capture `0e583ab135b74721865d53cc78019c32`, sequences 739127
and 1394062. Native requests 228 with town cash 5, and 364 with cash 268. The town
clamps its debit but the workshop receives the full request: 223 + 96 = 319
unfunded credit. This is NOT the cause of the much larger hero-wallet residuals.

Native target (Bannerlord 1.4.8):
`WorkshopsCampaignBehavior.ProduceAnOutputToTown(EquipmentElement, Workshop, bool)`.
After native `MathF.Min(1000, itemPrice)` and before storing the shared payment
local, cap positive payment to max(0, town.Gold). Both original mutations use the
same bounded local. Preserve quote evaluation, stock writes, RNG, event dispatch,
effectCapital/game-start branches and all original instructions. Negative native
requests retain native semantics. No debt is created for unpaid output: the town
still receives the original output, but workshop credit cannot exceed actual cash.
This deliberately fixes conservation, not recipe profitability or pricing policy.

Compatibility: exact target and shared-local IL pattern required, one Min and
one of each gold mutation, no branch/exception entry inside that payment span.
Changed, duplicate or ambiguous IL is rejected. Existing module boundary logs
failure and rolls back this sidecar's hooks; native game is not rewritten.
Other mods modifying this body need compatibility validation.

Verification: `Verify-WorkshopPayment.ps1` installs on the real native target,
replays both sequential-output shortfalls, tests empty/full/negative cash and
rejects mismatched locals/double transforms. Three instructions are inserted;
all original instructions/labels/blocks are compared unchanged. Existing recipe
and calendar sidecar checks also pass. Offline verification is not live campaign
acceptance; patched third-party interactions and campaign overhead remain untested.

## September 20 follow-up verification and next gates

Both sidecars build in Release. Protected baseline, calendar math, strategic map,
payment checks, cash observer fixtures and 17 supply analyzer tests pass.
`Verify-NativeWorkshopBatch.ps1` executes six actual native AI production cycles:
exact, insufficient and zero cash, each with rising and falling output prices.
It checks actual inventory, input consumption, payment, events and quote counts.
Four additional native player cycles cover market sales and warehouse-only
production, with sufficient and insufficient town cash. Player market outputs
retain dynamic native quotes; warehouse outputs do not debit town cash. The
existing native player affordability gate is preserved, not rebalanced.
Campaign setup, pricing, item selection, event dispatch, warehouse capacity and
skill awards are deterministic boundaries. This is not a loaded campaign.
`Verify-WorkshopBatch.ps1` separately tests nested collection and external cash
depletion. Native player inventory/production orchestration is now covered.

Transfer receipts are now grouped by transfer id, with endpoint counts and
external-source/sink classification, and are never added to wallet deltas as
additional income. Missing, duplicate and incomplete endpoints remain visible.
Historical v6 records cannot retroactively recover missing attribution.

All ten native cases also pass with the complete SupplyChainObserver hook set
installed using `Verify-NativeWorkshopBatch.ps1 -WithDiagnostics`. The fixture
installs its boundary stubs before rebuilding native callers to avoid inlined
access to fake campaign state. This is not an actively recording campaign or
coverage of arbitrary third-party mods.

Next, in priority order:
1. Resolve the release-source boundary. `Tests/Verify-Release.ps1` was run and
   stopped at its uncommitted-source gate. Do not use AllowDirtySource or commit
   unrelated user work to bypass it. Full packaging/security gates did not run.
2. Deploy only the two verified sidecars with protected-hash and rollback checks.
3. Use one combined bounded live capture for batch settlement, nested transfer
   attribution, wallet reconciliation, route receipts and runtime overhead.
   Require complete SESSION_END and covered endpoints; elapsed days alone are
   not acceptance. Existing bounds are 15 campaign days / 60 minutes / 5 GiB.
4. Trace remaining hero/clan residuals to callers before changing income. Reassess
   wine, smithy and linen profitability only after input/payment evidence closes.
   Keep Charas routing and sheep yields unchanged without causal evidence.

Engineering confidence: 8/10 for the focused local payment/receipt candidate,
not release readiness. Remaining release-source, live compatibility and
transaction attribution gaps prevent a 10/10 or a perfect-fix claim.
No deployment, save change or game launch was performed in this follow-up.
Capture arming now requires the cash observer checks (which include the supply
capture checks), and its request text correctly identifies v7 instead of v6.
No new capture was armed; the existing AocSoakRun.enabled marker was preserved.

## Scoped release follow-up

The full-Core dirty-tree blocker is now addressed by a separately documented
`Verify-Release.ps1 -EconomySidecarsOnly` contract. It creates an isolated
committed source snapshot without changing the user's repository index/branch.
The default Core release gate remains unchanged. Only the two sidecar DLLs are
packaged; protected Core artifacts are read and hash-verified, never copied.
External verification inputs and native/Harmony dependencies are hash-bound;
backups and rollback copies are verified. Two council reviews found and closed
game-path binding, unverified rollback and post-scan input-drift issues.

Receipt analyzer follow-up: malformed counts, duplicate endpoints and conflicting
transfer metadata cannot be certified as balanced. Missing presence metadata is
unknown, not assumed external finance. Twenty analyzer tests now pass.

Candidate `C:\Users\fpicc\AppData\Local\AocRelease\148986e34eb7` passed isolated
Release builds, ten native AI/player cases with all diagnostic hooks installed,
cash/wool/recipe/payment checks, twenty analyzer tests and protected/calendar/map
checks. Deployment STOPPED before any installed DLL copy: Start-MpScan failed
0x80070003 and a direct current-platform MpCmdRun scan failed 0x80508023. Both
candidate DLLs remain present. This is a scan failure, not evidence that this
candidate contains malware or a clean security verdict. No protection settings
were disabled or exclusions added. Deployment requires a successful scan/hold.

Root cause follow-up: MpCmdRun's exclusion-check log resolved the apparent
AppData candidate to `Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local`.
The actual files there matched both candidate hashes. Scanning that physical
path succeeded without elevation, with Defender completion event 1001, scan
84A54E9F-A6A9-41DB-9E06-225F04706610 (2026-09-20 10:34:18 local).
The release staging root now uses USERPROFILE\AocRelease, outside virtualized
AppData. Earlier permission suspicion was not the demonstrated scan cause.

Final deployment: `C:\Users\fpicc\AocRelease\1e995262a92a\verification.json`
records a clean security scan and ten-minute hold, unchanged verification inputs,
verified backups, and Deployed=true. Independent post-copy filesystem handles
resolve to the physical game module paths, not packaged-app storage. Installed
hashes match the scanned package:
- Calendar fixes: E075DC37C1BDDF9745CBB2E9EAA98AA3404C23B67CFBF7BD9B710B198DC34EF5
- Diagnostics: 5CF46AFE09C49095DDEFE85F2A952F90A1BF8E8C3CE44D96EDDEC462F5AAE848
The local candidate DLLs were synchronized to these exact verified binaries so
the existing capture-arm hash check remains valid. Protected baseline passed
again after copying. No game, save, settings, capture marker or automation was
started or modified. Live economy acceptance remains pending, not certified by
this deployment. Earlier blocked-candidate paragraphs above are historical.

## Supply audit decision

Existing capture: wool-bearing caravans considered Charas in 236 route decisions,
135 negative final scores and 101 nonnegative scores beaten by another town;
none selected Charas. Repeated decisions are not independent caravans. Negative
scores alone do not identify distance, path or permission causes. No routing
boost is justified by this evidence; no route behavior changed.

Sheep-input recipe: 994 affordability/profit gate observations, 481 rejected for
output <= input, 512 accepted, one town-cash rejection. Input quotes 14–82,
output quotes 12–116. Mean input 40.226358 and mean output 34.633803 across these
evaluations are not realized or volume-weighted workshop profit. Do not force
loss-making cycles or broadly increase resources. No sheep recipe changed.

Diagnostics candidate v7 adds canonical wallet owners, alias lifecycle markers,
nested-delta accounting and wider native cash boundaries. It observes prices
through the existing market-factor hook rather than re-running a model. Old v6
records remain legacy evidence, not retrospectively repaired transactions.
