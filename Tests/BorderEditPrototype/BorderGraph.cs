using System;
using System.Collections.Generic;
using System.Linq;

namespace Aoc.BorderEditPrototype
{
    internal struct Point2
    {
        internal float X, Y;
        internal Point2(float x, float y) { X = x; Y = y; }
        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator *(Point2 a, float b) => new Point2(a.X * b, a.Y * b);
        internal float Length => (float)Math.Sqrt(X * X + Y * Y);
        internal bool Finite => !float.IsNaN(X) && !float.IsNaN(Y) && !float.IsInfinity(X) && !float.IsInfinity(Y);
        internal static bool Same(Point2 a, Point2 b) => a.X == b.X && a.Y == b.Y;
        internal static float Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
    }

    internal sealed class BorderEdge
    {
        internal int A, B, Row;
        internal uint Left, Right;
        internal bool Authored;
    }

    internal sealed class FillPatch
    {
        internal Point2[] Points;
        internal uint Color;
        internal bool LocalColors = true;
        internal bool ClipToLand = true;
    }

    internal sealed class EditSnapshot
    {
        internal Point2[] Points;
        internal int OriginalPointCount;
        internal FillPatch[] Fills = new FillPatch[0];
        internal bool[] Deleted;
        internal BorderEdge[] Bridges = new BorderEdge[0];
        internal EditSnapshot Copy() => new EditSnapshot { Points = (Point2[])Points.Clone(), OriginalPointCount=OriginalPointCount, Fills=(FillPatch[])Fills.Clone(), Deleted = (bool[])Deleted.Clone(), Bridges = (BorderEdge[])Bridges.Clone() };
    }

    // Pure editing boundary: no scene, input, file or campaign API calls.
    internal sealed class BorderGraph
    {
        internal readonly BorderEdge[] Edges;
        internal readonly EditSnapshot Original;
        internal readonly List<int>[] Incident;
        private readonly float _minimumX,_maximumX,_minimumY,_maximumY;
        internal EditSnapshot Current { get; private set; }
        internal readonly HashSet<int> Selected = new HashSet<int>();
        private readonly List<EditSnapshot> _undo = new List<EditSnapshot>();
        private readonly List<EditSnapshot> _redo = new List<EditSnapshot>();
        internal bool CanUndo=>_undo.Count>0 || Dragging;
        internal bool CanRedo=>_redo.Count>0 && !Dragging;
        private EditSnapshot _drag;
        private Dictionary<int, float> _weights;
        private Point2 _grab;
        internal bool Dragging => _drag != null;
        internal int Revision { get; private set; }
        internal int EdgeCount => Edges.Length + Current.Bridges.Length;
        internal BorderEdge EdgeAt(int index) => index < Edges.Length ? Edges[index] : Current.Bridges[index - Edges.Length];
        internal bool IsDeleted(int index) => index < Edges.Length && Current.Deleted[index];
        internal IEnumerable<int> AtNode(int node)
        {
            foreach (int edge in node>=0 && node<Incident.Length ? Incident[node] : Enumerable.Empty<int>()) if (!Current.Deleted[edge]) yield return edge;
            for (int i=0; i<Current.Bridges.Length; i++)
                if (Current.Bridges[i].A == node || Current.Bridges[i].B == node) yield return Edges.Length+i;
        }

        internal BorderGraph(Point2[] points, BorderEdge[] edges)
        {
            Edges = edges;
            if(points.Length>0) { _minimumX=points.Min(p=>p.X)-200f; _maximumX=points.Max(p=>p.X)+200f;
                _minimumY=points.Min(p=>p.Y)-200f; _maximumY=points.Max(p=>p.Y)+200f; }
            Original = new EditSnapshot { Points = (Point2[])points.Clone(), OriginalPointCount=points.Length, Deleted = new bool[edges.Length] };
            Current = Original.Copy();
            Incident = points.Select(p => new List<int>()).ToArray();
            for (int i = 0; i < edges.Length; i++) { Incident[edges[i].A].Add(i); Incident[edges[i].B].Add(i); }
        }

