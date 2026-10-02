using System;
using System.IO;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    // Reads only Core's published-layout directory. Generated cache replay is
    // unsafe when this layout is active because it would replace its live rows.
    internal static class PublishedBorderLayoutPresence
    {
        internal static bool IsPresent()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(
                    typeof(PublishedBorderLayoutPresence).Assembly.Location);
                DirectoryInfo shippingDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
                    ? null : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = shippingDirectory == null
                    ? null : shippingDirectory.Parent;
                if (moduleDirectory == null) return false;
                string authoredBorders = Path.Combine(moduleDirectory.FullName, "AuthoredBorders");
                if (!Directory.Exists(authoredBorders)) return false;
                int layoutCount = 0;
                foreach (string path in Directory.GetFiles(authoredBorders, "*.xml"))
                    if (!string.Equals(Path.GetFileName(path), "Repair.xml",
                        StringComparison.OrdinalIgnoreCase)) layoutCount++;
                return layoutCount == 1;
            }
            catch (Exception exception)
            {
                BorderOptimizerDiagnostics.Error(
                    "Published-layout detection failed; normal optimizer cache behavior remains active.",
                    exception);
                return false;
            }
        }
    }
}
