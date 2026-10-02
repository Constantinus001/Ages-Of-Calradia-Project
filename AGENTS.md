# Agent instructions

Before changing code, read [`docs/CODE_QUALITY.md`](docs/CODE_QUALITY.md).

## Required workflow for every code change

1. Inspect the relevant code, tests, assets, and documentation before editing.
2. State the affected architectural boundary, compatibility risk, and intended
   verification when the change is more than a trivial edit.
3. Make the smallest focused change that satisfies the task.
4. Update the relevant test, contract check, or documentation when behavior or
   an architectural decision changes.
5. Run the Release build and the narrowest relevant verification checks.
6. Report what changed, what was verified, and any remaining failure or blocker.

## Required rules

- Preserve the boundaries between calendar/core logic, game integration, UI,
  persistence, diagnostics, and feature systems.
- New Harmony patches must document their native target, purpose, compatibility
  risk, failure behavior, and verification coverage.
- Do not add broad `catch (Exception)` handling unless crossing a Bannerlord,
  mod, file, or reflection boundary. Log the exception and fail safely.
- Keep new classes focused. Split a class when it approaches 800 lines or has
  more than one substantial responsibility.
- Keep settings and save/profile changes backward compatible unless the task
  explicitly changes the compatibility contract.
- Add or update a targeted verification check for behavior changes.
- Do not leave compiler warnings, dead code, temporary fallbacks, or debug
  output in a release path.
- Run the Release build and the relevant verification scripts before declaring
  a change complete. Record failures and environmental blockers clearly.
- Avoid unrelated refactors while implementing a feature. Refactor large
  existing classes in separately reviewable steps.

## Project-specific cautions

- **Diagnostic run closeout:** Immediately after analyzing a completed diagnostic
  run, archive that run in the same workflow; do not leave archiving until the
  next test preparation. Preserve its raw logs, checkpoint/readiness records,
  relevant incident packages, analysis reports, and build/settings provenance
  together with a hash manifest. Verify the archive before removing only the
  duplicated files belonging to that closed run from the active capture area.
  Never archive or move files belonging to a live writer or a newer session.
  If closure, analysis, or archive verification is uncertain, preserve originals
  and report the blocker. Report the archive location with the findings.
  Archiving does not authorize rearming, loading saves, or restarting the game.

- **Protected political renderer:** `bin/Win64_Shipping_Client/AgesOfCalradia.dll`
  is an immutable user-approved artifact. Its required SHA-256 is
  `560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`.
  The approved `GUI\Prefabs\WorldCalendar\WorldCalendar.xml` is also immutable
  at SHA-256 `E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E`.
  Never rebuild, replace, copy over, or deploy either protected artifact. Never
  fold new systems into them. Religion, population, strategic-map modes, and
  later features must ship in separate sidecar/module assemblies. Run
  `Tests\Verify-ProtectedPoliticalBaseline.ps1` before any deployment or release.
- Bannerlord APIs are version-sensitive; verify Harmony targets and optional
  integrations before relying on them.
- Native game calls, reflection, texture creation, mission setup, and save
  compatibility code must have a safe failure path and diagnostics.
- Production and diagnostics/test builds must remain intentionally separated.
- Treat `Tests\Verify-Release.ps1` as the release gate, not merely a packaging
  script.
