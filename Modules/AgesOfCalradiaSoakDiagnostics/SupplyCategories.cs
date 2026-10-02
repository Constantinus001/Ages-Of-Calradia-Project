using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem.Extensions;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Resolved once per campaign, including mod-added categories. No model calls.
    internal static class SupplyCategories
    {
        internal static string[] All = new[] { "cow", "sheep", "hog", "wool", "meat", "felt" };
        internal static void Initialize()
        {
            All = ItemCategories.All.Select(x => x.StringId).Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x).ToArray();
        }
    }
}
