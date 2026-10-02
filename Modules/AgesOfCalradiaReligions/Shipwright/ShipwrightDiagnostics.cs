using System;
using System.IO;

namespace AgesOfCalradiaReligions.Shipwright
{
    internal static class ShipwrightDiagnostics
    {
        private const long MaximumLogBytes = 2 * 1024 * 1024;
        private static readonly object Sync = new object();
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mount and Blade II Bannerlord",
            "Logs");
        internal static readonly string LogPath = Path.Combine(LogDirectory, "AgesOfCalradiaReligions.Shipwright.log");

        internal static void Info(string message) { Write("INFO", message, null); }
        internal static void Warning(string message) { Write("WARN", message, null); }
        internal static void Error(string message, Exception exception) { Write("ERROR", message, exception); }

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RotateIfNeeded();
                    string details = exception == null ? string.Empty : Environment.NewLine + exception;
                    File.AppendAllText(
                        LogPath,
                        string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}{4}", DateTime.Now, level, message, details, Environment.NewLine));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaximumLogBytes)
            {
                return;
            }

            string previousPath = LogPath + ".previous";
            if (File.Exists(previousPath))
            {
                File.Delete(previousPath);
            }

            File.Move(LogPath, previousPath);
        }
    }
}
