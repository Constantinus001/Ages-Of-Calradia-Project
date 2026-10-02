using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.WorkshopProcurement
{
    public static class ProcurementCommands
    {
        [CommandLineFunctionality.CommandLineArgumentFunction("procurement_status", "aoc")]
        public static string Status(List<string> arguments)
        {
            return ProcurementBehavior.Current == null ? "No procurement campaign loaded." : ProcurementBehavior.Current.Status();
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("procurement_stop", "aoc")]
        public static string Stop(List<string> arguments)
        {
            return ProcurementBehavior.Current == null ? "No procurement campaign loaded." : ProcurementBehavior.Current.SetOrdering(false);
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("procurement_resume", "aoc")]
        public static string Resume(List<string> arguments)
        {
            return ProcurementBehavior.Current == null ? "No procurement campaign loaded." : ProcurementBehavior.Current.SetOrdering(true);
        }
    }
}
