# Temporary political render diagnostics

This test-only sidecar observes the approved AOC renderer on Bannerlord v1.4.8.
It does not alter meshes, materials, visibility, weather, ownership or saves.
The protected Core DLL and WorldCalendar prefab must remain byte-identical.
It is excluded from the production manifest and release package.

## Boundary and failure behavior

The integration boundary is Harmony observation of methods inside the approved
`TwelveMonthCalendar` renderer, plus main-thread reads from TaleWorlds.Engine.
The DLL is accepted only at SHA-256
`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`.
Missing or ambiguous targets, incompatible binaries, unexpected threads, stale
scenes or native/reflection/I/O failures disable collection and log the reason.
Prefixes return `void` and cannot skip the original methods; postfixes do not
replace their results. Other Harmony owners are recorded for comparison.

The observations target `CampaignPoliticalTerritoryFill.Builder.Advance`,
`AddFrontierSegment`, `HasFrontierLandSupport`, `TryGetFrontierPoint`,
`AddDoubleSidedQuad`, `CreateRowMesh`, the containing fill class's `AddTriangle`,
and `CampaignKingdomBorderBehavior.SetPoliticalOverlayAlpha`,
`ApplyPoliticalEntityVisibility`, and `OnMapFrame`. These are ten exact targets.
`Verify-NativeTargets.ps1` checks the actual patch bindings against the approved
assembly using Windows PowerShell and Harmony 2.4.2, without invoking a scene.
The first application tick establishes thread affinity; module loading may run
on another thread. Scene callbacks before that tick skip capture. The binding
check includes a regression for loader thread 25, application thread 1 and a
later rejected thread change, plus the real diagnostic tick's no-builder path.

Only the first builder is sampled. Collection has explicit caps and a 3 ms
main-thread query budget per tick; an individual native call can exceed that
budget, so peak and total sampling time are reported. This diagnostic overhead
is not part of a production performance measurement.

## Build and verify

```powershell
dotnet msbuild Tests/PoliticalRenderDiagnostics/PoliticalRenderDiagnostics.csproj /t:Rebuild /p:Configuration=Release /nologo
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests/PoliticalRenderDiagnostics/Verify-NativeTargets.ps1
dotnet msbuild Tests/PoliticalRenderDiagnostics/RenderProbeMathTests.csproj /t:Rebuild /p:Configuration=Release /nologo
& ./Tests/PoliticalRenderDiagnostics/bin/Release/RenderProbeMathTests.exe
& ./Tests/Verify-ProtectedPoliticalBaseline.ps1
```

Do not rebuild the protected Core project to run these checks.

## Capture and remove

Close Bannerlord and its launcher before registering the diagnostic module.
The installer adds only this sidecar to the installed Core manifest, backs up
the previous manifest, verifies unrelated entries, and checks protected hashes
before and after. It does not change the repository's production manifest.

```powershell
& ./Tests/PoliticalRenderDiagnostics/Set-DiagnosticCapture.ps1 -Mode Enable
```

Start the game, load the affected save and let the first political map build
finish. Reproduce the affected area at close, transition and full political
zoom, including the night view with stripes. Capture the same framing in
daylight if available. Keep the build, log and video from the same process.
A second save load deliberately does not start another capture; restart the
game for another independent sample.

The camera timeline captures at most 300 records at one record per second.
Reproduce promptly after loading. Keep the camera still over the affected area
for several seconds at night, then zoom slightly in and out. Repeat at the same
location and zoom in daylight when available. It records scene lighting time
and day/night state separately from campaign time, plus sun, fog, rain and snow.
This matters because AOC remaps campaign hours onto renderer lighting hours.

Results are written below the installed module's
`bin/Win64_Shipping_Client/PoliticalRenderDiagnostics/<UTC timestamp>/` as
`identity-timeline.log`, `segments.csv`, `fill-triangles.csv`, and
`camera-timeline.csv`. The sample CSV distinguishes fill triangles
from frontier segments, accepted geometry from rejected candidates, and measured
sample roles. Fill triangles include corners, edge midpoints and their centroid;
sampling is bounded and is not a complete mesh-coverage proof.

