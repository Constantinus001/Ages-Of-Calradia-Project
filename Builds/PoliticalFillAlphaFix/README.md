# Political fill alpha publication correction

Status: staged for verification; not registered or deployed. A passive capture
of the affected night view takes priority before attributing its stripes to
this defect. This correction does not claim to fix coastline gaps or every
interior stripe.

The approved renderer compares new zoom opacity with the preceding requested
value, even when that request was not published to the fill entities. A sequence
of changes below 0.002 can accumulate without a native alpha write. For example,
published 0.997 can remain 0.997 after requests 0.998, 0.999 and 1.000.

The engine-free tracker compares with the last successfully published target.
It preserves the native 0.002 threshold, accumulates small changes and publishes
changed exact endpoints. The sidecar augments `forceAlpha` on the approved
`TwelveMonthCalendar.CampaignKingdomBorderBehavior.ApplyPoliticalEntityVisibility(bool)`
method. It retains the original entity iteration, visibility and alpha writes.
It never replaces or rebuilds the protected DLL or prefab.

The Harmony owner is `aoc.political-fill-applied-alpha.v1`. Prefixes preserve
explicit native force and visibility transitions. A postfix commits the tracker
only when the original ran successfully with visible eligible entities and an
assignment path. Hiding at zero does not invent an alpha-zero assignment.
Replacement fill entities trigger the original forced publication. Weak keys
isolate behavior instances across campaigns without adding save data.

Compatibility boundary: supported only for Core SHA-256
`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`
and the inspected Bannerlord v1.4.8 API. Exact target and field types are checked.
Reflection/setup failures disable the correction and log the reason. Other
Harmony owners are reported; a competing patch that changes the original's
entity list or assignment semantics requires separate compatibility validation.
Native exceptions retain their original propagation and do not commit state.

```powershell
dotnet msbuild Builds/PoliticalFillAlphaFix/PoliticalFillAlphaFix.csproj /t:Rebuild /p:Configuration=Release /nologo
dotnet msbuild Tests/PoliticalRenderDiagnostics/FillAlphaContractTests.csproj /t:Rebuild /p:Configuration=Release /nologo
& ./Tests/PoliticalRenderDiagnostics/bin/FillAlpha/Release/FillAlphaContractTests.exe
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests/PoliticalRenderDiagnostics/Verify-FillAlphaFixTargets.ps1
& ./Tests/Verify-ProtectedPoliticalBaseline.ps1
```

The behavioral tests establish publication correctness, including repeated
plateaus, slow transitions, hiding/re-showing, replacement entities, failed and
skipped operations, retries and independent campaign instances. They cannot
establish which GPU pixels produced a recorded stripe. Native visual acceptance
requires the same camera position and zoom in daylight and at night, plus
evidence that full political opacity no longer retains a stale alpha target.
