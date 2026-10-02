using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.CampaignLabelVisibility
{
    public sealed class CampaignLabelVisibilitySubModule : MBSubModuleBase
    {
        private const string HarmonyId = "AgesOfCalradia.CampaignLabelVisibility";
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(CampaignLabelVisibilitySubModule).Assembly);
        }

        protected override void OnSubModuleUnloaded()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }
            base.OnSubModuleUnloaded();
        }
    }

    /// <summary>
    /// Native target: SettlementNameplateVM.UpdateNameplateMT. Hides town
    /// nameplates once the campaign camera reaches the 580-altitude political
    /// overview so border artwork can be inspected on a clean map. Compatibility
    /// risk is limited to the native
    /// private _isTown and _bindIsVisibleOnMap fields; if Bannerlord changes
    /// either field, Harmony rejects this isolated patch without changing the
    /// political renderer or World Events UI. Source/assembly contract checks
    /// and runtime Harmony ownership verification cover registration.
    /// </summary>
    [HarmonyPatch]
    internal static class SettlementNameplateZoomPatch
    {
        private const float PoliticalOverviewStartAltitude = 580f;

        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(
                "SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM");
            return type == null ? null : AccessTools.Method(type, "UpdateNameplateMT");
        }

        private static void Postfix(
            ref bool ____bindIsVisibleOnMap,
            bool ____isTown,
            Vec3 cameraPosition)
        {
            if (cameraPosition.z >= PoliticalOverviewStartAltitude && ____isTown)
                ____bindIsVisibleOnMap = false;
        }
    }

}
