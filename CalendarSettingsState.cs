using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace TwelveMonthCalendar
{
    /// <summary>
    /// Runtime-safe settings bridge. The optional MCM adapter updates this
    /// class when MCM is installed; the calendar keeps these defaults without
    /// requiring MCM.
    /// </summary>
    public static class CalendarSettingsState
    {
        private static readonly object SyncRoot = new object();

        public static event Action SettingsChanged;

        // This module always owns a 365-day Gregorian-style calendar. Keep a
        // stable value for legacy adapters/configuration, but do not expose a
        // selectable native-calendar mode.
        private const string FixedCalendarSystem = "Gregorian12Month";
        private const int RequiredCommonDaysInYear = 365;
        private const string DefaultDateFormat = "{Month} {Day} {Year}";
        private const bool DefaultUseOrdinalDaySuffixes = true;
        private const bool DefaultUse24HourClock = true;
        internal const int DefaultNativeDaysInYear = 84;
        // Normal play targets about three real hours per 365-day calendar
        // year. The paired 2x fast-forward target is about ninety minutes.
        public const float DefaultCampaignTimeScale = 0.803f;
        public const float MinimumCampaignTimeScale = 0.001f;
        internal const float PreviousAutomaticCampaignTimeScale = 0.15f;
        public const float MaximumCampaignTimeScale = 2.0f;
        // Visual lighting follows these hours only when the optional clock-
        // synchronized atmosphere mode is enabled. Native sunrise/sunset
        // remain untouched for gameplay mechanics and compatibility.
        public const bool DefaultClockSynchronizedLighting = true;
        public const float DefaultVisualSunriseHour = 6.25f;
        // Begin the evening lighting transition around 19:30, with the
        // visual sunset occurring naturally in the 19:00–20:00 window.
        public const float DefaultVisualSunsetHour = 18.25f;
        public const float DefaultVisualLightingTransitionHours = 2f;
        public const bool DefaultStrategicMapShowLegend = true;
        public const int DefaultStrategicMapLegendWidth = 250;
        public const int DefaultStrategicMapLegendHeight = 108;
        public const int DefaultStrategicMapLegendMarginTop = 6;
        public const int DefaultStrategicMapLegendIconSize = 30;
        public const int DefaultStrategicMapLegendFontSize = 13;
        public const float DefaultStrategicMapMarkerSpacing = 52f;
        public const bool DefaultStrategicMapShowSettlementLabels = true;
        public const int DefaultStrategicMapLabelFontSize = 12;
        public const int DefaultPoliticalControlOpacityPercent = 100;
        public const int DefaultPoliticalControlBrightnessPercent = 100;
        public const bool DefaultPoliticalSolidWater = true;
        internal const float DefaultPregnancyDurationInDays = 273.75f;
        internal const int DefaultPregnancyDurationMonths = 9;
        internal const float DefaultRenownGainMultiplier = 0.5f;
        internal const float DefaultLordDeathRateMultiplier = 0.20f;
        internal const float DefaultNormalPlayTimeMultiplier = 1f;
        // Fast-forward stays within Bannerlord's supported native range and
        // pairs with the automatic scale for a ninety-minute calendar year.
        internal const float DefaultFastForwardTimeMultiplier = 2f;
        internal const float PreviousAutomaticFastForwardTimeMultiplier = 4f;
        internal const float MinimumPacingMultiplier = 1f;
        internal const float MaximumPacingMultiplier = 4f;
        private const int MaximumConfiguredMonthLength = 1000;
        private const int MaximumConfiguredMonthNameLength = 24;

        private static readonly string[] DefaultMonthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        private static readonly string[] DefaultSeasonNames =
        {
            "Spring", "Summer", "Autumn", "Winter"
        };

        private static readonly int[] DefaultMonthLengths =
        {
            31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31
        };

        private static bool _useLeapYears = true;
        private static bool _showDayLabel;
        private static bool _showYearLabel;
        private static bool _useOrdinalDaySuffixes = DefaultUseOrdinalDaySuffixes;
        private static bool _use24HourClock = DefaultUse24HourClock;
        private static float _campaignTimeScale = DefaultCampaignTimeScale;
        private static bool _autoCampaignTimeScale = true;
        private static float _fastForwardTimeMultiplier = DefaultFastForwardTimeMultiplier;
        private static bool _clockSynchronizedLighting = DefaultClockSynchronizedLighting;
        private static float _visualSunriseHour = DefaultVisualSunriseHour;
        private static float _visualSunsetHour = DefaultVisualSunsetHour;
        private static float _visualLightingTransitionHours = DefaultVisualLightingTransitionHours;
        private static bool _strategicMapShowLegend = DefaultStrategicMapShowLegend;
        private static int _strategicMapLegendWidth = DefaultStrategicMapLegendWidth;
        private static int _strategicMapLegendHeight = DefaultStrategicMapLegendHeight;
        private static int _strategicMapLegendMarginTop = DefaultStrategicMapLegendMarginTop;
        private static int _strategicMapLegendIconSize = DefaultStrategicMapLegendIconSize;
        private static int _strategicMapLegendFontSize = DefaultStrategicMapLegendFontSize;
        private static float _strategicMapMarkerSpacing = DefaultStrategicMapMarkerSpacing;
        private static bool _strategicMapShowSettlementLabels = DefaultStrategicMapShowSettlementLabels;
        private static int _strategicMapLabelFontSize = DefaultStrategicMapLabelFontSize;
        private static int _politicalControlOpacityPercent = DefaultPoliticalControlOpacityPercent;
        private static int _politicalControlBrightnessPercent = DefaultPoliticalControlBrightnessPercent;
        private static bool _politicalSolidWater = DefaultPoliticalSolidWater;
        private static string _dateFormat = DefaultDateFormat;
        private static int _nativeDaysInYear = DefaultNativeDaysInYear;
        private static float _pregnancyDurationInDays = DefaultPregnancyDurationInDays;
        private static int _pregnancyDurationMonths = DefaultPregnancyDurationMonths;
        private static bool _useCalendarMonthPregnancy = true;
        private static float _renownGainMultiplier = DefaultRenownGainMultiplier;
        private static float _lordDeathRateMultiplier = DefaultLordDeathRateMultiplier;
        private static bool _balancePartyImpairment = true;
        private static bool _balancePrisonerRecruitment = true;
        private static bool _balanceNpcMarriage = true;
        private static bool _balanceMapTracks = true;
        private static bool _balanceQuestDeadlines = true;
        private static bool _annualBalanceEnabled = true;
        private static bool _annualBalanceDiagnosticsEnabled = true;
        private static bool _campaignSessionStarted;
        private static bool _legacySaveAgeCompatibility;
        private static double _legacySaveAgeCutoverDay = double.NaN;
        private static readonly string[] _monthNames = (string[])DefaultMonthNames.Clone();
        private static readonly string[] _seasonNames = (string[])DefaultSeasonNames.Clone();
        private static readonly int[] _monthLengths = (int[])DefaultMonthLengths.Clone();
        private static readonly int[] _monthStarts = new int[12];
        private static int _commonDaysInYear;

        static CalendarSettingsState()
        {
            RebuildMonthCache();
        }

        public static bool ExtendedCalendarEnabled
        {
            get { return true; }
        }

        /// <summary>
        /// Legacy compatibility flag. The refuge runtime now lives in the
        /// optional Ages of Calradia Refuges module.
        /// </summary>
        internal static bool RefugeSystemEnabled
        {
            get { return false; }
        }

        public static bool UseLeapYears
        {
            get { lock (SyncRoot) return _useLeapYears; }
        }

        public static bool ShowDayLabel
        {
            get { lock (SyncRoot) return _showDayLabel; }
        }

        public static bool ShowYearLabel
        {
            get { lock (SyncRoot) return _showYearLabel; }
        }

        public static bool UseOrdinalDaySuffixes
        {
            get { lock (SyncRoot) return _useOrdinalDaySuffixes; }
        }

        public static bool Use24HourClock
        {
            get { lock (SyncRoot) return _use24HourClock; }
        }

        public static float CampaignTimeScale
        {
            get { lock (SyncRoot) return _campaignTimeScale; }
        }

        public static bool AutoCampaignTimeScale
        {
            get { lock (SyncRoot) return _autoCampaignTimeScale; }
        }

        /// <summary>
        /// Retained as a read-only legacy API surface for existing adapters.
        /// Normal campaign pace is intentionally fixed at 1.00.
        /// </summary>
        public static float NormalPlayTimeMultiplier
        {
            get { return DefaultNormalPlayTimeMultiplier; }
        }

        /// <summary>
        /// The direct Bannerlord Campaign.SpeedUpMultiplier used while map time
        /// fast-forwards. It is limited to Bannerlord's supported 1 through
        /// 4 range; normal pace remains fixed at native cadence.
        /// </summary>
        public static float FastForwardTimeMultiplier
        {
            get { lock (SyncRoot) return _fastForwardTimeMultiplier; }
        }

        public static bool ClockSynchronizedLighting
        {
            get { lock (SyncRoot) return _clockSynchronizedLighting; }
        }

        public static float VisualSunriseHour
        {
            get { lock (SyncRoot) return _visualSunriseHour; }
        }

        public static float VisualSunsetHour
        {
            get { lock (SyncRoot) return _visualSunsetHour; }
        }

        public static float VisualLightingTransitionHours
        {
            get { lock (SyncRoot) return _visualLightingTransitionHours; }
        }

        public static bool StrategicMapShowLegend
        {
            get { lock (SyncRoot) return _strategicMapShowLegend; }
        }

        public static int StrategicMapLegendWidth
        {
            get { lock (SyncRoot) return _strategicMapLegendWidth; }
        }

        public static int StrategicMapLegendHeight
        {
            get { lock (SyncRoot) return _strategicMapLegendHeight; }
        }

        public static int StrategicMapLegendMarginTop
        {
            get { lock (SyncRoot) return _strategicMapLegendMarginTop; }
        }

        public static int StrategicMapLegendIconSize
        {
            get { lock (SyncRoot) return _strategicMapLegendIconSize; }
        }

        public static int StrategicMapLegendFontSize
        {
            get { lock (SyncRoot) return _strategicMapLegendFontSize; }
        }

        public static float StrategicMapMarkerSpacing
        {
            get { lock (SyncRoot) return _strategicMapMarkerSpacing; }
        }

        public static bool StrategicMapShowSettlementLabels
        {
            get { lock (SyncRoot) return _strategicMapShowSettlementLabels; }
        }

        public static int StrategicMapLabelFontSize
        {
            get { lock (SyncRoot) return _strategicMapLabelFontSize; }
        }

        public static int PoliticalControlOpacityPercent
        {
            get { lock (SyncRoot) return _politicalControlOpacityPercent; }
        }

        public static int PoliticalControlBrightnessPercent
        {
            get { lock (SyncRoot) return _politicalControlBrightnessPercent; }
        }

        public static bool PoliticalSolidWater
        {
            get { lock (SyncRoot) return _politicalSolidWater; }
        }

        internal static bool IsCampaignProfileLocked
        {
            get { lock (SyncRoot) return _campaignSessionStarted; }
        }

        public static string DateFormat
        {
            get { lock (SyncRoot) return _dateFormat; }
        }

        public static int NativeDaysInYear
        {
            get { lock (SyncRoot) return _nativeDaysInYear; }
        }

        public static float PregnancyDurationInDays
        {
            get { lock (SyncRoot) return _pregnancyDurationInDays; }
        }

        public static int PregnancyDurationMonths
        {
            get { lock (SyncRoot) return _pregnancyDurationMonths; }
        }

        public static bool UseCalendarMonthPregnancy
        {
            get { lock (SyncRoot) return _useCalendarMonthPregnancy; }
        }

        public static float RenownGainMultiplier
        {
            get { lock (SyncRoot) return _renownGainMultiplier; }
        }

        /// <summary>
        /// Multiplies eligible noble heroes' native old-age and battle death
        /// probabilities. 0.20 retains twenty percent of the native chance;
        /// 1.00 leaves Bannerlord's chance unchanged.
        /// </summary>
        public static float LordDeathRateMultiplier
        {
            get { lock (SyncRoot) return _lordDeathRateMultiplier; }
        }

        public static bool BalancePartyImpairment
        {
            get { lock (SyncRoot) return _balancePartyImpairment; }
        }

        public static bool BalancePrisonerRecruitment
        {
            get { lock (SyncRoot) return _balancePrisonerRecruitment; }
        }

        public static bool BalanceNpcMarriage
        {
            get { lock (SyncRoot) return _balanceNpcMarriage; }
        }

        public static bool BalanceMapTracks
        {
            get { lock (SyncRoot) return _balanceMapTracks; }
        }

        public static bool BalanceQuestDeadlines
        {
            get { lock (SyncRoot) return _balanceQuestDeadlines; }
        }

        public static bool AnnualBalanceDiagnosticsEnabled
        {
            get { lock (SyncRoot) return _annualBalanceDiagnosticsEnabled; }
        }

        public static string GetMonthName(int month)
        {
            lock (SyncRoot)
            {
                return _monthNames[month];
            }
        }

        public static string GetSeasonName(int season)
        {
            lock (SyncRoot)
            {
                return _seasonNames[season];
            }
        }

        public static int GetMonthLength(int month)
        {
            lock (SyncRoot)
            {
                return _monthLengths[month];
            }
        }

        public static int CommonDaysInYear
        {
            get
            {
                lock (SyncRoot)
                {
                    return _commonDaysInYear;
                }
            }
        }

        public static int GetMonthStart(int month)
        {
            lock (SyncRoot)
            {
                return _monthStarts[month];
            }
        }

        public static string[] MonthNamesSnapshot()
        {
            lock (SyncRoot) return (string[])_monthNames.Clone();
        }

        public static string[] SeasonNamesSnapshot()
        {
            lock (SyncRoot) return (string[])_seasonNames.Clone();
        }

        public static int[] MonthLengthsSnapshot()
        {
            lock (SyncRoot) return (int[])_monthLengths.Clone();
        }

        public static bool AnnualBalanceEnabled
        {
            get { lock (SyncRoot) return _annualBalanceEnabled; }
        }

        public static bool AnnualRateBalanceEnabled
        {
            get { return ExtendedCalendarEnabled && AnnualBalanceEnabled; }
        }

        /// <summary>
        /// True for campaigns created before the save-time age compatibility
        /// marker was introduced. Their stored CampaignTime values use
        /// Bannerlord's native 84-day year for the portion before the first
        /// load with this version of the mod.
        /// </summary>
        internal static bool IsLegacySaveAgeCompatibility
        {
            get { lock (SyncRoot) return _legacySaveAgeCompatibility; }
        }

        internal static double LegacySaveAgeCutoverDay
        {
            get { lock (SyncRoot) return _legacySaveAgeCutoverDay; }
        }

        internal static void MarkLegacySaveAgeCompatibility(double cutoverDay)
        {
            lock (SyncRoot)
            {
                _legacySaveAgeCompatibility = true;
                if (!double.IsNaN(cutoverDay) && !double.IsInfinity(cutoverDay))
                {
                    _legacySaveAgeCutoverDay = cutoverDay;
                }
            }
        }

        internal static void MarkModernSaveAgeCompatibility()
        {
            lock (SyncRoot)
            {
                _legacySaveAgeCompatibility = false;
                _legacySaveAgeCutoverDay = double.NaN;
            }
        }

        /// <summary>
        /// Compact editor representation used by the native Options tab and
        /// optional MCM. A pipe is used rather than a comma so name punctuation
        /// remains unambiguous.
        /// </summary>
        public static string MonthNamesDelimited
        {
            get { lock (SyncRoot) return string.Join("|", _monthNames); }
        }

        public static string SeasonNamesDelimited
        {
            get { lock (SyncRoot) return string.Join("|", _seasonNames); }
        }

        public static string MonthLengthsDelimited
        {
            get
            {
                lock (SyncRoot)
                {
                    return string.Join(
                        "|",
                        _monthLengths.Select(value => value.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        public static bool TryParseMonthNamesDelimited(
            string input,
            out string[] values,
            out string failure)
        {
            if (!TryParseDelimitedNames(input, 12, "month", out values, out failure))
            {
                return false;
            }

            return true;
        }

        public static bool TryParseSeasonNamesDelimited(
            string input,
            out string[] values,
            out string failure)
        {
            if (!TryParseDelimitedNames(input, 4, "season", out values, out failure))
            {
                return false;
            }

            return true;
        }

        public static bool TryParseMonthLengthsDelimited(
            string input,
            out int[] values,
            out string failure)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                failure = "Enter twelve positive month lengths separated by |.";
                return false;
            }

            string[] parts = input.Replace(',', '|').Split('|');
            if (parts.Length != 12)
            {
                failure = "Enter exactly twelve month lengths separated by |.";
                return false;
            }

            int[] parsed = new int[parts.Length];
            int total = 0;
            for (int index = 0; index < parts.Length; index++)
            {
                int value;
                if (!int.TryParse(
                        parts[index].Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out value)
                    || value < 1
                    || value > MaximumConfiguredMonthLength)
                {
                    failure = "Each month length must be a whole number from 1 to "
                        + MaximumConfiguredMonthLength + ".";
                    return false;
                }

                parsed[index] = value;
                total += value;
            }

            if (total != RequiredCommonDaysInYear)
            {
                failure = "Month lengths must total exactly " + RequiredCommonDaysInYear + " days.";
                return false;
            }

            values = parsed;
            failure = null;
            return true;
        }

        public static bool TryApplyMonthNamesDelimited(string input, out string failure)
        {
            string[] values;
            if (!TryParseMonthNamesDelimited(input, out values, out failure))
            {
                return false;
            }

            Apply(
                CalendarSystem,
                UseLeapYears,
                ShowDayLabel,
                ShowYearLabel,
                CampaignTimeScale,
                DateFormat,
                monthNames: values);
            Save();
            return true;
        }

        public static bool TryApplySeasonNamesDelimited(string input, out string failure)
        {
            string[] values;
            if (!TryParseSeasonNamesDelimited(input, out values, out failure))
            {
                return false;
            }

            Apply(
                CalendarSystem,
                UseLeapYears,
                ShowDayLabel,
                ShowYearLabel,
                CampaignTimeScale,
                DateFormat,
                seasonNames: values);
            Save();
            return true;
        }

        public static bool TryApplyMonthLengthsDelimited(string input, out string failure)
        {
            if (IsCampaignProfileLocked)
            {
                failure = "Month lengths are locked by the active campaign profile.";
                return false;
            }

            int[] values;
            if (!TryParseMonthLengthsDelimited(input, out values, out failure))
            {
                return false;
            }

            Apply(
                CalendarSystem,
                UseLeapYears,
                ShowDayLabel,
                ShowYearLabel,
                CampaignTimeScale,
                DateFormat,
                monthLengths: values);
            Save();
            return true;
        }

        public static string CalendarSystem
        {
            get { return FixedCalendarSystem; }
        }

        public static string ConfigPath
        {
            get
            {
                return Path.Combine(
                    GetModuleRoot(),
                    "AgesOfCalradia.settings.xml");
            }
        }

        private static string PreviousUserConfigPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord",
                    "Configs",
                    "AgesOfCalradia",
                    "settings.xml");
            }
        }

        private static string LegacyConfigPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord",
                    "Configs",
                    "TwelveMonthCalendar",
                    "settings.xml");
            }
        }

        public static void Apply(
            string calendarSystem,
            bool useLeapYears,
            bool showDayLabel,
            bool showYearLabel,
            float campaignTimeScale,
            string dateFormat,
            string[] monthNames = null,
            int[] monthLengths = null,
            string[] seasonNames = null,
            int? nativeDaysInYear = null,
            float? pregnancyDurationInDays = null,
            int? pregnancyDurationMonths = null,
            bool? useCalendarMonthPregnancy = null,
            bool? autoCampaignTimeScale = null,
            float? renownGainMultiplier = null,
            float? lordDeathRateMultiplier = null,
            bool? useOrdinalDaySuffixes = null,
            bool? use24HourClock = null,
            bool? balancePartyImpairment = null,
            bool? balancePrisonerRecruitment = null,
            bool? balanceNpcMarriage = null,
            bool? balanceMapTracks = null,
            bool? balanceQuestDeadlines = null,
            bool? annualBalanceEnabled = null,
            bool? annualBalanceDiagnosticsEnabled = null,
            float? normalPlayTimeMultiplier = null,
            float? fastForwardTimeMultiplier = null,
            bool? clockSynchronizedLighting = null,
            float? visualSunriseHour = null,
            float? visualSunsetHour = null,
            float? visualLightingTransitionHours = null,
            bool? strategicMapShowLegend = null,
            int? strategicMapLegendWidth = null,
            int? strategicMapLegendHeight = null,
            int? strategicMapLegendMarginTop = null,
            int? strategicMapLegendIconSize = null,
            int? strategicMapLegendFontSize = null,
            float? strategicMapMarkerSpacing = null,
            bool? strategicMapShowSettlementLabels = null,
            int? strategicMapLabelFontSize = null,
            int? politicalControlOpacityPercent = null,
            int? politicalControlBrightnessPercent = null,
            bool? politicalSolidWater = null)
        {
            lock (SyncRoot)
            {
                if (_campaignSessionStarted
                    && useLeapYears != _useLeapYears)
                {
                    Diagnostics.Info("Leap-year setting change ignored after campaign session start; restart the campaign to apply it.");
                }
                else
                {
                    _useLeapYears = useLeapYears;
                }

                if (!string.IsNullOrWhiteSpace(calendarSystem)
                    && !string.Equals(calendarSystem, FixedCalendarSystem, StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.Info(
                        "Ignoring legacy calendar-system setting '" + calendarSystem
                        + "'; Ages of Calradia is always Gregorian12Month.");
                }
                _showDayLabel = showDayLabel;
                _showYearLabel = showYearLabel;
                _useOrdinalDaySuffixes = useOrdinalDaySuffixes ?? _useOrdinalDaySuffixes;
                _use24HourClock = use24HourClock ?? _use24HourClock;
                _dateFormat = NormalizeDateFormat(dateFormat);

                ApplyMonthNames(monthNames);
                ApplySeasonNames(seasonNames);
                ApplyMonthLengths(monthLengths);
                int requestedNativeDaysInYear = nativeDaysInYear ?? _nativeDaysInYear;
                if (requestedNativeDaysInYear != DefaultNativeDaysInYear)
                {
                    Diagnostics.Info(
                        "Ignoring NativeDaysInYear=" + requestedNativeDaysInYear
                        + "; annual balance is calibrated to Bannerlord's native 84-day year.");
                }
                _nativeDaysInYear = DefaultNativeDaysInYear;
                float requestedPregnancyDays = pregnancyDurationInDays ?? _pregnancyDurationInDays;
                float normalizedPregnancyDays = IsFinite(requestedPregnancyDays)
                    ? Math.Max(0.1f, Math.Min(10000f, requestedPregnancyDays))
                    : DefaultPregnancyDurationInDays;
                ApplyCampaignStartSetting(
                    ref _pregnancyDurationInDays,
                    normalizedPregnancyDays,
                    "PregnancyDurationDays");
                // These control only future life-cycle calculations. Keep
                // them live-adjustable so the native slider arrows remain
                // usable in an existing campaign.
                _pregnancyDurationMonths = Math.Max(1, pregnancyDurationMonths ?? _pregnancyDurationMonths);
                _useCalendarMonthPregnancy = useCalendarMonthPregnancy ?? _useCalendarMonthPregnancy;
                float requestedRenownMultiplier = renownGainMultiplier ?? _renownGainMultiplier;
                float normalizedRenownMultiplier = IsFinite(requestedRenownMultiplier)
                    ? Math.Max(0f, Math.Min(1f, requestedRenownMultiplier))
                    : DefaultRenownGainMultiplier;
                _renownGainMultiplier = normalizedRenownMultiplier;
                float requestedLordDeathRateMultiplier = lordDeathRateMultiplier ?? _lordDeathRateMultiplier;
                float normalizedLordDeathRateMultiplier = IsFinite(requestedLordDeathRateMultiplier)
                    ? Math.Max(0f, Math.Min(1f, requestedLordDeathRateMultiplier))
                    : DefaultLordDeathRateMultiplier;
                _lordDeathRateMultiplier = normalizedLordDeathRateMultiplier;
                // These switches govern future model evaluations, so they are
                // safe to change in an existing campaign. Existing quest
                // deadlines are intentionally never rewritten.
                _balancePartyImpairment = balancePartyImpairment ?? _balancePartyImpairment;
                _balancePrisonerRecruitment = balancePrisonerRecruitment ?? _balancePrisonerRecruitment;
                _balanceNpcMarriage = balanceNpcMarriage ?? _balanceNpcMarriage;
                _balanceMapTracks = balanceMapTracks ?? _balanceMapTracks;
                _balanceQuestDeadlines = balanceQuestDeadlines ?? _balanceQuestDeadlines;
                _annualBalanceEnabled = annualBalanceEnabled ?? _annualBalanceEnabled;
                _annualBalanceDiagnosticsEnabled = annualBalanceDiagnosticsEnabled ?? _annualBalanceDiagnosticsEnabled;
                bool requestedAutoCampaignTimeScale = autoCampaignTimeScale ?? _autoCampaignTimeScale;
                // Campaign pacing changes only affect future map-time ticks, so
                // unlike simulation-profile values this control is safe to use
                // during an active campaign.
                _autoCampaignTimeScale = requestedAutoCampaignTimeScale;
                float normalizedCampaignTimeScale = requestedAutoCampaignTimeScale
                    ? DefaultCampaignTimeScale
                    : IsFinite(campaignTimeScale)
                        ? Math.Max(MinimumCampaignTimeScale, Math.Min(MaximumCampaignTimeScale, campaignTimeScale))
                        : DefaultCampaignTimeScale;
                _campaignTimeScale = normalizedCampaignTimeScale;
                // Fast-forward is intentionally runtime-safe. Campaign.TickMapTime
                // applies it on its next fast-forward tick through Bannerlord's
                // own SpeedUpMultiplier; it does not reinterpret saved time.
                _fastForwardTimeMultiplier = NormalizePacingMultiplier(
                    fastForwardTimeMultiplier ?? _fastForwardTimeMultiplier,
                    DefaultFastForwardTimeMultiplier);
                _clockSynchronizedLighting = clockSynchronizedLighting ?? _clockSynchronizedLighting;
                _visualSunriseHour = NormalizeLightingHour(
                    visualSunriseHour ?? _visualSunriseHour,
                    DefaultVisualSunriseHour);
                _visualSunsetHour = NormalizeLightingHour(
                    visualSunsetHour ?? _visualSunsetHour,
                    DefaultVisualSunsetHour);
                if (Math.Abs(_visualSunsetHour - _visualSunriseHour) < 0.25f)
                {
                    _visualSunriseHour = DefaultVisualSunriseHour;
                    _visualSunsetHour = DefaultVisualSunsetHour;
                }
                _visualLightingTransitionHours = NormalizeLightingTransition(
                    visualLightingTransitionHours ?? _visualLightingTransitionHours);
                _strategicMapShowLegend = strategicMapShowLegend ?? _strategicMapShowLegend;
                _strategicMapLegendWidth = ClampInt(
                    strategicMapLegendWidth ?? _strategicMapLegendWidth,
                    180,
                    400,
                    DefaultStrategicMapLegendWidth);
                _strategicMapLegendHeight = ClampInt(
                    strategicMapLegendHeight ?? _strategicMapLegendHeight,
                    60,
                    220,
                    DefaultStrategicMapLegendHeight);
                _strategicMapLegendMarginTop = ClampInt(
                    strategicMapLegendMarginTop ?? _strategicMapLegendMarginTop,
                    0,
                    80,
                    DefaultStrategicMapLegendMarginTop);
                _strategicMapLegendIconSize = ClampInt(
                    strategicMapLegendIconSize ?? _strategicMapLegendIconSize,
                    16,
                    64,
                    DefaultStrategicMapLegendIconSize);
                _strategicMapLegendFontSize = ClampInt(
                    strategicMapLegendFontSize ?? _strategicMapLegendFontSize,
                    8,
                    24,
                    DefaultStrategicMapLegendFontSize);
                _strategicMapMarkerSpacing = ClampFloat(
                    strategicMapMarkerSpacing ?? _strategicMapMarkerSpacing,
                    DefaultStrategicMapMarkerSpacing,
                    200f,
                    DefaultStrategicMapMarkerSpacing);
                _strategicMapShowSettlementLabels = strategicMapShowSettlementLabels ?? _strategicMapShowSettlementLabels;
                _strategicMapLabelFontSize = ClampInt(
                    strategicMapLabelFontSize ?? _strategicMapLabelFontSize,
                    8,
                    24,
                    DefaultStrategicMapLabelFontSize);
                _politicalControlOpacityPercent = ClampInt(
                    politicalControlOpacityPercent ?? _politicalControlOpacityPercent,
                    10,
                    100,
                    DefaultPoliticalControlOpacityPercent);
                _politicalControlBrightnessPercent = ClampInt(
                    politicalControlBrightnessPercent ?? _politicalControlBrightnessPercent,
                    25,
                    125,
                    DefaultPoliticalControlBrightnessPercent);
                _politicalSolidWater = politicalSolidWater ?? _politicalSolidWater;
            }

            Diagnostics.Info(
                string.Format(
                    "Settings applied. CalendarSystem={0}; LeapYears={1}; ShowDayLabel={2}; ShowYearLabel={3}; OrdinalDays={4}; Clock24Hour={5}; TimeScale={6:F6}; NormalPace=fixed; FastForwardSpeed={7:F0}; LightingSync={8}; VisualSunrise={9:F2}; VisualSunset={10:F2}; LightingTransition={11:F2}; LordDeathRate={12:F3}; DateFormat={13}",
                    FixedCalendarSystem,
                    UseLeapYears,
                    ShowDayLabel,
                    ShowYearLabel,
                    UseOrdinalDaySuffixes,
                    Use24HourClock,
                    CampaignTimeScale,
                    FastForwardTimeMultiplier,
                    ClockSynchronizedLighting,
                    VisualSunriseHour,
                    VisualSunsetHour,
                    VisualLightingTransitionHours,
                    LordDeathRateMultiplier,
                    DateFormat));

            NotifySettingsChanged();
        }

        public static void ResetToDefaults()
        {
            Apply(
                FixedCalendarSystem,
                true,
                false,
                false,
                DefaultCampaignTimeScale,
                DefaultDateFormat,
                (string[])DefaultMonthNames.Clone(),
                (int[])DefaultMonthLengths.Clone(),
                (string[])DefaultSeasonNames.Clone(),
                DefaultNativeDaysInYear,
                DefaultPregnancyDurationInDays,
                DefaultPregnancyDurationMonths,
                true,
                true,
                DefaultRenownGainMultiplier,
                lordDeathRateMultiplier: DefaultLordDeathRateMultiplier,
                useOrdinalDaySuffixes: DefaultUseOrdinalDaySuffixes,
                use24HourClock: DefaultUse24HourClock,
                balancePartyImpairment: true,
                balancePrisonerRecruitment: true,
                balanceNpcMarriage: true,
                balanceMapTracks: true,
                balanceQuestDeadlines: true,
                annualBalanceEnabled: true,
                annualBalanceDiagnosticsEnabled: true,
                normalPlayTimeMultiplier: DefaultNormalPlayTimeMultiplier,
                fastForwardTimeMultiplier: DefaultFastForwardTimeMultiplier,
                clockSynchronizedLighting: DefaultClockSynchronizedLighting,
                visualSunriseHour: DefaultVisualSunriseHour,
                visualSunsetHour: DefaultVisualSunsetHour,
                visualLightingTransitionHours: DefaultVisualLightingTransitionHours,
                strategicMapShowLegend: DefaultStrategicMapShowLegend,
                strategicMapLegendWidth: DefaultStrategicMapLegendWidth,
                strategicMapLegendHeight: DefaultStrategicMapLegendHeight,
                strategicMapLegendMarginTop: DefaultStrategicMapLegendMarginTop,
                strategicMapLegendIconSize: DefaultStrategicMapLegendIconSize,
                strategicMapLegendFontSize: DefaultStrategicMapLegendFontSize,
                strategicMapMarkerSpacing: DefaultStrategicMapMarkerSpacing,
                strategicMapShowSettlementLabels: DefaultStrategicMapShowSettlementLabels,
                strategicMapLabelFontSize: DefaultStrategicMapLabelFontSize,
                politicalControlOpacityPercent: DefaultPoliticalControlOpacityPercent,
                politicalControlBrightnessPercent: DefaultPoliticalControlBrightnessPercent,
                politicalSolidWater: DefaultPoliticalSolidWater);
            Save();
            Diagnostics.Info("Calendar settings reset to defaults.");
        }

        internal static void ResetCalendarCategory()
        {
            lock (SyncRoot)
            {
                if (!_campaignSessionStarted)
                {
                    _useLeapYears = true;
                    Array.Copy(DefaultMonthLengths, _monthLengths, DefaultMonthLengths.Length);
                    RebuildMonthCache();
                }

                Array.Copy(DefaultMonthNames, _monthNames, DefaultMonthNames.Length);
                Array.Copy(DefaultSeasonNames, _seasonNames, DefaultSeasonNames.Length);
            }

            Save();
            NotifySettingsChanged();
            Diagnostics.Info("Calendar category settings reset to defaults.");
        }

        internal static void MarkCampaignSessionStarted()
        {
            lock (SyncRoot)
            {
                _campaignSessionStarted = true;
            }
        }

        /// <summary>
        /// Clears campaign-owned runtime state before Bannerlord creates or
        /// loads another campaign in the same process. SyncData restores the
        /// saved compatibility mode immediately afterward for loaded games.
        /// </summary>
        internal static void BeginCampaignSession()
        {
            lock (SyncRoot)
            {
                _campaignSessionStarted = false;
                _legacySaveAgeCompatibility = false;
                _legacySaveAgeCutoverDay = double.NaN;
            }
        }

        /// <summary>
        /// Restores the simulation profile stored inside a campaign save. This
        /// intentionally bypasses normal session-start locks because the save,
        /// rather than the user's current global XML/MCM values, owns these
        /// values for the active campaign.
        /// </summary>
        internal static void ApplyPersistedCampaignProfile(CalendarCampaignProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            string failure;
            int[] profileMonthLengths;
            if (!profile.TryValidate(out failure)
                || !profile.TryGetMonthLengths(out profileMonthLengths))
            {
                Diagnostics.Info(
                    "Saved campaign profile restore was skipped because "
                    + (failure ?? "its month lengths are invalid.")
                    + ".");
                return;
            }

            lock (SyncRoot)
            {
                _useLeapYears = profile.UseLeapYears;
                for (int index = 0; index < _monthLengths.Length; index++)
                {
                    _monthLengths[index] = profileMonthLengths[index];
                }

                RebuildMonthCache();
                _autoCampaignTimeScale = profile.AutoCampaignTimeScale;
                _campaignTimeScale = _autoCampaignTimeScale
                    ? DefaultCampaignTimeScale
                    : Math.Max(MinimumCampaignTimeScale, Math.Min(MaximumCampaignTimeScale, profile.CampaignTimeScale));
                _fastForwardTimeMultiplier = NormalizePacingMultiplier(
                    profile.FastForwardTimeMultiplier,
                    DefaultFastForwardTimeMultiplier);
                _useCalendarMonthPregnancy = profile.UseCalendarMonthPregnancy;
                _pregnancyDurationMonths = Math.Max(1, profile.PregnancyDurationMonths);
                _pregnancyDurationInDays = Math.Max(0.1f, Math.Min(10000f, profile.PregnancyDurationInDays));
                _renownGainMultiplier = Math.Max(0f, Math.Min(1f, profile.RenownGainMultiplier));
                _lordDeathRateMultiplier = Math.Max(0f, Math.Min(1f, profile.LordDeathRateMultiplier));
                _balancePartyImpairment = profile.BalancePartyImpairment;
                _balancePrisonerRecruitment = profile.BalancePrisonerRecruitment;
                _balanceNpcMarriage = profile.BalanceNpcMarriage;
                _balanceMapTracks = profile.BalanceMapTracks;
                _balanceQuestDeadlines = profile.BalanceQuestDeadlines;
                _annualBalanceEnabled = profile.AnnualBalanceEnabled;
            }

            Diagnostics.Info(
                "Persisted campaign profile applied. Fingerprint=" + profile.Fingerprint
                + "; TimeScale=" + CampaignTimeScale.ToString("F6", CultureInfo.InvariantCulture)
                + "; NormalPace=fixed"
                + "; FastForwardSpeed=" + FastForwardTimeMultiplier.ToString("F0", CultureInfo.InvariantCulture)
                + "; LordDeathRate=" + LordDeathRateMultiplier.ToString("F3", CultureInfo.InvariantCulture)
                + ".");
            NotifySettingsChanged();
        }

        internal static bool IsGameplaySettingLocked(string settingName)
        {
            if (!IsCampaignProfileLocked || string.IsNullOrWhiteSpace(settingName))
            {
                return false;
            }

            switch (settingName)
            {
                case "Use Leap Years":
                default:
                    return false;
            }
        }

        public static void Load()
        {
            try
            {
                string settingsPath = ConfigPath;
                bool migratedLegacyPath = false;
                if (!File.Exists(settingsPath) && File.Exists(PreviousUserConfigPath))
                {
                    settingsPath = PreviousUserConfigPath;
                    migratedLegacyPath = true;
                }
                else if (!File.Exists(settingsPath) && File.Exists(LegacyConfigPath))
                {
                    settingsPath = LegacyConfigPath;
                    migratedLegacyPath = true;
                }

                if (!File.Exists(settingsPath))
                {
                    Diagnostics.Info("No standalone settings file found; using defaults.");
                    Save();
                    return;
                }

                XmlDocument document = new XmlDocument();
                document.Load(settingsPath);
                XmlElement root = document.DocumentElement;
                if (root == null
                    || (root.Name != "AgesOfCalradia"
                        && root.Name != "TwelveMonthCalendar"))
                {
                    throw new InvalidDataException("The standalone settings file has an invalid root element.");
                }

                Apply(
                    ReadAttribute(root, "CalendarSystem", FixedCalendarSystem),
                    ReadBoolean(root, "UseLeapYears", true),
                    ReadBoolean(root, "ShowDayLabel", false),
                    ReadBoolean(root, "ShowYearLabel", false),
                    ReadFloat(root, "CampaignTimeScale", DefaultCampaignTimeScale),
                    ReadAttribute(root, "DateFormat", DefaultDateFormat),
                    ReadMonthNames(root),
                    ReadMonthLengths(root),
                    ReadSeasonNames(root),
                    ReadInt(root, "NativeDaysInYear", DefaultNativeDaysInYear),
                    ReadFloat(root, "PregnancyDurationDays", DefaultPregnancyDurationInDays),
                    ReadInt(root, "PregnancyDurationMonths", DefaultPregnancyDurationMonths),
                    ReadBoolean(root, "UseCalendarMonthPregnancy", true),
                    ReadBoolean(root, "AutoCampaignTimeScale", true),
                    ReadFloat(root, "RenownGainMultiplier", DefaultRenownGainMultiplier),
                    lordDeathRateMultiplier: ReadFloat(
                        root,
                        "LordDeathRateMultiplier",
                        DefaultLordDeathRateMultiplier),
                    useOrdinalDaySuffixes: ReadBoolean(root, "UseOrdinalDaySuffixes", DefaultUseOrdinalDaySuffixes),
                    use24HourClock: ReadBoolean(root, "Use24HourClock", DefaultUse24HourClock),
                    balancePartyImpairment: ReadBoolean(root, "BalancePartyImpairment", true),
                    balancePrisonerRecruitment: ReadBoolean(root, "BalancePrisonerRecruitment", true),
                    balanceNpcMarriage: ReadBoolean(root, "BalanceNpcMarriage", true),
                    balanceMapTracks: ReadBoolean(root, "BalanceMapTracks", true),
                    balanceQuestDeadlines: ReadBoolean(root, "BalanceQuestDeadlines", true),
                    annualBalanceEnabled: ReadBoolean(root, "AnnualBalanceEnabled", true),
                    annualBalanceDiagnosticsEnabled: ReadBoolean(root, "AnnualBalanceDiagnosticsEnabled", true),
                    fastForwardTimeMultiplier: ReadFastForwardSpeed(root),
                    clockSynchronizedLighting: ReadBoolean(
                        root,
                        "ClockSynchronizedLighting",
                        DefaultClockSynchronizedLighting),
                    visualSunriseHour: ReadFloat(
                        root,
                        "VisualSunriseHour",
                        DefaultVisualSunriseHour),
                    visualSunsetHour: ReadFloat(
                        root,
                        "VisualSunsetHour",
                        DefaultVisualSunsetHour),
                    visualLightingTransitionHours: ReadFloat(
                        root,
                        "VisualLightingTransitionHours",
                        DefaultVisualLightingTransitionHours),
                    strategicMapShowLegend: ReadBoolean(root, "StrategicMapShowLegend", DefaultStrategicMapShowLegend),
                    strategicMapLegendWidth: ReadInt(root, "StrategicMapLegendWidth", DefaultStrategicMapLegendWidth),
                    strategicMapLegendHeight: ReadInt(root, "StrategicMapLegendHeight", DefaultStrategicMapLegendHeight),
                    strategicMapLegendMarginTop: ReadInt(root, "StrategicMapLegendMarginTop", DefaultStrategicMapLegendMarginTop),
                    strategicMapLegendIconSize: ReadInt(root, "StrategicMapLegendIconSize", DefaultStrategicMapLegendIconSize),
                    strategicMapLegendFontSize: ReadInt(root, "StrategicMapLegendFontSize", DefaultStrategicMapLegendFontSize),
                    strategicMapMarkerSpacing: ReadFloat(root, "StrategicMapMarkerSpacing", DefaultStrategicMapMarkerSpacing),
                    strategicMapShowSettlementLabels: ReadBoolean(root, "StrategicMapShowSettlementLabels", DefaultStrategicMapShowSettlementLabels),
                    strategicMapLabelFontSize: ReadInt(root, "StrategicMapLabelFontSize", DefaultStrategicMapLabelFontSize),
                    politicalControlOpacityPercent: ReadInt(root, "PoliticalControlOpacityPercent", DefaultPoliticalControlOpacityPercent),
                    politicalControlBrightnessPercent: ReadInt(root, "PoliticalControlBrightnessPercent", DefaultPoliticalControlBrightnessPercent),
                    politicalSolidWater: ReadBoolean(root, "PoliticalSolidWater", DefaultPoliticalSolidWater));

                Diagnostics.Info(
                    string.Format(
                        "Standalone settings loaded from {0}.{1}",
                        settingsPath,
                        migratedLegacyPath
                            ? " Values will be copied to the legacy internal settings path"
                            : string.Empty));
                // Rewrite the file after loading so newly added configurable
                // fields and the renamed settings path are added automatically.
                Save();
            }
            catch (Exception exception)
            {
                Diagnostics.Error("Standalone settings could not be loaded; defaults remain active.", exception);
            }
        }

        public static void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                XmlDocument document = new XmlDocument();
                XmlElement root = document.CreateElement("AgesOfCalradia");
                document.AppendChild(root);
                root.SetAttribute("UseLeapYears", UseLeapYears.ToString());
                root.SetAttribute("ShowDayLabel", ShowDayLabel.ToString());
                root.SetAttribute("ShowYearLabel", ShowYearLabel.ToString());
                root.SetAttribute("UseOrdinalDaySuffixes", UseOrdinalDaySuffixes.ToString());
                root.SetAttribute("Use24HourClock", Use24HourClock.ToString());
                root.SetAttribute("CampaignTimeScale", CampaignTimeScale.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("AutoCampaignTimeScale", AutoCampaignTimeScale.ToString());
                root.SetAttribute(
                    "FastForwardSpeedMultiplier",
                    FastForwardTimeMultiplier.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("ClockSynchronizedLighting", ClockSynchronizedLighting.ToString());
                root.SetAttribute("VisualSunriseHour", VisualSunriseHour.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("VisualSunsetHour", VisualSunsetHour.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute(
                    "VisualLightingTransitionHours",
                    VisualLightingTransitionHours.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapShowLegend", StrategicMapShowLegend.ToString());
                root.SetAttribute("StrategicMapLegendWidth", StrategicMapLegendWidth.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapLegendHeight", StrategicMapLegendHeight.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapLegendMarginTop", StrategicMapLegendMarginTop.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapLegendIconSize", StrategicMapLegendIconSize.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapLegendFontSize", StrategicMapLegendFontSize.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapMarkerSpacing", StrategicMapMarkerSpacing.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("StrategicMapShowSettlementLabels", StrategicMapShowSettlementLabels.ToString());
                root.SetAttribute("StrategicMapLabelFontSize", StrategicMapLabelFontSize.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("PoliticalControlOpacityPercent", PoliticalControlOpacityPercent.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("PoliticalControlBrightnessPercent", PoliticalControlBrightnessPercent.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("PoliticalSolidWater", PoliticalSolidWater.ToString());
                root.SetAttribute("DateFormat", DateFormat);
                root.SetAttribute("NativeDaysInYear", NativeDaysInYear.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("PregnancyDurationDays", PregnancyDurationInDays.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("PregnancyDurationMonths", PregnancyDurationMonths.ToString(CultureInfo.InvariantCulture));
                root.SetAttribute("UseCalendarMonthPregnancy", UseCalendarMonthPregnancy.ToString());
                root.SetAttribute("RenownGainMultiplier", RenownGainMultiplier.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute(
                    "LordDeathRateMultiplier",
                    LordDeathRateMultiplier.ToString("R", CultureInfo.InvariantCulture));
                root.SetAttribute("BalancePartyImpairment", BalancePartyImpairment.ToString());
                root.SetAttribute("BalancePrisonerRecruitment", BalancePrisonerRecruitment.ToString());
                root.SetAttribute("BalanceNpcMarriage", BalanceNpcMarriage.ToString());
                root.SetAttribute("BalanceMapTracks", BalanceMapTracks.ToString());
                root.SetAttribute("BalanceQuestDeadlines", BalanceQuestDeadlines.ToString());
                root.SetAttribute("AnnualBalanceEnabled", AnnualBalanceEnabled.ToString());
                root.SetAttribute("AnnualBalanceDiagnosticsEnabled", AnnualBalanceDiagnosticsEnabled.ToString());
                string[] monthNames = MonthNamesSnapshot();
                string[] seasonNames = SeasonNamesSnapshot();
                int[] monthLengths = MonthLengthsSnapshot();
                for (int i = 0; i < 12; i++)
                {
                    root.SetAttribute(string.Format("Month{0}Name", i + 1), monthNames[i]);
                    root.SetAttribute(string.Format("Month{0}Days", i + 1), monthLengths[i].ToString(CultureInfo.InvariantCulture));
                }
                for (int i = 0; i < seasonNames.Length; i++)
                {
                    root.SetAttribute(string.Format("Season{0}Name", i + 1), seasonNames[i]);
                }
                string temporaryPath = ConfigPath + ".tmp";
                document.Save(temporaryPath);
                if (File.Exists(ConfigPath))
                {
                    File.Replace(temporaryPath, ConfigPath, null);
                }
                else
                {
                    File.Move(temporaryPath, ConfigPath);
                }

                Diagnostics.Info(string.Format("Standalone settings saved to {0}.", ConfigPath));
            }
            catch (Exception exception)
            {
                try
                {
                    string temporaryPath = ConfigPath + ".tmp";
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch { }
                Diagnostics.Error("Standalone settings could not be saved.", exception);
            }
        }

        private static string GetModuleRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(typeof(CalendarSettingsState).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory))
            {
                throw new InvalidOperationException("The module assembly location is unavailable.");
            }

            DirectoryInfo binaryDirectory = Directory.GetParent(assemblyDirectory);
            DirectoryInfo moduleDirectory = binaryDirectory == null ? null : binaryDirectory.Parent;
            if (moduleDirectory == null)
            {
                throw new InvalidOperationException("The module directory could not be resolved.");
            }

            return moduleDirectory.FullName;
        }

        private static string ReadAttribute(XmlElement root, string name, string fallback)
        {
            string value = root.GetAttribute(name);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void NotifySettingsChanged()
        {
            Action changed = SettingsChanged;
            if (changed == null)
            {
                return;
            }

            try
            {
                changed();
            }
            catch (Exception exception)
            {
                Diagnostics.Error("A settings synchronization listener failed.", exception);
            }
        }

        private static void ApplyCampaignStartSetting(ref bool currentValue, bool? requestedValue, string name)
        {
            if (!requestedValue.HasValue || requestedValue.Value == currentValue)
            {
                return;
            }

            if (_campaignSessionStarted)
            {
                Diagnostics.Info(name + " change ignored after campaign session start; restart the campaign to apply it.");
                return;
            }

            currentValue = requestedValue.Value;
        }

        private static void ApplyCampaignStartSetting(ref float currentValue, float requestedValue, string name)
        {
            if (Math.Abs(requestedValue - currentValue) < 0.0001f)
            {
                return;
            }

            if (_campaignSessionStarted)
            {
                Diagnostics.Info(name + " change ignored after campaign session start; this save's campaign profile owns it.");
                return;
            }

            currentValue = requestedValue;
        }

        private static void ApplyCampaignStartSetting(ref int currentValue, int requestedValue, string name)
        {
            if (requestedValue == currentValue)
            {
                return;
            }

            if (_campaignSessionStarted)
            {
                Diagnostics.Info(name + " change ignored after campaign session start; this save's campaign profile owns it.");
                return;
            }

            currentValue = requestedValue;
        }

        private static bool ReadBoolean(XmlElement root, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(root.GetAttribute(name), out value) ? value : fallback;
        }

        private static float ReadFloat(XmlElement root, string name, float fallback)
        {
            float value;
            if (!float.TryParse(
                root.GetAttribute(name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
            {
                return fallback;
            }

            return IsFinite(value) ? value : fallback;
        }

        private static float ReadFastForwardSpeed(XmlElement root)
        {
            if (root.HasAttribute("FastForwardSpeedMultiplier"))
            {
                return ReadFloat(
                    root,
                    "FastForwardSpeedMultiplier",
                    DefaultFastForwardTimeMultiplier);
            }

            if (root.HasAttribute("FastForwardTimeMultiplier"))
            {
                return ConvertLegacyFastForwardPacingToSpeed(
                    ReadFloat(root, "FastForwardTimeMultiplier", 1f));
            }

            return DefaultFastForwardTimeMultiplier;
        }

        private static float NormalizeLightingHour(float value, float fallback)
        {
            if (!IsFinite(value))
            {
                return fallback;
            }

            float normalized = value % 24f;
            if (normalized < 0f)
            {
                normalized += 24f;
            }

            return normalized;
        }

        private static float NormalizeLightingTransition(float value)
        {
            return IsFinite(value)
                ? Math.Max(0.25f, Math.Min(4f, value))
                : DefaultVisualLightingTransitionHours;
        }

        private static int ClampInt(int value, int minimum, int maximum, int fallback)
        {
            return value < minimum || value > maximum ? fallback : value;
        }

        private static float ClampFloat(float value, float minimum, float maximum, float fallback)
        {
            return !IsFinite(value) || value < minimum || value > maximum ? fallback : value;
        }

        private static int ReadInt(XmlElement root, string name, int fallback)
        {
            int value;
            return int.TryParse(root.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static string[] ReadMonthNames(XmlElement root)
        {
            string[] values = (string[])DefaultMonthNames.Clone();
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ReadAttribute(root, string.Format("Month{0}Name", i + 1), values[i]);
            }

            return values;
        }

        private static bool TryParseDelimitedNames(
            string input,
            int expectedCount,
            string label,
            out string[] values,
            out string failure)
        {
            values = null;
            if (string.IsNullOrWhiteSpace(input))
            {
                failure = "Enter " + expectedCount + " " + label + " names separated by |.";
                return false;
            }

            string[] parts = input.Split('|');
            if (parts.Length != expectedCount)
            {
                failure = "Enter exactly " + expectedCount + " " + label + " names separated by |.";
                return false;
            }

            string[] parsed = new string[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                string value = parts[index] == null ? string.Empty : parts[index].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    failure = "Every " + label + " name must contain text.";
                    return false;
                }

                if (value.Length > MaximumConfiguredMonthNameLength)
                {
                    failure = label + " names are limited to " + MaximumConfiguredMonthNameLength + " characters.";
                    return false;
                }

                parsed[index] = value;
            }

            values = parsed;
            failure = null;
            return true;
        }

        private static string[] ReadSeasonNames(XmlElement root)
        {
            string[] values = (string[])DefaultSeasonNames.Clone();
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = ReadAttribute(root, string.Format("Season{0}Name", i + 1), values[i]);
            }

            return values;
        }

        private static int[] ReadMonthLengths(XmlElement root)
        {
            int[] values = (int[])DefaultMonthLengths.Clone();
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Math.Max(
                    1,
                    Math.Min(
                        MaximumConfiguredMonthLength,
                        ReadInt(root, string.Format("Month{0}Days", i + 1), values[i])));
            }

            return values;
        }

        private static void ApplyMonthNames(string[] values)
        {
            if (values == null || values.Length != _monthNames.Length)
            {
                return;
            }

            for (int i = 0; i < _monthNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    string name = values[i].Trim();
                    _monthNames[i] = name.Length > MaximumConfiguredMonthNameLength
                        ? name.Substring(0, MaximumConfiguredMonthNameLength)
                        : name;
                }
            }
        }

        private static void ApplySeasonNames(string[] values)
        {
            if (values == null || values.Length != _seasonNames.Length)
            {
                return;
            }

            for (int i = 0; i < _seasonNames.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    string name = values[i].Trim();
                    _seasonNames[i] = name.Length > MaximumConfiguredMonthNameLength
                        ? name.Substring(0, MaximumConfiguredMonthNameLength)
                        : name;
                }
            }
        }

        private static void ApplyMonthLengths(int[] values)
        {
            if (values == null || values.Length != _monthLengths.Length)
            {
                return;
            }

            int totalDays = 0;
            for (int i = 0; i < values.Length; i++)
            {
                totalDays += Math.Max(1, Math.Min(MaximumConfiguredMonthLength, values[i]));
            }

            if (totalDays != RequiredCommonDaysInYear)
            {
                Diagnostics.Info(
                    "Ignoring custom month lengths totaling " + totalDays
                    + " days; Ages of Calradia requires exactly "
                    + RequiredCommonDaysInYear + " common-year days.");
                return;
            }

            if (_campaignSessionStarted)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    int normalized = Math.Max(1, Math.Min(MaximumConfiguredMonthLength, values[i]));
                    if (normalized != _monthLengths[i])
                    {
                        Diagnostics.Info(
                            "Custom month-length change ignored after campaign session start; restart the campaign to apply it.");
                        return;
                    }
                }
            }

            for (int i = 0; i < _monthLengths.Length; i++)
            {
                _monthLengths[i] = Math.Max(1, Math.Min(MaximumConfiguredMonthLength, values[i]));
            }

            RebuildMonthCache();
        }

        private static void RebuildMonthCache()
        {
            int runningTotal = 0;
            for (int i = 0; i < _monthLengths.Length; i++)
            {
                _monthStarts[i] = runningTotal;
                runningTotal += _monthLengths[i];
            }

            _commonDaysInYear = runningTotal;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string NormalizeDateFormat(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultDateFormat;
            }

            string normalized = value;
            string[] tokens = { "Month", "Season", "Day", "Year", "MonthNumber", "DayOfYear" };
            foreach (string token in tokens)
            {
                normalized = Regex.Replace(
                    normalized,
                    "\\{" + token + "\\}",
                    "{" + token + "}",
                    RegexOptions.IgnoreCase);
            }

            return normalized;
        }

        private static float NormalizePacingMultiplier(float value, float fallback)
        {
            return IsFinite(value)
                ? Math.Max(MinimumPacingMultiplier, Math.Min(MaximumPacingMultiplier, value))
                : fallback;
        }

        internal static float ConvertLegacyFastForwardPacingToSpeed(float legacyPacing)
        {
            if (!IsFinite(legacyPacing))
            {
                return DefaultFastForwardTimeMultiplier;
            }

            float normalizedLegacyPacing = Math.Max(0.1f, Math.Min(10f, legacyPacing));
            return NormalizePacingMultiplier(
                normalizedLegacyPacing * DefaultFastForwardTimeMultiplier,
                DefaultFastForwardTimeMultiplier);
        }
    }
}
