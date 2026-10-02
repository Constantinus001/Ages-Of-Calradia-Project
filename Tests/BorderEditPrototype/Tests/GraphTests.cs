using System;
using System.IO;
using System.Linq;

namespace Aoc.BorderEditPrototype
{
    internal static class GraphTests
    {
        private static int _checks;
        private static void Check(bool condition,string name)
        { if(!condition) throw new Exception(name); _checks++; Console.WriteLine("PASS "+name); }
        private static BorderGraph Line()
        {
            Point2[] points=Enumerable.Range(0,6).Select(i=>new Point2(i*4,0)).ToArray();
            BorderEdge[] edges=Enumerable.Range(0,5).Select(i=>new BorderEdge{A=i,B=i+1,Row=i/2,Left=10,Right=20}).ToArray();
            return new BorderGraph(points,edges);
        }
        private static void DragTests()
        {
            var gesture=new DragIntent();gesture.Begin(new Point2(0,0),.2f);
            Check(!gesture.Update(new Point2(.1f,.05f)),"small click jitter stays a selection gesture");
            Check(gesture.Update(new Point2(.3f,0)) && gesture.Update(new Point2(.05f,0)),"deliberate drag stays active when returning near its start");
            gesture.Begin(new Point2(1,1),.2f);
            Check(!gesture.Update(new Point2(1.05f,1)),"a new click resets drag intent");
            BorderGraph graph=Line(); string reason;
            graph.BeginDrag(2,new Point2(10,0),8);
            Check(graph.MoveDrag(new Point2(10,2),out reason),"direct drag modifies nearby border");
            Check(graph.Current.Points[2].Y>graph.Current.Points[1].Y && graph.Current.Points[1].Y>0,"smooth distance falloff");
            Check(Point2.Same(graph.Current.Points[0],graph.Original.Points[0]) && Point2.Same(graph.Current.Points[5],graph.Original.Points[5]),"distant endpoints remain fixed");
            EditSnapshot first=graph.Current.Copy(); graph.MoveDrag(new Point2(10,2),out reason);
            Check(BorderGraph.Equal(first,graph.Current),"repeated mouse coordinate does not accumulate drift");
            Check(!graph.MoveDrag(new Point2(30,0),out reason),"excessive drag rejected");
            Check(!graph.MoveDrag(new Point2(float.NaN,0),out reason),"NaN pointer rejected");
            graph.CancelDrag(); Check(BorderGraph.Equal(graph.Current,graph.Original),"cancel restores exact original coordinates");
            graph.BeginDrag(2,new Point2(10,0),8); graph.MoveDrag(new Point2(10,2),out reason); graph.FinishDrag();
            EditSnapshot moved=graph.Current.Copy(); graph.Undo(); Check(BorderGraph.Equal(graph.Current,graph.Original),"Undo reverses whole gesture");
            graph.Redo(); Check(BorderGraph.Equal(graph.Current,moved),"Redo restores whole gesture");
            graph.Undo(); graph.Selected.Add(0); graph.DeleteSelected(); graph.Redo();
            Check(graph.Current.Deleted[0] && graph.Current.Points[2].Y==0,"new command clears redo history");
            var invalid=graph.Current.Copy(); invalid.Points[2]=invalid.Points[3];
            Check(!graph.Validate(invalid,out reason),"collapsed segment rejected");
        }
        private static void DeleteConnectTests()
        {
            BorderGraph graph=Line(); graph.Selected.Add(2); graph.DeleteSelected();
            Check(graph.Current.Deleted.Count(x=>x)==1,"deletion is section-specific");
            graph.Selected.Add(1); graph.Selected.Add(3); string reason;
            Check(graph.ConnectSelected(out reason),"connect exposed ends across deleted section");
            Check(graph.Current.Bridges.Length==1 && graph.Current.Bridges[0].A==2 && graph.Current.Bridges[0].B==3,"connector reuses endpoint identities");
            Check(graph.Current.Points.Zip(graph.Original.Points,Point2.Same).All(x=>x),"connecting does not reposition existing borders");
            Check(graph.AtNode(2).Count()==2 && graph.AtNode(3).Count()==2,"connected endpoints join graph adjacency");
            graph.BeginDrag(5,new Point2(10,0),8); Check(graph.MoveDrag(new Point2(10,1),out reason),"new connector is draggable"); graph.FinishDrag();
            Check(graph.Current.Points[2].Y>0 && graph.Current.Points[3].Y>0,"connected ends deform with adjacent borders");
            graph.Undo(); graph.Undo(); Check(graph.Current.Bridges.Length==0 && graph.Current.Deleted[2],"Undo connection restores gap");
            graph.Redo(); graph.Selected.Add(5); graph.DeleteSelected(); Check(graph.Current.Bridges.Length==0,"new connector can be deleted");
            graph.Undo(); Check(graph.Current.Bridges.Length==1,"Undo connector deletion");
            graph.Selected.Add(1); graph.Selected.Add(3); Check(!graph.ConnectSelected(out reason),"connected ends cannot be connected again");
            BorderGraph mismatch=Line(); mismatch.Edges[3].Left=77; mismatch.Selected.Add(2); mismatch.DeleteSelected(); mismatch.Selected.Add(1); mismatch.Selected.Add(3);
            Check(!mismatch.ConnectSelected(out reason),"incompatible border colours rejected");
            var crossing=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0),new Point2(8,0),new Point2(12,0),new Point2(6,-2),new Point2(6,2)},
                new[]{new BorderEdge{A=0,B=1,Left=1,Right=1},new BorderEdge{A=2,B=3,Left=1,Right=1},new BorderEdge{A=4,B=5,Left=1,Right=1}});
            crossing.Selected.Add(0); crossing.Selected.Add(1);
            Check(!crossing.ConnectSelected(out reason),"connection crossing existing border rejected");
            Check(reason.Contains("edge=")&&reason.Contains("other="),"crossing rejection identifies both offending edges");
            var junction=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0),new Point2(8,0),new Point2(4,4)},
                new[]{new BorderEdge{A=0,B=1},new BorderEdge{A=1,B=2},new BorderEdge{A=1,B=3}});
            junction.BeginDrag(0,new Point2(2,0),8); junction.MoveDrag(new Point2(2,-1),out reason);
            Check(Point2.Same(junction.Current.Points[1],junction.Original.Points[1]),"three-way junction is pinned");
            var longGap=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0),new Point2(24,0),new Point2(28,0)},
                new[]{new BorderEdge{A=0,B=1,Left=1,Right=2},new BorderEdge{A=2,B=3,Left=1,Right=2}});
            longGap.Selected.Add(0);longGap.Selected.Add(1);Check(longGap.ConnectSelected(out reason),"20-unit connection accepted");
            longGap.BeginDrag(2,new Point2(14,0),8);Check(longGap.MoveDrag(new Point2(14,2),out reason),"long connector can be dragged at its centre");
            Check(longGap.Current.Points[1].Y==2 && longGap.Current.Points[2].Y==2,"grabbed connector follows full pointer displacement");
            longGap.FinishDrag();longGap.Selected.Add(2);longGap.Undo();Check(longGap.Selected.Count==0,"Undo clears positional connector selection");
            longGap.Selected.Add(2);longGap.Redo();Check(longGap.Selected.Count==0,"Redo clears positional connector selection");
            var overlap=Line();var state=overlap.Current.Copy();state.Points[1]=new Point2(10,0);
            Check(!overlap.Validate(state,out reason),"overlapping adjacent edges are rejected");
        }
        private static void EndpointConnectionTests()
        {
            BorderGraph graph=Line(); string reason;
            graph.Selected.Add(2); graph.DeleteSelected();
            EditSnapshot gap=graph.Current.Copy();
            Check(!graph.ConnectEndpoints(-1,3,out reason) && !graph.ConnectEndpoints(2,99,out reason),"invalid explicit endpoint IDs rejected");
            Check(!graph.ConnectEndpoints(2,2,out reason),"same endpoint cannot connect to itself");
            Check(!graph.ConnectEndpoints(1,3,out reason),"degree-two node is not an open endpoint");
            Check(BorderGraph.Equal(graph.Current,gap),"rejected endpoint commands preserve graph");
            Check(graph.ConnectEndpoints(3,2,out reason),"explicit endpoints connect without section selection");
            BorderEdge bridge=graph.Current.Bridges[0];
            Check(bridge.A==3 && bridge.B==2 && bridge.Left==20 && bridge.Right==10,"reverse click order preserves oriented side colours");
            Check(!graph.ConnectEndpoints(3,2,out reason),"already connected endpoints are no longer open");
            EditSnapshot connected=graph.Current.Copy();
            graph.Undo(); Check(BorderGraph.Equal(graph.Current,gap),"Undo explicit endpoint connection restores gap");
            graph.Redo(); Check(BorderGraph.Equal(graph.Current,connected),"Redo explicit endpoint connection restores orientation");
            graph=Line(); graph.Selected.Add(0); graph.DeleteSelected();
            Check(!graph.ConnectEndpoints(0,5,out reason),"isolated node is not a visible open endpoint");
            var bounded=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0),new Point2(28,0),new Point2(32,0)},
                new[]{new BorderEdge{A=0,B=1,Left=1,Right=2},new BorderEdge{A=2,B=3,Left=1,Right=2}});
            Check(!bounded.ConnectEndpoints(0,3,out reason),"explicit endpoint distance above 24 rejected");
            Check(bounded.ConnectEndpoints(1,2,out reason),"explicit endpoint distance exactly 24 accepted");
            graph=Line(); graph.Selected.Add(2); graph.DeleteSelected(); graph.Edges[3].Left=77;
            Check(!graph.ConnectEndpoints(2,3,out reason),"explicit endpoint incompatible side colours rejected");
            var crossing=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0),new Point2(8,0),new Point2(12,0),new Point2(6,-2),new Point2(6,2)},
                new[]{new BorderEdge{A=0,B=1,Left=1,Right=1},new BorderEdge{A=2,B=3,Left=1,Right=1},new BorderEdge{A=4,B=5,Left=1,Right=1}});
            Check(!crossing.ConnectEndpoints(1,2,out reason),"explicit endpoint crossing rejected");
            var single=new BorderGraph(new[]{new Point2(0,0),new Point2(4,0)},new[]{new BorderEdge{A=0,B=1,Left=1,Right=1}});
            Check(!single.ConnectEndpoints(0,1,out reason),"explicit endpoints cannot duplicate existing edge");
        }
        private static void PersistenceTests(string root)
        {
            BorderGraph graph=Line(); graph.Selected.Add(2); graph.DeleteSelected(); string reason; graph.ConnectEndpoints(3,2,out reason);
            string signature=DraftStore.Hash("fixture"),path=DraftStore.PathFor(root,"campaign-a",signature);
            DraftStore.Save(path,signature,graph.Current);
            Check(BorderGraph.Equal(graph.Current,DraftStore.Load(path,signature,Line())),"draft roundtrip includes deletions and connections");
            string legacyXml=File.ReadAllText(path).Replace("version=\"2\"","version=\"1\"").Replace(" originalPoints=\"6\"","").Replace(" authored=\"false\"","");
            File.WriteAllText(path,legacyXml);
            Check(!DraftStore.Load(path,signature,Line()).Bridges[0].Authored,"legacy connections default to strict unauthored colour compatibility");
            Check(DraftStore.Load(DraftStore.PathFor(root,"campaign-b",signature),signature,Line())==null,"second campaign does not inherit edits");
            graph.Selected.Add(0); graph.DeleteSelected(); DraftStore.Save(path,signature,graph.Current);
            Check(File.Exists(path+".bak"),"atomic overwrite preserves previous draft backup");
            bool rejected=false; try { DraftStore.Load(path,DraftStore.Hash("different"),Line()); } catch(InvalidDataException){rejected=true;}
            Check(rejected,"changed topology is not silently applied");
            string xml=File.ReadAllText(path); File.WriteAllText(path,xml.Replace("x=\"0\"","x=\"NaN\""));
            rejected=false; try { DraftStore.Load(path,signature,Line()); } catch(InvalidDataException){rejected=true;}
            Check(rejected,"malformed numeric draft rejected");
            File.WriteAllText(path,"<!DOCTYPE BorderDraft [<!ENTITY x SYSTEM 'file:///not-read'>]><BorderDraft>&x;</BorderDraft>");
            rejected=false; try{DraftStore.Load(path,signature,Line());}catch(System.Xml.XmlException){rejected=true;}
            Check(rejected,"draft DTD/entity expansion prohibited");
        }
        private static int Main(string[] args)
        {
            string root=Path.Combine(Path.GetTempPath(),"aoc-border-prototype-tests-"+Guid.NewGuid().ToString("N"));
            try { PublishedLayoutTests(root); RepairFingerprintTests(); TopologyFingerprintTests(); TopologyCaptureStoreTests(root); PublishedNodeComparisonTests(root); EnclosureTests(); DrawingMaskTests(root); LargeDraftTests(root); GuideTests(); DragTests(); DeleteConnectTests(); EndpointConnectionTests(); DraftStatusTests(); AddTests(root); PersistenceTests(root); if(args.Length==2)ReviewedBindingTests(root,args[0],args[1]);else if(args.Length!=0)throw new ArgumentException("Expected exact capture and published asset directory."); Console.WriteLine("PASS "+_checks+" behavior assertions"); return 0; }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
        }
        private static void PublishedLayoutTests(string root)
        {
            string topology=new string('A',64);
            Check(PublishedBorderLayout.DraftPath(root,"campaign",topology)==DraftStore.PathFor(root,"campaign",topology),"editor keeps campaign-specific persistence");
            Directory.CreateDirectory(root);
            string path=Path.Combine(root,topology+".xml");
            BorderGraph graph=Line();graph.Selected.Add(2);graph.DeleteSelected();DraftStore.Save(path,topology,graph.Current);
            PublishedBorderLayout.Root=root;
            try
            {
                Check(PublishedBorderLayout.DraftPath("missing",null,topology)==path && PublishedBorderLayout.DraftPath("missing","another campaign",topology)==path,"published layout loads from module without a personal draft or campaign identity");
                Check(BorderGraph.Equal(DraftStore.Load(path,topology,graph),graph.Current),"published geometry restores exact saved edits");
                bool refused=false;try { PublishedBorderLayout.DraftPath(root,"campaign",new string('B',64)); }catch(InvalidDataException){refused=true;}
                Check(refused,"different map topology cannot load published geometry");
            }
            finally { PublishedBorderLayout.Root=null; }
        }
        private static void ReviewedBindingTests(string root,string capture,string assets)
        {
            string[] lines=File.ReadAllLines(capture);
            Check(lines.Contains("format=1")&&lines.Contains("schema=exact-xy-ieee754-v1")
                &&lines.Contains("rendererHash="+PublishedBorderLayout.ReviewedRenderer)
                &&lines.Contains("layoutIdentity="+PublishedBorderLayout.ReviewedSourceIdentity)
                &&lines.Contains("topologySignature="+PublishedBorderLayout.ReviewedExactGraph),"reviewed capture headers bind renderer, native identity and exact graph schema/hash");
            Point2[] points=lines.Where(x=>x.StartsWith("nodebits=")).Select((line,index)=>
            {
                string[] pair=line.Split(':');
                if(pair[0]!="nodebits="+index)throw new InvalidDataException("Capture node order differs.");
                string[] xy=pair[1].Split(',');
                return new Point2(BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(xy[0],16)),0),BitConverter.ToSingle(BitConverter.GetBytes(Convert.ToUInt32(xy[1],16)),0));
            }).ToArray();
            BorderEdge[] edges=lines.Where(x=>x.StartsWith("edge=")).Select((line,index)=>
            {
                string[] pair=line.Split(':');if(pair[0]!="edge="+index)throw new InvalidDataException("Capture edge order differs.");
                string[] e=pair[1].Split(',');return new BorderEdge{A=int.Parse(e[0]),B=int.Parse(e[1]),Row=int.Parse(e[2].Split('=')[1]),Left=uint.Parse(e[3].Split('=')[1]),Right=uint.Parse(e[4].Split('=')[1])};
            }).ToArray();
            string directory=Path.Combine(root,"reviewed-binding");Directory.CreateDirectory(directory);
            string name=PublishedBorderLayout.ReviewedAssetIdentity+".xml";
            File.Copy(Path.Combine(assets,name),Path.Combine(directory,name));File.Copy(Path.Combine(assets,"Repair.xml"),Path.Combine(directory,"Repair.xml"));
            PublishedBorderLayout.Root=directory;
            try
            {
                var graph=new BorderGraph(points,edges);EditSnapshot untouched=graph.Current.Copy();
                Func<PublishedBorderLayout.Binding> load=()=>PublishedBorderLayout.LoadReviewedBinding(PublishedBorderLayout.ReviewedRenderer,PublishedBorderLayout.ReviewedSourceIdentity,graph);
                var bound=load();
                Check(bound.AssetIdentity==PublishedBorderLayout.ReviewedAssetIdentity&&bound.SourceIdentity==PublishedBorderLayout.ReviewedSourceIdentity&&bound.Path==Path.Combine(directory,name),"exact captured EB graph resolves unchanged F1 asset identity and path");
                Check(BorderGraph.Equal(graph.Current,untouched),"binding validation has no graph mutation");
                Check(bound.Draft.Bridges.Length==371&&bound.Repair.Rows.Count==25&&bound.Repair.Regions==42&&bound.Repair.CanApply(true,bound.AssetIdentity,PublishedBorderLayout.ReviewedDraftHash),"reviewed connection and matching F1 fill repair are accepted together");
                graph.Restore(bound.Draft);Check(BorderGraph.Equal(graph.Current,DraftStore.Load(bound.Path,bound.AssetIdentity,graph)),"accepted binding restores exact authored coordinates");graph.Restore(untouched);
                Action<Action,string> reject=(action,label)=>{bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected&&BorderGraph.Equal(graph.Current,untouched),label);};
                reject(()=>PublishedBorderLayout.LoadReviewedBinding(new string('0',64),PublishedBorderLayout.ReviewedSourceIdentity,graph),"wrong renderer rejects without restore");
                reject(()=>PublishedBorderLayout.LoadReviewedBinding(PublishedBorderLayout.ReviewedRenderer,new string('0',64),graph),"wrong source identity rejects without restore");
                Point2 old=graph.Original.Points[0];uint bits=BitConverter.ToUInt32(BitConverter.GetBytes(old.X),0);
                graph.Original.Points[0]=new Point2(BitConverter.ToSingle(BitConverter.GetBytes(bits+1),0),old.Y);
                reject(()=>load(),"one native float bit rejects without restore");graph.Original.Points[0]=old;
                int oldRow=graph.Edges[0].Row;graph.Edges[0].Row++;reject(()=>load(),"changed native row rejects without restore");graph.Edges[0].Row=oldRow;
                uint oldColor=graph.Edges[0].Left;graph.Edges[0].Left++;reject(()=>load(),"changed native colour rejects without restore");graph.Edges[0].Left=oldColor;
                int oldA=graph.Edges[0].A;graph.Edges[0].A=graph.Edges[0].B;reject(()=>load(),"changed native endpoint rejects without restore");graph.Edges[0].A=oldA;
                foreach(string file in new[]{name,"Repair.xml"})
                {
                    string path=Path.Combine(directory,file);byte[] bytes=File.ReadAllBytes(path);File.AppendAllText(path," ");
                    reject(()=>load(),"changed "+file+" bytes reject before restore or repair authorization");File.WriteAllBytes(path,bytes);
                }
                Check(File.ReadAllBytes(Path.Combine(directory,name)).SequenceEqual(File.ReadAllBytes(Path.Combine(assets,name))),"binding never rewrites original published file");
            }
            finally {PublishedBorderLayout.Root=null;}
        }
        private static void RepairFingerprintTests()
        {
            Point2 a=new Point2(1.2345f,2),b=new Point2(3,4),c=new Point2(5,6);
            string first=FillRepairPlan.TriangleKey(a,b,c,12),reverse=FillRepairPlan.TriangleKey(c,b,a,12);
            Check(first==reverse,"repair identity is independent of triangle winding");
            Check(first!=FillRepairPlan.TriangleKey(a,b,c,13),"repair identity includes territory colour");
            Check(FillRepairPlan.Fingerprint(new[]{first,reverse})!=FillRepairPlan.Fingerprint(new[]{first}),"repair identity preserves triangle multiplicity");
        }
        private static void TopologyFingerprintTests()
        {
            Point2[] points={new Point2(1.2345f,2),new Point2(3,4)};
            BorderEdge[] edges={new BorderEdge{A=0,B=1,Row=7,Left=11,Right=22}};
            TopologyFingerprint first=TopologyFingerprint.Create("renderer",points,edges);
            TopologyFingerprint same=TopologyFingerprint.Create("renderer",points,edges);
            Check(first.Signature==same.Signature,"canonical topology excludes mesh Z and is deterministic");
            var moved=(Point2[])points.Clone();moved[1]=new Point2(3.01f,4);
            TopologyFingerprint changed=TopologyFingerprint.Create("renderer",moved,edges);
            Check(first.Signature!=changed.Signature&&TopologyFingerprint.FirstDifference(first.Tokens,changed.Tokens).Contains("node=1"),"canonical topology records ordered native XY keys");
            BorderEdge[] recoloured={new BorderEdge{A=0,B=1,Row=7,Left=12,Right=22}};
            changed=TopologyFingerprint.Create("renderer",points,recoloured);
            Check(first.Signature!=changed.Signature&&TopologyFingerprint.FirstDifference(first.Tokens,changed.Tokens).Contains("left=12"),"canonical topology records ordered edge row and colours");
        }
        private static void TopologyCaptureStoreTests(string root)
        {
            var plan=new FillRepairPlan {Topology="map",DraftHash="draft"};
            Check(!plan.CanApply(false,"map","draft"),"rejected border draft cannot authorize matching fill repair");
            Check(!plan.CanApply(true,"other","draft")&&!plan.CanApply(true,"map","other"),"restored draft still requires exact repair topology and content");
            Check(plan.CanApply(true,"map","draft"),"successfully restored exact draft permits its repair");
            float exactValue=BitConverter.ToSingle(BitConverter.GetBytes(0x3F800001),0);
            var exact=TopologyFingerprint.CreateExact(new string('A',64),new[]{new Point2(exactValue,-0.0f)},new BorderEdge[0]);
            Check(exact.Tokens[1]=="nodebits=0:3F800001,80000000","exact graph capture retains sub-key precision and signed zero");
            string exactPath=TopologyCaptureStore.Write(Path.Combine(root,"exact"),new string('A',64),new string('B',64),exact);
            string encoded=File.ReadAllLines(exactPath).Single(line=>line.StartsWith("nodebits="));
            uint bits=Convert.ToUInt32(encoded.Split(':')[1].Split(',')[0],16);
            Check(BitConverter.ToSingle(BitConverter.GetBytes(bits),0)==exactValue,"persisted exact graph recreates original float bits");
            Check(exact.Signature!=TopologyFingerprint.CreateExact(new string('A',64),new[]{new Point2(1,-0.0f)},new BorderEdge[0]).Signature,"exact signature detects differences hidden by quantized topology");
            Point2[] points={new Point2(1,2),new Point2(3,4)};
            BorderEdge[] edges={new BorderEdge{A=0,B=1,Row=7,Left=11,Right=22}};
            TopologyFingerprint topology=TopologyFingerprint.Create(new string('A',64),points,edges);
            string captures=Path.Combine(root,"captures");
            string path=TopologyCaptureStore.Write(captures,new string('A',64),new string('B',64),topology);
            string contents=File.ReadAllText(path);
            Check(File.Exists(path)&&contents.Contains("rendererHash="+new string('A',64))
                &&contents.Contains("layoutIdentity="+new string('B',64))
                &&contents.Contains("topologySignature="+topology.Signature)
                &&contents.Contains("node=0:1000,2000")
                &&contents.Contains("edge=0:0,1,row=7,left=11,right=22"),"topology capture persists the full canonical graph");
            Check(TopologyCaptureStore.Write(captures,new string('A',64),new string('B',64),topology)==path,"identical topology capture is idempotent");
            bool rejected=false;try { TopologyCaptureStore.Write(captures,new string('C',64),new string('B',64),topology); } catch(InvalidDataException) { rejected=true; }
            Check(rejected,"topology capture rejects a signature collision");
        }
        private static void PublishedNodeComparisonTests(string root)
        {
            string layouts=Path.Combine(root,"published");Directory.CreateDirectory(layouts);
            string topology=new string('A',64);
            File.WriteAllText(Path.Combine(layouts,topology+".xml"),"<BorderDraft version=\"2\" topology=\""+topology+"\" originalPoints=\"2\" points=\"2\" edges=\"0\"><Point id=\"0\" x=\"1\" y=\"2\"/><Point id=\"1\" x=\"3\" y=\"4\"/></BorderDraft>");
            PublishedBorderLayout.Root=layouts;
            try
            {
                Check(PublishedBorderLayout.FirstNativeNodeDifference(new[]{"node=0:1000,2000","node=1:3000,4000"})=="none","published node comparison accepts exact native XY prefix");
                Check(PublishedBorderLayout.FirstNativeNodeDifference(new[]{"node=0:1000,2000","node=1:3001,4000"}).Contains("index=1"),"published node comparison reports the first changed native XY token");
            }
            finally { PublishedBorderLayout.Root=null; }
        }
        private static void EnclosureTests()
        {
            Point2[] points={new Point2(0,0),new Point2(4,0),new Point2(8,0),new Point2(8,4),new Point2(4,4),new Point2(0,4)};
            var edges=Enumerable.Range(0,6).Select(i=>new BorderEdge{A=i,B=(i+1)%6,Left=1,Right=1}).Concat(new[]{new BorderEdge{A=1,B=4,Left=1,Right=1}}).ToArray();
            var graph=new BorderGraph(points,edges);var finder=new BorderEnclosure();Point2[] polygon;string reason;
            Check(finder.Find(graph,new Point2(2,2),out polygon,out reason)&&polygon.Max(p=>p.X)==4,"border fill selects one face at a shared junction");
            Check(finder.Find(graph,new Point2(6,2),out polygon,out reason)&&polygon.Min(p=>p.X)==4,"neighboring enclosed face is selected independently");
            Check(!finder.Find(graph,new Point2(12,2),out polygon,out reason),"outside the borders cannot be filled");
            graph.Selected.Add(0);graph.DeleteSelected();Check(!finder.Find(graph,new Point2(2,2),out polygon,out reason),"deleted boundary creates a gap rather than an invented closure");
            graph.Undo();Check(finder.Find(graph,new Point2(2,2),out polygon,out reason),"Undo rebuilds enclosure detection");
            Point2[] nested={new Point2(0,0),new Point2(8,0),new Point2(8,8),new Point2(0,8),new Point2(2,2),new Point2(4,2),new Point2(4,4),new Point2(2,4)};
            var loops=new BorderGraph(nested,Enumerable.Range(0,8).Select(i=>new BorderEdge{A=i,B=i/4*4+(i+1)%4}).ToArray());
            var holes=new BorderEnclosure();Check(!holes.Find(loops,new Point2(1,1),out polygon,out reason)&&reason.Contains("another enclosed"),"nested hole is rejected instead of painting across its border");
            Check(holes.Find(loops,new Point2(3,3),out polygon,out reason),"inner nested region remains fillable");
        }
        private static void DrawingMaskTests(string root)
        {
            uint left,right;Point2 sampled=new Point2();
            BorderDrawingStyle.Resolve(new Point2(0,10),new Point2(4,10),p=>false,p=>{sampled=p;return 77;},out left,out right);
            Check(left==77&&right==77&&Point2.Same(sampled,new Point2(2,10)),"manual drawing uses local colour when the political land mask rejects every support");
            var graph=Line();string reason;Check(graph.AddStroke(new[]{new Point2(0,10),new Point2(4,10)},new[]{left},new[]{right},out reason),"mask-rejected drawing can commit as an authored stroke");
            Directory.CreateDirectory(root);string file=Path.Combine(root,"mask.xml");DraftStore.Save(file,"mask",graph.Current);
            Check(BorderGraph.Equal(DraftStore.Load(file,"mask",graph),graph.Current),"manual stroke on mask-rejected terrain saves and reloads");
            BorderDrawingStyle.Resolve(new Point2(0,0),new Point2(4,0),p=>true,p=>p.Y>0?11u:22u,out left,out right);
            Check(left==11&&right==22,"normal land retains independent territory colours on each side");
            BorderDrawingStyle.Resolve(new Point2(0,0),new Point2(4,0),p=>p.Y>1,p=>33,out left,out right);
            Check(left==33&&right==33,"coastal drawing retains the confirmed land colour on both sides");
            bool rejected=false;try{BorderDrawingStyle.Resolve(new Point2(0,0),new Point2(0,0),p=>false,p=>1,out left,out right);}catch(ArgumentException){rejected=true;}
            Check(rejected,"relaxed land mask still rejects collapsed strokes");
        }
        private static void LargeDraftTests(string root)
        {
            Directory.CreateDirectory(root);var graph=Line();var state=graph.Original.Copy();
            var points=state.Points.ToList();var edges=new System.Collections.Generic.List<BorderEdge>();
            for(int i=0;i<300;i++)
            {
                int a=points.Count;float x=(i%15)*3,y=10+(i/15)*3;
                points.Add(new Point2(x,y));points.Add(new Point2(x+1,y));
                edges.Add(new BorderEdge{A=a,B=a+1,Authored=true,Row=-1,Left=1,Right=1});
            }
            state.Points=points.ToArray();state.Bridges=edges.ToArray();string reason;
            Check(graph.Validate(state,out reason),"300 segments and 600 added points exceed former draft caps safely");graph.Restore(state);
            Check(graph.AddSegment(new Point2(0,100),new Point2(1,100),1,1,out reason),"editing continues beyond the former segment and point limits");
            var edited=graph.Current.Copy();graph.Undo();Check(graph.Current.Bridges.Length==300,"large draft Undo retains earlier segments");graph.Redo();
            string file=Path.Combine(root,"large.xml");DraftStore.Save(file,"large",graph.Current);
            Check(BorderGraph.Equal(DraftStore.Load(file,"large",graph),edited),"large draft roundtrips through the existing v2 format");
            var xml=new System.Xml.XmlDocument();xml.Load(file);xml.DocumentElement.SetAttribute("points","2147483647");xml.Save(file);
            bool rejected=false;try{DraftStore.Load(file,"large",graph);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"inflated declared point count is rejected before allocation");
        }
        private static void GuideTests()
        {
            int calls=0;Point2 result;
            Point2[] path;
            var straight=new BoundaryGuide(p=>p.X<0?1u:0u);
            Check(straight.Trace(new Point2(0,0),new Point2(0,6),out path)&&path.Length==6&&path.All(p=>Math.Abs(p.X)<.01f),"fast movement fills intermediate boundary points");
            Check(!straight.Trace(new Point2(0,0),new Point2(0,20),out path),"large teleport is not bridged");
            var bay=new BoundaryGuide(p=>p.Length<3?1u:0u);
            Check(!bay.Trace(new Point2(3,0),new Point2(0,3),out path),"tracing rejects shortcut across curved bay");
            Point2[] line=Enumerable.Range(0,7).Select(i=>new Point2(i,0)).ToArray();uint[] l=Enumerable.Repeat(1u,6).ToArray(),r=Enumerable.Repeat(2u,6).ToArray();
            StrokeSimplifier.Simplify(ref line,ref l,ref r);
            Check(line.Length==3&&line[0].X==0&&line.Last().X==6,"straight stroke uses fewer segments with exact endpoints and bounded chords");
            Point2[] bend={new Point2(0,0),new Point2(1,0),new Point2(1,1)};l=new[]{1u,1u};r=new[]{2u,2u};
            StrokeSimplifier.Simplify(ref bend,ref l,ref r);Check(bend.Length==3,"simplification retains a corner");
            Point2[] colours={new Point2(0,0),new Point2(1,0),new Point2(2,0)};l=new[]{1u,3u};r=new[]{2u,2u};
            StrokeSimplifier.Simplify(ref colours,ref l,ref r);Check(colours.Length==3&&l[1]==3,"simplification retains a local colour boundary");
            var coast=new BoundaryGuide(p=>{calls++;return p.X<0?1u:0u;});
            Check(coast.Snap(new Point2(1,4),out result)&&Math.Abs(result.X)<.01f,"coast guide snaps a water-side cursor to land boundary");
            Check(calls<=104,"boundary search has a fixed per-update lookup budget");
            int before=calls;coast.Snap(new Point2(1.01f,4),out result);
            Check(calls==before,"stationary guide does not repeat native lookups");
            Check(coast.Snap(new Point2(-1,8),out result)&&Math.Abs(result.X)<.01f,"coast guide works from land side");
            Check(!coast.Snap(new Point2(8,8),out result),"no boundary within reach is rejected");
            var curved=new BoundaryGuide(p=>p.Length<5?1u:0u);
            Check(curved.Snap(new Point2(3,3),out result)&&Math.Abs(result.Length-5)<.01f,"curved coastline refinement follows the shore");
            var political=new BoundaryGuide(p=>p.X<0?0xFF800000u:0xFF008000u);
            Check(political.Snap(new Point2(1,0),out result)&&Math.Abs(result.X)<.01f,"political guide follows local colour transition");
            Check(!new BoundaryGuide(p=>1u).Snap(new Point2(0,0),out result),"uniform territory has no invented boundary");
        }
        private static void AddTests(string root)
        {
            var graph=Line();string reason;
            Check(graph.AddSegment(new Point2(0,10),new Point2(4,10),31,32,out reason),"independent segment can be drawn");
            Check(graph.Current.Points.Length==8 && graph.AtNode(6).Count()==1,"new point IDs have dynamic adjacency");
            Check(graph.AddSegment(new Point2(4.2f,10),new Point2(8,10),99,99,out reason),"new line can extend from snapped end");
            Check(graph.Current.Bridges[1].A==7 && graph.Current.Bridges[1].Left==99 && graph.Current.Bridges[1].Right==99,"authored extension preserves supplied location colours");
            var extended=graph.Current.Copy();graph.Undo();Check(graph.Current.Points.Length==8,"Undo addition removes appended point");
            graph.Redo();Check(BorderGraph.Equal(extended,graph.Current),"Redo addition restores complete geometry");
            graph.BeginDrag(6,new Point2(6,10),8);Check(graph.MoveDrag(new Point2(6,11),out reason),"newly drawn segment is draggable");graph.CancelDrag();
            graph.Selected.Add(6);graph.DeleteSelected();Check(graph.Current.Bridges.Length==1,"newly drawn segment can be deleted");graph.Undo();
            Check(!graph.AddSegment(new Point2(2,8),new Point2(2,12),31,32,out reason),"crossing new segment rejected");
            Check(!graph.AddSegment(new Point2(float.NaN,0),new Point2(5,5),31,32,out reason),"nonfinite new segment rejected");
            Check(!graph.AddSegment(new Point2(300,10),new Point2(302,10),31,32,out reason),"new points outside bounded map extent rejected");
            var attached=Line();
            Check(attached.AddSegment(new Point2(20,0),new Point2(24,2),99,99,out reason)
                && attached.Current.Bridges[0].Left==10 && attached.Current.Bridges[0].Right==20,"original endpoint attachment still inherits native side colours");
            var stroke=Line();var strokePoints=new[]{new Point2(0,10),new Point2(4,10),new Point2(6,14)};
            Check(stroke.AddStroke(strokePoints,new uint[]{31,41},new uint[]{32,42},out reason),"freehand stroke adds a bent chain");
            Check(stroke.Current.Bridges.Length==2 && stroke.Current.Points.Length==9,"stroke segments share the middle point");
            Check(stroke.Current.Bridges[0].Authored && stroke.Current.Bridges[1].Left==41 && stroke.Current.Bridges[1].Right==42,"freehand stroke preserves local colour transitions at authored points");
            var drawn=stroke.Current.Copy();stroke.Undo();Check(BorderGraph.Equal(stroke.Current,stroke.Original),"one Undo removes whole freehand gesture");
            stroke.Redo();Check(BorderGraph.Equal(stroke.Current,drawn),"one Redo restores whole freehand gesture");
            stroke=Line();
            Check(!stroke.AddStroke(new[]{new Point2(0,10),new Point2(4,10),new Point2(4,-2)},new uint[]{31,31},new uint[]{32,32},out reason)
                && BorderGraph.Equal(stroke.Current,stroke.Original) && !stroke.CanUndo,"invalid later stroke segment rejects the whole gesture atomically");
            Check(!stroke.AddStroke(strokePoints,new uint[]{31},new uint[]{32,32},out reason),"stroke rejects mismatched per-segment colours");
            var polygon=new[]{new Point2(0,20),new Point2(4,20),new Point2(4,24),new Point2(0,24)};
            Check(graph.AddFill(polygon,123,out reason),"closed fill patch can be added");polygon[0]=new Point2(99,99);
            Check(graph.Current.Fills[0].Points[0].X==0 && graph.Current.Fills[0].LocalColors,"fill copies input and defaults to local colours");
            var filled=graph.Current.Copy();graph.DeleteFill(0);Check(graph.Current.Fills.Length==0,"fill area can be deleted");
            graph.Undo();Check(BorderGraph.Equal(filled,graph.Current),"Undo restores fill area");
            string signature=DraftStore.Hash("newgeometry"),path=DraftStore.PathFor(root,"campaign-c",signature);
            DraftStore.Save(path,signature,graph.Current);
            Check(BorderGraph.Equal(graph.Current,DraftStore.Load(path,signature,Line())),"v2 roundtrip preserves added points and fill");
            var legacy=Line();DraftStore.Save(path,signature,legacy.Current);
            string xml=File.ReadAllText(path).Replace("version=\"2\"","version=\"1\"").Replace(" originalPoints=\"6\"","");
            File.WriteAllText(path,xml);
            Check(BorderGraph.Equal(legacy.Current,DraftStore.Load(path,signature,Line())),"legacy v1 draft remains readable");
        }
        private static void DraftStatusTests()
        {
            BorderGraph graph=Line();var saved=new DraftChangeTracker(graph.Original);
            Check(!graph.CanUndo&&!graph.CanRedo&&!saved.IsDirty(graph),"fresh border has no enabled history or unsaved changes");
            graph.Selected.Add(2);graph.DeleteSelected();
            Check(graph.CanUndo&&!graph.CanRedo&&saved.IsDirty(graph),"editing enables Undo and marks draft unsaved");
            saved.MarkSaved(graph.Current);
            Check(!saved.IsDirty(graph),"successful save advances the disk baseline");
            graph.Undo();
            Check(graph.CanRedo&&saved.IsDirty(graph),"Undo after saving marks the differing state unsaved");
            graph.Redo();
            Check(!saved.IsDirty(graph),"Redo exactly to saved state clears unsaved indicator");
            graph.Selected.Add(0);graph.DeleteSelected();
            Check(saved.IsDirty(graph)&&saved.IsDirty(graph),"unsaved status survives repeated UI refresh and editor reopening");
            graph.Restore(graph.Original);
            Check(!graph.CanUndo&&!graph.CanRedo&&saved.IsDirty(graph),"restoring session state does not pretend it was written to disk");
        }
    }
}
