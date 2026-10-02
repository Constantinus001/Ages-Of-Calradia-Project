using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Library;

namespace CalradiaCampaignClock
{
    /// <summary>
    /// Extends the native map-time VM without replacing it or reading private
    /// callback fields. UIExtenderEx invokes OnRefresh after native Tick(), so
    /// normal play, pause transitions, waits, and accelerated time share one
    /// refresh path.
    /// </summary>
    [ViewModelMixin(nameof(MapTimeControlVM.Tick), true)]
    internal sealed class CampaignClockMapTimeMixin
        : BaseViewModelMixin<MapTimeControlVM>
    {
        private string _clockText = string.Empty;
        private int _lastMinuteOfDay = -1;
        private bool _refreshFailureLogged;

        public CampaignClockMapTimeMixin(MapTimeControlVM viewModel)
            : base(viewModel)
        {
            RefreshClock();
        }

        [DataSourceProperty]
        public string CalradiaCampaignClockText
        {
            get { return _clockText; }
        }

        public override void OnRefresh()
        {
            RefreshClock();
        }

        private void RefreshClock()
        {
            try
            {
                MapTimeControlVM viewModel = ViewModel;
                if (viewModel == null || Campaign.Current == null)
                {
                    SetClockText(string.Empty, -1);
                    return;
                }

                // This is the native MapTimeControlVM hour expression. Both
                // the numeric label and inherited sundial receive this exact
                // canonical value, preventing display drift in fast-forward.
                double hourInDay = CampaignTime.Now.ToHours
                    % CampaignTime.HoursInDay;
                if (hourInDay < 0d)
                {
                    hourInDay += CampaignTime.HoursInDay;
                }

                viewModel.Time = hourInDay;
                int minuteOfDay = ClockFormatter.GetMinuteOfDay(hourInDay);
                if (minuteOfDay == _lastMinuteOfDay)
                {
                    return;
                }

                string formatted = ClockFormatter.FormatHour(
                    hourInDay,
                    CampaignClockSettings.Use24HourClock,
                    CampaignClockSettings.ShowMeridiemOnSecondLine);
                SetClockText(formatted, minuteOfDay);
            }
            catch (Exception exception)
            {
                SetClockText(string.Empty, -1);
                if (_refreshFailureLogged)
                {
                    return;
                }

                _refreshFailureLogged = true;
                CampaignClockDiagnostics.Error(
                    "Campaign clock refresh failed; the native sundial and time controls remain available.",
                    exception);
            }
        }

        private void SetClockText(string value, int minuteOfDay)
        {
            _lastMinuteOfDay = minuteOfDay;
            string normalized = value ?? string.Empty;
            if (string.Equals(_clockText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _clockText = normalized;
            OnPropertyChangedWithValue(
                _clockText,
                nameof(CalradiaCampaignClockText));
        }
    }

    /// <summary>
    /// Native asset target: SandBox GUI/Prefabs/Map/MapBar.xml, the Children
    /// collection of MapCurrentTimeVisualWidget Id="CenterPanel".
    /// Purpose: add only the numeric label beside the native sundial.
    /// Compatibility risk: another UI overhaul may remove or rename the XPath;
    /// UIExtenderEx then omits this additive widget without replacing MapBar.
    /// Verification: Tests/Verify-CampaignClock.ps1 checks the unique widget,
    /// binding, XPath, and absence of a packaged MapBar.xml override.
    /// </summary>
    [PrefabExtension(
        "MapBar",
        "descendant::MapCurrentTimeVisualWidget[@Id='CenterPanel']/Children")]
    internal sealed class CampaignClockMapBarPrefabPatch
        : PrefabExtensionInsertPatch
    {
        public override InsertType Type
        {
            get { return InsertType.Child; }
        }

        public override int Index
        {
            get { return 1; }
        }

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            return
                "<TextWidget Id=\"CalradiaCampaignClockText\""
                + " DoNotAcceptEvents=\"true\""
                + " WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\""
                + " SuggestedWidth=\"70\" SuggestedHeight=\"36\""
                + " HorizontalAlignment=\"Center\" PositionXOffset=\"47\""
                + " VerticalAlignment=\"Center\" PositionYOffset=\"0\""
                + " Brush=\"MapTextBrushGal\" Brush.FontSize=\"14\""
                + " Brush.FontColor=\"#FFF2D0FF\""
                + " Brush.TextHorizontalAlignment=\"Center\""
                + " Brush.TextVerticalAlignment=\"Center\""
                + " Text=\"@CalradiaCampaignClockText\" />";
        }
    }
}
