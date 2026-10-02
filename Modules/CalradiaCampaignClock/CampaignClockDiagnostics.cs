using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace CalradiaCampaignClock
{
    internal static class CampaignClockDiagnostics
    {
        private static readonly object SyncRoot = new object();
        private static string _logPath;

        internal static void Initialize()
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Mount and Blade II Bannerlord",
                    "Logs");
                Directory.CreateDirectory(directory);
                _logPath = Path.Combine(directory, "CalradiaCampaignClock.log");
                Info("Diagnostics initialized.");
            }
            catch (Exception exception)
            {
                _logPath = null;
                Trace.WriteLine("Calradia Campaign Clock diagnostics initialization failed: " + exception);
            }
        }

        internal static void Info(string message)
        {
            Write("INFO", message, null);
        }

        internal static void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private static void Write(string level, string message, Exception exception)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0:O} [{1}] {2}{3}",
                DateTime.UtcNow,
                level,
                message ?? string.Empty,
                exception == null ? string.Empty : Environment.NewLine + exception);
            Trace.WriteLine(line);

            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            try
            {
                lock (SyncRoot)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
            }
            catch (Exception writeException)
            {
                Trace.WriteLine("Calradia Campaign Clock log write failed: " + writeException);
            }
        }
    }
}
