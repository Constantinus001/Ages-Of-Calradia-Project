using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>DefaultEncounterGameMenuModel.GetEncounterMenu v1.4.8: select native battle menu only for
    /// the declared pair's free land lord parties. Native Init owns StartBattle and mission setup.
    /// All other encounters remain native. Exact MVID + contract audit; requires live player attack/defense test.</summary>
    [HarmonyPatch(typeof(DefaultEncounterGameMenuModel), nameof(DefaultEncounterGameMenuModel.GetEncounterMenu))]
    internal static class InternalWarFieldEncounterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(PartyBase attackerParty, PartyBase defenderParty,
            ref bool startBattle, ref bool joinBattle, ref string __result)
        {
            if (MobileParty.MainParty == null || (attackerParty != MobileParty.MainParty.Party
                && defenderParty != MobileParty.MainParty.Party)
                || (!InternalWarCombatService.CanStartFieldBattle(attackerParty, defenderParty)
                    && !InternalWarSallyService.CanStart(attackerParty, defenderParty)
                    && !InternalWarReliefService.CanStart(attackerParty, defenderParty))) return;
            __result = "encounter";
            startBattle = true;
            joinBattle = false;
            InternalWarTestDiagnostics.Info("FIELD ENCOUNTER: native battle entry authorized for declared clan pair.");
        }
    }

    /// <summary>DefaultMobilePartyAIModel.GetBestInitiativeBehavior v1.4.8: replace precisely three IsEnemy
    /// helper calls, retaining native protection, navigation and strength decisions. Patching the tiny helper
    /// alone risks inlining. Pattern mismatch throws, causing the submodule to unpatch the entire test assembly.
    /// Verification: exact native IL count, Release build, pure hostility tests; AI runtime matrix outstanding.</summary>
    [HarmonyPatch(typeof(DefaultMobilePartyAIModel), nameof(DefaultMobilePartyAIModel.GetBestInitiativeBehavior))]
    internal static class InternalWarAiHostilityPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo native = AccessTools.Method(typeof(DefaultMobilePartyAIModel), "IsEnemy", new[] { typeof(PartyBase), typeof(MobileParty) });
            MethodInfo resolver = AccessTools.Method(typeof(InternalWarAiHostilityPatch), nameof(IsEnemy));
            List<CodeInstruction> result = instructions.ToList();
            int count = 0;
            foreach (CodeInstruction instruction in result)
            {
                if (!instruction.Calls(native)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = resolver;
                count++;
            }
            if (count != 3) throw new InvalidOperationException("AI initiative expected three IsEnemy calls, found " + count + ".");
            return result;
        }

        private static bool IsEnemy(DefaultMobilePartyAIModel model, PartyBase party, MobileParty mobileParty)
        {
            if (mobileParty != null && (InternalWarCombatService.CanStartFieldBattle(party, mobileParty.Party)
                || InternalWarReliefService.CanStart(party, mobileParty.Party))) return true;
            // Exact audited native helper: two MapFaction properties followed by this native query.
            return FactionManager.IsAtWarAgainstFaction(party.MapFaction, mobileParty.MapFaction);
        }
    }
}
