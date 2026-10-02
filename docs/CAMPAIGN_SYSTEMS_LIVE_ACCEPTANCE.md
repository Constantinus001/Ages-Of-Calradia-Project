# One coordinated framework acceptance session

Do not start until the revised exact candidate is packaged, explicitly authorized
for private deployment, installed and its installed hashes match its receipt.
Per user direction, Windows security scans are for public release only, not this
private diagnostic test. Use `Package-CampaignSystemsCandidate.ps1
-PrivateTestNoSecurityScan`, followed by `Deploy-CampaignSystemsPrivateTest.ps1`.
Its receipt explicitly records `not_run_private_test_only`; the public/acceptance
installer rejects it, and this private installer rejects a public receipt. This
is a declared no-scan local test contract, never a substituted security PASS.
The older bc12fcdaa8bd candidate lacks procurement persistence evidence and is
superseded for this acceptance.

The integrated candidate cannot be deployed while the prior standalone Workshop
Procurement candidate remains selected in `LauncherData.xml`; loading the same
service from two module locations is unsafe. The installer refuses that state and
does not change launcher selections. Disable that prior module only with explicit
user approval, then rerun the private preflight.
No public Nexus readiness is implied by offline checks.

The acceptance installer refuses deployment while Bannerlord or its known native,
TaleWorlds or BLSE launchers are running, including a second check after backup.
Process enumeration errors fail closed; the installer never closes them itself.
Verify this guard with `Tests/Verify-CampaignDeploymentProcessGuard.ps1`.
This check does not remove the need to leave the game and launcher closed for the
entire deployment: process startup cannot be locked atomically with file copying.

Both installers accept either the original installed Core input hash or the exact
candidate hash for files in their own verified plan. This permits a read-only
post-install preflight without accepting any third-party modification.

## Before launch

Record package receipt, native version, enabled modules and pre-upgrade save.
Use player files plus opt-in diagnostics. Logistics is optional; record whether
present. Preserve settings and speed, and use a separate test save slot.
Explicitly arm only the combined SupplyCapture, not the long-soak controller.
Current mode: rolling 30 campaign days, 5 GiB, no wall-time expiry. The fixed
current-cycle file is overwritten at rollover. Preserve required evidence by an
authorized safe diagnostic close and export before rollover. These are safety
limits, not a promise of complete coverage or a requirement to run to every limit.

## Segment A and reload

1. Confirm CORE_SYSTEMS valid status, Core MVID and policy revision, campaign
   identity and actual loaded module hashes. Check aoc.procurement_status.
2. Observe dispatch and at least one pending order. Pause at a known campaign day.
3. Save to the test slot while capture is active. Require PROCUREMENT_LEDGER
   serialized_for_save evidence. This records the payload submitted to IDataStore,
   not a claim that disk writing has completed. Wait for the game's save completion.
4. Request a safe diagnostic close while the campaign still exists, verify final
   snapshots and SESSION_END, and preserve the evidence. Then load that exact test
   save when the user authorizes the reload. The persistent rolling marker registers
   capture automatically; do not rearm a competing one-shot marker. The same owned
   log appends a new identified segment while retaining its original period anchor.
   A save older than the last observed log day is a rollback: capture refuses to
   append and preserves evidence. Do not silently erase/rearm to evade this guard.
   Arrange the save/close boundary with equal campaign time. Normal teardown alone
   remains game_ended_incomplete and is not an exact terminal snapshot.

## Segment B and completion

Require loaded_payload evidence at capture start. Compare the last serialized
payload of A with the first loaded payload of B using Compare-ProcurementReload.py.
Campaign, day, Core/procurement builds, policy and full payload must match.
Empty obligations are NOT_EXERCISED, not proof of pending-order continuity.
Never silently follow a later unrelated session or merge all captures together.

Resume and observe arrival/consumption, or investigate a legitimate hold.
Use Analyze-FrameworkPeriods.py for the closed multi-segment log; never sum wallet
baselines across reloads. Analyze each segment's cash/stock reconciliation, workshop inputs/output,
profitability/capital, supplier/town balances and framework/quest evidence.
Use aoc.logistics_status for a real tracked party when Logistics is present;
unknown or missing is not a measured zero. Preserve the console output.

Review lifecycle_coverage for dispatch, arrival, consumption, returns/liquidation,
rollback/failure, battle and enabled naval recovery/destruction. Missing rare
branches stay explicit limitations, not automatic reruns or induced failures.
Report finite quest coverage only when actual finite quests exist. Do not force
rare wars, returns or expiries merely to make a coverage counter green.
DAY_TIMING measures total campaign throughput including pauses/diagnostics;
it does not isolate framework CPU overhead. Compare UI responsiveness and measured
frame costs separately; offline ledger timings are not in-game frame timings.

Absent/present optional-module startup combinations remain separate short checks,
not additional long soak tests. Stop once planned evidence is adequate or bounded
capture limits are reached, and report unresolved branches explicitly.
