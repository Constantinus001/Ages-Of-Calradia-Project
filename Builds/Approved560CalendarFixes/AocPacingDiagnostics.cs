using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    /// <summary>Opt-in pacing observations only; never changes simulation timing.</summary>
    internal static class AocPacingDiagnostics
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AOC Diagnostics");
        private static readonly bool Enabled = File.Exists(Path.Combine(DirectoryPath, "pacing.enabled"));
        private static readonly Stopwatch Clock = Enabled ? Stopwatch.StartNew() : null;
        private static bool _failed, _started;
        private static double _wallStart, _dayStart, _inputSum, _maximumInput;
        private static int _ticks;
        private static Campaign _campaign;
        private static CampaignTimeControlMode _mode;
        private static float _speed;

        // Call before changing realDt, after selecting the effective native speed.
        // The enable marker is sampled once at process startup; disabled runs do
        // no clock reads or per-frame filesystem access.
        internal static void Observe(Campaign campaign, float incomingRealDt)
        {
            if (!Enabled || _failed || campaign == null) return;
            try
            {
                double wall = Clock.Elapsed.TotalSeconds;
                double day = CampaignTime.Now.ToDays;
                CampaignTimeControlMode mode = campaign.TimeControlMode;
                float speed = CampaignSimulationTimeFix.IsFastForward(mode) ? campaign.SpeedUpMultiplier : 1f;
                if (mode == CampaignTimeControlMode.Stop || mode == CampaignTimeControlMode.FastForwardStop)
                    speed = 0f;
                if (!_started || !ReferenceEquals(_campaign, campaign))
                {
                    _campaign = campaign;
                    Reset(wall, day, mode, speed);
                }
                else if (mode != _mode || speed != _speed || wall - _wallStart >= 10d)
                {
                    WriteWindow(wall, day);
                    Reset(wall, day, mode, speed);
                }
                _inputSum += incomingRealDt;
                _maximumInput = Math.Max(_maximumInput, incomingRealDt);
                _ticks++;
            }
            catch (Exception exception)
            {
                // Native property/file-system diagnostic boundary. Disable logging
                // after one failure so gameplay is never interrupted or log-flooded.
                _failed = true;
                Trace.WriteLine("AOC pacing diagnostics disabled after failure: " + exception);
            }
        }

        private static void Reset(double wall, double day, CampaignTimeControlMode mode, float speed)
        {
            _started = true;
            _wallStart = wall;
            _dayStart = day;
            _inputSum = 0d;
            _maximumInput = 0d;
            _ticks = 0;
            _mode = mode;
            _speed = speed;
        }

        private static void WriteWindow(double wall, double day)
        {
            if (_ticks == 0) return;
            Directory.CreateDirectory(DirectoryPath);
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0:O} wallSeconds={1:F6} inputDtSum={2:F6} ticks={3} maxInputDt={4:F6} dayDelta={5:F9} mode={6} effectiveSpeed={7:F6}{8}",
                DateTime.UtcNow, wall - _wallStart, _inputSum, _ticks, _maximumInput,
                day - _dayStart, _mode, _speed, Environment.NewLine);
            File.AppendAllText(Path.Combine(DirectoryPath, "pacing.log"), line);
        }
    }
}
