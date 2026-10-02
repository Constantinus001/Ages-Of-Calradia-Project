using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradiaInternalWarsTest
{
    internal static class InternalWarTestMenu
    {
        private static InternalWarTestBehavior _rootBehavior;
        private static InternalWarTestBehavior _behavior { get { return _rootBehavior == null ? null : _rootBehavior.SelectedController; } }
        internal static void Register(CampaignGameStarter starter, InternalWarTestBehavior behavior)
        {
            _rootBehavior = behavior;
            InternalWarRealmMenu.Register(starter, behavior, () => _rootBehavior);
            starter.AddGameMenuOption("village", "aoc_internal_raid", "[TEST] Raid this rival clan's village",
                CanRaid, BeginRaid, false, -1);
            foreach (string menu in new[] { "town", "castle", "village" })
            {
                starter.AddGameMenuOption(menu, "aoc_internal_declare_" + menu, "[TEST] Declare an internal clan war",
                    CanDeclare, Declare, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_war_" + menu, "[TEST] Player clan: choose an internal siege", CanStart, OpenTargets, false, -1);
            }
            foreach (string menu in new[] { "town", "castle" })
            {
                starter.AddGameMenuOption(menu, "aoc_test_internal_begin_siege_" + menu,
                    "[TEST] Begin siege of this settlement", CanBeginSiege, BeginSiege, false, -1);
            }
            // Native v1.4.8 SiegeEventCampaignBehavior owns menu_siege_strategies.
            // Recovery must remain reachable while the player occupies a siege camp.
            // No new test can start there; the native menu contract is audited before packaging.
            foreach (string menu in new[] { "town", "castle", "village", "menu_siege_strategies" })
            {
                starter.AddGameMenuOption(menu, "aoc_internal_negotiate_" + menu, "[TEST] Negotiate internal peace",
                    CanNegotiate, Negotiate, false, -1);
                starter.AddGameMenuOption(menu, "aoc_internal_history_" + menu, "[TEST] Previous private wars",
                    CanShowStatus, ShowHistory, false, -1);
                starter.AddGameMenuOption(menu, "aoc_internal_wars_" + menu, "[TEST] Select active private war",
                    CanShowStatus, SelectWar, false, -1);
                starter.AddGameMenuOption(menu, "aoc_internal_political_ai_" + menu, "[TEST] Toggle NPC clan declarations",
                    CanShowStatus, args => { if (_behavior != null) { _behavior.TogglePoliticalAi();
                        InformationManager.DisplayMessage(new InformationMessage("NPC declarations: " + _behavior.PoliticalAiEnabled)); } }, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_peace_" + menu, "[TEST] Monarch: force internal peace", CanForcePeace, ForcePeace, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_abort_" + menu, "[TEST] Emergency abort and cleanup", CanEmergencyAbort, EmergencyAbort, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_status_" + menu, "[TEST] Internal-war status", CanShowStatus, ShowStatus, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_report_" + menu, "[TEST] Write diagnostic snapshot", CanShowStatus, WriteReport, false, -1);
                starter.AddGameMenuOption(menu, "aoc_test_internal_ai_" + menu, "[TEST] Toggle siege/raid AI for this war", CanShowStatus, ToggleAi, false, -1);
            }
        }

        private static bool CanStart(MenuCallbackArgs args)
        {
            string blocker = InternalWarTestService.GetStartBlocker();
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            args.IsEnabled = string.IsNullOrEmpty(blocker);
            args.Tooltip = new TextObject(blocker);
            return true;
        }

        private static void SelectWar(MenuCallbackArgs args)
        {
            if (_behavior == null) return;
            InternalWarTestBehavior owner = _behavior;
            List<InquiryElement> entries = owner.Conflicts.Where(c => c.IsOpen).Select(c =>
                new InquiryElement(c.Id, c.AttackerClanId + " vs " + c.DefenderClanId + " / " + c.Phase, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Active private wars",
                "Choose the conflict controlled by the siege, raid and peace commands.", entries, true, 1, 1,
                "Select", "Cancel", selection =>
                {
                    if (_behavior != owner || selection == null || selection.Count != 1) return;
                    string result;
                    bool selected = owner.SelectConflict(selection[0].Identifier as string, out result);
                    InformationManager.DisplayMessage(new InformationMessage(selected ? "Private war selected." : result));
                }, null), true);
        }

        private static void ShowHistory(MenuCallbackArgs args)
        {
            if (_behavior == null) return;
            InformationManager.ShowInquiry(new InquiryData("Previous private wars", _behavior.GetWarHistory(),
                true, false, "Close", string.Empty, null, null), true);
        }

        private static bool CanNegotiate(MenuCallbackArgs args)
        {
            string blocker = _behavior == null ? "Test behavior unavailable." : _behavior.GetPeaceOfferBlocker(true);
            args.IsEnabled = string.IsNullOrEmpty(blocker);
            args.Tooltip = new TextObject(blocker);
            return true;
        }

        private static void Negotiate(MenuCallbackArgs args)
        {
            if (_behavior == null || _behavior.Conflict == null) return;
            InternalWarTestBehavior expectedBehavior = _behavior;
            string expectedConflictId = _behavior.Conflict.Id;
            string blocker = _behavior.GetPeaceOfferBlocker(false);
            var terms = new List<InquiryElement>
            {
                new InquiryElement(0, "Status-quo peace", null, string.IsNullOrEmpty(blocker),
                    string.IsNullOrEmpty(blocker) ? "Both clans keep their current fiefs." : blocker),
                new InquiryElement(1, "Concede and abandon the remaining claim", null, true,
                    "The rival accepts. Current fief ownership is retained; no compensation is charged."),
                new InquiryElement(2, "Offer 5,000 gold compensation", null, Hero.MainHero != null && Hero.MainHero.Gold >= 5000,
                    "Pay the rival leader immediately to settle this conflict. Both clans retain their current fiefs.")
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Negotiate private peace",
                "Choose terms. Active battles must finish safely before peace cleanup completes.", terms,
                true, 1, 1, "Offer terms", "Cancel", selected =>
                {
                    if (selected == null || selected.Count != 1) return;
                    if (_behavior != expectedBehavior)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("The campaign changed; reopen negotiations."));
                        return;
                    }
                    string result;
                    int term = (int)selected[0].Identifier;
                    if (term == 2) expectedBehavior.OfferCompensation(expectedConflictId, out result);
                    else expectedBehavior.OfferPeace(expectedConflictId, term == 1, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                }, null), true);
        }

        private static bool CanDeclare(MenuCallbackArgs args)
        {
            string blocker = InternalWarTestService.GetDeclarationBlocker();
            if (string.IsNullOrEmpty(blocker) && _behavior != null)
                blocker = _behavior.GetRealmDeclarationBlocker(Clan.PlayerClan == null ? null : Clan.PlayerClan.Kingdom);
            args.IsEnabled = string.IsNullOrEmpty(blocker);
            args.Tooltip = new TextObject(blocker);
            return true;
        }

        private static void Declare(MenuCallbackArgs args)
        {
            Clan player = Clan.PlayerClan;
            if (player == null || player.Kingdom == null || _behavior == null) return;
            InternalWarTestBehavior owner = _behavior;
            InternalConflictRecord selectedWar = owner.Conflict;
            List<InquiryElement> clans = Clan.All.Where(c => c != player && c.Kingdom == player.Kingdom && !c.IsEliminated && c.Leader != null && c.Leader.IsAlive
                && !c.IsMinorFaction && !c.IsClanTypeMercenary && !c.IsUnderMercenaryService)
                .OrderBy(c => c.Name.ToString()).Select(c => new InquiryElement(c, c.Name.ToString(), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Declare internal clan war", "Both clans stay in their kingdom. This enables field combat, raids and siege selection. Use a disposable save.",
                clans, true, 1, 1, "Declare war", "Cancel", selected =>
                {
                    if (selected == null || selected.Count != 1 || _behavior != owner || owner.Conflict != selectedWar) return;
                    string result;
                    _behavior.TryDeclare(selected[0].Identifier as Clan, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                }, null), true);
        }

        private static void OpenTargets(MenuCallbackArgs args)
        {
            InternalWarTestBehavior owner = _behavior;
            InternalConflictRecord selectedWar = owner == null ? null : owner.Conflict;
            List<InquiryElement> elements = EligibleTargets()
                .OrderBy(settlement => settlement.Name.ToString())
                .Select(settlement => new InquiryElement(settlement,
                    settlement.Name + " - " + settlement.OwnerClan.Name, null, true,
                    "Your clan and " + settlement.OwnerClan.Name + " remain inside " + settlement.OwnerClan.Kingdom.Name + "."))
                .ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "TEST ONLY: Player-Clan Internal Siege", "Choose the fortification your clan will attack. Use a backup save.",
                elements, true, 1, 1, "Select", "Cancel", selection =>
                { if (_behavior == owner && owner != null && owner.Conflict == selectedWar) ConfirmTarget(selection); }, null), true);
        }

        private static void ConfirmTarget(List<InquiryElement> selected)
        {
            Settlement target = selected == null || selected.Count == 0 ? null : selected[0].Identifier as Settlement;
            Clan attacker = Clan.PlayerClan;
            if (target == null || target.OwnerClan == null || attacker == null || attacker.Kingdom == null || _behavior == null) return;
            InternalWarTestBehavior owner = _behavior;
            InternalConflictRecord selectedWar = owner.Conflict;
            InformationManager.ShowInquiry(new InquiryData(
                "TEST: Lead your clan against " + target.Name + "?",
                "This starts a test feud between your clan and " + target.OwnerClan.Name + " while both stay inside "
                    + attacker.Kingdom.Name + ". Your party travels to the target. Enter its town or castle menu and choose "
                    + "[TEST] Begin siege of this settlement. Prepare the camp, then choose the native Lead an assault option.",
                true, true, "Start test", "Cancel",
                delegate
                {
                    if (_behavior != owner || owner.Conflict != selectedWar) return;
                    string result = "Internal-war test behavior is unavailable.";
                    bool success = _behavior != null && _behavior.TryStart(attacker, target, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                    if (!success) InternalWarTestDiagnostics.Info("TEST START REJECTED: " + result);
                }, null), true);
        }

        private static bool CanBeginSiege(MenuCallbackArgs args)
        {
            InternalWarTestRecord record = InternalWarTestService.Current;
            Settlement target = Settlement.CurrentSettlement;
            if (record == null || record.State != InternalWarTestState.Marching
                || target == null || target.StringId != record.SettlementId) return false;
            string blocker = InternalWarTestService.GetBeginSiegeBlocker(target);
            args.optionLeaveType = GameMenuOption.LeaveType.BesiegeTown;
            args.IsEnabled = string.IsNullOrEmpty(blocker);
            args.Tooltip = new TextObject(blocker);
            return true;
        }

        private static void BeginSiege(MenuCallbackArgs args)
        {
            string result = "Internal-war test behavior is unavailable.";
            bool success = _behavior != null && _behavior.TryBeginSiege(Settlement.CurrentSettlement, out result);
            InformationManager.DisplayMessage(new InformationMessage(result));
            if (!success) InternalWarTestDiagnostics.Info("TEST SIEGE START REJECTED: " + result);
        }

        private static bool CanForcePeace(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            bool monarch = Clan.PlayerClan != null && Clan.PlayerClan.Kingdom != null
                && Clan.PlayerClan.Kingdom.RulingClan == Clan.PlayerClan;
            args.IsEnabled = InternalWarTestService.HasOpenConflict && monarch;
            if (!InternalWarTestService.HasOpenConflict) args.Tooltip = new TextObject("No open internal-war test exists.");
            else if (!monarch) args.Tooltip = new TextObject("Only the kingdom's ruling clan can issue this order.");
            return true;
        }

        private static void ForcePeace(MenuCallbackArgs args)
        {
            InternalWarTestBehavior owner = _behavior;
            InternalConflictRecord expected = owner == null ? null : owner.Conflict;
            InternalWarTestRecord operation = owner == null ? null : owner.CurrentRecord;
            InformationManager.ShowInquiry(new InquiryData(
                "TEST: Force internal peace?", "Ends clan hostility and lifts the test siege. A live player battle exits before cleanup.",
                true, true, "Force peace", "Cancel",
                delegate
                {
                    if (_behavior != owner || owner == null || owner.Conflict != expected || owner.CurrentRecord != operation) return;
                    string result;
                    if (_behavior == null) result = "Internal-war test behavior is unavailable.";
                    else _behavior.RequestRoyalPeace(true, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                }, null), true);
        }

        private static bool CanEmergencyAbort(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Leave;
            args.IsEnabled = InternalWarTestService.HasOpenConflict;
            if (!args.IsEnabled) args.Tooltip = new TextObject("No operational internal-war test exists.");
            return true;
        }

        private static void EmergencyAbort(MenuCallbackArgs args)
        {
            InternalWarTestBehavior owner = _behavior;
            InternalConflictRecord expected = owner == null ? null : owner.Conflict;
            InternalWarTestRecord operation = owner == null ? null : owner.CurrentRecord;
            InformationManager.ShowInquiry(new InquiryData(
                "TEST: Emergency cleanup?", "Ends the test hostility, lifts its siege, and restores your main-party movement order.",
                true, true, "Abort test", "Cancel",
                delegate
                {
                    if (_behavior != owner || owner == null || owner.Conflict != expected || owner.CurrentRecord != operation) return;
                    string result;
                    if (_behavior == null) result = "Internal-war test behavior is unavailable.";
                    else _behavior.RequestRoyalPeace(false, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                }, null), true);
        }

        private static bool CanShowStatus(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            args.IsEnabled = _behavior != null;
            return true;
        }

        private static void ShowStatus(MenuCallbackArgs args)
        {
            string status = _behavior == null ? "Internal-war test behavior is unavailable." : _behavior.GetStatusText();
            InformationManager.ShowInquiry(new InquiryData("TEST: Internal-war status", status,
                true, false, "Close", string.Empty, null, null), true);
        }

        private static void WriteReport(MenuCallbackArgs args)
        {
            string path, message;
            InternalWarDiagnosticsReport.TryWrite(_behavior, "player-requested snapshot", out path, out message);
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private static void ToggleAi(MenuCallbackArgs args)
        {
            if (_behavior == null) return;
            _behavior.ToggleAutonomousAi();
            InformationManager.DisplayMessage(new InformationMessage("This war's siege/raid AI: " + (_behavior.AutonomousAi ? "ON" : "OFF")));
        }

        private static bool CanRaid(MenuCallbackArgs args)
        {
            string blocker = _behavior == null ? "Test behavior unavailable." : _behavior.GetRaidBlocker(Settlement.CurrentSettlement);
            args.IsEnabled = string.IsNullOrEmpty(blocker);
            args.Tooltip = new TextObject(blocker);
            return true;
        }
        private static void BeginRaid(MenuCallbackArgs args)
        {
            string result = "Test behavior unavailable.";
            if (_behavior != null) _behavior.TryBeginRaid(Settlement.CurrentSettlement, out result);
            InformationManager.DisplayMessage(new InformationMessage(result));
        }

        private static IEnumerable<Settlement> EligibleTargets()
        {
            return InternalWarTestService.GetEligibleTargets();
        }
    }
}
