using System;
using System.Collections.Generic;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    internal static class PoliticalBorderGeneratedSnapshotExporter
    {
        internal static void TryWrite(IList<PoliticalBorderGeometryCache.RawSegment> segments)
        {
            try
            {
                List<GeneratedBorderChain> output = new List<GeneratedBorderChain>();
                List<PoliticalBorderChainBuilder.Chain> chains = PoliticalBorderChainBuilder.Build(segments);
                for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
                {
                    PoliticalBorderChainBuilder.Chain source = chains[chainIndex];
                    if (source.Segments.Count == 0) continue;
                    GeneratedBorderChain chain = new GeneratedBorderChain { Id = "generated-" + chainIndex.ToString("D5"), Closed = source.Closed };
                    for (int index = 0; index < source.Segments.Count; index++)
                    {
                        PoliticalBorderChainBuilder.DirectedSegment directed = source.Segments[index];
                        PoliticalBorderGeometryCache.RawSegment segment = segments[directed.SegmentIndex];
                        Vec2 first = directed.Forward ? segment.First : segment.Second;
                        Vec2 second = directed.Forward ? segment.Second : segment.First;
                        if (index == 0) chain.Points.Add(first);
                        chain.Points.Add(second);
                        chain.Coastal = chain.Coastal || PoliticalBorderChainBuilder.IsCoastal(segment);
                    }
                    if (chain.Closed && chain.Points.Count > 2 && (chain.Points[0] - chain.Points[chain.Points.Count - 1]).Length < 0.001f)
                        chain.Points.RemoveAt(chain.Points.Count - 1);
                    output.Add(chain);
                }
                PoliticalBorderGeneratedSnapshot.Save(output);
                BorderOptimizerDiagnostics.Info("Generated-border editor snapshot written: chains=" + output.Count + "; segments=" + segments.Count + ".");
            }
            catch (Exception exception)
            {
                BorderOptimizerDiagnostics.Error("Generated-border editor snapshot could not be written; political rendering continues.", exception);
            }
        }
    }
}
