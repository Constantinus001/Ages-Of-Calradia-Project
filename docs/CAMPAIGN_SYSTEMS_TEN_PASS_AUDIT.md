# Core Campaign Systems: ten audit-and-improve passes

2026-09-20. Undeployed candidate. No live game or saves touched.

## Readiness verdict

Ten reviews completed does not equal 10/10 pre-test readiness. Candidate regressions
pass, but the complete release gate stopped at the dirty-source guard. That first
attempt produced no archive/security verdict. The later isolated offline delta
candidate passed package checks and Defender scanning plus a ten-minute hold;
see CAMPAIGN_SYSTEMS_PRETEST_RECEIPT.md. This is not full-Core publication or live
acceptance. Do not describe the review count as 10/10 readiness.

| Pass | Finding | Improvement | Evidence |
| --- | --- | --- | --- |
| 1 Lifecycle | Startup/end/restart and locked configuration were not exercised through the native entry point; blank logistics command could throw | Native lifecycle fixture using only a private temporary config; blank argument guard; cached settings fingerprint | Startup/default/edited/invalid/locked/recovery/end/unload/noncampaign checks pass |
| 2 Configuration | Text outside the Procurement section could be silently ignored | Reject non-whitespace root text; verify equivalent overrides have the same fingerprint | Framework regression passes |
| 3 Save compatibility | Saved policy fingerprints accepted malformed text; fractional sub-day persisted settings bypassed schema bounds | Validate hexadecimal fingerprint length and nonzero duration minimum; retain zero legacy sentinel | Saved-state regression passes |
| 4 Time boundaries | Negative and negative-infinite due dates could qualify as due | Validate both sides of deadline comparison | NaN/infinite/negative cases pass |
| 5 Accounting | One account accessor could be supplied twice under different names | Reject repeated delegates and ambiguous whitespace identities before debit | Alias rejection and unchanged balance assertions pass; arbitrary differently wrapped aliases still require trustworthy adapters |
| 6 Dispatch | Offer fields and cargo could disagree before constructing transaction legs | Validate identities, item/category mapping, complete bundle quantities and proposed ledger before mutation | Malformed offer leaves wallets and ledger untouched; native dispatch regressions pass |
| 7 Supplier quotes | Every candidate input was priced twice; invalid cheapest quote could hide valid stock | Snapshot one quote per item, filter nonpositive quotes before selection | Native quote-call count and alternate valid input tests pass |
| 8 Logistics | Reflection handshake did not explicitly require a public version getter/non-generic read method | Enforce exact callable ABI shape before provider invocation | Private getter rejected; missing/unsupported/failing/known-zero/unknown cases and actual provider bridge pass |
| 9 Diagnostics | Failed/noncapital batches could bypass finite cash checks; other stream fields used unchecked float parsing | Check cash before early return, use finite parser for observed stream numbers, reject NaN JSON output | 35 analyzer tests pass |
| 10 Packaging | Core manifest referenced a companion not included in the full/slim release file sets | Add companion Release build, output checks, archive inclusion/expected-entry list; require framework integration gate | Script syntax and combined candidate pass; full release execution BLOCKED at uncommitted-source guard |

## Current verification

- Core companion, procurement, isolated diagnostics and isolated Logistics Release
  builds: zero warnings/errors.
- 40 framework assertions; 84 procurement assertions; 11 native lifecycle assertions.
- Native supplier quote/configuration scenarios, dispatch/duplicate/invalid plan,
  cash-limited return, replay rejection, delayed arrival, supplier war/removal,
  funded liquidation and 16 production cases.
- Real optional Logistics ABI discovery, no phantom reserve, known zero and stale
  campaign protection; existing Logistics math/dependency checks pass.
- 43 diagnostics Python tests pass after the framework/quest observation follow-up.
- Protected renderer, calendar contract and strategic-map checks pass.
- Full release attempted without bypass: blocked by existing uncommitted changes.
  It did not rebuild protected Core, generate a release archive or scan a package.

## Remaining pre-test gates (not hidden by the review count)

1. Exact clean release snapshot including this Core companion and its consumers;
   do not commit unrelated user changes or use AllowDirtySource to claim readiness.
2. Full archive/dependency verification and mandatory security scan/hold for the
   exact deliverable. Source-script review is not an archive/security PASS.
3. Procurement ledger size/admission follow-up is now closed by the checks below;
   save/load acceptance in the actual packaged campaign remains unproven.
4. Diagnostics coverage preflight for a single combined acceptance run, including
   settings/build identity, economic reconciliation and native quest observations.
5. Packaging/load-order and command discovery coverage for the actual bundle;
   no standalone duplicate framework assembly identity.

Logistics freight integration is outside the implemented capability: current
Logistics supplies party reserves, not a workshop freight provider. Read-only
plug-in discovery is implemented. No shipment hand-off or physical courier claim.
Core's ownership map still declares existing owners; it does not enforce authority
over every third-party Harmony patch. These scope limits remain explicit.

## Follow-up: persistence and candidate-bound diagnostics

The ledger writer now enforces the same 4,000,000-character bound as its reader.
Admission reserves escaped fault text and per-order lifecycle growth before native
cash or inventory changes. Saved fault text is capped; full exceptions are logged.
Readable legacy ledgers without growth headroom hold actions and retain their raw
save payload exactly. Pure tests exercise rejection, escaped text, extreme finite
timestamps and cargo round-trips; native fixtures verify no-charge rejection and
exact raw-payload preservation. Combined candidate gate passed after this change.

The economy coverage gate now accepts an explicit diagnostics assembly path; the
combined candidate gate supplies its fresh isolated build. Its synthetic fixtures
reject stale build identity, missing payment coverage, open scopes, reconciliation
gaps and malformed numbers without touching campaign logs. This is a preflight of
the evidence checker, not observed economic balance or complete quest coverage.

Further continuation: native quest observations and package preservation checks
are implemented. See CAMPAIGN_SYSTEMS_PRETEST_RECEIPT.md for the exact offline
candidate, test counts, ledger timing, fixture-log provenance and remaining limits.
The first candidate was blocked after detecting an outdated checkout manifest;
the replacement preserves all installed Core registrations rather than silently
removing border integrations. Full release approval is still distinct.
