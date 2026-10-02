using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace AgesOfCalradiaLogistics
{
    /// <summary>Thin town-menu adapter for player-controlled provisioning.</summary>
    internal sealed class LogisticsMenuBehavior : CampaignBehaviorBase
    {
        private readonly LogisticsReserveBehavior _reserves;

        internal LogisticsMenuBehavior(LogisticsReserveBehavior reserves)
        {
            _reserves = reserves ?? throw new ArgumentNullException(nameof(reserves));
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, AddTownMenuOptions);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void AddTownMenuOptions(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption(
                "town",
                "aoc_logistics_load_baggage",
                "Load supplies into baggage train",
                CanLoadMainPartyBaggage,
                LoadMainPartyBaggage,
                isLeave: false,
                index: -1);
            starter.AddGameMenuOption(
                "town",
                "aoc_logistics_provision_baggage",
                "Ask the quartermaster to provision the baggage train",
                CanProvisionMainPartyBaggage,
                ProvisionMainPartyBaggage,
                isLeave: false,
                index: -1);
        }

        private bool CanLoadMainPartyBaggage(MenuCallbackArgs args)
        {
            Settlement settlement = Settlement.CurrentSettlement;
            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(LogisticsReserveBehavior.SupplyItemId);
            int reserve = _reserves.GetReserve(MobileParty.MainParty);
            int crateCount = supply == null ? 0 : MobileParty.MainParty.ItemRoster.GetItemNumber(supply);
            int capacity = LogisticsReserveBehavior.MaximumReserve - reserve;
            float projectedDays = LogisticsSupplyMath.CalculateDaysRemaining(
                reserve,
                _reserves.GetDailyUse(MobileParty.MainParty));

            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled = settlement != null && settlement.IsTown && crateCount > 0
                && capacity >= LogisticsReserveBehavior.ReservePerSupplyCrate;
            args.Text = new TextObject(
                "Load supplies into baggage train ({RESERVE}/{MAXIMUM}; {DAYS} days; {CRATES} crate(s) carried).");
            args.Text.SetTextVariable("RESERVE", reserve);
            args.Text.SetTextVariable("MAXIMUM", LogisticsReserveBehavior.MaximumReserve);
            args.Text.SetTextVariable("CRATES", crateCount);
            args.Text.SetTextVariable("DAYS", float.IsPositiveInfinity(projectedDays)
                ? "--"
                : projectedDays.ToString("0.0"));
            if (!args.IsEnabled)
            {
                args.Tooltip = new TextObject(
                    crateCount == 0
                        ? "Buy or capture Supply crates before loading the baggage train."
                        : "The baggage train does not have enough free capacity for another crate.");
            }

            return settlement != null && settlement.IsTown;
        }

        private void LoadMainPartyBaggage(MenuCallbackArgs args)
        {
            _reserves.LoadAllAvailableSupplyCrates(MobileParty.MainParty);
        }

        private bool CanProvisionMainPartyBaggage(MenuCallbackArgs args)
        {
            int crates;
            int unitPrice;
            int targetReserve;
            bool available = TryCalculateMainPartyProvision(out crates, out unitPrice, out targetReserve);
            args.optionLeaveType = GameMenuOption.LeaveType.Trade;
            args.IsEnabled = available;
            args.Text = new TextObject(
                "Provision for {DAYS} field days ({CRATES} crate(s), {COST} denars).");
            args.Text.SetTextVariable("DAYS", LogisticsAiProcurementService.ProvisionDays);
            args.Text.SetTextVariable("CRATES", crates);
            args.Text.SetTextVariable("COST", crates * unitPrice);
            if (!available)
            {
                args.Tooltip = new TextObject(
                    "This town lacks stock, the baggage train is already provisioned, or you cannot afford another crate.");
            }

            return Settlement.CurrentSettlement != null && Settlement.CurrentSettlement.IsTown;
        }

        private void ProvisionMainPartyBaggage(MenuCallbackArgs args)
        {
            int crates;
            int unitPrice;
            int targetReserve;
            if (!TryCalculateMainPartyProvision(out crates, out unitPrice, out targetReserve))
            {
                return;
            }

            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(LogisticsReserveBehavior.SupplyItemId);
            if (!LogisticsSupplyMarketService.TryBuy(
                Settlement.CurrentSettlement,
                MobileParty.MainParty,
                Hero.MainHero,
                supply,
                crates,
                unitPrice))
            {
                return;
            }
            _reserves.AddPurchasedSupplyCrates(MobileParty.MainParty, crates);
            LogisticsDiagnostics.Info(string.Format(
                "Quartermaster provisioned {0} Supply crate(s) for {1} denars; player reserve is now {2}/{3} (target {4}).",
                crates,
                crates * unitPrice,
                _reserves.GetReserve(MobileParty.MainParty),
                LogisticsReserveBehavior.MaximumReserve,
                targetReserve));
        }

        private bool TryCalculateMainPartyProvision(
            out int crates,
            out int unitPrice,
            out int targetReserve)
        {
            crates = 0;
            unitPrice = 0;
            targetReserve = 0;
            Settlement settlement = Settlement.CurrentSettlement;
            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(LogisticsReserveBehavior.SupplyItemId);
            if (settlement == null || settlement.Town == null || supply == null || Hero.MainHero == null)
            {
                return false;
            }

            int reserve = _reserves.GetReserve(MobileParty.MainParty);
            targetReserve = LogisticsSupplyMath.CalculateProvisionTarget(
                _reserves.GetDailyUse(MobileParty.MainParty),
                LogisticsAiProcurementService.ProvisionDays);
            int townStock;
            if (!LogisticsSupplyMarketService.TryGetOffer(
                settlement,
                MobileParty.MainParty,
                supply,
                out townStock,
                out unitPrice))
            {
                return false;
            }
            crates = LogisticsSupplyMath.CalculateAiPurchaseCrates(
                reserve,
                targetReserve,
                townStock,
                Hero.MainHero.Gold,
                0,
                unitPrice,
                LogisticsReserveBehavior.ReservePerSupplyCrate);
            return crates > 0;
        }
    }
}
