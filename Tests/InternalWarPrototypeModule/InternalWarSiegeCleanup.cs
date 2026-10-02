using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;

namespace AgesOfCalradiaInternalWarsTest
{
    // Owns only native siege cleanup and transient UI references. Domain outcomes stay in the behavior/records.
    internal sealed class InternalWarSiegeCleanup
    {
        private readonly InternalWarTestBehavior _owner;
        internal InternalWarSiegeCleanup(InternalWarTestBehavior owner) { _owner = owner; }
        private InternalWarTestRecord CurrentRecord { get { return _owner.CurrentRecord; } }
        private InternalConflictRecord Conflict { get { return _owner.Conflict; } }
        private MenuContext _ownedMenuContext;
        private GameMenu _ownedMenu;
        private PlayerEncounter _ownedEncounter;
        internal void Reset() { _ownedMenuContext = null; _ownedMenu = null; _ownedEncounter = null; }
        private static Settlement FindSettlement(string id) { return InternalWarTestBehavior.FindSettlement(id); }
        private static MobileParty FindParty(string id) { return InternalWarTestBehavior.FindParty(id); }
        private static Clan FindClan(string id) { return InternalWarTestBehavior.FindClan(id); }
        private static void EndDiplomacy(Clan first, Clan second) { InternalWarTestBehavior.EndDiplomacy(first, second); }
        private bool HasConflictFieldBattles() { return InternalWarTestBehavior.HasConflictFieldBattles(Conflict); }
        private bool CleanupPostconditions(Settlement target, MobileParty leader, MenuContext context, GameMenu menu)
        { return _owner.CleanupPostconditions(target, leader, context, menu); }

        private void CleanupSiege(InternalWarTestState finalState, MenuContext testMenuContext, GameMenu testMenu, PlayerEncounter testEncounter)
        {
            InternalWarTestRecord record = CurrentRecord;
            if (record == null) return;
            Settlement target = FindSettlement(record.SettlementId);
            MobileParty leader = FindParty(record.LeaderPartyId);
            if (target != null && target.Party.MapEvent == null && target.SiegeEvent != null)
            {
                SiegeEvent siege = target.SiegeEvent;
                BesiegerCamp camp = siege.BesiegerCamp;
                if (camp != null && leader != null && camp.LeaderParty == leader)
                {
                    camp.RemoveAllSiegeParties();
                    if (target.SiegeEvent == siege) siege.FinalizeSiegeEvent();
                }
                else if (leader != null && camp != null && leader.BesiegerCamp == camp)
                {
                    // v1.4.8 setter also removes attached parties, and native removal finalizes an empty camp.
                    // Detach only this free participant when a separate active leader remains in the foreign camp.
                    MobileParty foreignLeader = camp.LeaderParty;
                    if (leader.Party.MapEvent != null || leader.Army != null || leader.AttachedParties.Count != 0
                        || foreignLeader == null || !foreignLeader.IsActive || foreignLeader.BesiegerCamp != camp)
                        throw new InvalidOperationException("Cannot safely detach the test party from a foreign siege; cleanup remains pending.");
                    leader.BesiegerCamp = null;
                }
            }
            RestoreLeaderControl();
            ExitTestSiegeMenu(target, leader, testMenuContext, testMenu, testEncounter);
            if (!CleanupPostconditions(target, leader, testMenuContext, testMenu)) return;
            _owner.CompleteCleanup(record, finalState);
        }

        private static void ExitTestSiegeMenu(Settlement target, MobileParty leader, MenuContext testMenuContext,
            GameMenu testMenu, PlayerEncounter testEncounter)
        {
            if (testMenuContext == null || target == null || leader == null || leader != MobileParty.MainParty
                || Campaign.Current.CurrentMenuContext != testMenuContext || testMenuContext.GameMenu != testMenu
                || target.SiegeEvent != null || target.Party.MapEvent != null || leader.Party.MapEvent != null
                || leader.BesiegerCamp != null) return;
            if (testEncounter != null && PlayerEncounter.Current == testEncounter
                && PlayerEncounter.EncounterSettlement == target && PlayerEncounter.Battle == null)
                PlayerEncounter.Finish(true);
            else if (PlayerEncounter.Current == null)
                GameMenu.ExitToLast();
        }

