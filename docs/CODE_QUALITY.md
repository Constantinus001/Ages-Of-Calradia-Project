# Code quality and architecture standard

This document is the detailed engineering standard for Ages of Calradia. It
applies to production code, optional integrations, UI assets, diagnostics,
build scripts, and verification scripts.

This process is mandatory for every code-writing task, including small fixes.
The amount of inspection, documentation, and testing may be scaled to the
risk of the change, but no code change is exempt from verification and a clear
handoff.

The project is a Bannerlord v1.4.8 single-player module targeting .NET
Framework 4.7.2. Bannerlord's APIs, Harmony patch behavior, native UI
lifecycles, save behavior, and asset loading are compatibility boundaries.
Code must be designed around those boundaries rather than treating the game as
a normal application library.

## Architectural boundaries

Organize new work by responsibility. Existing files do not need an opportunistic
rewrite, but new features should follow these boundaries and refactors should
move one responsibility at a time.

### Core calendar and domain logic

This layer owns calendar conversion, leap years, month lengths, pacing math,
balance formulas, validation, and small domain records.

- Prefer deterministic, side-effect-free methods.
- Keep Bannerlord types out of pure calculations where practical.
- Put constants behind clearly named settings or domain constants.
- Test boundary dates, leap years, rounding, invalid input, and migration cases.

### Game integration and Harmony patches

This layer adapts Bannerlord models, behaviors, screens, and campaign events to
the module's domain rules.

Each patch should make clear:

1. the exact native target and supported game version;
2. why the patch is necessary;
3. whether it is a prefix, postfix, transpiler, finalizer, or replacement;
4. what happens if the target is absent or changed;
5. how duplicate ownership and conflicts are avoided; and
6. which verification check covers it.

Prefer the narrowest patch that preserves native behavior. Do not patch a
second layer when an earlier module layer already owns the behavior. Avoid
transpilers unless a stable model or method-level patch cannot express the
requirement; document every instruction pattern used by a transpiler.

### Settings and persistence

`CalendarSettingsState` is the central settings authority. UI adapters, XML,
MCM, and runtime consumers must read and write through the same state model.

- Validate every external value at the boundary.
- Preserve defaults and tolerate missing future/legacy attributes.
- Treat profile and save changes as migrations with explicit schema behavior.
- Never silently reinterpret an existing save.
- Keep fixed internal values out of user-facing settings unless they are truly
  supported configuration.

### UI and view models

View models expose state and commands to Gauntlet. They should not become the
place where campaign rules, file formats, asset discovery, and native model
patching are all implemented.

- Keep display formatting separate from domain calculations.
- Use stable data-source property names and explicit change notification.
- Keep layout constants together and document coordinate-system assumptions.
- Ensure optional UI integrations degrade to the native fallback.

### Feature systems and runtime assets

Refuges, camps, the strategic map, and other large features should have clear
boundaries between campaign state, mission/runtime behavior, layout or asset
catalogs, and UI.

- Keep asset manifests and runtime asset paths deterministic.
- Validate required scenes, markers, textures, navmesh, and prefab data before
  use.
- Never use a temporary/native fallback as a production release path without an
  explicit feature flag and verification check.
- Keep diagnostics-only features out of the normal player build.

### Diagnostics

Diagnostics should explain what the module attempted, what target or asset was
selected, and why a fallback or failure occurred. Do not log every campaign
tick or flood normal gameplay logs.

## Maintainability rules

- Prefer one primary responsibility per class and method.
- New production classes should normally stay below 500 lines; split classes
  approaching 800 lines. Large existing classes should be reduced through
  focused follow-up refactors rather than expanded indefinitely.
- Prefer named constants and small value types over repeated magic numbers.
- Use explicit names for native units, calendar units, coordinate spaces, and
  scaling factors.
- Keep public APIs minimal; use `internal` or `private` by default.
- Avoid static mutable state except for deliberate process-wide services,
  caches, or settings authority. Document reset and lifecycle behavior.
- Do not suppress compiler warnings without an explanation immediately beside
  the suppression.
- Remove dead fields, unused imports, abandoned adapters, and temporary debug
  code before release.

## Error handling

Catch exceptions only at meaningful recovery boundaries:

