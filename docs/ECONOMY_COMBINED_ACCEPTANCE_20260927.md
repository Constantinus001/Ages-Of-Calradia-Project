# Combined economy acceptance — readiness first

## Scope and boundary

Diagnostics sidecar, offline analyzers and explicit preparation tools only.
No income/production rates, calendar settings, protected Core/UI, save schema,
save contents, speed, pauses, launcher selections or game process control change.
This is a private acceptance candidate, not a public release or balance certificate.

## Implemented improvements

1. `CaptureReadiness` writes a compact atomic receipt after successful startup,
   required-hook validation and capture flush, then at most once per five seconds.
   It includes process identity/start, campaign/session, loaded assembly MVIDs,
   disk hashes, required hooks, committed/pending/discarded counters and lightweight
   production/procurement/naval counters. Hook removal after startup fails capture
   safely. The read-only readiness command additionally verifies launcher selection,
   candidate/installed hashes, free space, activation conflicts, process identity,
   receipt freshness, latest complete row session and the campaign checkpoint.
   `-ProbeWrite` owns only a unique disposable probe file. A successful offline
   check says `READY_TO_LOAD_NOT_RECORDING`, never that a game has been loaded.
2. Wealth observation now brackets actual non-ref clan income/expense model calls
   and daily clan finance with canonical hero/clan/kingdom wallets. Snapshot identity
   maps heroes to noble clans; changed membership is excluded from simple clan
   aggregation. Existing GiveGold transaction tags/caller chains and workshop payout
   records remain available. All receipts are net of nested wallet changes; gross
   transfers are not added again. Null-giver transfers are not automatically grants.
   Reports preserve unknown callers and intermediate as well as final residuals.
3. Ship lifecycle reads native `MobileParty.RemoveParty()` and `Ship.set_Owner`.
   Reward snapshots also record post-distribution ownership and destination
   membership, covering an inlined owner setter without inventing destruction.
   Native `DestroyShipAction.Apply` remains a separate stronger observation.
   The actual selected ship-cost model's `GetShipSellingPenalty()` result is
   observed once during recovery, never recalculated. Skipped calls, transfers,
   list removal and destruction remain distinct. Player formula certification
   still requires a real exercised player recovery with coherent payout evidence.
4. One streaming detail analyzer groups livestock-to-meat and actual wool-output
   recipes, wine/smithy/linen workshops, deduplicated input-gate cycles, native
   cash/margin rejection candidates, progress, inputs consumed and outputs made.
   It provides next checks, not automatic balance diagnoses. Existing batch,
   transaction, accrual and profitability analyzers remain authoritative for their
   joins; owner withdrawals remain distinct from operating profit. Weak demand
   cannot be inferred from a rejected quote or zero stock.
5. Daily/terminal writer-cost receipts measure serialization/write/flush time,
   write calls, pending-memory peak and discarded rows. Day samples support actual
   bytes/day, projected 30-day size and seconds/day including pauses. Writer time
   explicitly excludes observer snapshots, stack walks and status IO: a measured
   lower bound, not claimed total diagnostic overhead/FPS impact. No-op money
   requests omit the expensive stack walk but still measure post-call cash, so
   another patch's unexpected mutation is not silently dropped. Routine monitoring
   reads the compact receipt instead of repeatedly scanning the full ledger.

## Native compatibility and failure contract

Native target definitions are in `SupplyCashObserver.BoundaryTargets`,
`SupplyShipLifecycleObserver.Targets`, and `SupplyRewardObserver.Install`.
Public finance signatures use Clan plus three bools; ref finance accumulators
are never marshalled through `__args` or recalculated. Missing signatures or
observer errors invalidate diagnostics and preserve native exceptions/gameplay.
Receipt corruption, stale identity, failed writes or missing hooks cannot certify
readiness. Hooks being installed is not proof that a branch executes in-game.

