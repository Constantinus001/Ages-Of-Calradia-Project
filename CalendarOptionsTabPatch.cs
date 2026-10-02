using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine.Options;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade.Options;
using TaleWorlds.MountAndBlade.ViewModelCollection.GameOptions;

namespace TwelveMonthCalendar
{
    /// <summary>
    /// Adds a real Calendar page to Bannerlord's native Settings screen.
    /// The native Options movie is created with OptionsVM, so its data source
    /// is replaced with a proxy that exposes one additional category without
    /// requiring MCM. The hook is deliberately restricted to that one movie
    /// and the unmodified native OptionsVM type.
    /// </summary>
    [HarmonyPatch]
    internal static class CalendarOptionsMoviePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            MethodBase target = AccessTools.Constructor(
                typeof(GauntletMovie),
                new[]
                {
                    typeof(string),
                    typeof(UIContext),
                    typeof(WidgetFactory),
                    typeof(IViewModel),
                    typeof(bool)
                });

            if (target == null)
            {
                Diagnostics.Info(
                    "Native Options movie constructor was not found; the optional in-game Calendar settings tab is disabled.");
                return new MethodBase[0];
            }

            return new[] { target };
        }

        [HarmonyPrefix]
        private static void Prefix(
            [HarmonyArgument(0)] ref string movieName,
            [HarmonyArgument(3)] ref IViewModel viewModel)
        {
            if (!string.Equals(movieName, "Options", StringComparison.Ordinal))
            {
                return;
            }

            // MCM owns the settings screen when its calendar page registered.
            // Do not wrap MCM's OptionsVM: doing so removes the data source its
            // Mods tab needs and leaves that tab blank.
            if (OptionalMcmIntegration.IsSettingsRegistered)
            {
                Diagnostics.Info(
                    "Native Calendar Options tab hidden because the MCM settings page is active.");
                return;
            }

            OptionsVM options = viewModel as OptionsVM;
            if (options == null
                || options is CalendarOptionsVM
                || options.GetType() != typeof(OptionsVM))
            {
                return;
            }

            if (!CalendarOptionsVM.IsNativeLayoutSupported())
            {
                Diagnostics.Info("Native Calendar Settings tab was skipped because this Bannerlord OptionsVM layout is incompatible; MCM/XML settings remain available.");
                return;
            }

            try
            {
                Diagnostics.Info("Native Settings OptionsVM detected; creating the Calendar category.");
                viewModel = new CalendarOptionsVM(options);
                movieName = "CalendarOptions";
                Diagnostics.Info("Native Settings screen switched to the Calendar fallback layout.");
            }
            catch (Exception exception)
            {
                Diagnostics.Error("Native Calendar Settings tab could not be created; original settings remain active.", exception);
            }
        }
    }

    [HarmonyPatch(typeof(GenericOptionDataVM), "RefreshValues")]
    internal static class CalendarOptionLabelRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GenericOptionDataVM __instance)
        {
            CalendarOptionDataBase calendarOption = __instance.GetOptionData() as CalendarOptionDataBase;
            if (calendarOption != null)
            {
                __instance.Name = calendarOption.Name;
                __instance.Description = calendarOption.Description;
            }

            ActionOptionDataVM actionOption = __instance as ActionOptionDataVM;
            CalendarActionOptionData calendarAction = __instance.GetOptionData() as CalendarActionOptionData;
            if (actionOption != null && calendarAction != null)
            {
                actionOption.Name = calendarAction.DisplayName;
                actionOption.Description = calendarAction.Description;
                actionOption.ActionName = calendarAction.DisplayActionName;
            }
        }
    }

    // The native GauntletOptionsScreen retains its original OptionsVM and owns
    // the active-state registration used to close the screen. The Calendar VM
    // is only the movie's data source, so it must never close the screen itself.
    [HarmonyPatch(typeof(OptionsVM), nameof(OptionsVM.ExecuteCancel))]
    internal static class CalendarOptionsCancelPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(OptionsVM __instance)
        {
            CalendarOptionsVM calendarOptions = __instance as CalendarOptionsVM;
            return calendarOptions == null || !calendarOptions.TryCloseThroughNativeOptions(false);
        }
    }

    [HarmonyPatch(typeof(OptionsVM), nameof(OptionsVM.ExecuteDone))]
    internal static class CalendarOptionsDonePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(OptionsVM __instance)
        {
            CalendarOptionsVM calendarOptions = __instance as CalendarOptionsVM;
            return calendarOptions == null || !calendarOptions.TryCloseThroughNativeOptions(true);
        }
    }

    // Reuse Bannerlord's visible per-category Reset to Defaults button for the
    // Calendar page. The generic reset would only restore values captured when
    // the page was opened; this invokes the mod's real persisted defaults.
    [HarmonyPatch(typeof(GroupedOptionCategoryVM), nameof(GroupedOptionCategoryVM.ExecuteResetToDefault))]
    internal static class CalendarOptionsTopResetPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(GroupedOptionCategoryVM __instance)
        {
            if (!CalendarOptionsVM.IsCalendarOptionsCategory(__instance))
            {
                return true;
            }

            CalendarOptionsVM.ShowTopResetConfirmation(__instance);
            return false;
        }
    }

    internal sealed class CalendarOptionsVM : OptionsVM
    {
        private static readonly string[] RequiredNativeListFields =
        {
            "_categories",
            "_groupedCategories"
        };
        private static bool? _nativeLayoutSupported;
        private readonly OptionsVM _nativeOptions;
        private readonly GroupedOptionCategoryVM _calendarOptions;

        [DataSourceProperty]
        public GroupedOptionCategoryVM CalendarOptions
        {
            get { return _calendarOptions; }
        }

        internal CalendarOptionsVM(OptionsVM source)
            : base(
                source.CurrentOptionsMode,
                delegate { },
                delegate(KeyOptionVM key) { },
                delegate { },
                delegate { })
        {
            _nativeOptions = source;
            CopyNativeState(source);
            DetachProxyGamepadHandler();

            // This VM is created only when MCM is absent or its adapter could
            // not register. Keep the fallback category guarded as a second
            // line of protection against duplicate active settings pages.
            bool nativeCalendarSettingsEnabled =
                !OptionalMcmIntegration.IsSettingsRegistered;

            List<IOptionData> calendarOptions = new List<IOptionData>
            {
                new CalendarActionOptionData(
                    "Calendar Month Names",
                    "Edit Month Names",
                    "Edit all twelve month names in one pipe-separated text field. Example: January|February|...|December.",
                    ShowMonthNamesEditor),
                new CalendarActionOptionData(
                    "Calendar Season Names",
                    "Edit Season Names",
                    "Edit all four season names in one pipe-separated text field. Example: Spring|Summer|Autumn|Winter.",
                    ShowSeasonNamesEditor),
                new CalendarActionOptionData(
                    "Calendar Month Lengths",
                    "Edit Month Lengths",
                    "Edit twelve pipe-separated month lengths that total exactly 365. This is locked after a campaign session starts.",
                    ShowMonthLengthsEditor),
                new CalendarActionOptionData("Reset Calendar", "Reset Category", "Restores Calendar defaults. In an active campaign, only custom month and season names can be reset safely.", ResetCalendarCategory)
            };
            List<IOptionData> displayOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Show Day Label",
                    delegate { return CalendarSettingsState.ShowDayLabel; },
                    delegate(bool value) { Apply(showDayLabel: value); }),
                new CalendarBooleanOptionData(
                    "Show Year Label",
                    delegate { return CalendarSettingsState.ShowYearLabel; },
                    delegate(bool value) { Apply(showYearLabel: value); }),
                new CalendarBooleanOptionData(
                    "Use Ordinal Day Suffixes",
                    delegate { return CalendarSettingsState.UseOrdinalDaySuffixes; },
                    delegate(bool value) { Apply(useOrdinalDaySuffixes: value); }),
                new CalendarSelectionOptionData(
                    "Clock Format",
                    new[] { "24-Hour", "12-Hour" },
                    GetClockFormatIndex,
                    delegate(float value) { Apply(use24HourClock: ClampIndex(value, 2) == 0); }),
                new CalendarSelectionOptionData(
                    "Date Format",
                    new[] { "Day-Month-Year", "Month-Day-Year", "Year-Month-Day" },
                    GetDateFormatIndex,
                    delegate(float value) { Apply(dateFormat: GetDateFormats()[ClampIndex(value, GetDateFormats().Length)]); }),
                new CalendarActionOptionData("Reset Display", "Reset Category", "Restores the Display category defaults.", ResetDisplayCategory)
            };
            List<IOptionData> pacingOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Automatic Campaign Time Scale",
                    delegate { return CalendarSettingsState.AutoCampaignTimeScale; },
                    delegate(bool value) { Apply(autoCampaignTimeScale: value); }),
                new CalendarNumericOptionData(
                    "Campaign Time Scale",
                    CalendarSettingsState.MinimumCampaignTimeScale,
                    CalendarSettingsState.MaximumCampaignTimeScale,
                    false,
                    0,
                    delegate { return CalendarSettingsState.CampaignTimeScale; },
                    delegate(float value) { Apply(campaignTimeScale: value, autoCampaignTimeScale: false); }),
                new CalendarNumericOptionData(
                    "Fast-Forward Speed Multiplier",
                    CalendarSettingsState.MinimumPacingMultiplier,
                    CalendarSettingsState.MaximumPacingMultiplier,
                    true,
                    1,
                    delegate { return CalendarSettingsState.FastForwardTimeMultiplier; },
                    delegate(float value) { Apply(fastForwardTimeMultiplier: value); }),
                new CalendarActionOptionData("Reset Pacing", "Reset Category", "Restores campaign pacing: about three hours normally and ninety minutes fast-forward per year.", ResetPacingCategory)
            };
            List<IOptionData> lightingOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Synchronize Campaign Lighting",
                    delegate { return CalendarSettingsState.ClockSynchronizedLighting; },
                    delegate(bool value) { Apply(clockSynchronizedLighting: value); }),
                new CalendarNumericOptionData(
                    "Visual Sunrise Hour",
                    0f,
                    23.75f,
                    false,
                    2,
                    delegate { return CalendarSettingsState.VisualSunriseHour; },
                    delegate(float value) { Apply(visualSunriseHour: value); }),
                new CalendarNumericOptionData(
                    "Visual Sunset Hour",
                    0f,
                    23.75f,
                    false,
                    2,
                    delegate { return CalendarSettingsState.VisualSunsetHour; },
                    delegate(float value) { Apply(visualSunsetHour: value); }),
                new CalendarNumericOptionData(
                    "Lighting Transition Hours",
                    0.25f,
                    4f,
                    false,
                    2,
                    delegate { return CalendarSettingsState.VisualLightingTransitionHours; },
                    delegate(float value) { Apply(visualLightingTransitionHours: value); }),
                new CalendarActionOptionData("Reset Lighting", "Reset Category", "Restores clock-synchronized lighting, the default sunrise and sunset, and two-hour transitions.", ResetLightingCategory)
            };
            List<IOptionData> lifeCycleOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Use Calendar-Month Pregnancy",
                    delegate { return CalendarSettingsState.UseCalendarMonthPregnancy; },
                    delegate(bool value) { Apply(useCalendarMonthPregnancy: value); }),
                new CalendarNumericOptionData(
                    "Pregnancy Duration (Months)",
                    1f,
                    24f,
                    true,
                    1,
                    delegate { return CalendarSettingsState.PregnancyDurationMonths; },
                    delegate(float value) { Apply(pregnancyDurationMonths: Math.Max(1, (int)value)); }),
                new CalendarNumericOptionData(
                    "Lord Death Rate Multiplier",
                    0f,
                    1f,
                    false,
                    0,
                    delegate { return CalendarSettingsState.LordDeathRateMultiplier; },
                    delegate(float value) { Apply(lordDeathRateMultiplier: value); }),
                new CalendarNumericOptionData(
                    "Renown Gain Multiplier",
                    0f,
                    1f,
                    false,
                    0,
                    delegate { return CalendarSettingsState.RenownGainMultiplier; },
                    delegate(float value) { Apply(renownGainMultiplier: value); }),
                new CalendarActionOptionData("Reset Life Cycle", "Reset Category", "Restores Life Cycle defaults. Changes apply to future pregnancies, death checks, and renown awards.", ResetLifeCycleCategory)
            };
            List<IOptionData> annualBalanceOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Annual Balance Enabled",
                    delegate { return CalendarSettingsState.AnnualBalanceEnabled; },
                    delegate(bool value) { Apply(annualBalanceEnabled: value); }),
                new CalendarBooleanOptionData(
                    "Balance Party Impairment",
                    delegate { return CalendarSettingsState.BalancePartyImpairment; },
                    delegate(bool value) { Apply(balancePartyImpairment: value); }),
                new CalendarBooleanOptionData(
                    "Balance Prisoner Recruitment",
                    delegate { return CalendarSettingsState.BalancePrisonerRecruitment; },
                    delegate(bool value) { Apply(balancePrisonerRecruitment: value); }),
                new CalendarBooleanOptionData(
                    "Balance NPC Marriage",
                    delegate { return CalendarSettingsState.BalanceNpcMarriage; },
                    delegate(bool value) { Apply(balanceNpcMarriage: value); }),
                new CalendarBooleanOptionData(
                    "Balance Map Tracks",
                    delegate { return CalendarSettingsState.BalanceMapTracks; },
                    delegate(bool value) { Apply(balanceMapTracks: value); }),
                new CalendarBooleanOptionData(
                    "Balance Quest Deadlines",
                    delegate { return CalendarSettingsState.BalanceQuestDeadlines; },
                    delegate(bool value) { Apply(balanceQuestDeadlines: value); }),
                new CalendarActionOptionData("Reset Annual Balance", "Reset Category", "Enables annual-rate balancing and restores the category defaults. Existing quest deadlines are unchanged.", ResetAnnualBalanceCategory)
            };
            List<IOptionData> diagnosticsOptions = new List<IOptionData>
            {
                new CalendarBooleanOptionData(
                    "Annual Balance Diagnostics",
                    delegate { return CalendarSettingsState.AnnualBalanceDiagnosticsEnabled; },
                    delegate(bool value) { Apply(annualBalanceDiagnosticsEnabled: value); })
            };
            List<IOptionData> politicalMapOptions = new List<IOptionData>
            {
                new CalendarNumericOptionData(
                    "Control Fill Opacity",
                    10f,
                    100f,
                    true,
                    1,
                    delegate { return CalendarSettingsState.PoliticalControlOpacityPercent; },
                    delegate(float value) { Apply(politicalControlOpacityPercent: (int)value); }),
                new CalendarNumericOptionData(
                    "Control Color Brightness",
                    25f,
                    125f,
                    true,
                    1,
                    delegate { return CalendarSettingsState.PoliticalControlBrightnessPercent; },
                    delegate(float value) { Apply(politicalControlBrightnessPercent: (int)value); }),
                new CalendarBooleanOptionData(
                    "Keep Rivers and Lakes Solid",
                    delegate { return CalendarSettingsState.PoliticalSolidWater; },
                    delegate(bool value) { Apply(politicalSolidWater: value); }),
                new CalendarActionOptionData(
                    "Reset Political Map",
                    "Reset Category",
                    "Restores full opacity, normal brightness, and solid inland water.",
                    ResetPoliticalMapCategory)
            };

            List<IOptionData> options = new List<IOptionData>();
            options.AddRange(calendarOptions);
            options.AddRange(displayOptions);
            options.AddRange(pacingOptions);
            options.AddRange(lightingOptions);
            options.AddRange(lifeCycleOptions);
            options.AddRange(annualBalanceOptions);
            options.AddRange(politicalMapOptions);
            options.AddRange(diagnosticsOptions);

            OptionCategory category = new OptionCategory(
                new List<IOptionData>(),
                new[]
                {
                    new OptionGroup(new TextObject("Calendar"), calendarOptions),
                    new OptionGroup(new TextObject("Display"), displayOptions),
                    new OptionGroup(new TextObject("Pacing"), pacingOptions),
                    new OptionGroup(new TextObject("Lighting"), lightingOptions),
                    new OptionGroup(new TextObject("Life Cycle"), lifeCycleOptions),
                    new OptionGroup(new TextObject("Annual Balance"), annualBalanceOptions),
                    new OptionGroup(new TextObject("Political Map"), politicalMapOptions),
                    new OptionGroup(new TextObject("Diagnostics"), diagnosticsOptions)
                });
            _calendarOptions = new GroupedOptionCategoryVM(
                this,
                new TextObject("Calendar"),
                category,
                nativeCalendarSettingsEnabled,
                true);

            AddCalendarSliderButtonViewModels();
            ApplyCalendarOptionLabels(options);

            AddCategoryToNativeLists(_calendarOptions);
        }

        internal bool TryCloseThroughNativeOptions(bool applyChanges)
        {
            if (_nativeOptions == null)
            {
                Diagnostics.Info("Calendar Settings proxy could not find the native OptionsVM while closing.");
                return false;
            }

            try
            {
                Diagnostics.Info(
                    "Calendar Settings close routed through the native OptionsVM. ApplyChanges="
                    + applyChanges + ".");
                if (applyChanges)
                {
                    _nativeOptions.ExecuteDone();
                }
                else
                {
                    _nativeOptions.ExecuteCancel();
                }

                return true;
            }
            catch (Exception exception)
            {
                Diagnostics.Error("Calendar Settings could not route screen close through the native OptionsVM.", exception);
                return false;
            }
        }

        internal static bool IsCalendarOptionsCategory(GroupedOptionCategoryVM category)
        {
            if (category == null)
            {
                return false;
            }

            foreach (GenericOptionDataVM option in category.AllOptions)
            {
                if (option.GetOptionData() is CalendarOptionDataBase
                    || option.GetOptionData() is CalendarActionOptionData)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The native settings tab is an optional compatibility feature. Check
        /// its private layout before creating a proxy so a game update retains
        /// the unmodified Options screen instead of risking partial UI state.
        /// </summary>
        internal static bool IsNativeLayoutSupported()
        {
            if (_nativeLayoutSupported.HasValue)
            {
                return _nativeLayoutSupported.Value;
            }

            try
            {
                foreach (string fieldName in RequiredNativeListFields)
                {
                    FieldInfo field = AccessTools.Field(typeof(OptionsVM), fieldName);
                    if (field == null || !typeof(IList).IsAssignableFrom(field.FieldType))
                    {
                        _nativeLayoutSupported = false;
                        return false;
                    }
                }

                _nativeLayoutSupported = true;
                return true;
            }
            catch
            {
                _nativeLayoutSupported = false;
                return false;
            }
        }

        internal static void ShowTopResetConfirmation(GroupedOptionCategoryVM category)
        {
            InformationManager.ShowInquiry(
                new InquiryData(
                    "Reset Calendar Settings",
                    "This will restore every Ages of Calradia setting to its default value. You will not be able to undo this action. Are you sure?",
                    true,
                    true,
                    "Yes",
                    "No",
                    delegate
                    {
                        CalendarSettingsState.ResetToDefaults();
                        category.RefreshValues();
                        Diagnostics.Info("Calendar Settings reset from the native Reset to Defaults button.");
                    },
                    null));
        }

        private static void ShowMonthNamesEditor()
        {
            ShowDelimitedTextEditor(
                "Edit Month Names",
                "Enter twelve month names separated by |. Names appear immediately in the date UI. Example: January|February|March|April|May|June|July|August|September|October|November|December",
                CalendarSettingsState.MonthNamesDelimited,
                CalendarSettingsState.TryParseMonthNamesDelimited,
                CalendarSettingsState.TryApplyMonthNamesDelimited);
        }

        private static void ShowSeasonNamesEditor()
        {
            ShowDelimitedTextEditor(
                "Edit Season Names",
                "Enter four season names separated by |. Example: Spring|Summer|Autumn|Winter",
                CalendarSettingsState.SeasonNamesDelimited,
                CalendarSettingsState.TryParseSeasonNamesDelimited,
                CalendarSettingsState.TryApplySeasonNamesDelimited);
        }

        private static void ShowMonthLengthsEditor()
        {
            if (CalendarSettingsState.IsCampaignProfileLocked)
            {
                InformationManager.ShowInquiry(
                    new InquiryData(
                        "Month Lengths Locked",
                        "Month lengths are saved with this campaign and cannot be changed after the campaign session starts.",
                        true,
                        false,
                        "OK",
                        string.Empty,
                        null,
                        null));
                return;
            }

            ShowDelimitedTextEditor(
                "Edit Month Lengths",
                "Enter twelve whole-number lengths separated by | or comma. They must total exactly 365. This is available only before the campaign session starts.",
                CalendarSettingsState.MonthLengthsDelimited,
                CalendarSettingsState.TryParseMonthLengthsDelimited,
                CalendarSettingsState.TryApplyMonthLengthsDelimited);
        }

        private static void ShowDelimitedTextEditor(
            string title,
            string description,
            string defaultValue,
            TryParseDelimitedString parse,
            TryApplyDelimitedString apply)
        {
            ShowCalendarTextInquiry(
                title,
                description,
                defaultValue,
                delegate(string input, out string failure)
                {
                    string[] ignored;
                    return parse(input, out ignored, out failure);
                },
                apply);
        }

        private static void ShowDelimitedTextEditor(
            string title,
            string description,
            string defaultValue,
            TryParseDelimitedIntegers parse,
            TryApplyDelimitedString apply)
        {
            ShowCalendarTextInquiry(
                title,
                description,
                defaultValue,
                delegate(string input, out string failure)
                {
                    int[] ignored;
                    return parse(input, out ignored, out failure);
                },
                apply);
        }

        private static void ShowCalendarTextInquiry(
            string title,
            string description,
            string defaultValue,
            TryValidateDelimitedInput validate,
            TryApplyDelimitedString apply)
        {
            InformationManager.ShowTextInquiry(
                new TextInquiryData(
                    title,
                    description,
                    true,
                    true,
                    "Save",
                    "Cancel",
                    delegate(string input)
                    {
                        string failure;
                        if (apply(input, out failure))
                        {
                            Diagnostics.Info(title + " saved from the native Calendar Options tab.");
                        }
                        else
                        {
                            Diagnostics.Info(title + " was rejected from the native Calendar Options tab: " + failure);
                        }
                    },
                    null,
                    false,
                    delegate(string input)
                    {
                        string failure;
                        bool valid = validate(input, out failure);
                        return Tuple.Create(valid, valid ? string.Empty : failure);
                    },
                    string.Empty,
                    defaultValue),
                false,
                false);
        }

        private delegate bool TryParseDelimitedString(string input, out string[] values, out string failure);
        private delegate bool TryParseDelimitedIntegers(string input, out int[] values, out string failure);
        private delegate bool TryValidateDelimitedInput(string input, out string failure);
        private delegate bool TryApplyDelimitedString(string input, out string failure);

        private void DetachProxyGamepadHandler()
        {
            try
            {
                MethodInfo method = AccessTools.Method(typeof(OptionsVM), "OnGamepadActiveStateChanged");
                if (method == null)
                {
                    Diagnostics.Info("Calendar Settings proxy could not locate the native gamepad handler.");
                    return;
                }

                Action handler = (Action)Delegate.CreateDelegate(typeof(Action), this, method);
                Input.OnGamepadActiveStateChanged = (Action)Delegate.Remove(
                    Input.OnGamepadActiveStateChanged,
                    handler);
            }
            catch (Exception exception)
            {
                Diagnostics.Error("Calendar Settings proxy could not detach its unused gamepad handler.", exception);
            }
        }

        private void ApplyCalendarOptionLabels(IList<IOptionData> options)
        {
            if (_calendarOptions.Groups == null || _calendarOptions.Groups.Count == 0)
            {
                return;
            }

            int optionIndex = 0;
            foreach (OptionGroupVM group in _calendarOptions.Groups)
            {
                for (int i = 0; i < group.Options.Count && optionIndex < options.Count; i++, optionIndex++)
                {
                    CalendarOptionDataBase calendarOption = options[optionIndex] as CalendarOptionDataBase;
                    if (calendarOption != null)
                    {
                        group.Options[i].Name = calendarOption.Name;
                        group.Options[i].Description = calendarOption.Description;
                        continue;
                    }

                    CalendarActionOptionData calendarAction = options[optionIndex] as CalendarActionOptionData;
                    ActionOptionDataVM actionOption = group.Options[i] as ActionOptionDataVM;
                    if (calendarAction != null && actionOption != null)
                    {
                        actionOption.Name = calendarAction.DisplayName;
                        actionOption.Description = calendarAction.Description;
                        actionOption.ActionName = calendarAction.DisplayActionName;
                    }
                }
            }
        }

        private void AddCalendarSliderButtonViewModels()
        {
            if (_calendarOptions.Groups == null)
            {
                return;
            }

            foreach (OptionGroupVM group in _calendarOptions.Groups)
            {
                for (int index = 0; index < group.Options.Count; index++)
                {
                    BooleanOptionDataVM boolean = group.Options[index] as BooleanOptionDataVM;
                    CalendarBooleanOptionData calendarBoolean = boolean == null
                        ? null
                        : boolean.GetOptionData() as CalendarBooleanOptionData;
                    if (calendarBoolean != null)
                    {
                        group.Options[index] = new CalendarBooleanOptionDataVM(
                            this,
                            calendarBoolean,
                            new TextObject(boolean.Name),
                            new TextObject(boolean.Description),
                            RefreshCalendarOptionControls);
                        continue;
                    }

                    NumericOptionDataVM numeric = group.Options[index] as NumericOptionDataVM;
                    CalendarNumericOptionData calendarNumeric = numeric == null
                        ? null
                        : numeric.GetOptionData() as CalendarNumericOptionData;
                    if (calendarNumeric == null)
                    {
                        continue;
                    }

                    group.Options[index] = new CalendarNumericOptionDataVM(
                        this,
                        calendarNumeric,
                        new TextObject(numeric.Name),
                        new TextObject(numeric.Description),
                        RefreshCalendarOptionControls);
                }
            }
        }

        private void RefreshCalendarOptionControls()
        {
            if (_calendarOptions.Groups == null)
            {
                return;
            }

            foreach (OptionGroupVM group in _calendarOptions.Groups)
            {
                for (int index = 0; index < group.Options.Count; index++)
                {
                    CalendarOptionDataBase option = group.Options[index].GetOptionData() as CalendarOptionDataBase;
                    if (option == null)
                    {
                        continue;
                    }

                    CalendarBooleanOptionDataVM boolean = group.Options[index] as CalendarBooleanOptionDataVM;
                    if (boolean != null)
                    {
                        boolean.RefreshFromOptionData();
                        continue;
                    }

                    CalendarNumericOptionDataVM numeric = group.Options[index] as CalendarNumericOptionDataVM;
                    if (numeric != null)
                    {
                        numeric.RefreshFromOptionData();
                    }
                }
            }
        }

        private void RefreshCalendarOptions()
        {
            if (_calendarOptions == null)
            {
                return;
            }

            _calendarOptions.RefreshValues();
            RefreshCalendarOptionControls();
        }

        private void CopyNativeState(OptionsVM source)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (FieldInfo field in typeof(OptionsVM).GetFields(flags))
            {
                field.SetValue(this, field.GetValue(source));
            }

            CloneListField("_categories");
            CloneListField("_groupedCategories");
        }

        private void CloneListField(string fieldName)
        {
            FieldInfo field = AccessTools.Field(typeof(OptionsVM), fieldName);
            if (field == null)
            {
                return;
            }

            IList original = field.GetValue(this) as IList;
            IList clone = Activator.CreateInstance(field.FieldType) as IList;
            if (original == null || clone == null)
            {
                return;
            }

            foreach (object value in original)
            {
                clone.Add(value);
            }

            field.SetValue(this, clone);
        }

        private void AddCategoryToNativeLists(GroupedOptionCategoryVM category)
        {
            AddToListField("_categories", category);
            AddToListField("_groupedCategories", category);
        }

        private void AddToListField(string fieldName, object value)
        {
            FieldInfo field = AccessTools.Field(typeof(OptionsVM), fieldName);
            IList list = field == null ? null : field.GetValue(this) as IList;
            if (list == null)
            {
                return;
            }

            // Keep every native/MCM category index unchanged. The Calendar
            // fallback is the final tab only in sessions where MCM is absent.
            list.Add(value);
        }

        private static void Apply(
            bool? useLeapYears = null,
            bool? showDayLabel = null,
            bool? showYearLabel = null,
            bool? useOrdinalDaySuffixes = null,
            bool? use24HourClock = null,
            float? campaignTimeScale = null,
            string dateFormat = null,
            bool? autoCampaignTimeScale = null,
            bool? useCalendarMonthPregnancy = null,
            int? pregnancyDurationMonths = null,
            float? pregnancyDurationInDays = null,
            float? renownGainMultiplier = null,
            float? lordDeathRateMultiplier = null,
            bool? balancePartyImpairment = null,
            bool? balancePrisonerRecruitment = null,
            bool? balanceNpcMarriage = null,
            bool? balanceMapTracks = null,
            bool? balanceQuestDeadlines = null,
            bool? annualBalanceEnabled = null,
            bool? annualBalanceDiagnosticsEnabled = null,
            float? fastForwardTimeMultiplier = null,
            bool? clockSynchronizedLighting = null,
            float? visualSunriseHour = null,
            float? visualSunsetHour = null,
            float? visualLightingTransitionHours = null,
            int? politicalControlOpacityPercent = null,
            int? politicalControlBrightnessPercent = null,
            bool? politicalSolidWater = null)
        {
            bool requestedLeapYears = useLeapYears ?? CalendarSettingsState.UseLeapYears;
            bool requestedShowDayLabel = showDayLabel ?? CalendarSettingsState.ShowDayLabel;
            bool requestedShowYearLabel = showYearLabel ?? CalendarSettingsState.ShowYearLabel;
            bool requestedOrdinalDaySuffixes = useOrdinalDaySuffixes ?? CalendarSettingsState.UseOrdinalDaySuffixes;
            bool requestedUse24HourClock = use24HourClock ?? CalendarSettingsState.Use24HourClock;
            float requestedCampaignTimeScale = campaignTimeScale ?? CalendarSettingsState.CampaignTimeScale;
            string requestedDateFormat = dateFormat ?? CalendarSettingsState.DateFormat;
            bool requestedAutoTimeScale = autoCampaignTimeScale ?? CalendarSettingsState.AutoCampaignTimeScale;
            // Only the checkbox enables automatic pacing. A slider edit is a
            // manual choice even when its resulting value is exactly the
            // automatic 0.803 default.
            float requestedFastForwardTimeMultiplier = fastForwardTimeMultiplier
                ?? CalendarSettingsState.FastForwardTimeMultiplier;
            bool requestedCalendarMonthPregnancy = useCalendarMonthPregnancy
                ?? CalendarSettingsState.UseCalendarMonthPregnancy;
            int requestedPregnancyMonths = pregnancyDurationMonths
                ?? CalendarSettingsState.PregnancyDurationMonths;
            float requestedPregnancyDays = pregnancyDurationInDays
                ?? CalendarSettingsState.PregnancyDurationInDays;
            float requestedRenownMultiplier = renownGainMultiplier
                ?? CalendarSettingsState.RenownGainMultiplier;
            float requestedLordDeathRateMultiplier = lordDeathRateMultiplier
                ?? CalendarSettingsState.LordDeathRateMultiplier;
            bool requestedBalancePartyImpairment = balancePartyImpairment
                ?? CalendarSettingsState.BalancePartyImpairment;
            bool requestedBalancePrisonerRecruitment = balancePrisonerRecruitment
                ?? CalendarSettingsState.BalancePrisonerRecruitment;
            bool requestedBalanceNpcMarriage = balanceNpcMarriage
                ?? CalendarSettingsState.BalanceNpcMarriage;
            bool requestedBalanceMapTracks = balanceMapTracks
                ?? CalendarSettingsState.BalanceMapTracks;
            bool requestedBalanceQuestDeadlines = balanceQuestDeadlines
                ?? CalendarSettingsState.BalanceQuestDeadlines;
            bool requestedAnnualBalanceDiagnostics = annualBalanceDiagnosticsEnabled
                ?? CalendarSettingsState.AnnualBalanceDiagnosticsEnabled;
            bool requestedAnnualBalanceEnabled = annualBalanceEnabled
                ?? CalendarSettingsState.AnnualBalanceEnabled;
            bool requestedClockSynchronizedLighting = clockSynchronizedLighting
                ?? CalendarSettingsState.ClockSynchronizedLighting;
            float requestedVisualSunriseHour = visualSunriseHour
                ?? CalendarSettingsState.VisualSunriseHour;
            float requestedVisualSunsetHour = visualSunsetHour
                ?? CalendarSettingsState.VisualSunsetHour;
            float requestedVisualLightingTransitionHours = visualLightingTransitionHours
                ?? CalendarSettingsState.VisualLightingTransitionHours;
            int requestedPoliticalControlOpacity = politicalControlOpacityPercent
                ?? CalendarSettingsState.PoliticalControlOpacityPercent;
            int requestedPoliticalControlBrightness = politicalControlBrightnessPercent
                ?? CalendarSettingsState.PoliticalControlBrightnessPercent;
            bool requestedPoliticalSolidWater = politicalSolidWater
                ?? CalendarSettingsState.PoliticalSolidWater;

            // Bannerlord initializes option controls by writing their current
            // value back to the data source. Do not treat those UI refreshes as
            // genuine settings edits or repeatedly save/synchronize the file.
            if (requestedLeapYears == CalendarSettingsState.UseLeapYears
                && requestedShowDayLabel == CalendarSettingsState.ShowDayLabel
                && requestedShowYearLabel == CalendarSettingsState.ShowYearLabel
                && requestedOrdinalDaySuffixes == CalendarSettingsState.UseOrdinalDaySuffixes
                && requestedUse24HourClock == CalendarSettingsState.Use24HourClock
                && NearlyEqual(requestedCampaignTimeScale, CalendarSettingsState.CampaignTimeScale)
                && string.Equals(requestedDateFormat, CalendarSettingsState.DateFormat, StringComparison.Ordinal)
                && requestedAutoTimeScale == CalendarSettingsState.AutoCampaignTimeScale
                && NearlyEqual(requestedFastForwardTimeMultiplier, CalendarSettingsState.FastForwardTimeMultiplier)
                && requestedCalendarMonthPregnancy == CalendarSettingsState.UseCalendarMonthPregnancy
                && requestedPregnancyMonths == CalendarSettingsState.PregnancyDurationMonths
                && NearlyEqual(requestedPregnancyDays, CalendarSettingsState.PregnancyDurationInDays)
                && NearlyEqual(requestedRenownMultiplier, CalendarSettingsState.RenownGainMultiplier)
                && NearlyEqual(requestedLordDeathRateMultiplier, CalendarSettingsState.LordDeathRateMultiplier)
                && requestedBalancePartyImpairment == CalendarSettingsState.BalancePartyImpairment
                && requestedBalancePrisonerRecruitment == CalendarSettingsState.BalancePrisonerRecruitment
                && requestedBalanceNpcMarriage == CalendarSettingsState.BalanceNpcMarriage
                && requestedBalanceMapTracks == CalendarSettingsState.BalanceMapTracks
                && requestedBalanceQuestDeadlines == CalendarSettingsState.BalanceQuestDeadlines
                && requestedAnnualBalanceEnabled == CalendarSettingsState.AnnualBalanceEnabled
                && requestedAnnualBalanceDiagnostics == CalendarSettingsState.AnnualBalanceDiagnosticsEnabled
                && requestedClockSynchronizedLighting == CalendarSettingsState.ClockSynchronizedLighting
                && NearlyEqual(requestedVisualSunriseHour, CalendarSettingsState.VisualSunriseHour)
                && NearlyEqual(requestedVisualSunsetHour, CalendarSettingsState.VisualSunsetHour)
                && NearlyEqual(requestedVisualLightingTransitionHours, CalendarSettingsState.VisualLightingTransitionHours)
                && requestedPoliticalControlOpacity == CalendarSettingsState.PoliticalControlOpacityPercent
                && requestedPoliticalControlBrightness == CalendarSettingsState.PoliticalControlBrightnessPercent
                && requestedPoliticalSolidWater == CalendarSettingsState.PoliticalSolidWater)
            {
                return;
            }

            CalendarSettingsState.Apply(
                CalendarSettingsState.CalendarSystem,
                requestedLeapYears,
                requestedShowDayLabel,
                requestedShowYearLabel,
                requestedCampaignTimeScale,
                requestedDateFormat,
                autoCampaignTimeScale: requestedAutoTimeScale,
                fastForwardTimeMultiplier: requestedFastForwardTimeMultiplier,
                useCalendarMonthPregnancy: requestedCalendarMonthPregnancy,
                pregnancyDurationMonths: requestedPregnancyMonths,
                pregnancyDurationInDays: requestedPregnancyDays,
                renownGainMultiplier: requestedRenownMultiplier,
                lordDeathRateMultiplier: requestedLordDeathRateMultiplier,
                useOrdinalDaySuffixes: requestedOrdinalDaySuffixes,
                use24HourClock: requestedUse24HourClock,
                balancePartyImpairment: requestedBalancePartyImpairment,
                balancePrisonerRecruitment: requestedBalancePrisonerRecruitment,
                balanceNpcMarriage: requestedBalanceNpcMarriage,
                balanceMapTracks: requestedBalanceMapTracks,
                balanceQuestDeadlines: requestedBalanceQuestDeadlines,
                annualBalanceEnabled: requestedAnnualBalanceEnabled,
                annualBalanceDiagnosticsEnabled: requestedAnnualBalanceDiagnostics,
                clockSynchronizedLighting: requestedClockSynchronizedLighting,
                visualSunriseHour: requestedVisualSunriseHour,
                visualSunsetHour: requestedVisualSunsetHour,
                visualLightingTransitionHours: requestedVisualLightingTransitionHours,
                politicalControlOpacityPercent: requestedPoliticalControlOpacity,
                politicalControlBrightnessPercent: requestedPoliticalControlBrightness,
                politicalSolidWater: requestedPoliticalSolidWater);
            CalendarSettingsState.Save();
        }

        private static bool NearlyEqual(float first, float second)
        {
            return Math.Abs(first - second) < 0.0001f;
        }

        private static string[] GetDateFormats()
        {
            return new[]
            {
                "{Day} {Month} {Year}",
                "{Month} {Day} {Year}",
                "{Year} {Month} {Day}"
            };
        }

        private static float GetDateFormatIndex()
        {
            int index = Array.IndexOf(GetDateFormats(), CalendarSettingsState.DateFormat);
            return index < 0 ? 1f : index;
        }

        private static bool IsAnnualBalanceEnabled()
        {
            return CalendarSettingsState.BalancePartyImpairment
                && CalendarSettingsState.BalancePrisonerRecruitment
                && CalendarSettingsState.BalanceNpcMarriage
                && CalendarSettingsState.BalanceMapTracks
                && CalendarSettingsState.BalanceQuestDeadlines;
        }

        private static void SetAnnualBalanceEnabled(bool enabled)
        {
            Apply(
                balancePartyImpairment: enabled,
                balancePrisonerRecruitment: enabled,
                balanceNpcMarriage: enabled,
                balanceMapTracks: enabled,
                balanceQuestDeadlines: enabled);
        }

        private void ResetCalendarCategory()
        {
            CalendarSettingsState.ResetCalendarCategory();
            CompleteCategoryReset(
                "Calendar",
                CalendarSettingsState.IsCampaignProfileLocked
                    ? "Calendar names were reset. Month lengths and leap-year rules are locked by this active campaign."
                    : "Calendar settings were reset.");
        }

        private void ResetDisplayCategory()
        {
            Apply(
                showDayLabel: false,
                showYearLabel: false,
                useOrdinalDaySuffixes: true,
                use24HourClock: true,
                dateFormat: "{Month} {Day} {Year}");
            CompleteCategoryReset("Display", "Display settings were reset.");
        }

        private void ResetPacingCategory()
        {
            Apply(
                campaignTimeScale: CalendarSettingsState.DefaultCampaignTimeScale,
                autoCampaignTimeScale: true,
                fastForwardTimeMultiplier: CalendarSettingsState.DefaultFastForwardTimeMultiplier);
            CompleteCategoryReset("Pacing", "Pacing was reset to about three hours normally and ninety minutes fast-forward per year.");
        }

        private void ResetLightingCategory()
        {
            Apply(
                clockSynchronizedLighting: CalendarSettingsState.DefaultClockSynchronizedLighting,
                visualSunriseHour: CalendarSettingsState.DefaultVisualSunriseHour,
                visualSunsetHour: CalendarSettingsState.DefaultVisualSunsetHour,
                visualLightingTransitionHours: CalendarSettingsState.DefaultVisualLightingTransitionHours);
            CompleteCategoryReset("Lighting", "Lighting was reset to the campaign clock with the default sunrise, sunset, and two-hour transitions.");
        }

        private void ResetLifeCycleCategory()
        {
            Apply(
                useCalendarMonthPregnancy: true,
                pregnancyDurationMonths: 9,
                lordDeathRateMultiplier: 0.20f,
                renownGainMultiplier: 0.50f);
            CompleteCategoryReset("Life Cycle", "Life Cycle settings were reset for future pregnancies, death checks, and renown awards.");
        }

        private void ResetAnnualBalanceCategory()
        {
            Apply(annualBalanceEnabled: true, balancePartyImpairment: true, balancePrisonerRecruitment: true, balanceNpcMarriage: true, balanceMapTracks: true, balanceQuestDeadlines: true);
            CompleteCategoryReset("Annual Balance", "Annual Balance settings were reset. Existing quest deadlines are unchanged.");
        }

        private void ResetPoliticalMapCategory()
        {
            Apply(
                politicalControlOpacityPercent: CalendarSettingsState.DefaultPoliticalControlOpacityPercent,
                politicalControlBrightnessPercent: CalendarSettingsState.DefaultPoliticalControlBrightnessPercent,
                politicalSolidWater: CalendarSettingsState.DefaultPoliticalSolidWater);
            CompleteCategoryReset("Political Map", "Political Map settings were reset to full opacity, normal brightness, and solid inland water.");
        }

        private void CompleteCategoryReset(string category, string message)
        {
            RefreshCalendarOptions();
            Diagnostics.Info(category + " category reset action: " + message);
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private static void ResetDiagnosticsCategory()
        {
            Apply(annualBalanceDiagnosticsEnabled: true);
        }

        private static float GetClockFormatIndex()
        {
            return CalendarSettingsState.Use24HourClock ? 0f : 1f;
        }

        private static int ClampIndex(float value, int length)
        {
            return Math.Max(0, Math.Min(length - 1, (int)value));
        }
    }

    internal abstract class CalendarOptionDataBase : IOptionData
    {
        private readonly string _name;

        protected CalendarOptionDataBase(string name)
        {
            _name = name;
        }

        internal string Name
        {
            get { return _name; }
        }

        internal string Description
        {
            get
            {
                switch (_name)
                {
                    case "Show Day Label":
                        return "Displays the word 'Day' before the day number on the campaign map date.";
                    case "Show Year Label":
                        return "Displays the word 'Year' before the year number on the campaign map date.";
                    case "Use Ordinal Day Suffixes":
                        return "Displays dates as 1st, 2nd, 3rd, and so on. 11th, 12th, and 13th use the correct th suffix.";
                    case "Automatic Campaign Time Scale":
                        return "Targets about three real hours per calendar year at normal speed. Turning it off lets you choose a different slider value.";
                    case "Campaign Time Scale":
                        return "Controls how quickly campaign time advances when automatic pacing is disabled. Lower values are slower.";
                    case "Fast-Forward Speed Multiplier":
                        return "Sets Bannerlord's built-in fast-forward speed while the map is fast-forwarding. Normal map pace remains fixed. 4 is Bannerlord's supported maximum and avoids AI time-step skips.";
                    case "Date Format":
                        return "Select the order of the month, day, and year. The season is displayed separately to the right of the map clock.";
                    case "Clock Format":
                        return "Shows the campaign clock beneath the calendar date as either 24-hour time or 12-hour time with AM/PM.";
                    case "Use Calendar-Month Pregnancy":
                        return "Uses calendar months for pregnancy duration instead of the fixed day value.";
                    case "Pregnancy Duration (Months)":
                        return "Sets how many calendar months a pregnancy lasts when calendar-month pregnancy is enabled.";
                    case "Lord Death Rate Multiplier":
                        return "Retains this fraction of Bannerlord's ordinary noble-lord old-age and battle death chance. 0.20 keeps 20%; 1.00 is native. Executions and scripted deaths are unchanged. Changes affect future checks.";
                    case "Renown Gain Multiplier":
                        return "Scales positive renown rewards. A value of 0.50 gives half the normal positive renown.";
                    case "Balance Party Impairment":
                        return "Scales post-battle disorganization and vulnerability durations to the 365-day year. Best configured before starting a campaign.";
                    case "Balance Prisoner Recruitment":
                        return "Scales prisoner conformity gained per campaign hour for player and AI parties. Best configured before starting a campaign.";
                    case "Balance NPC Marriage":
                        return "Converts NPC marriage probability to preserve its annual rate across the 365-day year. Best configured before starting a campaign.";
                    case "Balance Map Tracks":
                        return "Scales map-track lifetime to the 365-day year while keeping detection and spotting rules native. Best configured before starting a campaign.";
                    case "Balance Quest Deadlines":
                        return "Extends deadlines for quests started while enabled. Existing quest deadlines are never changed. Best configured before starting a campaign.";
                    case "Annual Balance Diagnostics":
                        return "Writes sampled annual-balance checkpoints into crash reports. Disable only when diagnosing performance.";
                    default:
                        return string.Empty;
                }
            }
        }

        public abstract float GetDefaultValue();

        public virtual void Commit()
        {
            CalendarSettingsState.Save();
        }

        public abstract float GetValue(bool forceRefresh);

        public abstract void SetValue(float value);

        public abstract object GetOptionType();

        public bool IsNative()
        {
            return false;
        }

        public virtual bool IsAction()
        {
            return false;
        }

        public (string, bool) GetIsDisabledAndReasonID()
        {
            if (CalendarSettingsState.IsGameplaySettingLocked(_name))
            {
                return ("CampaignProfileLocked", true);
            }

            return (string.Empty, false);
        }

    }

    // Action rows refresh through their own VM override, so the generic VM
    // refresh hook above is not guaranteed to run after a category reset.
    // Reapply Calendar action labels here to prevent the native fallback
    // "Start Benchmark" text from resurfacing.
    [HarmonyPatch(typeof(ActionOptionDataVM), nameof(ActionOptionDataVM.RefreshValues))]
    internal static class CalendarActionOptionLabelRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ActionOptionDataVM __instance)
        {
            if (__instance == null)
            {
                return;
            }

            CalendarActionOptionData calendarAction = __instance.GetOptionData() as CalendarActionOptionData;
            if (calendarAction == null)
            {
                return;
            }

            __instance.Name = calendarAction.DisplayName;
            __instance.Description = calendarAction.Description;
            __instance.ActionName = calendarAction.DisplayActionName;
        }
    }

    internal sealed class CalendarBooleanOptionData : CalendarOptionDataBase, IBooleanOptionData
    {
        private readonly Func<bool> _get;
        private readonly Action<bool> _set;
        private readonly bool _default;

        internal CalendarBooleanOptionData(string name, Func<bool> get, Action<bool> set)
            : base(name)
        {
            _get = get;
            _set = set;
            _default = get();
        }

        public override float GetDefaultValue()
        {
            return _default ? 1f : 0f;
        }

        public override float GetValue(bool forceRefresh)
        {
            return _get() ? 1f : 0f;
        }

        public override void SetValue(float value)
        {
            _set(value >= 0.5f);
        }

        public override object GetOptionType()
        {
            return OptionsVM.OptionsDataType.BooleanOption;
        }
    }

    internal sealed class CalendarNumericOptionData : CalendarOptionDataBase, INumericOptionData
    {
        private readonly float _min;
        private readonly float _max;
        private readonly bool _discrete;
        private readonly int _interval;
        private readonly Func<float> _get;
        private readonly Action<float> _set;
        private readonly float _default;

        internal CalendarNumericOptionData(
            string name,
            float min,
            float max,
            bool discrete,
            int interval,
            Func<float> get,
            Action<float> set)
            : base(name)
        {
            _min = min;
            _max = max;
            _discrete = discrete;
            _interval = interval;
            _get = get;
            _set = set;
            _default = get();
        }

        public override float GetDefaultValue()
        {
            return _default;
        }

        public override float GetValue(bool forceRefresh)
        {
            return Math.Max(_min, Math.Min(_max, _get()));
        }

        public override void SetValue(float value)
        {
            _set(Math.Max(_min, Math.Min(_max, value)));
        }

        public override object GetOptionType()
        {
            return OptionsVM.OptionsDataType.NumericOption;
        }

        public float GetMinValue()
        {
            return _min;
        }

        public float GetMaxValue()
        {
            return _max;
        }

        public bool GetIsDiscrete()
        {
            return _discrete;
        }

        public int GetDiscreteIncrementInterval()
        {
            return _interval;
        }

        public bool GetShouldUpdateContinuously()
        {
            return !_discrete;
        }
    }

    /// <summary>
    /// Supplies the command methods used by the Calendar-only previous/next
    /// slider buttons in the prefab. Native numeric rows keep their untouched
    /// view model and are not affected by the Calendar UI override.
    /// </summary>
    internal sealed class CalendarNumericOptionDataVM : NumericOptionDataVM
    {
        private readonly Action _onValueChanged;

        internal CalendarNumericOptionDataVM(
            OptionsVM options,
            INumericOptionData optionData,
            TextObject name,
            TextObject description,
            Action onValueChanged)
            : base(options, optionData, name, description)
        {
            _onValueChanged = onValueChanged;
        }

        public void ExecuteDecrease()
        {
            SetButtonValue(OptionValue - GetButtonIncrement());
        }

        public void ExecuteIncrease()
        {
            SetButtonValue(OptionValue + GetButtonIncrement());
        }

        public override void SetValue(float value)
        {
            base.SetValue(value);
            if (_onValueChanged != null)
            {
                _onValueChanged();
            }
        }

        internal void RefreshFromOptionData()
        {
            OptionValue = GetOptionData().GetValue(false);
        }

        private float GetButtonIncrement()
        {
            return IsDiscrete
                ? Math.Max(1f, DiscreteIncrementInterval)
                : Math.Max(0.01f, (Max - Min) / 100f);
        }

        private void SetButtonValue(float value)
        {
            // SetValue writes through to IOptionData. Assigning the bound UI
            // property can otherwise repaint the handle without persisting a
            // command-button change on some Bannerlord UI revisions.
            SetValue(Math.Max(Min, Math.Min(Max, value)));
        }
    }

    /// <summary>
    /// Makes Calendar checkboxes use an explicit command. This avoids relying
    /// on OptionsItemWidget's native boolean-event routing, which can leave a
    /// mod-provided checkbox visually clickable but without a state change.
    /// </summary>
    internal sealed class CalendarBooleanOptionDataVM : BooleanOptionDataVM
    {
        private readonly Action _onValueChanged;

        internal CalendarBooleanOptionDataVM(
            OptionsVM options,
            IBooleanOptionData optionData,
            TextObject name,
            TextObject description,
            Action onValueChanged)
            : base(options, optionData, name, description)
        {
            _onValueChanged = onValueChanged;
        }

        public void ExecuteToggle()
        {
            SetValue(OptionValueAsBoolean ? 0f : 1f);
        }

        public override void SetValue(float value)
        {
            base.SetValue(value);
            if (_onValueChanged != null)
            {
                _onValueChanged();
            }
        }

        internal void RefreshFromOptionData()
        {
            OptionValueAsBoolean = GetOptionData().GetValue(false) >= 0.5f;
        }
    }

    internal sealed class CalendarSelectionOptionData : CalendarOptionDataBase, ISelectionOptionData
    {
        private readonly string[] _names;
        private readonly Func<float> _get;
        private readonly Action<float> _set;

        internal CalendarSelectionOptionData(
            string name,
            string[] names,
            Func<float> get,
            Action<float> set)
            : base(name)
        {
            _names = names;
            _get = get;
            _set = set;
        }

        public override float GetDefaultValue()
        {
            return _get();
        }

        public override float GetValue(bool forceRefresh)
        {
            return _get();
        }

        public override void SetValue(float value)
        {
            _set(value);
        }

        public override object GetOptionType()
        {
            return OptionsVM.OptionsDataType.MultipleSelectionOption;
        }

        public int GetSelectableOptionsLimit()
        {
            return _names.Length;
        }

        public IEnumerable<SelectionData> GetSelectableOptionNames()
        {
            for (int i = 0; i < _names.Length; i++)
            {
                yield return new SelectionData(false, _names[i]);
            }
        }
    }

    /// <summary>
    /// Native options expose a supported action-row type. It lets the Calendar
    /// tab open a text inquiry for values that cannot fit safely into sliders
    /// or checkbox controls, such as custom month names.
    /// </summary>
    internal sealed class CalendarActionOptionData : ActionOptionData
    {
        internal CalendarActionOptionData(
            string displayName,
            string displayActionName,
            string description,
            Action action)
            // Bannerlord resolves action-option localization while building the
            // row, before module GameText variations are guaranteed to load.
            // Use a known native ID solely as an internal type token and set
            // the Calendar-specific visible strings directly in its VM.
            : base("Benchmark", action)
        {
            DisplayName = displayName;
            DisplayActionName = displayActionName;
            Description = description;
        }

        internal string DisplayName { get; private set; }

        internal string DisplayActionName { get; private set; }

        internal string Description { get; private set; }

    }

}
