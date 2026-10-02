# Vostrum coastline geometry proof

This is an offline geometry check, not an installed border fix. Run `prepare.py`
with Python 3 from any directory. It reads the successful 20260909-162030-656
capture and writes a JSON plan and top-down SVG under
`output/diagnostics/vostrum-coast-proof/`.

## Boundary and evidence

The scope is 36 accepted land/CoastalSea segments within 35 map units of Naval
DLC Vostrum (683.695, 291.421). Original source pairs remain in the plan for
provenance. No game files, political fill, inland borders, or exclusion rules
are modified. This is specific to the captured map, not a general coastline
algorithm or permission to replace rejected segments.

Five endpoint pairs differ by at most 0.000039 map units due to serialized
coordinate precision. Canonicalization is bounded to 0.0001 with a cluster
diameter check. Those differences are too small to explain visible gaps.
Larger separations, duplicate edges, branches and disconnected components fail.

A trial Chaikin smoothing pass at width 1.6 produced an inverted ribbon cell
near x=713. It was rejected. The current plan retains the source centerline,
computes shared miter joins before subdivision, then subdivides both sides to
at most one map unit per edge. It checks triangle orientation, nondegeneracy,
connectedness and proper boundary intersections. It has 108 cross sections,
with maximum miter ratio about 1.413. This demonstrates a connected XY ribbon;
it does not validate its height, visual smoothness or the native renderer.

## Remaining runtime requirements

- Revalidate source ownership, terrain classification and original support
  checks against the live generation; never substitute a stale captured plan.
- Sample the ribbon's own surface heights on the application thread, including
  its interior. Do not inherit political-fill height or apply an arbitrary lift.
- Retain ordinary terrain, settlement and ship occlusion while proving water
  visibility. Disabling all depth tests does not meet that contract.
- Stage a replacement with rollback before suppressing any original segment.
- Verify Vostrum visually at close/far zoom and day/night before wider use.

No water-only depth/compositing integration has been verified. This artifact
must not be presented as an installed or visually successful coastline fix.