- optional integration discovery;
- reflection against a version-sensitive native API;
- file/XML/texture/scene loading;
- diagnostic or telemetry sinks; and
- user-facing operations with a defined safe fallback.

Every catch must answer three questions: what failed, what safe behavior
follows, and where the failure is recorded. Do not catch an exception merely to
continue with partially invalid state. Do not hide errors from required
startup, save migration, or invariant validation.

## Testing and verification

Use the smallest effective test for each change:

- pure calendar or balance logic: deterministic calculation checks;
- settings/profile changes: parsing, defaults, validation, and round-trip checks;
- Harmony/native integration: target audits and reflection checks;
- UI and asset changes: XML/source-contract and asset-dimension checks;
- release changes: complete packaging and archive verification.

Every code-writing task follows this sequence:

1. Inspect the relevant implementation, tests, assets, and documentation.
2. Identify the affected boundary, compatibility risk, and expected contract.
3. Make the smallest focused change.
4. Update the verification or documentation when behavior or design changes.
5. Run the Release build and the narrowest relevant checks.
6. Report the change, verification performed, and any remaining blocker.

At minimum, run:

For the immutable approved Core, do **not** run the Core rebuild below: build
only the changed sidecar in Release and run the two verification scripts against
the approved artifacts. `Verify-CalendarMath.ps1` selects its hash-bound
Protected560 contract automatically. Development schema-6 artifacts require the
Development contract; see [BROAD_VERIFICATION_CONTRACTS.md](BROAD_VERIFICATION_CONTRACTS.md).
The Core rebuild command applies only to separately authorized development work,
never to replacing or rebuilding the protected political renderer.

```powershell
dotnet msbuild TwelveMonthCalendar.csproj /t:Rebuild /p:Configuration=Release
& .\Tests\Verify-CalendarMath.ps1
& .\Tests\Verify-StrategicMapCoverage.ps1
```

Before publishing, run `Tests\Verify-Release.ps1` from a clean committed tree.
For the two economy sidecars only, use `Verify-Release.ps1 -EconomySidecarsOnly`
(optionally `-DeployEconomySidecars`). This mode creates a separate committed
source snapshot, builds and verifies that snapshot, and packages exactly the
calendar-fixes and diagnostics DLLs. It does not commit the user's working tree
or rebuild/copy protected Core assets. Protected baseline checks remain mandatory,
as do native coexistence tests, analyzer tests, exact hashes and the Defender
scan/hold. The normal full-Core release gate is unchanged. A scoped PASS is not
full-Core release certification or live economy validation. Evidence and rollback
copies are retained under `%USERPROFILE%\AocRelease\<unique-run>`. Short paths
avoid .NET Framework loading limits; avoiding AppData also prevents packaged-app
redirection from giving Defender's service a different view of the scan path.
If a DLL is locked by the game or another process, use an isolated output
directory for compilation and report the environmental lock separately from
source failures.

For the Core Campaign Systems offline candidate, use
`Tests/Verify-Release.ps1 -CampaignSystemsCandidateOnly`. This isolated snapshot
route rejects bypasses and deployment, and requires at least a ten-minute security
hold. It does not certify a public Nexus distribution; follow
`docs/CAMPAIGN_SYSTEMS_NEXUS_RELEASE.md` for the additional publication requirements.

Tests must verify observable contracts, not just that a symbol exists. When
implementation and test expectations disagree, resolve the contract and then
update the implementation and test together; do not weaken a failing check
without documenting why.

## Release checklist

- Release build succeeds with zero compiler warnings.
- Calendar and feature verification scripts pass.
- Production-only and diagnostics-only feature flags are correct.
- Optional MCM and other integrations remain optional and fail safely.
- Runtime archive contains only approved files and assets.
- Save/profile compatibility behavior is covered.
- README, changelog, handoff notes, and relevant design documentation match
  the shipped behavior.
- No generated binaries, logs, temporary assets, or editor work are committed.

## Change template

For a non-trivial change, record this in the pull request, handoff, or task
notes:

```text
Feature/bug:
Affected boundary:
Native targets or assets:
Source of truth:
Compatibility impact:
Failure/fallback behavior:
Verification run:
Known limitations:
```

When a design decision is not obvious, update the relevant document or add a
short decision record instead of encoding the rationale only in agent prompts.
