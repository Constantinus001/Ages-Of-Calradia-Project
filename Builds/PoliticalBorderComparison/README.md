# Political border comparison trial

This diagnostic sidecar preserves the user-accepted political fill assembly at
SHA-256 0918C5DBB2AD59BFAB9F768EE781D9F86A6DDDDE342D6D76A2BFA52EB88F063E
and protected Core at 560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E.
It does not rebuild either assembly or change island classifiers.

## Two modes

A (default) derives ribbons from the committed corrected fill. Outer edges get
an inward 1.6-unit band; different-color shared edges get 0.8 on each side.
Corner disks clipped to incident source triangles join the bands. Every resulting
triangle remains inside an accepted fill triangle, within 0.00015-world-unit
floating-point containment tolerance. No band bridges an excluded hole. Heights
interpolate the working fill plus 0.15, and the legacy downward zoom translation
is cancelled for trial entities. Border brightness is reconstructed at 85 percent
from the fill's 50-percent RGB; integer rounding can differ slightly from Core.

B uses the same geometry and a private copy of the material with NoDepthTest and
NoModifyDepthBuffer. This is an unrestricted depth-override diagnostic, NOT a
water-only compositor. It may draw through terrain, buildings or ships. Actual
visibility through the water pass still requires runtime confirmation. No verified
public water-only depth switch was found. Neither shared material nor working fill
material is edited. B is not suitable as an accepted release fix without evaluating
those limitations. Mode file is polled once per second after successful commit.

## Native integration and failure boundaries

Two hash-pinned Harmony postfixes: NativeMeshTransaction.Commit in the accepted
fill sidecar observes successful publication, and Core ApplyFrontierZoomPresentation
restores identity transforms only on this trial's border entities. Competing owners
on those targets cause initialization refusal. No fill-emission, classification,
water shader, save, physics or global engine method is patched.

The worker reads the captured immutable managed faces. Native mesh/material/entity
operations run on the first OnApplicationTick thread. Candidate meshes are hidden
until the original border builder has finished and all new faces have been counted.
Uploads are bounded to 512 logical triangles per tick; this is a work-count cap,
not a hard native-time guarantee. Commit hides old entities, updates the existing
Core frontier list and removes old borders. Failures before publication clean up
candidates and retain originals. A native failure while rollback visibility is
being restored can still leave an incomplete rollback; logs are authoritative.
The trial does not claim atomic engine transactions or runtime visual acceptance.

The implementation uses exact shared XY endpoints and vertex colors as region
keys. The current captured map has matching edges, no open endpoints and no
nonmanifold edges. Same-color kingdoms cannot be distinguished by this interface;
a release design should carry faction IDs rather than infer identity from color.
Nonmanifold input is rejected. No new territory classifier is introduced.

## Checks and operation

Release build passed. Eleven behavioral checks include the full 118,254-triangle
user-accepted capture, producing 70,876 band triangles, every vertex inside its
accepted parent (within float tolerance). Synthetic checks cover corner joins,
no same-color internal diagonal, different-color frontiers, separated regions,
and nonmanifold rejection. Two actual Harmony targets bind against pinned binaries.
No live visual or native upload/commit success is implied by those checks.

BoundaryTests formerly raised an unhandled exception because its containment check
used a fixed cross-product tolerance. The offending sample was approximately
0.000043 units outside an edge after float rounding. The check now measures
world distance, and the runner reports failures with exit code 1 instead of a
Windows crash dialog. Both the full capture and controlled-error paths passed.

Use Tests/PoliticalBorderComparison/Set-BorderComparisonTrial.ps1 to install or
remove registration with the game closed. It preserves unrelated manifest entries
and uses backups. Set-BorderComparisonMode.ps1 -Mode A or B changes the live mode.
Watch bin/Win64_Shipping_Client/Logs/PoliticalBorderComparison.log for COMMITTED A.
The game also displays a message on commit and each mode change. Default is A.
