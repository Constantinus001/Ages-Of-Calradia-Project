# Economic goal completion audit

## Current decision as of 2 October 2026

The full goal remains open. The complete offline candidate gate now passes all
14 checks, including 274 analyzer tests, native wallet registration, procurement,
reward guarding, quest observation and rolling-log preservation. Sidecar Release
builds passed without warnings or errors; protected artifacts passed verification.
The receipt is `output/combined-economy-check-20261002-170453-f8fa6d65/receipt.json`.
This verification did not deploy, arm a capture, start a game or change a save.

The 29 September wallet correction is included: diagnostics SHA-256
`5C1F082D156A27995514EA37ECC42AF3936B4A93696D43461B313EBBA86BBE8B`.
Independent replay of the completed 16.000020-day run reconstructs the observed
balances while rejecting 493 duplicate baselines and 1,553 contradictory checks.
The old identity errors remain in preserved evidence; passing reconstruction does
not retroactively make that capture's identity integrity pass.

### Requirements and remaining evidence

- **Rolling diagnostics:** repeated 30-day rotation, owned-file replacement,
  reload/rollback protection and incident preservation pass native fixtures.
  The completed 16-day campaign is not proof of a full live 30-day rotation or
  sustained overhead. The corrected recorder still needs live acceptance.
- **Livestock and wool:** the completed capture observes 2,872 successful
  livestock cycles and 407 wool cycles, with separate input, margin and cash
  gate evidence. Final stock reconciliation checks 3,819 balances without
  residuals. Repeated gate failures do not establish independent shortages;
  current evidence does not justify blanket resource increases.
- **Workshop accounting:** 100 procurement transactions and 6,265 successful
  capital-affecting batches reconcile. Wine, smithy and linen aggregate operating
  results are positive after separating withdrawals, but expenses are inferred
  from native daily ticks. Return, liquidation and rare failure/rollback paths
  are not all exercised in the campaign. Fixture coverage is not live coverage.
- **Battle and naval rewards:** native guard and attribution checks pass.
  Player naval-selling penalties and rare reward routes remain live evidence
  gaps, not confirmed economic defects. The historic 28.19-million increase is
  not explained by a different cohort's net loss.
- **Framework authority:** `CampaignSystems.cs` records explicit owners.
  Shared settings, procurement and optional logistics services are implemented;
  quest observation is separate from protected deadline scaling. This is not
  universal authority over third-party economic behavior or every quest timer.
- **Demonstrated defects:** stable wallet identity and bounded critical incident
  retention are implemented and offline verified. No additional production or
  income adjustment is supported by this evidence.

### Bounded next action

Obtain deployment authorization before installing the verified candidate. After
installation, a short focused live check can validate the wallet/recorder fix;
it cannot certify unexercised naval branches or a 30-day rollover. Keep those
explicitly open rather than silently substituting a short check for the full
goal. Do not demand another long soak merely to repeat already reconciled paths.
No further diagnostic expansion is justified without a concrete failed contract.

The completed run and original analysis are preserved under
`C:/Users/fpicc/Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/completed-6bb19a70f90e4d08a0bee5f48578d429`.
Its `wallet-identity-replay-20260929` supplement has 11 hash-verified files and
preserves the corrected replay separately from the original findings.

## Historical checkpoint before the completed September run

The following record is retained for provenance, not current status. Its 99-test
count, capture inventory, deployment limitations and unresolved stock description
must not override the current decision above.

Status: **not complete; not deployed; not a release certificate**.

Latest follow-up: module-aware lifecycle inventory is implemented and included
in the combined report. Regression tests cover dispatch-only procurement,
battle-only evidence with naval enabled, absent optional modules and conflicting
availability. Current-status documentation now explicitly supersedes historical
implementation notes. The 99-test analyzer suite and diagnostics Release build
pass. The subsequent full offline candidate gate and independent re-review passed.
These resolve the identified implementation/reporting gaps, not live acceptance
or economic balance. Deployment authorization remains pending.

Safe explicit closure is now implemented at the application-tick boundary with
terminal snapshots before SESSION_END, and deferral while observed scopes are
active. Its fixture/native suite and Release build pass. Ordinary teardown still
does not certify complete capture. The lifecycle inventory and current-status
consolidation are implemented; the following review findings are historical.

