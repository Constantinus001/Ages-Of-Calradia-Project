using TaleWorlds.CampaignSystem;
namespace AgesOfCalradiaLogistics
{
    // Optional structural ABI. Standalone Logistics does not acquire a hard Core dependency.
    public static class CampaignSystemsApi
    {
        public static int ApiVersion { get { return 1; } }
        public static int ReadReserve(string partyId)
        {
            var behavior=Campaign.Current==null?null:Campaign.Current.GetCampaignBehavior<LogisticsReserveBehavior>();
            return behavior==null?-1:behavior.ReadExistingReserve(partyId);
        }
    }
}
