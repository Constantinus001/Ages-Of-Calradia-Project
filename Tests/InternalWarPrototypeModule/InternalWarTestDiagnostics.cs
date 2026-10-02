using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Diagnostics;

namespace AgesOfCalradiaInternalWarsTest
{
    internal static class InternalWarTestDiagnostics
    {
        private static string _logPath;

        internal static void Initialize()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                DirectoryInfo binaryDirectory = string.IsNullOrWhiteSpace(assemblyDirectory) ? null : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = binaryDirectory == null ? null : binaryDirectory.Parent;
                string root = moduleDirectory == null ? AppDomain.CurrentDomain.BaseDirectory : moduleDirectory.FullName;
                string logs = Path.Combine(root, "Logs");
                Directory.CreateDirectory(logs);
                _logPath = Path.Combine(logs, "AgesOfCalradiaInternalWarsTest.log");
            }
            catch (IOException exception) { Trace.WriteLine("Internal-war test logging unavailable: " + exception); }
            catch (UnauthorizedAccessException exception) { Trace.WriteLine("Internal-war test logging unavailable: " + exception); }
        }

        internal static void Info(string message)
        {
            try
            {
                if (!string.IsNullOrEmpty(_logPath))
                    File.AppendAllText(_logPath, string.Format(CultureInfo.InvariantCulture,
                        "{0:O} [INFO] {1}{2}", DateTime.UtcNow, message, Environment.NewLine));
            }
            catch (IOException exception) { Trace.WriteLine("Internal-war test log write failed: " + exception); }
            catch (UnauthorizedAccessException exception) { Trace.WriteLine("Internal-war test log write failed: " + exception); }
        }

        internal static void Error(string message, Exception exception)
        {
            Info("ERROR: " + message + (exception == null ? string.Empty : " " + exception));
        }
    }
}
