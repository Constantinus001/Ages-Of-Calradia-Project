using System;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Context for the already installed SupplyCashObserver boundaries. The END
    // receipt controls attribution: skipped originals must never become native
    // payouts. No new targets or additional game queries. Context is not itself
    // additive cash flow. Verified by Verify-WorkshopCashBoundaries.ps1.
    internal static class CashPurposeContext
    {
        internal sealed class Frame { internal Frame Previous; internal string Id, Purpose; }
        [ThreadStatic] private static Frame _current;
        internal static bool HasOpenScope { get { return _current != null; } }
        internal static string Context { get { return "; cashPurposeOperation=" + (_current?.Id ?? "none"); } }
        internal static void Reset() { _current = null; }
        internal static Frame Begin(string purpose)
        {
            var frame = new Frame { Previous = _current, Id = Guid.NewGuid().ToString("N"), Purpose = purpose };
            _current = frame;
            SupplyCapture.Write("CASH_PURPOSE_BEGIN", 0, 0, "cash_boundary", purpose, 0, 0,
                "operation=" + frame.Id + "; parentOperation=" + (frame.Previous?.Id ?? "none"));
            return frame;
        }
        internal static void End(Frame frame, bool ran, Exception error)
        {
            if (frame == null) return;
            SupplyCapture.Write("CASH_PURPOSE_END", 0, 0, "cash_boundary", frame.Purpose, 0, 0,
                "operation=" + frame.Id + "; parentOperation=" + (frame.Previous?.Id ?? "none")
                + "; originalRan=" + ran + "; error=" + (error == null ? "none" : error.GetType().FullName)
                + "; context_not_exclusive_cause; not_additive_cash");
        }
        internal static void Restore(Frame frame) { if (frame != null) _current = frame.Previous; }
    }
}
