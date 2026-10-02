using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    public sealed class PoliticalFillSeamFixSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad() { base.OnSubModuleLoad(); NativeFillSeamFix.Initialize(); }
        protected override void OnApplicationTick(float dt) { base.OnApplicationTick(dt); NativeFillSeamFix.Tick(); }
        protected override void OnSubModuleUnloaded() { NativeFillSeamFix.Stop(); base.OnSubModuleUnloaded(); }
    }
}