References: [Harmony argument injection and original-run status](https://harmony.pardeike.net/v2/articles/patching-injections.html)
and [inlining limitations](https://harmony.pardeike.net/v2/articles/patching-edgecases.html).
Installed 1.4.8 native removal IL was inspected and verifies the ship destruction
call inside `MobileParty.RemoveParty`; the binding is checked again by the fixture.

## One planned acceptance run

### Preparation, before asking the user to load

- Release build and native/synthetic regressions must pass.
- Deploy only the verified diagnostics sidecar with a hash-verified rollback copy;
  never replace the protected Core DLL/prefab or other economic modules.
- Run `Tests/Get-CaptureReadiness.ps1 -ProbeWrite` inside the diagnostics module.
- Run `Tests/Prepare-AcceptanceCapture.ps1 -ArchiveExisting -TargetDays 5` explicitly,
  with the game/launcher closed. It copies and hash-checks only the named current
  capture/state/summary/readiness/acceptance-day files to a unique archive, records
  their hashes, then removes those duplicate originals from the active paths.
  Old evidence remains recoverable. Unrelated diagnostics and saves are untouched.
- Recheck `READY_TO_LOAD_NOT_RECORDING`. Do not start/restart the game for the user.

### During the single load

Use the agreed existing save and the user's selected speed. Verify fresh `RECORDING`
before treating any elapsed days as acceptance evidence. Acceptance startup requires
valid framework configuration and an observed current opening procurement ledger.
Do not silently follow another session. A pause/nonadvancing day is not corruption;
a stale heartbeat is unverified recording, not proof that the save was not loaded.

The optional `AocAcceptance.days` contains 5. It bounds **diagnostics only** relative
to this fresh session. At five elapsed campaign days, final snapshots are taken and
the capture closes with `acceptance_window_complete_coverage_not_certified` while
the game remains running. It does not save, pause, alter speed or quit. Without this
file the existing normal 30-day rolling contract is unchanged. Acceptance mode
rejects reuse of a previous active log; explicit archival is required for another
run so evidence is never silently mixed or overwritten.

### Acceptance matrix and stop rules

| Area | Required evidence | Honest outcome if absent |
| --- | --- | --- |
| Readiness | Loaded identities, fresh session, live hooks, flushed writable log | Blocked with reason; do not count it as a run |
| Workshop cash | Aligned opening/final wallets, expense and payout boundary receipts, zero unexplained intermediate/final differences | Incomplete cash evidence; no profit certification |
| Noble wealth | Canonical observed sources, identity, finance settlements, residual checks | Preserve unknown sources; no income reduction |
| Chains and profitability | Recipe attempts/progress, consumption/output, procurement/payment/basis joins, town cash and market changes | Evidence gap/candidate mechanism, not an automatic shortage or demand claim |
| Naval | Executed reward, ship identities/values, transfers, removal and destruction; actual player penalty if exercised | NOT_EXERCISED for absent branches; no automatic rerun |
| Cost/integrity | No discarded records, aligned closure, measured day/growth/cost receipts | Incomplete; preserve evidence and diagnose offline |

At completion run `Tests/Analyze-FrameworkPeriods.py` once. It separates reload
segments and combines cash, procurement, reward, lifecycle, profitability and new
acceptance-detail reports. Do not treat an open scope/partial last row while the
writer is active as a confirmed persistent integrity failure. The five-day bound
does not guarantee rare events. Analyze this evidence before asking for any more
game time; do not induce artificial grants, wars, failures or destruction in the
user's save. No broad soak is scheduled by this work.

## Verification record

- Release diagnostics build, zero warnings/errors.
- 119 Python tests passed (readiness integration adds no fabricated raw receipts).
- Native cash/withdrawal, reward/penalty, ship lifecycle, naval coexistence,
  runtime summary, rolling preservation, bounded writer and new readiness fixtures.
- Readiness policy rejects stale/wrong PID, loaded MVID, session, hook and dropped-row evidence.
- Preparation fixture proves explicit hash-verified archival and unrelated-file preservation.
- Protected baseline/calendar checks passed. Strategic-map verification passed in
  Windows PowerShell; PowerShell 7 lacked a compiler reference for its pixel helper.
- No security scan/public-release gate or in-game acceptance pass is claimed.

Runtime results, diagnostic overhead beyond the measured writer lower bound, the
historical expense/payout split, exact cause of earlier wealth growth, and rare
player/naval branches remain unverified until actual evidence is recorded.

## Deployment and preparation evidence

Private deployment completed: only `AgesOfCalradia.SoakDiagnostics.dll` changed.
Installed SHA-256: `8A373B374845B7553B880A46B7D03328CE8F635ECA76FC01BD941B6AB4E05A12`.
Loaded-on-next-start candidate MVID: `d81f989b-93ba-4c7f-8ca6-54135ae18c52`.
Previous DLL backup:
`C:\Users\fpicc\AocRelease\combined-acceptance-2475c9ec36cb4c32bba18edb85d9f04b`.

The previous capture, checkpoint and summary were hash-verified into
`C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics\acceptance-archive-dc5c73e4d4134cbd82c1fd329e625bac`.
Its `archive-receipt.json` records original paths/hashes. Duplicate originals were
removed only from the active current-file paths; the archived evidence is recoverable.
The existing framework activation marker remains and `AocAcceptance.days` is 5.

Post-preparation readiness: `READY_TO_LOAD_NOT_RECORDING`, no blockers, write
probe verified, approximately 198.5 GiB free. Launcher selections are enabled for
Harmony, Core, diagnostics and NavalDLC. CampaignSystems and procurement source/
installed hashes match and neither was redeployed. Protected baseline passed
before and after deployment. No live loaded-session proof is claimed yet.
