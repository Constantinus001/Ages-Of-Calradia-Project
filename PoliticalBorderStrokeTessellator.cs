using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    /// <summary>
    /// Tessellates complete frontier chains with shared cross-sections. Native
    /// terrain and protected land classification are consulted only through the
    /// optimizer runtime boundary and all emitted faces are positively wound.
    /// </summary>
    internal static class PoliticalBorderStrokeTessellator
    {
        internal const float RibbonWidth = 1.8f;
        internal const float CoastalRibbonWidth = 1.6f;
        internal const float CoastalLandWidth = 1.1f;
        internal const float CoastalOuterWidth = 0.5f;
        internal const float FrontierHeight = 5f;
        internal const float CoastalVisibilityClearance = 0f;
        internal const int MinimumCurveSubdivisions = 3;
        internal const int MaximumCurveSubdivisions = 7;
        internal const float MaximumCoastalCurveDeviation = 0.05f;
        internal const float CoastalMaskCoverWidth = CoastalOuterWidth;
        internal const int RoundedCapStepsPerHalf = 4;
        private const float HalfPoliticalWidth = RibbonWidth * 0.5f;
        private const float CoastalHalfPoliticalWidth = CoastalRibbonWidth * 0.5f;
        private const float LandProbeDistance = 0.65f;
        private const float MinimumLength = 0.001f;
        private const float MinimumWidth = 0.015f;
        private const float MinimumSignedTriangleArea = 0.0001f;
        private const float DoubleLineInnerRatio = 0.45f;
        private const int DashCycleSections = 5;
        private const int DashVisibleSections = 3;
        private const int WidthSearchIterations = 9;
        private const int WidthStabilizationPasses = 3;
        private const int MaximumCoastalHeightGapSamples = 12;
        private const float MaximumCoastalHeightGapDistance = 4f;
        private static readonly float[] CoastalLandHeightSearchDistances =
            { CoastalHalfPoliticalWidth * 0.5f,
                CoastalHalfPoliticalWidth * 1.5f, CoastalRibbonWidth };
        private static float _borderWidthScale = 1f;

        internal static float BorderWidthScale { get { return _borderWidthScale; } }
        internal static float EffectiveRibbonWidth
        { get { return RibbonWidth * _borderWidthScale; } }

        internal static void ConfigureWidthScale(float value)
        {
            _borderWidthScale = Math.Max(0.5f, Math.Min(2.5f, value));
        }

        internal static bool TryBuild(
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Result result)
        {
            if (chain == null || chain.Segments.Count == 0) return false;
            List<int> subdivisionCounts;
            List<Sample> samples = BuildCenterSamples(
                chain, source, result, out subdivisionCounts);
            if (samples.Count < 2) return false;
            ComputeSharedNormals(samples, chain.Closed);

            bool coastal = HasCoastalSamples(samples);
            CoastSide coastSide = coastal
                ? ClassifyChainLandSide(samples) : CoastSide.Both;
            if (coastal)
            {
                if (coastSide == CoastSide.Left) result.LeftLandChains++;
                else if (coastSide == CoastSide.Right) result.RightLandChains++;
                else result.AmbiguousCoastalChains++;
                PrepareCoastalLandHeights(samples, coastSide, result);
            }
            InitializeWidths(samples, coastSide);
            StabilizeWidths(samples, result);
            return Emit(samples, chain, source, subdivisionCounts,
                geometry, coastSide, result);
        }

        private static List<Sample> BuildCenterSamples(
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            Result result,
            out List<int> subdivisionCounts)
        {
            subdivisionCounts = new List<int>(chain.Segments.Count);
            List<Sample> samples = new List<Sample>(
                chain.Segments.Count * MaximumCurveSubdivisions + 1);
            for (int chainIndex = 0; chainIndex < chain.Segments.Count; chainIndex++)
            {
                PoliticalBorderChainBuilder.DirectedSegment directed =
                    chain.Segments[chainIndex];
                PoliticalBorderGeometryCache.RawSegment segment =
                    source[directed.SegmentIndex];
                Vec2 first = directed.Forward ? segment.First : segment.Second;
                Vec2 second = directed.Forward ? segment.Second : segment.First;
                float firstHeight = directed.Forward
                    ? segment.FirstTerrainPoint.z : segment.SecondTerrainPoint.z;
                float secondHeight = directed.Forward
                    ? segment.SecondTerrainPoint.z : segment.FirstTerrainPoint.z;
                Vec2 previous = GetPreviousPoint(chain, source, chainIndex, first);
                Vec2 next = GetNextPoint(chain, source, chainIndex, second);
                Vec2 firstTangent = second - previous;
                Vec2 secondTangent = next - first;
                Vec2 direction = second - first;
                float length = direction.Normalize();
                if (length < MinimumLength) return new List<Sample>();
                if (firstTangent.Normalize() < MinimumLength) firstTangent = direction;
                if (secondTangent.Normalize() < MinimumLength) secondTangent = direction;
                bool curve = chain.Segments.Count > 1;
                bool coastal = PoliticalBorderChainBuilder.IsCoastal(segment);
                if (coastal && samples.Count > 0)
                    samples[samples.Count - 1].Coastal = true;
                int subdivisions = curve
                    ? GetSubdivisionCount(direction, firstTangent, secondTangent)
                    : MinimumCurveSubdivisions;
                subdivisionCounts.Add(subdivisions);
                int firstStep = chainIndex == 0 ? 0 : 1;
                for (int step = firstStep; step <= subdivisions; step++)
                {
                    float amount = step / (float)subdivisions;
                    Vec2 position = curve
                        ? Hermite(first, second, firstTangent, secondTangent, length, amount)
                        : first + (second - first) * amount;
                    if (coastal)
                    {
                        Vec2 linear = first + (second - first) * amount;
                        position = ClampDisplacement(
                            linear, position, MaximumCoastalCurveDeviation);
                    }
                    float fallbackHeight = firstHeight
                        + (secondHeight - firstHeight) * amount;
                    samples.Add(new Sample(position, fallbackHeight, coastal));
                }
                if (curve)
                {
                    result.CurvedSegments++;
                    if (coastal) result.CurvedCoastalSegments++;
                }
            }
            return samples;
        }

        private static int GetSubdivisionCount(
            Vec2 direction,
            Vec2 firstTangent,
            Vec2 secondTangent)
        {
            float alignment = Math.Min(
                Vec2.DotProduct(direction, firstTangent),
                Vec2.DotProduct(direction, secondTangent));
            if (alignment < 0.35f) return MaximumCurveSubdivisions;
            if (alignment < 0.80f) return 5;
            return MinimumCurveSubdivisions;
        }

        private static Vec2 GetPreviousPoint(
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            int index,
            Vec2 fallback)
        {
            if (index == 0 && !chain.Closed) return fallback;
            int previousIndex = index == 0 ? chain.Segments.Count - 1 : index - 1;
            PoliticalBorderChainBuilder.DirectedSegment directed = chain.Segments[previousIndex];
            PoliticalBorderGeometryCache.RawSegment segment = source[directed.SegmentIndex];
            return directed.Forward ? segment.First : segment.Second;
        }

        private static Vec2 GetNextPoint(
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            int index,
            Vec2 fallback)
        {
            if (index + 1 == chain.Segments.Count && !chain.Closed) return fallback;
            int nextIndex = index + 1 == chain.Segments.Count ? 0 : index + 1;
            PoliticalBorderChainBuilder.DirectedSegment directed = chain.Segments[nextIndex];
            PoliticalBorderGeometryCache.RawSegment segment = source[directed.SegmentIndex];
            return directed.Forward ? segment.Second : segment.First;
        }

        private static Vec2 Hermite(
            Vec2 first,
            Vec2 second,
            Vec2 firstTangent,
            Vec2 secondTangent,
            float length,
            float amount)
        {
            float squared = amount * amount;
            float cubed = squared * amount;
            float h00 = 2f * cubed - 3f * squared + 1f;
            float h10 = cubed - 2f * squared + amount;
            float h01 = -2f * cubed + 3f * squared;
            float h11 = cubed - squared;
            return first * h00 + firstTangent * length * h10
                + second * h01 + secondTangent * length * h11;
        }

        private static Vec2 ClampDisplacement(
            Vec2 original,
            Vec2 candidate,
            float maximum)
        {
            Vec2 displacement = candidate - original;
            float length = displacement.Normalize();
            return length <= maximum ? candidate : original + displacement * maximum;
        }

        private static void ComputeSharedNormals(List<Sample> samples, bool closed)
        {
            int last = samples.Count - 1;
            for (int index = 0; index < samples.Count; index++)
            {
                if (closed && index == last) continue;
                int previous = index == 0 ? (closed ? last - 1 : 0) : index - 1;
                int next = index == last ? (closed ? 1 : last) : index + 1;
                Vec2 tangent = samples[next].Center - samples[previous].Center;
                if (tangent.Normalize() < MinimumLength && index < last)
                    tangent = samples[index + 1].Center - samples[index].Center;
                tangent.Normalize();
                samples[index].Normal = new Vec2(-tangent.y, tangent.x);
            }
            if (closed) samples[last].Normal = samples[0].Normal;
        }

        private static CoastSide ClassifyChainLandSide(IList<Sample> samples)
        {
            int leftVotes = 0;
            int rightVotes = 0;
            for (int index = 0; index < samples.Count; index++)
            {
                if (!samples[index].Coastal) continue;
                bool left;
                bool right;
                if (!BorderOptimizerRuntime.TryClassifyFrontierLand(
                        samples[index].Center + samples[index].Normal * LandProbeDistance,
                        out left)
                    || !BorderOptimizerRuntime.TryClassifyFrontierLand(
                        samples[index].Center - samples[index].Normal * LandProbeDistance,
                        out right)
                    || left == right) continue;
                if (left) leftVotes++; else rightVotes++;
            }
            int decisive = leftVotes + rightVotes;
            if (decisive == 0) return CoastSide.Both;
            if (leftVotes * 4 >= decisive * 3) return CoastSide.Left;
            if (rightVotes * 4 >= decisive * 3) return CoastSide.Right;
            return CoastSide.Both;
        }

        private static bool HasCoastalSamples(IList<Sample> samples)
        {
            foreach (Sample sample in samples)
                if (sample.Coastal) return true;
            return false;
        }

        private static void InitializeWidths(
            IList<Sample> samples,
            CoastSide coastSide)
        {
            foreach (Sample sample in samples)
            {
                if (!sample.Coastal)
                {
                    sample.LeftWidth = HalfPoliticalWidth * _borderWidthScale;
                    sample.RightWidth = HalfPoliticalWidth * _borderWidthScale;
                }
                else if (coastSide == CoastSide.Left)
                {
                    sample.LeftWidth = CoastalLandWidth * _borderWidthScale;
                    sample.RightWidth = CoastalOuterWidth * _borderWidthScale;
                }
                else if (coastSide == CoastSide.Right)
                {
                    sample.LeftWidth = CoastalOuterWidth * _borderWidthScale;
                    sample.RightWidth = CoastalLandWidth * _borderWidthScale;
                }
                else
                {
                    sample.LeftWidth = CoastalHalfPoliticalWidth * _borderWidthScale;
                    sample.RightWidth = CoastalHalfPoliticalWidth * _borderWidthScale;
                }
            }
        }

        private static void PrepareCoastalLandHeights(
            IList<Sample> samples,
            CoastSide coastSide,
            Result result)
        {
            foreach (Sample sample in samples)
            {
                if (!sample.Coastal) continue;
                result.CoastalSamples++;
                float landHeight;
                bool expanded;
                float preferredSign = coastSide == CoastSide.Right ? -1f : 1f;
                bool found = TryFindCoastalLandHeight(
                    sample, preferredSign, out landHeight, out expanded);
                if (!found && coastSide == CoastSide.Both)
                    found = TryFindCoastalLandHeight(
                        sample, -preferredSign, out landHeight, out expanded);
                if (found)
                {
                    sample.CoastalLandHeight = landHeight;
                    sample.HasCoastalLandHeight = true;
                    result.CoastalLandHeightSamples++;
                    if (expanded) result.CoastalLandHeightExpandedSearchSamples++;
                }
                else
                {
                    result.CoastalLandHeightRejectedSamples++;
                }
            }
            FillCoastalLandHeightGaps(samples, result);
        }

        private static void FillCoastalLandHeightGaps(
            IList<Sample> samples,
            Result result)
        {
            int index = 0;
            while (index < samples.Count)
            {
                if (!samples[index].Coastal
                    || samples[index].HasCoastalLandHeight)
                {
                    index++;
                    continue;
                }
                int firstMissing = index;
                while (index < samples.Count
                    && samples[index].Coastal
                    && !samples[index].HasCoastalLandHeight)
                    index++;
                int missingCount = index - firstMissing;
                int previous = firstMissing - 1;
                int next = index;
                bool hasPrevious = previous >= 0
                    && samples[previous].Coastal
                    && samples[previous].HasCoastalLandHeight;
                bool hasNext = next < samples.Count
                    && samples[next].Coastal
                    && samples[next].HasCoastalLandHeight;
                float gapDistance = hasPrevious && hasNext
                    ? (samples[next].Center - samples[previous].Center).Length
                    : float.MaxValue;
                bool bridge = hasPrevious && hasNext
                    && missingCount <= MaximumCoastalHeightGapSamples
                    && gapDistance <= MaximumCoastalHeightGapDistance;
                bool extendPrevious = hasPrevious && !hasNext
                    && missingCount <= MaximumCoastalHeightGapSamples;
                bool extendNext = !hasPrevious && hasNext
                    && missingCount <= MaximumCoastalHeightGapSamples;
                if (!bridge && !extendPrevious && !extendNext)
                {
                    result.CoastalLandHeightUnresolvedSamples += missingCount;
                    continue;
                }
                for (int missing = 0; missing < missingCount; missing++)
                {
                    Sample sample = samples[firstMissing + missing];
                    if (bridge)
                    {
                        float amount = (missing + 1f) / (missingCount + 1f);
                        sample.CoastalLandHeight = samples[previous].CoastalLandHeight
                            + (samples[next].CoastalLandHeight
                                - samples[previous].CoastalLandHeight) * amount;
                    }
                    else
                    {
                        sample.CoastalLandHeight = extendPrevious
                            ? samples[previous].CoastalLandHeight
                            : samples[next].CoastalLandHeight;
                    }
                    sample.HasCoastalLandHeight = true;
                    result.CoastalLandHeightInterpolatedSamples++;
                }
            }
        }

        private static bool TryFindCoastalLandHeight(
            Sample sample,
            float sign,
            out float height,
            out bool expanded)
        {
            float candidateHeight;
            bool found = TrySampleLandHeight(
                sample, sign, CoastalHalfPoliticalWidth, out height);
            expanded = !found;
            foreach (float distance in CoastalLandHeightSearchDistances)
            {
                if (!TrySampleLandHeight(
                        sample, sign, distance, out candidateHeight)) continue;
                if (!found || candidateHeight > height)
                {
                    height = candidateHeight;
                    expanded = true;
                }
                found = true;
            }
            if (!found) height = 0f;
            return found;
        }

        private static bool TrySampleLandHeight(
            Sample sample,
            float sign,
            float distance,
            out float height)
        {
            Vec2 landward = sample.Center + sample.Normal * (sign * distance);
            bool isLand;
            height = 0f;
            return BorderOptimizerRuntime.TryClassifyFrontierLand(
                    landward, out isLand)
                && isLand
                && BorderOptimizerRuntime.TrySamplePreparedHeight(
                    landward, out height);
        }

        private static void StabilizeWidths(IList<Sample> samples, Result result)
        {
            for (int pass = 0; pass < WidthStabilizationPasses; pass++)
            {
                float[] leftCaps = CaptureWidths(samples, true);
                float[] rightCaps = CaptureWidths(samples, false);
                bool constrained = false;
                for (int section = 0; section + 1 < samples.Count; section++)
                {
                    Sample first = samples[section];
                    Sample second = samples[section + 1];
                    bool leftWindingSafe = IsSideValid(first, second, true);
                    if (!leftWindingSafe)
                    {
                        result.DiagnosticSamples.RecordConstraint(
                            first.Center, second.Center, first.Normal, second.Normal,
                            first.LeftWidth, second.LeftWidth, true, false,
                            leftWindingSafe, true, pass);
                        CapSectionWidths(samples, leftCaps, section, true);
                        result.WindingConstrainedSections++;
                        constrained = true;
                    }
                    bool rightWindingSafe = IsSideValid(first, second, false);
                    if (!rightWindingSafe)
                    {
                        result.DiagnosticSamples.RecordConstraint(
                            first.Center, second.Center, first.Normal, second.Normal,
                            first.RightWidth, second.RightWidth, false, false,
                            rightWindingSafe, true, pass);
                        CapSectionWidths(samples, rightCaps, section, false);
                        result.WindingConstrainedSections++;
                        constrained = true;
                    }
                }
                if (!constrained) return;
                ApplyWidthCaps(samples, leftCaps, rightCaps);
            }
        }

        private static float[] CaptureWidths(IList<Sample> samples, bool left)
        {
            float[] widths = new float[samples.Count];
            for (int index = 0; index < samples.Count; index++)
                widths[index] = left ? samples[index].LeftWidth : samples[index].RightWidth;
            return widths;
        }

        private static void CapSectionWidths(
            IList<Sample> samples,
            float[] caps,
            int section,
            bool left)
        {
            Sample first = samples[section];
            Sample second = samples[section + 1];
            float firstWidth = left ? first.LeftWidth : first.RightWidth;
            float secondWidth = left ? second.LeftWidth : second.RightWidth;
            float scale = FindMaximumSafeScale(first, second, left);
            caps[section] = Math.Min(caps[section], firstWidth * scale);
            caps[section + 1] = Math.Min(caps[section + 1], secondWidth * scale);
        }

        private static float FindMaximumSafeScale(
            Sample first,
            Sample second,
            bool left)
        {
            float firstWidth = left ? first.LeftWidth : first.RightWidth;
            float secondWidth = left ? second.LeftWidth : second.RightWidth;
            float minimumScale = Math.Max(
                MinimumWidth / Math.Max(firstWidth, MinimumWidth),
                MinimumWidth / Math.Max(secondWidth, MinimumWidth));
            if (!IsSectionSafeAtScale(first, second, left, minimumScale))
                return 0f;

            float low = minimumScale;
            float high = 1f;
            for (int iteration = 0; iteration < WidthSearchIterations; iteration++)
            {
                float candidate = (low + high) * 0.5f;
                if (IsSectionSafeAtScale(first, second, left, candidate))
                    low = candidate;
                else
                    high = candidate;
            }
            return low;
        }

        private static bool IsSectionSafeAtScale(
            Sample first,
            Sample second,
            bool left,
            float scale)
        {
            float firstWidth = left ? first.LeftWidth : first.RightWidth;
            float secondWidth = left ? second.LeftWidth : second.RightWidth;
            SetWidth(first, left, firstWidth * scale);
            SetWidth(second, left, secondWidth * scale);
            bool safe = IsSideValid(first, second, left);
            SetWidth(first, left, firstWidth);
            SetWidth(second, left, secondWidth);
            return safe;
        }

        private static void SetWidth(Sample sample, bool left, float width)
        {
            if (left) sample.LeftWidth = width;
            else sample.RightWidth = width;
        }

        private static void ApplyWidthCaps(
            IList<Sample> samples,
            float[] leftCaps,
            float[] rightCaps)
        {
            for (int index = 0; index < samples.Count; index++)
            {
                samples[index].LeftWidth = leftCaps[index];
                samples[index].RightWidth = rightCaps[index];
            }
        }

        private static bool Emit(
            IList<Sample> samples,
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            IList<int> subdivisionCounts,
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            CoastSide coastSide,
            Result result)
        {
            if (subdivisionCounts.Count != chain.Segments.Count) return false;
            int expectedSections = 0;
            foreach (int count in subdivisionCounts) expectedSections += count;
            if (samples.Count != expectedSections + 1) return false;
            int sampleIndex = 0;
            for (int chainIndex = 0; chainIndex < chain.Segments.Count; chainIndex++)
            {
                PoliticalBorderChainBuilder.DirectedSegment directed =
                    chain.Segments[chainIndex];
                PoliticalBorderGeometryCache.RawSegment segment = source[directed.SegmentIndex];
                PoliticalBorderGeometryCache.BorderLineStyle style = segment.Style;
                uint leftColor = directed.Forward ? segment.LeftColor : segment.RightColor;
                uint rightColor = directed.Forward ? segment.RightColor : segment.LeftColor;
                for (int step = 0; step < subdivisionCounts[chainIndex];
                    step++, sampleIndex++)
                {
                    Sample first = samples[sampleIndex];
                    Sample second = samples[sampleIndex + 1];
                    string reason;
                    bool coastal = PoliticalBorderChainBuilder.IsCoastal(segment);
                    if (coastal) result.EmittedCoastalSections++;
                    if (style == PoliticalBorderGeometryCache.BorderLineStyle.Dashed
                        && sampleIndex % DashCycleSections >= DashVisibleSections)
                        continue;
                    if (!TryEmitSide(geometry, first, second, true,
                            leftColor, style, result, out reason))
                    {
                        result.SuppressedUnsafeSections++;
                        result.DiagnosticSamples.RecordSuppression(
                            first.Center, second.Center, first.Normal, second.Normal,
                            first.LeftWidth, second.LeftWidth, true, coastal,
                            directed.SegmentIndex, reason);
                    }
                    if (!TryEmitSide(geometry, first, second, false,
                            rightColor, style, result, out reason))
                    {
                        result.SuppressedUnsafeSections++;
                        result.DiagnosticSamples.RecordSuppression(
                            first.Center, second.Center, first.Normal, second.Normal,
                            first.RightWidth, second.RightWidth, false, coastal,
                            directed.SegmentIndex, reason);
                    }
                }
            }
            PoliticalBorderGeometryCache.RawSegment firstChainSegment =
                source[chain.Segments[0].SegmentIndex];
            if (!chain.Closed
                && firstChainSegment.Style
                    == PoliticalBorderGeometryCache.BorderLineStyle.Solid)
                EmitRoundedEndpointCaps(
                    samples, chain, source, geometry, result);
            result.SharedCrossSections += Math.Max(0, samples.Count - 2);
            return true;
        }

        private static void EmitRoundedEndpointCaps(
            IList<Sample> samples,
            PoliticalBorderChainBuilder.Chain chain,
            IList<PoliticalBorderGeometryCache.RawSegment> source,
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Result result)
        {
            PoliticalBorderChainBuilder.DirectedSegment firstDirected =
                chain.Segments[0];
            PoliticalBorderGeometryCache.RawSegment firstSegment =
                source[firstDirected.SegmentIndex];
            uint firstLeft = firstDirected.Forward
                ? firstSegment.LeftColor : firstSegment.RightColor;
            uint firstRight = firstDirected.Forward
                ? firstSegment.RightColor : firstSegment.LeftColor;
            EmitRoundedEndpointCap(
                samples[0], samples[1], true, firstLeft, firstRight,
                geometry, result);

            int lastSample = samples.Count - 1;
            PoliticalBorderChainBuilder.DirectedSegment lastDirected =
                chain.Segments[chain.Segments.Count - 1];
            PoliticalBorderGeometryCache.RawSegment lastSegment =
                source[lastDirected.SegmentIndex];
            uint lastLeft = lastDirected.Forward
                ? lastSegment.LeftColor : lastSegment.RightColor;
            uint lastRight = lastDirected.Forward
                ? lastSegment.RightColor : lastSegment.LeftColor;
            EmitRoundedEndpointCap(
                samples[lastSample], samples[lastSample - 1], false,
                lastLeft, lastRight, geometry, result);
        }

        private static void EmitRoundedEndpointCap(
            Sample endpoint,
            Sample neighbor,
            bool firstEndpoint,
            uint leftColor,
            uint rightColor,
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Result result)
        {
            Vec2 outward = endpoint.Center - neighbor.Center;
            if (outward.Normalize() < MinimumLength) return;
            float startSign = firstEndpoint ? 1f : -1f;
            Vec2 start = endpoint.Normal * startSign;
            uint firstColor = firstEndpoint ? leftColor : rightColor;
            uint secondColor = firstEndpoint ? rightColor : leftColor;
            int steps = RoundedCapStepsPerHalf * 2;
            Vec3 center = Drape(endpoint.Center, endpoint, result);
            for (int step = 0; step < steps; step++)
            {
                float firstAngle = (float)(Math.PI * step / steps);
                float secondAngle = (float)(Math.PI * (step + 1) / steps);
                Vec2 firstDirection = start * (float)Math.Cos(firstAngle)
                    + outward * (float)Math.Sin(firstAngle);
                Vec2 secondDirection = start * (float)Math.Cos(secondAngle)
                    + outward * (float)Math.Sin(secondAngle);
                float firstAmount = firstAngle / (float)Math.PI;
                float secondAmount = secondAngle / (float)Math.PI;
                float startWidth = firstEndpoint
                    ? endpoint.LeftWidth : endpoint.RightWidth;
                float endWidth = firstEndpoint
                    ? endpoint.RightWidth : endpoint.LeftWidth;
                float firstRadius = startWidth
                    + (endWidth - startWidth) * firstAmount;
                float secondRadius = startWidth
                    + (endWidth - startWidth) * secondAmount;
                Vec3 first = Drape(
                    endpoint.Center + firstDirection * firstRadius,
                    endpoint, result);
                Vec3 second = Drape(
                    endpoint.Center + secondDirection * secondRadius,
                    endpoint, result);
                uint color = step < RoundedCapStepsPerHalf
                    ? firstColor : secondColor;
                if (AddUpwardTriangle(geometry, center, first, second, color))
                    result.RoundedEndpointCapTriangles++;
            }
            result.RoundedEndpointCaps++;
        }

        private static bool AddUpwardTriangle(
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Vec3 first,
            Vec3 second,
            Vec3 third,
            uint color)
        {
            float area = SignedArea(first, second, third);
            if (Math.Abs(area) <= MinimumSignedTriangleArea) return false;
            if (area < 0f)
            {
                Vec3 swap = second;
                second = third;
                third = swap;
            }
            geometry.Triangles.Add(new PoliticalBorderGeometryCache.Triangle(
                first, second, third,
                new Vec2(0.5f, 0.5f), new Vec2(0f, 0f), new Vec2(1f, 0f),
                color));
            return true;
        }

        private static bool TryEmitSide(
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Sample first,
            Sample second,
            bool left,
            uint color,
            PoliticalBorderGeometryCache.BorderLineStyle style,
            Result result,
            out string reason)
        {
            reason = null;
            float firstWidth = left ? first.LeftWidth : first.RightWidth;
            float secondWidth = left ? second.LeftWidth : second.RightWidth;
            if (firstWidth < MinimumWidth || secondWidth < MinimumWidth)
            {
                reason = "width-below-minimum";
                return false;
            }
            Vec3 firstCenter = Drape(first.Center, first, result);
            Vec3 secondCenter = Drape(second.Center, second, result);
            float sign = left ? 1f : -1f;
            Vec2 firstOuter2 = first.Center + first.Normal * (sign * firstWidth);
            Vec2 secondOuter2 = second.Center + second.Normal * (sign * secondWidth);
            Vec3 firstOuter = Drape(firstOuter2, first, result);
            Vec3 secondOuter = Drape(secondOuter2, second, result);
            if (style == PoliticalBorderGeometryCache.BorderLineStyle.Double)
            {
                Vec2 firstInner2 = first.Center + first.Normal
                    * (sign * firstWidth * DoubleLineInnerRatio);
                Vec2 secondInner2 = second.Center + second.Normal
                    * (sign * secondWidth * DoubleLineInnerRatio);
                firstCenter = Drape(firstInner2, first, result);
                secondCenter = Drape(secondInner2, second, result);
            }
            bool added = left
                ? AddUpwardQuad(geometry, firstOuter, firstCenter,
                    secondCenter, secondOuter, color, result)
                : AddUpwardQuad(geometry, firstCenter, firstOuter,
                    secondOuter, secondCenter, color, result);
            if (!added) reason = "invalid-final-winding";
            return added;
        }

        private static bool IsSideValid(Sample first, Sample second, bool left)
        {
            float firstWidth = left ? first.LeftWidth : first.RightWidth;
            float secondWidth = left ? second.LeftWidth : second.RightWidth;
            if (firstWidth < MinimumWidth || secondWidth < MinimumWidth) return false;
            float sign = left ? 1f : -1f;
            Vec2 firstOuter = first.Center + first.Normal * (sign * firstWidth);
            Vec2 secondOuter = second.Center + second.Normal * (sign * secondWidth);
            return left
                ? IsUpwardQuad(firstOuter, first.Center, second.Center, secondOuter)
                : IsUpwardQuad(first.Center, firstOuter, secondOuter, second.Center);
        }

        private static bool IsUpwardQuad(Vec2 first, Vec2 second, Vec2 third, Vec2 fourth)
        {
            bool primary = SignedArea(first, third, fourth) > MinimumSignedTriangleArea
                && SignedArea(first, second, third) > MinimumSignedTriangleArea;
            bool alternate = SignedArea(first, second, fourth) > MinimumSignedTriangleArea
                && SignedArea(second, third, fourth) > MinimumSignedTriangleArea;
            return primary || alternate;
        }

        private static bool AddUpwardQuad(
            PoliticalBorderGeometryCache.EntityGeometry geometry,
            Vec3 first,
            Vec3 second,
            Vec3 third,
            Vec3 fourth,
            uint color,
            Result result)
        {
            bool primary = SignedArea(first, third, fourth) > MinimumSignedTriangleArea
                && SignedArea(first, second, third) > MinimumSignedTriangleArea;
            bool alternate = SignedArea(first, second, fourth) > MinimumSignedTriangleArea
                && SignedArea(second, third, fourth) > MinimumSignedTriangleArea;
            if (!primary && !alternate) return false;
            Vec2 uv0 = new Vec2(0f, 0f);
            Vec2 uv1 = new Vec2(1f, 0f);
            Vec2 uv2 = new Vec2(1f, 1f);
            Vec2 uv3 = new Vec2(0f, 1f);
            if (primary)
            {
                geometry.Triangles.Add(new PoliticalBorderGeometryCache.Triangle(
                    first, third, fourth, uv0, uv2, uv1, color));
                geometry.Triangles.Add(new PoliticalBorderGeometryCache.Triangle(
                    first, second, third, uv0, uv3, uv2, color));
            }
            else
            {
                geometry.Triangles.Add(new PoliticalBorderGeometryCache.Triangle(
                    first, second, fourth, uv0, uv3, uv1, color));
                geometry.Triangles.Add(new PoliticalBorderGeometryCache.Triangle(
                    second, third, fourth, uv3, uv2, uv1, color));
                result.AlternativeDiagonalSections++;
            }
            return true;
        }

        private static Vec3 Drape(
            Vec2 point,
            Sample sample,
            Result result)
        {
            float height;
            if (BorderOptimizerRuntime.TrySamplePreparedHeight(point, out height))
                height += FrontierHeight;
            else
            {
                result.PreparedHeightFallbackVertices++;
                // Raw segment heights represent terrain, so preserve the
                // normal overlay offset even for an isolated snapshot miss.
                height = sample.FallbackHeight + FrontierHeight;
            }
            if (sample.HasCoastalLandHeight)
            {
                float coastalLandLevel = sample.CoastalLandHeight
                    + FrontierHeight + CoastalVisibilityClearance;
                if (height < coastalLandLevel)
                {
                    height = coastalLandLevel;
                    result.CoastalLandHeightLiftedVertices++;
                }
            }
            return new Vec3(point.x, point.y, height, -1f);
        }

        private static float SignedArea(Vec2 first, Vec2 second, Vec2 third)
        {
            return (second.x - first.x) * (third.y - first.y)
                - (second.y - first.y) * (third.x - first.x);
        }

        private static float SignedArea(Vec3 first, Vec3 second, Vec3 third)
        {
            return (second.x - first.x) * (third.y - first.y)
                - (second.y - first.y) * (third.x - first.x);
        }

        private enum CoastSide { Both, Left, Right }

        private sealed class Sample
        {
            internal Sample(Vec2 center, float fallbackHeight, bool coastal)
            { Center = center; FallbackHeight = fallbackHeight; Coastal = coastal; }
            internal readonly Vec2 Center;
            internal readonly float FallbackHeight;
            internal Vec2 Normal;
            internal float LeftWidth, RightWidth;
            internal float CoastalLandHeight;
            internal bool HasCoastalLandHeight;
            internal bool Coastal;
        }

        internal sealed class Result
        {
            internal int CurvedSegments, CurvedCoastalSegments;
            internal int LeftLandChains, RightLandChains, AmbiguousCoastalChains;
            internal int CoastalLandHeightSamples, CoastalLandHeightRejectedSamples;
            internal int CoastalSamples;
            internal int CoastalLandHeightExpandedSearchSamples;
            internal int CoastalLandHeightInterpolatedSamples;
            internal int CoastalLandHeightUnresolvedSamples;
            internal int CoastalLandHeightLiftedVertices;
            internal int RoundedEndpointCaps, RoundedEndpointCapTriangles;
            internal int EmittedCoastalSections;
            internal int SharedCrossSections;
            internal int WidthConstrainedSamples = 0;
            internal int WindingConstrainedSections, SuppressedUnsafeSections;
            internal int UnsafeCoastalSamples = 0;
            internal int PreparedHeightFallbackVertices;
            internal int AlternativeDiagonalSections;
            internal readonly PoliticalBorderRibbonDiagnosticSamples DiagnosticSamples =
                new PoliticalBorderRibbonDiagnosticSamples();
        }
    }
}
