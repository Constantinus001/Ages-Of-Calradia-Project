using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.WorkshopProcurement
{
    // Read-only integration with the SAME per-recipe base used by RunTownWorkshop.
    // No independent calendar math, native patches, RNG, or production mutation.
    // Missing/incompatible calendar bridge fails only new-order planning; existing
    // saved cargo remains serviceable. Never guess an unscaled native speed.
    internal static class ProcurementCadence
    {
        private static bool _failureReported;
        internal static double SafeDailyRate(Workshop shop, WorkshopType.Production recipe)
        {
            try
            {
                double result = DailyRate(shop, recipe);
                _failureReported = false;
                return result;
            }
            catch (Exception ex) // Explicit native model/reflection boundary, no cargo mutation.
            {
                if (!_failureReported) ProcurementLog.Write("CADENCE_UNAVAILABLE", ex.ToString());
                _failureReported = true;
                return double.NaN;
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double DailyRate(Workshop shop, WorkshopType.Production recipe)
        {
            return Campaign.Current.Models.WorkshopModel.GetEffectiveConversionSpeedOfProduction(shop, BaseSpeed(recipe), false).ResultNumber;
        }
        internal static float BaseSpeed(WorkshopType.Production recipe)
        {
            var calendar = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(
                a => a.GetName().Name == "AgesOfCalradia.Approved560CalendarFixes");
            if (calendar == null) throw new InvalidOperationException("Authoritative recipe cadence sidecar missing");
            var type = calendar.GetType("AgesOfCalradia.Approved560CalendarFixes.WorkshopRecipeCadenceFix", true);
            var method = type.GetMethod("RecipeBaseSpeed", BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(WorkshopType.Production).MakeByRefType() }, null);
            if (method == null || method.ReturnType != typeof(float))
                throw new MissingMethodException("Authoritative RecipeBaseSpeed signature changed");
            return (float)method.Invoke(null, new object[] { recipe });
        }
    }
}
