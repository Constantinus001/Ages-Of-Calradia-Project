# Exact published layout binding - 2026-09-22

The 13:04 live capture proved that the original reviewed F1 layout passes
DraftStore.Load against the exact native graph. Actual Attach failed only
because generic DraftPath requested EB567.xml. The optimizer log confirms its
pre-map loading scope was active and then failed PublishedReady on that rejection.

PublishedBorderLayout.LoadReviewedBinding now returns a validated snapshot,
asset path, separate source/asset identities, and matching repair plan.
It requires renderer 560F..., native identity EB567..., exact graph 8D4CB...,
authored F1 bytes 646F..., repair bytes CBF2..., and successful real validation.
F1 coordinates, XML root, file name, and repair metadata are unchanged.
No generic fallback or equivalence between historical hash algorithms is assumed.

Architectural boundary: sidecar asset resolution and repair selection.
Failure: no restore or repair after any failed binding predicate.
Native publication lifecycle and MapScreen observer remain unchanged.
Protected Core/calendar artifacts are unchanged.

Validation: Release sidecar build; 155 graph assertions including the lossless
live capture; 164 fake-native preview transaction assertions; actual native
Harmony binding and optimizer composition; protected baseline.
Negative replay tests alter renderer, source identity, a single native float
bit, edge endpoint, row, colour, authored bytes, and repair bytes independently.
They reject without changing the graph. Positive replay preserves all authored
coordinates and retains the reviewed 371-bridge/25-row/42-region binding.

In-game publication/readiness and first-frame visual acceptance remain pending
a restart with this candidate. No runtime visual success is claimed.