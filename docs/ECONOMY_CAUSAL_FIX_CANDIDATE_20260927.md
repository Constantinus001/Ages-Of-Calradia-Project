# Causal economy fix candidate — 2026-09-27

Status: **installed as a private-test candidate; live acceptance pending**.
Scope is the three approved next actions from `ECONOMY_CAUSAL_FOLLOWUP_20260927.md`.
No income, production rate, calendar speed, game setting or save change. No game
control, capture rearm, new soak, public release or Defender scan was performed.

## Architectural boundaries and compatibility

- CampaignSystems owns validated immutable procurement configuration.
- WorkshopProcurement owns order planning, cargo and transfers; the existing
  calendar sidecar remains authoritative for each recipe's production cadence.
- SoakDiagnostics owns optional observations and offline analysis. Native
  allocation/model hooks are diagnostic-only; they do not modify rewards.
- Protected Core DLL and WorldCalendar XML remain untouched. No save schema or
  native save-type ID changes. Existing order quote, ETA, policy revision and
  return/lifetime fields retain their saved values.

## Implemented

### Bounded earlier replenishment

An eligible AI workshop considers a supplier when at least one recipe input has
less market coverage than `ceil(dailyRecipeRate * (routeLeadDays + bufferDays))`
batches, clamped to 1 through configured `BatchesPerOrder` (default three).
The recipe base is read from the loaded calendar sidecar's exact `RecipeBaseSpeed`
method, then passed through the active workshop model, as native production does.
No copied calendar factor or new production multiplier is introduced.

Any existing order, including in-flight or arrived private stock, still blocks
another order. This deliberately conservative one-order policy accounts for
owned/in-flight stock by withholding a refill entirely; it is not a pipeline
inventory optimizer and cannot guarantee uninterrupted production.

Capital, output-price, native profit hurdle, supplier stock, cargo size, route,
town cash and duplicate-order checks remain. Missing or throwing cadence
integration returns an explicit unavailable decision and logs the failure;
saved cargo delivery is not quarantined because a forecast query failed.

### Category-aware grape reserve

New optional schema-1 settings:

| Setting | Default | Validation |
| --- | ---: | --- |
| ReorderBufferDays | 1 | finite 0–30 campaign days |
| GrapeCategoryFloor | 10 | integer 1–100000 units |

Only native category `grape` uses the new floor. The retained reserve is still
the maximum of that floor, `MinimumSupplierStock`, and summed local recipe units
times `SupplierReserveBatches`. All other food categories retain
`FoodCategoryFloor=100`. Aggregate town food stores and the declining-food buffer
are unchanged. Ten is the existing minimum-stock policy applied to this scarce
category, **not a proven optimal balance value**. Setting the grape floor to 100
restores its former category threshold. Old config files remain valid and pick
up optional defaults; a new policy fingerprint identifies the changed policy.

Each eligible daily plan reports the controlling supplier filters and the chosen
forecast. New additive `PlanObserved` ABI sends the same evidence into the
session-bound combined capture as `PROCUREMENT_PLAN`; subscriptions are removed
at capture closure. Older procurement builds remain readable, with missing plan
coverage explicitly reported. Candidate rejections are not independent shortages,
and an offer is not a committed shipment.

### Battle and ship allocation attribution

New exact-signature native hooks:

- `MapEvent.CalculatePlunderedAndLostGoldAmounts(MBReadOnlyList<MapEventParty>, MBReadOnlyList<MapEventParty>)`
- `MapEvent.LootDefeatedPartyShips(MBReadOnlyList<MapEventParty>, MBReadOnlyList<MapEventParty>)`
- Active concrete `ShipCostModel.GetShipTradeValue(Ship, PartyBase, PartyBase)`
  and `GetShipSellingPenalty()` when NavalDLC is present.

Weak event/party identities join before/after `GoldLost`/`PlunderedGold` to the
existing `CommitGoldChanges` reward receipts. Ship identities are shared with
existing lifecycle observers. Values and penalties are tapped from evaluated
results, never recalculated. Native skipped bodies and exceptions remain native;
observer failure closes diagnostic evidence, not gameplay. Detached parties use
a safe display label while preserving the authoritative weak identity.

`Analyze-BattleAllocations.py` is integrated into `Analyze-FrameworkPeriods.py`.
It reports allocation deltas separately from wallet cash, detects mismatched
commit allocations, and marks old captures/missing branches as coverage gaps.
An open scope or unfinished last line in an active file is not a confirmed
integrity failure. This does not independently certify native valuation formulas
or reconstruct the historical 28.19-million wealth increase.

## Verification

- Release builds: CampaignSystems, WorkshopProcurement, SoakDiagnostics; zero
  warnings/errors.
