# Political border and fill editor

`AgesOfCalradia.PoliticalBorderEditor.dll` is a separate Bannerlord v1.4.8
sidecar. `F10` opens a framed right-side Gauntlet workshop based on the Refuge
Builder HUD pattern. It captures map coordinates and writes bounded override data to
`ModuleData/PoliticalBorderOverrides.xml`. The protected political renderer,
its assembly, and the approved WorldCalendar prefab are never rebuilt or
modified.

## Controls

Every placement/editing mode is a planning mode. Line, Curve, Follow Coast,
TRACE, Freeform, SCULPT, Fill, and MOUNTAIN SURVEY create or modify black dotted
staged geometry only. **CONFIRM PLAN** moves the active draft into the in-memory
plan list; it still does not alter the live political mesh. Only **SAVE + APPLY
ALL PLANS** writes the XML and requests the optimizer rebuild.

- `F10`: enter or leave the editor.
- `B`: border mode; `L` selects straight lines, `K` selects a smooth
  control-point curve, `J` selects Follow Coast, and `G` selects click-and-drag
  freeform drawing. Follow Coast snaps each anchor to the hidden captured
  ocean/lake graph and routes between connected anchors instead of drawing a
  chord through the terrain.
- `R`: TRACE mode. Anchors snap to the registered red province-line PNG and
  route over its connected graph. `N` temporarily returns map input to
  Bannerlord for camera navigation. `T` cycles Solid, Dashed, and Double for
  the next authored path.
- `H`: smooth the active border twice while retaining the endpoints of an open
  connected path. Closed loops are smoothed cyclically.
- `U`: SCULPT mode. Click and drag a visible authored node over the terrain.
  Coincident connection points and the matching closed-fill vertex move with
  it, preserving the border network until SAVE + REBUILD applies the change.
- `Y`: start or stop MOUNTAIN SURVEY. While campaign time and AI remain paused,
  `WASD` moves the main-party visual survey offset over ordinary terrain and
  mountain ranges; holding Shift increases the speed. The route is sampled at
  bounded 0.25-map-unit intervals as a persistent black plan. Press `Y` again
  to restore the panel, then sculpt or commit. The editor restores the original
  visual offset on stop or close and never changes the party's logical campaign
  position, target, or navigation face.
- `F`: political-fill brush mode. Hold the left mouse button to paint existing
  political fill; `Page Up` and `Page Down` change the brush radius from 0.5 to
  20 map units. Panel buttons use precise 0.25-unit steps and permit a 0.10-unit
  minimum; the terrain ring is the exact staged footprint.
- `Enter`: commit the active path or brush stroke; `Backspace` removes its last
  point and `Delete` removes the last committed shape in the current mode.
- `Ctrl+S`: atomically save the XML and mark the political renderer dirty so
  the cache rebuilds with the edits.

The **SETTLEMENTS** button cycles `HIDDEN`, `ALL`, `TOWNS + CASTLES`, and
`TOWNS ONLY`. It runs only while the editor is open and deliberately executes
after the normal zoom filter so selected labels remain usable at full-map
drawing altitude. Closing the editor immediately returns label visibility to
the ordinary campaign rules; the World Events UI is not patched.

Pressing `F10` makes the existing light-gray political fill very transparent
at 18% entity opacity so terrain remains readable during planning. **TRANSPARENT
FILL: ALL ZOOMS** retains that faint fill at close zoom, while **TRANSPARENT
FILL: OVERVIEW** multiplies the ordinary altitude fade by the same 18%. Closing
the editor restores normal campaign-map opacity. This changes renderer alpha
only and cannot create fill over water or excluded terrain.

Every command above also has a button in the panel. The panel includes a
toggle for the terrain-draped 100-square-mile planning grid. Its established
planning scale is a ten-by-ten-map-unit cell, bounded to the settlement map
envelope plus a thirty-unit margin so debug rendering cannot grow without
limit.

**READY / DRAW** enters planning mode by hiding the panel while keeping the
selected drawing tool, crosshair, accumulated black mark, and guide overlays
active. `Tab` restores the panel. TRACE automatically enables the calibrated
red reference overlay before planning mode begins.

The panel is a focused interactive Gauntlet layer: every visible button calls
the same state operation as its shortcut and immediately refreshes the tool,
detected faction, radius, and shape counters. Pointer hits inside the panel are consumed
by the editor UI and cannot add a map point.

**EDIT GEN** exposes the optimizer's exact pre-tessellation generated
centerlines as orange plan lines. Clicking a chain promotes only that chain to
the active near-black dotted plan with circle-and-dot control handles; dragging
a handle moves it, while dragging directly on a segment inserts a new circular
control point and bends the path. Smoothing, undoing, and confirming remain staged operations. The
optional `replacesGenerated` XML attribute makes SAVE + APPLY remove only the
nearby source chain before adding its edited replacement. Unedited generated
chains remain read-only reference plans and are not duplicated in the override
file. A missing or malformed snapshot disables only EDIT GEN.

