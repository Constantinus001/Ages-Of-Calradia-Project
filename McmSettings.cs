using System;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TwelveMonthCalendar;

namespace AgesOfCalradia.MCM
{
    public sealed class CalendarMcmSettings : AttributeGlobalSettings<CalendarMcmSettings>
    {
        private bool _useLeapYears = CalendarSettingsState.UseLeapYears;
        private bool _showDayLabel = CalendarSettingsState.ShowDayLabel;
        private bool _showYearLabel = CalendarSettingsState.ShowYearLabel;
        private bool _useOrdinalDaySuffixes = CalendarSettingsState.UseOrdinalDaySuffixes;
        private bool _use24HourClock = CalendarSettingsState.Use24HourClock;
        private float _campaignTimeScale = CalendarSettingsState.CampaignTimeScale;
        private bool _autoCampaignTimeScale = CalendarSettingsState.AutoCampaignTimeScale;
        private float _fastForwardTimeMultiplier = CalendarSettingsState.FastForwardTimeMultiplier;
        private bool _clockSynchronizedLighting = CalendarSettingsState.ClockSynchronizedLighting;
        private float _visualSunriseHour = CalendarSettingsState.VisualSunriseHour;
        private float _visualSunsetHour = CalendarSettingsState.VisualSunsetHour;
        private float _visualLightingTransitionHours = CalendarSettingsState.VisualLightingTransitionHours;
        private bool _strategicMapShowLegend = CalendarSettingsState.StrategicMapShowLegend;
        private int _strategicMapLegendWidth = CalendarSettingsState.StrategicMapLegendWidth;
        private int _strategicMapLegendHeight = CalendarSettingsState.StrategicMapLegendHeight;
        private int _strategicMapLegendMarginTop = CalendarSettingsState.StrategicMapLegendMarginTop;
        private int _strategicMapLegendIconSize = CalendarSettingsState.StrategicMapLegendIconSize;
        private int _strategicMapLegendFontSize = CalendarSettingsState.StrategicMapLegendFontSize;
        private float _strategicMapMarkerSpacing = CalendarSettingsState.StrategicMapMarkerSpacing;
        private bool _strategicMapShowSettlementLabels = CalendarSettingsState.StrategicMapShowSettlementLabels;
        private int _strategicMapLabelFontSize = CalendarSettingsState.StrategicMapLabelFontSize;
        private string _monthNamesDelimited = CalendarSettingsState.MonthNamesDelimited;
        private string _seasonNamesDelimited = CalendarSettingsState.SeasonNamesDelimited;
        private string _monthLengthsDelimited = CalendarSettingsState.MonthLengthsDelimited;
        private string _dateFormat = CalendarSettingsState.DateFormat;
        private bool _useCalendarMonthPregnancy = CalendarSettingsState.UseCalendarMonthPregnancy;
        private int _pregnancyDurationMonths = CalendarSettingsState.PregnancyDurationMonths;
        private float _pregnancyDurationInDays = CalendarSettingsState.PregnancyDurationInDays;
        private float _renownGainMultiplier = CalendarSettingsState.RenownGainMultiplier;
        private float _lordDeathRateMultiplier = CalendarSettingsState.LordDeathRateMultiplier;
        private bool _balancePartyImpairment = CalendarSettingsState.BalancePartyImpairment;
        private bool _balancePrisonerRecruitment = CalendarSettingsState.BalancePrisonerRecruitment;
        private bool _balanceNpcMarriage = CalendarSettingsState.BalanceNpcMarriage;
        private bool _balanceMapTracks = CalendarSettingsState.BalanceMapTracks;
        private bool _balanceQuestDeadlines = CalendarSettingsState.BalanceQuestDeadlines;
        private bool _annualBalanceEnabled = CalendarSettingsState.AnnualBalanceEnabled;
        private bool _annualBalanceDiagnosticsEnabled = CalendarSettingsState.AnnualBalanceDiagnosticsEnabled;
        private bool _synchronizing;