        internal static float Distance(Point2 p, Point2 a, Point2 b, out float t)
        {
            Point2 d = b - a; float square = Point2.Dot(d, d);
            t = square < 0.000001f ? 0f : Math.Max(0f, Math.Min(1f, Point2.Dot(p - a, d) / square));
            return (p - (a + d * t)).Length;
        }

        internal void BeginDrag(int edgeIndex, Point2 grab, float radius)
        {
            CancelDrag();
            if (edgeIndex < 0 || edgeIndex >= EdgeCount || IsDeleted(edgeIndex)) return;
            _grab = grab; _drag = Current.Copy(); _weights = new Dictionary<int, float>();
            var distance = new Dictionary<int, float>(); var queue = new Queue<int>();
            BorderEdge edge = EdgeAt(edgeIndex);
            // Both ends of the grabbed section follow the pointer exactly. Falloff starts beyond them,
            // so even a long new connector can be grabbed in its centre without a dead zone.
            distance[edge.A] = 0f;
            distance[edge.B] = 0f;
            queue.Enqueue(edge.A); queue.Enqueue(edge.B);
            while (queue.Count > 0)
            {
                int node = queue.Dequeue(); float d = distance[node];
                if (d >= radius || AtNode(node).Count() > 2) continue; // Junctions remain pinned in v0.1.
                float u = d / radius; _weights[node] = (1f - u) * (1f - u) * (1f + 2f * u);
                foreach (int id in AtNode(node))
                {
                    BorderEdge next = EdgeAt(id); int other = next.A == node ? next.B : next.A;
                    float nd = d + (Current.Points[node] - Current.Points[other]).Length;
                    if (nd < radius && (!distance.ContainsKey(other) || nd < distance[other]))
                    { distance[other] = nd; queue.Enqueue(other); }
                }
            }
        }

        internal bool MoveDrag(Point2 pointer, out string reason)
        {
            reason = null;
            if (_drag == null) return false;
            Point2 delta = pointer - _grab;
            if (!delta.Finite || delta.Length > 12f) { reason = "Drag limited to 12 map units per gesture."; return false; }
            var candidate = _drag.Copy();
            foreach (var weight in _weights) candidate.Points[weight.Key] = _drag.Points[weight.Key] + delta * weight.Value;
            if (!Validate(candidate, out reason)) return false;
            if (Equal(Current, candidate)) return false;
            Current = candidate; Revision++; return true;
        }

