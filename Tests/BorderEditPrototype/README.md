# AOC Border Editor v0.1.27

Test-only separate module for the approved AOC renderer (SHA-256 560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E).
Targets Bannerlord 1.4.8 / .NET Framework 4.7.2. It does not rebuild or replace Core.

## Controls
Load a disposable campaign with this module enabled after AOC CORE and Harmony. Press F9 after borders finish loading.
- Drag a green line to move the border. Click a line to select it; Delete removes selected sections. Shift-click adds/removes selections.
- Click a green endpoint diamond, then hover another endpoint. A green dashed line previews an allowed join; red means rejected, with the reason in the panel. Click the second endpoint to connect.
- Clicking the first endpoint again, Escape, or Cancel link cancels the pending connection.
- A successful displayed join shows BORDER CONNECTED, a persistent yellow link and cyan joined endpoints. The editor automatically returns to dragging. No extra mode switch is required.
- Ctrl+Z / Ctrl+Y undo or redo changes; either returns to dragging.
- Save edits / Ctrl+S saves the border draft. Close editor / Enter / F9 retains current edits for this session; save first to restore them after reloading.
- Discard edits restores the state at editor entry or the last successful Save edits. Escape outside a drag or pending connection closes while retaining completed edits for this session; Save edits is still required for reload persistence.

Editing pauses campaign time and restores its previous speed on exit. Left-drag is withheld from the camera; wheel, right-button and keyboard camera controls remain native.
Dragging is limited to 12 map units per gesture. Connections require open ends within 24 map units, matching oriented side colors, and no crossing or overlap. Edits are cosmetic and do not change political ownership, terrain, navigation or campaign saves.
Green lines prepare in bounded batches; only nearby endpoint diamonds are shown. Native readability and responsiveness remain subject to live testing.
## Persistence
Drafts and logs are under %LOCALAPPDATA%/AgesOfCalradia/BorderEditPrototype.
Draft paths are scoped to Campaign.UniqueGameId and a hash of captured topology/style/terrain height.
A matching regenerated border restores edits; a different ownership/topology generation does not receive stale edits.
Changed-generation drafts remain on disk. This v0.1 does not migrate edits across conquests/map changes.
No campaign save schema modifications. A second campaign has a separate draft directory.

## Architecture and patch contract
- BorderGraph.cs: pure topology, transactions, deletion, connection and validity checks.
- DraftStore.cs: bounded, validated XML with atomic replacement and a backup.
- NativeCapture.cs: read-only capture of accepted native segments and their exact ribbon/cap triangles.
- NativePreview.cs: prepare replacement rows before updating the core-owned frontier list; preserve untouched triangles and retain original rows for rollback.
- NativeCleanup.cs: restore row ownership before disposal; retry failed entity removals independently.
- PrototypeRuntime/Panel/Selection: map-thread input, temporary selection outlines and toolbar.

Protected targets and patch purposes (all version/hash gated):
- Builder.Advance prefix/postfix/finalizer scopes capture and clears scope on native failure.
- Builder.AddFrontierSegment prefix/postfix records accepted segments, without replacing generation.
- Builder.AddHalfRoundCap prefix/postfix combines the two halves into unique endpoint caps; inner capture hooks bind before caller recompilation to avoid wrapper inlining.
- Exact call-site transpilers in Builder.AddDoubleSidedQuad (four triangle calls), Builder.AddDoubleSidedFanTriangle (two), and the hash-pinned CoastSurfacePatch.CapStart (two) relay each original native upload and then record its arguments. Capture is limited to the active frontier segment and native thread. This avoids previously compiled/inlined Mesh.AddTriangle wrappers. A changed call count refuses binding and rolls back prototype patches; no native upload exception is swallowed. Verify-NativeBindings.ps1 checks actual installed method composition and these exact call counts.
- Builder.AddFrontierEntity and TakeFrontierEntities postfixes map captured rows to their native entities.
- CampaignKingdomBorderBehavior.ReplacePoliticalFrontierEntities postfix attaches a completed capture.
- CampaignKingdomBorderBehavior.ClearPoliticalFrontierEntities prefix restores retained originals before native cleanup.
- MapScreen.HandleLeftMouseButtonClick prefix consumes map orders only while editing.
- MapScreen.OnFrameTick postfix runs input and budgeted publication.
- MapCameraView.OnBeforeTick prefix masks only the four native left-drag fields in InputInformation while editing. This is the actual camera consumption boundary; it captures the first press as well as movement. A changed struct/signature or competing patch disables the editor and restores normal camera input. The native-binding verifier tests field preservation and real Harmony by-reference delivery before a managed consumer reads the same struct.
- MapScreen.OpenEscapeMenu prefix routes Escape to editor cancellation only while editing.
- MapScreen.OnFinalize prefix restores originals and releases editor-owned state.

