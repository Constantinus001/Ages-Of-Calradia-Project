using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
namespace Aoc.BorderEditPrototype
{
    // Creation gesture and its disposable outline; graph owns committed edits/history.
    internal sealed class CreationTool
    {
        private readonly NativeCapture _source;private readonly NativeLocationStyle _style;
        private BoundaryGuide _guide;
        private readonly BorderEnclosure _enclosure=new BorderEnclosure();
        private Point2[] _boundaryPreview;
        private bool IsFill=>Mode=="fill"||Mode=="borderfill";
        internal bool Paused {get;private set;}
        internal Point2 LastPoint=>_points.Last();
        internal bool Following=>Mode=="coast"||Mode=="political";
        internal bool Guide(Point2 input,out Point2 result){result=input;return !Following||_guide.Snap(input,out result);}
        private readonly List<Point2> _points=new List<Point2>();private GameEntity _ghost;
        private Point2? _last;private int _drawnCount=-1;private bool _drawnDelete;
        internal string Mode {get;private set;}
        internal bool Active=>Mode!=null;
        internal bool Pending=>_points.Count>0;
        internal float FrameHeight {get;set;}
        internal bool Applied {get;private set;}
        internal CreationTool(NativeCapture source,NativeLocationStyle style){_source=source;_style=style;}
        internal void Begin(string mode){Clear();Mode=mode;if(Following)_guide=new BoundaryGuide(p=>mode=="coast"?(_style.Land(p)?1u:0u):(_style.Land(p)?_style.Color(p,100):0u));}
        internal string Hint=>Mode=="borderfill"?"Click inside a closed border to fill it.":Following?"Hold and trace near the edge. Release to keep.":!IsFill?"Draw freely. Green endpoints snap to your stroke.":"Draw a closed outline. Release fills with local colours.";
        internal void Down(Point2 point){Paused=false;_points.Clear();_points.Add(point);_drawnCount=-1;}
        internal void Drag(Point2 point)
        {
            if(_points.Count==0)return;
            float distance=(point-_points.Last()).Length;
            Paused=false;
            if(Following && distance>=.8f)
            {
                Point2[] path;
                if(!_guide.Trace(_points.Last(),point,out path)){Paused=true;return;}
                var additions=new List<Point2>();Point2 last=_points.Last();
                foreach(Point2 sample in path)if((sample-last).Length>=.8f){additions.Add(sample);last=sample;}
                if(_points.Count+additions.Count>64){Paused=true;return;}
                _points.AddRange(additions);return;
            }
            if(distance<(Following?.8f:1.5f))return;
            if(_points.Count>=(IsFill?32:64))throw new ArgumentException("Stroke is too long. Draw it in smaller sections.");
            _points.Add(point);
        }
        internal string Release(Point2? final,bool snapEnd=false)
        {
            Applied=false;
            try
            {
                if(Following && final.HasValue && _points.Count>0){Drag(final.Value);if(Paused)final=null;}
                if(final.HasValue && _points.Count>0)
                {
                    float distance=(final.Value-_points.Last()).Length;
                    if(Following && distance>2f)return "Stroke cancelled: boundary jumped too far. Trace more slowly.";
                    if(snapEnd && !IsFill)
                    {
                        // Keep the visible snapped endpoint even when it lies inside the
                        // normal sampling threshold. Avoid a tiny doubled-back final edge.
                        if(distance<1.5f && _points.Count>1)_points[_points.Count-1]=final.Value;
                        else if(distance>.03f)
                        {
                            if(_points.Count>=64)return "Stroke is too long. Draw it in smaller sections.";
                            _points.Add(final.Value);
                        }
                    }
                    else if(distance>.5f && distance<1.5f && _points.Count<(IsFill?32:64))_points.Add(final.Value);
                    else Drag(final.Value);
                }
                string reason;
                if(!IsFill)
                {
                    if(_points.Count<2)return "Hold the left mouse button and draw a line, then release.";
                    var left=new uint[_points.Count-1];var right=new uint[left.Length];
                    for(int i=0;i<left.Length;i++)_style.Border(_points[i],_points[i+1],out left[i],out right[i]);
                    Point2[] stroke=_points.ToArray();
                    StrokeSimplifier.Simplify(ref stroke,ref left,ref right);
                    if(!_source.Graph.AddStroke(stroke,left,right,out reason))return reason;
                    Applied=true;
                    return "Stroke added. Draw another, or Escape to finish. Ctrl+Z undoes the whole stroke.";
                }
                if(_points.Count>3 && (_points.Last()-_points[0]).Length<1.5f)_points.RemoveAt(_points.Count-1);
                return ApplyFill();
            }
            finally{_points.Clear();NativeCleanup.Release(_ghost);_ghost=null;_last=null;_drawnCount=-1;}
        }
        internal void HidePreview(){NativeCleanup.Release(_ghost);_ghost=null;_last=null;_drawnCount=-1;}
        internal string Finish()
        {
            Clear();return "Drawing finished. Save edits to keep your changes.";
        }
        internal string BorderFill(Point2 point,bool apply)
        {
            Applied=false;string reason;Point2[] polygon;
            if(!_enclosure.Find(_source.Graph,point,out polygon,out reason)){_boundaryPreview=null;HidePreview();return reason;}
            if(!ReferenceEquals(_boundaryPreview,polygon)){_boundaryPreview=polygon;HidePreview();}
            Preview(point);
            if(!apply)return "Click to fill the highlighted area to its border.";
            if(_source.Graph.Current.Fills.Any(f=>f.Points.Length==polygon.Length&&f.Points.Zip(polygon,Point2.Same).All(x=>x)))return "This border area is already filled.";
            return ApplyFill(polygon);
        }
        private string ApplyFill()=>ApplyFill(_points.ToArray());
        private string ApplyFill(Point2[] polygon)
        {
            string reason;
            if(!FillGeometry.ValidatePoints(polygon,out reason))return reason;
            FillGeometry.Subdivide(polygon); // Validate complexity before committing.
            if(!_source.Graph.AddFill(polygon,0,true,out reason,false))return reason;
            Applied=true;
            return "Fill area added using local territory colours. Draw another, or Escape to finish.";
        }
        internal void Preview(Point2 point,bool deleteFill=false)
        {
            MatrixFrame frame=MatrixFrame.Identity;frame.origin.z=!IsFill?FrameHeight:0;
            if(_ghost!=null)_ghost.SetGlobalFrame(frame,true);
            if(_last.HasValue && (_last.Value-point).Length<.25f && _drawnCount==_points.Count && _drawnDelete==deleteFill)return;
            NativeCleanup.Release(_ghost);_ghost=null;_last=point;_drawnCount=_points.Count;_drawnDelete=deleteFill;
            Point2[] outline;
            if(deleteFill)
            {
                FillPatch patch=_source.Graph.Current.Fills.LastOrDefault(f=>FillGeometry.Contains(f.Points,point));
                if(patch==null)return;outline=patch.Points.Concat(new[]{patch.Points[0]}).ToArray();
            }
            else if(_boundaryPreview!=null)outline=_boundaryPreview.Concat(new[]{_boundaryPreview[0]}).ToArray();
            else outline=_points.Concat(new[]{point}).ToArray();
            if(outline.Length<2 && Following)outline=new[]{point+new Point2(-.45f,0),point+new Point2(.45f,0),point,point+new Point2(0,-.45f),point+new Point2(0,.45f)};
            if(outline.Length<2)return;
            Mesh mesh=Mesh.CreateMesh(true);mesh.SetMaterial(_source.Rows[0].Material);mesh.SetMeshRenderOrder(112);
            UIntPtr handle=mesh.LockEditDataWrite();
            try
            {
                if(Following)
                {
                    Line(mesh,handle,point+new Point2(-.65f,0),point+new Point2(.65f,0),NativeSelection.HoverColor);
                    Line(mesh,handle,point+new Point2(0,-.65f),point+new Point2(0,.65f),NativeSelection.HoverColor);
                }
                for(int i=1;i<outline.Length;i++)Line(mesh,handle,outline[i-1],outline[i],deleteFill?NativeSelection.InvalidColor:NativeSelection.HoverColor);
                if(IsFill && outline.Length>=3)
                {
                    Line(mesh,handle,outline.Last(),outline[0],NativeSelection.StartColor);
                    string reason;
                    Point2[] interior=_boundaryPreview??outline;
                    if(FillGeometry.ValidatePoints(interior,out reason))foreach(Point2[] tri in FillGeometry.Triangulate(interior))
                    {
                        Vec3 a=NativeLocationStyle.Surface(tri[0],4.12f),b=NativeLocationStyle.Surface(tri[1],4.12f),c=NativeLocationStyle.Surface(tri[2],4.12f);
                        mesh.AddTriangle(a,b,c,Vec2.Zero,Vec2.Zero,Vec2.Zero,NativeSelection.OpenColor,handle);
                        mesh.AddTriangle(a,c,b,Vec2.Zero,Vec2.Zero,Vec2.Zero,NativeSelection.OpenColor,handle);
                    }
                }
            }
            finally{mesh.UnlockEditDataWrite(handle);}
            mesh.ComputeNormals();mesh.RecomputeBoundingBox();_ghost=GameEntity.CreateEmpty(_source.Scene,false,true,true);
            _ghost.SetGlobalFrame(frame,true);
            _ghost.AddMesh(mesh,true);_ghost.SetAlpha(.55f);_ghost.SetReadyToRender(true);_ghost.SetVisibilityExcludeParents(true);
        }
        private void Line(Mesh mesh,UIntPtr handle,Point2 a,Point2 b,uint color)
        {
            Point2 d=b-a;if(d.Length<.01f)return;
            Vec3 n=new Vec3(-d.Y,d.X,0)*(.25f/d.Length);
            float lift=IsFill?4.12f:5.4f;
            Vec3 x=NativeLocationStyle.Surface(a,lift),y=NativeLocationStyle.Surface(b,lift);
            NativeBindings.EmitQuad(mesh,x-n,x+n,y+n,y-n,color,handle);
        }
        internal void Clear(){Mode=null;_boundaryPreview=null;Paused=false;_guide=null;_points.Clear();NativeCleanup.Release(_ghost);_ghost=null;_last=null;_drawnCount=-1;}
    }
}