        public override string Id => "AgesOfCalradia_MCM";
        public override string DisplayName => "Ages of Calradia";
        public override string FolderName => "AgesOfCalradia";
        public override string FormatType => "json";

        [SettingPropertyText("Month Names", Order = 1, RequireRestart = false,
            HintText = "Twelve names separated by |. Example: January|February|...|December. Each name is limited to 24 characters.")]
        [SettingPropertyGroup("Calendar")]
        public string MonthNamesDelimited
        {
            get { return _monthNamesDelimited; }
            set
            {
                _monthNamesDelimited = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyText("Season Names", Order = 2, RequireRestart = false,
            HintText = "Four names separated by |. Example: Spring|Summer|Autumn|Winter.")]
        [SettingPropertyGroup("Calendar")]
        public string SeasonNamesDelimited
        {
            get { return _seasonNamesDelimited; }
            set
            {
                _seasonNamesDelimited = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyText("Month Lengths", Order = 3, RequireRestart = true,
            HintText = "Twelve whole-number lengths separated by | or comma. They must total exactly 365 and are locked by an active campaign profile.")]
        [SettingPropertyGroup("Calendar")]
        public string MonthLengthsDelimited
        {
            get { return _monthLengthsDelimited; }
            set
            {
                _monthLengthsDelimited = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Show Day Label", Order = 1, RequireRestart = false,
            HintText = "Displays Day before the day number.")]
        [SettingPropertyGroup("Display")]
        public bool ShowDayLabel
        {
            get { return _showDayLabel; }
            set
            {
                _showDayLabel = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Use Ordinal Day Suffixes", Order = 2, RequireRestart = false,
            HintText = "Displays dates as 1st, 2nd, 3rd, and so on.")]
        [SettingPropertyGroup("Display")]
        public bool UseOrdinalDaySuffixes
        {
            get { return _useOrdinalDaySuffixes; }
            set
            {
                _useOrdinalDaySuffixes = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Show Year Label", Order = 3, RequireRestart = false,
            HintText = "Displays Year before the year number.")]
        [SettingPropertyGroup("Display")]
        public bool ShowYearLabel
        {
            get { return _showYearLabel; }
            set
            {
                _showYearLabel = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Campaign Time Scale", CalendarSettingsState.MinimumCampaignTimeScale, CalendarSettingsState.MaximumCampaignTimeScale, "0.0000", Order = 4,
            RequireRestart = false, HintText = "Controls how quickly campaign time advances. Automatic pacing targets about three real hours per calendar year.")]
        [SettingPropertyGroup("Economy")]
        public float CampaignTimeScale
        {
            get { return _campaignTimeScale; }
            set
            {
                _campaignTimeScale = value;
                _autoCampaignTimeScale = false;
                Apply();
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutoCampaignTimeScale));
            }
        }

        [SettingPropertyBool("Automatic Campaign Time Scale", Order = 5, RequireRestart = false,
            HintText = "Keeps campaign pacing at about three real hours per calendar year. Turning it off enables custom pacing.")]
        [SettingPropertyGroup("Economy")]
        public bool AutoCampaignTimeScale
        {
            get { return _autoCampaignTimeScale; }
            set
            {
                _autoCampaignTimeScale = value;
                if (value)
                {
                    _campaignTimeScale = CalendarSettingsState.DefaultCampaignTimeScale;
                }
                Apply();
                OnPropertyChanged();
                OnPropertyChanged(nameof(CampaignTimeScale));
            }
        }