Compatibility risk: private renderer/map methods, shared meshes, native scene lifecycle and competing Harmony patches.
Other patches on the listed renderer/map targets cause binding refusal, except the reviewed installed
fill-seam, render-diagnostics and coast-surface revisions on Advance/AddFrontierSegment and the two face helpers. Those allowances
check target, owner, patch kind, method/type and assembly SHA-256; unknown revisions still fail safely.
The editor-local coast adapter retains captured coast/join
classification and applies corrected surface projection and clipping to edited geometry.
Missing targets or unsupported renderer disable the prototype. Integration failures log and attempt original-row restoration.
Native generation exceptions are not swallowed. No optimizer, global coast suppression, NoDepthTest override or Core rebuild.
Native quad/cap helpers preserve width 1.6, source side colours, UV/winding and round-cap construction.
Original material/render order 108 and native zoom height are retained. Temporary selection outlines use order 109.
One native row may exceed the 4ms preparation budget; actual peak batch timing is logged on editor exit.
Input hit tolerance scales approximately with camera distance/FOV; precise screen-space picking remains a potential refinement.

## Build and checks
Run Verify-Prototype.ps1 from this directory. It builds only this sidecar, runs pure edit/draft and fake-scene transaction tests,
binds Harmony against installed assemblies in a separate Windows PowerShell process, checks the prefab/manifest,
and runs the protected baseline checks. No installation or game launch occurs.
The resulting package is package/AgesOfCalradiaBorderEditPrototype.
Checks cover edit/draft behavior and native-adapter assertions, including injected publication/removal failures.
Startup checks reproduce the installed fill/coast/diagnostic hooks and verify rejection of an unknown patch owner.
The fake scene checks actual adapter control flow and ownership restoration; they do not validate native rendering.
No main Core build or production release gate is run: this is a staged test-only module, not a production release.

## Required live acceptance (NOT YET EXERCISED)
- Load a disposable campaign with only compatible border modules. F9 opens the toolbar. Verify that left-drag moves the border without panning the map, including the first press; wheel/right-button/keyboard camera controls still work.
- Charas coast: direct drag follows the mouse, keeps styling and has no original duplicate or missing neighbor row.
- Internal two-colour frontier: colours, width, caps and terrain/zoom presentation remain consistent.
- Shift-select/delete a short section; Undo restores exact geometry and Redo removes it again.
- Connect points: click two green exposed ends from separate sections; chosen first point becomes cyan. Reject incompatible sides/crossings with a visible reason; Escape returns to dragging; drag the connected result; Undo restores the gap.
- Test close/middle/far zoom, tilted camera, daylight/night, pointer release over toolbar, lost focus and invalid terrain pick.
- Cancel restores originals; no party orders; camera does not jump; previous campaign speed returns.
- Save/reload retains edits and deletions for matching topology; a different campaign does not inherit them.
- On ownership regeneration, stale edits are not applied to a different topology.
- Measure frame time/drag latency and inspect prototype.log for failures. Managed checks do not establish visual parity.


Marker visibility (v0.1.4): endpoint rings have a 4-map-unit outer radius, white inner band and dark outer outline. The selected start adds a white outer halo. Endpoint click tolerance covers the 4.6-unit halo. These remain world-space markers; readability at extreme zoom and native visual acceptance require in-game checking.


