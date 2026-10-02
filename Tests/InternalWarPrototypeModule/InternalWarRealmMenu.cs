using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradiaInternalWarsTest
{
    // Native inquiry adapter only. Realm/ruler authority is checked again by the behavior on confirmation.
    internal static class InternalWarRealmMenu
    {
        internal static void Register(CampaignGameStarter starter, InternalWarTestBehavior owner, Func<InternalWarTestBehavior> current)
        {
            foreach (string menu in new[] { "town", "castle", "village", "menu_siege_strategies" })
            {
                starter.AddGameMenuOption(menu, "aoc_realm_overview_" + menu, "[TEST] Kingdom private-war overview",
                    args => { args.IsEnabled = current() == owner; args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                    args => { if (current() == owner) InformationManager.ShowInquiry(new InquiryData("Kingdom private wars",
                        owner.GetRealmOverview(), true, false, "Close", string.Empty, null, null), true); }, false, -1);
                starter.AddGameMenuOption(menu, "aoc_realm_controls_" + menu, "[TEST] Monarch: kingdom private-war controls",
                    args =>
                    {
                        string id = Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null ? null : Clan.PlayerClan.Kingdom.StringId;
                        string blocker = current() == owner ? owner.GetRealmAuthorityBlocker(id) : "Campaign changed.";
                        args.IsEnabled = string.IsNullOrEmpty(blocker); args.Tooltip = new TextObject(blocker);
                        args.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true;
                    }, args => ShowControls(owner, current), false, -1);
            }
        }

        private static void ShowControls(InternalWarTestBehavior owner, Func<InternalWarTestBehavior> current)
        {
            if (current() != owner || Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null) return;
            string kingdomId = Clan.PlayerClan.Kingdom.StringId;
            if (!string.IsNullOrEmpty(owner.GetRealmAuthorityBlocker(kingdomId))) return;
            bool banned = owner.RealmDeclarationsBanned(kingdomId);
            var options = new List<InquiryElement> {
                new InquiryElement("policy", banned ? "Permit new private wars" : "Forbid new private wars", null),
                new InquiryElement("peace", "Order peace in every active private war in this kingdom", null)
            };
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Royal private-war policy",
                "The declaration policy applies to players and NPCs and survives ruler succession. A peace order queues cleanup; it does not interrupt battles or reverse captures.",
                options, true, 1, 1, "Review order", "Cancel", selection =>
                {
                    if (current() != owner || selection == null || selection.Count != 1) return;
                    string action = selection[0].Identifier as string;
                    if (action != "policy" && action != "peace") return;
                    string explanation = action == "peace" ? "Queue peace for all currently active private wars in this kingdom? Later declarations remain permitted unless separately forbidden."
                        : banned ? "Permit new private-war declarations again?" : "Forbid new player and NPC declarations? Existing wars are not ended by this policy alone.";
                    InformationManager.ShowInquiry(new InquiryData("Confirm royal order", explanation, true, true, "Issue order", "Cancel", () =>
                    {
                        if (current() != owner) return;
                        string result;
                        if (action == "peace") owner.RequestRealmPeace(kingdomId, out result);
                        else owner.SetRealmDeclarationBan(kingdomId, banned, !banned, out result);
                        InformationManager.DisplayMessage(new InformationMessage(result));
                    }, null), true);
                }, null), true);
        }
    }
}