## Map previews

Committed authored borders remain visible as black dotted planning lines while
the editor is open. Active anchors are black circular rings with a solid dot in
the center, making every accepted click distinct from the connecting stroke.
The scene preview uses a near-black charcoal vertex value because Bannerlord's
forward `vertex_color_mat` pass can lose zero-RGB geometry; saved authored
ribbons remain true opaque black. This presentation distinction does not alter
the plan file or apply a border early.
The active stroke, MOUNTAIN SURVEY trail, next segment, coast
route, and TRACE route use the same dotted presentation. A first anchor is
shown immediately as a short black mark before a second point exists. Dotted
plans use production line quads in a safe late render bucket rather than tiny
terrain discs. The cyan crosshair
marks only the current terrain cursor.
SCULPT temporarily exposes node handles and highlights the node being dragged.
Fill mode displays a terrain-draped faction-color brush-radius ring and cyan
crosshair before paint is committed.

The **REFERENCE OVERLAY** toggle reads the registered 1672x941 transparent PNG,
maps its 702x702 calibrated aperture back through the approved
settlement-fitted campaign transform, and incrementally samples its vertices
onto the 3D campaign terrain. It is not a flat screen texture. The same bounded
graph drives TRACE snapping and ghost-route previews. Missing image,
calibration, or terrain data disables only this optional guide and is logged.
Visible red connections are additionally checked against the source PNG, so
coarse routing adjacency cannot appear as unsupported wire or net chords.
The dark-green grid and red reference graph are persistent, bounded scene
meshes built once per campaign scene and ordered below the political fill.
Crosshair, first anchor, active route, and staged plans use a small dynamic
late no-depth scene mesh above the fill. A two-frame publication handoff keeps
each dynamic entity alive across a complete Bannerlord scene-render cycle.
These production entities do not
depend on debug primitives that the Shipping Client may hide.

Every map tool retains the terrain-draped cursor crosshair. While the left
mouse button is held for a click or drag, the crosshair expands and thickens so
the active sample position remains unmistakable for line, curve, Follow Coast,
freeform, smoothing-assisted, and fill workflows. Pointer activity over the
Gauntlet control panel is excluded from map marking, but panel focus no longer
hides the crosshair preview.

The panel remains the focused Gauntlet layer and registers only mouse buttons
for input restriction. This keeps panel clicks in Gauntlet hit routing without
blocking keyboard or mouse-wheel camera input. CAMERA NAVIGATE
suspends drawing in editor state without dropping toolbar focus, so the same
button always remains available to return to 2D editing. Native panning and
zoom input remain readable while navigation is active.

Opening `F10` captures the complete native camera state and switches to a
fixed north-up vertical 2D planning view at the current normal zoom level. Native map
panning and zoom continue to update the target and distance under the vertical presentation.
Closing the editor restores the captured frame, bearing, and camera distances.
Missing or changed MapCameraView members disable only the 2D presentation and
leave the native camera active.

There is no manual faction selector. At the first point of every border or fill
stroke, the editor finds the nearest owned town, castle, or village—the same
site rule used by the political renderer—and locks its kingdom or clan for the
complete shape. Every authored border stores that inferred faction. Closing a
border loop automatically creates a matching fill polygon in that faction's
color. Authored ribbon geometry is opaque black on both sides so the visible
stroke does not inherit two different neighboring faction colors. The XML
retains both the inferred faction ID and faction fill color from its enclosing
border or brush operation. Legacy colorless records fall back to neutral gray.
The temporary light-gray political-fill presentation is applied after these
records, so faction colors remain saved even though they are intentionally
masked during the current tracing pass.
This association is editor metadata and does not transfer settlements or
change Bannerlord's underlying kingdom ownership. The fill implementation
only recolors protected fill triangles that already exist, so water, excluded
islands, and border exclusions cannot acquire newly generated political fill.
For the current editor build, all generated ocean and lake border ribbons are
unconditionally suppressed. Their captured graph remains available only to
route the Follow Coast picker. Inland faction-to-faction borders are preserved,
and committed authored coastal paths are added back as connected ribbons.

**THINNER** and **THICKER** adjust a backward-compatible global width scale from
0.50x to 2.50x. Saving applies it to generated and authored ribbons alike.
Solid, Dashed, and Double are stored per authored path; generated native borders
remain Solid. Fill-brush radius remains independently adjustable from 0.5 to 20
map units.

`ModuleData/PoliticalBorderOverrides.xml` is the human-readable source of truth.
SAVE + REBUILD writes it atomically, including resolved TRACE points, closed
loops, inferred faction metadata, fill strokes, per-path style, and global width
scale. It can be inspected or corrected outside the game without decoding a
binary save.