Green editing overlay (v0.1.4): opening F9 draws bright green center lines with dark edges over all editable borders, including new connections. Hover/selection adds yellow outlines and endpoint handles. Lines follow published edits, disappear on deletion and close with the editor. NativeEditLines caches unchanged renderer rows independently of hover. This uses existing native mesh helpers with no new Harmony patches; protected border assets are untouched. Fake-scene coverage verifies row caching, movement, deletion, connection and cleanup; actual terrain/zoom readability remains a native visual acceptance check.


Connection interaction (v0.1.5): open-end rings remain visible in drag mode. Clicking a ring starts connecting immediately; click a second ring to join. Escape returns to dragging. Endpoint hit tests use the displayed sampled marker geometry and native zoom translation, with nearest-center preference for overlapping rings, rather than a terrain intersection. This allows clicks over water and raised coast markers. Existing degree, distance, crossing and side-color validation remains unchanged. Toolbar nonresponse from the supplied video is not yet explained; bounded UI-routing and command logs distinguish that separately. Automated coverage includes an angled raised-coast ray, miss/cleanup tests, and two nearby picked endpoints creating a bridge.


Activation workload (v0.1.6): line meshes upload one batch per frame, capped at eight edges and 32 subdivisions per edge. Pending work is discarded on close. Open handles are limited to the nearest 16 within 80 map units of the cursor area; the active connection start remains included. Each ring samples its anchor once and draws a flat 12-sector marker. These are bounded-work safeguards, not a measured native frame-time guarantee; flat handles can intersect steep terrain. Focused checks cover partial activation cancellation and native-sample counts. Live freeze resolution is unverified.


Connection clarity (v0.1.7): filled green diamonds replace oversized hollow rings (outer radius 1.8 map units, selected cyan halo 2.1). The panel distinguishes dragging lines from connecting diamonds and shows the first/second click steps. True black outlines and lower-radiance pure green/cyan replace pastel vertex colors. The installed vertex_color shader applies fog and pre-exposure, so this is a mitigation for the supplied washed-out video, not proof of exposure-independent rendering. No new shader or material mutation is introduced. Existing bounded marker and line workloads remain. Focused geometry checks verify filled centers, compact hit regions and two-point connection; native visual acceptance remains outstanding.


Connection confirmation (v0.1.8): after a valid bridge reaches the published preview, show BORDER CONNECTED in the panel and a native information message. Retain the new bridge selection in connect mode so its yellow outline and both cyan endpoint diamonds persist until another connection starts, mode changes, or Undo removes it. Rejected connections never trigger success feedback. Focused checks cover visible link/end markers, persistence across frames and Undo cleanup; live readability still requires in-game confirmation.


Interaction flow (v0.1.9): validation-only connection queries share the commit rules without changing graph state or Undo history. Re-evaluate only when the hovered endpoint, source endpoint or graph revision changes. Draw at most eight preview dashes from two sampled anchors; no full-map preview rebuild is introduced. Successful joins return to dragging while keeping the confirmed link selected. Focused checks cover allowed/rejected previews, unchanged history and retained confirmation in drag mode. No native live acceptance is claimed.


UI and interaction polish (v0.1.10): compact 420x280 slate panel with contextual hint, status card, familiar action buttons and shortcut footer. Hovered endpoints brighten and grow 18 percent with matching hit geometry. Pointer jitter below one quarter of the current border-picking tolerance remains selection-only; deliberate movement latches dragging. Escape cancels a pending gesture or closes while retaining completed edits; Discard edits is the explicit session rollback action. Overlay bridge deletion is prioritized and reconciles live entities rather than only previous queued snapshots, preventing stale links after rapid updates. Tests cover hover hit geometry, drag intent, interrupted-update deletion, and existing connection/cleanup contracts. The three-state panel layout was rendered from the prefab for approximate layout QA; it does not prove native fonts, lighting or live interaction.