        internal bool Validate(EditSnapshot state, out string reason)
        {
            reason = null;
            if (state.Points.Length < Original.Points.Length || state.OriginalPointCount!=Original.Points.Length || state.Deleted.Length != Edges.Length)
            { reason = "Snapshot dimensions differ."; return false; }
            var changed = new List<int>();
            for (int i = 0; i < state.Points.Length; i++)
                if (!state.Points[i].Finite || (i<Original.Points.Length ? (state.Points[i] - Original.Points[i]).Length > 200f : !WithinMapBounds(state.Points[i])))
                { reason = "Invalid or excessive point displacement."; return false; }
            if(state.Fills.Length>32) { reason="At most 32 fill areas are supported."; return false; }
            foreach(FillPatch fill in state.Fills)
                if(fill==null || !FillGeometry.ValidatePoints(fill.Points,out reason)) return false;
            BorderEdge[] allEdges = Edges.Concat(state.Bridges).ToArray();
            var spatial=new BorderSpatialIndex();
            var adjacency=allEdges.Select((edge,id)=>new {edge,id})
                .Where(x=>x.id>=Edges.Length||!state.Deleted[x.id])
                .SelectMany(x=>new[]{new {node=x.edge.A,x.id},new {node=x.edge.B,x.id}}).ToLookup(x=>x.node,x=>x.id);
            for (int i = 0; i < allEdges.Length; i++)
            {
                if (i < Edges.Length && state.Deleted[i]) continue;
                BorderEdge e = allEdges[i];
                if (e.A < 0 || e.B < 0 || e.A >= state.Points.Length || e.B >= state.Points.Length || e.A == e.B)
                { reason = "Invalid connection endpoints."; return false; }
                if ((state.Points[e.A] - state.Points[e.B]).Length < 0.03f)
                { reason = "Edit would collapse a border segment."; return false; }
                if(i >= Edges.Length)
                {
                    foreach(int node in new[]{e.A,e.B})
                    {
                        var neighbors=adjacency[node].Where(j=>j!=i).Select(j=>allEdges[j]).ToArray();
                        if(neighbors.Length>1) { reason="Connections must join open ends without making a junction."; return false; }
                        if(neighbors.Length==0) continue; // Deleting an adjoining section can expose a bridge end.
                        BorderEdge neighbor=neighbors[0];
                        if(e.Authored && neighbor.Authored && node>=Original.Points.Length)continue;
                        bool reversed=(e.A==node)==(neighbor.A==node);
                        if(e.Left!=(reversed?neighbor.Right:neighbor.Left) || e.Right!=(reversed?neighbor.Left:neighbor.Right))
                        { reason="Connection side colours do not match adjacent borders."; return false; }
                    }
                }
                if (i >= Edges.Length || !Point2.Same(state.Points[e.A], Original.Points[e.A]) || !Point2.Same(state.Points[e.B], Original.Points[e.B])) changed.Add(i);
                spatial.Add(i,state.Points[e.A],state.Points[e.B]);
            }
            foreach (int i in changed)
            {
                BorderEdge a = allEdges[i];
                foreach(int j in spatial.Query(state.Points[a.A],state.Points[a.B]))
                {
                    BorderEdge b = allEdges[j];
                    if (i == j || (j < Edges.Length && state.Deleted[j])) continue;
                    if (a.A == b.A || a.A == b.B || a.B == b.A || a.B == b.B)
                    {
                        if (i >= Edges.Length && ((a.A == b.A && a.B == b.B) || (a.A == b.B && a.B == b.A)))
                        { reason = "Connection already exists."; return false; }
                        int shared=a.A==b.A||a.A==b.B?a.A:a.B;
                        Point2 da=state.Points[a.A==shared?a.B:a.A]-state.Points[shared];
                        Point2 db=state.Points[b.A==shared?b.B:b.A]-state.Points[shared];
                        if(Math.Abs(Cross(da,db))<0.00001f && Point2.Dot(da,db)>0f)
                        { reason="Edit would overlap adjoining border segments."; return false; }
                        continue;
                    }
                    if (Intersects(state.Points[a.A], state.Points[a.B], state.Points[b.A], state.Points[b.B]))
                    { reason = "Edit would cross another border: edge="+i+" ("+a.A+"->"+a.B+"), other="+j+" ("+b.A+"->"+b.B+")."; return false; }
                }
            }
            return true;
        }