The editor and optimizer compile the same version-1 document reader. Width and
style are optional backward-compatible fields, and unknown XML elements are
ignored. Future geometry, terrain, tessellation, and cache optimizations must
continue to consume this world-coordinate file; the override-file hash already
invalidates an older optimized cache whenever an edit changes. A future
incompatible schema requires a new version and an explicit migration rather
than silently reinterpreting existing plans.

The planning grid is dark green and rendered below the red reference guide;
both guides render below the political fill.
A successful border commit plays a short UI confirmation sound. If Bannerlord
cannot resolve that sound event, audio disables safely while drawing and saving
remain active.

## Connectivity

All points inside a line, curve, or freeform stroke are emitted as one chain
with exactly shared endpoints. Once a border network exists, a new open path
must start or finish on an existing yellow node. Nearby clicks snap within 1.5
map units and joining a node commits the connection. A separate island or lake
may be started with `Shift+click`, but it must close back onto its first node;
the editor will not save it as a loose open fragment.

## Compatibility and failure behavior

The editor patches `SandBox.View.Map.MapScreen.HandleLeftMouseButtonClick` and
`MapScreen.OnFrameTick` by name. These private targets are version-sensitive.
If either target changes, all editor patches are removed and normal map input
continues. Cursor projection failure disables continuous freeform/brush
sampling while leaving click-based line and curve tools available. Malformed,
oversized, non-finite, or disconnected override data is rejected as a whole;
the generated political geometry remains active and the reason is written to
`Logs/PoliticalBorderEditor.log` or `Logs/PoliticalBorderOptimizer.log`.

The Gauntlet panel is optional presentation: if its movie or screen layer fails
to load, the editor remains active through the keyboard controls. Pointer hits
inside the panel are excluded from drawing so clicking a tool cannot place an
accidental map point.

As a global map overlay, the panel owns focus and requests Gauntlet mouse-button
routing. Its event-capable root is panel-sized, so the transparent map
area cannot consume drawing clicks. Every command logs
invoked/completed/failed diagnostics.
Camera-navigation mode suspends drawing without releasing toolbar focus. Cursor lookup
walks inherited private members because Naval DLC runs `NavalMapScreen` while
`_mouseRay` and the terrain-intersection method belong to base `MapScreen`.

`Tests/Verify-PoliticalBorderEditorRuntime.ps1` performs a real Harmony binding
check against the installed Bannerlord assemblies for map clicks, frame input,
and settlement-label filtering. The source contract test separately checks
every prefab button against its ViewModel command.

## Native external map editor

`Tools/PoliticalBorderMapEditor/Build-MapEditor.ps1` prepares and launches the
native WPF `AocMapStudio.exe`; it does not open Edge or host a web application.
Its sole authoritative presentation plane is the NavalDLC `Main_map` world
square `0..1040 x 0..1040`. A 2081x2081 inclusive raster uses two pixels per
campaign unit, flips Y for north-up display, and otherwise applies no affine.
World coordinates from `PoliticalBorderGeneratedLines.xml` therefore survive
load, pan, zoom, drag, save, and reload without compensation or nudging.

The v60 geometry-cache reader is offline and read-only. It loads the exact fill
and frontier triangles previously captured from the protected renderer, exposes
their terrain Z values to the 3D inspector, and builds the political-filled-area
index used by underwater/unfilled validation. Only a failing sampled border
subspan is drawn red; the diagnostic layer cannot produce a reference net or
change saved border color. Political fill is visible by default.

The old strategic-map affine is not authoritative: the settlement audit measured
13.29 pixels RMS and 42.02 pixels maximum residual. WAND measured 86.22 pixels
RMS. Those rasters are off-by-default approximate guides and may never drive
geometry, snapping, validation, or persistence.

An incrementally captured version-2 terrain package uses a 1041x1041 north-up
grid over the same exact world square, includes native terrain classifications,
and records the campaign scene token and revision. The builder rejects a stale
or legacy cropped package. Until it exists, cache-derived Z values provide exact
political-land relief and missing terrain remains visibly unavailable rather than
being synthesized from a misaligned image.

Generated lines can be promoted to black dotted plans and edited through their
circle-and-dot nodes. New line, freehand, coast-follow, and fill-brush operations
use the existing version-1 XML persistence contract. A closed border owns its
fill path: the UI renders one shared path, and save derives `FillPolygon
fill-{borderId}` from the same points. Moving a border node consequently moves
the matching fill edge and prevents an independently triangulated preview from
poking through the border. Open paths remain border-only. The native editor
infers the nearest settlement kingdom at operation start and saves its normalized
Bannerlord fill color while leaving the visible border stroke opaque black.
Editable fill previews are clipped by the exact cached fill geometry; they do
not render new light-gray coverage over water or excluded islands.

**Install into AOC CORE** creates a timestamped backup and writes only
`ModuleData/PoliticalBorderOverrides.xml`. Portable Save/Open use the identical
document. The native editor never rebuilds, replaces, or deploys the protected
`AgesOfCalradia.dll` or `WorldCalendar.xml` artifacts.
