# Coast surface correction

Separate, reversible sidecar for the approved 560F1B political renderer and
the locally verified Bannerlord API. No Core or accepted fill rebuild.

## Revision 3: cap overlap candidate

The approved cap fans extend back across incident ribbon strips. Revision 3
clips those covered cap portions in XY and emits only the exposed rounding.
At a straight two-segment join the redundant disk disappears; at a bend its
outer arc remains; a true end keeps its exposed semicircle. Existing strips
are not removed or raised. Point Z is interpolated within the original cap
triangle and color, double-sided winding, material, depth and zoom are retained.

The generation plan now records every supported incident segment, including
inland segments meeting a coastal node. The clipping half-plane proof applies
inside the radius-0.8 cap because approved incident spans exceed that radius;
the captured full-map test checks this length precondition. Short unsupported
directions do not participate. Only nodes already affected by the coast plan
are eligible; other borders and all political fill are unchanged.

Exact additional Harmony target:
`Builder.AddDoubleSidedFanTriangle(Mesh, Vec3, Vec3, Vec3, uint, UIntPtr)` prefix.
Preparation failure retains the original cap. Native upload failure is logged
and propagated, without appending duplicate fallback triangles after a partial
upload. Existing builder finalization still computes normals and bounds.

Tests sample cap/strip union coverage and cap/strip overlap for ends, straight
joins, several turns and a multi-direction junction. Combined with continuity
and whole-map fixtures, 77,573 checks pass. Six exact target bindings coexist
with accepted fill and diagnostics. This is a candidate for the reported
flashing, not a visually verified cure. Strip/strip overlaps at bends are not
removed by this change; further flashing must not be claimed resolved without
runtime evidence. Runtime logs report input/output cap triangle counts.

## Revision 2: whole-map continuity

The user accepted revision 1's coastline height, but reported disconnected
ribbons throughout the map. Capture 20260909-171339-283 shows 79 shared nodes
with height disagreement: 36 sea/unknown and 43 inland/sea junctions, maximum
1.521262 map units. The recording also shows abrupt ends near Garontor/Quyaz;
rejected candidate 1351 lies on that peninsula. These are separate failures.

Revision 2 scans the current builder's entire frontier grid before drawing its
first frontier row. It uses the approved region objects, equality, crossing
midpoints and saddle decider. It records coast spans and endpoint footprints
without drawing anything. The scan advances one row per original budgeted
builder iteration. The plan belongs to one builder via a weak key and cannot
be reused across ownership rebuilds. This adds a planning pass to border load.

Every incident segment uses the coast surface rule within 0.8002 map units of
a planned coast endpoint, including a short transition on an inland or unknown
segment. The remainder of that segment retains its original height. This is
an intentional expansion from revision 1's segment-only selection, necessary
to avoid cracks at the selection boundary. Near-identical query coordinates
within 0.00015 share a height; their geometry XY is never changed.

A confirmed-sea candidate rejected by the original wide land probes can be
restored only when all five stations have included political land either at
the original probes or within the ribbon footprint (center, +/-0.4, +/-0.8).
This uses the protected GetFrontierRegion.Land predicate, preserving island
exclusions. No arbitrary endpoint bridge or new cross-channel path is created.
The original support result is changed only for a planned rescue after all
required heights preflight successfully. Unknown/both-nonland candidates are
not rescued. Planning failure retains the revision 1 behavior and logs it.

Additional exact Harmony targets:

- `Builder.BuildNextFrontierRow(Scene)` prefix defers frontier drawing while
  the generation's complete join plan is prepared. It does not advance the
  original row counter, touch fill rows or allocate native meshes.
- `Builder.HasFrontierLandSupport(Vec2, Vec2, Vec2)` postfix permits only a
  preflighted planned rescue. Ordinary support calls and failed preflights
  preserve the original result. Patch priority precedes the read-only observer.

