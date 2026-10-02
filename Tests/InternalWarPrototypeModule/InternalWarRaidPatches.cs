using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>BeHostileAction.ApplyInternal v1.4.8: one IFaction.IsAtWarWith call receives actual actors.
    /// A registered raid uses wartime consequences instead of kingdom crime/influence penalties.
    /// Other actors/actions retain native behavior. One-call mismatch aborts the whole patch set.
    /// Verified by exact IL audit/Release; native relation, loot and cleanup tests remain mandatory.</summary>
    [HarmonyPatch]
    internal static class InternalWarRaidCrimePatch
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(BeHostileAction), "ApplyInternal"); }
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return InternalWarRaidWarQuery.Rewrite(instructions, AccessTools.Method(typeof(InternalWarRaidCrimePatch), nameof(IsWar)), true);
        }
        private static bool IsWar(IFaction first, IFaction second, PartyBase attacker, PartyBase defender)
        { return InternalWarTestService.IsRaidHostility(attacker, defender) || FactionManager.IsAtWarAgainstFaction(first, second); }
    }

    /// <summary>EncounterGameMenuBehavior.UpdateVillageHostileActionEncounter v1.4.8: one faction query
    /// recognizes only the current registered village raid. Native resistance/mission controls remain intact.
    /// Fail-closed one-call contract; no global faction hostility override.</summary>
    [HarmonyPatch]
    internal static class InternalWarRaidEncounterPatch
    {
        private static MethodBase TargetMethod()
        { return AccessTools.Method(typeof(EncounterGameMenuBehavior), "UpdateVillageHostileActionEncounter"); }
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        { return InternalWarRaidWarQuery.Rewrite(instructions, AccessTools.Method(typeof(InternalWarRaidEncounterPatch), nameof(IsWar)), false); }
        private static bool IsWar(IFaction first, IFaction second)
        {
            return (MobileParty.MainParty != null && PlayerEncounter.EncounterSettlement != null
                && InternalWarTestService.IsRaidHostility(MobileParty.MainParty.Party, PlayerEncounter.EncounterSettlement.Party))
                || FactionManager.IsAtWarAgainstFaction(first, second);
        }
    }

    internal static class InternalWarRaidWarQuery
    {
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, MethodInfo resolver, bool appendActors)
        {
            MethodInfo native = AccessTools.Method(typeof(IFaction), nameof(IFaction.IsAtWarWith));
            List<CodeInstruction> result = instructions.ToList();
            int count = 0;
            for (int index = 0; index < result.Count; index++)
            {
                CodeInstruction call = result[index];
                if (!call.Calls(native)) continue;
                if (appendActors)
                {
                    CodeInstruction load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(call.labels);
                    load.blocks.AddRange(call.blocks);
                    call.labels.Clear(); call.blocks.Clear();
                    result.InsertRange(index, new[] { load, new CodeInstruction(OpCodes.Ldarg_1) });
                    index += 2;
                }
                call.opcode = OpCodes.Call; call.operand = resolver; count++;
            }
            if (count != 1) throw new InvalidOperationException("Raid adapter expected one native war query, found " + count + ".");
            return result;
        }
    }
}
