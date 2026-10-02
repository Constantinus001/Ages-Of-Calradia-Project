using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Optional religion boundary: private fields are read, never set; no treaty
    // methods invoked. Absent/changed integration stays explicitly unavailable.
    internal static class TreatyStateDiagnostics
    {
        internal static string Read()
        {
            try
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AgesOfCalradiaReligions.OpeningPeaceBehavior", false)).FirstOrDefault(t => t != null);
                if (type == null) return "treatyState=module_absent";
                MethodInfo getter = typeof(Campaign).GetMethods().Single(m => m.Name == "GetCampaignBehavior" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                object behavior = getter.MakeGenericMethod(type).Invoke(Campaign.Current, null);
                if (behavior == null) return "treatyState=behavior_absent";
                FieldInfo initialized = type.GetField("_initialized", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo end = type.GetField("_endDay", BindingFlags.Instance | BindingFlags.NonPublic);
                if (initialized == null || initialized.FieldType != typeof(bool) || end == null || end.FieldType != typeof(int))
                    return "treatyState=unsupported_fields";
                bool started = (bool)initialized.GetValue(behavior);
                int endDay = (int)end.GetValue(behavior);
                int day = (int)Math.Floor(CampaignTime.Now.ToDays);
                return "treatyState=observed; initialized=" + started + "; active=" + (started && day < endDay)
                    + "; endDay=" + endDay + "; daysLeft=" + (started ? Math.Max(0, endDay - day) : 0);
            }
            catch (Exception ex)
            {
                SoakLog.Write("TREATY_CONTEXT_UNAVAILABLE", ex.GetType().Name + ": " + ex.Message);
                return "treatyState=read_failed";
            }
        }
    }
}
