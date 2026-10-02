# MapBar repair status — 2026-09-04

Target: user image codex-clipboard-088733ec-83b4-4589-bb20-1c0298214f7f.png.
Scope: UI assets, layout and diagnostics. Buttons retain existing commands.

## Fixed

- Preview reporter had 13 widget IDs but 12 geometry rows; the notch insertion
  shifted every later index. Named widget lookups and XML/rectangle correlation
  now prevent that capture failure.
- Halo/dial alignment now accounts for the native frame's transparent top inset.
- Native day/night widget remains bound to Time with circular clipping; the
  silver rim and bronze halo have transparent openings.
- Enlarged native stone backing covers the entire old notch.
- Year-season text no longer overlaps the clock.
- Removed obsolete provider recoloring and asymmetric alpha reduction.
- Old crown generator delegates to the current halo generator.

## Verification

Both sidecar and diagnostic host Release builds passed, zero warnings/errors.
Target layout, candidate, host, full Approved560 verifier and protected baseline
passed. Native request fae52184-5d91-4c2d-b5a6-895c11ff9b23 returned success after
three stable frames. Cropped actual capture: output/mapbar-repaired-native-preview.png.
The capture predates removal of the cosmetic provider edge-alpha reduction only.
Visual council found no blocking junction/aperture defect. A still does not
prove animation across time or campaign save/load behavior.

Production files are updated in the repository. Only AocMapBarPreviewHost was
deployed; the installed AOC CORE files were inspected but not replaced.
Protected AgesOfCalradia.dll and WorldCalendar.xml were not edited/rebuilt.

## Remaining

The subsequent proportions pass enlarged halo/rim1.5x to162x93 and108x108.
The native brush was scaled with the aperture (156x105, radius42); controls
moved7px right to maintain clearance. Both Release builds, full Approved560,
and focused layout/host checks passed. Native capture request
3dcf02f6-148b-42ab-9228-05790efa1217 succeeded; actual screenshot crop is
output/mapbar-reference-proportions-native.png. Visual council accepted the
reference proportions, continuous junction, and contained day/night aperture.
This supersedes the earlier smaller-center preview.
The subsequent icon-only pass adds winter/spring/summer/autumn pictograms in
left-to-right order without changing geometry. Native request
78e3caaa-b318-4e71-91d6-0db3a9219857 succeeded; latest actual capture is
output/mapbar-season-icons-final-native.png. Production checks validate each
panel's ordered icon color and opaque bronze background.
Buttons are deferred. Obtain visual acceptance before production deployment.
The broader Verify-CalendarMath check reports existing schema mismatch (expected6,
actual5); do not rebuild the protected Core to address it as part of UI work.
Verify-StrategicMapCoverage also fails its existing map-composer/legend-icons
contract. This UI repair does not change the protected WorldCalendar prefab.
