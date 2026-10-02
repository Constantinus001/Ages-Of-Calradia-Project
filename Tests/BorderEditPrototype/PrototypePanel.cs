using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace Aoc.BorderEditPrototype
{
    internal sealed class PrototypePanel : GlobalLayer
    {
        private static PrototypePanel _panel;
        private readonly GauntletLayer _layer;
        private readonly BorderPanelVM _vm;
        private bool _closing;
        private int _releaseDiagnostics;
        private PrototypePanel()
        {
            _vm=new BorderPanelVM(); _layer=new GauntletLayer("AocBorderEditPrototype",270,false);
            _layer.LoadMovie("AocBorderEditPrototype",_vm); Layer=_layer;
            _layer.IsFocusLayer=true; _layer.InputRestrictions.SetInputRestrictions(true,InputUsageMask.MouseButtons);
        }
        internal static bool PointerOverPanel => _panel!=null && !_panel._closing && _panel._layer.IsHitThisFrame;
        internal static bool OtherLayerFocused => _panel!=null && !_panel._closing && ScreenManager.FocusedLayer!=null
            && ScreenManager.FocusedLayer!=_panel._layer && !(ScreenManager.FocusedLayer is TaleWorlds.Engine.Screens.SceneLayer);
        internal static void Open()
        {
            RetryClose();
            if(_panel!=null) { if(_panel._closing) throw new System.InvalidOperationException("Previous editor panel is still closing."); return; }
            _panel=new PrototypePanel();
            ScreenManager.AddGlobalLayer(_panel,true); ScreenManager.TrySetFocus(_panel._layer); Refresh();
        }
        internal static void Refresh() { _panel?._vm.Refresh(); }
        protected override void OnTick(float dt)
        {
            base.OnTick(dt);
            // Bounded UI-routing evidence: distinguish a missing Gauntlet click
            // from a command rejected by editor state without logging every frame.
            if(!_closing && _releaseDiagnostics<12 && Input.IsKeyReleased(InputKey.LeftMouseButton))
            {
                _releaseDiagnostics++;
                PrototypeLog.Write("Panel pointer release; active="+PrototypeRuntime.Active
                    +"; layerActive="+_layer.IsActive+"; hit="+_layer.IsHitThisFrame
                    +"; firstHit="+ScreenManager.FirstHitLayer?.Name
                    +"; focused="+ScreenManager.FocusedLayer?.Name
                    +"; buttonsAllowed="+_layer.Input.IsMouseButtonAllowed
                    +"; pointer="+Input.MousePositionRanged);
            }
        }
        internal static void Close()
        {
            if(_panel==null) return; _panel._closing=true; RetryClose();
        }
        internal static void RetryClose()
        {
            if(_panel==null || !_panel._closing) return; PrototypePanel panel=_panel;
            NativeCleanup.Try(()=>ScreenManager.TryLoseFocus(panel._layer),"Editor focus release failed");
            NativeCleanup.Try(()=>panel._layer.InputRestrictions.ResetInputRestrictions(),"Editor input reset failed");
            panel._layer.IsFocusLayer=false;
            // Bannerlord RemoveGlobalLayer dereferences TopScreen. Retain the handle until a safe retry.
            if(ScreenManager.TopScreen==null) return;
            if(!NativeCleanup.Try(()=>ScreenManager.RemoveGlobalLayer(panel),"Editor global layer removal deferred")) return;
            _panel=null; NativeCleanup.Try(panel._vm.OnFinalize,"Editor view-model finalization failed");
        }
    }
    public sealed class BorderPanelVM : ViewModel
    {
        private string _status,_counts,_connectLabel,_deleteLabel,_addLabel,_fillLabel,_viewLabel,_coastLabel,_politicalLabel,_borderFillLabel;
        private string _actionHint;
        private bool _canUndo,_canRedo,_canSave,_canDelete;
        [DataSourceProperty] public bool CanUndo=>_canUndo;
        [DataSourceProperty] public bool CanRedo=>_canRedo;
        [DataSourceProperty] public bool CanSave=>_canSave;
        [DataSourceProperty] public bool CanDelete=>_canDelete;
        [DataSourceProperty] public float UndoOpacity=>_canUndo?1f:.4f;
        [DataSourceProperty] public float RedoOpacity=>_canRedo?1f:.4f;
        [DataSourceProperty] public float SaveOpacity=>_canSave?1f:.4f;
        [DataSourceProperty] public float DeleteOpacity=>_canDelete?1f:.4f;
        [DataSourceProperty] public string ActionHint { get=>_actionHint; set { if(value==_actionHint)return;_actionHint=value;OnPropertyChangedWithValue(value,nameof(ActionHint)); } }
        [DataSourceProperty] public string DeleteLabel { get=>_deleteLabel; set { if(value==_deleteLabel)return;_deleteLabel=value;OnPropertyChangedWithValue(value,nameof(DeleteLabel)); } }
        [DataSourceProperty] public string ConnectLabel { get=>_connectLabel; set { if(value==_connectLabel)return;_connectLabel=value;OnPropertyChangedWithValue(value,nameof(ConnectLabel)); } }
        [DataSourceProperty] public string Status { get=>_status; set { if(value==_status)return;_status=value;OnPropertyChangedWithValue(value,nameof(Status)); } }
        [DataSourceProperty] public string Counts { get=>_counts; set { if(value==_counts)return;_counts=value;OnPropertyChangedWithValue(value,nameof(Counts)); } }
        [DataSourceProperty] public string AddLabel { get=>_addLabel; set { if(value==_addLabel)return;_addLabel=value;OnPropertyChangedWithValue(value,nameof(AddLabel)); } }
        [DataSourceProperty] public string FillLabel { get=>_fillLabel; set { if(value==_fillLabel)return;_fillLabel=value;OnPropertyChangedWithValue(value,nameof(FillLabel)); } }
        [DataSourceProperty] public string ViewLabel { get=>_viewLabel; set { if(value==_viewLabel)return;_viewLabel=value;OnPropertyChangedWithValue(value,nameof(ViewLabel)); } }
        [DataSourceProperty] public string CoastLabel { get=>_coastLabel; set { if(value==_coastLabel)return;_coastLabel=value;OnPropertyChangedWithValue(value,nameof(CoastLabel)); } }
        [DataSourceProperty] public string PoliticalLabel { get=>_politicalLabel; set { if(value==_politicalLabel)return;_politicalLabel=value;OnPropertyChangedWithValue(value,nameof(PoliticalLabel)); } }
        [DataSourceProperty] public string BorderFillLabel { get=>_borderFillLabel; set { if(value==_borderFillLabel)return;_borderFillLabel=value;OnPropertyChangedWithValue(value,nameof(BorderFillLabel)); } }
        internal void Refresh()
        {
            BorderFillLabel=PrototypeRuntime.DrawingMode=="borderfill"?"Done filling to border":"Fill to border";
            CoastLabel=PrototypeRuntime.DrawingMode=="coast"?"Done following coast":"Follow coast";
            PoliticalLabel=PrototypeRuntime.DrawingMode=="political"?"Done following fill":"Follow political fill";
            AddLabel=PrototypeRuntime.DrawingMode=="add"?"Done drawing":"Draw border"; FillLabel=PrototypeRuntime.DrawingMode=="fill"?"Done filling":"Fill area";
            Status=PrototypeRuntime.Status;
            ViewLabel=PrototypeRuntime.TopDown?"3D view":"2D view";
            ActionHint=PrototypeRuntime.ActionHint;
            DeleteLabel=PrototypeRuntime.DeleteMode?"Done":"Delete";
            ConnectLabel=PrototypeRuntime.ConnectMode?"Cancel link":"Connect";
            BorderGraph graph=PrototypeRuntime.Source?.Graph;
            SetAction(ref _canUndo,graph!=null&&graph.CanUndo,nameof(CanUndo),nameof(UndoOpacity));
            SetAction(ref _canRedo,graph!=null&&graph.CanRedo,nameof(CanRedo),nameof(RedoOpacity));
            SetAction(ref _canSave,PrototypeRuntime.CanSave,nameof(CanSave),nameof(SaveOpacity));
            SetAction(ref _canDelete,PrototypeRuntime.CanDelete,nameof(CanDelete),nameof(DeleteOpacity));
            Counts=graph==null?"Waiting for borders":(PrototypeRuntime.ConnectMode?"Connecting":graph.Selected.Count+" selected")+" | "+graph.Current.Bridges.Length+" lines / "+graph.Current.Fills.Length+" fills | "+(PrototypeRuntime.Preview.Busy||!PrototypeRuntime.FillReady?"Updating":NativeSelection.PreparingLines?"Preparing lines":PrototypeRuntime.SaveState);
        }
        private void SetAction(ref bool field,bool value,string property,string opacity)
        {
            if(field==value)return;field=value;
            OnPropertyChangedWithValue(value,property);OnPropertyChangedWithValue(value?1f:.4f,opacity);
        }
        private void Execute(string action)
        {
            PrototypeLog.Write("Panel command="+action+"; editorActive="+PrototypeRuntime.Active);
            PrototypeRuntime.Command(action);
            if(PrototypeRuntime.Active) Refresh();
        }
        public void ExecuteBorderFill()=>Execute("borderfill");
        public void ExecuteCoast()=>Execute("coast");
        public void ExecutePolitical()=>Execute("political");
        public void ExecuteAdd()=>Execute("add");
        public void ExecuteView()=>Execute("view");
        public void ExecuteFill()=>Execute("fill");
        public void ExecuteDelete()=>Execute("deletemode");
        public void ExecuteConnect()=>Execute("connect");
        public void ExecuteUndo()=>Execute("undo");
        public void ExecuteRedo()=>Execute("redo");
        public void ExecuteSave()=>Execute("save");
        public void ExecuteFinish()=>Execute("finish");
        public void ExecuteCancel()=>Execute("cancel");
    }
}

