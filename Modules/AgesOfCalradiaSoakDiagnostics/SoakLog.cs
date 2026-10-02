using System;
using System.Globalization;
using System.IO;

namespace AgesOfCalradia.SoakDiagnostics
{
    internal static class SoakLog
    {
        private static readonly object SyncRoot = new object();
        private static string _directory;
        private static string _eventsPath;

        internal static string DirectoryPath
        {
            get
            {
                EnsurePaths();
                return _directory;
            }
        }

        internal static string ControlPath
        {
            get { return Path.Combine(DirectoryPath, "AocSoakControl.txt"); }
        }

        internal static string CheckpointRequestPath
        {
            get { return Path.Combine(DirectoryPath, "AocSoakCheckpoint.request"); }
        }

        internal static string DeadlinePath
        {
            get { return Path.Combine(DirectoryPath, "AocSoakDeadline.txt"); }
        }

        // Created by the launcher for a fresh, explicit campaign-day target.
        // It is deliberately separate from the persisted control receipt.
        internal static string TargetDaysPath
        {
            get { return Path.Combine(DirectoryPath, "AocSoakTargetDays.txt"); }
        }

        internal static void Initialize()
        {
            EnsurePaths();
        }

        internal static void Write(string eventName, string details)
        {
            try
            {
                EnsurePaths();
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + "\t" + Clean(eventName)
                    + "\t" + Clean(details);
                lock (SyncRoot)
                {
                    File.AppendAllText(_eventsPath, line + Environment.NewLine);
                    if (eventName.StartsWith("PEACE_", StringComparison.Ordinal) || eventName == "WAR_DIAGNOSTIC")
                        File.AppendAllText(Path.Combine(_directory, "AocPeaceDiagnostics.tsv"), line + Environment.NewLine);
                }
            }
            catch
            {
                // Diagnostics must never destabilize campaign simulation.
            }
        }

        private static void EnsurePaths()
        {
            if (!string.IsNullOrEmpty(_eventsPath)) return;
            lock (SyncRoot)
            {
                if (!string.IsNullOrEmpty(_eventsPath)) return;
                _directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord",
                    "AgesOfCalradiaSoakDiagnostics");
                Directory.CreateDirectory(_directory);
                _eventsPath = Path.Combine(_directory, "AocSoakEvents.tsv");
            }
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty)
                .Replace('\t', ' ')
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }
    }
}