- `Verify-Framework.ps1`: 46 configuration/framework assertions.
- `Verify-Procurement.ps1`: 99 policy, saved-state, transfer and lifecycle assertions.
- `Verify-NativeProcurement.ps1`: authoritative calendar bridge binding, early
  reorder versus sufficient stock, unavailable cadence isolation, planner ABI,
  native dispatch/production/cash conservation, duplicate rejection, save/load,
  returns and corruption quarantine. Its baseline verifies native AI/player
  cycles and calendar patch contracts without replacing protected artifacts.
- `Verify-BattleAllocation.ps1`: exact native allocation targets and both native
  ship-model signatures, actual unmodified model-result taps, native party
  snapshot deltas, stable commit identity, nested/skipped/error cleanup.
- `Verify-ProcurementLedgerObservation.ps1`: real additive observer ABI,
  duplicate-subscription prevention, captured plan and close-time detach.
- `Verify-CaptureReadiness.ps1`, `Verify-RewardObservation.ps1`,
  `Verify-NavalPatchCoexistence.ps1`: passed focused diagnostic regressions.
- Python analyzer suite: 131 tests passed.
- `Verify-ProtectedPoliticalBaseline.ps1`: repository and installed protected
  artifacts passed.

## Continued readiness audit

The complete `Modules/AgesOfCalradiaCampaignSystems/Tests/Verify-Candidate.ps1`
passed, including protected calendar math, strategic-map contracts, optional
Logistics integration, 30-day log rotation, cash observers, lifecycle hooks and
the 131-test analyzer suite. This is broader offline evidence, not live acceptance.

The continuation also added the new allocation observer to the requested-close
guard. A pending diagnostic close is retained until the allocation scope finishes;
it cannot acknowledge closure between its before/after receipts. This changes only
the diagnostic closure boundary, not gameplay or save behavior. The Release build,
`Verify-BattleAllocation.ps1` (including a real pending close request inside a
nested allocation), and `Verify-RollingFrameworkLog.ps1` passed after this change.

The isolated private-test package subsequently passed the complete candidate
suite and package/hash verification at
`C:\Users\fpicc\AocRelease\framework-b27835001927\verification.json`.
Its receipt is `private_test_candidate_pass`, security is explicitly
`not_run_private_test_only`, and `Deployed` remains false. The read-only private
deployment preflight passed, as did the ledger/planner observer ABI fixture
against the exact packaged assemblies. This is not a public-release receipt.

## Remaining acceptance, not another automatic run

Installation was explicitly authorized and completed on 2026-09-27 using the
verified private-test installer. All seven listed targets match their exact
package hashes; four files changed (three sidecar DLLs and the example config).
Protected Core/UI checks passed before and after installation. The calendar
sidecar and module manifests already matched and were not overwritten.
Rollback backups and the successful deployment receipt are retained at
`C:\Users\fpicc\AocRelease\framework-b27835001927\private-test-deployment-325eb26265f9\deployment.json`.
No game launch, save change, launcher change, user-settings change, capture rearm
or scheduled test was performed. The earlier package receipt's `Deployed=false`
describes packaging; the separate deployment receipt records the installation.

### Fresh capture preparation after the blocked load

The first post-install load was blocked before opening the writer because the
prior acceptance capture remained at the current-log path. After the user closed
the game, its five evidence/control files were hash-verified into
`acceptance-archive-93c2af57c6d54053b1897d044cc2a6d9` under the diagnostics directory.
Only those verified duplicate originals were removed; the capture remains
recoverable in that archive. A new five-campaign-day marker was prepared.
Final pre-load status: `READY_TO_LOAD_NOT_RECORDING`, no blockers, write probe
verified. No save, launcher selection, speed or other game setting was changed.

Use `-CandidateRun C:\Users\fpicc\AocRelease\framework-b27835001927` with
`Get-CaptureReadiness.ps1` and `Prepare-AcceptanceCapture.ps1` for this installation.
These scripts now verify the exact candidate archive/package receipt rather than
mistaking a separate workspace build for the installed isolated build. The
default workspace comparison remains available. Package tampering still blocks
preparation before archival; the game-closed guard is unchanged. Both preparation
and readiness-policy fixtures passed, and the diagnostic Release build had zero
warnings/errors. These are external script corrections; no reinstall is needed.

Before relying on a runtime test, check loaded sidecar
identities, fresh capture readiness, cadence availability and plan subscription
before relying on a run. Then verify that early offers dispatch and arrive,
grape rejections fall for the intended reason without unsafe food exports, and
workshop operating profit changes independently of owner withdrawals. Observe
ordinary battle allocation-to-commit joins and naval ship valuation/cleanup when
those events actually occur. Do not demand another long run solely because a
rare player/naval branch did not occur.

The live profitability impact, supplier-food effect, third-party model behavior
and total diagnostic overhead remain unverified. Native model queries are
read-only in the inspected native implementation; arbitrary third-party model
side effects cannot be certified by these fixtures. No blanket income cut or
production buff is justified by the remaining coverage gaps.