Independent pre-runtime review found two remaining blockers before requesting a
broad acceptance run: a safe final snapshot/close mechanism while campaign state
still exists, and explicit module-aware inventories of unexercised procurement
and reward lifecycle branches. A third finding, persistent-marker registration
on reload, was fixed: OnGameStart now considers the rolling marker. All 16 pacing/
soak/one-shot/rolling marker combinations passed the focused registration check,
along with the native rolling suite and clean Release build. The diagnostics hash
below predates this fix and is not a deployable current-build receipt.

The review also requested consolidation of the rolling-diagnostics status document
because historical implementation notes contradicted later completed work. No new
economic rate defect was demonstrated; runtime validation remains required after
the pre-runtime blockers are resolved.

## Requirement and evidence

| Requirement | Current evidence | Still unproven |
|---|---|---|
| Rolling 30-campaign-day diagnostics | Fixed owned current file, repeat-rotation/reload/rollback fixtures, latest-observed-time checkpoint fix | In-game reload, sustained overhead, filesystem failure behavior under real load |
| Livestock/wool supply and competing demand | Existing 15-day capture; town-consumption/trade/workshop breakdown; recipe-aware production checks | 31 old-capture stock residuals; new receipt coverage in a campaign |
| Workshop cargo and profitability | Snapshot/basis replay, explicit transaction cash joins, market identity, committed cost recognition and operating-cost report; regression tests | Full runtime coverage of dispatch/arrival/consumption/return/liquidation; inferred daily expense attribution |
| Battle/naval reward attribution | Native allocation/reset targets, canonical wallet joins, native post-penalty request, ship valuation/destruction identity | Live outcomes; independent player-penalty formula validation; historical 28.19-million cohort attribution |
| Demonstrated economic fixes | Existing candidate native conservation/recipe/procurement fixtures pass; no new guessed rate changes | Economic effectiveness of candidate in a matching live cohort; residual diagnosis before further tuning |
| Framework authority | CampaignSystems.cs explicitly declares owners for clock, settings, rates, procurement, quests and diagnostics | No universal third-party policy enforcement; not authority over every economic subsystem |
| Protection and verification | Candidate gate passes protected baseline/calendar/map checks, sidecar Release builds and analyzer/native fixtures | Public release/package/security approval; no private-test scan requested |

## Evidence provenance

Latest user-directory supply capture found by read-only file inventory:
`AocSupply-f50288772c15414ba92b923b3227d01a.tsv`, 968,473,941 bytes,
last write 2026-09-20 19:03:39 UTC. No `AocFramework*` capture exists there.
This older capture predates the new rolling/transaction/reward observations;
its absence of those records is not evidence the new hooks failed.

Audited build hashes at this checkpoint (later builds require fresh hashes):

- Diagnostics: `0CEF004BB896E710E040BBC47AAD26EDF0DED6BEC10355F96B7AF9073BB1B640`
- Procurement: `D7D8C575298E1F71EC7EF8CC9F8F626148A076008C9F22C3CF58DD1C76DB2569`
- CampaignSystems companion: `4D4349EF6E9E65CF51DDAA7962CE21485BF773E16480BB0F2EE42E00B97B5A60`

The full offline candidate gate was rerun successfully after the checkpoint,
safe-close and lifecycle-inventory changes. The Python suite contains 99 tests.
The deployment process guard additionally passed 10 assertions for game/launcher
detection and fail-closed enumeration; diagnostics Release build and protected
baseline verification passed afterward. Test counts do not replace the
requirement-specific runtime evidence above.

## Next decision

Independent pre-runtime review is complete. Await the requested authorization,
then prepare and deploy a hash-bound private candidate using an explicitly scoped
private-test contract (no Windows security scan; public release gates unchanged).
The existing installer still requires its older security receipt; do not fabricate
that receipt or bypass it. Do not silently use an older release receipt, start
or restart the game, alter settings/saves, or declare a fixed economy based on
fixture success. Missing rare events require an explicit coverage decision, not
an automatic demand for another long soak.
