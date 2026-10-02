using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AgesOfCalradia.WorkshopProcurement
{
    internal static class ProcurementLog
    {
        internal static void Order(string action, ProcurementOrder order, string details)
        {
            Write(action, "shop=" + order.Key + " source=" + order.Source + " lines="
                + string.Join(",", order.Lines.Select(l => l.Item + ":" + l.Remaining)) + " batchesRemaining=" + order.Quantity
                + " goods=" + order.GoodsCost + " freight=" + order.FreightCost + " due="
                + order.ArrivalDay.ToString("R", CultureInfo.InvariantCulture)
                + " policy=" + (order.PolicyRevision ?? "legacy") + " " + details);
        }
        internal static void Write(string action, string details)
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "AgesOfCalradiaProcurement");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "procurement.log");
                if (File.Exists(path) && new FileInfo(path).Length > 8 * 1024 * 1024)
                    File.Copy(path, path + ".previous", true);
                if (File.Exists(path) && new FileInfo(path).Length > 8 * 1024 * 1024)
                    File.WriteAllText(path, "");
                File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + action + " " + details.Replace("\r", " ").Replace("\n", " ") + Environment.NewLine);
            }
            catch (IOException ex) { System.Diagnostics.Trace.WriteLine("Procurement log unavailable: " + ex); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.WriteLine("Procurement log denied: " + ex); }
        }
    }
}
