# Fill subdivision continuity witness

This is an engine-free counterexample to a continuity guarantee, not a claim
that the captured video has this terrain or that all visible strokes are cracks.
No game file or production geometry is changed.

The approved decompile `tmp/approved-fill-diagnostic.cs` samples base-grid
vertices at terrain +4 (123-126). Each cell independently subdivides up to depth
2, introducing exact-height edge midpoints only when that cell refines
(784-800). The relief test samples the cell center and compares it to the four
corners' average with a 0.75 threshold (824-839). There is no visible neighbor
subdivision propagation or submitted edge stitching in those methods.

Let two adjacent cells have normalized coordinates u=[0,1] and [1,2], v=[0,1],
all within one land/owner region. Use continuous terrain
`h=8*max(0,1-abs(u-.75)/.75)*max(0,1-abs(v-.5)/.5)`.
Every root corner has height 0. The left root center has height 16/3 and refines;
the right root center has height 0 and stays coarse. The left cell submits its
shared midpoint at `16/3+4`, while the neighboring coarse edge remains at 4.
Further left-side refinement cannot introduce a midpoint into the right cell.
The source therefore does not guarantee a watertight submitted fill surface.

The actual scene still needs captured adjacent edges and height measurements.
`FillEdgeContinuityProbe` reports an interior vertex's height difference from a
coarse edge only when their XY positions agree within an explicit tolerance.
It neither establishes triangle adjacency nor changes topology. Map-wide
adjacency must be established from captured triangles, excluding unrelated
overlays and reverse duplicates. Rasterization and native processing remain
separate from the submitted-coordinate proof.

```powershell
dotnet msbuild Tests/PoliticalRenderDiagnostics/FillSubdivisionContractTests.csproj /t:Rebuild /p:Configuration=Release /nologo
& ./Tests/PoliticalRenderDiagnostics/bin/FillSubdivision/Release/FillSubdivisionContractTests.exe
```
