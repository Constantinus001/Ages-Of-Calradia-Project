using System;
using System.IO;

namespace AgesOfCalradiaLogistics
{
    internal static class LogisticsDiagnostics
    {
        private const long MaximumLogBytes = 2 * 1024 * 1024;
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mount and Blade II Bannerlord", "Logs");
        internal static readonly string LogPath = Path.Combine(LogDirectory, "AgesOfCalradiaLogistics.log");

        internal static void Info(string message) { Write("INFO", message); }
        internal static void Warning(string message) { Write("WARN", message); }
        internal static void Error(string message, Exception exception)
        {
            Write("ERROR", message + " " + (exception == null ? string.Empty : exception.GetType().Name + ": " + exception.Message));
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    File.AppendAllText(LogPath, string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}", DateTime.Now, level, message, Environment.NewLine));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes) return;
            string previousPath = LogPath + ".previous";
            if (File.Exists(previousPath)) File.Delete(previousPath);
            File.Move(LogPath, previousPath);
        }
    }
}
