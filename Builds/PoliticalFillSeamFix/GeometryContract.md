# Conforming political fill geometry

The approved renderer's independent cell refinement emits coarse edges whose
linear height differs from their neighboring fine edges. The September 9 capture
contains 83,058 logical fill triangles and 28,185 mismatched coarse/fine overlap
pairs. This correction addresses that measured geometry defect only.

`FillSeamConformer.TryBuild` is engine-free and has no persistent state. The
caller supplies an immutable snapshot of logical triangles, each with original
identity, row, color, XYZ and UV. Capturing the actual native submissions avoids
recomputing ownership, color, island exclusions or water classification.

The solver indexes axis-oriented edges and examines positive-length overlaps
whose triangle interiors are on opposite sides. Only existing fine-edge vertices
can subdivide their neighboring coarse edge. Point contacts, disjoint spans and
same-side overlaps do not establish adjacency. Coordinates are never snapped.
The 0.0001 candidate tolerance permits nearly axis-aligned collinear edges;
parallel-offset candidates requiring a footprint change fail safely.

An affected triangle becomes a fan from its original plane's barycenter to its
ordered boundary vertices. Each output face retains its parent's color and row.
New UVs come from that parent's affine UV field, preserving UV seams between
owners. Fine boundary XYZ and original corner XYZ/UV are retained. Unaffected
triangles replay unchanged. Both source winding directions are supported.
The native adapter remains responsible for preserving the original reverse-face
policy, materials, entity presentation and main-thread uploads.

Before returning success the solver verifies positive original winding,
per-parent XY area conservation and every indexed output shared-edge height.
Conflicting explicit heights, degenerate/nonfinite inputs, duplicate IDs,
ambiguous XY relationships, excessive subdivisions or remaining edge mismatches
return failure without exposing partial geometry. Limits are 262,144 source
triangles, 1,048,576 output triangles, 48 inserted vertices per parent and eight
million candidate-pair comparisons. Unexpected runtime exceptions are left to
the native adapter's fail-safe boundary; the input is never modified.

The complete captured map produces 118,254 logical output triangles: 9,902
parents change, with 15,392 inserted boundary points. An independent existing
capture analyzer finds 89,165 shared overlap pairs, zero coarse/fine pairs and
zero height mismatch after correction. Original parent coverage, corner data,
colors, row assignment and UV interpolation are checked across the complete
output using synthetic metadata because the diagnostic CSV contains XYZ only.

This is an offline geometry result, not an in-game visual result. It does not
prove all terrain/props are below every fill face, alter nighttime lighting,
fix coastal ribbon submersion, or close deliberately excluded water regions.
CPU solver timing excludes native mesh construction and upload cost. The native
adapter must retain the original entities unless the complete replacement is
validated and safely ready to publish.

## Surface projection precedes this contract

For the September 9 campaign-surface trial, the input Z values are obtained by
FillSurfaceProjection using the campaign surface plus three units. Original XY,
UV, row and color values remain exact. The conformer's corner/height preservation
contract applies to this projected input; it no longer means terrain-only Core
Z values are retained. Excluded XY remains absent through both stages. Native
projection failure leaves the original scene meshes active.
