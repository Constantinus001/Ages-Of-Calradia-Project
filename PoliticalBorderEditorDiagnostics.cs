using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using AgesOfCalradia.PoliticalBorderOverrides;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    internal static class PoliticalBorderEditorDiagnostics
    {
        private static readonly object SyncRoot = new object();
        private static string _path;

        internal static void Initialize()
        {
            try
            {
                string moduleRoot = PoliticalBorderEditorDocument.ResolveModuleRoot();
                string directory = Path.Combine(moduleRoot, "Logs");
                Directory.CreateDirectory(directory);
                _path = Path.Combine(directory, "PoliticalBorderEditor.log");
                Info("Political border editor diagnostics initialized; assembly="
                    + typeof(PoliticalBorderEditorDiagnostics).Assembly.GetName().Version + ".");
            }
            catch
            {
                _path = null;
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
            string path = _path;
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + " [" + level + "] " + message;
                if (exception != null) line += Environment.NewLine + exception;
                lock (SyncRoot) File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Diagnostics must never affect campaign or editor input.
            }
        }
    }
}
