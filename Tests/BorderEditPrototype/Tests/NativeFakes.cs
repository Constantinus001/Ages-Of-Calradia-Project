using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.Library;

namespace TaleWorlds.Engine
{
    internal sealed class Material { }
    internal sealed class Scene { }
    internal sealed class Mesh
    {
        internal static Aoc.BorderEditPrototype.NativeCapture Capture;
        internal readonly List<Aoc.BorderEditPrototype.Face> Faces=new List<Aoc.BorderEditPrototype.Face>();
        internal Material Material;
        internal int Order;
        internal bool Locked;
        internal static Mesh CreateMesh(bool dynamic) => new Mesh();
        internal void SetMaterial(Material value) { Material=value; }
        internal Material GetMaterial()=>Material;
        internal void SetMeshRenderOrder(int value) { Order=value; }
        internal UIntPtr LockEditDataWrite() { if(Locked)throw new Exception("Already locked"); Locked=true;return (UIntPtr)1; }
        internal void UnlockEditDataWrite(UIntPtr handle) { if(!Locked)throw new Exception("Not locked");Locked=false; }
        internal void ComputeNormals() { }
        internal void RecomputeBoundingBox() { }
        internal void AddTriangle(Vec3 a,Vec3 b,Vec3 c,Vec2 u,Vec2 v,Vec2 w,uint color,UIntPtr handle)
        {
            if(!Locked)throw new Exception("Mesh mutation without lock");
            Faces.Add(new Aoc.BorderEditPrototype.Face{A=a,B=b,C=c,U=u,V=v,W=w,Color=color});
            Capture?.Triangle(this,a,b,c,u,v,w,color);
        }
    }
    internal sealed class GameEntity
    {
        internal static readonly List<GameEntity> All=new List<GameEntity>();
        internal static int FailFrameCountdown=-1;
        internal static int FailRemoveCountdown=-1;
        internal bool Visible,Removed;
        internal Mesh Mesh;
        internal MatrixFrame Frame;
        internal static GameEntity CreateEmpty(Scene scene,bool a,bool b,bool c)
        { var entity=new GameEntity(); All.Add(entity); return entity; }
        internal void SetGlobalFrame(MatrixFrame frame,bool value)
        {
            if(FailFrameCountdown==0){FailFrameCountdown=-1;throw new InvalidOperationException("Injected publication failure");}
            if(FailFrameCountdown>0)FailFrameCountdown--;
            Frame=frame;
        }
        internal Mesh GetFirstMesh()=>Mesh;
        internal MatrixFrame GetGlobalFrame()=>Frame;
        internal void AddMesh(Mesh mesh,bool value){Mesh=mesh;}
        internal void SetForceDecalsToRender(bool value){if(value)throw new Exception("Border was made a decal");}
        internal void SetVisibilityExcludeParents(bool value){if(Removed)throw new Exception("Using removed entity");Visible=value;}
        internal bool GetVisibilityExcludeParents(){return Visible;}
        internal void SetReadyToRender(bool value){ }
        internal void SetAlpha(float value){if(value<0f||value>1f||float.IsNaN(value))throw new Exception("Invalid mesh alpha");}
        internal void Remove(int value)
        {
            if(FailRemoveCountdown==0) { FailRemoveCountdown=-1;throw new InvalidOperationException("Injected native removal failure"); }
            if(FailRemoveCountdown>0)FailRemoveCountdown--;
            if(Removed)throw new InvalidOperationException("Double removal");
            Removed=true;Visible=false;
        }
    }
}
namespace Aoc.BorderEditPrototype
{
    using TaleWorlds.Engine;
    internal sealed class RepairCapturedRow { internal GameEntity Entity;internal string Fingerprint;internal uint Faces; }
    internal sealed class NativeLocationStyle
    {
        internal bool Land(Point2 p)=>p.X<10;
        internal uint Color(Point2 p,int brightness)=>p.X<5?0xFF800000u:0xFF000080u;
        internal void Border(Point2 a,Point2 b,out uint left,out uint right){left=10;right=20;}
        internal static Vec3 Surface(Point2 p,float lift)=>new Vec3(p.X,p.Y,lift);
    }
    internal sealed class FakeBuilder { public List<GameEntity> Entities=new List<GameEntity>(); }
    internal sealed class FakeBehavior { public List<GameEntity> Entities=new List<GameEntity>(); public float Alpha=1f; }
    internal static class PrototypeRuntime
    { internal static void RequireNativeThread(){ } }
    internal static class PrototypeLog
    { internal static void Write(string text){ Console.WriteLine("FAKE NATIVE DIAGNOSTIC "+text.Split('\n')[0]); } }
    internal static class NativeBindings
    {
        internal const string RendererHash="test";
        internal static FieldInfo BuilderEntities=typeof(FakeBuilder).GetField("Entities");
        internal static FieldInfo LiveEntities=typeof(FakeBehavior).GetField("Entities");
        internal static FieldInfo Alpha=typeof(FakeBehavior).GetField("Alpha");
        internal static bool RejectSamples;
        internal static int SampleCalls;
        internal static bool Sample(Vec2 p,out Vec3 result)
        { SampleCalls++;result=new Vec3(p.x,p.y,5f+p.x*0.1f);return !RejectSamples; }
        internal static void EmitQuad(Mesh mesh,Vec3 a,Vec3 b,Vec3 c,Vec3 d,uint color,UIntPtr handle)
        {
            Vec2 u0=new Vec2(0,0),u1=new Vec2(1,0),u2=new Vec2(1,1),u3=new Vec2(0,1);
            mesh.AddTriangle(a,c,d,u0,u2,u1,color,handle);mesh.AddTriangle(a,b,c,u0,u3,u2,color,handle);
            mesh.AddTriangle(a,d,c,u0,u1,u2,color,handle);mesh.AddTriangle(a,c,b,u0,u2,u3,color,handle);
        }
        internal static void EmitCap(Mesh mesh,Vec2 p,Vec3 center,Vec2 direction,Vec2 normal,uint left,uint right,UIntPtr handle)
        {
            foreach(int side in new[]{1,-1})
            {
                Vec3 previous;Sample(p+direction*0.8f,out previous);
                for(int step=1;step<=4;step++)
                {
                    float angle=(float)Math.PI*step/4; Vec3 current;
                    Sample(p+direction*((float)Math.Cos(angle)*0.8f)+normal*((float)Math.Sin(angle)*0.8f*side),out current);
                    uint color=side==1?left:right;
                    mesh.AddTriangle(center,previous,current,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);
                    mesh.AddTriangle(center,current,previous,Vec2.Zero,Vec2.Zero,Vec2.Zero,color,handle);previous=current;
                }
            }
        }
    }
}
