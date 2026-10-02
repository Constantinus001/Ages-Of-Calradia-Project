# Procurement Core integration and committed accounting

Source and private candidate packaging change; not yet deployed or live accepted.

Affected boundaries: optional diagnostics, backward-compatible persistence,
Core submodule registration and release packaging. No native Harmony target is
added and no economic rate or protected renderer/prefab is changed.

Core registers Campaign Systems followed by Workshop Procurement. Procurement
keeps its DLL identity, namespace, behavior type and aoc_workshop_procurement_v1
save key. A process-wide owner rejects duplicate submodule registration; repeated
game-start delivery to that owner does not add a second behavior. Duplicate
instances cannot unpatch or reset the active owner's state on unload.

New orders persist an optional GUID. Old ledgers remain readable; legacy order
identity derives from immutable workshop/source/departure/recipe fields without
rewriting the original payload. Invalid or duplicate persisted GUIDs are rejected.

Optional MovementObserved receipts describe only committed dispatch, arrival,
consumption, return and liquidation transitions. Each row has a unique receipt,
stable order identity, item/category, market and cargo endpoints, and campaign/
assembly provenance. Arrival moves no market stock. Consumption is a private-cargo
sink, not an additional market withdrawal. Failed/rolled-back transfers do not
publish committed movements. Subscriber failures cannot undo gameplay commits.

Broad stock reconciliation includes market deltas from these receipts once,
rejecting duplicate IDs, malformed balances and nonconserving transfer stages.
This does not retrofit missing receipts into older captures or automatically
certify total cargo lifecycle conservation from a partial observation window.

Verification: native dispatch, funded returns/liquidation, arrival and production
fixtures exercise the optional event and endpoint conservation. Python fixtures
cover all stages, duplicate rejection and unbalanced receipts retaining residuals.
Release builds and broader protected/native regression checks remain required.

Deployment must refuse an enabled old candidate launcher entry; don't remove
user saves or reset ordering preferences. One copied-save migration test must
establish pending-order continuity, followed by delivery without double charge.
Optional Logistics and active finite quest coverage remain live acceptance items.
The public additive installer remains a separate unfinished release requirement.
