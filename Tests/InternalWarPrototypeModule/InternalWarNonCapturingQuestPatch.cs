using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradiaInternalWarsTest
{
    // v1.4.8: private instance void TheConquestOfSettlementIssueBehavior+
    // TheConquestOfSettlementIssueQuest.OnSiegeCompleted(Settlement, MobileParty, bool, BattleTypes).
    // Native listener ignores battleType and can award/cancel conquest quests for a non-capture.
    // This prefix suppresses ONLY that listener for an owned private sally/relief target; it never
    // suppresses the campaign event, other quests, actual assaults, or unregistered native wars.
    // Reflection signature drift throws during patch installation, handled by module startup's
    // diagnostic/failure boundary; never guess another target. FindOwned intentionally also works
    // during recovery suppression. Actual-prefix verifier covers event-kind and ownership scope.
    [HarmonyPatch]
    internal static class InternalWarNonCapturingQuestPatch
    {
        internal const string NativeTypeName = "TaleWorlds.CampaignSystem.Issues.TheConquestOfSettlementIssueBehavior+TheConquestOfSettlementIssueQuest";

        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(NativeTypeName);
            MethodInfo method = type == null ? null : AccessTools.Method(type, "OnSiegeCompleted",
                new[] { typeof(Settlement), typeof(MobileParty), typeof(bool), typeof(MapEvent.BattleTypes) });
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                throw new MissingMethodException(NativeTypeName, "OnSiegeCompleted(Settlement, MobileParty, bool, BattleTypes)");
            return method;
        }

        [HarmonyPrefix]
        internal static bool Prefix(Settlement __0, MapEvent.BattleTypes __3)
        {
            return (__3 != MapEvent.BattleTypes.SallyOut && __3 != MapEvent.BattleTypes.SiegeOutside)
                || InternalWarTestService.FindOwned(__0) == null;
        }
    }
}
