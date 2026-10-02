using System;
using System.Reflection;
using AgesOfCalradiaInternalWarsTest;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

internal static class NonCapturingQuestProgram
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    { _assertions++; if (!value) throw new InvalidOperationException(reason); }
    private static int Main()
    {
        var target = new Settlement();
        InternalWarTestService.Owned = target;
        foreach (MapEvent.BattleTypes kind in Enum.GetValues(typeof(MapEvent.BattleTypes)))
        {
            bool expected = kind != MapEvent.BattleTypes.SallyOut && kind != MapEvent.BattleTypes.SiegeOutside;
            Check(InternalWarNonCapturingQuestPatch.Prefix(target, kind) == expected, "owned event kind " + kind);
            Check(InternalWarNonCapturingQuestPatch.Prefix(new Settlement(), kind), "unrelated target " + kind);
            Check(InternalWarNonCapturingQuestPatch.Prefix(null, kind), "null target " + kind);
        }
        InternalWarTestService.Owned = null;
        Check(InternalWarNonCapturingQuestPatch.Prefix(target, MapEvent.BattleTypes.SallyOut), "unregistered sally");
        Check(InternalWarNonCapturingQuestPatch.Prefix(target, MapEvent.BattleTypes.SiegeOutside), "unregistered relief");
        MethodBase method = InternalWarNonCapturingQuestPatch.TargetMethod();
        Check(method.Name == "OnSiegeCompleted" && method.IsPrivate && !method.IsStatic, "exact instance target");
        HarmonyLib.AccessTools.Missing = true;
        bool missingRejected = false;
        try { InternalWarNonCapturingQuestPatch.TargetMethod(); }
        catch (MissingMethodException) { missingRejected = true; }
        Check(missingRejected, "missing native target rejected");
        Console.WriteLine("Noncapturing conquest quest: " + _assertions + " assertions passed; actual prefix with injected native boundaries.");
        return 0;
    }
}
namespace AgesOfCalradiaInternalWarsTest
{
    internal static class InternalWarTestService
    {
        internal static Settlement Owned;
        internal static object FindOwned(Settlement target) { return target != null && target == Owned ? target : null; }
    }
}
namespace TaleWorlds.CampaignSystem.Settlements { public sealed class Settlement { } }
namespace TaleWorlds.CampaignSystem.Party { public sealed class MobileParty { } }
namespace TaleWorlds.CampaignSystem.MapEvents
{
    public sealed class MapEvent { public enum BattleTypes { None = 0, FieldBattle = 1, Raid = 2, Siege = 5, SallyOut = 7, SiegeOutside = 8, BlockadeSallyOutBattle = 10 } }
}
namespace TaleWorlds.CampaignSystem.Issues
{
    public sealed class TheConquestOfSettlementIssueBehavior
    {
        private sealed class TheConquestOfSettlementIssueQuest
        {
            private void OnSiegeCompleted(Settlement settlement, MobileParty party, bool won, MapEvent.BattleTypes kind) { }
        }
    }
}
namespace HarmonyLib
{
    internal sealed class HarmonyPatch : Attribute { }
    internal sealed class HarmonyPrefix : Attribute { }
    internal sealed class HarmonyTargetMethod : Attribute { }
    internal static class AccessTools
    {
        internal static bool Missing;
        internal static Type TypeByName(string name) { return Missing ? null : typeof(AccessTools).Assembly.GetType(name); }
        internal static MethodInfo Method(Type type, string name, Type[] args)
        { return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, args, null); }
    }
}
