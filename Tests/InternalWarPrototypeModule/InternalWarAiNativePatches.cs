using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// Native v1.4.8 DefaultMobilePartyAIModel.CalculateInitiativeScoresForEnemy: replace its one
    /// CalculateStanceScore call, because shared MapFaction otherwise multiplies attack scores by -1.
    /// Caller-level adaptation avoids inlining of the small stance helper. Protection, morale, distance
    /// and tactical scoring remain native; shared-kingdom nearby-strength estimates are still a limitation.
    /// Exact MVID/signatures and one-call IL audit are required. A pattern mismatch throws during PatchAll,
    /// whose registration boundary removes every test patch. Verify synthetic IL and live AI attack/flee.
    /// </summary>
    [HarmonyPatch]
    internal static class InternalWarAiStancePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(DefaultMobilePartyAIModel), "CalculateInitiativeScoresForEnemy",
                new[] { typeof(MobileParty), typeof(MobileParty), typeof(float).MakeByRefType(),
                    typeof(float).MakeByRefType(), typeof(float), typeof(float) });
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo native = AccessTools.Method(typeof(DefaultMobilePartyAIModel), "CalculateStanceScore",
                new[] { typeof(MobileParty), typeof(MobileParty) });
            MethodInfo resolver = AccessTools.Method(typeof(InternalWarAiStancePatch), nameof(StanceScore));
            if (native == null || resolver == null) throw new InvalidOperationException("AI stance targets are unavailable.");
            List<CodeInstruction> result = instructions.ToList();
            int count = 0;
            foreach (CodeInstruction instruction in result)
            {
                if (!instruction.Calls(native)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = resolver;
                count++;
            }
            if (count != 1) throw new InvalidOperationException("AI score expected one CalculateStanceScore call, found " + count + ".");
            return result;
        }

        private static float StanceScore(DefaultMobilePartyAIModel model, MobileParty party, MobileParty otherParty)
        {
            if (party != null && otherParty != null
                && (InternalWarCombatService.CanStartFieldBattle(party.Party, otherParty.Party)
                    || InternalWarReliefService.CanStart(party.Party, otherParty.Party))) return 1f;
            // Exact inspected native helper fallback, including eliminated-faction semantics.
            if (FactionManager.IsAtWarAgainstFaction(party.MapFaction, otherParty.MapFaction)) return 1f;
            return DiplomacyHelper.IsSameFactionAndNotEliminated(party.MapFaction, otherParty.MapFaction) ? -1f : 0f;
        }
    }

    /// <summary>
    /// Native v1.4.8 EncounterManager.StartPartyEncounter: adapt only its nonplayer same-MapFaction branch.
    /// The declared pair's free land lord parties enter the existing StartBattleAction.Apply branch instead
    /// of being assigned to one side. No prefix skips the remaining encounter/relief handling. Match exactly
    /// ldarg.0/get_MapFaction/ldarg.1/get_MapFaction/bne.un; a missing or duplicate pattern disables all test
    /// patches at registration. Verify branch polarity/labels, native fallback and real AI-vs-AI aftermath.
    /// </summary>
    [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter),
        new[] { typeof(PartyBase), typeof(PartyBase) })]
    internal static class InternalWarAiPartyEncounterPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo factionGetter = AccessTools.PropertyGetter(typeof(PartyBase), nameof(PartyBase.MapFaction));
            MethodInfo resolver = AccessTools.Method(typeof(InternalWarAiPartyEncounterPatch), nameof(UseNativeSameSide));
            if (factionGetter == null || resolver == null) throw new InvalidOperationException("Party encounter targets are unavailable.");
            List<CodeInstruction> result = instructions.ToList();
            int count = 0;
            for (int index = 4; index < result.Count; index++)
            {
                CodeInstruction branch = result[index];
                if ((branch.opcode != OpCodes.Bne_Un && branch.opcode != OpCodes.Bne_Un_S)
                    || result[index - 4].opcode != OpCodes.Ldarg_0 || !result[index - 3].Calls(factionGetter)
                    || result[index - 2].opcode != OpCodes.Ldarg_1 || !result[index - 1].Calls(factionGetter)) continue;
                CodeInstruction loadAttacker = new CodeInstruction(OpCodes.Ldarg_0);
                loadAttacker.labels.AddRange(branch.labels);
                loadAttacker.blocks.AddRange(branch.blocks);
                branch.labels.Clear();
                branch.blocks.Clear();
                result.InsertRange(index, new[] { loadAttacker, new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Call, resolver) });
                index += 3;
                // Existing false/equal fallthrough is the same-side assignment; false now takes battle.
                branch.opcode = OpCodes.Brfalse;
                count++;
            }
            if (count != 1) throw new InvalidOperationException("Party encounter expected one same-faction branch, found " + count + ".");
            return result;
        }

        private static bool UseNativeSameSide(IFaction first, IFaction second, PartyBase attacker, PartyBase defender)
        {
            return !InternalWarCombatService.CanStartFieldBattle(attacker, defender)
                && !InternalWarReliefService.CanStart(attacker, defender) && first == second;
        }
    }

    /// <summary>
    /// Native v1.4.8 EncounterManager.StartSettlementEncounter: attach explicit party/settlement context to
    /// its two FactionManager.IsAtWarAgainstFaction calls (raid and assault arrival). Only a registered AI
    /// fortification assault or reserved NPC village raid can receive the exception; the separate IFaction
    /// war query stays native. Operation, clan, owner, kingdom and political phase must still match. The native method
    /// retains battle creation and aftermath. Exact two-call mismatch throws and disables all test patches.
    /// Verification: native call count plus guard negatives and live AI siege; this does not select targets
    /// or supply missing AI camp-continuity/capture integration by itself.
    /// </summary>
    [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartSettlementEncounter),
        new[] { typeof(MobileParty), typeof(Settlement) })]
    internal static class InternalWarAiSettlementEncounterPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo native = AccessTools.Method(typeof(FactionManager), nameof(FactionManager.IsAtWarAgainstFaction),
                new[] { typeof(IFaction), typeof(IFaction) });
            MethodInfo resolver = AccessTools.Method(typeof(InternalWarAiSettlementEncounterPatch), nameof(IsAtWarForSettlement));
            if (native == null || resolver == null) throw new InvalidOperationException("Settlement encounter targets are unavailable.");
            List<CodeInstruction> result = instructions.ToList();
            int count = 0;
            for (int index = 0; index < result.Count; index++)
            {
                CodeInstruction call = result[index];
                if (!call.Calls(native)) continue;
                CodeInstruction loadAttacker = new CodeInstruction(OpCodes.Ldarg_0);
                loadAttacker.labels.AddRange(call.labels);
                loadAttacker.blocks.AddRange(call.blocks);
                call.labels.Clear();
                call.blocks.Clear();
                result.InsertRange(index, new[] { loadAttacker, new CodeInstruction(OpCodes.Ldarg_1) });
                index += 2;
                call.opcode = OpCodes.Call;
                call.operand = resolver;
                count++;
            }
            if (count != 2) throw new InvalidOperationException("Settlement encounter expected two faction-war calls, found " + count + ".");
            return result;
        }

        private static bool IsAtWarForSettlement(IFaction first, IFaction second, MobileParty attacker, Settlement settlement)
        {
            return CanStartRecordedAssault(attacker, settlement) || InternalWarTestService.CanStartNpcRaid(attacker, settlement)
                || FactionManager.IsAtWarAgainstFaction(first, second);
        }

        internal static bool CanStartRecordedAssault(MobileParty attacker, Settlement settlement)
        {
            if (attacker == null || attacker == MobileParty.MainParty || !attacker.IsActive || !attacker.IsLordParty
                || attacker.IsCurrentlyAtSea || attacker.Army != null || attacker.AttachedTo != null
                || attacker.AttachedParties.Count != 0 || attacker.CurrentSettlement != null || attacker.Party.MapEvent != null
                || settlement == null || !settlement.IsFortification || settlement.Party.MapEvent != null
                || attacker.ShortTermBehavior != AiBehavior.AssaultSettlement || attacker.ShortTermTargetSettlement != settlement)
                return false;
            InternalWarTestRecord record = InternalWarTestService.Find(settlement);
            Clan actorClan = InternalWarTestService.ResolveClan(attacker);
            Clan ownerClan = settlement.OwnerClan;
            InternalConflictRecord conflict = InternalWarTestService.FindConflict(actorClan, ownerClan);
            if (record == null || (record.State != InternalWarTestState.Preparing && record.State != InternalWarTestState.Assaulting)
                || record.CaptureApplied || record.CleanupComplete || conflict == null || conflict.Phase != InternalConflictPhase.Active
                || !conflict.Matches(record) || conflict.ActiveOperationId != record.ConflictId
                || attacker.StringId != record.LeaderPartyId || actorClan == null || actorClan.StringId != record.AttackerClanId
                || ownerClan == null || ownerClan.StringId != record.DefenderClanId
                || actorClan.Kingdom == null || actorClan.Kingdom.StringId != record.KingdomId || ownerClan.Kingdom != actorClan.Kingdom
                || actorClan.IsClanTypeMercenary || actorClan.IsUnderMercenaryService
                || (actorClan.IsMinorFaction && actorClan != Clan.PlayerClan))
                return false;
            SiegeEvent siege = settlement.SiegeEvent;
            return siege != null && siege.BesiegedSettlement == settlement && siege.BesiegerCamp != null
                && siege.BesiegerCamp.SiegeEvent == siege && siege.BesiegerCamp.LeaderParty == attacker
                && attacker.BesiegerCamp == siege.BesiegerCamp && siege.BesiegerCamp.IsReadyToBesiege;
        }
    }
}
