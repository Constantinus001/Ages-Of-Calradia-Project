using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// v1.4.8 EncounterManager.StartSettlementEncounter(MobileParty, Settlement) prefix.
    /// IL_03d2-0436 directly assigns a side by MapFaction for existing village battles;
    /// IL_04a8-04d1 drafts an inside player using kingdom identity, bypassing CanPartyJoinBattle.
    /// Resolve free NPC reinforcements by actual clan and join through PartyBase.MapEventSide,
    /// whose native setter adds party/event membership and invalidates simulation setup.
    /// Player defense of their own village retains the native path.
    /// Unregistered/native raids are untouched. Target discovery failure disables the patch set;
    /// native join failures are logged without inline retry or speculative rollback. Actual-source RaidArrivalVerifier covers dispatch
    /// boundaries; native IL audit and live arrival/defense tests remain the compatibility gates.
    /// </summary>
    [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartSettlementEncounter),
        new[] { typeof(MobileParty), typeof(Settlement) })]
    internal static class InternalWarRaidArrivalPatch
    {
        private static DateTime _nextLogUtc;

        [HarmonyPrefix]
        internal static bool Prefix(MobileParty attackerParty, Settlement settlement)
        {
            if (attackerParty == null || settlement == null || !settlement.IsVillage) return true;
            MapEvent battle = settlement.Party.MapEvent;
            if (battle != null && InternalWarTestService.IsRaidBattle(battle))
            {
                if (attackerParty == MobileParty.MainParty && Clan.PlayerClan != null
                    && settlement.OwnerClan == Clan.PlayerClan) return true;
                if (attackerParty != MobileParty.MainParty) JoinNpc(attackerParty, settlement, battle);
                else Block("Recorded private raid arrival withheld: the player is outside the defending clan.");
                return false;
            }
            if (InternalWarTestService.CanStartNpcRaid(attackerParty, settlement)
                && MobileParty.MainParty != null && MobileParty.MainParty.CurrentSettlement == settlement
                && (Clan.PlayerClan == null || settlement.OwnerClan != Clan.PlayerClan))
                return Block("Recorded NPC raid waits while a player outside the defending clan is inside the village.");
            return true;
        }

        private static void JoinNpc(MobileParty party, Settlement settlement, MapEvent battle)
        {
            if (!InternalWarCombatService.IsLandLord(party.Party) || battle.IsFinalized
                || battle.AttackerSide == null || battle.DefenderSide == null
                || settlement.Party.MapEvent != battle || !InternalWarTestService.IsRaidBattle(battle))
            { Block("Private raid reinforcement rejected: party or event is unavailable, committed, or unsupported."); return; }
            InternalConflictRecord war = InternalWarTestService.FindRaidConflict(battle);
            Clan clan = InternalWarTestService.ResolveClan(party);
            Clan attacker = InternalWarTestService.ResolveClan(battle.AttackerSide.LeaderParty);
            Clan defender = settlement.OwnerClan;
            if (war == null || war.Phase != InternalConflictPhase.Active || clan == null || attacker == null || defender == null
                || !InternalWarCombatService.Opposed(attacker, defender))
            { Block("Private raid reinforcement rejected: clan hostility is no longer active."); return; }
            BattleSideEnum side = clan == attacker ? BattleSideEnum.Attacker
                : clan == defender ? BattleSideEnum.Defender : BattleSideEnum.None;
            if (side == BattleSideEnum.None) { Block("Neutral clan cannot reinforce this private raid."); return; }
            try
            {
                if (!battle.CanPartyJoinBattle(party.Party, side) || war.Phase != InternalConflictPhase.Active
                    || !InternalWarCombatService.IsLandLord(party.Party) || settlement.Party.MapEvent != battle
                    || battle.IsFinalized || InternalWarTestService.FindRaidConflict(battle) != war
                    || InternalWarTestService.ResolveClan(party) != clan || settlement.OwnerClan != defender
                    || InternalWarTestService.ResolveClan(battle.AttackerSide.LeaderParty) != attacker
                    || !InternalWarCombatService.Opposed(attacker, defender)
                    || !InternalWarTestService.IsRaidBattle(battle)) return;
                party.Party.MapEventSide = side == BattleSideEnum.Attacker ? battle.AttackerSide : battle.DefenderSide;
            }
            catch (Exception exception)
            {
                // The native setter may have partially delivered AddInvolvedPartyInternal callbacks.
                // Never repeat it or remove a party speculatively after a failed callback.
                InternalWarTestService.BlockRecovery("Private raid reinforcement membership is uncertain after a native callback failure.");
                InternalWarTestDiagnostics.Error("Private raid reinforcement native join failed; retain diagnostics and inspect event membership.", exception);
            }
        }

        private static bool Block(string reason)
        {
            DateTime now = DateTime.UtcNow;
            if (now >= _nextLogUtc)
            {
                _nextLogUtc = now.AddMinutes(1);
                InternalWarTestDiagnostics.Info(reason);
            }
            return false;
        }
    }
}
