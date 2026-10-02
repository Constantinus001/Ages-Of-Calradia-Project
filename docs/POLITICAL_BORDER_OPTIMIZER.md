# Political border optimizer sidecar

## Scope and boundary

`AgesOfCalradia.PoliticalBorderOptimizer.dll` is an optional game-integration
sidecar for Bannerlord v1.4.8. It does not modify, rebuild, replace, or deploy
the protected `AgesOfCalradia.dll` political renderer or the protected World
Events prefab.

The sidecar builds separate 384-column height and terrain snapshots in
three-millisecond main-thread slices. Political mesh construction remains
paused until that bounded snapshot is complete. The height snapshot samples
the approved exact-height method at grid vertices so bilinear lookups preserve
terrain-relief refinement without repeating native height probes during mesh
construction. Terrain cells retain both the native result and its validity:
cached invalid cells return `false` directly instead of repeating the same
`CampaignVec2` lookup. Calls made before the snapshot is ready or outside its
bounds fall through to the original implementation.

Every authored line, curve, freeform path, and Follow Coast route samples its
own endpoint height from that prepared local terrain snapshot. Ribbon centers
and both outer edges are then independently draped at their final XY positions,
so a hand-authored border follows slopes instead of inheriting the elevation of
a nearby generated coast. If an isolated prepared cell is unavailable, the
captured source height is used with the same terrain-relative overlay offset and
the aggregate fallback count is written to diagnostics.

The current editor-calibration build temporarily recolors every political-fill
triangle opaque light gray (`#D8D8D8`) after authored fill processing. This changes only
existing fill triangles: it does not create geometry over ocean, lake, or
island exclusions, and it does not recolor frontier ribbons. Diagnostics mark
the mode as `temporaryLightGrayFill=true` so it can be removed cleanly after the
manual border pass.

At campaign-load completion, the sidecar also asks the approved renderer to
finish its existing scene-bound build synchronously while Bannerlord still
owns the loading transition. An unattached overlay instance safely suppresses
label publication until the real map view attaches. This does not make native
mesh construction free: it deliberately exchanges incremental stalls after
the map appears for additional campaign loading time so borders can be ready
on the first political map frame.

Version 0.11.0 retains the protected renderer's accepted inland political
centerlines, territory fill and exclusion decisions. Ocean and lake ribbons
are aligned in world space to the repaired fill boundary, avoiding the detached
strokes produced by the rejected screen-space screenshot calibration. It then replaces the close-zoom
ribbon tessellation on a cold
build and stores a compressed, versioned geometry cache under
`Cache/PoliticalBorders/v60`. The key includes the approved renderer hash,
Bannerlord engine version, campaign scene, map bounds, settlement positions,
owners, political colours, and the authored override document hash. A warm load still prepares the small terrain
lookup grid needed by labels and integrations, but replays cached geometry in
bulk rather than rerunning adaptive territory/frontier classification.

Replay uses explicit upward normals instead of recomputing a separate lighting
normal for every unshared triangle. A v59 cold capture reduces the protected
renderer fill's uniform 4.0-unit presentation lift to a 2.0-unit terrain-relative
height while preserving every XY coordinate and exclusion decision. This keeps
the light-gray fill clearly above terrain and decals without restoring the old
full-height floating presentation.
Replay adds no further vertical offset. Frontier geometry and the close-zoom
frontier drop remain unchanged.

On a v59 cold build, the sidecar orders the captured graph into continuous
chains and rebuilds a 1.8-map-unit ribbon with three to seven tangent-continuous
curve sections selected from the turn alignment of each segment. Adjacent
sections use the same cross-section at
their shared node instead of independently offsetting both ends. Every protected
anchor remains fixed, while coastal interpolation may deviate no more than 0.20
map units from its accepted chord. The rebuilt surface emits only upward-facing
triangles: the protected renderer's coplanar reverse faces and overlapping
internal cap fans are omitted. True endpoints and junctions use butt geometry.
If every accepted segment cannot be rebuilt, the captured protected ribbon
remains active unchanged.

