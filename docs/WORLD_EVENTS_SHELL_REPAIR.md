# World Events shell repair

## Purpose and boundary

`AgesOfCalradia.WorldEventsShellRepair.dll` is a UI-integration sidecar for the
immutable approved `WorldCalendar.xml` and `AgesOfCalradia.dll`. It does not
change calendar data, view-model behavior, save data, political rendering, or
the files protected by `Tests/Verify-ProtectedPoliticalBaseline.ps1`.

The approved prefab references five compiled atlas sprites for its outer shell
and selected-tab states. On the supported installation those sprite widgets can
instantiate without rendering their atlas texture. The protected assembly still
contains direct texture providers whose source PNGs live in
`GUI/CustomUI/WorldEventsSkin`. The sidecar reconnects only those five exact
widget IDs and expected sprite names to those existing providers in memory.

## Harmony target and compatibility

- Native target: Bannerlord v1.4.8
  `TaleWorlds.GauntletUI.PrefabSystem.WidgetTemplate.LoadFrom` with parameters
  `PrefabExtensionContext`, `WidgetAttributeContext`, and `XmlNode`.
- Patch form: prefix that may replace the incoming XML node object. It does not
  edit the source XML document on disk.
- Scope: the five unique `WorldEvents*Shell` IDs are changed only when their
  current `Widget` element and `Sprite` value exactly match the approved
  prefab contract.
- Conflict avoidance: the sidecar requires the approved primary DLL SHA-256 and
  all five expected provider types before installing its patch.
- Compatibility risk: a Bannerlord prefab-loader signature change, a provider
  rename, or a different primary DLL disables the repair.

## Failure behavior and diagnostics

Activation failures leave the protected sprite path unchanged. A failure while
transforming a node restores the original node and is logged once. Successful
replacement of each shell widget is also logged once per process in
`Logs/WorldEventsShellRepair.log`.

`Tests/Verify-WorldEventsShellRepair.ps1` checks hash gating, exact widget and
provider mappings, native target discovery, fail-open behavior, sidecar module
registration, runtime PNGs, and the unchanged protected hashes.
# Center alignment at 80% MapBar scale

The existing WidgetTemplate.LoadFrom prefix also clones the exact 1220x871
WorldEventsFrame containing WorldEventsFullBorderShell and sets its horizontal
offset to 0.8 UI units. It is now bottom-aligned at offset -131.2 rather than
center-aligned: 77.6 center-bar height + 1.6 bottom inset + 48 crown rise +
4 UI units of clear space. This keeps the window directly above the season
dial as viewport height changes, replacing the earlier center offsets.
This matches the scaled sundial center (295 minus half
the 588-unit center panel, multiplied by 0.8). No child
layout changes. The protected prefab remains untouched; the existing guarded
loader-boundary failure path restores the original node on error.

## Personal Chronicle readability and initial clan labels

The in-memory frame clone increases CharacterStoryBody and CharacterMilestoneBody
to font size 19 and PersonalChronicleSubtitle to 16. Existing scroll panels,
CoverChildren text sizing, margins, bindings, and commands remain unchanged.

An optional postfix on the hash-approved
CalendarWorldLedgerBehavior.GetCharacterMilestoneStory(Hero) corrects the two
recorded `Clan Playerland stands at tier ...` and `Clan Playerland is independent.`
phrases to that hero's current clan name for display. It does not mutate saved
events or clan data, and does not replace other historical clan names. If the
current name is unknown or still Playerland, it leaves the record unchanged.
Signature mismatch skips this optional repair; a runtime reflection failure
retains the original result and logs once. The constructor/finance labels are
not targeted. Verify-WorldEventsStoryReadability.ps1 checks the exact strings,
unchanged unrelated history/layout, and installation on the approved target.
In-game visual validation remains required for wrapping and scroll behavior.

## All-tab readability

The guarded frame clone applies a 16-point minimum to static ordinary text
throughout every tab, with at least 20-unit fixed line boxes. Larger heading
fonts stay unchanged. Decorative glyphs, compact P/C/A map symbols, and bound
map-legend scaling remain native. Fixed-height NotesText, SummaryText, and
BackgroundText fields become clipped, mouse-wheel-scrollable panels with
CoverChildren text bodies, retaining their original outer placement and data
bindings. Existing text IDs, commands, and protected on-disk XML are preserved.
This is a runtime UI-only adaptation, not a Core rebuild. The story readability
test checks the full frame's font floor, unchanged text/commands, and valid
scroll paths. Per-tab in-game wrapping and overlap still require visual review.

## W button hit areas

The MapBar center background now sets DoNotAcceptEvents=true. In Bannerlord
1.4.8 EventManager.CollectEnableWidgetsAt, this removes only the parent hit
candidate; children remain interactive unless DoNotPassEventsToChildren is set.
This prevents the late center panel from intercepting the W button's left edge.

The existing hash-gated prefab loader also clones only the exact legacy
WorldCalendarOpenOverlayToggle ButtonWidget (ExecuteClose, 34x31). Its replacement
matches the scaled visible W: 37.6x34.4, center X248.8, bottom inset11.2. The
old open-screen target overlapped only a narrow strip of the visible button.
Command, alpha, and protected on-disk prefab are retained. Unrelated ButtonWidget
nodes are ignored; the existing loader error path restores the original node.
Verify-MapBarWorldButtonHitArea.ps1 derives dimensions from the deployed-scale
MapBar, verifies nine boundary/interior samples and commands, and confirms the
passive background still permits child input. Actual in-game clicks require
confirmation after restart; geometric checks alone are not a live click test.
