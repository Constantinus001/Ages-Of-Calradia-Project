using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Aoc.BorderEditPrototype
{
    // Published geometry belongs to the module, not to an individual campaign draft.
    // Existing topology validation still runs before any native rows are replaced.
    internal static class PublishedBorderLayout
    {
        internal static string Root;
        internal static bool Enabled => Root != null;
        internal const string ReviewedSourceIdentity="EB567EBCAAB584975C8726270A3B6D384E6A76BC8E4B1344A06881C040623F75";
        internal const string ReviewedAssetIdentity="F1B414DB1DC9677436F1AADA3E93606DBEDEB60F52E797417C379265B399FDF9";
        internal const string ReviewedRenderer="560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        internal const string ReviewedExactGraph="8D4CB08EF8059E0DEF887808FFFF782D14F3426649A5BF9DDE08E8668371BA3A";
        internal const string ReviewedDraftHash="646FEEE68ED46DE6434E4E55157A5C1C370203F25651E1C6956497CE94052507";
        internal const string ReviewedRepairHash="CBF2C8DF9E94EA73C3E858E223DCD8DAC15B5DB02A9288BCDB252F94955E17DD";
        internal sealed class Binding
        {
            internal string Path,SourceIdentity,AssetIdentity,ExactGraph;
            internal EditSnapshot Draft;
            internal FillRepairPlan Repair;
        }

        // Explicit compatibility pair established by the lossless 2026-09-22
        // renderer capture and real validator replay. This is not a general
        // legacy alias. Every predicate must pass before the caller restores.
        internal static Binding LoadReviewedBinding(string renderer,string sourceIdentity,BorderGraph graph)
        {
            if(renderer!=ReviewedRenderer) throw new InvalidDataException("Published binding renderer hash differs.");
            if(sourceIdentity!=ReviewedSourceIdentity) throw new InvalidDataException("Published binding source identity differs.");
            string exact=TopologyFingerprint.CreateExact(renderer,graph.Original.Points,graph.Edges).Signature;
            if(exact!=ReviewedExactGraph) throw new InvalidDataException("Published binding exact native graph differs: "+exact);
            string path=Path.Combine(Root,ReviewedAssetIdentity+".xml");
            string repairPath=Path.Combine(Root,"Repair.xml");
            if(FileHash(path)!=ReviewedDraftHash) throw new InvalidDataException("Published binding authored file hash differs.");
            if(FileHash(repairPath)!=ReviewedRepairHash) throw new InvalidDataException("Published binding repair file hash differs.");
            EditSnapshot draft=DraftStore.Load(path,ReviewedAssetIdentity,graph);
            if(draft==null) throw new InvalidDataException("Published binding draft is missing.");
            FillRepairPlan repair=FillRepairPlan.Load(repairPath);
            if(!repair.CanApply(true,ReviewedAssetIdentity,ReviewedDraftHash)) throw new InvalidDataException("Published binding repair metadata differs.");
            return new Binding {Path=path,SourceIdentity=sourceIdentity,AssetIdentity=ReviewedAssetIdentity,ExactGraph=exact,Draft=draft,Repair=repair};
        }
        private static string FileHash(string path)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())
            using(var input=File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","");
        }
        // Diagnostic replay only. The result is never restored into the graph
        // and does not authorize a legacy/native topology alias.
        internal static string DiagnoseReviewedGeometry(BorderGraph graph)
        {
            const string topology="F1B414DB1DC9677436F1AADA3E93606DBEDEB60F52E797417C379265B399FDF9";
            const string hash="646FEEE68ED46DE6434E4E55157A5C1C370203F25651E1C6956497CE94052507";
            string path=Path.Combine(Root,topology+".xml");
            using(var sha=System.Security.Cryptography.SHA256.Create())
            using(var input=File.OpenRead(path))
                if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","")!=hash)
                    throw new InvalidDataException("Reviewed diagnostic source hash differs.");
            DraftStore.Load(path,topology,graph);
            return "Reviewed geometry passes validation against exact captured graph; topology compatibility remains unproven; no publication authorized.";
        }
        internal static string DraftPath(string draftRoot, string campaign, string topology)
        {
            if (!Enabled) return DraftStore.PathFor(draftRoot, campaign, topology);
            if (topology == null || topology.Length != 64 || Array.Exists(topology.ToCharArray(), c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("Invalid published border topology.");
            string path = Path.Combine(Root, topology + ".xml");
            if (File.Exists(path)) return path;
            throw new InvalidDataException("Published borders do not match this map topology; original borders retained.");
        }

        // Read-only evidence for a rejected published layout. A draft stores
        // authored coordinates, including moved original nodes. Differences
        // here are NOT evidence of native topology drift or compatibility.
        internal static string FirstNativeNodeDifference(string[] nativeTokens)
        {
            if (!Enabled || nativeTokens == null) return null;
            string[] layouts=Directory.GetFiles(Root,"*.xml",SearchOption.TopDirectoryOnly);
            layouts=Array.FindAll(layouts,path=>!string.Equals(Path.GetFileName(path),"Repair.xml",StringComparison.OrdinalIgnoreCase));
            if(layouts.Length!=1) throw new InvalidDataException("Expected exactly one published border layout.");
            var xml=new XmlDocument{XmlResolver=null};
            using(var reader=XmlReader.Create(layouts[0],new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})) xml.Load(reader);
            XmlElement root=xml.DocumentElement;
            if(root==null || root.Name!="BorderDraft") throw new InvalidDataException("Published border layout is invalid.");
            int original=int.Parse(root.GetAttribute("originalPoints"),CultureInfo.InvariantCulture);
            XmlNodeList points=root.SelectNodes("Point");
            if(original<0 || points.Count<original) throw new InvalidDataException("Published border layout has an incomplete original-node prefix.");
            int nativeNodeCount=Array.FindIndex(nativeTokens,token=>token.StartsWith("edge=",StringComparison.Ordinal));
            if(nativeNodeCount<0) nativeNodeCount=nativeTokens.Length;
            int shared=Math.Min(original,nativeNodeCount);
            for(int index=0;index<shared;index++)
            {
                var point=points[index] as XmlElement;
                if(point==null) throw new InvalidDataException("Published border point is invalid.");
                int id=int.Parse(point.GetAttribute("id"),CultureInfo.InvariantCulture);
                int x=(int)Math.Round(float.Parse(point.GetAttribute("x"),CultureInfo.InvariantCulture)*1000f);
                int y=(int)Math.Round(float.Parse(point.GetAttribute("y"),CultureInfo.InvariantCulture)*1000f);
                string expected="node="+id.ToString(CultureInfo.InvariantCulture)+":"+x.ToString(CultureInfo.InvariantCulture)+","+y.ToString(CultureInfo.InvariantCulture);
                if(!string.Equals(expected,nativeTokens[index],StringComparison.Ordinal))
                    return "index="+index+";published="+expected+";native="+nativeTokens[index];
            }
            if(original!=nativeNodeCount)
                return "index="+shared+";published="+(shared<original?"<node>":"<end>")+";native="+(shared<nativeTokens.Length?nativeTokens[shared]:"<end>");
            return "none";
        }

    }
}
