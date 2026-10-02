using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace AgesOfCalradiaReligions.Shipwright
{
    public sealed class ShipwrightCampaignBehavior : CampaignBehaviorBase
    {
        private const string TownMenuId = "town";
        private const string CommissionOptionId = "aoc_shipwright_commission_dromon";

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddTownMenuOption);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private static void AddTownMenuOption(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(
                TownMenuId,
                CommissionOptionId,
                "Commission a custom Dromon",
                CanCommissionDromon,
                CommissionDromon,
                isLeave: false,
                index: -1);
        }

        private static bool CanCommissionDromon(MenuCallbackArgs args)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            bool isPortTown = settlement != null && settlement.IsTown && settlement.HasPort;
            if (!isPortTown)
            {
                return false;
            }

            string contractFailure;
            bool contractAvailable = NavalContractVerifier.TryValidate(out contractFailure);
            bool hasMainParty = MobileParty.MainParty != null && MobileParty.MainParty.Party != null;

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled = contractAvailable && hasMainParty && settlement.Party != null;
            if (!args.IsEnabled)
            {
                args.Tooltip = new TextObject(
                    contractAvailable
                        ? "The shipwright cannot identify the player or this port's trading party."
                        : "The War Sails ship-building contract is unavailable: {REASON}");
                args.Tooltip.SetTextVariable("REASON", contractFailure ?? "unknown compatibility failure");
            }

            return true;
        }

        private static void CommissionDromon(MenuCallbackArgs args)
        {
            ShipwrightPortLauncher.OpenDromonCommissioning();
        }
    }
}
