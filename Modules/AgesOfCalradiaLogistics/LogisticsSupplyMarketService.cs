using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// The only adapter permitted to trade Supply. Settlement.ItemRoster is the
    /// Bannerlord market; Town.Owner is a PartyBase and is not market stock.
    /// </summary>
    internal static class LogisticsSupplyMarketService
    {
        internal static bool TryGetOffer(
            Settlement settlement,
            MobileParty buyer,
            ItemObject supply,
            out int availableCrates,
            out int unitPrice)
        {
            availableCrates = 0;
            unitPrice = 0;
            Town town = settlement == null ? null : settlement.Town;
            if (town == null || buyer == null || supply == null)
            {
                return false;
            }

            availableCrates = settlement.ItemRoster.GetItemNumber(supply);
            unitPrice = town.GetItemPrice(new EquipmentElement(supply), buyer);
            return availableCrates > 0 && unitPrice > 0;
        }

        internal static bool TryBuy(
            Settlement settlement,
            MobileParty buyer,
            Hero payer,
            ItemObject supply,
            int crates,
            int unitPrice)
        {
            if (settlement == null || buyer == null || payer == null || supply == null
                || crates <= 0 || unitPrice <= 0 || settlement.ItemRoster.GetItemNumber(supply) < crates)
            {
                return false;
            }

            int totalCost = crates * unitPrice;
            if (payer.Gold < totalCost)
            {
                return false;
            }

            settlement.ItemRoster.AddToCounts(supply, -crates);
            GiveGoldAction.ApplyBetweenCharacters(payer, null, totalCost, disableNotification: true);
            return true;
        }

        internal static void Produce(Settlement settlement, ItemObject supply, int crates)
        {
            if (settlement != null && supply != null && crates > 0)
            {
                settlement.ItemRoster.AddToCounts(supply, crates);
            }
        }
    }
}
