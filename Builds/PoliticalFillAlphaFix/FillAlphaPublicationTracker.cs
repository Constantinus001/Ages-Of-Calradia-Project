using System;

namespace AgesOfCalradia.PoliticalFillAlphaFix
{
    // Pure policy only. Commit only after a successful visible, nonempty native
    // publication. At zero the approved renderer hides without SetAlpha(0), so
    // leave the prior applied alpha untouched; readiness forces the next show.
    internal sealed class FillAlphaPublicationTracker
    {
        internal const float ChangeThreshold = 0.002f;
        internal bool HasPublished { get; private set; }
        internal float LastPublished { get; private set; } = float.NaN;

        internal bool NeedsPublication(float requested, bool force, bool visibilityChanged)
        {
            if (!Finite(requested)) return false;
            float target = Clamp(requested);
            if (force || visibilityChanged || !HasPublished) return true;
            if ((target == 0f || target == 1f) && target != LastPublished) return true;
            return Math.Abs(target - LastPublished) > ChangeThreshold;
        }
        internal void CommitPublished(float requested)
        {
            if (!Finite(requested)) throw new ArgumentOutOfRangeException("requested");
            LastPublished = Clamp(requested); HasPublished = true;
        }
        internal void Invalidate() { HasPublished = false; LastPublished = float.NaN; }
        private static float Clamp(float value) { return Math.Max(0f, Math.Min(1f, value)); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
