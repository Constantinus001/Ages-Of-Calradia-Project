using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    internal sealed class FillRow
    {
        internal int Id;
        internal int CapturedTriangles;
        internal Mesh Original, Candidate;
        internal GameEntity Entity;
    }
    internal static class NativeMeshTransaction
    {
        private sealed class RowAdapter : IMeshSwapRow
        {
            private readonly FillRow _row;
            internal RowAdapter(FillRow row) { _row = row; }
            public int Id { get { return _row.Id; } }
            public bool HasOriginal { get { return Meshes().Contains(_row.Original.Pointer); } }
            public bool HasCandidate { get { return Meshes().Contains(_row.Candidate.Pointer); } }
            public void AddOriginal() { _row.Entity.AddMesh(_row.Original, true); }
            public void AddCandidate() { _row.Entity.AddMesh(_row.Candidate, true); }
            public bool RemoveOriginal() { return _row.Entity.RemoveComponentWithMesh(_row.Original); }
            public bool RemoveCandidate() { return _row.Entity.RemoveComponentWithMesh(_row.Candidate); }
            private HashSet<UIntPtr> Meshes()
            {
                if (_row.Entity == null) throw new InvalidOperationException("Row entity no longer exists.");
                var result = new HashSet<UIntPtr>();
                int count = _row.Entity.GetComponentCount(GameEntity.ComponentType.MetaMesh);
                if (count > 1024) throw new InvalidOperationException("Unexpected mesh component count.");
                for (int i = 0; i < count; i++)
                {
                    GameEntityComponent component = _row.Entity.GetComponentAtIndex(i, GameEntity.ComponentType.MetaMesh);
                    MetaMesh meta = component.GetFirstMetaMesh();
                    if (meta == null) continue;
                    int meshCount = meta.MeshCount;
                    if (meshCount > 1024) throw new InvalidOperationException("Unexpected component mesh count.");
                    for (int j = 0; j < meshCount; j++) result.Add(meta.GetMeshAtIndex(j).Pointer);
                }
                Mesh first = _row.Entity.GetFirstMesh();
                if (first != null && !result.Contains(first.Pointer))
                    throw new InvalidOperationException("Component enumeration cannot account for first row mesh; do not mutate this entity.");
                return result;
            }
        }
        internal static void Commit(IList<FillRow> rows, Action refreshAlpha)
        {
            var adapters = new List<IMeshSwapRow>();
            foreach (FillRow row in rows)
                if (row.Candidate != null) adapters.Add(new RowAdapter(row));
            MeshSwapTransaction.Commit(adapters, refreshAlpha, NativeFillSeamFix.Log);
        }
        internal static void Emit(Mesh mesh, SeamTriangle triangle, UIntPtr handle)
        {
            Vec3 a = Position(triangle.A), b = Position(triangle.B), c = Position(triangle.C);
            Vec2 ua = Uv(triangle.A), ub = Uv(triangle.B), uc = Uv(triangle.C);
            mesh.AddTriangle(a, b, c, ua, ub, uc, triangle.Color, handle);
            mesh.AddTriangle(a, c, b, ua, uc, ub, triangle.Color, handle);
        }
        private static Vec3 Position(SeamVertex p) { return new Vec3(p.X, p.Y, p.Z); }
        private static Vec2 Uv(SeamVertex p) { return new Vec2(p.U, p.V); }
    }
}