`CoastContinuityTests.csproj` covers straight/saddle/multi-owner topology,
completed-scan behavior, thin-land rescue, excluded islands, channel rejection,
join-footprint limits, precision merging and a reversed-order whole-map replay
of 5,746 incident endpoints (11,509 checks). The binding check now covers five
targets coexisting with accepted fill and read-only diagnostics. These tests
do not replace the native replay and visual verification.

## Evidence and scope

The successful 20260909-162030-656 capture contains 33,960 sampled vertices on
2,830 accepted, confirmed CoastalSea boundaries. Of these samples, 19,167 have
negative close-zoom clearance relative to the campaign scene query. Counts
include repeated quad vertices; they are not unique vertices or visible gaps.

The approved renderer samples terrain plus 5, then moves the border entity
down by up to 4.65. Its terrain query is not the campaign surface query used
by the accepted political-fill correction. This sidecar changes only Z of
eligible coastal points to campaign surface plus 5. Where the point itself
has native CoastalSea classification, its floor is the greater of campaign
surface and Scene.GetWaterLevelAtPosition(point, true, true). Original zoom
presentation leaves a nominal 0.35 clearance at close zoom. It does not use
the rejected trial's political-fill Z plus 0.15 or cancel zoom lowering.

For a full coast span, both region sides must differ in political Land and the nonland side must
resolve to native CoastalSea. False Land alone cannot qualify an excluded
island. Revision 2 also corrects incident join footprints and permits the narrow
support rescues described above. Rejected original height samples remain
rejected. Every required original quad/cap point is preflighted
before activating a segment correction. Any failed or nonfinite required
query retains the entire original segment. Shared exact XY samples are cached
per builder; weak keys release old generations.

## Integration contract

Harmony owner: `aoc.coast-surface-fix.v1`.

- `CampaignPoliticalTerritoryFill.Builder.Advance(Scene)`: prefix/finalizer
  establish and restore main-thread scene context, including exceptions.
- `Builder.AddFrontierSegment(Mesh, Vec2, Vec2, UIntPtr)`: prefix/finalizer
  preflight coast samples and restore segment context. The original still
  runs with its support checks, colors, geometry and island exclusions.
- `Builder.TryGetFrontierPoint(Vec2, out Vec3)`: postfix changes Z only after
  original success and only for points present in the prepared coast context.

First application tick binds the native-call thread. Exact methods and the
Core hash must match. Unknown competing Harmony owners disable the sidecar;
known read-only diagnostics and the accepted fill owner are permitted. Install
after the accepted fill submodule, whose own startup checks reject new owners
if they load before it. No engine-wide patches, depth overrides, shaders,
settings or save changes. Revision 2 permits the original renderer to emit
only those rejected spans that pass the footprint-support rescue.

## Verification and limitations

Release build and `Tests/CoastSurfaceFix/Verify-CoastSurfaceFix.ps1` cover exact
Harmony bindings, cache construction, application-thread ownership, coast
classification, invalid heights, sea floor selection, zoom values, the actual
point postfix, and replay of all 33,960 confirmed-coast captured samples.
These are managed checks; the script does not invoke native scene methods.

Runtime evidence is still required for water waves, triangle interiors,
shoreline appearance and native query cost. A finite water height is used only
at explicitly classified CoastalSea points; unknown terrain is not treated as
water. This addresses measured sample burial, not a verified water-only GPU
compositor or missing geometry. No visual-success claim is warranted until a
fresh in-game frame supports it. This is a local correction candidate, not a
published release.

Enable/disable using `Tests/CoastSurfaceFix/Set-CoastSurfaceFix.ps1`. The script
checks closed game/launcher, protects hashes, backs up the manifest and any
existing sidecar, and verifies that only this registration changed. Installation
does not launch or load a campaign. Runtime summary/failures go to
`bin/Win64_Shipping_Client/Logs/CoastSurfaceFix.log` under AOC CORE.
