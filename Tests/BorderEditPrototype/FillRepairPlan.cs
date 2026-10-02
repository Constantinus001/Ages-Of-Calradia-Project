using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
namespace Aoc.BorderEditPrototype
{
    internal sealed class RepairTriangle { internal Point2 A,B,C;internal uint Color; }
    internal sealed class RepairRow
    { internal int Id,Faces;internal string Fingerprint;internal readonly List<RepairTriangle> Triangles=new List<RepairTriangle>(); }
    internal sealed class FillRepairPlan
    {
        internal string Topology,DraftHash;internal int Regions;
        internal readonly List<RepairRow> Rows=new List<RepairRow>();
        internal bool CanApply(bool restoredDraft,string topology,string draftHash)
            => restoredDraft && !string.IsNullOrEmpty(Topology) && !string.IsNullOrEmpty(DraftHash)
                && string.Equals(Topology,topology,StringComparison.Ordinal)
                && string.Equals(DraftHash,draftHash,StringComparison.Ordinal);
        internal static FillRepairPlan Load(string path)
        {
            var doc=new XmlDocument{XmlResolver=null};
            using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32000000}))doc.Load(reader);
            XmlElement root=doc.DocumentElement;if(root?.Name!="BorderFillRepair")throw new InvalidDataException("Invalid fill repair plan.");
            var plan=new FillRepairPlan{Topology=root.GetAttribute("topology"),DraftHash=root.GetAttribute("draftHash"),Regions=int.Parse(root.GetAttribute("regions"),CultureInfo.InvariantCulture)};
            foreach(XmlElement element in root.SelectNodes("Row"))
            {
                var row=new RepairRow{Id=int.Parse(element.GetAttribute("id"),CultureInfo.InvariantCulture),Faces=int.Parse(element.GetAttribute("faces"),CultureInfo.InvariantCulture),Fingerprint=element.GetAttribute("fingerprint")};
                if(row.Id<0||row.Faces<0||row.Fingerprint.Length!=64||plan.Rows.Any(r=>r.Id==row.Id))throw new InvalidDataException("Invalid repair row identity.");
                foreach(XmlElement triangle in element.SelectNodes("T"))
                {
                    var t=new RepairTriangle{A=Point(triangle.GetAttribute("a")),B=Point(triangle.GetAttribute("b")),C=Point(triangle.GetAttribute("c")),Color=uint.Parse(triangle.GetAttribute("color"),CultureInfo.InvariantCulture)};
                    if(Math.Abs(FillGeometry.Cross(t.A,t.B,t.C))<1e-9f)continue; // Float conversion can collapse a sub-pixel clipping sliver.
                    row.Triangles.Add(t);
                }
                plan.Rows.Add(row);
            }
            if(plan.Rows.Count==0||plan.Topology.Length!=64||plan.DraftHash.Length!=64)throw new InvalidDataException("Incomplete fill repair plan.");
            return plan;
        }
        private static Point2 Point(string value)
        {
            string[] p=value.Split(',');if(p.Length!=2)throw new InvalidDataException("Invalid repair vertex.");
            var result=new Point2(float.Parse(p[0],CultureInfo.InvariantCulture),float.Parse(p[1],CultureInfo.InvariantCulture));
            if(!result.Finite||Math.Abs(result.X)>100000||Math.Abs(result.Y)>100000)throw new InvalidDataException("Invalid repair coordinate.");return result;
        }
        internal static string TriangleKey(Point2 a,Point2 b,Point2 c,uint color)
        {
            Func<Point2,string> key=p=>((int)Math.Round(p.X*1000f)).ToString(CultureInfo.InvariantCulture)+","+((int)Math.Round(p.Y*1000f)).ToString(CultureInfo.InvariantCulture);
            return color.ToString(CultureInfo.InvariantCulture)+"|"+string.Join("|",new[]{key(a),key(b),key(c)}.OrderBy(x=>x,StringComparer.Ordinal));
        }
        internal static string Fingerprint(IEnumerable<string> triangles)=>DraftStore.Hash(string.Join("\n",triangles.OrderBy(x=>x,StringComparer.Ordinal)));
    }
}
