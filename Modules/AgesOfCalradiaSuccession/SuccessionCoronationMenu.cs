using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Localization;

namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionCoronationMenu
    {
        private static SuccessionCampaignBehavior _behavior { get { return SuccessionService.CurrentBehavior; } }

        internal static void Register(CampaignGameStarter starter, SuccessionCampaignBehavior behavior)
        {
            starter.AddGameMenuOption("town", "aoc_hold_coronation_town", "Hold a royal coronation", CanCoronate, Coronate, false, -1);
            starter.AddGameMenuOption("castle", "aoc_hold_coronation_castle", "Hold a royal coronation", CanCoronate, Coronate, false, -1);
        }

        private static bool CanCoronate(MenuCallbackArgs args)
        {
            Kingdom kingdom = Hero.MainHero == null || Hero.MainHero.Clan == null ? null : Hero.MainHero.Clan.Kingdom;
            bool ruler = kingdom != null && kingdom.Leader == Hero.MainHero;
            bool available = ruler && _behavior != null && !_behavior.IsAuthorityTransferBlocked(kingdom)
                && _behavior.GetMinorHeir(kingdom) == null && !_behavior.IsCoronated(kingdom);
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            args.IsEnabled = available;
            if (ruler && !available) args.Tooltip = new TextObject(_behavior != null && _behavior.IsAuthorityTransferBlocked(kingdom)
                ? "Succession has not been resolved. A coronation is unavailable."
                : _behavior != null && _behavior.GetMinorHeir(kingdom) != null
                ? "A regent cannot be crowned in place of the underage heir."
                : "This ruler has already been crowned.");
            return ruler;
        }

        private static void Coronate(MenuCallbackArgs args)
        {
            Kingdom kingdom = Hero.MainHero == null || Hero.MainHero.Clan == null ? null : Hero.MainHero.Clan.Kingdom;
            if (_behavior != null) _behavior.HoldCoronation(kingdom, Hero.MainHero, true);
        }
    }
}
