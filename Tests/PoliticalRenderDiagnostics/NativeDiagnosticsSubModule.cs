using System;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // Diagnostics only; no save behavior, material writes, or replacement patches.
    public sealed class NativeDiagnosticsSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            NativeRenderProbe.Initialize();
        }
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            NativeRenderProbe.Tick();
        }
        protected override void OnSubModuleUnloaded()
        {
            NativeRenderProbe.Stop();
            base.OnSubModuleUnloaded();
        }
    }
}