The raw triangle export captures up to 262,144 accepted logical triangles, with
all vertex coordinates and explicit truncation metadata. The analyzer compares
overlapping axis-aligned edges whose triangle interiors lie on opposite sides,
reports height mismatches and locations, and summarizes numerical clearances:

```powershell
& ./Tests/PoliticalRenderDiagnostics/Analyze-Capture.ps1 -CaptureDirectory '<capture folder>' -OutputPath '<workspace analysis path>'
```

Alpha assignment records describe the approved original's observed execution
path and inferred last assignment. They are not a native alpha getter. A skipped
original or competing assignment patch must not be interpreted as proof of a
native opacity value. Camera coordinates support correlation with video; the
logger does not automatically detect striped pixels.

After collecting, close the game and remove the temporary registration:

```powershell
& ./Tests/PoliticalRenderDiagnostics/Set-DiagnosticCapture.ps1 -Mode Disable
```

Removal preserves the logs and unrelated manifest entries. It does not restore
an old whole manifest over subsequent module changes.

## Interpretation limits

Fill vertices already contain the approved +4 terrain offset and use the
approved identity entity frame. Fill opacity changes with zoom; it must not use
the frontier's vertical translation. Recorded requested alpha and observed
native state must be distinguished when interpreting the timeline.

Finite water-query returns are numerical candidates only: this API does not
return a water-coverage validity flag. An apparent negative clearance requires
matching visible water and geometry before attributing an artifact to water.
Sparse terrain samples, material properties and source inspection cannot alone
prove the cause of a captured pixel. In particular, these probes do not isolate
flora, force a GPU pass or establish whether both triangle windings are drawn.
No visible repair is claimed by installing the observer.

## Engine-free measurement contracts

The approved renderer translates frontier entities by `-4.65 * (1 - alpha)`.
Measure triangle interior points as well as vertices in final world coordinates.

Surface queries must report success and a justified valid range. Pass a finite
no-surface sentinel only when its semantics have been verified for that API.
Unknown measurements stay unknown, never zero. `Clear` means not below the
sampled surface within tolerance; it does not prove complete triangle clearance.

Endpoint counts use explicit 0.001-unit XY buckets with nearest, away-from-zero
rounding. Source coordinates remain unchanged. Cell centers can round trip their
keys at campaign-map scales; quantization does not recover original coordinates.
Neighboring buckets are not automatically merged. Caller-supplied intentional
endpoints are excluded only from the unexpected-endpoint count. No bridges form.

## Coastline-only evidence capture

The rejected border comparison was disabled on September 9. This diagnostic
revision does not change border geometry, political fill, island exclusions,
materials, depth behavior or zoom transforms.

New coastline-paths.csv records the original candidate segment and acceptance,
political-land state on both sides, native terrain classifications, and the
protected territory OwnerKey/Color properties. Samples use the same midpoint and
2.75-unit side spacing as the protected segment color selection. Land/nonland is
explicitly not labeled seawater: nonland may be an exclusion. Native terrain may
also be unavailable or classify a shoreline ambiguously; uncertainty is retained.

New coastline-surfaces.csv records the campaign-scene height at existing frontier
samples and compares it with the actual close/full zoom transforms. Invalid
height queries are reported as unknown. It does not establish visible water height
or prove a water-aware GPU compositor. Existing segments.csv retains terrain and
water-height candidates for correlation.

Both files run inside the existing three-millisecond geometry tick budget on the
bound scene thread. A metadata sample includes multiple native/reflection reads
and can exceed the deadline; peak tick timing remains logged. Output is bounded
by the original segment/sample caps and flushed on completion/unload. An optional
probe error disables this evidence stream without changing original rendering.

Release compilation and all ten existing Harmony bindings passed, along with
four classification guards and pinned owner/native-terrain metadata verification.
This is a diagnostic deployment, not a claimed coastline fix. The next scene
capture is required before selecting true coastline segments for replacement.

September 9 capture 20260909-161244-245: general capture completed, but the new
coastline stream failed on its first record because FrontierRegion Land/Owner
were accessed as fields. They are properties. The reader now explicitly requires
properties, and verification constructs actual approved FrontierRegion values for
both land and nonland and exercises the real reader, including null Owner.
Release and all binding/classification checks passed. No coastline findings may be
inferred from the failed stream. Installation of this correction is pending until
the launcher is closed; the working fill remains unchanged.
