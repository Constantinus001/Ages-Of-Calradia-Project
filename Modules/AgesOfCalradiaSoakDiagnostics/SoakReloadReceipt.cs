using System;
using System.Globalization;
using System.IO;

namespace AgesOfCalradia.SoakDiagnostics
{
    // External diagnostics receipt, never serialized into a campaign save.
    internal static class SoakReloadReceipt
    {
        private static string ReceiptPath { get { return Path.Combine(SoakLog.DirectoryPath, "AocSoakReloadReceipt.txt"); } }

        internal static void Capture(double day, string campaignId, int gold, double birthDay)
        {
            File.WriteAllLines(ReceiptPath, new[] { "PendingSave", day.ToString("R", CultureInfo.InvariantCulture),
                campaignId, gold.ToString(CultureInfo.InvariantCulture), birthDay.ToString("R", CultureInfo.InvariantCulture) });
        }

        internal static void MarkReady()
        {
            string[] receipt = File.ReadAllLines(ReceiptPath);
            receipt[0] = "ReadyToReload";
            File.WriteAllLines(ReceiptPath, receipt);
        }

        internal static bool Matches(double expectedDay, double actualDay, string expectedId, string actualId,
            int expectedGold, int actualGold, double expectedBirth, double actualBirth)
        {
            return !double.IsNaN(actualDay) && !double.IsInfinity(actualDay)
                && Math.Abs(expectedDay - actualDay) < 0.00001d
                && string.Equals(expectedId, actualId, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(expectedId) && expectedGold == actualGold
                && Math.Abs(expectedBirth - actualBirth) < 0.00001d;
        }

        internal static bool Confirm(double day, string campaignId, int gold, double birthDay, out bool reloaded)
        {
            reloaded = false;
            try
            {
                if (!File.Exists(ReceiptPath)) return true;
                string[] receipt = File.ReadAllLines(ReceiptPath);
                if (receipt.Length == 5 && receipt[0] == "Consumed") return true;
                if (receipt.Length != 5 || receipt[0] != "ReadyToReload"
                    || !Matches(double.Parse(receipt[1], CultureInfo.InvariantCulture), day, receipt[2], campaignId,
                        int.Parse(receipt[3], CultureInfo.InvariantCulture), gold,
                        double.Parse(receipt[4], CultureInfo.InvariantCulture), birthDay))
                {
                    SoakLog.Write("RELOAD_FAILURE", "Saved day/campaign identity/player gold/birth date did not match receipt.");
                    return false;
                }
                receipt[0] = "Consumed";
                File.WriteAllLines(ReceiptPath, receipt);
                reloaded = true;
                return true;
            }
            catch (Exception exception)
            {
                SoakLog.Write("RELOAD_FAILURE", exception.GetType().Name + ": " + exception.Message);
                return false;
            }
        }
    }
}
