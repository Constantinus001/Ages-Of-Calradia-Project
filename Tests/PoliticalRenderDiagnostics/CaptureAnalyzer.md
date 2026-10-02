# Offline capture analyzer

`Analyze-Capture.ps1` reads a completed or partial diagnostic capture and writes
`capture-analysis.md` into that directory. It does not load Bannerlord, patch a
renderer, change capture inputs, or repair geometry. The C# helper streams the
numeric CSV files and indexes edges to support the 262144-triangle capture cap.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\PoliticalRenderDiagnostics\Analyze-Capture.ps1 -CaptureDirectory 'C:\path\to\capture'
```

Use `-OutputPath 'C:\path\to\report.md'` to choose another existing destination
directory. The output must have a `.md` extension. The script compiles its small
engine-independent helper in memory with `Add-Type`; no game assembly is used.

Inputs:

- `segments.csv`: the `NativeRenderProbe` schema, including `geometryKind`,
  acceptance and rejection fields, terrain clearances and water status.
- `fill-triangles.csv`: optional full-geometry dump with header
  `triangle,ax,ay,az,bx,by,bz,cx,cy,cz`. Missing geometry produces a report that
  explicitly omits continuity analysis.
- `identity-timeline.log`: optional provenance/completion evidence. The explicit
  `fillGeometryTruncated=true|false` field is reported when available; absent
  evidence remains unknown. Relevant cap/completion/truncation lines are quoted.

Malformed schemas, nonfinite triangle coordinates, duplicate triangle IDs and
invalid status/boolean fields fail instead of silently producing partial totals.
The CSV reader intentionally accepts the probe's unquoted numeric/status schema,
not arbitrary quoted CSV text.

The report separates distinct fill/frontier IDs from sample-row counts and
accepted geometry from rejected candidates. Negative terrain counts are sampled
measurements. Finite water-height differences are numerical candidates whose
water-coverage validity is unknown; they do not establish submersion.

For edges within 0.0001 world units of an axis, the analyzer compares overlapping
intervals only when their actual XY positions agree within 0.0001 at both overlap
endpoints and triangle interiors are on opposite sides. It measures interpolated
Z at those endpoints, distinguishes coarse/fine from equal-length overlap pairs,
and reports mismatch counts, maximum gap and up to 12 examples. Disjoint segments,
point-only contact, same-side overlays and degenerate triangles are excluded.
These local conditions do not establish global adjacency or whole-map continuity.
Non-axis edges and scene/terrain occlusion need separate investigation. No bridge
is created across missing geometry.

Analysis is limited to 262144 triangle rows and 10000000 indexed candidate pair
checks. A reached limit is explicit in the report. A below-cap file alone does
not demonstrate that the runtime capture completed.

Verification is separate from the protected production project:

```powershell
dotnet msbuild Tests/PoliticalRenderDiagnostics/CaptureAnalyzerTests.csproj /t:Rebuild /p:Configuration=Release /v:minimal
& .\Tests\PoliticalRenderDiagnostics\bin\CaptureAnalyzer\Release\CaptureAnalyzerTests.exe .\tmp\capture-analyzer-fixtures
```

Fixtures verify a 5.333333-unit coarse/fine seam, the conforming counterpart,
disjoint/point-only/same-side exclusions, actual XY tolerance across bucket
boundaries, distinct-ID and terrain counts, unknown water validity, explicit
runtime truncation, and a full-cap geometry input. Generated fixtures and build
outputs are diagnostics artifacts, not release contents.