The editor XML may set a global border-width scale between 0.50x and 2.50x.
The scale is applied to every inland, coastal, generated, and authored ribbon
cross-section before the existing winding-safety solver runs. Authored paths
also persist Solid, Dashed, or Double presentation. Style transitions split
chains, dashed sections use a bounded 3-on/2-off pattern, and double paths emit
two terrain-draped outer bands with a protected center gap. Generated paths
default to Solid. Cache v59 prevents older geometry from bypassing these
settings.

The editor/optimizer integration boundary is the shared version-1
`PoliticalBorderOverrides.xml` document, not cached ribbon triangles. Both
sidecars compile the same bounded reader, and the optimizer includes the
document hash in its cache key. Consequently, later coastal alignment,
terrain-drape, ribbon tessellation, or cache-format fixes rebuild saved plans
without requiring the user to redraw them. Any truly incompatible document
change must use a new schema version plus an explicit migration.

The protected territory fill and inland centerline classification are never regenerated.
The fill-boundary contour corrects coastal ribbon endpoints by at most 1.25 map
units after protected land/water classification. This keeps ocean/lake ribbons
in the same coordinate space as the rendered terrain and preserves island
exclusions without any camera- or resolution-dependent transform.
The rejected contour smoothing, angular Voronoi, and inset-seam experiments are
not compiled into the sidecar. Replay adds zero height to both fill and frontier,
avoiding the earlier +3 floating-overlay regression. The protected `IsPoliticalLandExact`
classifier and IslandExclusion patch run before capture, so excluded islands,
water, and their border exclusion remain the source geometry authority. Cache
format changes invalidate prior experimental geometry automatically.

After protected contour repair and before ribbon tessellation, version 0.11.0
may read `ModuleData/PoliticalBorderOverrides.xml`. Validated authored paths
replace only nearby generated coastal segments, are resampled to no more than
1.5 map units per segment, and retain exactly shared endpoints. Every open path
after the first must touch the existing authored network; separate island and
lake paths must be closed loops. Authored fill polygons and adjustable brush
strokes only recolor protected fill triangles that already exist, so they
cannot construct fill over water or an excluded island. A malformed or
disconnected document is rejected in full and generated geometry remains
active.

Polygon and brush fill retains the saved inferred faction color. Legacy
colorless records fall back to neutral editor color `0xFFA8A096`. The stored
`faction` attribute remains the ownership association for the enclosing
authored region, while authored border ribbons use opaque black for both sides.
The later temporary-light-gray pass currently masks the saved fill color without
discarding it. The sidecar does not mutate campaign settlement ownership.

The current editor build suppresses every generated ocean/lake ribbon after
capturing its topology. Inland faction frontiers remain unchanged. A path
marked `followCoast="true"` snaps its anchors within eight map units of that
hidden topology and uses a bounded breadth-first route on the same connected
coast component. Disconnected anchors reject the complete override and restore
the generated segment list; successfully authored coastal routes are the only
coastal ribbons added back for display.

The versioned cache also retains bounded diagnostic metadata. Each cold build
and warm replay logs fill/frontier entity and triangle counts, degenerate and
oversized triangle counts, maximum triangle edge and area, the number of source
fill triangles whose terrain-derived normals could produce visible facets,
frontier endpoints and junctions, duplicate or zero-length segments, sharp
turns, maximum graph degree, source and rebuilt ribbon triangles, eliminated
internal-cap triangles, bounded miter joins, ribbon width, and the final cached
fill/frontier counts. Diagnostics also record the number of exact reverse fill
faces removed. These are aggregate
measurements; individual vertices and ordinary frames are not logged.

## Harmony targets and compatibility

All targets are internal members of the approved SHA-256
`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`:

| Native/mod target | Patch | Purpose | Failure behavior |
| --- | --- | --- | --- |
| `CampaignMapTerrainGridCache.Reset` | Postfix | Allocate sidecar height/terrain snapshots and capture map bounds. | Capture failure leaves original probes active. |
| `CampaignMapTerrainGridCache.Clear` | Prefix | Invalidate references before the scene-bound cache is cleared. | Original clear always runs. |
| `CampaignMapTerrainGridCache.BeginOrAdvance` | Postfix | Build the 384-column exact-height and terrain snapshots in bounded slices after the approved coarse grid is ready and delay mesh construction until completion. | Sampling failure returns the approved result and leaves original probes active. |
| `CampaignMapTerrainGridCache.TrySampleExactHeight` | Prefix | Replace repeated scene height calls with high-resolution bilinear interpolation after warmup. | Warmup, invalid vertices, and bounds misses run the original method. |
| `CampaignMapTerrainGridCache.TryGetNativeTerrain` | Prefix | Replace post-build `CampaignVec2` face resolution with the prepared high-resolution terrain snapshot, including definitive cached-invalid results. | Warmup and bounds misses run the original method. |
| `CampaignMapTerrainGridCache.IsPoliticalLandExact(Vec2)` | Read-only delegate | Diagnose the landward side of captured coast segments and clip the separate territory fill; accepted ribbon existence remains authoritative. | Missing readiness, reflection failure, or an exception preserves accepted ribbon topology and fails coastal fill clipping safely. |
| `CampaignPoliticalTerritoryFill.Builder.Advance` | Prefix/postfix | Measure per-frame builder time and publish aggregate diagnostics. | Diagnostics never replace builder behavior. |
| `CampaignKingdomBorderBehavior.OnGameLoadFinished` | Postfix | Complete the approved renderer's existing border build inside a bounded campaign-load window before the first political map frame. | Missing scene state, timeout, or reflection/native failure restores the dirty flag and leaves normal frame-by-frame rendering active. |
| `SandBox.View.Map.MapScreen.HandleIfBlockerStatesDisabled` in SHA-256 `C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2` | Prefix | For a published layout only, run the existing bounded build after the same ready-to-render checks that precede MapScreen's first ready frame, and set the published sidecar's loading scope so it applies borders and fill synchronously. | A hash/member/readiness mismatch disables the optimizer before patching. A build failure leaves MapScreen unchanged and restores the protected renderer's dirty retry. |
| `Mesh.AddTriangle` | Prefix | Capture exact approved fill/frontier triangle attributes only while the protected builder is synchronously active. | Outside an active cold capture it performs no work; capture failure abandons the cache and preserves the original call. |
| `Builder.AddRowEntity` / `Builder.AddFrontierEntity` | Prefix | Preserve the approved entity grouping for cached replay. | Original entity publication always runs on a cold build. |
| `Builder.AddFrontierSegment` / `CampaignPoliticalTerritoryFill.TryGetFrontierPoint` | Prefix/postfix | Capture only accepted frontier connectivity, side colors, and terrain-draped center points for the continuous ribbon. | Original exclusion and terrain projection remain authoritative; incomplete reconstruction retains the approved captured ribbon. |

Captured fill triangles retain their native kingdom colours. Inland ownership
frontiers also retain the renderer's native faction colours and classification;
ocean/lake centerlines are corrected only against the repaired world-space fill boundary.

The compatibility risk is intentionally constrained by checking the exact
approved renderer hash before patching. A missing type, member, field, or hash
match disables the complete optimizer and records the reason. Partial patches
are removed before returning control to Bannerlord.

## Diagnostics

`Logs/PoliticalBorderOptimizer.log` records:

- activation and approved-renderer hash validation;
- preservation of native faction fill colours, inland ownership borders, and faction-name labels;
- height/terrain snapshot allocation, separate sample counts, total work, and maximum slice;
- cached height hits, valid terrain hits, and definitive cached-invalid terrain hits;
- true original-method fallback counts;
- builder advance count, total time, and maximum single advance;
- scene clear, grid reset, build completion, and unload summaries.
- eager campaign-load warmup start, step count, wall time, deferral, or failure.
- persistent cache hit/miss, compressed size, triangle count, replay time and replay lift;
- captured and final fill/frontier counts, inland centerline preservation and
  world-space fill-boundary shoreline correction,
  source/generated ribbon triangles, removed internal cap triangles, mitered
  endpoints, ribbon width, zero inland-centerline displacement, and removed reverse faces.

Individual terrain probes and ordinary frames are not logged.

