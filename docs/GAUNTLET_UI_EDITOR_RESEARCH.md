# Gauntlet UI editing in AOC Map Studio

## Decision

Add UI authoring to the native WPF Map Studio, but use Bannerlord itself for
the authoritative preview. A WPF canvas can edit and validate Gauntlet XML and
give immediate structural feedback; it cannot faithfully reproduce the
Gauntlet renderer without embedding version-sensitive native engine state.

The recommended product is therefore one application with two cooperating
preview levels:

1. **Design preview in Map Studio** for hierarchy, alignment, margins, fixed
   sizes, text, colors, sprites, brushes, visibility, drag/resize, and XML
   validation.
2. **Exact preview in Bannerlord** for real brushes, sprite categories, fonts,
   list measurement, bindings, visual states, custom widgets, input, and the
   final resolution/scale behavior.

## Evidence

Gauntlet movies are XML documents rooted at `Prefab`; most authored content is
a widget tree under `Window`. Common widget properties include alignment,
margins, suggested size, size policy, brush, sprite, text, commands, and data
source. This makes lossless XML-backed property editing practical in the WPF
application.

- [Gauntlet movie XML structure](https://docs.bannerlordmodding.com/_gauntlet/movie.html)
- [Common widgets and properties](https://docs.bannerlordmodding.com/_gauntlet/widget.html)
- [Official sprite-sheet and movie-loading workflow](https://moddocs.bannerlord.com/asset-management/generating_and_loading_ui_sprite_sheets/)

Exact rendering is a different boundary. The documented runtime creates a
`GauntletLayer`, loads a movie, and resolves UI resources through Bannerlord.
The installed v1.4.8 `Win64_Shipping_wEditor` binaries confirm that this path
owns `UIContext`, `WidgetFactory`, `BrushFactory`, `FontFactory`, sprite data,
the resource depot, prefab loading, and change detection. The directory has
font-atlas, code-generation, and sprite-sheet tools, but no supported visual
Gauntlet editor executable that Map Studio can launch or embed.

Gauntlet already supports runtime XML refresh. The community documentation
describes enabling UI debug mode and seeing prefab changes after saving, while
the installed engine contains `GetIsHotReloadEnabled`, `CheckForChanges`,
`StartWatchingChangesInDepot`, and UI debug commands. This makes a controlled
in-game preview bridge lower risk than rebuilding Gauntlet layout behavior in
WPF.

- [Live UI editing workflow](https://docs.bannerlordmodding.com/_tutorials/modding-gauntlet-without-csharp.html#how-to-enable-use-live-ui-editing)
- [Gauntlet screen/layer lifecycle](https://docs.bannerlordmodding.com/_gauntlet/screenbase)

## Proposed architecture

### Map Studio UI workspace

Keep this in `Tools/PoliticalBorderMapEditor.Native` but separate it from the
political-map model and controls.

- `GauntletPrefabDocument`: secure XML load/save, snapshots for undo, property
  validation, and protected-path policy.
- `GauntletUiEditorWindow`: hierarchy, canvas, selection, drag/resize, property
  inspector, resolution presets, and diagnostics.
- `GauntletLayoutPreview`: deterministic approximation of `Fixed`,
  `StretchToParent`, `CoverChildren`, alignment, margins, and common panels.
- `GauntletResourceCatalog`: read-only indexing of module sprite-data and brush
  XML for property pickers; it must not load TPAC/native textures itself.
- `GauntletPrefabValidator`: unknown widget/property reporting, duplicate IDs,
  missing sprites/brushes, unresolved `@` bindings, and layout contradictions.

The XML document remains the source of truth. Unknown elements and attributes
must round-trip unchanged so the editor is forward-compatible with custom and
version-specific widgets.

### Exact-preview bridge

Ship this as a diagnostics-only sidecar assembly, never in the normal player
package.

The installed Bannerlord v1.4.8 editor assemblies were inspected directly.
They expose the exact primitives needed for a targeted preview:

- `GauntletLayer.LoadMovie`, `ReleaseMovie`, `GetMovieIdentifier`,
  `OnResourceRefreshBegin`, and `OnResourceRefreshEnd`;
- `UIResourceManager.WidgetFactory`, `ResourceDepot`, `Refresh`, and
  `LoadSpriteCategory`;
- public `WidgetFactory.OnUnload(string movieName)`, which invalidates one
  cached prefab without globally reloading every active screen;
- `ResourceDepot.StartWatchingChangesInDepot`, `CheckForChanges`, and
  `OnResourceChange`;
- `UIConfig.DebugModeEnabled`, `DoNotUseGeneratedPrefabs`, and
  `GetIsHotReloadEnabled`;
- `UIContext.Root`, the runtime widget hierarchy, measured size, text, sprite,
  visibility, and child traversal; and
- `TaleWorlds.Engine.Utilities.TakeScreenshot(string)`, which can return an
  exact game-rendered frame to Map Studio.

The installed command strings are `ui.set_debug_mode [1/0]` and
`ui.use_generated_prefabs [1/0]`. The commonly documented
`ui.toggle_debug_mode` is not the command embedded in this v1.4.8 build, so the
bridge should use the API properties directly and restore their previous values
when previewing ends.

#### Preview module

Create a separate `AocUiPreviewBridge` diagnostics module with no campaign
behavior and no production packaging entry. It owns two prefab slots that exist
when the module is loaded:

- `GUI/Prefabs/AocUiPreviewSlotA.xml`
- `GUI/Prefabs/AocUiPreviewSlotB.xml`

Map Studio alternates between these slots for each revision. The bridge loads
the candidate slot before releasing the previous movie. If Gauntlet rejects the
new XML, the last valid UI remains visible and the failure is reported instead
of leaving a black or empty screen.

Targeted reload sequence on Bannerlord's main thread:

1. Read a fully written command manifest and reject stale revisions.
2. Validate the candidate file path and SHA-256 under the preview module.
3. Call `UIResourceManager.WidgetFactory.OnUnload(candidateMovieName)`.
4. Call `previewLayer.LoadMovie(candidateMovieName, previewViewModel)`.
5. Wait for two completed frames, then release the old movie.
6. Inspect `identifier.Movie.RootWidget` and emit measured widget diagnostics.
7. Call `Utilities.TakeScreenshot(exchangeScreenshotPath)` when requested.

Do not call `UIResourceManager.Refresh()` for normal prefab edits. It refreshes
shared resources and can disturb unrelated game screens. Brush or sprite-sheet
changes are a separate, explicit full-resource refresh operation with a warning.

#### Black-screen and map modes

The preview must offer two modes from the Map Studio menu:

- **Black Stage** pushes a dedicated `ScreenBase` with a clearing
  `GauntletLayer` and a full-screen opaque black backing movie. Only the edited
  prefab is rendered above it. This is the default because it makes transparent
  pixels, text contrast, clipping, and alignment obvious.
- **Campaign Map Overlay** adds a diagnostics global layer while the campaign
  map is already active. It is useful for the political editor panel and map
  labels, but must not steal focus unless interactive testing is enabled.

Closing preview removes the layer/screen, resets input restrictions, restores
the prior `UIConfig` flags, and leaves campaign state unchanged.

#### Map Studio connection

Use a bounded file mailbox rather than injecting into the WPF process or making
engine calls from a named-pipe worker thread:

```text
Documents/AOC Map Studio/LivePreview/
  command.json          Map Studio -> game, atomic replace
  status.json           game -> Map Studio, atomic replace
  preview.png           optional exact screenshot
  logs/preview.log      bounded diagnostics
```

`command.json` contains a monotonic revision, action, slot, black/map mode,
prefab hash, mock-data profile, requested sprite categories, and screenshot
flag. The bridge polls it from `MBSubModuleBase.OnApplicationTick`, then performs
all Gauntlet work on the engine thread. Map Studio debounces authoring changes
for 250-400 ms and shows `Connected`, `Rendering revision`, `Live`, or the exact
load error in its status bar.

This design avoids firewall prompts, cross-thread engine access, port
collisions, and stale pipe servers. Both processes must verify that mailbox
paths remain inside the one expected exchange directory.

#### View-model bindings

A general prefab can contain `@Property`, `{Collection}`, `Command.*`, nested
data sources, and custom widgets. A dictionary cannot satisfy Gauntlet because
its binding system discovers real `[DataSourceProperty]` members and methods.
Use three explicit preview profiles:

1. **Literal layout:** Map Studio creates a transient copy in which the user
   supplies sample values for discovered `@` bindings. Commands are inert. This
   provides exact Gauntlet layout and rendering without changing source XML.
2. **AOC known screen:** use a compiled diagnostics view model matching known
   prefabs such as `PoliticalBorderEditor`, including safe no-op commands.
3. **Real screen:** attach to the actual game's native/AOC view model only when
   the corresponding screen is open. This is the final behavioral test, not the
   default editor preview.

Runtime-generated ViewModel types are technically possible with
`Reflection.Emit`, but collection element types, command methods, custom widget
contracts, and nested data sources make it fragile. It is not recommended for
the first implementation.

#### Sprites, brushes, and TPAC

Prefab-only edits can reload immediately. Existing registered sprites and
brushes render exactly. New or changed PNG source textures cannot appear in the
real Gauntlet renderer until the TaleWorlds sprite-sheet/resource import
pipeline has rebuilt SpriteData, atlas sources, and `_tex.tpac`. Map Studio can
orchestrate that pipeline, but it must label the operation **Rebuild UI Assets**
and never pretend a loose PNG is already an in-game sprite.

The official process and generated locations are documented by TaleWorlds:
[Generating and loading UI sprite sheets](https://moddocs.bannerlord.com/asset-management/generating_and_loading_ui_sprite_sheets/).

#### Screenshot and interactive behavior

After each successful reload, the bridge can capture one exact PNG using
`Utilities.TakeScreenshot`. Map Studio watches `preview.png` and displays it in
an **In-Game Preview** panel. This is change-driven, not continuous video.
Interactive hover, click, animation, and focus testing remain in the Bannerlord
window; attempting to stream and remote-control the game surface inside WPF
would add latency and input-routing problems without improving fidelity.

The runtime widget tree should also be returned in `status.json`: type, ID,
measured rectangle, visibility, resolved text, sprite name, and selected visual
state. Map Studio can compare those measured rectangles with its structural
preview and highlight discrepancies.

#### Why not embed Gauntlet in WPF

`UIContext` requires the engine two-dimensional context, input service, sprite
data, font factory, brush factory, widget factory, and resource depot. The
engine implementation also depends on native rendering initialization and the
game's frame lifecycle. Loading those DLLs into `AocMapStudio.exe` would make
editor startup dependent on Bannerlord native state and could reproduce the
frozen black-window behavior seen in the obsolete Unity build. The supported
process boundary is therefore deliberate, not a temporary limitation.

The bridge should use the Shipping Client for final compatibility checks and
the Modding Kit only when resource authoring/import is required. Directly
hosting `TaleWorlds.Engine.GauntletUI` inside the WPF process is not recommended:
the native engine initialization, rendering context, input service, resource
depot, and game assembly versions would become editor startup dependencies.

## Recommended implementation order for live preview

1. Add the fixed-slot diagnostics module and black-stage screen.
2. Implement command/status mailbox with revision and path validation.
3. Implement alternating targeted movie reload and last-known-good fallback.
4. Add literal mock binding substitution in Map Studio.
5. Return screenshots and measured widget-tree diagnostics.
6. Add the known `PoliticalBorderEditor` preview VM profile.
7. Add campaign-map overlay mode and interactive input as opt-in features.
8. Add explicit sprite/brush resource rebuild and refresh controls.

The first useful milestone is steps 1-5. It gives an exact black-stage render
inside Bannerlord and a returned screenshot inside Map Studio without touching
the protected renderer, protected `WorldCalendar.xml`, saves, or campaign state.

### Implemented diagnostics slice

The `PoliticalBorderSceneStudio` diagnostics module now implements the first
MapBar-focused slice against `Win64_Shipping_wEditor`: alternating targeted
movie slots, last-known-good retention through candidate load, a v2 request
contract with GUID/revision/SHA-256 correlation, unique screenshot paths,
black-stage wrapping, a fixed inert `MapTimeControl` profile, and bounded
runtime widget-rectangle reporting. The Unity client rejects stale or
uncorrelated results. This remains a Modding Kit preview; a distinct Shipping
Client host or an in-campaign test is still required before claiming runtime
compatibility.

## Safety and compatibility rules

- Never overwrite the protected
  `GUI/Prefabs/WorldCalendar/WorldCalendar.xml`; its approved SHA-256 remains
  `E7013CF2B18B381119CC7479F0840BC423CD59565913BD22BBFC1E0C55A82E5E`.
- Save new or derived work as a sidecar prefab with an atomic replace and a
  timestamped backup.
- Do not write or rebuild `AgesOfCalradia.dll` from the editor workflow.
- Keep preview/mock view models diagnostics-only. A mock command must never
  perform campaign mutations.
- Validate against the installed Bannerlord version and report unknown widgets
  instead of discarding them.
- Treat the WPF canvas as structural unless an exact Bannerlord preview has
  passed. Never label WPF output pixel-perfect.

## Delivery stages

1. **Usable XML layout editor:** hierarchy, common properties, drag positioning,
   undo, atomic save, and protected-path guard.
2. **Layout fidelity:** parent-relative rectangles, list/grid behavior,
   stretch/cover policies, resize handles, multi-resolution presets, and
   sprite/brush catalogs.
3. **Validation:** widget/property catalogs from the installed v1.4.8
   assemblies, binding profiles, and resource diagnostics.
4. **Exact preview:** diagnostics-only Bannerlord bridge with reload, errors,
   and screenshots.
5. **Authoring polish:** add/reparent/reorder widgets, copy/paste, snapping,
   guides, visual-state editing, and diff view.

The current first slice in Map Studio covers stage 1. Stage 2 should be
completed before calling the canvas a general visual editor; stage 4 is the
required gate before calling any preview game-accurate.
