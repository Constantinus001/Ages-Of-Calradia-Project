# Political fill seam correction

This separate sidecar repairs the measured disagreement between independently
subdivided neighboring fill cells in the protected AOC renderer. It does not
rebuild or replace Core, alter coastline ribbons, change ownership, recolor
territories or modify the material/shader. See `GeometryContract.md` for the
engine-free correction and its limits.

## Evidence and scope

The baseline runtime capture contained 83,058 accepted logical fill triangles
and 28,185 overlapping coarse/fine edge pairs with unequal heights. Maximum
measured separation was 4.932564 world units. Offline correction of this entire
capture produces 118,254 triangles, with zero residual shared-edge height
mismatches in an independent analyzer. Only 9,902 original triangles change.
Original XY footprints, colors, row membership and UVs are preserved.

The active shader is `notexture`, with light/shadow reception disabled and no
shader dithering. The failing capture reached a full alpha assignment. The
separate staged alpha-bookkeeping correction is therefore not enabled for this
trial. Visual acceptance of the seam correction still requires the affected
day/night view after an actual successful runtime commit.

## Integration boundary

Supported Core SHA-256 is
`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`,
with the inspected Bannerlord v1.4.8 engine. Harmony owner
`aoc.political-fill-seam-fix.v1` attaches only to these approved mod methods:

| Target | Observation or purpose | Failure behavior |
|---|---|---|
| Fill builder `Advance` prefix/finalizer | Tracks builder generation and queues immutable CPU preparation after full fill capture. | Reentrancy, original exceptions or incomplete capture abandon correction; original exceptions propagate. |
| Fill builder `AddRowEntity` postfix | Associates exact submitted triangles with their original row mesh/entity. | Mismatched or missing associations abandon the generation. |
| Fill `AddTriangle` transpiler | Routes only the first of exactly two native face submissions through a relay, recording exact XYZ, UV and color after preserving the original native call. The reverse face is untouched. | Wrong DLL hash, changed two-call pattern or unverified competing owners disable setup. No global native Mesh patch is installed. |
| Behavior `ReplacePoliticalFillEntities` postfix | Confirms that the captured rows are the published generation. | Publication mismatch leaves original fill in use. |
| Builder `Cancel` prefix | Discards stale capture and preparation state. | No change to original cancellation. |

The first application tick establishes native thread affinity. Module loading
does not establish it. CPU preparation receives engine-free immutable values;
scene and mesh calls remain on the application thread. Source/output limits and
geometry validation reject unexpected inputs without committing partial meshes.

## Native upload and replacement

The approved fill publishes normally while replacement meshes are prepared.
Detached candidates retain the original row material and render order. Native
upload is split into a four-millisecond tick budget and at most 256 logical
triangles per lock. An individual native call, finalization or final commit can
exceed the budget; actual times are logged.

Original face counts must equal twice their captured logical triangle counts.
Candidate face counts are checked after every unlocked chunk and after final
normal/bounds computation. Thus an unsupported lock/append behavior cannot
silently install a partial row.

After all candidates pass, the transaction checks actual entity mesh membership,
attaches each candidate and removes the original. Originals remain referenced
until commit and the approved forced alpha/visibility refresh succeed. Failure
attempts rollback using current membership rather than assumptions about where
an exception occurred. Any incomplete native rollback is explicitly logged;
the engine has no atomic mesh-replacement API. Successful commit releases old
meshes and CPU snapshots. The existing entities, frames and behavior lists remain
in place; intended alpha is reapplied through the approved behavior.

## Verification and trial

```powershell
dotnet msbuild Builds/PoliticalFillSeamFix/PoliticalFillSeamFix.csproj /t:Rebuild /p:Configuration=Release /nologo
dotnet msbuild Tests/PoliticalFillSeamFix/SeamGeometryTests.csproj /t:Rebuild /p:Configuration=Release /nologo
powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tests/PoliticalFillSeamFix/Verify-NativeTargets.ps1
```

Geometry verification covers the actual capture, unchanged faces, per-parent
area/attributes, disjoint and point-only contacts, invalid/conflicting input,
near-axis ambiguity and complexity limits. Native target verification binds the
five real targets and rejects a changed relay pattern without invoking a scene.
The shared mesh-transaction state machine has simulated failures before and
after mutations, alpha refresh failure and rollback failure checks.

With Bannerlord and its launcher closed, `Tests/PoliticalFillSeamFix/Set-SeamFixTrial.ps1`
registers or removes only this assembly in the installed manifest, backs up the
previous manifest and verifies protected hashes. It does not add the sidecar to
the repository's production manifest or publish a release.

Runtime evidence is in the installed binary directory's
`Logs/PoliticalFillSeamFix.log` and `fill-seam-corrected-*.csv` files. A
`COMMITTED` record includes source/output counts, validated shared edges,
expected/verified native faces and measured upload/commit time. A prepared CSV
alone does not prove a native commit. The final visual gate is the same save,
camera position and zoom in daylight and at night, plus zoom transitions and
map re-entry. Coastline clearance remains a separate diagnosis.

## Campaign-surface trial, September 9

The user reports stripes remained after the previous seam-only trial. This
revision adds a separate, bounded campaign-surface projection before seam
conformation. Its rendering approach is derived from observation of the supplied
Kingdom Frontiers binary; no decompiled implementation is included.

`FillSurfaceProjection` changes only the Z of vertices already accepted by Core.
It retains the triangle count, parent/row IDs, XY positions, colors and UVs. The
existing conformer then triangulates inside those same parent footprints. Thus
island exclusions, excluded channels and water holes are retained; the projection
does not run a new classifier, create cells or bridge absent regions. Coastline
ribbons and their exclusions are untouched.

Height comes from `Campaign.Current.MapSceneWrapper.GetHeightAtPoint` with
`CampaignVec2(..., isOnLand:false)`, followed by +3, matching the comparator's
surface-query policy. In the installed v1.4.8 wrapper this delegates to
`Scene.GetHeightAtPoint`, using Moveable | CommonCollisionExcludeFlags. This
is distinct from the approved terrain-only GetTerrainHeightAndNormal path.
Whether the difference accounts for the remaining stripes is not yet proven.
The log reports query count and min/max height differences in the actual scene.

All native surface reads run on the bound application thread, after original
publication, at most 256 triangles per tick and with a 4 ms deadline checked
between triangles. A triangle can require up to three new native queries and
can exceed the deadline. Shared XY positions are queried once per generation.
The CPU worker receives only completed immutable projected triangles. A failed
or nonfinite query abandons the candidate; the original renderer remains visible.
New generations/scene changes discard the pending projection. Candidate upload,
face validation, alpha refresh and transaction rollback retain the existing path.
Even a generation with no seam subdivisions must publish changed surface heights.

Verification: Release build, 23 surface-projection behavioral checks, 27 seam
checks including the full 83,058-triangle baseline, seven transaction/rollback
checks, five actual Harmony bindings, and protected Core/prefab hash checks.
Runtime surface clearance, stripe disappearance and preparation cost still need
measurement. This is a trial, not a validated release fix.
