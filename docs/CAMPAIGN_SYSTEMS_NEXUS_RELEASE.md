# Campaign Systems public-release contract

Status: preparation, not authorized for public upload.

## Frozen first-release scope

Core companion: campaign clock access, saved quest-deadline observation,
validated procurement policy, conserved transfers and optional read-only
Logistics reserve integration. Existing calendar and quest scaling keep their
existing owners. No universal third-party authority, physical workshop freight,
noble-wealth rebalance, army-growth curve or opening-war suppression is claimed.

Protected Core renderer and WorldCalendar prefab must never be rebuilt or replaced.

## Release tooling

`Tests/Verify-Release.ps1 -CampaignSystemsCandidateOnly` creates an isolated,
committed source snapshot and runs the offline candidate build, regressions,
package verification and mandatory Defender scan/hold. It rejects deployment,
security/baseline bypasses, mixed release modes and holds shorter than ten minutes.
This route does not bypass or certify the full-Core public-release gate.

## Public distribution blockers

1. Replace the installed-manifest-dependent private delta with a reproducible
   distribution against a specified published AOC baseline. Do not distribute
   this machine's private manifest or assume its extra border assemblies exist.
2. Separate player, optional Logistics and opt-in diagnostics archives; test each
   supported combination and missing-dependency failure. Diagnostics must not
   become a mandatory gameplay dependency.
3. Bind public archives, source revision, supported Bannerlord version, dependency
   versions and file allowlists to a release receipt. Scan the final archives,
   not merely an earlier private candidate.
4. Include installation, configuration, upgrade and rollback instructions.
   Saved procurement obligations prohibit promising safe mid-campaign removal;
   retain pre-upgrade saves and do not erase ledgers or silently refund orders.
5. Complete exact-package startup, existing-save load, save/reload, pending-order
   continuity and optional-module acceptance. Measure overhead in the game.
   Existing soak evidence predating this build is not acceptance of this build.

Use one combined acceptance session for planned runtime questions. Missing rare
events remain NOT_EXERCISED, not an automatic request for another long soak.

No release rating substitutes for these receipts. An offline candidate pass is
not evidence that Nexus players can install it safely on a fresh machine.