        private static bool Intersects(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            if (Math.Max(a.X,b.X) < Math.Min(c.X,d.X) || Math.Max(c.X,d.X) < Math.Min(a.X,b.X)
                || Math.Max(a.Y,b.Y) < Math.Min(c.Y,d.Y) || Math.Max(c.Y,d.Y) < Math.Min(a.Y,b.Y)) return false;
            return Cross(b-a,c-a)*Cross(b-a,d-a) <= 0f && Cross(d-c,a-c)*Cross(d-c,b-c) <= 0f;
        }
        private static float Cross(Point2 a, Point2 b) => a.X*b.Y-a.Y*b.X;
        internal void FinishDrag()
        {
            if (_drag == null) return;
            if (!Equal(_drag, Current)) Push(_undo, _drag);
            if (!Equal(_drag, Current)) _redo.Clear();
            _drag = null; _weights = null;
        }
        internal void CancelDrag()
        { if (_drag != null) { Current = _drag; _drag = null; _weights = null; Revision++; } }
        internal void DeleteSelected()
        {
            CancelDrag(); var next = Current.Copy();
            foreach (int edge in Selected) if (edge >= 0 && edge < Edges.Length) next.Deleted[edge] = true;
            next.Bridges = next.Bridges.Where((edge, i) => !Selected.Contains(Edges.Length+i)).ToArray();
            Commit(next); Selected.Clear();
        }
        internal bool ConnectSelected(out string reason)
        {
            CancelDrag(); reason = "Select exactly two border sections using Shift-click.";
            if (Selected.Count != 2) return false;
            int[] selected = Selected.ToArray();
            if (selected.Any(i => i < 0 || i >= EdgeCount || IsDeleted(i))) return false;
            BorderEdge first = EdgeAt(selected[0]), second = EdgeAt(selected[1]);
            int from = -1, to = -1; float distance = float.MaxValue;
            foreach (int a in new[] {first.A,first.B}) foreach (int b in new[] {second.A,second.B})
            {
                if (a == b || AtNode(a).Count() != 1 || AtNode(b).Count() != 1) continue;
                float d = (Current.Points[a]-Current.Points[b]).Length;
                if (d < distance) { distance=d; from=a; to=b; }
            }
            return ConnectEndpoints(from, to, out reason);
        }
        internal bool ConnectEndpoints(int from, int to, out string reason)
        {
            CancelDrag();
            EditSnapshot next;
            if(!TryConnection(from,to,out next,out reason))return false;
            Commit(next);Selected.Clear();return true;
        }
        internal bool CanConnectEndpoints(int from,int to,out string reason)
        { EditSnapshot next;return TryConnection(from,to,out next,out reason); }
        private bool TryConnection(int from,int to,out EditSnapshot next,out string reason)
        {
            next=null;
            reason = "Choose two different open border ends.";
            if (from < 0 || to < 0 || from >= Current.Points.Length || to >= Current.Points.Length || from == to)
                return false;
            int[] fromEdges = AtNode(from).ToArray(), toEdges = AtNode(to).ToArray();
            if (fromEdges.Length != 1 || toEdges.Length != 1) return false;
            float distance = (Current.Points[from] - Current.Points[to]).Length;
            if (distance > 24f || distance < 0.03f)
            { reason = "Choose two open ends within 24 map units."; return false; }
            BorderEdge first = EdgeAt(fromEdges[0]), second = EdgeAt(toEdges[0]);
            uint left = from == first.B ? first.Left : first.Right;
            uint right = from == first.B ? first.Right : first.Left;
            uint targetLeft = to == second.A ? second.Left : second.Right;
            uint targetRight = to == second.A ? second.Right : second.Left;
            if (left != targetLeft || right != targetRight)
            { reason = "The two ends have incompatible side colours."; return false; }
            next = Current.Copy();
            next.Bridges = next.Bridges.Concat(new[] { new BorderEdge { A=from, B=to, Row=-1, Left=left, Right=right } }).ToArray();
            if (!Validate(next, out reason)) return false;
            reason = null; return true;
        }
        private bool WithinMapBounds(Point2 point)
        {
            if(Original.Points.Length==0) return false;
            return point.X>=_minimumX && point.X<=_maximumX && point.Y>=_minimumY && point.Y<=_maximumY;
        }
        private int SnapOpen(Point2 point)
        {
            int result=-1; float nearest=.5f;
            for(int i=0;i<Current.Points.Length;i++)
            {
                float distance=(Current.Points[i]-point).Length;
                if(distance<=nearest && AtNode(i).Count()==1) { nearest=distance; result=i; }
            }
            return result;
        }
        internal bool AddSegment(Point2 from,Point2 to,uint left,uint right,out string reason)
        {
            CancelDrag(); reason=null;
            if(!from.Finite || !to.Finite) { reason="Choose valid map points."; return false; }
            int a=SnapOpen(from),b=SnapOpen(to);
            bool fixedStart=false;
            if(a>=0)
            {
                BorderEdge edge=EdgeAt(AtNode(a).Single());
                fixedStart=!edge.Authored || a<Original.Points.Length;
                if(fixedStart){left=a==edge.B?edge.Left:edge.Right; right=a==edge.B?edge.Right:edge.Left;}
            }
            if(b>=0)
            {
                BorderEdge edge=EdgeAt(AtNode(b).Single());
                uint targetLeft=b==edge.A?edge.Left:edge.Right,targetRight=b==edge.A?edge.Right:edge.Left;
                if(!edge.Authored || b<Original.Points.Length)
                {
                    if(fixedStart && (left!=targetLeft || right!=targetRight)) { reason="The two ends have incompatible side colours."; return false; }
                    left=targetLeft; right=targetRight;
                }
            }
            var next=Current.Copy(); var points=next.Points.ToList();
            if(a<0) { a=points.Count; points.Add(from); }
            if(b<0) { b=points.Count; points.Add(to); }
            next.Points=points.ToArray();
            next.Bridges=next.Bridges.Concat(new[]{new BorderEdge{A=a,B=b,Row=-1,Left=left,Right=right,Authored=true}}).ToArray();
            if(!Validate(next,out reason)) return false;
            Commit(next); Selected.Clear(); return true;
        }
        internal bool AddStroke(Point2[] points,uint[] left,uint[] right,out string reason)
        {
            CancelDrag(); reason=null;
            if(points==null || points.Length<2 || points.Length>64 || left==null || right==null
                || left.Length!=points.Length-1 || right.Length!=points.Length-1)
            { reason="Draw a stroke with 2 to 64 points."; return false; }
            // Prepare in an isolated graph so later invalid segments cannot leave a partial stroke.
            var candidate=new BorderGraph(Original.Points,Edges);candidate.Restore(Current);
            for(int i=0;i<points.Length-1;i++)
                if(!candidate.AddSegment(points[i],points[i+1],left[i],right[i],out reason))return false;
            Commit(candidate.Current);Selected.Clear();return true;
        }
        internal bool AddFill(Point2[] points,uint color,out string reason) => AddFill(points,color,true,out reason);
        internal bool AddFill(Point2[] points,uint color,bool localColors,out string reason,bool clipToLand=true)
        {
            CancelDrag();
            if(!FillGeometry.ValidatePoints(points,out reason))return false;
            var next=Current.Copy();
            next.Fills=next.Fills.Concat(new[]{new FillPatch{Points=(Point2[])points.Clone(),Color=color,LocalColors=localColors,ClipToLand=clipToLand}}).ToArray();
            if(!Validate(next,out reason))return false;
            Commit(next); Selected.Clear();return true;
        }
        internal void DeleteFill(int index)
        {
            CancelDrag(); if(index<0 || index>=Current.Fills.Length)return;
            var next=Current.Copy();next.Fills=next.Fills.Where((fill,i)=>i!=index).ToArray();Commit(next);
        }
        internal void Commit(EditSnapshot next)
        { if (!Equal(next, Current)) { Push(_undo, Current.Copy()); Current = next.Copy(); _redo.Clear(); Revision++; } }
        internal void Undo() { CancelDrag(); Travel(_undo, _redo); }
        internal void Redo() { CancelDrag(); Travel(_redo, _undo); }
        private void Travel(List<EditSnapshot> from, List<EditSnapshot> to)
        { if (from.Count == 0) return; Push(to, Current.Copy()); Current = from[from.Count-1]; from.RemoveAt(from.Count-1); Selected.Clear(); Revision++; }
        internal void Restore(EditSnapshot snapshot)
        { _drag = null; _weights = null; Current = snapshot.Copy(); _undo.Clear(); _redo.Clear(); Selected.Clear(); Revision++; }
        private static void Push(List<EditSnapshot> history, EditSnapshot state)
        { if (history.Count == 32) history.RemoveAt(0); history.Add(state); }
        internal static bool Equal(EditSnapshot a, EditSnapshot b)
        { return a.OriginalPointCount==b.OriginalPointCount && a.Fills.Length==b.Fills.Length
            && a.Fills.Zip(b.Fills,(x,y)=>x.ClipToLand==y.ClipToLand && x.Color==y.Color && x.LocalColors==y.LocalColors && x.Points.Length==y.Points.Length && x.Points.Zip(y.Points,Point2.Same).All(v=>v)).All(v=>v)
            && a.Points.Length == b.Points.Length && a.Deleted.SequenceEqual(b.Deleted) && a.Points.Zip(b.Points, Point2.Same).All(x => x)
            && a.Bridges.Length == b.Bridges.Length && a.Bridges.Zip(b.Bridges, (x,y) => x.A==y.A && x.B==y.B && x.Left==y.Left && x.Right==y.Right && x.Authored==y.Authored).All(x=>x); }
    }
}