Visible-line picking (v0.1.11): line hit testing now uses the actual cached green/outline triangles, translated by native zoom height. Per-batch bounds reject distant meshes; queries perform no native terrain calls. Changed batches awaiting upload and deleted connections have no stale hit targets. Dragging projects the pointer onto the horizontal plane at the picked surface, avoiding ground-intersection jumps on raised coastal ribbons. The panel reports Preparing lines during overlay setup. No additional Harmony patches or protected-renderer changes. Focused checks cover elevated angled picks, native-call counts, inactive batches, bridge IDs, parallel drag rays, deletion and cleanup. Native map rendering and user-perceived smoothness still need live acceptance.


Save/history clarity (v0.1.12): Undo, Redo, Save and Delete disable and dim when unavailable. Keyboard shortcuts report a precise no-op reason. Saved/Unsaved compares current edits with the last loaded or successfully written disk draft, independent of editor entry/Discard state. The comparison is cached by graph revision so panel refresh does not compare the full map every frame. Undoing away from a saved state marks Unsaved; redoing back marks Saved. Save failure leaves the disk baseline unchanged. Persisted draft loading now remains separate from in-memory border replay. Focused checks cover draft state transitions, history availability, real VM default enabled/opacity values and prefab bindings. No native UI click or campaign save-reload acceptance is claimed.


Coastal precision view (v0.1.13): hide normal frontier entities while the editor is active, leaving thin green editing lines (0.44-unit green core; 0.68 total with dark outline). Closing restores normal styling and the captured visibility flags; edited replacements inherit their predecessor's intended visibility. Undo does not reveal duplicate normal ribbons during editing. Overlay failure exits the editor and restores normal borders. New connection lines and attached endpoints stay cyan even without selection; connected markers are squares and open ends are green diamonds. Nearby completed endpoints are capped at 16 in addition to the existing 16 open-point cap. Open-end hit geometry adds 0.75 map units of forgiving padding, keeping nearest-center selection for overlap. Focused checks cover normal visibility across publish/undo/close, thin-line geometry, persistent connected markers and near-miss picking. Native coastal appearance remains pending live acceptance.

Visual connection correction (v0.1.14): the approved renderer's ApplyFrontierZoomPresentation(bool) unconditionally restores normal border visibility. A hash-pinned, instance-scoped Harmony prefix now skips that presentation only during editing; closing still restores captured visibility. Missing targets or unknown patch owners disable binding safely and log through the existing boundary. The real target is exercised under Harmony in Verify-NativeBindings; fake scene tests cover restoration. Endpoint picking retains exact marker hits, then accepts near misses within an approximately 18-pixel angular radius using camera FOV and resolution, with no terrain query. First-point selection immediately previews the closest legal displayed endpoint; a click on the destination is still required. Marker highlight bands use green rather than exposure-washed gray. Focused tests cover angular picking at different depths, behind-ray rejection, and non-mutating automatic guides. Native appearance and interaction acceptance remain pending.

Cursor/save correction (v0.1.15): runtime log for 04:42-04:44 UTC showed missed endpoint picks, one selected start, no completed connection, zero published batches, and no saved draft. Use SceneView.TranslateMouse(ref near, ref far, -1) exactly as installed MapScreen.HandleMouse instead of independent camera projection; verify shared native target in the binding check. No new Harmony target. Untouched sessions say No edits; Save stays actionable when ready so it can explain no changes or an unfinished connection. Actual writes log path and change counts. Close explains when nothing was applied. Fake scene regression verifies edited geometry remains visible in normal colours after close and reopen. Live cursor alignment and save/close acceptance still need in-game validation. Actual game logs are readable through localhost administrative share; packaged-app LocalAppData reads returned redirected offline verifier logs, so do not mistake these for gameplay evidence.

Direct deletion (v0.1.16): toolbar Delete toggles a dedicated mode without requiring a selection. Hover paints only the targeted section red; click removes one section and stays in Delete mode. Endpoint handles are hidden and cannot intercept clicks. Done, Escape or Connect exits deletion. Delete-key selection removal remains available outside the direct workflow. Deletion uses a separate cached hit strip padded one map unit on either side; precision drag geometry remains unchanged, and dirty/deleted/unprepared strips cannot be clicked. Undo still restores the graph. No new native hooks or persistence schema. Tests cover near-line selection, stale-target removal, red-only feedback and suppressed endpoint hits. Live deletion feel remains to be validated.