        [SettingPropertyBool("Use 24-Hour Clock", Order = 4, RequireRestart = false,
            HintText = "Shows the campaign clock below the map-bar date as 24-hour time. Turn off for 12-hour AM/PM time.")]
        [SettingPropertyGroup("Display")]
        public bool Use24HourClock
        {
            get { return _use24HourClock; }
            set
            {
                _use24HourClock = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Fast-Forward Speed Multiplier", 1f, 4f, "0", Order = 6,
            RequireRestart = false,
            HintText = "Uses Bannerlord's built-in fast-forward speed. Automatic fast-forward targets about ninety real minutes per calendar year.")]
        [SettingPropertyGroup("Pacing")]
        public float FastForwardTimeMultiplier
        {
            get { return _fastForwardTimeMultiplier; }
            set
            {
                _fastForwardTimeMultiplier = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Synchronize Campaign Lighting", Order = 1, RequireRestart = false,
            HintText = "Aligns visual sunrise and sunset with the campaign clock without changing native gameplay sunrise/sunset mechanics.")]
        [SettingPropertyGroup("Lighting")]
        public bool ClockSynchronizedLighting
        {
            get { return _clockSynchronizedLighting; }
            set
            {
                _clockSynchronizedLighting = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Visual Sunrise Hour", 0f, 23.75f, "0.00", Order = 2,
            RequireRestart = false, HintText = "Visual sunrise hour on the campaign clock. Default: 05:00.")]
        [SettingPropertyGroup("Lighting")]
        public float VisualSunriseHour
        {
            get { return _visualSunriseHour; }
            set
            {
                _visualSunriseHour = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Visual Sunset Hour", 0f, 23.75f, "0.00", Order = 3,
            RequireRestart = false, HintText = "Visual sunset hour on the campaign clock. Default: 19:30.")]
        [SettingPropertyGroup("Lighting")]
        public float VisualSunsetHour
        {
            get { return _visualSunsetHour; }
            set
            {
                _visualSunsetHour = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Lighting Transition Hours", 0.25f, 4f, "0.00", Order = 4,
            RequireRestart = false, HintText = "Length of the gradual dawn and dusk transition. Default: 2 hours.")]
        [SettingPropertyGroup("Lighting")]
        public float VisualLightingTransitionHours
        {
            get { return _visualLightingTransitionHours; }
            set
            {
                _visualLightingTransitionHours = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyText("Date Format", Order = 8, RequireRestart = false,
            HintText = "Tokens: {Month}, {Season}, {Day}, {Year}, {MonthNumber}, {DayOfYear}. Example: {Month} {Day} {Year}. The map bar shows the season separately to the right of the clock.")]
        [SettingPropertyGroup("Display")]
        public string DateFormat
        {
            get { return _dateFormat; }
            set
            {
                _dateFormat = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Use Calendar-Month Pregnancy", Order = 7, RequireRestart = false,
            HintText = "Uses the configured number of calendar months for future pregnancies.")]
        [SettingPropertyGroup("Life Cycle")]
        public bool UseCalendarMonthPregnancy
        {
            get { return _useCalendarMonthPregnancy; }
            set
            {
                _useCalendarMonthPregnancy = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyInteger("Pregnancy Duration (Months)", 1, 24, "0", Order = 8,
            RequireRestart = false, HintText = "Calendar months from conception to birth. Changes affect future pregnancies.")]
        [SettingPropertyGroup("Life Cycle")]
        public int PregnancyDurationMonths
        {
            get { return _pregnancyDurationMonths; }
            set
            {
                _pregnancyDurationMonths = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Lord Death Rate Multiplier", 0f, 1f, "0.00", Order = 10,
            RequireRestart = false,
            HintText = "Retains this fraction of Bannerlord's ordinary noble-lord old-age and battle death chance. 0.20 keeps 20%; 1.00 is native. Executions and scripted deaths are unchanged.")]
        [SettingPropertyGroup("Life Cycle")]
        public float LordDeathRateMultiplier
        {
            get { return _lordDeathRateMultiplier; }
            set
            {
                _lordDeathRateMultiplier = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyFloatingInteger("Renown Gain Multiplier", 0f, 1f, "0.00", Order = 10,
            RequireRestart = false, HintText = "Scales positive renown awards. Default is 0.50.")]
        [SettingPropertyGroup("Progression")]
        public float RenownGainMultiplier
        {
            get { return _renownGainMultiplier; }
            set
            {
                _renownGainMultiplier = value;
                Apply();
                OnPropertyChanged();
            }
        }

        [SettingPropertyBool("Show Strategic Map Legend", Order = 1, RequireRestart = false,
            HintText = "Shows the town/castle legend on the Strategic Map.")]
        [SettingPropertyGroup("Strategic Map")]
        public bool StrategicMapShowLegend
        {
            get { return _strategicMapShowLegend; }
            set { _strategicMapShowLegend = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Legend Width", 180, 400, "0", Order = 2, RequireRestart = false,
            HintText = "Width of the Strategic Map legend in UI units.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLegendWidth
        {
            get { return _strategicMapLegendWidth; }
            set { _strategicMapLegendWidth = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Legend Height", 60, 220, "0", Order = 3, RequireRestart = false,
            HintText = "Height of the Strategic Map legend in UI units.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLegendHeight
        {
            get { return _strategicMapLegendHeight; }
            set { _strategicMapLegendHeight = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Legend Top Margin", 0, 80, "0", Order = 4, RequireRestart = false,
            HintText = "Top offset of the Strategic Map legend.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLegendMarginTop
        {
            get { return _strategicMapLegendMarginTop; }
            set { _strategicMapLegendMarginTop = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Legend Icon Size", 16, 64, "0", Order = 5, RequireRestart = false,
            HintText = "Town and castle icon size in the legend. The map itself uses its own fixed-size renderer.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLegendIconSize
        {
            get { return _strategicMapLegendIconSize; }
            set { _strategicMapLegendIconSize = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Legend Font Size", 8, 24, "0", Order = 6, RequireRestart = false,
            HintText = "Text size used by the Strategic Map legend.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLegendFontSize
        {
            get { return _strategicMapLegendFontSize; }
            set { _strategicMapLegendFontSize = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyFloatingInteger("Marker Spacing", 52f, 200f, "0.0", Order = 7, RequireRestart = false,
            HintText = "Minimum source-map spacing between settlement markers. Increase it when icons overlap.")]
        [SettingPropertyGroup("Strategic Map")]
        public float StrategicMapMarkerSpacing
        {
            get { return _strategicMapMarkerSpacing; }
            set { _strategicMapMarkerSpacing = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Show Settlement Labels", Order = 8, RequireRestart = false,
            HintText = "Shows town names on the composed Strategic Map texture.")]
        [SettingPropertyGroup("Strategic Map")]
        public bool StrategicMapShowSettlementLabels
        {
            get { return _strategicMapShowSettlementLabels; }
            set { _strategicMapShowSettlementLabels = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyInteger("Settlement Label Font Size", 8, 24, "0", Order = 9, RequireRestart = false,
            HintText = "Font size used for town names on the Strategic Map.")]
        [SettingPropertyGroup("Strategic Map")]
        public int StrategicMapLabelFontSize
        {
            get { return _strategicMapLabelFontSize; }
            set { _strategicMapLabelFontSize = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Annual Balance Enabled", Order = 19, RequireRestart = false,
            HintText = "Master switch for annual-rate balancing. The calendar and display remain active when disabled.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool AnnualBalanceEnabled
        {
            get { return _annualBalanceEnabled; }
            set { _annualBalanceEnabled = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Balance Party Impairment", Order = 20, RequireRestart = false,
            HintText = "Scales post-battle disorganization and vulnerability durations to the 365-day year.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool BalancePartyImpairment
        {
            get { return _balancePartyImpairment; }
            set { _balancePartyImpairment = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Balance Prisoner Recruitment", Order = 21, RequireRestart = false,
            HintText = "Scales prisoner conformity gained per campaign hour for player and AI parties.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool BalancePrisonerRecruitment
        {
            get { return _balancePrisonerRecruitment; }
            set { _balancePrisonerRecruitment = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Balance NPC Marriage", Order = 22, RequireRestart = false,
            HintText = "Converts NPC marriage chance to preserve its annual rate across the 365-day year.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool BalanceNpcMarriage
        {
            get { return _balanceNpcMarriage; }
            set { _balanceNpcMarriage = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Balance Map Tracks", Order = 23, RequireRestart = false,
            HintText = "Scales track lifetime while preserving native track detection and spotting rules.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool BalanceMapTracks
        {
            get { return _balanceMapTracks; }
            set { _balanceMapTracks = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Balance Quest Deadlines", Order = 24, RequireRestart = false,
            HintText = "Extends deadlines only for quests started while this setting is enabled.")]
        [SettingPropertyGroup("Annual Balance")]
        public bool BalanceQuestDeadlines
        {
            get { return _balanceQuestDeadlines; }
            set { _balanceQuestDeadlines = value; Apply(); OnPropertyChanged(); }
        }

        [SettingPropertyBool("Annual Balance Diagnostics", Order = 25, RequireRestart = false,
            HintText = "Writes sampled annual-balance checkpoints to crash reports.")]
        [SettingPropertyGroup("Diagnostics")]
        public bool AnnualBalanceDiagnosticsEnabled
        {
            get { return _annualBalanceDiagnosticsEnabled; }
            set { _annualBalanceDiagnosticsEnabled = value; Apply(); OnPropertyChanged(); }
        }

        public static bool RegisterSettings()
        {
            if (Instance == null)
            {
                return false;
            }

            Instance.SubscribeToCoreState();
            Instance.SyncFromCoreState();
            Instance.Apply();
            return true;
        }

        private void Apply()
        {
            if (_synchronizing)
            {
                return;
            }

            string failure;
            string[] requestedMonthNames;
            if (!CalendarSettingsState.TryParseMonthNamesDelimited(
                    _monthNamesDelimited,
                    out requestedMonthNames,
                    out failure))
            {
                requestedMonthNames = CalendarSettingsState.MonthNamesSnapshot();
                _monthNamesDelimited = CalendarSettingsState.MonthNamesDelimited;
            }

            string[] requestedSeasonNames;
            if (!CalendarSettingsState.TryParseSeasonNamesDelimited(
                    _seasonNamesDelimited,
                    out requestedSeasonNames,
                    out failure))
            {
                requestedSeasonNames = CalendarSettingsState.SeasonNamesSnapshot();
                _seasonNamesDelimited = CalendarSettingsState.SeasonNamesDelimited;
            }

            int[] requestedMonthLengths;
            if (!CalendarSettingsState.TryParseMonthLengthsDelimited(
                    _monthLengthsDelimited,
                    out requestedMonthLengths,
                    out failure))
            {
                requestedMonthLengths = CalendarSettingsState.MonthLengthsSnapshot();
                _monthLengthsDelimited = CalendarSettingsState.MonthLengthsDelimited;
            }

            CalendarSettingsState.Apply(
                CalendarSettingsState.CalendarSystem,
                _useLeapYears,
                _showDayLabel,
                _showYearLabel,
                _campaignTimeScale,
                _dateFormat,
                monthNames: requestedMonthNames,
                monthLengths: requestedMonthLengths,
                seasonNames: requestedSeasonNames,
                autoCampaignTimeScale: _autoCampaignTimeScale,
                fastForwardTimeMultiplier: _fastForwardTimeMultiplier,
                pregnancyDurationMonths: _pregnancyDurationMonths,
                pregnancyDurationInDays: _pregnancyDurationInDays,
                useCalendarMonthPregnancy: _useCalendarMonthPregnancy,
                renownGainMultiplier: _renownGainMultiplier,
                lordDeathRateMultiplier: _lordDeathRateMultiplier,
                useOrdinalDaySuffixes: _useOrdinalDaySuffixes,
                use24HourClock: _use24HourClock,
                balancePartyImpairment: _balancePartyImpairment,
                balancePrisonerRecruitment: _balancePrisonerRecruitment,
                balanceNpcMarriage: _balanceNpcMarriage,
                balanceMapTracks: _balanceMapTracks,
                balanceQuestDeadlines: _balanceQuestDeadlines,
                annualBalanceEnabled: _annualBalanceEnabled,
                annualBalanceDiagnosticsEnabled: _annualBalanceDiagnosticsEnabled,
                clockSynchronizedLighting: _clockSynchronizedLighting,
                visualSunriseHour: _visualSunriseHour,
                visualSunsetHour: _visualSunsetHour,
                visualLightingTransitionHours: _visualLightingTransitionHours,
                strategicMapShowLegend: _strategicMapShowLegend,
                strategicMapLegendWidth: _strategicMapLegendWidth,
                strategicMapLegendHeight: _strategicMapLegendHeight,
                strategicMapLegendMarginTop: _strategicMapLegendMarginTop,
                strategicMapLegendIconSize: _strategicMapLegendIconSize,
                strategicMapLegendFontSize: _strategicMapLegendFontSize,
                strategicMapMarkerSpacing: _strategicMapMarkerSpacing,
                strategicMapShowSettlementLabels: _strategicMapShowSettlementLabels,
                strategicMapLabelFontSize: _strategicMapLabelFontSize);
            CalendarSettingsState.Save();
        }

        private void SubscribeToCoreState()
        {
            CalendarSettingsState.SettingsChanged -= OnCoreStateChanged;
            CalendarSettingsState.SettingsChanged += OnCoreStateChanged;
        }

        private void SyncFromCoreState()
        {
            _synchronizing = true;
            try
            {
                _useLeapYears = CalendarSettingsState.UseLeapYears;
                _showDayLabel = CalendarSettingsState.ShowDayLabel;
                _showYearLabel = CalendarSettingsState.ShowYearLabel;
                _useOrdinalDaySuffixes = CalendarSettingsState.UseOrdinalDaySuffixes;
                _use24HourClock = CalendarSettingsState.Use24HourClock;
                _campaignTimeScale = CalendarSettingsState.CampaignTimeScale;
                _autoCampaignTimeScale = CalendarSettingsState.AutoCampaignTimeScale;
                _fastForwardTimeMultiplier = CalendarSettingsState.FastForwardTimeMultiplier;
                _clockSynchronizedLighting = CalendarSettingsState.ClockSynchronizedLighting;
                _visualSunriseHour = CalendarSettingsState.VisualSunriseHour;
                _visualSunsetHour = CalendarSettingsState.VisualSunsetHour;
                _visualLightingTransitionHours = CalendarSettingsState.VisualLightingTransitionHours;
                _strategicMapShowLegend = CalendarSettingsState.StrategicMapShowLegend;
                _strategicMapLegendWidth = CalendarSettingsState.StrategicMapLegendWidth;
                _strategicMapLegendHeight = CalendarSettingsState.StrategicMapLegendHeight;
                _strategicMapLegendMarginTop = CalendarSettingsState.StrategicMapLegendMarginTop;
                _strategicMapLegendIconSize = CalendarSettingsState.StrategicMapLegendIconSize;
                _strategicMapLegendFontSize = CalendarSettingsState.StrategicMapLegendFontSize;
                _strategicMapMarkerSpacing = CalendarSettingsState.StrategicMapMarkerSpacing;
                _strategicMapShowSettlementLabels = CalendarSettingsState.StrategicMapShowSettlementLabels;
                _strategicMapLabelFontSize = CalendarSettingsState.StrategicMapLabelFontSize;
                _monthNamesDelimited = CalendarSettingsState.MonthNamesDelimited;
                _seasonNamesDelimited = CalendarSettingsState.SeasonNamesDelimited;
                _monthLengthsDelimited = CalendarSettingsState.MonthLengthsDelimited;
                _dateFormat = CalendarSettingsState.DateFormat;
                _useCalendarMonthPregnancy = CalendarSettingsState.UseCalendarMonthPregnancy;
                _pregnancyDurationMonths = CalendarSettingsState.PregnancyDurationMonths;
                _pregnancyDurationInDays = CalendarSettingsState.PregnancyDurationInDays;
                _renownGainMultiplier = CalendarSettingsState.RenownGainMultiplier;
                _lordDeathRateMultiplier = CalendarSettingsState.LordDeathRateMultiplier;
                _balancePartyImpairment = CalendarSettingsState.BalancePartyImpairment;
                _balancePrisonerRecruitment = CalendarSettingsState.BalancePrisonerRecruitment;
                _balanceNpcMarriage = CalendarSettingsState.BalanceNpcMarriage;
                _balanceMapTracks = CalendarSettingsState.BalanceMapTracks;
                _balanceQuestDeadlines = CalendarSettingsState.BalanceQuestDeadlines;
                _annualBalanceEnabled = CalendarSettingsState.AnnualBalanceEnabled;
                _annualBalanceDiagnosticsEnabled = CalendarSettingsState.AnnualBalanceDiagnosticsEnabled;
            }
            finally
            {
                _synchronizing = false;
            }
        }

        private void OnCoreStateChanged()
        {
            if (_synchronizing)
            {
                return;
            }

            SyncFromCoreState();
            OnPropertyChanged(nameof(MonthNamesDelimited));
            OnPropertyChanged(nameof(SeasonNamesDelimited));
            OnPropertyChanged(nameof(MonthLengthsDelimited));
            OnPropertyChanged(nameof(ShowDayLabel));
            OnPropertyChanged(nameof(ShowYearLabel));
            OnPropertyChanged(nameof(UseOrdinalDaySuffixes));
            OnPropertyChanged(nameof(Use24HourClock));
            OnPropertyChanged(nameof(CampaignTimeScale));
            OnPropertyChanged(nameof(AutoCampaignTimeScale));
            OnPropertyChanged(nameof(FastForwardTimeMultiplier));
            OnPropertyChanged(nameof(ClockSynchronizedLighting));
            OnPropertyChanged(nameof(VisualSunriseHour));
            OnPropertyChanged(nameof(VisualSunsetHour));
            OnPropertyChanged(nameof(VisualLightingTransitionHours));
            OnPropertyChanged(nameof(StrategicMapShowLegend));
            OnPropertyChanged(nameof(StrategicMapLegendWidth));
            OnPropertyChanged(nameof(StrategicMapLegendHeight));
            OnPropertyChanged(nameof(StrategicMapLegendMarginTop));
            OnPropertyChanged(nameof(StrategicMapLegendIconSize));
            OnPropertyChanged(nameof(StrategicMapLegendFontSize));
            OnPropertyChanged(nameof(StrategicMapMarkerSpacing));
            OnPropertyChanged(nameof(StrategicMapShowSettlementLabels));
            OnPropertyChanged(nameof(StrategicMapLabelFontSize));
            OnPropertyChanged(nameof(DateFormat));
            OnPropertyChanged(nameof(UseCalendarMonthPregnancy));
            OnPropertyChanged(nameof(PregnancyDurationMonths));
            OnPropertyChanged(nameof(RenownGainMultiplier));
            OnPropertyChanged(nameof(LordDeathRateMultiplier));
            OnPropertyChanged(nameof(BalancePartyImpairment));
            OnPropertyChanged(nameof(BalancePrisonerRecruitment));
            OnPropertyChanged(nameof(BalanceNpcMarriage));
            OnPropertyChanged(nameof(BalanceMapTracks));
            OnPropertyChanged(nameof(BalanceQuestDeadlines));
            OnPropertyChanged(nameof(AnnualBalanceEnabled));
            OnPropertyChanged(nameof(AnnualBalanceDiagnosticsEnabled));
        }
    }
}
