using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Applies the AI side of the same finite Supply economy used by the player.
    /// It never changes destinations: procurement is attempted only after an AI
    /// party has already entered a safe friendly town.
    /// </summary>
    internal static class LogisticsAiProcurementService
    {
        internal const int ProvisionDays = 10;
        private const int GoldFloor = 5000;

        internal static int TryPurchase(
            MobileParty party,
            ItemObject supply,
            int currentReserve,
            float dailyUse)
        {
            if (party == null || party == MobileParty.MainParty || party.LeaderHero == null
                || party.LeaderHero.Clan == Clan.PlayerClan || supply == null)
            {
                return 0;
            }

            Settlement settlement = party.CurrentSettlement;
            if (settlement == null || settlement.Town == null || settlement.IsUnderSiege
                || settlement.MapFaction != party.MapFaction)
            {
                return 0;
            }

            int townStock;
            int supplyCratePrice;
            if (!LogisticsSupplyMarketService.TryGetOffer(
                settlement,
                party,
                supply,
                out townStock,
                out supplyCratePrice))
            {
                return 0;
            }
            int targetReserve = LogisticsSupplyMath.CalculateProvisionTarget(
                dailyUse,
                ProvisionDays);
            int crates = LogisticsSupplyMath.CalculateAiPurchaseCrates(
                currentReserve,
                targetReserve,
                townStock,
                party.LeaderHero.Gold,
                GoldFloor,
                supplyCratePrice,
                LogisticsReserveBehavior.ReservePerSupplyCrate);
            if (crates <= 0)
            {
                return 0;
            }

            return LogisticsSupplyMarketService.TryBuy(
                settlement,
                party,
                party.LeaderHero,
                supply,
                crates,
                supplyCratePrice) ? crates : 0;
        }
    }
}
