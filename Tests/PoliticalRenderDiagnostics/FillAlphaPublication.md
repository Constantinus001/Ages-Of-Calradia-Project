# Fill alpha publication defect and narrow correction

Approved `tmp/approved-behavior-diagnostic.cs:284-288` compares alpha to the
previous requested alpha using a 0.002 threshold, then always stores the new
request. `ApplyPoliticalEntityVisibility` skips native `SetAlpha` when neither
that threshold nor visibility changes (293-307). Consequently `.997 -> .998 ->
.999 -> 1 -> 1` can leave actual alpha at .997 indefinitely while the requested
value is 1. A forced rebuild resynchronizes it (383-388). This defect is proven
independently of whether it explains the reported stripes or the active shader
uses alpha dithering; those require runtime material/output evidence.

The narrow sidecar boundary is `ApplyPoliticalEntityVisibility(bool forceAlpha)`.
Retain existing force/readiness logic, augment force using the last successfully
published target, and force a changed exact 0/1 endpoint even below threshold.
Do not round intermediate fade targets or alter the renderer's visibility
cutoff. Commit only after the original runs successfully and publishes alpha to
visible, nonempty entities. Zero invokes native hiding and leaves the previous
applied alpha unchanged, since the original does not call `SetAlpha(0)`.
Per-behavior weak state avoids cross-load leakage. Invalidate on entity
replacement/clear or honor the existing forced replacement call before committing.
Do not commit a successful per-entity alpha claim when no entities were updated.
An exception must leave the tracker uncommitted so a future update can retry.

The executable fixture links the engine-free production policy from
`Builds/PoliticalFillAlphaFix/FillAlphaPublicationTracker.cs`. It does not deploy
native code. Skipped originals, native failures, and empty entity collections
must not advance the tracker.

```powershell
dotnet msbuild Tests/PoliticalRenderDiagnostics/FillAlphaContractTests.csproj /t:Rebuild /p:Configuration=Release /nologo
& ./Tests/PoliticalRenderDiagnostics/bin/FillAlpha/Release/FillAlphaContractTests.exe
```