Version 0.6.6 additionally separates coastal ribbon evidence from territory-fill
evidence. It records coastal segment, node, endpoint, junction, sharp-turn,
maximum-length, and height-delta counts; source and output reverse-face counts;
and fill/ribbon triangles within 2.25 map units of a captured coastal centerline.
Near-coast fill facets are split into tilted, high-relief, and large-edge groups.
Output geometry separately reports non-upward and degenerate coastal faces. A
maximum of twelve suspicious map-coordinate samples is logged for each source
and output pass. This analysis is read-only and uses the protected renderer's
already accepted segments, so it cannot alter island or border exclusions.

Version 0.6.7 samples the protected single-argument `IsPoliticalLandExact`
decision at five positions along every same-colour coastal segment. When exactly one stable side
is land, the full 1.8-unit ribbon is emitted landward from the accepted coastal
centerline instead of extending over water. Ambiguous or changing decisions
retain both half-ribbons. Each generated left, center, and right vertex is
independently draped through the prepared exact-height snapshot. Before a curved
section is accepted, both triangles of every emitted quad must retain positive
map-plane winding; an unsafe curve is discarded and rebuilt as its protected
straight chord. Diagnostics record land-left, land-right, ambiguous, unsafe
curve fallback, rejected winding, and height-fallback counts.

Version 0.6.8 replaces independent per-segment offsets with shared chain
cross-sections. Coastal chains choose one stable landward side; if the chain
vote is ambiguous, each half is constrained independently and only a
classifier-approved half can survive. Every outer sample is checked at its
outer half, and unsafe width is reduced by bounded binary search. The outer
edge and both triangle centroids are also checked before a
coastal section can be emitted. Shared widths are reduced when necessary until
both triangles have positive map-plane winding. A still unsafe half-section is
suppressed rather than emitting an inverted or water-crossing triangle. New
diagnostics record chain and closed-chain counts, shared cross-sections,
constrained width samples, winding constraints, unsafe coastal samples, and
suppressed half-sections. Political fill is untouched.

Version 0.6.9 corrects the fail-safe contract for a chain whose every
half-section is rejected by the protected land/exclusion classifier. Such a
fully excluded chain now contributes no mesh and is counted as an empty chain;
it no longer aborts reconstruction of every other valid chain and trigger a
global fallback to the legacy ribbon.

Version 0.7.0 separates the remaining coastal artifact from ribbon geometry.
After reverse fill faces are removed, fully inland fill triangles remain byte-for-byte
unchanged. A fill triangle whose vertices, edge midpoints, or centroid cross the
protected `IsPoliticalLandExact` decision is adaptively subdivided to a bounded
depth. Terminal crossing edges are located by bisection and emitted pieces are
validated at their edge midpoints and centroid. Classifier failure or excessive
output abandons the complete fill refinement and retains the protected source
fill. This clips the coarse coloured fill fringe without regenerating ownership
cells or weakening IslandExclusion. Ribbon land checks retain their outer edge
and triangle-centroid invariants but no longer require the intentionally
coast-straddling inner quarter of the border to classify as land.

Version 0.7.1 increases boundary-only fill refinement from four to five levels
and raises the accepted terminal area to the same `0.0001` threshold used by
artifact diagnostics, preventing microscopic coastline slivers from entering
the cache. Ribbon interpolation uses three rather than five curve sections per
captured segment and limits coastal curve displacement to 0.20 map units. This
better matches a 1.8-unit-wide stroke and avoids creating curvature tighter
than the visible ribbon can safely offset.

Version 0.8.0 returns adaptive classifier clipping to four levels, then extracts
the exposed fill boundary only within three map units of a protected coastal
segment. Degree-two boundary nodes receive four bounded Taubin-style smoothing
passes with a maximum displacement of 0.28 map units. A candidate moves only
when probes on opposite sides still disagree under `IsPoliticalLandExact`.
Before mutation, any move that would invert or collapse an incident fill face
is rejected globally. Captured coastal ribbon endpoints snap to this validated
smoothed contour while internal political segments remain unchanged, giving
fill and coastal ribbon one visual source without regenerating ownership or
weakening IslandExclusion.

Version 0.8.1 preserves captured coastal graph connectivity when sharing the
smoothed fill contour. Each protected coastal endpoint receives the nearest
validated contour node's displacement vector rather than snapping to that
node's absolute position. Distinct neighboring endpoints therefore cannot
collapse onto one dense fill vertex, while fill and ribbon still inherit the
same bounded visual movement.

