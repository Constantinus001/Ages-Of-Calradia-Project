using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.SceneInformationPopupTypes;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // UI boundary: the native clock can contain a line break between the time
    // and meridiem. The target MapBar keeps them together on one compact line.
    public sealed class CompactMapClockTextWidget : TextWidget
    {
        private string _clockSourceText = string.Empty;

        public CompactMapClockTextWidget(UIContext context) : base(context) { }

        public string ClockSourceText
        {
            get { return _clockSourceText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_clockSourceText, normalized, StringComparison.Ordinal))
                    return;
                _clockSourceText = normalized;
                Text = NormalizeForVerification(normalized);
            }
        }

        internal static string NormalizeForVerification(string source)
        {
            string normalized = (source ?? string.Empty)
                .Replace("\r", string.Empty).Trim();
            string[] lines = normalized.Split(new[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < lines.Length; index++)
                lines[index] = lines[index].Trim();
            return string.Join(" ", lines).Trim();
        }
    }

    // UI-only formatting: the season is shown by the bezel, leaving only the year below the date.
    public sealed class MapYearTextWidget : TextWidget
    {
        private string _sourceText = string.Empty;

        public MapYearTextWidget(UIContext context) : base(context) { }

        public string SourceText
        {
            get { return _sourceText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_sourceText, normalized, StringComparison.Ordinal))
                    return;
                _sourceText = normalized;
                Text = ExtractYearForVerification(normalized);
            }
        }

        internal static string ExtractYearForVerification(string source)
        {
            string normalized = (source ?? string.Empty).Trim();
            int separator = normalized.IndexOf(',');
            return separator < 0 ? normalized : normalized.Substring(0, separator).Trim();
        }
    }

    public sealed class Approved560CalendarFixesSubModule : MBSubModuleBase
    {
        internal const string HarmonyId = "AgesOfCalradia.Approved560CalendarFixes.560F1B51";
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                ApprovedCalendarBridge.Validate();
                CalendarFixTargets.Validate();
                WorkshopRecipeCadenceFix.ValidateTargets();
                WorkshopPaymentConservationFix.ValidateTargets();
                WorkshopApprovalQuoteFix.ValidateTargets();
                SettlementPaymentConservationFix.ValidateTargets();
                AiSettlementSaleQuantityFix.ValidateTargets();

                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(typeof(Approved560CalendarFixesSubModule).Assembly);

                // The approved DLL remains byte-for-byte untouched. Remove only
                // the superseded calendar patches whose replacements were
                // successfully installed above. Political renderer patches are
                // deliberately outside this list.
                LegacyPatchControl.UnpatchDeclaringTypes(
                    "TwelveMonthCalendar.MapTimeTrackerPatch",
                    "TwelveMonthCalendar.CampaignPacingPatch",
                    "TwelveMonthCalendar.WorkshopProductionBalancePatch",
                    "TwelveMonthCalendar.WorkshopFoodContextPatch",
                    "TwelveMonthCalendar.VillageFoodProductionBalancePatch",
                    "TwelveMonthCalendar.VillageProductionBalancePatch",
                    "TwelveMonthCalendar.SettlementDemandBalancePatch",
                    "TwelveMonthCalendar.SettlementBudgetBalancePatch",
                    "TwelveMonthCalendar.SettlementMarketSmoothingBalancePatch",
                    "TwelveMonthCalendar.KingdomWarCooldownPatch");
            }
            catch (Exception exception)
            {
                if (_harmony != null)
                {
                    _harmony.UnpatchAll(HarmonyId);
                    _harmony = null;
                }
                System.Diagnostics.Trace.WriteLine(
                    "AgesOfCalradia approved-build calendar fixes disabled safely: " + exception);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            try
            {
                // This sidecar owns the MapBar providers and must not rely on
                // another module's later provider-discovery refresh.
                TextureProviderFactory.RefreshProviderTypes();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.WriteLine(
                    "AgesOfCalradia MapBar texture-provider registration failed safely: " + exception);
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            MapTimeControlFourTimesButtonPatch.ResetRegisteredButtons();
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }
            base.OnSubModuleUnloaded();
        }
    }

    [HarmonyPatch]
    internal static class MapClockMeridiemLayoutPatch
    {
        private static PropertyInfo _timeOfDayProperty;
        private static bool _smoothClockFailureLogged;

        private static MethodBase TargetMethod()
        {
            Assembly approved = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(
                    a.GetName().Name,
                    "AgesOfCalradia",
                    StringComparison.Ordinal));
            Type clockType = approved == null
                ? null
                : approved.GetType("TwelveMonthCalendar.CalendarMapTimeControlVM", false);
            if (clockType == null)
                throw new TypeLoadException("TwelveMonthCalendar.CalendarMapTimeControlVM");

            _timeOfDayProperty = clockType.GetProperty(
                "TimeOfDay",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_timeOfDayProperty == null || !_timeOfDayProperty.CanRead || !_timeOfDayProperty.CanWrite)
                throw new MissingMemberException(clockType.FullName, "TimeOfDay");

            MethodInfo refreshClock = clockType.GetMethod(
                "RefreshClock",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (refreshClock == null)
                throw new MissingMethodException(clockType.FullName, "RefreshClock");
            return refreshClock;
        }

        private static void Postfix(object __instance)
        {
            if (__instance == null || Campaign.Current == null || _timeOfDayProperty == null)
                return;

            string current = _timeOfDayProperty.GetValue(__instance, null) as string;
            if (string.IsNullOrWhiteSpace(current))
                return;

            double hourInDay = CampaignTime.Now.ToHours % CampaignTime.HoursInDay;
            if (hourInDay < 0d)
                hourInDay += CampaignTime.HoursInDay;
            string formatted = FormatForVerification(current, (int)Math.Floor(hourInDay));
            _timeOfDayProperty.SetValue(__instance, formatted, null);
        }

        internal static string FormatForVerification(string current, int hour)
        {
            string clock = (current ?? string.Empty).Replace("\r", string.Empty);
            int newline = clock.IndexOf('\n');
            if (newline >= 0)
                clock = clock.Substring(0, newline);
            clock = clock.Trim();
            if (clock.EndsWith(" AM", StringComparison.OrdinalIgnoreCase)
                || clock.EndsWith(" PM", StringComparison.OrdinalIgnoreCase))
            {
                clock = clock.Substring(0, clock.Length - 3).TrimEnd();
            }

            int normalizedHour = ((hour % 24) + 24) % 24;
            return clock + "\n" + (normalizedHour < 12 ? "AM" : "PM");
        }

        internal static void SetSmoothedMinute(object instance, long absoluteMinute)
        {
            if (instance == null || _timeOfDayProperty == null)
                return;

            try
            {
                _timeOfDayProperty.SetValue(
                    instance,
                    SmoothMapClockMinutePatch.FormatMinuteForVerification(absoluteMinute),
                    null);
            }
            catch (TargetInvocationException exception)
            {
                LogSmoothClockFailure(exception);
            }
            catch (ArgumentException exception)
            {
                LogSmoothClockFailure(exception);
            }
        }

        private static void LogSmoothClockFailure(Exception exception)
        {
            if (_smoothClockFailureLogged)
                return;

            _smoothClockFailureLogged = true;
            System.Diagnostics.Trace.WriteLine(
                "AOC minute-by-minute map clock failed safely: " + exception);
        }
    }

    // Native target: MapTimeControlVM.Tick(), Bannerlord 1.4.8.
    // Purpose: refresh the native dial value and derive the clock from that same
    // value. No independent minute catch-up clock. CampaignTime, AI, events,
    // and simulation ticks remain untouched.
    // Compatibility/failure: startup validates the native target and protected VM
    // property. Reflection failures retain the native clock and log once. The pure
    // dial-to-minute and formatting contracts are covered by the sidecar verifier.
    [HarmonyPatch]
    internal static class SmoothMapClockMinutePatch
    {
        private static readonly ConditionalWeakTable<MapTimeControlVM, DisplayMinuteState> States =
            new ConditionalWeakTable<MapTimeControlVM, DisplayMinuteState>();

        private static MethodBase TargetMethod()
        {
            return CalendarFixTargets.MapTimeControlTick;
        }

        private static void Postfix(MapTimeControlVM __instance)
        {
            if (__instance == null || Campaign.Current == null || !ApprovedCalendarBridge.ExtendedEnabled)
                return;

            __instance.Time = CampaignTime.Now.ToHours % 24d;
            DisplayMinuteState state = States.GetOrCreateValue(__instance);
            ConfigureNativeHints(__instance, state);
            MapClockMeridiemLayoutPatch.SetSmoothedMinute(
                __instance, MinuteFromDialForVerification(__instance.Time));
        }

        private static void ConfigureNativeHints(MapTimeControlVM instance, DisplayMinuteState state)
        {
            if (!ReferenceEquals(state.PauseHint, instance.PauseHint))
            {
                state.PauseHint = instance.PauseHint;
                if (state.PauseHint != null)
                    state.PauseHint.SetHintCallback(GetPauseHintText);
            }
            if (!ReferenceEquals(state.PlayHint, instance.PlayHint))
            {
                state.PlayHint = instance.PlayHint;
                if (state.PlayHint != null)
                    state.PlayHint.SetHintCallback(GetPlayHintText);
            }
            if (!ReferenceEquals(state.FastForwardHint, instance.FastForwardHint))
            {
                state.FastForwardHint = instance.FastForwardHint;
                if (state.FastForwardHint != null)
                    state.FastForwardHint.SetHintCallback(MapTimeControlFourTimesButtonPatch.GetFastForwardHintText);
            }
        }

        private static string GetPauseHintText() { return "Pause [1]"; }
        private static string GetPlayHintText() { return "Play x1 [2]"; }

        internal static long MinuteFromDialForVerification(double dialHours)
        {
            double hourOfDay = dialHours % 24d;
            if (hourOfDay < 0d) hourOfDay += 24d;
            return (long)Math.Floor(hourOfDay * 60d);
        }

        internal static string FormatMinuteForVerification(long absoluteMinute)
        {
            const long MinutesPerDay = 24L * 60L;
            long minuteOfDay = absoluteMinute % MinutesPerDay;
            if (minuteOfDay < 0)
                minuteOfDay += MinutesPerDay;
            int hour = (int)(minuteOfDay / 60L);
            int minute = (int)(minuteOfDay % 60L);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}\n{2}",
                hour,
                minute,
                hour < 12 ? "AM" : "PM");
        }

        private sealed class DisplayMinuteState
        {
            internal BasicTooltipViewModel PauseHint;
            internal BasicTooltipViewModel PlayHint;
            internal BasicTooltipViewModel FastForwardHint;
        }
    }

    internal static class ApprovedCalendarBridge
    {
        private const string ApprovedMainSha256 =
            "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private static Type _settingsType;
        private static Type _dailyRateType;
        private static Type _timeMathType;
        private static Type _formatterType;
        private static Type _financeModelType;
        private static Type _settlementFoodModelType;
        private static Type _strategicMarkerType;
        private static PropertyInfo _extendedEnabled;
        private static PropertyInfo _annualEnabled;
        private static PropertyInfo _factor;
        private static PropertyInfo _campaignMultiplier;
        private static MethodInfo _format;
        private static MethodInfo _getDayOfYear;
        private static MethodInfo _getYear;
        private static MethodInfo _isLeapYear;
        private static FieldInfo _nativeFinance;
        private static FieldInfo _nativeSettlementFood;

        internal static Type FinanceModelType { get { return _financeModelType; } }
        internal static Type SettlementFoodModelType { get { return _settlementFoodModelType; } }
        internal static Type StrategicMarkerType { get { return _strategicMarkerType; } }

        internal static void Validate()
        {
            Assembly approved = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "AgesOfCalradia", StringComparison.Ordinal));
            if (approved == null)
                throw new InvalidOperationException("The approved AgesOfCalradia main DLL is not loaded.");
            using (SHA256 sha = SHA256.Create())
            using (System.IO.FileStream stream = System.IO.File.OpenRead(approved.Location))
            {
                string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                if (!string.Equals(actual, ApprovedMainSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Calendar fixes require approved main DLL " + ApprovedMainSha256
                        + "; loaded " + actual + ".");
            }

            _settingsType = RequireType(approved, "TwelveMonthCalendar.CalendarSettingsState");
            _dailyRateType = RequireType(approved, "TwelveMonthCalendar.DailyRateBalance");
            _timeMathType = RequireType(approved, "TwelveMonthCalendar.CalendarTimeMath");
            _formatterType = RequireType(approved, "TwelveMonthCalendar.CalendarFormatter");
            _financeModelType = RequireType(approved, "TwelveMonthCalendar.CalendarClanFinanceModel");
            _settlementFoodModelType = RequireType(approved, "TwelveMonthCalendar.CalendarSettlementFoodModel");
            _strategicMarkerType = RequireType(approved, "TwelveMonthCalendar.CalendarWorldStrategicMarkerVM");

            _extendedEnabled = RequireProperty(_settingsType, "ExtendedCalendarEnabled");
            _annualEnabled = RequireProperty(_settingsType, "AnnualRateBalanceEnabled");
            _factor = RequireProperty(_dailyRateType, "Factor");
            _campaignMultiplier = RequireProperty(_timeMathType, "CampaignTimeMultiplier");
            _format = _formatterType.GetMethod(
                "Format",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(CampaignTime) },
                null);
            if (_format == null)
                throw new MissingMethodException(_formatterType.FullName, "Format(CampaignTime)");
            _getDayOfYear = RequireStaticMethod(_timeMathType, "GetDayOfYear", typeof(CampaignTime));
            _getYear = RequireStaticMethod(_timeMathType, "GetYear", typeof(CampaignTime));
            _isLeapYear = RequireStaticMethod(_timeMathType, "IsLeapYear", typeof(int));
            _nativeFinance = _financeModelType.GetField(
                "_native",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_nativeFinance == null)
                throw new MissingFieldException(_financeModelType.FullName, "_native");
            _nativeSettlementFood = _settlementFoodModelType.GetField(
                "_native",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_nativeSettlementFood == null)
                throw new MissingFieldException(_settlementFoodModelType.FullName, "_native");
        }

        internal static bool ExtendedEnabled
        {
            get { return _extendedEnabled != null && (bool)_extendedEnabled.GetValue(null, null); }
        }

        internal static bool AnnualEnabled
        {
            get { return _annualEnabled != null && (bool)_annualEnabled.GetValue(null, null); }
        }

        internal static float Factor
        {
            get { return _factor == null ? 1f : (float)_factor.GetValue(null, null); }
        }

        internal static float CampaignMultiplier
        {
            get { return _campaignMultiplier == null ? 1f : (float)_campaignMultiplier.GetValue(null, null); }
        }

        internal static string Format(CampaignTime time)
        {
            return _format == null ? null : _format.Invoke(null, new object[] { time }) as string;
        }

        internal static int GetDayOfYear(CampaignTime time)
        {
            return (int)_getDayOfYear.Invoke(null, new object[] { time });
        }

        internal static int GetYear(CampaignTime time)
        {
            return (int)_getYear.Invoke(null, new object[] { time });
        }

        internal static bool IsLeapYear(int year)
        {
            return (bool)_isLeapYear.Invoke(null, new object[] { year });
        }

        internal static bool WrapsNativeFinance(object instance)
        {
            return instance != null
                && _nativeFinance != null
                && _nativeFinance.GetValue(instance) is DefaultClanFinanceModel;
        }

        internal static SettlementFoodModel GetNativeSettlementFood(object instance)
        {
            return instance == null || _nativeSettlementFood == null
                ? null
                : _nativeSettlementFood.GetValue(instance) as SettlementFoodModel;
        }

        internal static float ScaleDailyProbability(float probability)
        {
            if (!ExtendedEnabled) return probability;
            probability = Math.Max(0f, Math.Min(1f, probability));
            return 1f - (float)Math.Pow(1d - probability, Factor);
        }

        private static Type RequireType(Assembly assembly, string name)
        {
            Type type = assembly.GetType(name, false);
            if (type == null) throw new TypeLoadException(name);
            return type;
        }

        private static MethodInfo RequireStaticMethod(
            Type type, string name, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, parameters, null);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static PropertyInfo RequireProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null) throw new MissingMemberException(type.FullName, name);
            return property;
        }
    }

    internal static class CalendarFixTargets
    {
        internal static MethodInfo CampaignTickMapTime;
        internal static MethodInfo MapTimeWidgetUpdate;
        internal static MethodInfo MapTimeControlTick;
        internal static MethodInfo ButtonHandleClick;
        internal static MethodInfo WorkshopConversionSpeed;
        internal static MethodInfo TownFoodStocksChange;
        internal static MethodInfo VillageProduction;
        internal static MethodInfo SettlementDemand;
        internal static MethodInfo SettlementSupplyDemand;
        internal static MethodInfo RandomWarDecision;
        internal static ConstructorInfo StrategicMarkerConstructor;

        internal static void Validate()
        {
            CampaignTickMapTime = AccessTools.Method(typeof(Campaign), "TickMapTime", new[] { typeof(float) });
            Type mapTimeWidgetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Map.MapBar.MapCurrentTimeVisualWidget");
            MapTimeWidgetUpdate = mapTimeWidgetType == null
                ? null
                : AccessTools.Method(mapTimeWidgetType, "OnUpdate", new[] { typeof(float) });
            MapTimeControlTick = AccessTools.Method(typeof(MapTimeControlVM), "Tick", Type.EmptyTypes);
            ButtonHandleClick = AccessTools.Method(typeof(ButtonWidget), "HandleClick");
            WorkshopConversionSpeed = AccessTools.Method(
                typeof(DefaultWorkshopModel),
                "GetEffectiveConversionSpeedOfProduction");
            TownFoodStocksChange = AccessTools.Method(
                ApprovedCalendarBridge.SettlementFoodModelType,
                "CalculateTownFoodStocksChange");
            VillageProduction = AccessTools.Method(
                typeof(DefaultVillageProductionCalculatorModel),
                "CalculateDailyProductionAmount",
                new[] { typeof(Village), typeof(ItemObject) });
            SettlementDemand = AccessTools.Method(
                typeof(DefaultSettlementEconomyModel),
                "GetDailyDemandForCategory",
                new[] { typeof(Town), typeof(ItemCategory), typeof(int) });
            SettlementSupplyDemand = AccessTools.Method(
                typeof(DefaultSettlementEconomyModel),
                "GetSupplyDemandForCategory",
                new[]
                {
                    typeof(Town), typeof(ItemCategory), typeof(float),
                    typeof(float), typeof(float), typeof(float)
                });
            RandomWarDecision = AccessTools.Method(
                typeof(KingdomDecisionProposalBehavior),
                "GetRandomWarDecision");
            StrategicMarkerConstructor = ApprovedCalendarBridge.StrategicMarkerType
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault();

            if (CampaignTickMapTime == null) throw new MissingMethodException("Campaign.TickMapTime(float)");
            if (MapTimeWidgetUpdate == null)
                throw new MissingMethodException("MapCurrentTimeVisualWidget.OnUpdate(float)");
            if (MapTimeControlTick == null)
                throw new MissingMethodException("MapTimeControlVM.Tick()");
            if (ButtonHandleClick == null)
                throw new MissingMethodException("ButtonWidget.HandleClick()");
            if (WorkshopConversionSpeed == null)
                throw new MissingMethodException("DefaultWorkshopModel.GetEffectiveConversionSpeedOfProduction");
            if (TownFoodStocksChange == null)
                throw new MissingMethodException(
                    ApprovedCalendarBridge.SettlementFoodModelType.FullName,
                    "CalculateTownFoodStocksChange");
            if (VillageProduction == null)
                throw new MissingMethodException(
                    "DefaultVillageProductionCalculatorModel.CalculateDailyProductionAmount(Village, ItemObject)");
            if (SettlementDemand == null)
                throw new MissingMethodException(
                    "DefaultSettlementEconomyModel.GetDailyDemandForCategory(Town, ItemCategory, int)");
            if (SettlementSupplyDemand == null)
                throw new MissingMethodException(
                    "DefaultSettlementEconomyModel.GetSupplyDemandForCategory(Town, ItemCategory, float, float, float, float)");
            if (RandomWarDecision == null)
                throw new MissingMethodException("KingdomDecisionProposalBehavior.GetRandomWarDecision");
            if (StrategicMarkerConstructor == null)
                throw new MissingMethodException("CalendarWorldStrategicMarkerVM constructor");
            ParameterInfo[] markerParameters = StrategicMarkerConstructor.GetParameters();
            if (markerParameters.Length != 11
                || markerParameters[4].ParameterType != typeof(bool)
                || markerParameters[7].ParameterType != typeof(bool))
            {
                throw new InvalidOperationException("Approved strategic marker constructor no longer matches the verified contract.");
            }

            foreach (string method in new[] { "CalculateClanGoldChange", "CalculateClanIncome", "CalculateClanExpenses" })
            {
                if (AccessTools.Method(ApprovedCalendarBridge.FinanceModelType, method) == null)
                    throw new MissingMethodException(ApprovedCalendarBridge.FinanceModelType.FullName, method);
            }
        }
    }

    internal static class LegacyPatchControl
    {
        internal static void UnpatchDeclaringTypes(params string[] declaringTypeNames)
        {
            HashSet<string> targets = new HashSet<string>(declaringTypeNames, StringComparer.Ordinal);
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToArray())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null) continue;
                foreach (Patch patch in info.Prefixes
                    .Concat(info.Postfixes)
                    .Concat(info.Transpilers)
                    .Concat(info.Finalizers)
                    .ToArray())
                {
                    MethodInfo patchMethod = patch.PatchMethod;
                    if (patchMethod != null
                        && patchMethod.DeclaringType != null
                        && targets.Contains(patchMethod.DeclaringType.FullName))
                    {
                        new Harmony(Approved560CalendarFixesSubModule.HarmonyId)
                            .Unpatch(original, patchMethod);
                    }
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class CampaignSimulationTimeFix
    {
        // Normal play targets 80 real seconds per campaign day.
        // The 2x and 4x controls therefore target
        // 40 and 20 seconds per day without enlarging
        // Bannerlord's campaign-time quantum or skipping AI work.
        // The sidecar owns the value because the approved Core DLL remains
        // byte-for-byte immutable.
        // Native TickMapTime advances 1080 game seconds per input second at 1x:
        // 86400 / 1080 = 80 real seconds/day before this factor.
        internal const float WarbandCampaignTimeScale = 80f / 80f;
        internal const float WarbandFastForwardMultiplier = 2f;
        internal const float WarbandFourTimesFastForwardMultiplier = 4f;
        private static float _selectedFastForwardMultiplier = WarbandFastForwardMultiplier;

        private static MethodBase TargetMethod() { return CalendarFixTargets.CampaignTickMapTime; }

        private static void Prefix(Campaign __instance, ref float realDt)
        {
            float incomingRealDt = realDt;
            if (ApprovedCalendarBridge.ExtendedEnabled)
            {
                bool fastForward = __instance != null && IsFastForward(__instance.TimeControlMode);
                realDt = ScaleRealDeltaForVerification(realDt, WarbandCampaignTimeScale);
                if (fastForward)
                    __instance.SpeedUpMultiplier = _selectedFastForwardMultiplier;
            }
            AocPacingDiagnostics.Observe(__instance, incomingRealDt);
        }

        internal static float ScaleRealDeltaForVerification(float realDt, float campaignMultiplier)
        {
            return realDt * campaignMultiplier;
        }

        internal static bool IsFastForward(CampaignTimeControlMode mode)
        {
            return mode == CampaignTimeControlMode.UnstoppableFastForward
                || mode == CampaignTimeControlMode.UnstoppableFastForwardForPartyWaitTime
                || mode == CampaignTimeControlMode.StoppableFastForward;
        }

        internal static float SelectFastForwardMultiplierForVerification(int requestedSpeed)
        {
            return requestedSpeed == 4
                ? WarbandFourTimesFastForwardMultiplier
                : WarbandFastForwardMultiplier;
        }

        internal static void SelectFastForwardMultiplier(int requestedSpeed)
        {
            _selectedFastForwardMultiplier = SelectFastForwardMultiplierForVerification(requestedSpeed);
        }

        internal static bool IsFourTimesSelected
        {
            get { return Math.Abs(_selectedFastForwardMultiplier - WarbandFourTimesFastForwardMultiplier) < 0.001f; }
        }
    }

    // Native target: MapCurrentTimeVisualWidget.OnUpdate(float), Bannerlord 1.4.8.
    // Purpose: use the native 2x transition for the added button, then explicitly set
    // AOC's selected fast-forward multiplier to 4x. This mirrors Better Time's UI
    // ownership pattern without requiring Better Time at runtime.
    // Compatibility/failure: the target is resolved during startup validation. If the
    // native widget changes, the sidecar unloads safely and the map bar stays native.
    // A button is registered once per widget instance and cleared on module unload.
    // Verify-Approved560CalendarFixes.ps1 checks the target, XML command, and handler.
    [HarmonyPatch]
    internal static class MapTimeControlFourTimesButtonPatch
    {
        private static readonly Dictionary<ButtonWidget, RegisteredTimeButtons> RegisteredButtonSets =
            new Dictionary<ButtonWidget, RegisteredTimeButtons>();
        private static bool _keyActivationFailureLogged;

        private static MethodBase TargetMethod()
        {
            return CalendarFixTargets.MapTimeWidgetUpdate;
        }

        private static void Postfix(object __instance)
        {
            Widget mapTimeWidget = __instance as Widget;
            if (mapTimeWidget == null)
                return;

            ButtonWidget fourTimesButton = mapTimeWidget.FindChild("FastForward4xButton", true) as ButtonWidget;
            ButtonWidget twoTimesButton = mapTimeWidget.FindChild("FastForwardButton", true) as ButtonWidget;
            ButtonWidget playButton = mapTimeWidget.FindChild("PlayButton", true) as ButtonWidget;
            ButtonWidget pauseButton = mapTimeWidget.FindChild("PauseButton", true) as ButtonWidget;
            if (fourTimesButton == null || twoTimesButton == null
                || playButton == null || pauseButton == null)
                return;

            RegisteredTimeButtons buttons;
            if (!RegisteredButtonSets.TryGetValue(fourTimesButton, out buttons))
            {
                buttons = new RegisteredTimeButtons(
                    pauseButton,
                    playButton,
                    twoTimesButton,
                    fourTimesButton);
                RegisteredButtonSets.Add(
                    fourTimesButton,
                    buttons);
                twoTimesButton.ClickEventHandlers.Add(OnTwoTimesClicked);
                fourTimesButton.ClickEventHandlers.Add(OnFourTimesClicked);
            }

            if (fourTimesButton.IsVisible
                && fourTimesButton.IsEnabled
                && Input.IsKeyPressed(InputKey.D4))
            {
                ActivateFourTimesButtonFromKey(fourTimesButton);
            }

            bool fourTimesSelected = Campaign.Current != null
                && CampaignSimulationTimeFix.IsFastForward(Campaign.Current.TimeControlMode)
                && CampaignSimulationTimeFix.IsFourTimesSelected;
            fourTimesButton.IsSelected = fourTimesSelected;
            if (fourTimesSelected)
                twoTimesButton.IsSelected = false;
        }

        private static void OnTwoTimesClicked(Widget widget)
        {
            CampaignSimulationTimeFix.SelectFastForwardMultiplier(2);
        }

        private static void OnFourTimesClicked(Widget widget)
        {
            if (Campaign.Current == null)
                return;

            CampaignSimulationTimeFix.SelectFastForwardMultiplier(4);
            Campaign.Current.SpeedUpMultiplier =
                CampaignSimulationTimeFix.WarbandFourTimesFastForwardMultiplier;
        }

        private static void ActivateFourTimesButtonFromKey(ButtonWidget fourTimesButton)
        {
            try
            {
                CalendarFixTargets.ButtonHandleClick.Invoke(fourTimesButton, null);
            }
            catch (TargetInvocationException exception)
            {
                LogKeyActivationFailure(exception);
            }
            catch (MethodAccessException exception)
            {
                LogKeyActivationFailure(exception);
            }
        }

        private static void LogKeyActivationFailure(Exception exception)
        {
            if (_keyActivationFailureLogged)
                return;

            _keyActivationFailureLogged = true;
            System.Diagnostics.Trace.WriteLine(
                "AOC Super Fast Forward [4] key activation failed safely: " + exception);
        }

        internal static string GetFastForwardHintText()
        {
            foreach (RegisteredTimeButtons buttons in RegisteredButtonSets.Values)
            {
                if (buttons.FourTimes.IsHovered)
                    return "Super Fast Forward x4 [4]";
            }
            return "Fast Forward x2 [3]";
        }

        internal static void ResetRegisteredButtons()
        {
            foreach (RegisteredTimeButtons buttons in RegisteredButtonSets.Values)
            {
                buttons.TwoTimes.ClickEventHandlers.Remove(OnTwoTimesClicked);
                buttons.FourTimes.ClickEventHandlers.Remove(OnFourTimesClicked);
            }
            RegisteredButtonSets.Clear();
            _keyActivationFailureLogged = false;
        }

        private sealed class RegisteredTimeButtons
        {
            internal RegisteredTimeButtons(
                ButtonWidget pause,
                ButtonWidget play,
                ButtonWidget twoTimes,
                ButtonWidget fourTimes)
            {
                Pause = pause;
                Play = play;
                TwoTimes = twoTimes;
                FourTimes = fourTimes;
            }

            internal ButtonWidget Pause { get; private set; }
            internal ButtonWidget Play { get; private set; }
            internal ButtonWidget TwoTimes { get; private set; }
            internal ButtonWidget FourTimes { get; private set; }
        }
    }
    internal static class WorkshopProductionFix
    {
        internal static float ScaleBaseSpeedForVerification(
            float nativeBaseSpeed,
            bool producesFood,
            float factor)
        {
            return producesFood ? nativeBaseSpeed : nativeBaseSpeed * factor;
        }
    }

    internal static class VanillaFoodCadence
    {
        internal static bool IsFood(ItemCategory category)
        {
            return category != null
                && category.Properties == ItemCategory.Property.BonusToFoodStores;
        }

        internal static float ScaleDemandForVerification(float nativeDemand, bool isFood, float factor)
        {
            return isFood ? nativeDemand : nativeDemand * factor;
        }

        internal static float ScaleFinalForVerification(float nativeResult, float factor)
        {
            return nativeResult * factor;
        }

        internal static void ScaleFinal(ref ExplainedNumber value)
        {
            float factor = ApprovedCalendarBridge.Factor;
            float scaledResult = value.ResultNumber * factor;
            if (!value.IncludeDescriptions)
            {
                value = new ExplainedNumber(scaledResult, false, null);
                return;
            }

            ExplainedNumber scaled = new ExplainedNumber(0f, true, null);
            foreach (var line in value.GetLines())
                scaled.Add(line.number * factor, new TextObject("{=!}" + line.name));
            if (scaled.GetLines().Count == 0 && Math.Abs(scaledResult) > 0.0001f)
                scaled.Add(scaledResult, new TextObject("{=AoCCalendarCadence}Calendar cadence"));
            value = scaled;
        }
    }

    // Native 1.4.8 CalculateDailyProductionAmount(Village, ItemObject): retain
    // native food and its four audited supply inputs. Only the existing annual
    // adjustment changes; native eligibility, bonuses and discrete stock updates
    // remain native. Legacy duplicate patches are retired at startup. Unknown
    // categories retain the previous policy. Verify-VillageInputSupply covers
    // the allowlist, annual-off, zero output and industrial scaling contracts.
    [HarmonyPatch(typeof(DefaultVillageProductionCalculatorModel), "CalculateDailyProductionAmount")]
    internal static class FoodAwareVillageProductionFix
    {
        private static void Postfix(ItemObject item, ref ExplainedNumber __result)
        {
            if (item != null) ApplyPolicy(item.ItemCategory, ApprovedCalendarBridge.AnnualEnabled, ref __result);
        }

        internal static void ApplyPolicy(ItemCategory category, bool annualEnabled, ref ExplainedNumber result)
        {
            if (annualEnabled && !UsesNativeCadence(category)) VanillaFoodCadence.ScaleFinal(ref result);
        }

        internal static bool UsesNativeCadence(ItemCategory category)
        {
            if (VanillaFoodCadence.IsFood(category)) return true;
            if (category == null) return false;
            // Exact audited category IDs; do not exempt all animals or all
            // materials merely because one consumer happens to make food.
            switch (category.StringId)
            {
                case "cow":
                case "sheep":
                case "hog":
                case "wool":
                    return true;
                default:
                    return false;
            }
        }
    }

    // Native target: DefaultSettlementEconomyModel.GetDailyDemandForCategory.
    // Food demand stays vanilla; non-food demand keeps one annual conversion.
    // The legacy budget postfix is retired because Bannerlord derives budget
    // directly from this demand and scaling the budget again was factor^2.
    [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetDailyDemandForCategory")]
    internal static class FoodAwareSettlementDemandFix
    {
        private static void Postfix(ItemCategory category, ref float __result)
        {
            if (ApprovedCalendarBridge.AnnualEnabled && !VanillaFoodCadence.IsFood(category))
                __result *= ApprovedCalendarBridge.Factor;
        }
    }

    // Native target: DefaultSettlementEconomyModel.GetSupplyDemandForCategory.
    // Food prices retain native smoothing because their production and demand
    // both run at native cadence. Non-food categories retain Gregorian annual
    // smoothing. If the signature changes, startup validation disables the
    // whole sidecar before any legacy patch is removed.
    [HarmonyPatch(typeof(DefaultSettlementEconomyModel), "GetSupplyDemandForCategory")]
    internal static class FoodAwareMarketSmoothingFix
    {
        private static void Postfix(
            ItemCategory category,
            float dailySupply,
            float dailyDemand,
            float oldSupply,
            float oldDemand,
            ref ValueTuple<float, float> __result)
        {
            if (!ApprovedCalendarBridge.AnnualEnabled || VanillaFoodCadence.IsFood(category))
                return;

            const float nativeDailySmoothing = 0.15f;
            float factor = ApprovedCalendarBridge.Factor;
            float smoothing = 1f - (float)Math.Pow(1f - nativeDailySmoothing, factor);
            float supply = Math.Max(0.1f, oldSupply * (1f - smoothing) + dailySupply * smoothing);
            float demand = oldDemand * (1f - smoothing) + dailyDemand * smoothing;
            __result = new ValueTuple<float, float>(supply, demand);
        }
    }

    // Native target: the approved main DLL's
    // CalendarSettlementFoodModel.CalculateTownFoodStocksChange wrapper.
    // Purpose: run the complete food calculation with native daily values,
    // then annualize only its final surplus or deficit. The prefix
    // is hash-locked to 560F1B51 and falls back to the approved implementation
    // when its native model cannot be resolved. Verify-Approved560CalendarFixes
    // checks target registration and the final-result scaling contract.
    [HarmonyPatch]
    internal static class TownMarketFoodAccountingFix
    {
        private static MethodBase TargetMethod()
        {
            return CalendarFixTargets.TownFoodStocksChange;
        }

        private static bool Prefix(
            object __instance,
            Town town,
            bool includeMarketStocks,
            bool includeDescriptions,
            ref ExplainedNumber __result)
        {
            if (!ApprovedCalendarBridge.AnnualEnabled)
            {
                return true;
            }

            SettlementFoodModel native = ApprovedCalendarBridge.GetNativeSettlementFood(__instance);
            if (native == null || town == null)
            {
                return true;
            }

            ExplainedNumber nativeResult = native.CalculateTownFoodStocksChange(
                town,
                includeMarketStocks: includeMarketStocks,
                includeDescriptions: includeDescriptions);
            VanillaFoodCadence.ScaleFinal(ref nativeResult);
            __result = nativeResult;
            return false;
        }

        internal static float CombineForVerification(
            float nativeDirect,
            float nativeWithMarket,
            bool includeMarketStocks,
            float factor)
        {
            float selected = includeMarketStocks ? nativeWithMarket : nativeDirect;
            return VanillaFoodCadence.ScaleFinalForVerification(selected, factor);
        }
    }

    [HarmonyPatch(typeof(DefaultTournamentModel), "GetTournamentStartChance")]
    internal static class TournamentStartFix
    {
        private static readonly MethodInfo WeekGetter = AccessTools.PropertyGetter(
            typeof(CampaignTime), "GetWeekOfSeason");
        private static readonly MethodInfo Normalize = AccessTools.Method(
            typeof(TournamentStartFix), nameof(NormalizeWeekSlot));

        internal static int NormalizeWeekSlot(int week)
        {
            int slot = week % 3;
            return slot < 0 ? slot + 3 : slot;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool replaced = false;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (!replaced && WeekGetter != null && instruction.Calls(WeekGetter))
                {
                    yield return new CodeInstruction(OpCodes.Call, Normalize);
                    replaced = true;
                }
            }
            if (!replaced)
                throw new InvalidOperationException("Tournament week gate was not found.");
        }

        private static void Postfix(ref float __result)
        {
            if (ApprovedCalendarBridge.ExtendedEnabled && __result > 0f)
                __result = ApprovedCalendarBridge.ScaleDailyProbability(__result);
        }
    }

    [HarmonyPatch(typeof(DefaultTournamentModel), "GetTournamentEndChance")]
    internal static class TournamentEndFix
    {
        private static bool Prefix(TournamentGame tournament, ref float __result)
        {
            if (!ApprovedCalendarBridge.ExtendedEnabled) return true;
            if (tournament == null)
            {
                __result = 0f;
                return false;
            }
            float nativeElapsed = tournament.CreationTime.ElapsedDaysUntilNow * ApprovedCalendarBridge.Factor;
            float nativeChance = Math.Max(0f, (nativeElapsed - 10f) * 0.05f);
            __result = ApprovedCalendarBridge.ScaleDailyProbability(Math.Min(1f, nativeChance));
            return false;
        }
    }

    [HarmonyPatch(typeof(CampaignSceneNotificationHelper), "GetFormalDayAndSeasonText")]
    internal static class SceneNotificationDateFix
    {
        private static bool Prefix(CampaignTime time, ref TextObject __result)
        {
            if (!ApprovedCalendarBridge.ExtendedEnabled) return true;
            string date = ApprovedCalendarBridge.Format(time);
            if (string.IsNullOrWhiteSpace(date)) return true;
            TextObject result = new TextObject("{=AoCFormalCalendarDate}{DATE}");
            result.SetTextVariable("DATE", date);
            __result = result;
            return false;
        }
    }

    internal static class WageText
    {
        internal static string Format(int nativeDailyWage)
        {
            if (!ApprovedCalendarBridge.AnnualEnabled)
                return nativeDailyWage.ToString(CultureInfo.CurrentCulture);
            return EffectiveDailyWageForVerification(
                    nativeDailyWage,
                    ApprovedCalendarBridge.Factor)
                .ToString("0.##", CultureInfo.CurrentCulture) + "/day";
        }

        internal static float EffectiveDailyWageForVerification(
            float nativeDailyWage,
            float factor)
        {
            return nativeDailyWage * factor;
        }
    }

    [HarmonyPatch(typeof(PartyVM), "RefreshTopInformation")]
    internal static class PartyTotalWageFix
    {
        private static void Postfix(PartyVM __instance)
        {
            if (__instance != null && MobileParty.MainParty != null)
                __instance.MainPartyTotalWeeklyCostLbl = WageText.Format(MobileParty.MainParty.TotalWage);
        }
    }

    [HarmonyPatch(typeof(PartyVM), "RefreshCurrentCharacterInformation")]
    internal static class PartyCharacterWageFix
    {
        private static void Postfix(PartyVM __instance)
        {
            if (__instance != null
                && __instance.CurrentCharacter != null
                && __instance.CurrentCharacter.Character != null
                && __instance.IsCurrentCharacterWageEnabled)
            {
                __instance.CurrentCharacterWageLbl = WageText.Format(
                    __instance.CurrentCharacter.Character.TroopWage);
            }
        }
    }

    [HarmonyPatch(typeof(PartyVM), nameof(PartyVM.RefreshValues))]
    internal static class PartyWageHintFix
    {
        private static void Postfix(PartyVM __instance)
        {
            if (__instance == null || !ApprovedCalendarBridge.AnnualEnabled) return;
            TextObject hint = new TextObject(
                "{=AoCEffectiveCalendarWage}Effective wage per Gregorian calendar day. Native troop rates and all native wage modifiers are annualized once when clan finance is applied.");
            if (__instance.TotalWageHint != null) __instance.TotalWageHint.HintText = hint;
            if (__instance.WageHint != null) __instance.WageHint.HintText = hint;
        }
    }

    [HarmonyPatch]
    internal static class FinanceDoubleScaleFix
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string method in new[] { "CalculateClanGoldChange", "CalculateClanIncome", "CalculateClanExpenses" })
                yield return AccessTools.Method(ApprovedCalendarBridge.FinanceModelType, method);
        }

        private static void Postfix(object __instance, ref ExplainedNumber __result)
        {
            if (!ApprovedCalendarBridge.AnnualEnabled
                || !ApprovedCalendarBridge.WrapsNativeFinance(__instance)) return;
            float factor = ApprovedCalendarBridge.Factor;
            if (factor <= 0f || Math.Abs(factor - 1f) < 0.000001f) return;

            float correctedResult = CorrectDoubleScaledForVerification(
                __result.ResultNumber,
                factor);
            if (!__result.IncludeDescriptions)
            {
                __result = new ExplainedNumber(correctedResult, false, null);
                return;
            }

            ExplainedNumber corrected = new ExplainedNumber(0f, true, null);
            foreach (var line in __result.GetLines())
                corrected.Add(line.number / factor, new TextObject("{=!}" + line.name));
            if (corrected.GetLines().Count == 0 && Math.Abs(correctedResult) > 0.0001f)
                corrected.Add(correctedResult, new TextObject("{=AoCCalendarCadence}Calendar cadence"));
            __result = corrected;
        }

        internal static float CorrectDoubleScaledForVerification(
            float doubleScaledResult,
            float factor)
        {
            return doubleScaledResult / factor;
        }
    }

    [HarmonyPatch]
    internal static class WarCooldownFix
    {
        private const float GregorianTruceDays = 87f;
        private static MethodBase TargetMethod() { return CalendarFixTargets.RandomWarDecision; }

        private static void Postfix(Clan clan, ref KingdomDecision __result)
        {
            if (!ApprovedCalendarBridge.ExtendedEnabled || __result == null || clan == null || clan.Kingdom == null)
                return;
            DeclareWarDecision war = __result as DeclareWarDecision;
            if (war == null || war.FactionToDeclareWarOn == null) return;
            StanceLink stance = clan.Kingdom.GetStanceWith(war.FactionToDeclareWarOn);
            if (IsWithinTruceForVerification(
                stance.PeaceDeclarationDate.ElapsedDaysUntilNow))
                __result = null;
        }

        internal static bool IsWithinTruceForVerification(float elapsedCalendarDays)
        {
            return elapsedCalendarDays <= GregorianTruceDays;
        }
    }

    [HarmonyPatch]
    internal static class StrategicUiCityLabelFix
    {
        private static MethodBase TargetMethod()
        {
            return CalendarFixTargets.StrategicMarkerConstructor;
        }

        private static void Prefix(object[] __args)
        {
            // Constructor arguments 4 and 7 are isTown and showLabel in the
            // approved 560F1B51 build. Keep town labels enabled at every zoom;
            // the constructor still suppresses labels for castles.
            if (__args != null
                && __args.Length == 11
                && __args[4] is bool
                && (bool)__args[4])
            {
                __args[7] = true;
            }
        }
    }

    /// <summary>
    /// Runtime widget required by the reviewed UI REDESIGN prefab. The test
    /// build originally supplied it from the alignment-diagnostics assembly;
    /// keeping the focused widget here makes release scrolling functional
    /// without shipping diagnostics or changing the approved main DLL.
    /// </summary>
    public sealed class WorldEventsRowSnapScrollablePanel : ScrollablePanel
    {
        private float _rowStride = 1f;
        private float _wheelTarget;
        private bool _hasWheelTarget;
        private bool _resetOnShow;
        private bool _wasVisible;
        private float _previousInnerHeight;

        public WorldEventsRowSnapScrollablePanel(UIContext context) : base(context) { }

        [Editor(false)]
        public float RowStride
        {
            get { return _rowStride; }
            set { _rowStride = Math.Max(1f, value); }
        }

        [Editor(false)]
        public bool ResetOnShow
        {
            get { return _resetOnShow; }
            set { _resetOnShow = value; }
        }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);

            float innerHeight = InnerPanel == null ? 0f : InnerPanel.Size.Y;
            bool contentBecameReady = _previousInnerHeight <= 0.5f && innerHeight > 0.5f;
            if (_resetOnShow && IsVisible && (!_wasVisible || contentBecameReady))
            {
                float minimum = VerticalScrollbar == null ? 0f : VerticalScrollbar.MinValue;
                if (VerticalScrollbar != null) VerticalScrollbar.SetValueForced(minimum);
                if (InnerPanel != null) InnerPanel.ScaledPositionYOffset = -minimum;
                _wheelTarget = minimum;
                _hasWheelTarget = true;
            }

            _wasVisible = IsVisible;
            _previousInnerHeight = innerHeight;
        }

        protected override bool OnPreviewMouseScroll()
        {
            return true;
        }

        protected override void OnMouseScroll()
        {
            if (VerticalScrollbar == null || EventManager.DeltaMouseScroll == 0f)
                return;

            float current = VerticalScrollbar.ValueFloat;
            if (!_hasWheelTarget || Math.Abs(current - _wheelTarget) > _rowStride)
                _wheelTarget = (float)Math.Round(current / _rowStride) * _rowStride;

            float direction = EventManager.DeltaMouseScroll < 0f ? 1f : -1f;
            _wheelTarget = Math.Max(
                VerticalScrollbar.MinValue,
                Math.Min(VerticalScrollbar.MaxValue, _wheelTarget + direction * _rowStride));
            _hasWheelTarget = true;
            SetVerticalScrollTarget(_wheelTarget, 0.10f);
        }
    }
}
