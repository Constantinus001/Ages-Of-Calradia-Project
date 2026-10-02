# Published border recovery - 2026-09-22

Status: confirmed asset damage recovered and independent fill-after-rejection bug fixed.
The remaining native/published graph mismatch is NOT resolved or visually verified.

## Source of truth
The published F1B layout plus reviewed connection is SHA256
646FEEE68ED46DE6434E4E55157A5C1C370203F25651E1C6956497CE94052507.
Its repair is CBF2C8DF9E94EA73C3E858E223DCD8DAC15B5DB02A9288BCDB252F94955E17DD.
All 5491 saved coordinates are authored state, including the first 5132 nodes.
The discarded EB rebase replaced those first 5132 coordinates with rounded keys.
Restored source and repair byte-for-byte from the pre-rebase backup.
The rebase script now fails before writing. Deployment verification pins F1B again.

## Integration boundary and failure contract
Only the BorderEditPrototype sidecar changes; protected Core/calendar remain immutable.
Repair requires successful graph restore, exact draft hash and topology.
Normal repair ticks additionally wait until border preview is caught up.
MapScreen remains an observer; no loading gate or new native patch is added.

Canonical lookup keys remain unchanged. A separate exact-xy-ieee754-v1 diagnostic
records ordered float bits and ordered edge endpoints/rows/colors, excluding mesh Z.
The renderer hash and schema token participate in the capture hash.
At ReplacePoliticalFrontierEntities, a read-only hash-pinned F1B geometry replay
runs the real draft validator against the exact captured native graph. It never
restores the replay result and never authorizes topology aliases/publication.
Crossing failures now identify both edge and endpoint IDs.
Comparing saved coordinates with native nodes cannot prove topology drift:
saved original-node positions may be user edits. Rehashing them cannot prove
that the historical identity algorithm differed either.

## Verification
Release sidecar build, 142 graph assertions, 164 fake-native preview/transaction
assertions, actual Harmony/native binding and optimizer composition passed.
Protected baseline and restored repair binding passed.
The preview test project was missing the existing TopologyFingerprint source;
its explicit Compile reference was repaired to run those tests.

## Remaining evidence
A fresh in-game native capture is required. The previous captures round XY to
0.001 and cannot faithfully reproduce validator behavior. Do not enable a legacy
alias or modify coordinates based on those lossy files. Even a successful
geometry replay alone does not prove historical edge identity compatibility.
No claim is made that the restored F1B borders currently render in-game.
Installed recovery/diagnostic sidecar SHA256: 0DAEFBA6E91388A5F75246A48E3DAE5AE60E7D22CC6D783B24C20943C27E0FD3

Follow-up: the 13:04 exact live replay passed. The remaining filename rejection is addressed by the explicit hash-pinned source/asset binding documented in PUBLISHED_BINDING_20260922.md. Earlier pending-capture status above is historical; first-frame live verification is still pending.
