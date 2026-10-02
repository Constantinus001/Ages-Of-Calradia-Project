using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace CalradiaCampaignClock
{
    internal static class CampaignClockSettings
    {
        internal const bool DefaultUse24HourClock = false;
        internal const bool DefaultShowMeridiemOnSecondLine = true;

        internal static bool Use24HourClock { get; private set; } = DefaultUse24HourClock;
        internal static bool ShowMeridiemOnSecondLine { get; private set; } = DefaultShowMeridiemOnSecondLine;

        internal static void Load()
        {
            Use24HourClock = DefaultUse24HourClock;
            ShowMeridiemOnSecondLine = DefaultShowMeridiemOnSecondLine;

            string path = GetSettingsPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                CampaignClockDiagnostics.Info(
                    "Settings file was not found; using the default 12-hour, two-line clock.");
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(path);
                XmlElement root = document.DocumentElement;
                if (root == null || !string.Equals(root.Name, "CampaignClockSettings", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The settings root must be CampaignClockSettings.");
                }

                Use24HourClock = ReadBoolean(
                    root,
                    "Use24HourClock",
                    DefaultUse24HourClock);
                ShowMeridiemOnSecondLine = ReadBoolean(
                    root,
                    "ShowMeridiemOnSecondLine",
                    DefaultShowMeridiemOnSecondLine);
                CampaignClockDiagnostics.Info(
                    "Settings loaded. Use24HourClock=" + Use24HourClock
                    + "; ShowMeridiemOnSecondLine=" + ShowMeridiemOnSecondLine + ".");
            }
            catch (Exception exception)
            {
                Use24HourClock = DefaultUse24HourClock;
                ShowMeridiemOnSecondLine = DefaultShowMeridiemOnSecondLine;
                CampaignClockDiagnostics.Error(
                    "Settings could not be read; defaults were restored.",
                    exception);
            }
        }

        private static string GetSettingsPath()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            DirectoryInfo outputDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
                ? null
                : new DirectoryInfo(assemblyDirectory);
            DirectoryInfo binDirectory = outputDirectory == null
                ? null
                : outputDirectory.Parent;
            DirectoryInfo moduleDirectory = binDirectory == null
                ? null
                : binDirectory.Parent;
            return moduleDirectory == null
                ? null
                : Path.Combine(moduleDirectory.FullName, "CalradiaCampaignClock.settings.xml");
        }

        private static bool ReadBoolean(XmlElement root, string name, bool fallback)
        {
            string raw = root.GetAttribute(name);
            bool value;
            return bool.TryParse(raw, out value) ? value : fallback;
        }
    }
}