Version 0.8.2 replaces the order-dependent safety loop that repeatedly halved
both ends of each unsafe ribbon section. Every unsafe half-section now solves
for its largest valid width by bounded search, neighboring constraints are
collected without mutating the chain, and their minimum caps are applied
simultaneously for up to three convergence passes. Positive winding and
protected-land centroid checks remain mandatory. This preserves substantially
more of the rounded shared ribbon at tight coastal turns while an unsatisfiable
section still collapses safely instead of producing a protruding triangle.

Version 0.8.3 increases sampling only where a captured segment enters or exits
a substantial turn: nearly straight segments retain three sections, moderate
turns use five, and sharp turns use seven. This rounds close-zoom elbows without
moving protected endpoints or applying a global triangle multiplier. The shared
coastal fill contour receives six validated smoothing passes and may move at
most 0.42 map units from its clipped origin. Every candidate must still straddle
the protected land classifier, and any incident fill-face inversion restores
the original node, preserving water and island exclusions.

Runtime evidence rejected the larger v0.8.3 fill-contour movement: unsafe-node
rejections nearly doubled while accepted total displacement and the distant
coast silhouette were unchanged. Version 0.8.4 restores the 0.28-unit/four-pass
fill contour and instead covers only the visible clipped-mask seam. A stable
coastal chain keeps 1.25 units of classifier-validated ribbon on land and emits
the same captured faction colour for 0.55 units across the immediate mask edge.
The total remains 1.8 units. This adds no political fill, ownership, or new
centerline; a protected exclusion with no accepted coastal segment still emits
nothing. The outward cover half retains positive-winding validation but does
not incorrectly require a water-side sample to classify as land.

Version 0.8.5 corrects thin coastal stretches observed in v0.8.4. The larger
0.35-unit curve allowance had pushed additional samples across the exact coast,
causing 2,280 land halves to collapse before section validation. Coastal curve
deviation returns to 0.20. A stable chain now begins at its intended 1.25-unit
land width and the simultaneous section solver constrains it using the outer
edge at 25%, 50%, and 75% plus both triangle centroids. Endpoint-only classifier
noise can no longer erase a shared cross-section, while every material part of
the emitted land half remains protected. The minimum output face area now
matches the `0.0001` artifact threshold so diagnostic slivers are rejected.

Version 0.8.6 addresses the remaining apparent thinness rather than another
geometry collapse. Runtime evidence showed v0.8.5 reduced unsafe coastal
samples from 2,280 to 59 while the visible width barely changed: most of the
1.25-unit land half blended into the same-colour territory, leaving only the
0.55-unit cover visible against water. Stable coastal ribbons now retain the
same 1.8-unit total as two centered 0.9-unit halves. The land half remains
section-validated and the water-side half remains winding-validated. No fill,
ownership, source centerline, or exclusion topology changes.

Version 0.8.7 addresses the actual persistent missing-half geometry exposed by
the centered-width test. Each ribbon section is a four-corner polygon. Earlier
versions always split it across corners one and three; at a concave turn that
diagonal can lie outside the polygon, falsely classifying one face as inverted
and shrinking or suppressing the section. The tessellator now tests both legal
diagonals and emits the alternate split only when it produces two positively
wound faces. Shared corners, width, terrain drape, land validation, and source
topology remain unchanged. Cold-build diagnostics report the number of sections
recovered through the alternate diagonal.

Version 0.8.8 adds bounded, location-bearing diagnostics after runtime evidence
showed alternate diagonals recovered only 58 of 7,562 suppressed half-sections.
A cold build logs at most 24 width-constraint samples and 24 final-suppression
samples. Each entry includes map midpoint, side, endpoint widths, section length,
adjacent-normal alignment, validation pass, land requirement/result, source
segment and coastal status, and final failure reason. This component is
read-only, emits no per-frame messages, and is not replayed from a warm cache.

