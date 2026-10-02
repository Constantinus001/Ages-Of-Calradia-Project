using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    /// <summary>
    /// UI-only reader of the approved calendar. No campaign or calendar state is
    /// changed. Missing calendar methods hide the pointer and log once. Native
    /// targets are CampaignTime.Now and the standard hover HintViewModel API.
    /// Verify-MapBarSeasonPointer.ps1 covers day projection and calendar boundaries.
    /// </summary>
    public sealed class AocMapBarSeasonPointerWidget : TextureWidget
    {
        private MethodInfo _season, _year, _day, _length, _seasonName;
        private bool _resolutionAttempted, _failed;
        private HintViewModel _hint;
        private string _hintText;
        private bool _hovering;
        private Widget _seasonCrown;
        private Widget _seasonDial;
        private bool _diagnosticWriteFailed;

        public AocMapBarSeasonPointerWidget(UIContext context) : base(context)
        {
            PreviewSeason = -1;
            Radius = 75f;
        }

        [Editor(false)] public float CenterX { get; set; }
        [Editor(false)] public float CenterY { get; set; }
        [Editor(false)] public float Radius { get; set; }
        [Editor(false)] public int PreviewSeason { get; set; }
        [Editor(false)] public int PreviewDay { get; set; }
        [Editor(false)] public int PreviewLength { get; set; }

        internal static bool TryProject(int season, int day, int length,
            out float angle, out int daysRemaining)
        {
            angle = 0f;
            daysRemaining = 0;
            if (season < 0 || season > 3 || length <= 0 || day < 0 || day >= length)
                return false;
            int visualIndex = (season + 1) % 4; // WIN, SPR, SUM, AUT
            angle = (float)(Math.PI + (visualIndex + day / (double)length) * Math.PI / 4);
            daysRemaining = length - day;
            return true;
        }

        protected override void OnUpdate(float dt)
        {
            base.OnUpdate(dt);
            if (_failed) return;
            try
            {
                int season, day, length;
                string name;
                // Preview values are accepted only in the diagnostics assembly.
                if (GetType().Assembly.GetName().Name == "AocMapBarPreviewHost" && PreviewSeason >= 0)
                {
                    season = PreviewSeason; day = PreviewDay; length = PreviewLength;
                    name = season >= 0 && season < 4
                        ? new[] { "Spring", "Summer", "Autumn", "Winter" }[season] : string.Empty;
                }
                else
                {
                    if (Campaign.Current == null) { HidePointer(); return; }
                    ResolveCalendar();
                    CampaignTime now = CampaignTime.Now;
                    season = (int)_season.Invoke(null, new object[] { now });
                    int year = (int)_year.Invoke(null, new object[] { now });
                    day = (int)_day.Invoke(null, new object[] { now });
                    length = (int)_length.Invoke(null, new object[] { year, season });
                    name = (string)_seasonName.Invoke(null, new object[] { season });
                }
                float angle;
                int remaining;
                if (!TryProject(season, day, length, out angle, out remaining))
                { HidePointer(); return; }
                PositionXOffset = CenterX + (float)Math.Cos(angle) * Radius - SuggestedWidth / 2;
                PositionYOffset = CenterY + (float)Math.Sin(angle) * Radius - SuggestedHeight / 2;
                Rotation = (angle + (float)Math.PI / 2) * 180f / (float)Math.PI; // Gauntlet rotation is degrees; point inward.
                IsVisible = true;
                string text = FormatHint(name, remaining);
                if (text != _hintText)
                {
                    WriteDiagnostic(string.Format(CultureInfo.InvariantCulture,
                        "season={0}; day={1}/{2}; daysRemaining={3}; x={4:0.###}; y={5:0.###}; rotationDegrees={6:0.###}; radius={7:0.###}; source={8}",
                        name, day + 1, length, remaining, PositionXOffset, PositionYOffset,
                        Rotation, Radius, GetType().Assembly.GetName().Name == "AocMapBarPreviewHost" ? "preview" : "campaign"));
                    if (_hovering && _hint != null) _hint.ExecuteEndHint();
                    _hintText = text;
                    _hint = new HintViewModel(new TextObject(text), null);
                    if (_hovering) _hint.ExecuteBeginHint();
                }
                // Treat the complete assembly as one hover target, including
                // child widgets. Do not require the mouse to hit the tiny arrow.
                // The dial keeps its native click command, but not a competing hint.
                if (_seasonCrown == null && ParentWidget != null)
                    _seasonCrown = ParentWidget.FindChild("SeasonCrown", true);
                if (_seasonDial == null && ParentWidget != null)
                    _seasonDial = ParentWidget.FindChild("MapTimeDialButton", true);
                UpdateHover(IsHovered || IsHoveredWithin(_seasonCrown) || IsHoveredWithin(_seasonDial));
            }
            catch (Exception exception)
            {
                // Reflection/native UI boundary: hide rather than display guessed dates.
                _failed = true;
                HidePointer();
                Trace.WriteLine("AOC season pointer hidden after calendar/UI failure: " + exception);
                WriteDiagnostic("ERROR: indicator hidden; " + exception);
            }
        }

        internal static string FormatHint(string seasonName, int daysRemaining)
        {
            return seasonName + ": " + daysRemaining.ToString(CultureInfo.InvariantCulture)
                + (daysRemaining == 1 ? " day remaining" : " days remaining");
        }

        private static bool IsHoveredWithin(Widget widget)
        {
            if (widget == null || !widget.IsVisible) return false;
            if (widget.IsHovered) return true;
            for (int index = 0; index < widget.ChildCount; index++)
                if (IsHoveredWithin(widget.GetChild(index))) return true;
            return false;
        }

        private void UpdateHover(bool hovering)
        {
            if (_hovering == hovering) return;
            _hovering = hovering;
            if (_hint == null) return;
            if (hovering) _hint.ExecuteBeginHint();
            else _hint.ExecuteEndHint();
        }

        private void HidePointer()
        {
            IsVisible = false;
            if (_hovering && _hint != null) _hint.ExecuteEndHint();
            _hovering = false;
        }

        // Opt-in file diagnostics only. One record on first display and each changed
        // season-day, including rewind/load; no per-frame disk writes. I/O failure
        // disables logging for this widget, never the indicator or campaign.
        private void WriteDiagnostic(string message)
        {
            if (_diagnosticWriteFailed) return;
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AOC Diagnostics");
                if (!File.Exists(Path.Combine(directory, "season-indicator.enabled"))) return;
                string path = Path.Combine(directory, "season-indicator.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1048576)
                    File.Copy(path, Path.Combine(directory, "season-indicator.previous.log"), true);
                if (File.Exists(path) && new FileInfo(path).Length > 1048576)
                    File.WriteAllText(path, string.Empty);
                File.AppendAllText(path, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch (IOException error) { DisableDiagnostic(error); }
            catch (UnauthorizedAccessException error) { DisableDiagnostic(error); }
            catch (System.Security.SecurityException error) { DisableDiagnostic(error); }
        }

        private void DisableDiagnostic(Exception error)
        {
            _diagnosticWriteFailed = true;
            Trace.WriteLine("AOC season indicator diagnostic logging disabled: " + error);
        }

        private void ResolveCalendar()
        {
            if (_resolutionAttempted) return;
            _resolutionAttempted = true;
            Assembly approved = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == "AgesOfCalradia") { approved = assembly; break; }
            if (approved == null) throw new InvalidOperationException("Approved calendar assembly is not loaded.");
            Type math = approved.GetType("TwelveMonthCalendar.CalendarTimeMath", true);
            Type settings = approved.GetType("TwelveMonthCalendar.CalendarSettingsState", true);
            _season = RequireMethod(math, "GetSeason", typeof(CampaignTime));
            _year = RequireMethod(math, "GetSeasonYear", typeof(CampaignTime));
            _day = RequireMethod(math, "GetDayOfSeason", typeof(CampaignTime));
            _length = RequireMethod(math, "GetSeasonLength", typeof(int), typeof(int));
            _seasonName = RequireMethod(settings, "GetSeasonName", typeof(int));
        }

        private static MethodInfo RequireMethod(Type type, string name, params Type[] arguments)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic, null, arguments, null);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}