        private void CaptureTestSiegeMenu(out MenuContext menuContext, out GameMenu menu, out PlayerEncounter encounter)
        {
            menuContext = null;
            menu = null;
            encounter = null;
            InternalWarTestRecord record = CurrentRecord;
            Settlement target = record == null ? null : FindSettlement(record.SettlementId);
            MobileParty leader = record == null ? null : FindParty(record.LeaderPartyId);
            SiegeEvent siege = target == null ? null : target.SiegeEvent;
            MenuContext currentMenu = Campaign.Current.CurrentMenuContext;
            if (leader == null || leader != MobileParty.MainParty || siege == null || siege.BesiegerCamp == null
                || siege.BesiegerCamp.LeaderParty != leader || leader.BesiegerCamp != siege.BesiegerCamp
                || PlayerSiege.PlayerSiegeEvent != siege || currentMenu == null || currentMenu.GameMenu == null
                || currentMenu.GameMenu.StringId != "menu_siege_strategies") return;
            menuContext = currentMenu;
            menu = currentMenu.GameMenu;
            encounter = PlayerEncounter.Current;
        }

        internal void RestoreLeaderControl()
        {
            InternalWarTestRecord record = CurrentRecord;
            MobileParty leader = record == null ? null : FindParty(record.LeaderPartyId);
            if (leader == null) return;
            // Do not repeatedly cancel a new player order or a foreign encounter while recovery waits.
            if (leader.Party.MapEvent == null && leader.BesiegerCamp == null
                && leader.TargetSettlement == FindSettlement(record.SettlementId)) leader.SetMoveModeHold();
            if (leader != MobileParty.MainParty)
            {
                leader.Ai.SetDoNotMakeNewDecisions(false);
                leader.Ai.RethinkAtNextHourlyTick = true;
            }
        }

        internal void ContinuePendingCleanup()
        {
            InternalWarTestRecord record = CurrentRecord;
            if (record == null || record.State != InternalWarTestState.RoyalPeacePending) return;
            if (HasConflictFieldBattles()) return;
            // Capture before peace dispatch: native diplomacy listeners can detach the camp.
            MenuContext testMenuContext;
            GameMenu testMenu;
            PlayerEncounter testEncounter;
            CaptureTestSiegeMenu(out testMenuContext, out testMenu, out testEncounter);
            if (testMenuContext != null)
            {
                _ownedMenuContext = testMenuContext;
                _ownedMenu = testMenu;
                _ownedEncounter = testEncounter;
            }
            // Native result callbacks only queue intent. This method runs from the guarded map tick.
            // A continuing conflict keeps its stance after conquest; peace is a political decision.
            if (Conflict == null || Conflict.Phase != InternalConflictPhase.Active)
                EndDiplomacy(FindClan(record.AttackerClanId), FindClan(record.DefenderClanId));
            Settlement target = FindSettlement(record.SettlementId);
            MobileParty leader = FindParty(record.LeaderPartyId);
            MapEvent mapEvent = target == null ? null : target.Party.MapEvent;
            if (mapEvent != null)
            {
                SiegeEvent siege = target.SiegeEvent;
                if (!record.CaptureApplied && (Conflict == null || Conflict.Phase != InternalConflictPhase.Active)
                    && (mapEvent.IsSiegeAssault || InternalWarSallyService.Find(mapEvent) == record || InternalWarReliefService.Find(mapEvent) == record)
                    && leader != null && leader.Party.MapEvent == mapEvent && siege != null
                    && siege.BesiegerCamp != null && siege.BesiegerCamp.LeaderParty == leader)
                    mapEvent.DiplomaticallyFinished = true;
                // Never terminate an unrelated party's battle; retry once it has finished naturally.
                return;
            }
            CleanupSiege(record.PendingFinalState, _ownedMenuContext, _ownedMenu, _ownedEncounter);
        }
    }
}
