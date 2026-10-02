using System;
using AgesOfCalradia.PoliticalFillAlphaFix;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal static class FillAlphaContractTests
    {
        private static int _checks;
        private static void Check(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException(reason); _checks++; }
        // Minimal executable model of approved behavior lines 284-318. The
        // comparison defect and native visibility rules are deliberately kept.
        private sealed class PublicationModel
        {
            internal float Requested, ActualAlpha;
            internal bool Visible;
            internal bool HasEntities = true;
            internal int AlphaWrites;
            internal readonly FillAlphaPublicationTracker Tracker;
            internal PublicationModel(bool corrected)
            { if (corrected) Tracker = new FillAlphaPublicationTracker(); }
            internal void Request(float alpha, bool force = false, bool runOriginal = true,
                bool nativeSucceeded = true)
            {
                float target = Math.Max(0f, Math.Min(1f, alpha));
                bool update = force || Math.Abs(target - Requested) > 0.002f;
                Requested = target;
                bool visible = target > 0.001f, visibilityChanged = visible != Visible;
                if (Tracker != null) update |= Tracker.NeedsPublication(target, force, visibilityChanged);
                if ((!update && !visibilityChanged) || !runOriginal || !nativeSucceeded) return;
                if (visible && HasEntities) { ActualAlpha = target; AlphaWrites++; }
                Visible = visible;
                if (Tracker != null && visible && HasEntities) Tracker.CommitPublished(target);
            }
        }
        private static void Main()
        {
            var old = new PublicationModel(false); var corrected = new PublicationModel(true);
            foreach (float alpha in new[] { 0.997f, 0.998f, 0.999f, 1f, 1f, 1f })
            { old.Request(alpha); corrected.Request(alpha); }
            Check(old.Requested == 1f && old.ActualAlpha == 0.997f,
                "Approved request-to-request threshold leaves actual alpha stale at full-zoom plateau.");
            Check(corrected.Requested == 1f && corrected.ActualAlpha == 1f,
                "Published-state comparison and endpoint handling reach exact opacity one.");
            int writes = corrected.AlphaWrites;
            for (int i = 0; i < 100; i++) corrected.Request(1f);
            Check(corrected.AlphaWrites == writes, "Stable exact plateau does not repeat native writes.");
            old = new PublicationModel(false); corrected = new PublicationModel(true);
            for (int step = 0; step <= 1000; step++)
            { old.Request(step / 1000f); corrected.Request(step / 1000f); }
            Check(old.ActualAlpha < 0.01f && corrected.ActualAlpha == 1f,
                "Thousands of small requests accumulate against last applied value.");
            old.Request(1f, true);
            Check(old.ActualAlpha == 1f, "Approved forced publication during replacement resynchronizes output.");
            corrected.Request(0f);
            Check(!corrected.Visible && corrected.Tracker.LastPublished == 1f && corrected.ActualAlpha == 1f,
                "Zero target hides natively without inventing an actual SetAlpha(0) write.");
            corrected.Request(0.0011f);
            Check(corrected.Visible && corrected.ActualAlpha == 0.0011f,
                "Visibility transition publishes sub-threshold alpha.");
            corrected.Request(0.001f);
            Check(!corrected.Visible, "Approved visibility cutoff remains exact.");
            var policy = new FillAlphaPublicationTracker(); policy.CommitPublished(0.5f);
            Check(!policy.NeedsPublication(0.501f, false, false)
                && policy.NeedsPublication(0.503f, false, false), "Cumulative drift triggers; small drift coalesces.");
            Check(policy.LastPublished == 0.5f && policy.NeedsPublication(0.503f, false, false),
                "A failed or skipped native operation does not advance last published state.");
            policy.CommitPublished(0.9995f);
            Check(policy.NeedsPublication(1f, false, false), "Endpoint change smaller than threshold still publishes.");
            policy.CommitPublished(1f); policy.Invalidate();
            Check(policy.NeedsPublication(1f, false, false), "Fresh replacement entities require publication at same alpha.");
            Check(new FillAlphaPublicationTracker().NeedsPublication(1f, false, false),
                "Each behavior/load has independent publication state.");
            policy.CommitPublished(1f);
            Check(!policy.NeedsPublication(float.NaN, true, true)
                && !policy.NeedsPublication(float.PositiveInfinity, true, true), "Nonfinite requests are not published.");
            Check(policy.NeedsPublication(1f, true, false), "Explicit rebuild force is preserved.");
            var empty = new PublicationModel(true) { HasEntities = false };
            empty.Request(0.6f, true);
            Check(!empty.Tracker.HasPublished && empty.AlphaWrites == 0, "Empty fill cannot establish applied state.");
            empty.HasEntities = true; empty.Request(0.6f, true);
            Check(empty.Tracker.LastPublished == 0.6f && empty.AlphaWrites == 1,
                "Forced new entity publication succeeds at identical request.");
            var skipped = new PublicationModel(true); skipped.Request(0.8f, true, false);
            Check(!skipped.Tracker.HasPublished && skipped.AlphaWrites == 0, "Skipped original is not committed.");
            skipped.Request(0.8f, true, true, false);
            Check(!skipped.Tracker.HasPublished && skipped.AlphaWrites == 0, "Native failure is not committed.");
            skipped.Request(0.8f);
            Check(skipped.Tracker.LastPublished == 0.8f && skipped.AlphaWrites == 1,
                "An unchanged request retries after failed publication.");
            Console.WriteLine("Fill alpha publication: " + _checks + " behavioral checks passed.");
        }
    }
}
