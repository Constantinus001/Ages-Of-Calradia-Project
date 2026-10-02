# Current best build

## Current selection: August 28, 2026 at 6:44 PM borders

The latest user selection supersedes the August 29 selection below. Historical
deployment records and the pre-optimizer manifest indicate that August 28 at
18:44 America/New_York used the protected original renderer without the
Political Border Optimizer. The retained game log records a launch at 18:41:52;
the first optimizer deployment/load was August 29 at approximately 01:23/01:24.

Both current manifests therefore disable the optimizer and editor. No DLL,
cache, settings, save, UI, or clock asset is replaced for this selection.
The immutable renderer hash below remains verified. This restores historical
border rendering, not historical campaign ownership or unrelated game settings.
The v0.6.3 DLL is retained on disk, inactive, for recoverable comparison.

Pre-change manifests:
`output/deployment-backups/borders-20260828-1844-restoration-20260908-185627/`.
Verify with `Tests/Verify-BestBordersRestore.ps1` (default August28Original).
No compilation is needed for this configuration-only restoration; rebuilding
either the protected Core or newer optimizer would not restore the old binary.
Live visual confirmation after launch remains outstanding. No soak test starts.

## Previous selection: August 29, 12:15 PM borders (inactive)

The earlier requested baseline was the border sidecar installed on August 29,
2026 at 12:15 PM America/New_York (16:15 UTC), not the protected renderer alone.
Restore the archived **v0.6.3 baseline-visual repair** binary without rebuilding
the newer optimizer sources:

- Source: `tmp/deploy-backup-optimizer-v063-before-continuous-ribbon-v064-20260829/AgesOfCalradia.PoliticalBorderOptimizer.v0.6.3.dll`
- Installed/repository target: `bin/Win64_Shipping_Client/AgesOfCalradia.PoliticalBorderOptimizer.dll`
- SHA-256: `FC2B81B43F48E2028BAA79A5C5C67C8FBA324153CCFD77FE1DCBA3EE2497ED42`
- Evidence: deployment recorded at 09:35 on August 29; installed runtime log
  confirms v0.6.3 at 09:40:59 -04:00. The next deployment was v0.6.4 at 17:27,
  with its first recorded load at 17:32:27 -04:00.

This version retains original frontier shape/width, zero replay lift, flat
normals, reverse-fill-face cleanup, eager loading, and cache format v10. Its
versioned cache is isolated from later formats; no caches or authoring files
were deleted. The newer optimizer source tree is retained for development,
but rebuilding it does NOT reproduce this historical binary.

That restoration registered only this optimizer, leaving the Political Border
Editor disabled. Current UI/calendar sidecars and both protected assets remained unchanged.
Recoverable pre-restoration copies are in
`output/deployment-backups/borders-20260829-1215-restoration-20260908-185025/`.
Use `Tests/Verify-BestBordersRestore.ps1 -BorderBaseline August29V063` only when
deliberately selecting that older alternative, for its pinned binary/configuration
check. The current-source optimizer verifier is not a historical-binary gate.
This alternative is no longer registered by the current selection above.

The v1.5.12 patch preserves the user-accepted v1.5.11 visual baseline and adds
only the guarded combined-fix sidecar update.

## Approved archives

- Release: `artifacts/AgesOfCalradia-v1.5.12.zip`
  - Size: 38,187,151 bytes (36.42 MiB)
  - SHA-256: `333737B6B8053FE0915CB14DD35159CC7BCD9BBE5908581E11B73480D4BEFDEC`
- Test: `artifacts/AgesOfCalradia-v1.5.11-Test.zip`
  - SHA-256: `8FB203D63B9FF9BF3E9905A72E3B5FF537F127CA914AC35C6ABFE19098398622`

## Immutable visual baseline

`bin/Win64_Shipping_Client/AgesOfCalradia.dll`

SHA-256: `560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`

This exact main DLL contains the user-approved political fill. Do not rebuild
or replace it when restoring or extending v1.5.12.

This is a hard architectural boundary, not a recommendation. All new systems,
including religion, population, and World Events map modes, must be implemented
as separate modules or sidecars. Run
`Tests\Verify-ProtectedPoliticalBaseline.ps1` before deployment.

The approved World Events prefab is protected by the same gate at SHA-256
`E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E`.

Calendar and UI corrections are isolated in sidecars:

- `AgesOfCalradia.Approved560CalendarFixes.dll`
  - Version: `1.5.12.0`
  - SHA-256: `5187E07E2D323CB801CFF030D2D03F0EA9FBC335CDC03E97ECA695E94F24F2A7`
- `AgesOfCalradia.CampaignLabelVisibility.dll`
  - SHA-256: `59F9773D7F0B224FCA0109D0BAC8C9FECCD5ECC3663A0D683D0B6E97D06FDCD5`

The v1.5.12 release package excludes diagnostics, logs, and symbols. The older
v1.5.11 test package remains available separately and is not part of v1.5.12.
