using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    internal readonly struct SeamVertex
    {
        internal readonly float X, Y, Z, U, V;
        internal SeamVertex(float x, float y, float z, float u, float v)
        { X = x; Y = y; Z = z; U = u; V = v; }
    }

    internal readonly struct SeamTriangle
    {
        internal readonly int ParentId, RowId;
        internal readonly uint Color;
        internal readonly SeamVertex A, B, C;
        internal SeamTriangle(int parentId, int rowId, uint color,
            SeamVertex a, SeamVertex b, SeamVertex c)
        { ParentId = parentId; RowId = rowId; Color = color; A = a; B = b; C = c; }
    }

    internal sealed class SeamBuildResult
    {
        internal readonly List<SeamTriangle> Triangles = new List<SeamTriangle>();
        internal int SourceCount, ChangedParentCount, InsertedBoundaryVertices;
        internal int ValidatedSharedEdgePairs;
        internal float MaximumHeightCorrection;
    }
}
