using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace AgesOfCalradiaLogistics
{
    /// <summary>Provides sparse, actionable Supply warnings without modifying protected Gauntlet assets.</summary>
    internal sealed class LogisticsPlayerSupplyNotificationBehavior : CampaignBehaviorBase
    {
        private readonly LogisticsReserveBehavior _reserves;
        private LogisticsSupplyCondition _lastCondition = LogisticsSupplyCondition.Supported;
        private bool _hasObservedCondition;

        internal LogisticsPlayerSupplyNotificationBehavior(LogisticsReserveBehavior reserves)
        {
            _reserves = reserves;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnDailyTickParty);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnDailyTickParty(MobileParty party)
        {
            if (party != MobileParty.MainParty)
            {
                return;
            }

            int reserve = _reserves.GetReserve(party);
            float dailyUse = _reserves.GetDailyUse(party);
            LogisticsSupplyCondition condition = LogisticsSupplyMath.GetCondition(reserve, dailyUse);
            if (_hasObservedCondition && condition == _lastCondition)
            {
                return;
            }

            _hasObservedCondition = true;
            _lastCondition = condition;
            float days = LogisticsSupplyMath.CalculateDaysRemaining(reserve, dailyUse);
            string summary = "Supply " + reserve + "/" + LogisticsReserveBehavior.MaximumReserve
                + " — " + (float.IsPositiveInfinity(days) ? "--" : days.ToString("0.0")) + " field days.";
            if (condition == LogisticsSupplyCondition.Empty)
            {
                InformationManager.DisplayMessage(new InformationMessage(summary + " Baggage resupply is unavailable and travel is severely impaired."));
            }
            else if (condition == LogisticsSupplyCondition.Critical)
            {
                InformationManager.DisplayMessage(new InformationMessage(summary + " Critical: resupply at a town immediately."));
            }
            else if (condition == LogisticsSupplyCondition.Strained)
            {
                InformationManager.DisplayMessage(new InformationMessage(summary + " Strained: plan your next resupply."));
            }
        }
    }
}
