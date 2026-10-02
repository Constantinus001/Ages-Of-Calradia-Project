# Prototype verification

Status: v0.1.1 captured 5056 native border segments in 391 rows and the user confirmed editing opens/works.
v0.1.2 corrects camera left-drag capture and adds direct connection-point selection. Its live interaction acceptance is pending.

Live diagnosis on a separate copy of borderredrag test:
- Installed v0.1.1 initializes successfully with all current AOC sidecars.
- Direct Mesh.AddTriangle observation missed compiled/inlined uploads; exact helper call-site capture replaces it.
- Cap identity then exposed double-versus-float quantization drift. The revised key uses the protected renderer's
  float multiplication and passes 4096 comparisons against its actual key constructor.
- Successful capture was recorded after that correction; the user then reported camera movement during border dragging.
- The old TickNavigationInput guard controlled menu navigation, not camera movement. v0.1.2 masks native left-drag fields at MapCameraView.OnBeforeTick and preserves other camera controls.
- Connect mode exposes all open endpoints; choose two points directly instead of requiring two Shift-selected sections. Selection markers use corrected coast height.

Implemented:
- Capture accepted border segments and the exact original ribbon/cap faces from the protected renderer.
- Drag visible sections, preserving stored side colours and using the protected native quad/cap helpers.
- Shift-select and delete sections; connect compatible open ends without moving existing endpoints.
- Edit connected borders; Undo/Redo and per-gesture/session cancellation.
- Campaign- and generation-scoped draft persistence, including deleted sections and connectors.
- Separate Gauntlet toolbar and transient yellow selection outlines.
- Incremental preparation, complete-batch publication, retained originals and independent cleanup retries.

Executed checks:
- Release sidecar compilation with warnings treated as errors: passed.
- 52 pure geometry/edit-history/draft assertions: passed, including explicit endpoint connections between separate pieces.
- 36 fake-scene adapter assertions: passed, including native terrain refusal, publication failure, coastal markers, connection point colors and failed entity removal.
- Actual Harmony binding against installed assemblies in Windows PowerShell/.NET Framework: passed.
- Installed fill/coast/diagnostics patch composition, exact 2/2/4 upload call sites and unknown-owner refusal: passed.
- 4096 endpoint keys around fractional rounding boundaries match the protected renderer constructor: passed.
- Actual native camera-input struct: editor mode removes only four left-drag fields; inactive mode leaves input unchanged; Harmony by-reference mutation arrives before consumer execution: passed.
- Protected DLL native width, cap-step, render-order and height constants: matched.
- Prefab/manifest parse and command wiring: passed.
- Repository and installed protected renderer/prefab baseline: passed; legacy editor and optimizer remain disabled.
- Three-agent council implementation review: reported issues corrected; renderer and interaction rechecks found no remaining critical issue in reviewed paths.

Not established by those checks:
- Real mouse hit behaviour, Gauntlet layout/focus, visual parity, flicker-free native publication or smooth drag latency.
- In-game save/reload, ownership-regeneration and campaign/menu teardown acceptance.
- Compatibility with additional patches on the guarded renderer/input targets.

The README contains the complete live-test matrix. The sidecar is installed in the game's Modules directory
under AgesOfCalradiaBorderEditPrototype. Protected binaries/prefabs and other module registrations remain unchanged.
