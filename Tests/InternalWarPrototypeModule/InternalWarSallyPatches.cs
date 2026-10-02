using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace AgesOfCalradiaInternalWarsTest
{
    // Native v1.4.8 menu condition: replace exactly its one IFaction war query while preserving
    // player-side and commander checks. Exact-count failure disables the full module patch set.
    // Actual service tests + native one-query audit cover guard/shape; live menu/mission remains required.
    [HarmonyPatch]
    internal static class InternalWarSallyMenuPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(EncounterGameMenuBehavior), "menu_sally_out_from_gate_on_condition"); }
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            MethodInfo native = AccessTools.Method(typeof(IFaction), nameof(IFaction.IsAtWarWith));
            MethodInfo replacement = AccessTools.Method(typeof(InternalWarSallyMenuPatch), nameof(IsWar));
            var result = source.ToList(); int count = 0;
            foreach (CodeInstruction instruction in result)
                if (instruction.Calls(native)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; count++; }
            if (count != 1) throw new InvalidOperationException("Sally menu expected one native faction-war query, found " + count + ".");
            return result;
        }
        private static bool IsWar(IFaction first, IFaction second)
        {
            SiegeEvent siege = PlayerSiege.PlayerSiegeEvent;
            MobileParty leader = siege == null || siege.BesiegerCamp == null ? null : siege.BesiegerCamp.LeaderParty;
            return (MobileParty.MainParty != null && leader != null
                && InternalWarSallyService.CanStart(MobileParty.MainParty.Party, leader.Party))
                || FactionManager.IsAtWarAgainstFaction(first, second);
        }
    }

    // Native consequence finalizes an existing besieger event before initiating a sally. Prevent that
    // destructive shortcut for an owned private siege; fresh events retain native player Init/StartBattle.
    [HarmonyPatch]
    internal static class InternalWarSallyConsequencePatch
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(EncounterGameMenuBehavior), "sally_out_consequence"); }
        [HarmonyPrefix]
        internal static bool Prefix()
        {
            Settlement target = Settlement.CurrentSettlement;
            if (InternalWarTestService.FindOwned(target) == null) return true;
            MobileParty leader = target.SiegeEvent == null || target.SiegeEvent.BesiegerCamp == null ? null : target.SiegeEvent.BesiegerCamp.LeaderParty;
            return MobileParty.MainParty != null && leader != null
                && InternalWarSallyService.CanStart(MobileParty.MainParty.Party, leader.Party);
        }
    }

    // Native StartPartyEncounter same-MapFaction branch merges NPC garrison/besieger parties.
    // For exact NPC-only private sallies use native ApplyStartSallyOut instead. Player participants retain
    // native encounter initialization, with the root party/menu adapter authorizing the scoped pair.
    [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter), new[] { typeof(PartyBase), typeof(PartyBase) })]
    internal static class InternalWarNpcSallyEntryPatch
    {
        [HarmonyPrefix]
        internal static bool Prefix(PartyBase attackerParty, PartyBase defenderParty)
        {
            if (!InternalWarSallyService.CanStart(attackerParty, defenderParty)) return true;
            if (attackerParty.MobileParty == MobileParty.MainParty || defenderParty.MobileParty == MobileParty.MainParty) return true;
            Settlement target = attackerParty.MobileParty.CurrentSettlement;
            if (MobileParty.MainParty != null && MobileParty.MainParty.CurrentSettlement == target) return false;
            try { StartBattleAction.ApplyStartSallyOut(target, defenderParty.MobileParty); }
            catch (Exception exception)
            {
                InternalWarTestService.BlockRecovery("Private NPC sally creation failed; native battle state needs inspection.");
                InternalWarTestDiagnostics.Error("NPC sally entry failed.", exception);
            }
            return false;
        }
    }

    // Native CheckSallyOut(Settlement,bool,out bool) scans nearby kingdom allies and may move them
    // directly into battle. Suppress it only for registered sieges; root ticks TryAutomatic's clan-only
    // land heuristic. Naval sallies remain unsupported and cannot escape this guard.
    [HarmonyPatch]
    internal static class InternalWarNativeSallyAiPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(SallyOutsCampaignBehavior), "CheckSallyOut",
            new[] { typeof(Settlement), typeof(bool), typeof(bool).MakeByRefType() }); }
        [HarmonyPrefix]
        internal static bool Prefix(Settlement __0, ref bool __2)
        {
            if (InternalWarTestService.FindOwned(__0) == null) return true;
            __2 = false;
            return false;
        }
    }
}
