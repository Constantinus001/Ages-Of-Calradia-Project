using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Retains a bounded sample of cold-build ribbon failures. This diagnostic
    /// never changes geometry and deliberately avoids per-frame output.
    /// </summary>
    internal sealed class PoliticalBorderRibbonDiagnosticSamples
    {
        internal const int MaximumSamplesPerCategory = 24;
        private readonly List<string> _constraints = new List<string>();
        private readonly List<string> _suppressions = new List<string>();

        internal void RecordConstraint(
            Vec2 first,
            Vec2 second,
            Vec2 firstNormal,
            Vec2 secondNormal,
            float firstWidth,
            float secondWidth,
            bool left,
            bool requireLand,
            bool windingSafe,
            bool landSafe,
            int pass)
        {
            if (_constraints.Count >= MaximumSamplesPerCategory) return;
            _constraints.Add(Format(first, second, firstNormal, secondNormal,
                firstWidth, secondWidth, left,
                "pass=" + pass
                + ",winding=" + windingSafe
                + ",landRequired=" + requireLand
                + ",land=" + landSafe));
        }

        internal void RecordSuppression(
            Vec2 first,
            Vec2 second,
            Vec2 firstNormal,
            Vec2 secondNormal,
            float firstWidth,
            float secondWidth,
            bool left,
            bool coastal,
            int segmentIndex,
            string reason)
        {
            if (_suppressions.Count >= MaximumSamplesPerCategory) return;
            _suppressions.Add(Format(first, second, firstNormal, secondNormal,
                firstWidth, secondWidth, left,
                "segment=" + segmentIndex
                + ",coastal=" + coastal
                + ",reason=" + reason));
        }

        internal void Log()
        {
            BorderOptimizerDiagnostics.Info(
                "Political ribbon constraint samples: sampleCount="
                + _constraints.Count + "; samples="
                + (_constraints.Count == 0 ? "none" : string.Join(" | ", _constraints.ToArray()))
                + ".");
            BorderOptimizerDiagnostics.Info(
                "Political ribbon suppression samples: sampleCount="
                + _suppressions.Count + "; samples="
                + (_suppressions.Count == 0 ? "none" : string.Join(" | ", _suppressions.ToArray()))
                + ".");
        }

        private static string Format(
            Vec2 first,
            Vec2 second,
            Vec2 firstNormal,
            Vec2 secondNormal,
            float firstWidth,
            float secondWidth,
            bool left,
            string detail)
        {
            Vec2 midpoint = (first + second) * 0.5f;
            float normalAlignment = Vec2.DotProduct(firstNormal, secondNormal);
            return string.Format(CultureInfo.InvariantCulture,
                "@({0:F2},{1:F2}),side={2},width={3:F3}/{4:F3},length={5:F3},normalDot={6:F3},{7}",
                midpoint.x, midpoint.y, left ? "L" : "R",
                firstWidth, secondWidth, (second - first).Length,
                normalAlignment, detail);
        }
    }
}
