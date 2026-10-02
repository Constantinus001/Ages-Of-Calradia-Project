# MapBar target-layout specification

## Current repair — 2026-09-04

The reference `codex-clipboard-088733ec-83b4-4589-bb20-1c0298214f7f.png`
supersedes the historical composition below: filled segmented bronze half-ring,
silver circular bezel, and native animated day/night aperture at the bar edge.
The four panels contain pictograms in left-to-right order: snowflake (winter),
sprout (spring), sun (summer), leaf (autumn). Muted seasonal colors retain
contrast against bronze. These are drawn directly, with no emoji-font dependency.
Buttons are deferred and keep existing commands. The four time controls move
seven pixels right solely to maintain clearance from the enlarged bezel.

In the 802x145 diagnostic stage, the visible native frame starts at y64
(the frame widget at y46 has 18 pixels of transparent sprite padding).
The halo is162x93 at321,-14; the108x108 bezel is at348,12.
The1.5x scale matches the reference's assembly-to-center-panel proportion.
Its156x105 native MapTimeImageBrushWidget uses circular clip radius42 and
DayTime=@Time. The ring asset is transparent inside and outside.
Both moving brush strips scale with the widget to316.5x105 with offset27.
The halo rises14 pixels above the diagnostic panel, within the unclipped stage.
Capture crops must include that upper area rather than cut off the arch.
The obsolete notch needs a60x66 native-stone backing at372,64.
The year-season label is110 pixels wide and ends before the clock begins.

Build-PlainDialHalo.py owns the plain halo and silver rim. The older generator
entry point delegates to it so it cannot overwrite these with rejected icons.
Production and diagnostic texture providers use the same dimensions and assets.
Reporter validation uses named widgets and checks the geometry against XML;
this repairs the mismatched ID/rectangle arrays that previously broke captures.

The Release sidecar build, layout/host checks, and full Approved560 verification
pass. A successful build is not evidence of visual acceptance or campaign runtime.
No protected core DLL or WorldCalendar prefab is rebuilt by this repair.

## Historical specification (superseded)

## Source of truth

The current user-supplied target layout is the raised-medallion image supplied on
2026-09-03 as `codex-clipboard-17e9874b-3153-446c-8f4d-78b67f12e6ae.png`.
It supersedes the older 1944 x 355 full-composition reference. The target is a
layout reference; ordinary Bannerlord parts remain the cosmetic source of
truth.

## Measured composition

The non-black target composition occupies x 168..969 and y 125..269, an
802 x 145 footprint.

| Element | Target bounds | Required relationship |
|---|---:|---|
| Visible MapBar | x 168..969, y 190..269 | Vanilla textured frame stack. |
| Season crown | x 494..645, y 126..195 | Centered above the dial; four gilded season emblems read left-to-right as winter, spring, summer, autumn. |
| Circular assembly | approximately x 515..631, y 155..269 | Bronze rim surrounding one enlarged native live dial. |
| Date | approximately x 309..442, y 194..217 | Upper-left of the dial. |
| Year | approximately x 281..334, y 230..250 | Lower-left row. |
| Clock | approximately x 412..482, y 235..251 | Beside the year on the same row. |
| Time controls | approximately x 637..792, y 209..243 | Immediately right of the circular assembly. |
| World Calendar | approximately x 909..949, y 208..241 | Far-right compartment. |

## Implementation contract

- Use native `MapBar\mapbar_center_frame` sprites for the frame stack.
- Keep `MapTimeImageBrushWidget` and `DayTime="@Time"` as the live celestial
  renderer. Scale its two native `mapbar_center_circle_daynight` layers inside
  the dedicated `AocMapTimeImageLarge` brush; use a 104 x 70 widget and clip
  radius 35 so the underlying live aperture remains circular.
- Use separate bounded texture providers for the transparent 124 x 58 season
  crown and 94 x 94 bronze rim. Its true-alpha oval opening supplies the
  target perspective while the native moon/sun continues moving underneath.
- Cover the obsolete native center-frame notch behind the raised medallion with
  a 30 x 26 patch sampled from the same vanilla Shipping Client frame texture.
  Keep the patch behind the opaque center frame so only the notch opening can
  reveal it and no rectangular edge is exposed.
- Use pictorial season emblems in the compact crown: snowflake, sprout, sun,
  and falling leaf. Do not squeeze abbreviated season text back into the arch.
- Do not add a backing disc or a second nested dial.
- Do not restore the rejected horizontal season bar, custom celestial face,
  independent competing texture providers, or season-progress rotor.
- Date remains on the upper line. Year and clock share the lower line.

## Verification

- `Tests/Verify-MapBarTargetLayout.ps1` verifies the production XML and sidecar
  source contract.
- `Tests/Verify-MapBarPreviewCandidate.ps1` verifies measured diagnostic
  geometry and native asset bindings.
- `Tests/Verify-MapBarPreviewHost.ps1` verifies the isolated Shipping Client
  preview host.
- `Tests/Verify-ProtectedPoliticalBaseline.ps1` must pass before deployment.
# Season pointer placement correction

The season pointer uses a 75px radius to embed it in the bezel. Its inward-facing
orientation converts the calendar angle from radians to Gauntlet degrees; calendar
progression and hover day counts are unchanged.
