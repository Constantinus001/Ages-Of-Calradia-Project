using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Opt-in raw evidence only: preserve the uninstrumented caller above root
    // money actions without adding invasive patches to every grant/trade system.
    // Stack frames may be inlined or absent; this is caller evidence, not a
    // guaranteed semantic classification. No paths, locals or argument values.
    internal static class EconomyCallerDiagnostics
    {
        internal static bool ShouldCapture(bool raw, bool root, string name)
        {
            return raw && root && (name.StartsWith("Apply") || name == "set_Gold"
                || name == "set_PartyTradeGold" || name == "set_KingdomBudgetWallet"
                || name == "ChangeGold");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static string Capture()
        {
            var frames = new StackTrace(1, false).GetFrames();
            if (frames == null) return "unavailable";
            var names = frames.Select(x => x.GetMethod()).Where(x => x != null && x.DeclaringType != null)
                .Where(x => x.DeclaringType.Assembly != typeof(EconomyCallerDiagnostics).Assembly
                    && !x.DeclaringType.FullName.StartsWith("System.")
                    && !x.DeclaringType.FullName.StartsWith("HarmonyLib."))
                .Take(12).Select(x => x.DeclaringType.FullName + "." + x.Name).ToArray();
            return names.Length == 0 ? "unavailable" : string.Join(" <- ", names);
        }
    }
}