Version 0.8.9 applies the conclusion proven by those samples. Every captured
bad section was positively wound with well-aligned normals, but one complete
half failed the point classifier and was reduced below minimum width; every
final suppression was coastal and reported `width-below-minimum`. The protected
renderer and IslandExclusion have already decided which coastal segments exist,
so accepted coast ribbons now begin centered at 0.9 units per side and are
constrained only by positive-winding safety. The classifier remains available
for read-only side diagnostics and separate territory-fill clipping. Political
fill, inland ribbons, source centerlines, ownership, and exclusion topology are
unchanged, and the bounded v0.8.8 failure diagnostics remain active.

Version 0.9.0 brings offset coastal sections closer to the same protected fill
boundary without changing their width. After applying the boundary's shared
smoothing displacement, each accepted coastal node moves at most 0.32 map units
toward the nearest point on that contour. Shared source nodes reuse one result,
and a segment that would collapse retains its captured position. The cold-build
log reports corrected-node count and maximum/total correction. The campaign
scene water level is also captured once at load; only coastal ribbon vertices
whose terrain drape would remain submerged are raised to 0.35 units above that
surface. The native lookup is not repeated per vertex, and failure retains the
terrain-only drape. Political fill, inland ribbons, width, ownership, and
IslandExclusion remain unchanged.

Version 0.9.1 corrects the water reference used by v0.9.0. Runtime diagnostics
showed Bannerlord's scene-wide water value was `-100.000` and therefore lifted
zero coastal vertices despite the visible problem. Cold tessellation now asks
the campaign scene for the water surface at each distinct coastal vertex using
`GetWaterLevelAtPosition`, caches the result by quantized vertex, and reuses it
for shared faces. The cold-build ribbon summary reports native query count,
cache hits, and lifted vertices. The lookup remains main-thread-only and
fail-safe; one native failure disables the floor for that build without
affecting protected rendering.

Version 0.9.2 follows the runtime proof and the locally supplied Artem's Better
UI Visuals v1.0.4 implementation. Position-specific water lookup executed
35,234 native queries with 57,910 cache hits but lifted zero vertices, proving
the reported appearance was not geometric submersion. Artem publishes its
border ribbons with Bannerlord's `vertex_color_blend_mat` and parent-excluded
visibility. Cached frontier entities now use those two rendering choices while
fill entities deliberately retain `vertex_color_mat` and their existing
visibility. The ineffective water-query path is removed. Render order, height,
geometry, width, protected topology, IslandExclusion, and UI remain unchanged.

Version 0.9.3 rejects the v0.9.2 material experiment after live testing exposed
triangle seams without changing the reported coastal appearance. Frontier
replay returns to opaque `vertex_color_mat` and the approved parent-visibility
setting. Placement diagnostics also showed 1,895 corrected nodes averaging
0.294 units against the former 0.32-unit cap, so the correction was saturated.
The nearest protected fill-boundary search now uses the existing 3.0-unit coast
neighborhood and permits at most 1.25 units of movement. Shared-node reuse,
collapsed-segment fallback, positive-winding validation, source segment
existence, fill clipping, and IslandExclusion remain enforced.

Version 0.9.4 addresses the remaining visual shoreline overlap directly. The
v0.9.1 water-height experiment reported zero submerged vertices, so campaign
water height is not used. For each confidently classified coastal chain, the
tessellator samples terrain at the adjacent landward half-width and clamps the
complete cross-section to at least that land terrain elevation plus the normal
five-unit frontier height. This raises only the coastal vertices that would
otherwise follow lower seabed terrain while leaving the protected centerline
and full ribbon width in place. Samples whose land classification or prepared
height is unavailable retain the existing height and are counted separately.
Inland ribbons, political fill, materials, render order, colors, UI, and
IslandExclusion are unchanged.

Version 0.9.5 closes the remaining diagnosed land-height gaps. The v0.9.4 cold
build lifted 67,745 coastal vertices but rejected 349 samples and left five
coastal chains ambiguous. Height resolution now runs per cross-section, tries
both sides only for an ambiguous chain, and uses a bounded 0.45, 0.9, 1.35, and
1.8-unit landward search. Every accepted probe still must pass the protected
land classifier. Expanded-search successes and final rejections are logged;
the centerline, width, fill, and IslandExclusion contracts remain unchanged.

