using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Opt-in live measurement. Owns no save state and never saves or quits.
    internal static class PacingCalibration
    {
        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly int[] Speeds = { 1, 2, 4 };
        private static int _stage;
        private static double _startDay;
        private static bool _started, _completed;
        private static double _warmupSeconds, _lastWarmupDay;
        private static long _lastWarmupTick;
        private static MethodInfo _selectSpeed;
        private static int _waitingHour = -1;
        internal static bool Enabled { get; private set; }

        internal static void Initialize()
        {
            Enabled = File.Exists(Path.Combine(SoakLog.DirectoryPath, "AocPacingCalibration.enabled"));
            _stage = 0; _started = false; _completed = false; Clock.Reset();
            _waitingHour = -1;
            _warmupSeconds = 0; _lastWarmupDay = double.NaN; _lastWarmupTick = 0;
            if (Enabled) SoakLog.Write("PACING_ENABLED", "one campaign day each at 1x, 2x, 4x; no autosave or exit");
        }

        internal static double ExpectedSecondsPerDay(int speed)
        {
            if (speed != 1 && speed != 2 && speed != 4) throw new ArgumentOutOfRangeException("speed");
            return 80d / speed;
        }

        internal static void Tick()
        {
            if (!Enabled || _completed || Campaign.Current == null) return;
            try
            {
                if (_selectSpeed == null)
                {
                    Assembly sidecar = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                        a => a.GetName().Name == "AgesOfCalradia.Approved560CalendarFixes");
                    if (sidecar == null) throw new InvalidOperationException("Pacing sidecar is not loaded.");
                    _selectSpeed = sidecar.GetType("AgesOfCalradia.Approved560CalendarFixes.CampaignSimulationTimeFix", true)
                        .GetMethod("SelectFastForwardMultiplier", BindingFlags.Static | BindingFlags.NonPublic);
                    if (_selectSpeed == null) throw new MissingMethodException("SelectFastForwardMultiplier");
                }
                double now = CampaignTime.Now.ToDays;
                if (_started && now - _startDay >= 1d)
                {
                    Clock.Stop();
                    double span = now - _startDay;
                    SoakLog.Write("PACING_END", "speed=" + Speeds[_stage]
                        + "; elapsedSeconds=" + Clock.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture)
                        + "; days=" + span.ToString("R", CultureInfo.InvariantCulture)
                        + "; secondsPerDay=" + (Clock.Elapsed.TotalSeconds / span).ToString("R", CultureInfo.InvariantCulture)
                        + "; expectedSecondsPerDay=" + ExpectedSecondsPerDay(Speeds[_stage]).ToString("R", CultureInfo.InvariantCulture));
                    _stage++;
                    _started = false;
                    if (_stage == Speeds.Length)
                    {
                        _completed = true;
                        Pause();
                        SoakLog.Write("PACING_COMPLETE", "three measurements recorded; campaign paused; no save or exit requested");
                        return;
                    }
                }
                int speed = Speeds[_stage];
                Campaign.Current.SetTimeControlModeLock(false);
                _selectSpeed.Invoke(null, new object[] { speed == 1 ? 2 : speed });
                Campaign.Current.SpeedUpMultiplier = speed;
                Campaign.Current.TimeControlMode = speed == 1
                    ? CampaignTimeControlMode.UnstoppablePlay : CampaignTimeControlMode.UnstoppableFastForward;
                Campaign.Current.SetTimeControlModeLock(true);
                // Campaign.Current exists before loading has finished. Require
                // five seconds with advancing campaign ticks before measuring.
                // Cap each advancing interval so a load or pause cannot count
                // as the warm-up itself.
                if (_stage == 0 && _warmupSeconds < 5d)
                {
                    long stamp = Stopwatch.GetTimestamp();
                    if (!double.IsNaN(_lastWarmupDay) && now > _lastWarmupDay)
                    {
                        _warmupSeconds += Math.Min(0.25d, (stamp - _lastWarmupTick) / (double)Stopwatch.Frequency);
                        _lastWarmupTick = stamp;
                        _lastWarmupDay = now;
                    }
                    else if (double.IsNaN(_lastWarmupDay))
                    {
                        _lastWarmupDay = now;
                        _lastWarmupTick = stamp;
                    }
                    if (_warmupSeconds < 5d) return;
                    SoakLog.Write("PACING_WARMUP_COMPLETE", "five seconds of observed campaign advancement");
                }
                if (!_started)
                {
                    if (_stage == 0)
                    {
                        int hour = CampaignTime.Now.GetHourOfDay;
                        bool crossedNine = hour == 9 && _waitingHour >= 0 && _waitingHour != 9;
                        if (_waitingHour < 0) SoakLog.Write("PACING_WAIT_0900", "waiting for next in-game 09:00 crossing");
                        _waitingHour = hour;
                        if (!crossedNine) return;
                    }
                    _startDay = now;
                    _started = true;
                    SoakLog.Write("PACING_START", "speed=" + speed + "; day=" + now.ToString("R", CultureInfo.InvariantCulture));
                    Clock.Restart();
                }
            }
            catch (Exception exception)
            {
                _completed = true;
                SoakLog.Write("PACING_FAILURE", exception.ToString());
                Pause();
            }
        }

        private static void Pause()
        {
            Campaign.Current.SetTimeControlModeLock(false);
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
        }
    }
}
