using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.Engine;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    internal static class PoliticalBorderEditorState
    {
        private const float ConnectionSnapDistance = 1.5f;
        private const float MinimumPointDistance = 0.10f;
        private const float ActivePointerCrosshairRadius = 6f;
        private const float SculptPickDistance = 3f;
        private const float PartySurveySpeed = 12f;
        private const float FastPartySurveySpeed = 30f;
        private const float EditorPoliticalFillOpacity = 0.18f;
        private const uint BorderPreviewColor = 0xFFFF3030u;
        private const uint PointPreviewColor = 0xFFFFFF40u;
        private const uint NeutralFillColor = 0xFFA8A096u;
        private const uint AuthoredBorderColor = 0xFF000000u;
        // vertex_color_mat can visually lose zero-RGB geometry over the map's
        // forward overlay pass. Keep the persisted authored ribbon true black,
        // but publish every staged plan as an effectively black charcoal so it
        // remains visible. This is presentation only and never reaches XML.
        private const uint PlanningBorderColor = 0xFF181818u;
        private const uint CursorPreviewColor = PlanningBorderColor;
        private const uint CoastRoutePreviewColor = PlanningBorderColor;

        private static PoliticalBorderEditorDocument _document =
            new PoliticalBorderEditorDocument();
        private static readonly List<Vec2> ActivePoints = new List<Vec2>();
        private static readonly List<SculptNodeRef> SculptTargets =
            new List<SculptNodeRef>();
        private static EditorMode _mode = EditorMode.Border;
        private static bool _active;
        private static bool _activeRequiresClosure;
        private static BorderTool _borderTool = BorderTool.Line;
        private static float _brushRadius = 3f;
        private static string _borderStyle = "Solid";
        private static FieldInfo _mouseRayField;
        private static MethodInfo _cursorIntersectionMethod;
        private static bool _cursorProjectionFailed;
        private static bool _nativeClickHandledThisFrame;
        private static bool _gridVisible;
        private static bool _cameraNavigation;
        private static FillVisibilityMode _fillVisibility = FillVisibilityMode.AllZooms;
        private static SettlementVisibilityMode _settlementVisibility =
            SettlementVisibilityMode.Hidden;
        private static bool _hasCursorPreview;
        private static Vec2 _cursorPreviewPoint;
        private static EditorFaction _activeFaction;
        private static EditorFaction _previewFaction;
        private static Vec2 _lastFactionPreviewPoint;
        private static bool _hasFactionPreviewPoint;
        private static bool _sculptDragging;
        private static int _generatedDragIndex = -1;
        private static bool _editingGenerated;
        private static bool _editingGeneratedClosed;
        private static bool _hasSculptSelection;
        private static Vec2 _sculptSelectionPoint;
        private static bool _partyTraceRecording;
        private static bool _partySurveyVisualCaptured;
        private static Vec2 _partySurveyOriginalAdder;
        private static Vec2 _partySurveyPosition;
        private static FieldInfo _partyVisualAdderField;
        private static bool _timeModeCaptured;
        private static CampaignTimeControlMode _timeModeBeforeEditor;

        internal static bool IsActive { get { return _active; } }

        internal static void Initialize()
        {
            try
            {
                _document = PoliticalBorderEditorDocument.Load();
                PoliticalBorderEditorDiagnostics.Info("Editor document loaded: borders="
                    + _document.Borders.Count + "; fills=" + _document.Fills.Count + ".");
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor override file was rejected; an empty in-memory document is active.",
                    exception);
                _document = new PoliticalBorderEditorDocument();
            }
        }

        internal static void Tick(object mapScreen, float dt)
        {
            if (Input.IsKeyReleased(InputKey.F10))
            {
                SetActive(!_active);
            }
            if (!_active) return;

            PoliticalBorderTerrainSnapshot.Tick();

            PoliticalBorderEditorTopDownCamera.Apply(mapScreen);

            if (Input.IsKeyReleased(InputKey.Tab)) ToggleDrawingPanel();
            if (Input.IsKeyReleased(InputKey.Y)) TogglePartyTrace();
            if (_partyTraceRecording) UpdatePartySurvey(dt);
            if (Input.IsKeyReleased(InputKey.N)) ToggleCameraNavigation();
            if (_cameraNavigation)
            {
                _hasCursorPreview = false;
                RenderPreview(mapScreen);
                PoliticalBorderEditorUi.Refresh();
                _nativeClickHandledThisFrame = false;
                return;
            }

            if (Input.IsKeyReleased(InputKey.B)) SetMode(EditorMode.Border);
            if (Input.IsKeyReleased(InputKey.F)) SetMode(EditorMode.Fill);
            if (Input.IsKeyReleased(InputKey.L)) SetBorderTool(BorderTool.Line);
            if (Input.IsKeyReleased(InputKey.K)) SetBorderTool(BorderTool.Curve);
            if (Input.IsKeyReleased(InputKey.G)) SetBorderTool(BorderTool.Freeform);
            if (Input.IsKeyReleased(InputKey.J)) SetBorderTool(BorderTool.FollowCoast);
            if (Input.IsKeyReleased(InputKey.R)) SetBorderTool(BorderTool.Reference);
            if (Input.IsKeyReleased(InputKey.U)) SetBorderTool(BorderTool.Sculpt);
            if (Input.IsKeyReleased(InputKey.H)) SmoothActiveBorder();
            if (Input.IsKeyReleased(InputKey.T)) CycleBorderStyle();
            if (Input.IsKeyReleased(InputKey.PageUp)) ResizeBrush(1f);
            if (Input.IsKeyReleased(InputKey.PageDown)) ResizeBrush(-1f);
            if (Input.IsKeyReleased(InputKey.BackSpace)) UndoPoint();
            if (Input.IsKeyReleased(InputKey.Delete)) DeleteLastShape();
            if (Input.IsKeyReleased(InputKey.Enter)
                || Input.IsKeyReleased(InputKey.NumpadEnter)) CommitActive(false);
            if (Input.IsKeyDown(InputKey.LeftControl)
                && Input.IsKeyReleased(InputKey.S)) Save();
            bool pointerOverPanel = PoliticalBorderEditorUi.IsPointerOverPanel;
            // The focused Gauntlet panel owns pointer input, but the live map
            // ray remains useful as an always-on planning crosshair. Panel hits
            // are still excluded independently from every placement path.
            _hasCursorPreview = TryGetCursorMapPoint(
                mapScreen, out _cursorPreviewPoint);
            if (Input.IsKeyReleased(InputKey.LeftMouseButton))
            {
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor cursor release: projected=" + _hasCursorPreview
                    + "; screen=" + Input.MousePositionRanged
                    + "; map=" + _cursorPreviewPoint
                    + "; panelHit=" + pointerOverPanel + ".");
            }
            if (_borderTool == BorderTool.Sculpt && _mode == EditorMode.Border)
                UpdateSculptDrag(pointerOverPanel);
            if (_borderTool == BorderTool.Generated && _mode == EditorMode.Border)
                UpdateGeneratedEdit(pointerOverPanel);
            UpdateFactionPreview();
            if (Input.IsKeyDown(InputKey.LeftMouseButton) && !pointerOverPanel
                && (_mode == EditorMode.Fill || _borderTool == BorderTool.Freeform))
                SampleContinuousTool(mapScreen);
            if (Input.IsKeyReleased(InputKey.LeftMouseButton)
                && !_nativeClickHandledThisFrame && !pointerOverPanel
                && _mode == EditorMode.Border
                && _borderTool != BorderTool.Freeform
                && _borderTool != BorderTool.Sculpt
                && _borderTool != BorderTool.Generated)
            {
                Vec2 point;
                if (TryGetCursorMapPoint(mapScreen, out point))
                    AddBorderPoint(point);
            }
            RenderPreview(mapScreen);
            PoliticalBorderEditorUi.Refresh();
            _nativeClickHandledThisFrame = false;
        }

        internal static bool HandleMapClick(CampaignVec2 campaignPosition)
        {
            if (!_active) return false;
            if (_cameraNavigation) return false;
            if (PoliticalBorderEditorUi.IsPointerOverPanel) return true;
            Vec2 point = campaignPosition.ToVec2();
            if (!IsUsableMapPoint(point))
            {
                // The vertical editor camera can make Bannerlord's native map
                // click report (0,0). Do not suppress Tick's maintained-ray
                // fallback; it has the correct terrain-projected coordinate.
                _nativeClickHandledThisFrame = false;
                PoliticalBorderEditorDiagnostics.Info(
                    "Native map click produced an invalid coordinate; using the projected cursor fallback.");
                return true;
            }
            _nativeClickHandledThisFrame = true;
            if (_mode == EditorMode.Border)
            {
                if (_borderTool != BorderTool.Freeform
                    && _borderTool != BorderTool.Sculpt
                    && _borderTool != BorderTool.Generated) AddBorderPoint(point);
            }
            else AddBrushPoint(point);
            return true;
        }

        private static void AddBorderPoint(Vec2 point)
        {
            PoliticalBorderEditorDiagnostics.Info(
                "Border point request: tool=" + _borderTool + "; point="
                + point + "; existing=" + ActivePoints.Count + ".");
            if (_borderTool == BorderTool.Reference)
            {
                if (ActivePoints.Count == 0)
                {
                    Vec2 referencePoint;
                    if (!PoliticalBorderReferenceOverlay.TrySnap(point, out referencePoint))
                    {
                        Show("TRACE rejected: click closer to a red reference line.");
                        return;
                    }
                    point = referencePoint;
                }
                else
                {
                    List<Vec2> route;
                    string failure;
                    if (!PoliticalBorderReferenceOverlay.TryBuildRoute(
                            ActivePoints[ActivePoints.Count - 1], point,
                            out route, out failure))
                    {
                        Show("TRACE rejected: " + failure + ".");
                        return;
                    }
                    for (int index = 1; index < route.Count; index++)
                        if ((route[index] - ActivePoints[ActivePoints.Count - 1]).Length
                            >= MinimumPointDistance)
                            ActivePoints.Add(route[index]);
                    Show("Reference route added: " + route.Count + " projected points.");
                    return;
                }
            }
            if (ActivePoints.Count == 0 && !TryLockFaction(point)) return;
            Vec2 snapped;
            bool connected = TrySnapToBorderNetwork(point, out snapped);
            if (ActivePoints.Count == 0 && _document.Borders.Count > 0 && !connected)
            {
                if (!Input.IsKeyDown(InputKey.LeftShift)
                    && !Input.IsKeyDown(InputKey.RightShift))
                {
                    Show("Border rejected: start on an existing yellow node, or Shift+click to begin a new closed coast/lake loop.");
                    _activeFaction = null;
                    return;
                }
                _activeRequiresClosure = true;
            }
            if (connected) point = snapped;
            if (ActivePoints.Count > 0
                && (point - ActivePoints[ActivePoints.Count - 1]).Length < MinimumPointDistance)
                return;

            bool joinsExisting = ActivePoints.Count > 0 && connected;
            ActivePoints.Add(point);
            PoliticalBorderEditorDiagnostics.Info(
                "Border point accepted: count=" + ActivePoints.Count
                + "; point=" + point + ".");
            Show("Border point " + ActivePoints.Count + " added"
                + (connected ? " (snapped)." : "."));

            if (joinsExisting && ActivePoints.Count >= 2)
            {
                bool closed = Same(point, ActivePoints[0]);
                CommitActive(closed);
            }
        }

        private static void AddBrushPoint(Vec2 point)
        {
            if (ActivePoints.Count == 0 && !TryLockFaction(point)) return;
            if (ActivePoints.Count > 0
                && (point - ActivePoints[ActivePoints.Count - 1]).Length
                    < Math.Max(MinimumPointDistance, _brushRadius * 0.25f))
                return;
            ActivePoints.Add(point);
        }

        private static void CommitActive(bool forceClosed)
        {
            if (_mode == EditorMode.Border)
            {
                if (ActivePoints.Count < 2)
                {
                    Show("A border path needs at least two connected points.");
                    return;
                }
                bool closesAtStart = Same(ActivePoints[0],
                    ActivePoints[ActivePoints.Count - 1]);
                if (_activeRequiresClosure && !forceClosed && !closesAtStart)
                {
                    Show("This separate coast/lake path must close on its first yellow node.");
                    return;
                }
                EditorFaction faction = RequireActiveFaction();
                BorderPath path = new BorderPath
                {
                    Id = "border-" + (_document.Borders.Count + 1).ToString("D3"),
                    Closed = forceClosed || _editingGeneratedClosed || Same(ActivePoints[0],
                        ActivePoints[ActivePoints.Count - 1]),
                    FollowCoast = _borderTool == BorderTool.FollowCoast,
                    ReplacesGenerated = _editingGenerated,
                    Style = _borderStyle,
                    FactionId = faction.Id,
                    Color = AuthoredBorderColor
                };
                List<Vec2> committedPoints = _borderTool == BorderTool.Curve
                    ? PoliticalBorderEditorPathMath.BuildCurve(ActivePoints)
                    : new List<Vec2>(ActivePoints);
                path.Points.AddRange(committedPoints);
                if (path.Closed && path.Points.Count > 2
                    && Same(path.Points[0], path.Points[path.Points.Count - 1]))
                    path.Points.RemoveAt(path.Points.Count - 1);
                _document.Borders.Add(path);
                PoliticalBorderEditorSounds.PlayBorderPlaced();
                if (path.Closed && path.Points.Count >= 3)
                {
                    FillPolygon impliedFill = new FillPolygon
                    {
                        Id = "fill-" + (_document.Fills.Count + 1).ToString("D3"),
                        FactionId = path.FactionId,
                        Color = faction.Color
                    };
                    impliedFill.Points.AddRange(path.Points);
                    _document.Fills.Add(impliedFill);
                }
                ClearActive();
                Show("Border plan confirmed for " + faction.Name
                    + (path.Closed ? "; matching fill was added." : ".")
                    + " Ctrl+S saves and applies all plans.");
            }
            else
            {
                if (ActivePoints.Count < 1)
                {
                    Show("A fill brush stroke needs at least one sampled point.");
                    return;
                }
                EditorFaction faction = RequireActiveFaction();
                FillBrush brush = new FillBrush
                {
                    Id = "brush-" + (_document.Brushes.Count + 1).ToString("D3"),
                    FactionId = faction.Id,
                    Color = faction.Color,
                    Radius = _brushRadius
                };
                brush.Points.AddRange(ActivePoints);
                _document.Brushes.Add(brush);
                ClearActive();
                Show("Faction fill plan assigned to " + faction.Name
                    + ". Ctrl+S saves and applies all plans.");
            }
        }

        private static void UndoPoint()
        {
            if (ActivePoints.Count == 0)
            {
                Show("Nothing active to undo.");
                return;
            }
            ActivePoints.RemoveAt(ActivePoints.Count - 1);
            if (ActivePoints.Count == 0) _activeFaction = null;
            Show("Last active point removed.");
        }

        private static void DeleteLastShape()
        {
            ClearActive();
            if (_mode == EditorMode.Border && _document.Borders.Count > 0)
            {
                _document.Borders.RemoveAt(_document.Borders.Count - 1);
                Show("Last border path deleted.");
            }
            else if (_mode == EditorMode.Fill && _document.Brushes.Count > 0)
            {
                _document.Brushes.RemoveAt(_document.Brushes.Count - 1);
                Show("Last fill brush stroke deleted.");
            }
            else Show("No completed shape to delete.");
        }

        private static void Save()
        {
            try
            {
                _document.Save();
                RequestPoliticalRebuild();
                Show("Political plans saved. Renderer apply/rebuild requested.");
                PoliticalBorderEditorDiagnostics.Info("Editor document saved: borders="
                    + _document.Borders.Count + "; fills=" + _document.Fills.Count
                    + "; brushes=" + _document.Brushes.Count + ".");
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Political override save failed; the previous file remains active.",
                    exception);
                Show("Political override save failed; see PoliticalBorderEditor.log.");
            }
        }

        private static void RequestPoliticalRebuild()
        {
            Campaign campaign = Campaign.Current;
            if (campaign == null || campaign.CampaignBehaviorManager == null) return;
            foreach (CampaignBehaviorBase behavior in
                campaign.CampaignBehaviorManager.GetBehaviors<CampaignBehaviorBase>())
            {
                Type type = behavior.GetType();
                if (type.FullName != "TwelveMonthCalendar.CampaignKingdomBorderBehavior")
                    continue;
                FieldInfo dirty = type.GetField("_dirty",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo signature = type.GetField("_lastOwnershipSignature",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (dirty == null)
                    throw new MissingFieldException(type.FullName, "_dirty");
                dirty.SetValue(behavior, true);
                if (signature != null) signature.SetValue(behavior, null);
                return;
            }
        }

        private static bool TrySnapToBorderNetwork(Vec2 point, out Vec2 snapped)
        {
            snapped = point;
            float bestSquared = ConnectionSnapDistance * ConnectionSnapDistance;
            bool found = false;
            bool canCloseAtStart = ActivePoints.Count >= 3
                && (ActivePoints[ActivePoints.Count - 1] - ActivePoints[0]).Length
                    > ConnectionSnapDistance * 2f;
            foreach (BorderPath path in _document.Borders)
            foreach (Vec2 candidate in path.Points)
            {
                if (ActivePoints.Count > 0 && Same(candidate, ActivePoints[0])
                    && !canCloseAtStart) continue;
                float squared = (candidate - point).LengthSquared;
                if (squared >= bestSquared) continue;
                bestSquared = squared;
                snapped = candidate;
                found = true;
            }
            if (canCloseAtStart)
            {
                Vec2 candidate = ActivePoints[0];
                float squared = (candidate - point).LengthSquared;
                if (squared < bestSquared)
                {
                    snapped = candidate;
                    found = true;
                }
            }
            return found;
        }

        private static void SetMode(EditorMode mode)
        {
            ClearActive();
            _mode = mode;
            Show("Editor mode: " + mode
                + " | faction will be detected from the first map point.");
        }

        private static void SetBorderTool(BorderTool tool)
        {
            ClearActive();
            _mode = EditorMode.Border;
            _borderTool = tool;
            if (tool == BorderTool.Reference)
                PoliticalBorderReferenceOverlay.SetVisible(true);
            PoliticalBorderGeneratedOverlay.SetVisible(tool == BorderTool.Generated);
            Show("Border tool: " + tool + ".");
        }

        private static void ResizeBrush(float change)
        {
            _brushRadius = Math.Max(0.10f, Math.Min(20f, _brushRadius + change));
            Show("Fill brush radius: " + _brushRadius.ToString("F2", CultureInfo.InvariantCulture) + ".");
        }

        private static void ResizeAllBorders(float change)
        {
            _document.BorderWidthScale = Math.Max(0.5f,
                Math.Min(2.5f, _document.BorderWidthScale + change));
            Show("Global border width: "
                + _document.BorderWidthScale.ToString("F2", CultureInfo.InvariantCulture)
                + "x. Save to rebuild every border.");
        }

        private static void CycleBorderStyle()
        {
            _borderStyle = _borderStyle == "Solid" ? "Dashed"
                : _borderStyle == "Dashed" ? "Double" : "Solid";
            Show("Authored border style: " + _borderStyle + ".");
        }

        private static void ToggleCameraNavigation()
        {
            _cameraNavigation = !_cameraNavigation;
            PoliticalBorderEditorUi.SetCameraNavigation(_cameraNavigation);
            Show(_cameraNavigation
                ? "Camera navigation ON: map input is no longer drawing."
                : "Camera navigation OFF: map editing resumed.");
        }

        private static void SampleContinuousTool(object mapScreen)
        {
            Vec2 point;
            if (!TryGetCursorMapPoint(mapScreen, out point)) return;
            if (_mode == EditorMode.Fill) AddBrushPoint(point);
            else AddBorderPoint(point);
        }

        private static void UpdateSculptDrag(bool pointerOverPanel)
        {
            if (pointerOverPanel || !_hasCursorPreview)
            {
                if (Input.IsKeyReleased(InputKey.LeftMouseButton)) EndSculptDrag();
                return;
            }
            if (Input.IsKeyPressed(InputKey.LeftMouseButton))
                BeginSculptDrag(_cursorPreviewPoint);
            if (_sculptDragging && Input.IsKeyDown(InputKey.LeftMouseButton))
            {
                foreach (SculptNodeRef target in SculptTargets)
                    target.Points[target.Index] = _cursorPreviewPoint;
                _sculptSelectionPoint = _cursorPreviewPoint;
                _hasSculptSelection = true;
            }
            if (_sculptDragging && Input.IsKeyReleased(InputKey.LeftMouseButton))
                EndSculptDrag();
        }

        private static void UpdateGeneratedEdit(bool pointerOverPanel)
        {
            if (pointerOverPanel || !_hasCursorPreview) return;
            if (Input.IsKeyPressed(InputKey.LeftMouseButton))
            {
                if (ActivePoints.Count == 0)
                {
                    GeneratedBorderChain chain;
                    if (!PoliticalBorderGeneratedOverlay.TrySelect(_cursorPreviewPoint, out chain))
                    { Show("EDIT GENERATED: click closer to an orange generated plan line."); return; }
                    if (!TryLockFaction(chain.Points[0])) return;
                    ActivePoints.AddRange(chain.Points);
                    _activeRequiresClosure = false;
                    _editingGenerated = true;
                    _editingGeneratedClosed = chain.Closed;
                    Show("Generated chain promoted to an editable plan: " + chain.Id + ".");
                    return;
                }
                float best = SculptPickDistance * SculptPickDistance;
                for (int index = 0; index < ActivePoints.Count; index++)
                {
                    float distance = (ActivePoints[index] - _cursorPreviewPoint).LengthSquared;
                    if (distance < best) { best = distance; _generatedDragIndex = index; }
                }
                if (_generatedDragIndex >= 0)
                {
                    Show("Generated anchor selected; drag and release to stage its new position.");
                    PoliticalBorderEditorDiagnostics.Info(
                        "Generated anchor drag started: index=" + _generatedDragIndex
                        + "; point=" + ActivePoints[_generatedDragIndex] + ".");
                }
                else if (TryInsertGeneratedAnchor(_cursorPreviewPoint,
                    out _generatedDragIndex))
                {
                    Show("Generated segment selected; drag to bend it with a new control point.");
                    PoliticalBorderEditorDiagnostics.Info(
                        "Generated segment manipulation started: insertedIndex="
                        + _generatedDragIndex + "; point="
                        + ActivePoints[_generatedDragIndex] + ".");
                }
                else Show("EDIT GENERATED: drag a circle anchor or drag directly on the plan line.");
            }
            if (_generatedDragIndex >= 0 && Input.IsKeyDown(InputKey.LeftMouseButton))
                ActivePoints[_generatedDragIndex] = _cursorPreviewPoint;
            if (Input.IsKeyReleased(InputKey.LeftMouseButton) && _generatedDragIndex >= 0)
            {
                PoliticalBorderEditorDiagnostics.Info(
                    "Generated anchor drag completed: index=" + _generatedDragIndex
                    + "; point=" + ActivePoints[_generatedDragIndex] + ".");
                _generatedDragIndex = -1;
                Show("Generated-border control point change staged.");
            }
        }

        private static bool TryInsertGeneratedAnchor(Vec2 cursor, out int insertedIndex)
        {
            insertedIndex = -1;
            if (ActivePoints.Count < 2
                || ActivePoints.Count >= PoliticalBorderEditorDocument.MaximumPointsPerShape)
                return false;
            float best = SculptPickDistance * SculptPickDistance;
            Vec2 closest = Vec2.Zero;
            int edgeCount = _editingGeneratedClosed
                ? ActivePoints.Count : ActivePoints.Count - 1;
            for (int edge = 0; edge < edgeCount; edge++)
            {
                Vec2 first = ActivePoints[edge];
                Vec2 second = ActivePoints[(edge + 1) % ActivePoints.Count];
                Vec2 delta = second - first;
                float denominator = delta.LengthSquared;
                if (denominator <= 0.000001f) continue;
                float amount = Math.Max(0f, Math.Min(1f,
                    Vec2.DotProduct(cursor - first, delta) / denominator));
                Vec2 candidate = first + delta * amount;
                float distance = (cursor - candidate).LengthSquared;
                if (distance >= best) continue;
                best = distance;
                closest = candidate;
                insertedIndex = edge + 1;
            }
            if (insertedIndex < 0) return false;
            ActivePoints.Insert(insertedIndex, closest);
            return true;
        }

        private static void TogglePartyTrace()
        {
            if (_partyTraceRecording)
            {
                _partyTraceRecording = false;
                RestorePartySurveyVisual();
                SetPlanningTime();
                _cameraNavigation = false;
                PoliticalBorderEditorUi.Show();
                PoliticalBorderEditorUi.SetCameraNavigation(false);
                Show("MOUNTAIN SURVEY stopped with " + ActivePoints.Count
                    + " samples. Sculpt or commit the black plan.");
                return;
            }
            MobileParty party = MobileParty.MainParty;
            if (party == null)
            {
                Show("PARTY TRACE unavailable: the main campaign party is missing.");
                return;
            }
            Vec2 originalAdder = party.ArmyPositionAdder;
            Vec2 point = new Vec2(party.Position.X, party.Position.Y)
                + originalAdder;
            ClearActive();
            _mode = EditorMode.Border;
            _borderTool = BorderTool.PartyTrace;
            if (!TryLockFaction(point)) return;
            _partySurveyOriginalAdder = originalAdder;
            _partySurveyPosition = point;
            _partySurveyVisualCaptured = true;
            ActivePoints.Add(point);
            _partyTraceRecording = true;
            SetPlanningTime();
            _cameraNavigation = true;
            PoliticalBorderEditorUi.SetCameraNavigation(true);
            PoliticalBorderEditorUi.HideForPartyTrace();
            Show("MOUNTAIN SURVEY recording while paused. Move with WASD; hold Shift for fast travel; press Y to stop.");
        }

        private static void ToggleDrawingPanel()
        {
            if (_partyTraceRecording)
            {
                Show("Press Y to stop PARTY TRACE before restoring the panel.");
                return;
            }
            if (PoliticalBorderEditorUi.IsOpen)
            {
                if (_borderTool == BorderTool.Reference)
                    PoliticalBorderReferenceOverlay.SetVisible(true);
                PoliticalBorderEditorUi.HideForDrawing();
                Show("PLANNING MODE READY: panel hidden; drawing remains active. Press Tab to restore it.");
            }
            else
            {
                PoliticalBorderEditorUi.Show();
                PoliticalBorderEditorUi.SetCameraNavigation(_cameraNavigation);
                Show("Editor panel restored.");
            }
        }

        private static void UpdatePartySurvey(float dt)
        {
            MobileParty party = MobileParty.MainParty;
            if (party == null || ActivePoints.Count >=
                PoliticalBorderEditorDocument.MaximumPointsPerShape)
            {
                TogglePartyTrace();
                return;
            }
            Vec2 direction = Vec2.Zero;
            if (Input.IsKeyDown(InputKey.W)) direction.y += 1f;
            if (Input.IsKeyDown(InputKey.S)) direction.y -= 1f;
            if (Input.IsKeyDown(InputKey.D)) direction.x += 1f;
            if (Input.IsKeyDown(InputKey.A)) direction.x -= 1f;
            if (direction.Normalize() <= 0.001f) return;
            bool fast = Input.IsKeyDown(InputKey.LeftShift)
                || Input.IsKeyDown(InputKey.RightShift);
            float frameSeconds = Math.Max(0f, Math.Min(0.05f, dt));
            _partySurveyPosition += direction
                * (fast ? FastPartySurveySpeed : PartySurveySpeed) * frameSeconds;
            try
            {
                Vec2 logicalPosition = new Vec2(party.Position.X, party.Position.Y);
                SetPartyVisualAdder(party, _partySurveyPosition - logicalPosition);
                if (party.Party != null) party.Party.SetVisualAsDirty();
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Paused mountain-survey visual movement failed; the original party visual was restored.",
                    exception);
                TogglePartyTrace();
                return;
            }
            if ((_partySurveyPosition - ActivePoints[ActivePoints.Count - 1]).Length
                >= 0.25f)
                ActivePoints.Add(_partySurveyPosition);
        }

        private static void RestorePartySurveyVisual()
        {
            if (!_partySurveyVisualCaptured) return;
            try
            {
                MobileParty party = MobileParty.MainParty;
                if (party == null) return;
                SetPartyVisualAdder(party, _partySurveyOriginalAdder);
                if (party.Party != null) party.Party.SetVisualAsDirty();
                _partySurveyVisualCaptured = false;
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "The editor could not restore the main-party visual offset after mountain surveying.",
                    exception);
            }
        }

        private static void SetPartyVisualAdder(MobileParty party, Vec2 value)
        {
            if (_partyVisualAdderField == null)
                _partyVisualAdderField = typeof(MobileParty).GetField(
                    "<ArmyPositionAdder>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (_partyVisualAdderField == null)
                throw new MissingFieldException(
                    typeof(MobileParty).FullName, "<ArmyPositionAdder>k__BackingField");
            _partyVisualAdderField.SetValue(party, value);
        }

        private static void SetPlanningTime()
        {
            try
            {
                Campaign campaign = Campaign.Current;
                if (campaign == null) return;
                campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor could not change campaign time; drawing remains active.",
                    exception);
            }
        }

        private static void BeginSculptDrag(Vec2 cursor)
        {
            SculptTargets.Clear();
            Vec2 selected = Vec2.Zero;
            float bestSquared = SculptPickDistance * SculptPickDistance;
            foreach (BorderPath path in _document.Borders)
            {
                foreach (Vec2 point in path.Points)
                {
                    float squared = (point - cursor).LengthSquared;
                    if (squared >= bestSquared) continue;
                    bestSquared = squared;
                    selected = point;
                }
            }
            if (bestSquared >= SculptPickDistance * SculptPickDistance)
            {
                Show("SCULPT: click closer to a visible border node.");
                return;
            }
            foreach (BorderPath path in _document.Borders)
                CollectSculptTargets(path.Points, selected);
            foreach (FillPolygon polygon in _document.Fills)
                CollectSculptTargets(polygon.Points, selected);
            _sculptDragging = SculptTargets.Count > 0;
            _hasSculptSelection = _sculptDragging;
            _sculptSelectionPoint = selected;
            if (_sculptDragging)
                Show("SCULPT node selected; drag to reshape connected border and fill points.");
        }

        private static void CollectSculptTargets(List<Vec2> points, Vec2 selected)
        {
            for (int index = 0; index < points.Count; index++)
                if (Same(points[index], selected))
                    SculptTargets.Add(new SculptNodeRef(points, index));
        }

        private static void EndSculptDrag()
        {
            if (_sculptDragging)
                Show("SCULPT change staged. Save + Rebuild to apply it.");
            _sculptDragging = false;
            SculptTargets.Clear();
        }

        private static bool TryGetCursorMapPoint(object mapScreen, out Vec2 point)
        {
            point = Vec2.Zero;
            if (mapScreen == null || _cursorProjectionFailed) return false;
            try
            {
                if (_mouseRayField == null)
                    _mouseRayField = FindInstanceField(
                        mapScreen.GetType(), "_mouseRay");
                if (_cursorIntersectionMethod == null)
                    _cursorIntersectionMethod = FindInstanceMethod(
                        mapScreen.GetType(), "GetCursorIntersectionPoint");
                if (_cursorIntersectionMethod == null)
                    throw new MissingMemberException(mapScreen.GetType().FullName,
                        "GetCursorIntersectionPoint");
                Vec3 rayOrigin;
                Vec3 rayEnd;
                bool hasRay = false;
                if (_mouseRayField != null)
                {
                    Ray ray = (Ray)_mouseRayField.GetValue(mapScreen);
                    rayOrigin = ray.Origin;
                    rayEnd = ray.EndPoint;
                    hasRay = rayOrigin.IsValid && rayEnd.IsValid
                        && (rayEnd - rayOrigin).Length > 0.01f;
                }
                else
                {
                    rayOrigin = Vec3.Zero;
                    rayEnd = Vec3.Zero;
                }
                if (!hasRay)
                    hasRay = TryCreateLiveCursorRay(
                        mapScreen, out rayOrigin, out rayEnd);
                if (!hasRay)
                    throw new MissingMemberException(mapScreen.GetType().FullName,
                        "MapCameraView.Camera/_mouseRay");
                Vec3 originalRayOrigin = rayOrigin;
                Vec3 originalRayEnd = rayEnd;
                object[] arguments =
                {
                    rayOrigin, rayEnd, 0f, new Vec3(),
                    new PathFaceRecord(), false, (BodyFlags)0
                };
                _cursorIntersectionMethod.Invoke(mapScreen, arguments);
                Vec3 intersection = (Vec3)arguments[3];
                if (IsUsableMapIntersection(intersection))
                {
                    point = intersection.AsVec2;
                    return true;
                }
                return TryProjectRayToMapPlane(
                    originalRayOrigin, originalRayEnd, out point);
            }
            catch (Exception exception)
            {
                // Map cursor projection is a reflection/native boundary. Disable
                // drag sampling safely while preserving click-based line/curve.
                _cursorProjectionFailed = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Continuous freeform/brush cursor projection failed; line and curve tools remain available.",
                    exception);
                Show("Freeform/brush cursor projection unavailable; see editor log.");
                return false;
            }
        }

        private static bool TryCreateLiveCursorRay(
            object mapScreen,
            out Vec3 origin,
            out Vec3 end)
        {
            origin = Vec3.Zero;
            end = Vec3.Zero;
            PropertyInfo viewProperty = FindInstanceProperty(
                mapScreen.GetType(), "MapCameraView");
            object view = viewProperty == null
                ? null : viewProperty.GetValue(mapScreen, null);
            PropertyInfo cameraProperty = view == null ? null
                : FindInstanceProperty(view.GetType(), "Camera");
            Camera camera = cameraProperty == null ? null
                : cameraProperty.GetValue(view, null) as Camera;
            if (camera == null) return false;
            camera.ScreenSpaceRayProjection(
                Input.MousePositionRanged, ref origin, ref end);
            return true;
        }

        private static bool IsUsableMapIntersection(Vec3 intersection)
        {
            return intersection.IsValid
                && !float.IsNaN(intersection.x)
                && !float.IsNaN(intersection.y)
                && IsUsableMapPoint(intersection.AsVec2);
        }

        private static bool IsUsableMapPoint(Vec2 point)
        {
            return !float.IsNaN(point.x) && !float.IsNaN(point.y)
                && Math.Abs(point.x) + Math.Abs(point.y) > 1f;
        }

        private static bool TryProjectRayToMapPlane(
            Vec3 origin,
            Vec3 end,
            out Vec2 point)
        {
            point = Vec2.Zero;
            Vec3 direction = end - origin;
            if (!origin.IsValid || !end.IsValid
                || Math.Abs(direction.z) < 0.0001f) return false;
            float amount = -origin.z / direction.z;
            if (amount < 0f || amount > 1f) return false;
            Vec3 projected = origin + direction * amount;
            if (!IsUsableMapIntersection(projected)) return false;
            point = projected.AsVec2;
            return true;
        }

        private static FieldInfo FindInstanceField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            return null;
        }

        private static MethodInfo FindInstanceMethod(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (method != null) return method;
            }
            return null;
        }

        private static PropertyInfo FindInstanceProperty(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        private static void SmoothActiveBorder()
        {
            if (_mode != EditorMode.Border || ActivePoints.Count < 3)
            {
                Show("Smooth needs an active border with at least three points.");
                return;
            }
            bool closed = Same(ActivePoints[0], ActivePoints[ActivePoints.Count - 1]);
            List<Vec2> smoothed = new List<Vec2>(ActivePoints);
            for (int pass = 0; pass < 2; pass++)
                smoothed = SmoothOnce(smoothed, closed);
            ActivePoints.Clear();
            ActivePoints.AddRange(smoothed);
            Show("Active border smoothed; connected endpoints were preserved.");
        }

        private static List<Vec2> SmoothOnce(IList<Vec2> points, bool closed)
        {
            List<Vec2> result = new List<Vec2>(points.Count * 2);
            int count = closed && Same(points[0], points[points.Count - 1])
                ? points.Count - 1 : points.Count;
            if (!closed) result.Add(points[0]);
            int edgeCount = closed ? count : count - 1;
            for (int index = 0; index < edgeCount; index++)
            {
                Vec2 first = points[index];
                Vec2 second = points[(index + 1) % count];
                result.Add((first * 0.75f) + (second * 0.25f));
                result.Add((first * 0.25f) + (second * 0.75f));
            }
            if (!closed) result.Add(points[count - 1]);
            else result.Add(result[0]);
            return result;
        }

        private static bool TryLockFaction(Vec2 point)
        {
            _activeFaction = InferFaction(point);
            if (_activeFaction == null)
            {
                Show("No owned town, castle, or village could determine this faction.");
                return false;
            }
            Show("Faction detected: " + _activeFaction.Name + ".");
            return true;
        }

        private static EditorFaction InferFaction(Vec2 point)
        {
            if (Campaign.Current == null || Settlement.All == null) return null;
            Settlement nearest = null;
            float bestSquared = float.MaxValue;
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null
                    || (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage)
                    || settlement.OwnerClan == null) continue;
                Vec2 position = new Vec2(settlement.Position.X, settlement.Position.Y);
                float squared = (position - point).LengthSquared;
                if (squared >= bestSquared) continue;
                bestSquared = squared;
                nearest = settlement;
            }
            if (nearest == null) return null;
            Clan clan = nearest.OwnerClan;
            Kingdom kingdom = clan.Kingdom;
            return kingdom != null
                ? new EditorFaction("kingdom:" + kingdom.StringId,
                    kingdom.Name.ToString(), kingdom.PrimaryBannerColor)
                : new EditorFaction("clan:" + clan.StringId,
                    clan.Name.ToString(), clan.Color);
        }

        private static EditorFaction RequireActiveFaction()
        {
            if (_activeFaction != null) return _activeFaction;
            if (ActivePoints.Count > 0) _activeFaction = InferFaction(ActivePoints[0]);
            if (_activeFaction == null)
                throw new InvalidOperationException(
                    "An active border or fill stroke has no inferred faction.");
            return _activeFaction;
        }

        private static void UpdateFactionPreview()
        {
            if (!_hasCursorPreview || ActivePoints.Count > 0) return;
            if (_hasFactionPreviewPoint
                && (_cursorPreviewPoint - _lastFactionPreviewPoint).LengthSquared < 0.25f)
                return;
            _previewFaction = InferFaction(_cursorPreviewPoint);
            _lastFactionPreviewPoint = _cursorPreviewPoint;
            _hasFactionPreviewPoint = true;
        }

        private static void RenderPreview(object mapScreen)
        {
            PoliticalBorderEditorScenePreview.BeginFrame(mapScreen);
            try
            {
            PoliticalBorderReferenceOverlay.Render();
            PoliticalBorderGeneratedOverlay.Render();
            if (_gridVisible) PoliticalBorderEditorGrid.Render();
            foreach (BorderPath path in _document.Borders)
            {
                PoliticalBorderEditorPreview.RenderDottedPath(
                    path.Points, path.Closed, PlanningBorderColor);
                if (_borderTool == BorderTool.Sculpt)
                    foreach (Vec2 point in path.Points)
                        PoliticalBorderEditorPreview.RenderMarker(
                            point, 0.38f, PointPreviewColor);
            }
            foreach (FillPolygon polygon in _document.Fills)
                PoliticalBorderEditorPreview.RenderPath(
                    polygon.Points, true, NeutralFillColor, false, 3);
            foreach (FillBrush brush in _document.Brushes)
                PoliticalBorderEditorPreview.RenderBrush(
                    brush.Points, brush.Radius, brush.Color);
            if (_mode == EditorMode.Border)
            {
                PoliticalBorderEditorPreview.RenderDottedPath(
                    ActivePoints, false, PlanningBorderColor);
                foreach (Vec2 anchor in ActivePoints)
                    PoliticalBorderEditorPreview.RenderAnchor(
                        anchor, PlanningBorderColor);
            }
            else
                PoliticalBorderEditorPreview.RenderPath(
                    ActivePoints, false, NeutralFillColor, true, 6);
            if (_mode == EditorMode.Fill)
                PoliticalBorderEditorPreview.RenderBrush(
                    ActivePoints, _brushRadius, NeutralFillColor);
            RenderCursorPreview();
            if (_borderTool == BorderTool.Sculpt && _hasSculptSelection)
            {
                PoliticalBorderEditorPreview.RenderMarker(
                    _sculptSelectionPoint, 0.72f, CoastRoutePreviewColor);
                PoliticalBorderEditorPreview.RenderCrosshair(
                    _sculptSelectionPoint, 1.2f, CoastRoutePreviewColor, 6);
            }

            string status = "AOC BORDER EDITOR | " + _mode
                + (_mode == EditorMode.Border ? "/" + _borderTool : "/Brush")
                + (_mode == EditorMode.Border ? "/" + _borderStyle : string.Empty)
                + " | active points " + ActivePoints.Count
                + " | borders " + _document.Borders.Count
                + " | fills " + _document.Fills.Count
                + " | brushes " + _document.Brushes.Count;
            if (_mode == EditorMode.Fill) status += " | radius "
                + _brushRadius.ToString("F1", CultureInfo.InvariantCulture);
            EditorFaction displayedFaction = _activeFaction ?? _previewFaction;
            status += " | detected faction "
                + (displayedFaction == null ? "none" : displayedFaction.Name);
            TaleWorlds.Library.Debug.RenderDebugText(
                24f, 72f, status, 0xFFFFFFFFu, 0f);
            }
            finally
            {
                PoliticalBorderEditorScenePreview.EndFrame();
            }
        }

        private static void RenderCursorPreview()
        {
            if (!_hasCursorPreview) return;
            bool pointerActive = Input.IsKeyDown(InputKey.LeftMouseButton);
            float crosshairRadius = pointerActive
                ? ActivePointerCrosshairRadius : 4f;
            int crosshairThickness = pointerActive ? 10 : 6;
            if (_mode == EditorMode.Fill)
            {
                PoliticalBorderEditorPreview.RenderTerrainRing(
                    _cursorPreviewPoint, _brushRadius,
                    NeutralFillColor, 32);
                PoliticalBorderEditorPreview.RenderCrosshair(_cursorPreviewPoint,
                    pointerActive ? Math.Max(crosshairRadius, _brushRadius * 0.2f)
                        : Math.Max(0.5f, _brushRadius * 0.15f),
                    CursorPreviewColor, crosshairThickness);
                return;
            }

            uint color = _borderTool == BorderTool.FollowCoast
                ? CoastRoutePreviewColor : CursorPreviewColor;
            PoliticalBorderEditorPreview.RenderMarker(
                _cursorPreviewPoint, 0.9f, color);
            PoliticalBorderEditorPreview.RenderCrosshair(
                _cursorPreviewPoint, crosshairRadius, color,
                crosshairThickness);
            if (ActivePoints.Count == 0) return;

            if (_borderTool == BorderTool.Reference)
            {
                IList<Vec2> referenceRoute;
                if (PoliticalBorderReferenceOverlay.TryGetPreviewRoute(
                        ActivePoints[ActivePoints.Count - 1], _cursorPreviewPoint,
                        out referenceRoute))
                    PoliticalBorderEditorPreview.RenderDottedPath(
                        referenceRoute, false, PlanningBorderColor);
                return;
            }

            List<Vec2> preview = new List<Vec2>(ActivePoints);
            preview.Add(_cursorPreviewPoint);
            if (_borderTool == BorderTool.Curve)
                preview = PoliticalBorderEditorPathMath.BuildCurve(preview);
            PoliticalBorderEditorPreview.RenderDottedPath(
                preview, false, PlanningBorderColor);
        }

        private static bool Same(Vec2 first, Vec2 second)
        {
            return (first - second).Length <= 0.001f;
        }

        private static void ClearActive()
        {
            ActivePoints.Clear();
            _activeRequiresClosure = false;
            _activeFaction = null;
            _editingGenerated = false;
            _editingGeneratedClosed = false;
            _generatedDragIndex = -1;
        }

        private static void Show(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        internal static void SetActive(bool active)
        {
            if (_active == active) return;
            if (active && Campaign.Current != null)
            {
                _timeModeBeforeEditor = Campaign.Current.TimeControlMode;
                _timeModeCaptured = true;
            }
            _active = active;
            if (!active)
            {
                _partyTraceRecording = false;
                RestorePartySurveyVisual();
            }
            ClearActive();
            if (active)
            {
                // F10 always opens in fixed north-up 2D drawing mode. Camera
                // navigation remains an explicit temporary toggle.
                _cameraNavigation = false;
                PoliticalBorderEditorTopDownCamera.BeginSession();
                PoliticalBorderEditorScenePreview.BeginSession();
                PoliticalBorderGeneratedOverlay.BeginSession();
                PoliticalBorderTerrainSnapshot.Begin();
                SetPlanningTime();
                PoliticalBorderEditorUi.Show();
            }
            else
            {
                PoliticalBorderEditorTopDownCamera.Restore();
                PoliticalBorderEditorScenePreview.Clear();
                PoliticalBorderEditorGrid.ClearSceneEntity();
                PoliticalBorderReferenceOverlay.ClearSceneEntity();
                PoliticalBorderGeneratedOverlay.ClearEntities();
                PoliticalBorderEditorUi.CloseFromState();
                if (_timeModeCaptured && Campaign.Current != null)
                {
                    try { Campaign.Current.TimeControlMode = _timeModeBeforeEditor; }
                    catch (Exception exception)
                    {
                        PoliticalBorderEditorDiagnostics.Error(
                            "Editor could not restore the prior campaign time mode.",
                            exception);
                    }
                }
                _timeModeCaptured = false;
            }
            Show(active
                ? "Political Border Editor ON | use the panel or keyboard shortcuts"
                : "Political Border Editor OFF");
            PoliticalBorderEditorDiagnostics.Info("Editor active=" + active + ".");
        }

        internal static void UiSelectLine() { SetBorderTool(BorderTool.Line); }
        internal static void UiSelectCurve() { SetBorderTool(BorderTool.Curve); }
        internal static void UiSelectFollowCoast() { SetBorderTool(BorderTool.FollowCoast); }
        internal static void UiSelectReference() { SetBorderTool(BorderTool.Reference); }
        internal static void UiSelectFreeform() { SetBorderTool(BorderTool.Freeform); }
        internal static void UiSelectSculpt() { SetBorderTool(BorderTool.Sculpt); }
        internal static void UiSelectGenerated() { SetBorderTool(BorderTool.Generated); }
        internal static void UiTogglePartyTrace() { TogglePartyTrace(); }
        internal static void UiToggleDrawingPanel() { ToggleDrawingPanel(); }
        internal static void UiSelectFillBrush() { SetMode(EditorMode.Fill); }
        internal static void UiSmooth() { SmoothActiveBorder(); }
        internal static void UiCycleBorderStyle() { CycleBorderStyle(); }
        internal static void UiResizeBrush(float change) { ResizeBrush(change); }
        internal static void UiUndo() { UndoPoint(); }
        internal static void UiDelete() { DeleteLastShape(); }
        internal static void UiCommit() { CommitActive(false); }
        internal static void UiSave() { Save(); }
        internal static void UiToggleGrid()
        {
            _gridVisible = !_gridVisible;
            PoliticalBorderEditorGrid.SetVisible(_gridVisible);
            Show("100-square-mile grid: " + (_gridVisible ? "visible" : "hidden") + ".");
        }
        internal static void UiToggleReferenceOverlay()
        {
            PoliticalBorderReferenceOverlay.Toggle();
            Show(PoliticalBorderReferenceOverlay.StatusText + ".");
        }
        internal static void UiToggleCameraNavigation() { ToggleCameraNavigation(); }
        internal static void UiCycleSettlementVisibility()
        {
            _settlementVisibility = (SettlementVisibilityMode)
                (((int)_settlementVisibility + 1) % 4);
            Show("Editor settlement labels: " + UiSettlementVisibilityText + ".");
        }
        internal static void UiToggleCloseZoomFill()
        {
            _fillVisibility = (FillVisibilityMode)(((int)_fillVisibility + 1) % 3);
            Show(UiCloseZoomFillText + ".");
        }
        internal static void UiResizeAllBorders(float change) { ResizeAllBorders(change); }
        internal static string UiToolText
        { get { return "TOOL: " + (_mode == EditorMode.Fill ? "FACTION FILL BRUSH" : _borderTool.ToString().ToUpperInvariant()); } }
        internal static string UiFactionText
        {
            get
            {
                EditorFaction faction = _activeFaction ?? _previewFaction;
                return faction == null ? "NO FACTION DETECTED" : faction.Name;
            }
        }
        internal static string UiBrushText
        { get { return _brushRadius.ToString("F2", CultureInfo.InvariantCulture); } }
        internal static string UiCountsText
        {
            get
            {
                return "Active " + ActivePoints.Count + "  |  Plans "
                    + _document.Borders.Count + "  |  Fill strokes "
                    + (_document.Fills.Count + _document.Brushes.Count);
            }
        }
        internal static string UiGeneratedCoastsText
        { get { return PoliticalBorderGeneratedOverlay.StatusText; } }
        internal static string UiGridText
        { get { return _gridVisible ? "100 SQ MI GRID: VISIBLE" : "100 SQ MI GRID: HIDDEN"; } }
        internal static string UiReferenceText
        { get { return PoliticalBorderReferenceOverlay.StatusText; } }
        internal static string UiCameraText
        { get { return _cameraNavigation ? "CAMERA: NAVIGATE" : "CAMERA: 2D EDITING"; } }
        internal static string UiSettlementVisibilityText
        {
            get
            {
                switch (_settlementVisibility)
                {
                    case SettlementVisibilityMode.All: return "SETTLEMENTS: ALL";
                    case SettlementVisibilityMode.TownsAndCastles:
                        return "SETTLEMENTS: TOWNS + CASTLES";
                    case SettlementVisibilityMode.TownsOnly:
                        return "SETTLEMENTS: TOWNS ONLY";
                    default: return "SETTLEMENTS: HIDDEN";
                }
            }
        }
        internal static string UiCloseZoomFillText
        {
            get
            {
                if (_fillVisibility == FillVisibilityMode.Hidden) return "POLITICAL FILL: HIDDEN";
                return _fillVisibility == FillVisibilityMode.AllZooms
                    ? "POLITICAL FILL: VISIBLE ALL ZOOMS"
                    : "POLITICAL FILL: OVERVIEW ONLY";
            }
        }
        internal static string UiBorderWidthText
        { get { return "WIDTH " + _document.BorderWidthScale.ToString("F2", CultureInfo.InvariantCulture) + "x"; } }
        internal static string UiBorderStyleText
        { get { return "STYLE: " + _borderStyle.ToUpperInvariant(); } }

        internal static void ApplySettlementVisibility(
            ref bool visible,
            bool isTown,
            bool isCastle,
            bool isVillage)
        {
            if (!_active) return;
            switch (_settlementVisibility)
            {
                case SettlementVisibilityMode.All:
                    visible = isTown || isCastle || isVillage;
                    break;
                case SettlementVisibilityMode.TownsAndCastles:
                    visible = isTown || isCastle;
                    break;
                case SettlementVisibilityMode.TownsOnly:
                    visible = isTown;
                    break;
                default:
                    if (isTown || isCastle || isVillage) visible = false;
                    break;
            }
        }

        internal static void ApplyCloseZoomFill(ref float alpha)
        {
            if (!_active) return;
            if (_fillVisibility == FillVisibilityMode.Hidden) alpha = 0f;
            else alpha = _fillVisibility == FillVisibilityMode.AllZooms
                ? EditorPoliticalFillOpacity : alpha * EditorPoliticalFillOpacity;
        }

        private enum EditorMode { Border, Fill }
        private enum BorderTool
        { Line, Curve, FollowCoast, Reference, Freeform, Sculpt, Generated, PartyTrace }
        private enum SettlementVisibilityMode
        { Hidden, All, TownsAndCastles, TownsOnly }
        private enum FillVisibilityMode { AllZooms, OverviewOnly, Hidden }

        private sealed class EditorFaction
        {
            internal EditorFaction(string id, string name, uint color)
            { Id = id ?? string.Empty; Name = name ?? id ?? string.Empty; Color = color; }
            internal readonly string Id, Name;
            internal readonly uint Color;
        }

        private sealed class SculptNodeRef
        {
            internal SculptNodeRef(List<Vec2> points, int index)
            { Points = points; Index = index; }
            internal readonly List<Vec2> Points;
            internal readonly int Index;
        }
    }
}
