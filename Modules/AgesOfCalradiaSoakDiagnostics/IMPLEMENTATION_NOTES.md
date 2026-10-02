# Economy diagnostic additions

## Status

Implemented and Release-built locally. Not deployed, not activated in the game,
and no campaign or soak launched. Existing saves, pacing settings, borders,
protected Core DLL and WorldCalendar prefab are unchanged.

## Boundary and compatibility

Only the diagnostics module changes. Its 57 static native targets are enumerated
by `EconomyDiagnosticPatches.Targets()` for the installed Bannerlord 1.4.8
assemblies. Added targets are both production-cycle methods, both production
eligibility predicates, input-availability predicate, bankruptcy handler,
workshop conversion-speed model and village item-production model. These are
observer prefixes/postfixes/finalizers, not replacements or transpilers.

The observer never changes arguments/results, repeats production/finance model
calls for evidence, or suppresses original exceptions. Ref finance values use a
direct typed finalizer rather than stale boxed output arguments. Missing native
targets or observation errors invalidate evidence and remove/disable capture;
they do not alter simulation. Other patch owners and native MVIDs are recorded.
Overrides/inlining can bypass hooks and must not be treated as observed coverage.

## Evidence

- Default: bounded daily flow aggregates with gross positive/negative values,
  net change and counts. Inventory cardinality is reduced by caller/item, so
  this is explicitly not a complete transaction ledger.
- Opt-in `AocEconomyTransactions.enabled`: the detailed short-validation ledger,
  with parent scopes and actual account/inventory changes. No marker is created
  by the implementation or build.
- Actual recipe attempt/result and eligibility result, native-candidate rejection
  reasons, progress before/after, attempts/successes and derived native increment.
  Shared-speed model calls do not pretend to identify a recipe.
- Village production model results keyed by item; recipe/input/output definitions
  retain actual food-category properties, including felt and livestock.
- All current workshop recipe market categories include explicit zero stock,
  concrete in-stock item quotes, and unavailable quotes when absent.
- Daily workshop owner/type/capital, bankruptcy-handler before/after context,
  town rebellion/siege/loyalty, village output/trade destination, clan-leader and
  kingdom budget balances, and actual model identities.
- Monotonic wall seconds per observed campaign day; includes pauses/stalls and
  records the current speed mode. A mixed-speed interval is not a calibrated
  measurement of that last mode.
- Opening-peace caller classification retains raw reversal warnings.

Daily writer bounds are 50,000 keys/day and 5 GiB/session. Raw mode retains
its 1 GiB cap; accounting retains its 500,000-key cap. Overflow is a diagnostic
failure, never silent data loss presented as success. Summary files without
session completion are rejected. Aggregation detail is last context, not a list
of every transaction; first/last values are not additive balances across owners.
Inventory indexing avoids scanning all accounts separately for every roster.

The zero-net settlement fix writes completed scopes even when income/expense
offset. Raw coverage accepts a no-cash root only when both zero settlement and
zero final finance-result evidence exist. Coverage still does not establish
individual wage/tribute affordability or global cash conservation.

## Verification

- Isolated module Release build: passed, zero compiler warnings/errors.
- `Verify-EconomyTransactions.ps1`: passed; 57 installed-version targets,
  synthetic Harmony deltas/clamping, nesting, indexed inventory disappearance,
  ref finance preservation, unchanged bool/model results without re-evaluation,
  original exceptions and session closure.
- `Verify-EconomyDailySummary.ps1`: passed; offsetting gross/net flows, zero-net
  records, day rollover, item grouping, numeric rejection, incomplete-session
  rejection, 50,000-key overflow rejection and actual native IL patch installation
  in an isolated verifier process.
- `Verify-EconomyCoverageGate.ps1`: passed, including no-cash zero-net root and
  rejection when the zero finance result is missing/contradictory.
- Existing soak and peace checks: passed.
- `Tests/Verify-ProtectedPoliticalBaseline.ps1`: passed.
- Broader `Tests/Verify-CalendarMath.ps1`: blocked by existing protected-artifact
  mismatch, expected campaign profile schema 6 versus actual 5. No protected DLL
  rebuild or test weakening was performed.
- Broader `Tests/Verify-StrategicMapCoverage.ps1`: no verdict; its isolated
  verifier process was stopped after more than three minutes without output.
  This unrelated asset check was not weakened or reported as passing.

`Verify-EconomyDailyCoverage.ps1` is for completed real daily summaries;
`Verify-EconomyRuntimeCoverage.ps1` is for completed real raw ledgers. Their
synthetic fixture tests do not count as live campaign validation.

## Remaining live acceptance

NOT_EXERCISED: actual campaign branch coverage, wages/tribute attribution,
warehouse flows, real save/reload lifecycle, runtime CPU overhead and year-long
log volume. The prior raw overflow makes short overhead/volume validation
mandatory before unattended long use. Do not claim the new build is logging in
an already running game. Do not claim source coverage means every mod's internal
economy or every Nexus issue is instrumented.

No deployment or release packaging was performed. The immutable Core build was
not rebuilt to satisfy the generic repository build command; only the isolated
diagnostics project was compiled.