Version 0.9.6 addresses the intermittent coastal cutoffs shown near Danustica.
The v0.9.5 diagnostics recorded 914 suppressed sections clustered around
corrected segments only 0.007 to 0.012 units long. Before tessellation, proposed
coast corrections are now validated against each captured source segment. A
correction that would reduce a segment below 0.15 units or half its original
length, whichever is smaller, is rejected at both shared endpoints. Validation
iterates because a reverted shared node can affect its neighboring segment.
Rejected-node totals remain in the bounded diagnostic summary. The 1.25-unit
coast search, land-height clamp, colors, fill, and IslandExclusion remain intact.

Version 0.9.7 separates remaining visual depth gaps from true mesh cutoffs. The
v0.9.6 cold build emitted every ribbon section with zero suppressions, but 121
coastal samples still lacked a validated land height. Runs of at most twelve
samples are now interpolated only when their validated neighbors are within
four map units; an equally bounded run at a chain endpoint extends its nearest
validated height. Larger or spatially uncertain gaps remain unchanged. Filled
and unresolved counts are logged independently. No XY geometry, width, fill,
material, UI, or IslandExclusion behavior changes.

Version 0.9.8 covers the remaining open-chain seams with eight-segment rounded
caps built only from upward-wound triangles. Each half retains its original
political color and uses the endpoint's stabilized width and coastal land-height
clamp. Coastal chains are widened from 1.8 to 2.2 map units while inland chains
remain 1.8. The coastal elevation probe now evaluates its complete bounded
landward range and selects the highest validated terrain sample rather than the
first valid shore sample. This prevents a locally low shore probe from pulling
part of a thick coastal cross-section beneath nearby land or water rendering.
No back-facing caps, XY centerline moves, fill changes, or UI changes are added.

Version 0.9.9 applies coastal calibration per generated cross-section instead
of inheriting it from the first segment of a chain. Mixed inland/coastal chains
now mark every sample originating from a coastal segment, including the shared
transition node. Only those samples receive 2.2-unit width, adjacent-land
height selection, and bounded gap interpolation; inland samples remain at the
approved 1.8-unit width and ordinary terrain drape. The number of coastal
samples is logged so live coverage can be compared with captured coastal
segments. Rounded caps, topology validation, fill, UI, and IslandExclusion are
unchanged.

Version 0.10.0 renders validated ocean and lake frontier sections at the
tessellation boundary. Coastal sections use the 2.2-unit ribbon and highest
validated adjacent-land height described above; open coastal chains receive
the same upward-only rounded endpoint caps as inland chains. Diagnostics count
emitted coastal sections so a build that accidentally drops shorelines cannot
pass the source/runtime contract. Political territory fill, IslandExclusion,
inland border geometry, native faction colours and faction labels remain
unchanged.

Version 0.10.1 adds a 0.35-map-unit visibility clearance above the highest
validated adjacent-land elevation for coastal ribbon samples. Runtime v0.10.0
resolved every coastal height gap and lifted 99,219 vertices, so the remaining
underwater appearance is treated as shoreline water-render overlap rather than
missing terrain data. The clearance changes only coastal Z placement: captured
XY centerlines, ribbon width, inland borders, political fill, native faction
colours and labels, and IslandExclusion remain unchanged. The cold-build summary
records the active clearance for calibration. Live testing rejected a two-unit
clearance because it visibly detached the ribbon from the terrain.

Version 0.10.2 removes that added visibility clearance and corrects the actual
XY placement error. Accepted coastal nodes now project completely onto the
nearest authoritative smoothed fill boundary within the existing three-unit
protected search radius; the former 1.25-unit movement cap could leave source
nodes offshore. Projection still reuses one result for shared nodes and rejects
any correction that would collapse a captured segment. Coastal Hermite
interpolation is limited to 0.05 map units from the projected segment rather
than 0.20, preventing smoothing between projected nodes from visibly bowing
away from the fill edge. Inland centerlines, coastal width, terrain-relative
frontier height, political fill, native faction colours and labels, and
IslandExclusion remain unchanged.

