using System;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Gauntlet presentation boundary for the map editor. Drawing and document
    /// behavior remain owned by PoliticalBorderEditorState; this layer only
    /// presents commands and observable state using the Refuge Builder panel
    /// pattern. Movie-load failure leaves all keyboard tools operational.
    /// </summary>
    internal sealed class PoliticalBorderEditorUi : GlobalLayer
    {
        private static PoliticalBorderEditorUi _active;
        private readonly GauntletLayer _layer;
        private readonly PoliticalBorderEditorVM _dataSource;

        private PoliticalBorderEditorUi()
        {
            _dataSource = new PoliticalBorderEditorVM();
            _layer = new GauntletLayer("PoliticalBorderEditor", 260, false);
            _layer.LoadMovie("PoliticalBorderEditor", _dataSource);
            Layer = _layer;
            Layer.IsFocusLayer = true;
            // A map-global Gauntlet layer must register mouse-button usage or
            // ScreenManager does not include it in pointer hit routing. Limit
            // the mask to buttons: panel clicks are consumed, while keyboard
            // and wheel camera input remain readable and map clicks outside
            // the panel continue through the panel-sized root.
            Layer.InputRestrictions.SetInputRestrictions(
                true, InputUsageMask.MouseButtons);
        }

        internal static bool IsPointerOverPanel
        { get { return _active != null && _active._layer.IsHitThisFrame; } }
        internal static bool IsOpen { get { return _active != null; } }

        internal static void Show()
        {
            if (_active != null) return;
            try
            {
                PoliticalBorderEditorUi ui = new PoliticalBorderEditorUi();
                _active = ui;
                ScreenManager.AddGlobalLayer(ui, true);
                ScreenManager.TrySetFocus(ui._layer);
                PoliticalBorderEditorDiagnostics.Info(
                    "Refuge-style political border editor panel opened.");
            }
            catch (Exception exception)
            {
                _active = null;
                PoliticalBorderEditorDiagnostics.Error(
                    "Political border editor UI failed to load; keyboard controls remain active.",
                    exception);
            }
        }

        internal static void Refresh()
        {
            if (_active != null) _active._dataSource.Refresh();
        }

        internal static void CloseFromState()
        {
            if (_active != null) _active.Close();
        }

        internal static void HideForPartyTrace()
        {
            if (_active != null) _active.Close();
        }

        internal static void HideForDrawing()
        {
            if (_active != null) _active.Close();
        }

        internal static void SetCameraNavigation(bool navigation)
        {
            if (_active == null) return;
            // Navigation suspends map drawing in the state layer; it must not
            // remove Gauntlet focus or the CAMERA button cannot switch back.
            // The mouse-button mask keeps the toolbar in ScreenManager hit
            // routing without blocking keyboard or wheel navigation.
            _active.Layer.IsFocusLayer = true;
            _active.Layer.InputRestrictions.SetInputRestrictions(
                true, InputUsageMask.MouseButtons);
            ScreenManager.TrySetFocus(_active._layer);
            PoliticalBorderEditorDiagnostics.Info(
                "Editor UI focus retained for camera mode: navigation="
                + navigation + "; inputMask=MouseButtons.");
        }

        protected override void OnTick(float dt)
        {
            base.OnTick(dt);
            _dataSource.Refresh();
            if (Input.IsKeyReleased(InputKey.LeftMouseButton))
            {
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor UI pointer release: layerHit=" + _layer.IsHitThisFrame
                    + "; focusLayer=" + Layer.IsFocusLayer + ".");
            }
        }

        private void Close()
        {
            if (_active != this) return;
            try
            {
                ScreenManager.TryLoseFocus(_layer);
                // During application/screen teardown TopScreen can already be
                // null; ScreenManager.RemoveGlobalLayer dereferences that
                // state internally in Bannerlord 1.4.8.
                if (ScreenManager.TopScreen != null)
                    ScreenManager.RemoveGlobalLayer(this);
                _layer.InputRestrictions.ResetInputRestrictions();
                _dataSource.OnFinalize();
                PoliticalBorderEditorDiagnostics.Info(
                    "Political border editor panel closed.");
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Political border editor UI cleanup failed; editor state was still closed.",
                    exception);
            }
            finally
            {
                _active = null;
            }
        }
    }

    internal sealed class PoliticalBorderEditorVM : ViewModel
    {
        private string _tool = string.Empty;
        private string _faction = string.Empty;
        private string _brushRadius = string.Empty;
        private string _counts = string.Empty;
        private string _coastStatus = string.Empty;
        private string _gridStatus = string.Empty;
        private string _referenceStatus = string.Empty;
        private string _cameraStatus = string.Empty;
        private string _settlementVisibility = string.Empty;
        private string _closeZoomFill = string.Empty;
        private string _borderWidth = string.Empty;
        private string _borderStyle = string.Empty;

        internal PoliticalBorderEditorVM() { Refresh(); }

        [DataSourceProperty]
        public string Tool
        {
            get { return _tool; }
            private set { Set(ref _tool, value, nameof(Tool)); }
        }

        [DataSourceProperty]
        public string Faction
        {
            get { return _faction; }
            private set { Set(ref _faction, value, nameof(Faction)); }
        }

        [DataSourceProperty]
        public string BrushRadius
        {
            get { return _brushRadius; }
            private set { Set(ref _brushRadius, value, nameof(BrushRadius)); }
        }

        [DataSourceProperty]
        public string Counts
        {
            get { return _counts; }
            private set { Set(ref _counts, value, nameof(Counts)); }
        }

        [DataSourceProperty]
        public string CoastStatus
        {
            get { return _coastStatus; }
            private set { Set(ref _coastStatus, value, nameof(CoastStatus)); }
        }

        [DataSourceProperty]
        public string GridStatus
        {
            get { return _gridStatus; }
            private set { Set(ref _gridStatus, value, nameof(GridStatus)); }
        }

        [DataSourceProperty] public string ReferenceStatus
        { get { return _referenceStatus; } private set { Set(ref _referenceStatus, value, nameof(ReferenceStatus)); } }
        [DataSourceProperty] public string CameraStatus
        { get { return _cameraStatus; } private set { Set(ref _cameraStatus, value, nameof(CameraStatus)); } }
        [DataSourceProperty] public string SettlementVisibility
        { get { return _settlementVisibility; } private set { Set(ref _settlementVisibility, value, nameof(SettlementVisibility)); } }
        [DataSourceProperty] public string CloseZoomFill
        { get { return _closeZoomFill; } private set { Set(ref _closeZoomFill, value, nameof(CloseZoomFill)); } }
        [DataSourceProperty] public string BorderWidth
        { get { return _borderWidth; } private set { Set(ref _borderWidth, value, nameof(BorderWidth)); } }
        [DataSourceProperty] public string BorderStyle
        { get { return _borderStyle; } private set { Set(ref _borderStyle, value, nameof(BorderStyle)); } }

        internal void Refresh()
        {
            Tool = PoliticalBorderEditorState.UiToolText;
            Faction = PoliticalBorderEditorState.UiFactionText;
            BrushRadius = PoliticalBorderEditorState.UiBrushText;
            Counts = PoliticalBorderEditorState.UiCountsText;
            CoastStatus = PoliticalBorderEditorState.UiGeneratedCoastsText;
            GridStatus = PoliticalBorderEditorState.UiGridText;
            ReferenceStatus = PoliticalBorderEditorState.UiReferenceText;
            CameraStatus = PoliticalBorderEditorState.UiCameraText;
            SettlementVisibility = PoliticalBorderEditorState.UiSettlementVisibilityText;
            CloseZoomFill = PoliticalBorderEditorState.UiCloseZoomFillText;
            BorderWidth = PoliticalBorderEditorState.UiBorderWidthText;
            BorderStyle = PoliticalBorderEditorState.UiBorderStyleText;
        }

        public void ExecuteLine() { ExecuteCommand("LINE", PoliticalBorderEditorState.UiSelectLine); }
        public void ExecuteCurve() { ExecuteCommand("CURVE", PoliticalBorderEditorState.UiSelectCurve); }
        public void ExecuteFollowCoast() { ExecuteCommand("COAST", PoliticalBorderEditorState.UiSelectFollowCoast); }
        public void ExecuteReference() { ExecuteCommand("TRACE", PoliticalBorderEditorState.UiSelectReference); }
        public void ExecuteFreeform() { ExecuteCommand("FREE", PoliticalBorderEditorState.UiSelectFreeform); }
        public void ExecuteSculpt() { ExecuteCommand("SCULPT", PoliticalBorderEditorState.UiSelectSculpt); }
        public void ExecuteGenerated() { ExecuteCommand("EDIT GENERATED", PoliticalBorderEditorState.UiSelectGenerated); }
        public void ExecutePartyTrace() { ExecuteCommand("SURVEY", PoliticalBorderEditorState.UiTogglePartyTrace, false); }
        public void ExecuteReady() { ExecuteCommand("READY / DRAW", PoliticalBorderEditorState.UiToggleDrawingPanel, false); }
        public void ExecuteSmooth() { ExecuteCommand("SMOOTH", PoliticalBorderEditorState.UiSmooth); }
        public void ExecuteFillBrush() { ExecuteCommand("FILL", PoliticalBorderEditorState.UiSelectFillBrush); }
        public void ExecuteSmallerBrush() { ExecuteCommand("SMALLER", () => PoliticalBorderEditorState.UiResizeBrush(-0.25f)); }
        public void ExecuteLargerBrush() { ExecuteCommand("LARGER", () => PoliticalBorderEditorState.UiResizeBrush(0.25f)); }
        public void ExecuteUndo() { ExecuteCommand("UNDO POINT", PoliticalBorderEditorState.UiUndo); }
        public void ExecuteDelete() { ExecuteCommand("DELETE LAST", PoliticalBorderEditorState.UiDelete); }
        public void ExecuteCommit() { ExecuteCommand("CONFIRM PLAN", PoliticalBorderEditorState.UiCommit); }
        public void ExecuteSave() { ExecuteCommand("SAVE + APPLY", PoliticalBorderEditorState.UiSave); }
        public void ExecuteToggleGrid() { ExecuteCommand("GRID", PoliticalBorderEditorState.UiToggleGrid); }
        public void ExecuteToggleReference() { ExecuteCommand("REFERENCE OVERLAY", PoliticalBorderEditorState.UiToggleReferenceOverlay); }
        public void ExecuteToggleCamera() { ExecuteCommand("CAMERA", PoliticalBorderEditorState.UiToggleCameraNavigation); }
        public void ExecuteCycleSettlementVisibility() { ExecuteCommand("SETTLEMENTS", PoliticalBorderEditorState.UiCycleSettlementVisibility); }
        public void ExecuteToggleCloseZoomFill() { ExecuteCommand("FILL ALL ZOOMS", PoliticalBorderEditorState.UiToggleCloseZoomFill); }
        public void ExecuteThinnerBorders() { ExecuteCommand("THINNER", () => PoliticalBorderEditorState.UiResizeAllBorders(-0.1f)); }
        public void ExecuteThickerBorders() { ExecuteCommand("THICKER", () => PoliticalBorderEditorState.UiResizeAllBorders(0.1f)); }
        public void ExecuteCycleBorderStyle() { ExecuteCommand("BORDER STYLE", PoliticalBorderEditorState.UiCycleBorderStyle); }
        public void ExecuteClose() { ExecuteCommand("CLOSE", () => PoliticalBorderEditorState.SetActive(false), false); }

        private void ExecuteCommand(string name, Action command, bool refresh = true)
        {
            PoliticalBorderEditorDiagnostics.Info(
                "Editor UI command invoked: " + name + ".");
            try
            {
                command();
                if (refresh) Refresh();
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor UI command completed: " + name + ".");
            }
            catch (Exception exception)
            {
                // Gauntlet commands cross both the native UI and campaign-state
                // boundaries. Preserve the session and record the exact command.
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor UI command failed: " + name + ".", exception);
            }
        }

        private void Set(ref string field, string value, string property)
        {
            value = value ?? string.Empty;
            if (field == value) return;
            field = value;
            OnPropertyChangedWithValue(value, property);
        }
    }
}
