using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionDebugMenu
    {
        private static SuccessionCampaignBehavior _behavior { get { return SuccessionService.CurrentBehavior; } }

        internal static void Register(CampaignGameStarter starter, SuccessionCampaignBehavior behavior)
        {
            starter.AddGameMenuOption("town", "aoc_debug_kill_ruler_town", "[DEBUG] Kill a ruler to test succession", CanOpen, Open, false, -1);
            starter.AddGameMenuOption("castle", "aoc_debug_kill_ruler_castle", "[DEBUG] Kill a ruler to test succession", CanOpen, Open, false, -1);
            starter.AddGameMenuOption("village", "aoc_debug_kill_ruler_village", "[DEBUG] Kill a ruler to test succession", CanOpen, Open, false, -1);
            starter.AddGameMenuOption("town", "aoc_debug_succession_war_town", "[DEBUG] Cause a succession civil war", CanStartCivilWar, OpenCivilWar, false, -1);
            starter.AddGameMenuOption("castle", "aoc_debug_succession_war_castle", "[DEBUG] Cause a succession civil war", CanStartCivilWar, OpenCivilWar, false, -1);
            starter.AddGameMenuOption("village", "aoc_debug_succession_war_village", "[DEBUG] Cause a succession civil war", CanStartCivilWar, OpenCivilWar, false, -1);
        }

        private static bool CanOpen(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            bool hasTarget = Kingdom.All.Any(k => IsValidTarget(k == null ? null : k.Leader));
            args.IsEnabled = hasTarget;
            if (!hasTarget) args.Tooltip = new TextObject("No living non-player ruler is available.");
            return true;
        }

        private static void Open(MenuCallbackArgs args)
        {
            List<InquiryElement> rulers = Kingdom.All
                .Where(k => k != null && !k.IsEliminated && k.Leader != null && k.Leader.IsAlive)
                .OrderBy(k => k.Name.ToString(), StringComparer.CurrentCulture)
                .Select(k => new InquiryElement(
                    k.Leader,
                    k.Name + " — " + k.Leader.Name,
                    null,
                    IsValidTarget(k.Leader),
                    k.Leader == Hero.MainHero
                        ? "The debug tool will not kill the player character."
                        : BuildTooltip(k)))
                .ToList();

            MBInformationManager.ShowMultiSelectionInquiry(
                new MultiSelectionInquiryData(
                    "Succession Debug: Select Ruler",
                    "Choose one ruler to kill. You will receive a final confirmation before the campaign is changed.",
                    rulers,
                    true,
                    1,
                    1,
                    "Select",
                    "Cancel",
                    ConfirmSelection,
                    null),
                true);
        }

        private static void ConfirmSelection(List<InquiryElement> selected)
        {
            Hero ruler = selected == null || selected.Count == 0 ? null : selected[0].Identifier as Hero;
            if (!IsValidTarget(ruler))
            {
                InformationManager.DisplayMessage(new InformationMessage("That ruler is no longer a valid debug target."));
                return;
            }

            Kingdom kingdom = ruler.Clan == null ? null : ruler.Clan.Kingdom;
            string realmName = kingdom == null ? "their realm" : kingdom.Name.ToString();
            InformationManager.ShowInquiry(new InquiryData(
                "Kill " + ruler.Name + "?",
                "This debug action permanently kills " + ruler.Name + ", ruler of " + realmName
                    + ", and immediately tests hereditary succession. Save first if you may want to undo it.",
                true,
                true,
                "Kill ruler",
                "Cancel",
                delegate { Execute(ruler, kingdom); },
                null), true);
        }

        private static void Execute(Hero ruler, Kingdom expectedKingdom)
        {
            if (!IsValidTarget(ruler) || expectedKingdom == null || expectedKingdom.Leader != ruler)
            {
                InformationManager.DisplayMessage(new InformationMessage("The ruler changed before the debug action could run."));
                return;
            }

            SuccessionDiagnostics.Info("DEBUG ruler death requested: " + ruler.Name + " of " + expectedKingdom.Name + ".");
            KillCharacterAction.ApplyByOldAge(ruler, true);
        }

        private static bool IsValidTarget(Hero hero)
        {
            return hero != null && hero != Hero.MainHero && hero.IsAlive && hero.IsActive && hero.IsKingdomLeader;
        }

        private static string LawName(SuccessionLaw law)
        {
            switch (law)
            {
                case SuccessionLaw.MalePreferencePrimogeniture: return "male-preference primogeniture";
                case SuccessionLaw.AgnaticPrimogeniture: return "agnatic primogeniture";
                case SuccessionLaw.HouseSeniority: return "house seniority";
                case SuccessionLaw.NomadicHouseSeniority: return "nomadic house seniority";
                default: return "absolute primogeniture";
            }
        }

        private static string BuildTooltip(Kingdom kingdom)
        {
            Hero heir = SuccessionService.GetUnderageHeir(kingdom);
            Hero regent = SuccessionService.GetRegent(kingdom);
            string status = heir == null
                ? "No active regency."
                : "Regency: " + (regent == null ? "vacant" : regent.Name.ToString()) + " governs for heir " + heir.Name + ".";
            Hero pretender = SuccessionService.GetPretender(kingdom);
            return "Law: " + LawName(SuccessionService.GetLaw(kingdom)) + ". Legitimacy: "
                + SuccessionService.GetLegitimacy(kingdom).ToString("0", CultureInfo.InvariantCulture) + ". " + status
                + " Pretender: " + (pretender == null ? "none" : pretender.Name.ToString()) + "."
                + " This uses Bannerlord's old-age death action and triggers the normal succession pipeline.";
        }

        private static bool CanStartCivilWar(MenuCallbackArgs args)
        {
            bool available = _behavior != null && Kingdom.All.Any(k => k != null && !k.IsEliminated && k.Clans.Count > 1
                && _behavior.GetCivilWarPretender(k) != null);
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            args.IsEnabled = available;
            if (!available) args.Tooltip = new TextObject("No realm currently has an eligible non-ruling claimant clan.");
            return true;
        }

        private static void OpenCivilWar(MenuCallbackArgs args)
        {
            List<InquiryElement> realms = Kingdom.All.Where(k => k != null && !k.IsEliminated)
                .OrderBy(k => k.Name.ToString(), StringComparer.CurrentCulture)
                .Select(k =>
                {
                    Hero claimant = _behavior == null ? null : _behavior.GetCivilWarPretender(k);
                    int supporters = claimant == null ? 0 : _behavior.GetCivilWarSupporters(k, claimant).Count;
                    return new InquiryElement(k, k.Name + " — claimant " + (claimant == null ? "none" : claimant.Name.ToString()), null,
                        claimant != null,
                        "Legitimacy: " + (_behavior == null ? "?" : _behavior.GetLegitimacy(k).ToString("0", CultureInfo.InvariantCulture))
                        + ". Defecting clans: " + supporters + ". The opening 20-day peace may immediately suspend hostilities.");
                }).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Debug Succession Civil War",
                "Select a realm. Its strongest eligible claimant and opposition clans will form a separate claimant kingdom.",
                realms, true, 1, 1, "Select", "Cancel", ConfirmCivilWar, null), true);
        }

        private static void ConfirmCivilWar(List<InquiryElement> selected)
        {
            Kingdom kingdom = selected == null || selected.Count == 0 ? null : selected[0].Identifier as Kingdom;
            Hero claimant = kingdom == null || _behavior == null ? null : _behavior.GetCivilWarPretender(kingdom);
            if (kingdom == null || claimant == null) return;
            List<Clan> supporters = _behavior.GetCivilWarSupporters(kingdom, claimant);
            InformationManager.ShowInquiry(new InquiryData(
                "Start a succession war in " + kingdom.Name + "?",
                claimant.Name + " and " + supporters.Count + " clan(s) will leave " + kingdom.Name
                    + ", form a claimant kingdom, and declare war. This permanently changes the campaign; save first.",
                true, true, "Start civil war", "Cancel",
                delegate
                {
                    string result;
                    bool success = SuccessionCivilWar.TryStart(kingdom, claimant, supporters, _behavior, out result);
                    InformationManager.DisplayMessage(new InformationMessage(result));
                    if (!success) SuccessionDiagnostics.Info("DEBUG civil war request rejected: " + result);
                }, null), true);
        }
    }
}
