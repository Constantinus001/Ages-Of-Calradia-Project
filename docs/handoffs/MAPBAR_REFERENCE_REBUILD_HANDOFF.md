# MapBar target-layout handoff

## Status

The current raised-medallion target layout has been researched, audited, implemented
in source, and rendered by the isolated Win64 Shipping Client preview host. It
has not been copied into the installed AOC CORE module.

Council-approved current preview:
`output/mapbar-reference-creative-final.png`

## Source of truth

The latest user-supplied target is
`codex-clipboard-17e9874b-3153-446c-8f4d-78b67f12e6ae.png`. It supersedes the
older 1944 x 355 composition. The image specifies layout; vanilla Bannerlord
assets remain the cosmetic source of truth.

See `docs/MAPBAR_REFERENCE_SPEC.md` for measured bounds.

## Implemented architecture

- A compensated 885 x 88 vanilla outer frame and 588 x 97 raised center frame;
  their painted contours reproduce the measured 802 x 145 target silhouette.
- One 94 x 94 dial input containing a 104 x 70 native animated day/night
  widget with a 35-pixel clip radius.
- A dedicated Gauntlet brush scales the original vanilla day/night sprite
  layers while preserving `MapTimeImageBrushWidget` and `DayTime="@Time"`.
- Separate bounded providers load the transparent season crown and true-alpha
  oval bronze rim. The rim has no backing, so the live vanilla face passes
  through while its curved inner lip supplies the reference perspective.
- Date is on the upper-left line. Year and clock share the lower-left line.
- Native pause/play/fast-forward controls remain right of the dial. W remains
  in the far-right compartment.
- Rejected horizontal season bars, custom celestial faces, and season pointer
  rotors are absent.

## Verification evidence

- `Tests/Verify-MapBarTargetLayout.ps1`: passed.
- `Tests/Verify-MapBarPreviewCandidate.ps1`: passed.
- `Tests/Verify-MapBarPreviewHost.ps1`: passed.
- `Tests/Verify-ProtectedPoliticalBaseline.ps1`: passed.
- `Tools/MapBarPreviewHost/AocMapBarPreviewHost.csproj` Release build: passed,
  zero warnings.
- `Builds/Approved560CalendarFixes/Approved560CalendarFixes.csproj` Release
  build: passed.
- Shipping Client request `838b7a77-a991-491e-a157-4c03f82085ff`: succeeded
  after three stable geometry frames at 1920 x 1080.
- Fresh midnight, dawn, noon, and dusk Shipping Client renders are recorded in
  `output/mapbar-final-four-phase-audit.png`.

The compact crown now uses four antique-gold pictorial season emblems—snowflake,
sprout, sun, and falling leaf—in place of abbreviated text. Its 124 x 58 bounds,
transparent center, raised placement, and the native animated dial underneath
remain unchanged.

The broad sidecar verifier still stops before its MapBar assertions because its
Harmony patch enumeration returns no owned patches in the standalone
PowerShell host. This is an existing verifier/runtime-host blocker; the new
focused production MapBar verifier passes.

## Agreed campaign-time soak values

The campaign-time sidecar is now configured for a four-real-hour 365.2425-day
campaign year:

- 1x: 39.425861 real seconds per day, about 4 hours per year.
- 2x: 19.712930 real seconds per day, about 2 hours per year.
- 4x: 9.856465 real seconds per day, about 1 hour per year.

These modes scale only the global campaign time delta. The soak test must
confirm that travel, AI, wages, aging, workshops, tournaments, sieges, and
diplomacy continue to resolve by in-game time rather than receive a second
speed multiplier.

## Protected boundaries

Never modify, rebuild, replace, or deploy:

- `bin/Win64_Shipping_Client/AgesOfCalradia.dll`
  (`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`)
- `GUI/Prefabs/WorldCalendar/WorldCalendar.xml`
  (`E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E`)
