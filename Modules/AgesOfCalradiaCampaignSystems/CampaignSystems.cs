using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AgesOfCalradia.CampaignSystems
{
    // Per-campaign composition, not a second ticking clock or a replacement for Core settings.
    public sealed class CampaignSystem
    {
        public CampaignClock Time { get; private set; }
        public QuestTiming Quests { get; private set; }
        public ProcurementSettings Supply { get; private set; }
        public LogisticsConnection Logistics { get; private set; }
        public ReadOnlyDictionary<string, string> Owners { get; private set; }

        public CampaignSystem(Func<double> campaignDays, ProcurementSettings supply)
        {
            if (supply == null) throw new ArgumentNullException("supply");
            Time = new CampaignClock(campaignDays);
            Quests = new QuestTiming(Time);
            Supply = supply;
            Logistics = new LogisticsConnection();
            // Declared integration contracts, not a claim to inspect third-party Harmony patches.
            Owners = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal) {
                {"clock", "Bannerlord CampaignTime + protected AOC calendar"},
                {"calendar-settings", "protected CalendarSettingsState"},
                {"economic-rates", "approved calendar-fixes sidecar + Core models"},
                {"workshop-supply", "WorkshopProcurement using CampaignSystems settings"},
                {"procurement-transactions", "CampaignSystems EconomicTransfer"},
                {"quest-deadline-scaling", "protected QuestDeadlineBalancePatch only"},
                {"quest-deadline-observation", "CampaignSystems QuestTiming"},
                {"diagnostics", "SoakDiagnostics observation only"}
            });
        }
    }

    public sealed class CampaignClock
    {
        private readonly Func<double> _days;
        public CampaignClock(Func<double> campaignDays)
        { if (campaignDays == null) throw new ArgumentNullException("campaignDays"); _days = campaignDays; }
        public double Now { get { return ValidDay(_days()); } }
        public static double ValidDay(double day)
        {
            if (double.IsNaN(day) || double.IsInfinity(day) || day < 0) throw new ArgumentOutOfRangeException("day");
            return day;
        }
    }

    public sealed class QuestTiming
    {
        private readonly CampaignClock _clock;
        internal QuestTiming(CampaignClock clock) { _clock = clock; }
        // Adapter converts native CampaignTime.Never to null BEFORE arithmetic.
        // Existing deadlines are already authoritative; never scale them again.
        public double? RemainingDays(double? authoritativeDueDay)
        {
            if (!authoritativeDueDay.HasValue) return null;
            return Math.Max(0, CampaignClock.ValidDay(authoritativeDueDay.Value) - _clock.Now);
        }
    }
}