Freehand creation (v0.1.17): Draw border uses left-button hold, sampled freehand preview and release to commit one stroke; one Undo removes the entire stroke. Drawing is allowed in empty map areas and after deleting old segments. Nearby open ends snap within 0.5 map units. Fill area uses a freehand outline and release to close/apply a simple polygon. Fills sample the captured native nearest-site index per small triangle at native 50-percent brightness; strokes sample local colours at 85 percent, preserving colour transitions between authored nodes while retaining strict original endpoint connection rules. Territory ownership is unchanged.

Creation boundary: pure graph/geometry and XML v2 persistence, isolated native location adapter, creation gesture overlay and separately owned fill meshes. Version1 drafts still load. New fills reject self-crossings, empty land results and excessive complexity before commit. Exact supported protected API is IsPoliticalLandExact(Vec2), verified against the immutable DLL (current repository source contains a newer differently named helper). Water-adjacent triangles are conservatively omitted, so a narrow coastal gap is possible. Added fills follow normal political-layer alpha outside editing. Delete mode removes a hovered custom fill when no border line is targeted; Undo/Redo and save apply to fills too.

Prototype limits: 64 sampled points per line stroke, 32 per fill outline, 48-unit fill extent, 256 additional graph points, 128 custom segments and 32 fill patches per draft. A stroke is rejected wholly if invalid or over limits. Fill uploads use at most16 triangles per tick and reuse meshes until geometry or alpha changes. Release checks cover freehand commit/undo, new-point native publication/close, localcolour lookup against installed renderer, fill terrain exclusion, upload bounds, cancellation, deletion, old draft compatibility and new draft roundtrip. UI layout preview checked separately; native freehand feel and fill appearance still require in-game acceptance.

Cursor and freehand save feedback (v0.1.18): game log showed an open-water stroke rejection immediately before a successful draft save (11 links,49 deletions). Drawing now intersects the scene-view mouse ray with the elevated drawing surface instead of raising the ground intersection afterward. A bounded12-step surface refinement matches the5.4 border lift and native close-zoom drop; fill preview/input share4.12. Unresolvable surface cancels an active stroke rather than committing a partial shape. Ghost frame updates even when the cursor is stationary. Narrow-headland localstyle sampling checks center/endpoints/closer side supports before rejecting both wide water samples. Rejections explicitly notify Drawing not applied; successful Save reports drawnsegment/deletion/fill counts. Regression checks cover angled ray, zoom, slope, invalid ray, ghost frame and actual released-stroke draft roundtrip. Native cursor accuracy remains pending live acceptance.

Top-down editing (v0.1.19): opening F9 defaults to straight-down camera elevation. Header3D view/2D view toggles presentation; switching cancels an unfinished gesture. The Harmony prefix on Bannerlord1.4.8 MapCameraView.ComputeMapCamera changes only the local cameraElevation argument toPI/2 while Active andTopDown. Nativebearing,target,zoom,mapbounds and storedcamera state remain under native control. Closing or3D toggle resumes normal elevation on the nextnative camera update. This is a top-down perspective view; terrain itself is unchanged. Missing signature or unsupported patchowner fails binding with existing diagnostics, retainingnative execution. Verification uses actualHarmony argument flow and camera rotation math, plus inactive/3D/close cases. No protected artifacts or saved terrain changed. Live camera interaction remains pending acceptance.

Continuous authored ribbons (v0.1.20): replace independent square segment ends with shared cross sections at adjoining authored nodes. Both incident segments sample identical corner positions, eliminating wedge gaps and corner overlap at bends. Miter reach is limited by width and neighboring segment lengths to prevent spikes at acute turns. Green/cyan editing strips and their hit geometry use the same join calculation; original captured native ribbons and their cap style remain untouched. Existing saved authored strokes rebuild with these joins automatically. Tests verify shared right-angle corners in published native geometry with separate local colours, sharp-turn bounds, open ends after deletion, and exclusion of captured native ribbons. No saved point coordinates or draft schema changed. Screenshot issue diagnosed from visible square-ended strips; corrected in-game appearance still requires acceptance.

