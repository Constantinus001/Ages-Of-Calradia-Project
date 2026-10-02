using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.WarScoreRecovery
{
    // Campaign callback is only a bridge to the opt-in diagnostic buffer; it
    // never persists or alters campaign state.
    internal sealed class WarScoreTraceBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnDailyTick()
        {
            WarScoreTrace.RecordNextDay();
        }
    }
}
