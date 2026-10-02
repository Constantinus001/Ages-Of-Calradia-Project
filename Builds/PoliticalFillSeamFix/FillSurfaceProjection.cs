using System;
using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    // Reprojects ONLY vertices of already accepted fill triangles. Classification,
    // XY coverage, island holes, colors, row IDs and UVs are not inputs to change.
    internal sealed class FillSurfaceProjection
    {
        internal delegate bool HeightQuery(float x, float y, out float height);
        private readonly IList<SeamTriangle> _source;
        private readonly HeightQuery _query;
        private readonly Dictionary<Tuple<float, float>, float> _heights = new Dictionary<Tuple<float, float>, float>();
        internal readonly SeamTriangle[] Output;
        internal int CompletedTriangles { get; private set; }
        internal int QueryCount { get; private set; }
        internal float MinimumDelta { get; private set; } = float.PositiveInfinity;
        internal float MaximumDelta { get; private set; } = float.NegativeInfinity;
        internal bool Complete { get { return CompletedTriangles == Output.Length; } }
        internal FillSurfaceProjection(IList<SeamTriangle> source, HeightQuery query)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _query = query ?? throw new ArgumentNullException(nameof(query));
            Output = new SeamTriangle[source.Count];
        }
        internal void Advance(int maximumTriangles, Func<bool> shouldYield)
        {
            if (maximumTriangles <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTriangles));
            for (int n = 0; n < maximumTriangles && !Complete && !shouldYield(); n++)
            {
                SeamTriangle t = _source[CompletedTriangles];
                SeamVertex a = Project(t.A), b = Project(t.B), c = Project(t.C);
                Output[CompletedTriangles++] = new SeamTriangle(t.ParentId, t.RowId, t.Color, a, b, c);
            }
        }
        private SeamVertex Project(SeamVertex vertex)
        {
            var key = Tuple.Create(vertex.X, vertex.Y);
            float z;
            if (!_heights.TryGetValue(key, out z))
            {
                float height;
                QueryCount++;
                if (!_query(vertex.X, vertex.Y, out height) || float.IsNaN(height) || float.IsInfinity(height))
                    throw new InvalidOperationException("Campaign surface query failed at " + vertex.X + "," + vertex.Y + "; original fill retained.");
                z = height + 3f;
                if (float.IsNaN(z) || float.IsInfinity(z)) throw new InvalidOperationException("Invalid projected height.");
                _heights.Add(key, z);
            }
            float delta = z - vertex.Z;
            MinimumDelta = Math.Min(MinimumDelta, delta); MaximumDelta = Math.Max(MaximumDelta, delta);
            return new SeamVertex(vertex.X, vertex.Y, z, vertex.U, vertex.V);
        }
    }
}