Visible drawing endpoints (v0.1.21): border drawing no longer reuses Delete mode's hidden-endpoint presentation. Nearby open diamonds remain visible and brighten when the cursor snaps to them. Drawing uses the existing screen-angle endpoint picker and places the stroke preview at that exact graph point. A snapped release retains the exact endpoint even inside the freehand sample threshold, replacing a nearby final sample rather than creating a tiny return segment. Successful joins explicitly confirm connection; graph rejection still reports no applied stroke. Fill and deletion retain their uncluttered presentation. Tests exercise a stroke starting and ending at original endpoints, exact final endpoint identity, and single-step Undo restoring both open ends. No schema or protected renderer changes; native usability awaits acceptance.

Boundary tracing (v0.1.22): Follow coast and Follow political fill are assisted drawing modes. Hold left mouse near the boundary and guide slowly; the yellow marker/preview shows the snapped path. Coast reads the protected renderer's existing exact land policy; political mode reads its captured nearest-site colours on land (including the land/water edge), not pixels or custom fill polygon outlines. A bounded local search reaches 3 world units, at most 104 label queries per moved cursor, with stationary caching. Draw short sections; jumps over 2 units or loss of boundary cancel the pending stroke visibly. Nearby endpoint snapping remains available. Completed strokes use existing atomic graph validation, Undo, rendering and v2 draft persistence. No new Harmony targets or protected asset/schema changes. Native performance and visual alignment still require in-game acceptance; narrow features below the sampling interval and same-colour ownership boundaries cannot be resolved by this guide.

Tracing improvements (v0.1.23): cursor movement up to 6 units is resampled in at most six boundary searches, retaining local steps rather than a direct chord. Unsupported jumps, loss of boundary, and guided stroke capacity pause at the last accepted point; release keeps that section and returning nearby resumes it. The yellow cross remains visible at the drawing tip. New strokes remove nearly collinear points within 0.12 units, retaining local colour transitions, endpoints and a 4-unit maximum simplified chord. Existing drafts are unchanged. The 128-segment draft cap remains; simplification reduces consumption for new strokes but does not free capacity in an already-full draft. No new patches or schema changes. Native frame time and visual acceptance remain unverified.

Draft capacity (v0.1.24): removes the 128 authored/connected segment cap and the 256 appended point cap in graph validation and v2 loading. The panel displays segment count without a quota. Drafts retain version 2; old v1/v2 files remain readable. Loading checks declared point count against actual Point elements before allocating, within the existing 32 MB document bound. Connection validation builds adjacency once instead of scanning every edge for each node. Normal-style drawn border meshes use batches of at most 32 segments and the existing staged publication/time budget. Empty trailing batches remain reusable until baseline restoration/teardown. Stroke sampling, fill bounds, history depth and document-size safeguards remain. Large maps still consume memory and CPU; this change does not promise unlimited performance. No protected artifact or native patch changes.

Manual drawing land-mask correction (v0.1.25): negative political land-mask samples no longer reject a manually drawn border as open water. Confirmed land still supplies the usual side colours; when all support samples are rejected, the midpoint's local territory colour supplies both sides. This deliberately allows manual lines over actual water as well as land excluded by the mask. Valid native surface sampling, topology and crossing checks still apply. Coast/political guidance and fill land filtering are unchanged; this does not correct the underlying mask or change ownership. Tests cover all-negative samples, normal two-sided colours, coastal inheritance, collapsed strokes, and commit/save/reload. No schema, protected renderer or Harmony changes.

