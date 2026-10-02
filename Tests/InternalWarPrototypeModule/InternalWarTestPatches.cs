using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>SiegeEvent.CanPartyJoinSide v1.4.8: replaces only the active test siege's kingdom-side test.</summary>
    [HarmonyPatch(typeof(SiegeEvent), nameof(SiegeEvent.CanPartyJoinSide))]
    internal static class TestSiegeJoinSidePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SiegeEvent __instance, PartyBase party, BattleSideEnum side, ref bool __result)
        {
            if (__instance == null || __instance.BesiegedSettlement == null || __instance.BesiegedSettlement.SiegeEvent != __instance) return true;
            InternalWarTestRecord record = InternalWarTestService.Find(__instance == null ? null : __instance.BesiegedSettlement);
            if (record == null) return true;
            __result = InternalWarTestService.CanJoin(record, party, side);
            return false;
        }
    }

    /// <summary>MobileParty.BesiegerCamp setter v1.4.8: prevents defender and neutral parties entering the active test camp.</summary>
    [HarmonyPatch(typeof(MobileParty), "set_BesiegerCamp")]
    internal static class TestBesiegerCampMembershipPatch
    {
        [HarmonyPrefix]
        private static void Prefix(MobileParty __instance, ref BesiegerCamp value)
        {
            if (value == null || value.SiegeEvent == null || value.SiegeEvent.BesiegedSettlement == null
                || value.SiegeEvent.BesiegedSettlement.SiegeEvent != value.SiegeEvent) return;
            InternalWarTestRecord record = InternalWarTestService.Find(value.SiegeEvent.BesiegedSettlement);
            if (record == null) return;
            if (__instance == null || !InternalWarTestService.CanJoin(record, __instance.Party, BattleSideEnum.Attacker)) value = null;
        }
    }

    /// <summary>Town.GetDefenderParties v1.4.8: restores owner-clan defenders hidden by shared MapFaction.</summary>
    [HarmonyPatch(typeof(Town), nameof(Town.GetDefenderParties))]
    internal static class TestTownDefenderPartiesPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Town __instance, MapEvent.BattleTypes battleType, ref IEnumerable<PartyBase> __result)
        {
            if (InternalWarTestService.Find(__instance == null ? null : __instance.Settlement) == null) return true;
            __result = InternalWarTestService.DefenderParties(__instance, battleType);
            return false;
        }
    }

    /// <summary>Town.GetNextDefenderParty v1.4.8: applies the same test defender list to incremental enumeration.</summary>
    [HarmonyPatch(typeof(Town), nameof(Town.GetNextDefenderParty))]
    internal static class TestTownNextDefenderPartyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Town __instance, ref int partyIndex, MapEvent.BattleTypes battleType, ref PartyBase __result)
        {
            if (InternalWarTestService.Find(__instance == null ? null : __instance.Settlement) == null) return true;
            List<PartyBase> parties = InternalWarTestService.DefenderParties(__instance, battleType).ToList();
            int next = partyIndex + 1;
            partyIndex = next;
            __result = next >= 0 && next < parties.Count ? parties[next] : null;
            return false;
        }
    }

    /// <summary>
    /// Internal MapEvent.Update v1.4.8: replaces its one leader-MapFaction war query. If the IL pattern changes,
    /// the transpiler throws and the submodule removes every test patch rather than running partially.
    /// </summary>
    [HarmonyPatch]
    internal static class TestMapEventContinuityPatch
    {
        private static MethodBase TargetMethod() { return AccessTools.Method(typeof(MapEvent), "Update"); }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> result = instructions.ToList();
            MethodInfo nativeWar = AccessTools.Method(typeof(IFaction), nameof(IFaction.IsAtWarWith));
            MethodInfo resolver = AccessTools.Method(typeof(TestMapEventContinuityPatch), nameof(IsAtWarForMapEvent));
            int replacements = 0;
            for (int index = 0; index < result.Count; index++)
            {
                if (!result[index].Calls(nativeWar)) continue;
                CodeInstruction original = result[index];
                CodeInstruction loadMapEvent = new CodeInstruction(OpCodes.Ldarg_0);
                loadMapEvent.labels.AddRange(original.labels);
                loadMapEvent.blocks.AddRange(original.blocks);
                original.labels.Clear();
                original.blocks.Clear();
                result.Insert(index, loadMapEvent);
                index++;
                original.opcode = OpCodes.Call;
                original.operand = resolver;
                replacements++;
            }
            if (replacements != 1)
                throw new InvalidOperationException("MapEvent.Update expected one IsAtWarWith call, found " + replacements + ".");
            return result;
        }

        private static bool IsAtWarForMapEvent(IFaction first, IFaction second, MapEvent mapEvent)
        {
            return InternalWarTestService.Find(mapEvent) != null || InternalWarSallyService.Find(mapEvent) != null
                || InternalWarReliefService.Find(mapEvent) != null
                || InternalWarCombatService.IsConflictFieldBattle(mapEvent)
                || InternalWarTestService.IsRaidBattle(mapEvent)
                || FactionManager.IsAtWarAgainstFaction(first, second);
        }
    }

    /// <summary>MapEvent.CanPartyJoinBattle v1.4.8: assigns only the two registered test clans.</summary>
    [HarmonyPatch(typeof(MapEvent), nameof(MapEvent.CanPartyJoinBattle))]
    internal static class TestMapEventJoinPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(MapEvent __instance, PartyBase party, BattleSideEnum side, ref bool __result)
        {
            InternalWarTestRecord record = InternalWarTestService.Find(__instance);
            InternalWarTestRecord relief = InternalWarReliefService.Find(__instance);
            if (relief != null)
            {
                Clan clan = InternalWarTestService.ResolveClan(party);
                __result = InternalWarReliefService.BattleSideFor(relief, __instance, clan) == side
                    && InternalWarTestService.CanJoin(relief, party, InternalWarTestService.SideOf(relief, clan));
                return false;
            }
            InternalWarTestRecord sally = InternalWarSallyService.Find(__instance);
            if (sally != null)
            {
                BattleSideEnum siegeSide = side == BattleSideEnum.Attacker ? BattleSideEnum.Defender
                    : side == BattleSideEnum.Defender ? BattleSideEnum.Attacker : BattleSideEnum.None;
                __result = InternalWarTestService.CanJoin(sally, party, siegeSide);
                return false;
            }
            if (record == null && InternalWarTestService.IsRaidBattle(__instance))
            {
                __result = InternalWarRaidJoinPolicy.CanJoin(InternalWarTestService.FindRaidConflict(__instance), __instance, party, side);
                return false;
            }
            if (record == null && InternalWarCombatService.IsConflictFieldBattle(__instance))
            {
                __result = InternalWarCombatService.CanJoinFieldBattle(__instance, party, side);
                return false;
            }
            if (record == null) return true;
            __result = InternalWarTestService.CanJoin(record, party, side);
            return false;
        }
    }

    /// <summary>ChangeOwnerOfSettlementAction.ApplyBySiege v1.4.8: redirects only an authorized player battle capture.
    /// Actor, side and event guards fail to native behavior. Finalizer clears transaction context even on native exceptions.
    /// Compatibility: exact-build gate; service negative tests and native audit, plus required live capture regression.</summary>
    [HarmonyPatch(typeof(ChangeOwnerOfSettlementAction), nameof(ChangeOwnerOfSettlementAction.ApplyBySiege))]
    internal static class TestSiegeOwnerPatch
    {
        internal static InternalWarTestRecord AuthorizedRecord;
        [HarmonyPrefix]
        private static void Prefix(ref Hero newOwner, Hero capturerHero, Settlement settlement, out InternalWarTestRecord __state)
        {
            __state = null;
            if (AuthorizedRecord != null || !InternalWarTestService.CanAuthorizeCapture(capturerHero, settlement)) return;
            InternalWarTestRecord record = InternalWarTestService.Find(settlement);
            Clan capturerClan = record == null ? null : InternalWarTestBehavior.FindClan(record.AttackerClanId);
            if (record == null || capturerClan == null) return;
            if (settlement.OwnerClan == null || settlement.OwnerClan.StringId != record.DefenderClanId) return;
            Hero recipient = capturerClan.Leader ?? (capturerHero != null && capturerHero.Clan == capturerClan ? capturerHero : null);
            if (recipient != null)
            {
                __state = record;
                AuthorizedRecord = record;
                newOwner = recipient;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Settlement settlement, InternalWarTestRecord __state)
        {
            if (InternalWarTestService.OwnsOperation(__state))
                InternalWarTestService.MarkCapture(settlement);
        }

        [HarmonyFinalizer]
        private static void Finalizer(InternalWarTestRecord __state)
        {
            if (__state != null && AuthorizedRecord == __state) AuthorizedRecord = null;
        }
    }

    /// <summary>SettlementClaimantCampaignBehavior.OnSettlementOwnerChanged v1.4.8: suppresses ordinary kingdom redistribution.</summary>
    [HarmonyPatch(typeof(SettlementClaimantCampaignBehavior), nameof(SettlementClaimantCampaignBehavior.OnSettlementOwnerChanged))]
    internal static class TestSiegeClaimantPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Settlement settlement, ref bool openToClaim, Hero newOwner,
            ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            InternalWarTestRecord record = InternalWarTestService.Find(settlement);
            if (record == null || TestSiegeOwnerPatch.AuthorizedRecord != record
                || detail != ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail.BySiege) return;
            if (newOwner != null && newOwner.Clan != null && newOwner.Clan.StringId == record.AttackerClanId) openToClaim = false;
        }
    }
}