Version 0.10.3 reduces only the projected ocean and lake ribbon width from 2.2
to 1.6 map units by preserving the former 1.1-unit land-facing half and reducing
only the classified water-facing outer half to 0.5. Ambiguous coastal chains
use a safe centered 0.8/0.8 fallback. Rounded endpoint caps interpolate between
the two side widths so they cannot restore the removed outer bulge. Inland
ownership borders remain at 1.8 units. Full coastline projection, the 0.05-unit
coastal curve limit, terrain-relative height, native faction presentation, and
IslandExclusion are unchanged.

Version 0.10.4 changes frontier compositing without moving its geometry. Replay
uses a sidecar-owned copy of opaque `vertex_color_mat` with depth testing and
depth writes disabled, forward rendering enabled, and the latest supported
material/mesh render order. Consequently the ribbon covers political fill,
terrain, scene objects, and water instead of being occluded by them, while it
does not write false depth that could hide later UI or scene content. Fill keeps
its existing material and render order. Failure to acquire or copy the native
material aborts sidecar replay and leaves the protected renderer's normal
fail-open rebuild path active.

Runtime testing rejected a proposed alpha-sorted `ModulateNoWrite` pass because
the complete frontier disappeared. Captured faction vertex colours cannot be
assumed to supply alpha compatible with that blend mode. The experiment was
rolled back to the visible opaque no-depth overlay; it must not be reintroduced
without first normalizing and validating vertex alpha in an isolated build.


## Verification contract

When exactly one reviewed layout is present in `AOC CORE/AuthoredBorders`, cached
generated-geometry replay is bypassed. The optimizer keeps its bounded terrain
sampling and eager native build, while the published-border sidecar applies the
reviewed layout before the first map frame. Replaying a generated cache in that
case would replace the published rows, so this is an intentional correctness
guard rather than a cache failure.

The eager native renderer phase is bounded to 30 seconds. `OnGameLoadFinished`
only records a pending published build because its scene is not yet renderable.
The hash-pinned `MapScreen.HandleIfBlockerStatesDisabled` prefix runs that build
after `ReadyToRender` and `CheckSceneReadyToRender` succeed, but before native
MapScreen code releases its first ready frame. The published sidecar then runs
its separate borders-and-fill publication phase inside the final
`RebuildBorders` callback, with its own budget. These are sequential phases;
the optimizer's 30-second bound is not a total startup deadline, and a native
operation that already began may run past a soft timing check before control
returns. Diagnostics require the published sidecar's final readiness signal
before reporting published-layout readiness.
This remains a separate pre-map-frame publication phase, and the native eager
phase remains bounded to 30 seconds.

Published layouts also use a scene-scoped exact-probe memo for the protected
height and terrain calls used while their native rows are generated. A cache
entry is keyed by the exact floating-point XY pair and preserves both the native
return value and its `out` value, including failed terrain probes. A nearby point
never shares an entry, and reset or scene clear discards every entry. This avoids
the prepared-grid interpolation changing the row topology to which a published
layout is bound. The memo is capped at 262,144 entries; after that it falls
through to the approved native probe rather than evicting or approximating.
Non-published builds retain the bounded prepared-grid lookup.

The source contract check is `Tests/Verify-PoliticalBorderOptimizer.ps1`.
The loading-screen dismissal contract is documented in
[Published border loading](PUBLISHED_BORDER_LOADING.md). Session/save callbacks
queue preparation; a narrowly verified transpiler guards only the native loading
dismissal call until the same campaign has verified borders and fill. Native map
updates continue. OnFinalize cancels the map-owned hold. Failures/timeouts release
native loading with an explicit warning, rather than deadlocking or claiming
published readiness. Source/IL, executable patched-loop and actual Harmony
composition checks cover this contract. Live visual acceptance is still required;
this is not a performance improvement.
Runtime acceptance requires the approved renderer's existing completion log and
the optimizer summary from the same campaign load. The optimizer is successful
only if visual coast/border coverage and nonzero terrain-relief refinement
remain approved while true native fallbacks and maximum builder advance time
drop materially without native access violations. For published layouts, record
the exact-height and native-terrain memo hit/miss totals and capacity fallbacks;
prepared-grid counts are intentionally zero in that mode.