Fill to border (v0.1.26): click inside a closed border face to preview and fill its exact outline with local territory colours. The graph face walk uses surviving original, drawn and connected edges, caches by revision, and handles shared junctions. Open faces and nested holes are rejected explicitly. Existing polygon bounds (32 nonredundant corners, 48 units extent, 8192 subdivided triangles) remain, and straight redundant border vertices are removed without moving the outline. Filled areas are snapshots; later border edits do not automatically reshape them. New manual and border-derived fills bypass the political land mask, including intentionally selected water, but still require valid native surface sampling. Optional v2 clipToLand attribute defaults true when absent, preserving old draft clipping. New manual fills save false. No protected or native patch changes. Managed tests are not native visual acceptance.

Saved-layout fill repair (v0.1.27): Repair.xml is a reviewed geometry plan for the specific backed-up draft/topology, not a global automatic fill. It fills 22 coastal delta regions and trims 20, leaving two mixed-side regions unchanged. Original fill colours remain and additions query the captured local colour index. The plan is gated by exact draft SHA-256, border topology signature, each source row's full canonical XY/colour triangle fingerprint (native float*1000 round-to-even, winding-independent, duplicate-preserving), row face count and entity membership. A read-only postfix on hash-pinned PoliticalFillSeamFix.NativeMeshTransaction.Commit records successful row identities before CPU data is released. Unknown module revisions fail binding; missing/mismatched data leaves original fill. A prefix on protected ClearPoliticalFillEntities restores original row references before native cleanup. Native calls remain main-thread-only; candidate upload is at most 64 triangles per tick with a 4 ms soft budget, original rows remain until complete, then the list replacement is reversible. Height uses the same campaign surface+3 convention as the installed seam fix. Later edits invalidate the one-layout repair and restore the original fill with a diagnostic; this is not dynamic ownership or continuous clipping. Protected artifacts and saved border geometry are unchanged. Geometry preparation verifies coverage, no spill within accepted trim masks, no missing addition, and no overlapping output; native adapter tests cover staging, fingerprint refusal, changed drafts and surface failure. Live acceptance is required.

## Published borders inside AOC Core (v0.1.28)

`Publish-CoreLayout.ps1 -Draft <saved XML>` installs the reviewed layout and fill
repair under Core/AuthoredBorders and registers PublishedBorderSubModule in the
existing Core manifest. The protected renderer and calendar prefab remain unchanged.
Core loads the packaged geometry by exact topology, independent of campaign ID or
LocalAppData drafts. `F9` opens the editor directly against the published layout;
`Ctrl+S` atomically saves that same Core/AuthoredBorders layout. The panel labels
this result `PUBLISHED LAYOUT SAVED`. Keep the separate editor module disabled.
Original editor drafts are preserved. A changed published outline invalidates the
exact-hash fill repair until its reviewed repair plan is refreshed.

At campaign load, the published layout and its exact reviewed fill repair apply
before the first map frame, with 15 seconds for borders and 45 seconds total for
the layout, seam correction and reviewed repair. While capture is pending, the
published adapter advances the hash-pinned seam module's existing Tick on the
native thread; its immutable CPU preparation remains asynchronous. Missing,
broken or mismatched seam generations fail immediately. This avoids waiting for
map frames which cannot run during loading. Failed staging restores that
operation's original rows and records failure in `prototype.log`. PublishedReady
is true only after the loaded layout and its required repair succeed; topology
rejection, mismatched repair and timeouts cannot report first-frame readiness.
Synchronous publication additionally requires the optimizer's explicit
`LoadingPublication` scope. Ordinary campaign-frame rebuilds retain bounded
preview/repair ticks; they cannot run the loading loop and freeze gameplay.

This reuses the verified native renderer adapter and existing Harmony targets;
no new native targets are introduced. Unsupported topology retains original borders
and logs rejection. Fill repair additionally requires exact draft and native row
fingerprints. The two ambiguous repair regions remain untouched. The published
layout remains loaded while editing, so saving does not fall back to the original borders.

Verification: Release build, GraphTests published-path/round-trip/topology tests,
PreviewTests and actual Harmony binding checks via Verify-Prototype.ps1; deployment
checks installed hashes and the protected baseline. A fresh game load is required
to visually validate the Core-only startup path.
