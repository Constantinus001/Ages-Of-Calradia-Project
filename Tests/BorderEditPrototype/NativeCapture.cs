using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace Aoc.BorderEditPrototype
{
    internal sealed class Face
    {
        internal Vec3 A, B, C;
        internal Vec2 U, V, W;
        internal uint Color;
        internal void Emit(Mesh mesh, UIntPtr handle) => mesh.AddTriangle(A,B,C,U,V,W,Color,handle);
    }
    internal sealed class CapturedEdge
    {
        internal int Id;
        internal Vec2 A, B;
        internal Mesh Mesh;
        internal readonly List<Face> Ribbon = new List<Face>();
    }
    internal sealed class CapturedCap
    {
        internal int Node, Edge, Row;
        internal Vec2 Position;
        internal readonly List<Face> Faces = new List<Face>();
    }
    internal sealed class CapturedRow
    {
        internal GameEntity Original;
        internal Material Material;
        internal readonly List<CapturedEdge> Edges = new List<CapturedEdge>();
        internal readonly List<CapturedCap> Caps = new List<CapturedCap>();
    }

    // Read-only capture of accepted protected-renderer geometry. Never activates the optimizer.
    internal sealed class NativeCapture
    {
        internal object Builder;
        internal Scene Scene;
        internal List<GameEntity> TakenEntities;
        internal readonly List<CapturedRow> Rows = new List<CapturedRow>();
        internal readonly List<CapturedEdge> Edges = new List<CapturedEdge>();
        internal readonly List<CapturedCap> Caps = new List<CapturedCap>();
        internal readonly Dictionary<int, CapturedCap> CapByNode = new Dictionary<int, CapturedCap>();
        internal BorderGraph Graph;
        internal string Signature;
        internal string TopologySignature;
        internal string[] TopologyTokens;
        internal string RendererSignature;
        internal bool InAdvance;
        internal bool CapturingSegment => InAdvance && _edge != null;
        private CapturedEdge _edge;
        private CapturedCap _cap;
        private readonly Dictionary<Vec2,CapturedCap> _segmentCaps=new Dictionary<Vec2,CapturedCap>();
        private int _faceCount;

        internal void BeginSegment(Mesh mesh, Vec2 a, Vec2 b)
        { if (InAdvance) { _segmentCaps.Clear(); _edge = new CapturedEdge { Id=Edges.Count, Mesh=mesh, A=a, B=b }; } }
        internal void BeginCap(Vec2 position)
        {
            if(_edge==null) return;
            // Both half-cap invocations belong to one native endpoint cap.
            if(!_segmentCaps.TryGetValue(position,out _cap))
            { _cap=new CapturedCap { Position=position, Edge=_edge.Id }; _segmentCaps.Add(position,_cap); Caps.Add(_cap); }
        }
        internal void EndCap()
        { _cap = null; }
        internal void Triangle(Mesh mesh, Vec3 a, Vec3 b, Vec3 c, Vec2 u, Vec2 v, Vec2 w, uint color)
        {
            if (!InAdvance || _edge == null || !ReferenceEquals(_edge.Mesh, mesh)) return;
            if (++_faceCount > 3000000) throw new InvalidOperationException("Border capture exceeds prototype memory bound.");
            var face = new Face { A=a, B=b, C=c, U=u, V=v, W=w, Color=color };
            if (_cap != null) _cap.Faces.Add(face); else _edge.Ribbon.Add(face);
        }
        internal void EndSegment(bool accepted)
        {
            if (_edge == null) return;
            if (accepted)
            {
                if (_edge.Ribbon.Count != 8) throw new InvalidOperationException("Unsupported native border ribbon layout: faces="+_edge.Ribbon.Count+"; caps="+Caps.Count+"; edge="+_edge.Id+".");
                Edges.Add(_edge);
            }
            else if (_edge.Ribbon.Count > 0) throw new InvalidOperationException("Native border emitted partial rejected geometry.");
            _edge = null; _cap = null;
        }
        internal void AddRow(object builder, Scene scene, Mesh mesh)
        {
            if (!InAdvance || !ReferenceEquals(Builder,builder)) return;
            Scene = scene;
            var entities = (List<GameEntity>)NativeBindings.BuilderEntities.GetValue(builder);
            var row = new CapturedRow { Original=entities[entities.Count-1], Material=mesh.GetMaterial() };
            row.Edges.AddRange(Edges.Where(e=>ReferenceEquals(e.Mesh,mesh)));
            var ids = new HashSet<int>(row.Edges.Select(e=>e.Id));
            foreach (CapturedCap cap in Caps.Where(c=>ids.Contains(c.Edge))) { cap.Row=Rows.Count; row.Caps.Add(cap); }
            if (row.Edges.Count == 0) throw new InvalidOperationException("Uncaptured native border row.");
            Rows.Add(row);
        }
        internal void Complete()
        {
            if (TakenEntities == null || TakenEntities.Count != Rows.Count || Rows.Count == 0)
                throw new InvalidOperationException("Native frontier row mapping is incomplete.");
            var nodes = new Dictionary<string,int>(); var points = new List<Point2>();
            Func<Vec2,int> node = p =>
            {
                string key = PointKey(p);
                int id; if (!nodes.TryGetValue(key,out id)) { id=points.Count; nodes.Add(key,id); points.Add(new Point2(p.x,p.y)); } return id;
            };
            var edges = new BorderEdge[Edges.Count];
            for (int r=0; r<Rows.Count; r++)
            {
                if (!ReferenceEquals(TakenEntities[r],Rows[r].Original)) throw new InvalidOperationException("Native row order changed.");
                foreach (CapturedEdge edge in Rows[r].Edges)
                    edges[edge.Id] = new BorderEdge { A=node(edge.A), B=node(edge.B), Row=r, Left=edge.Ribbon[0].Color, Right=edge.Ribbon[4].Color };
            }
            foreach (CapturedCap cap in Caps) { cap.Node=node(cap.Position); if (CapByNode.ContainsKey(cap.Node)) throw new InvalidOperationException("Duplicate native cap identity at "+cap.Position+"; previous="+CapByNode[cap.Node].Position+"."); CapByNode.Add(cap.Node,cap); }
            Graph = new BorderGraph(points.ToArray(),edges);
            var signature = new StringBuilder(NativeBindings.RendererHash);
            // Saved-layout identity deliberately excludes terrain-dependent
            // ribbon Z. The protected renderer hash and ordered native nodes
            // identify this map; row/edge data remains in RendererSignature
            // for diagnostics because it cannot change independently here.
            foreach (Point2 p in points) signature.Append('|').Append((int)Math.Round(p.X*1000f)).Append(',').Append((int)Math.Round(p.Y*1000f));
            Signature=DraftStore.Hash(signature.ToString());
            TopologyFingerprint topology = TopologyFingerprint.Create(NativeBindings.RendererHash,points.ToArray(),edges);
            TopologySignature=topology.Signature;
            TopologyTokens=topology.Tokens;
            foreach (BorderEdge e in edges) signature.Append('|').Append(e.A).Append(',').Append(e.B).Append(',').Append(e.Row).Append(',').Append(e.Left).Append(',').Append(e.Right);
            foreach (CapturedEdge e in Edges) foreach (Face f in e.Ribbon)
                signature.Append('|').Append(f.A.z.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(f.B.z.ToString("R",CultureInfo.InvariantCulture)).Append(',').Append(f.C.z.ToString("R",CultureInfo.InvariantCulture));
            RendererSignature=DraftStore.Hash(signature.ToString());
            foreach (CapturedEdge edge in Edges) edge.Mesh=null; // Do not retain native mesh handles beyond source ownership.
        }
        // Match the protected FrontierPointKey IL: float multiplication BEFORE
        // conversion to double/Math.Round. Double multiplication merges different native keys.
        internal static string PointKey(Vec2 p) => ((int)Math.Round(p.x*1000f)).ToString(CultureInfo.InvariantCulture)+":"+((int)Math.Round(p.y*1000f)).ToString(CultureInfo.InvariantCulture);
    }
}
