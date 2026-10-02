using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Aoc.BorderEditPrototype
{
    internal static class DraftStore
    {
        internal static string Hash(string text)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
        internal static string PathFor(string root, string campaign, string topology)
        {
            if (string.IsNullOrWhiteSpace(campaign)) throw new InvalidOperationException("Campaign identity unavailable; draft saving disabled.");
            return Path.Combine(root, Hash(campaign), topology + ".xml");
        }
        internal static void Save(string path, string topology, EditSnapshot state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = Path.Combine(Path.GetDirectoryName(path), "draft-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (XmlWriter writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartElement("BorderDraft"); writer.WriteAttributeString("version", "2");
                    writer.WriteAttributeString("topology", topology);
                    writer.WriteAttributeString("originalPoints", state.OriginalPointCount.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("points", state.Points.Length.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("edges", state.Deleted.Length.ToString(CultureInfo.InvariantCulture));
                    for (int i=0; i<state.Points.Length; i++)
                    {
                        writer.WriteStartElement("Point"); writer.WriteAttributeString("id", i.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("x", state.Points[i].X.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("y", state.Points[i].Y.ToString("R", CultureInfo.InvariantCulture)); writer.WriteEndElement();
                    }
                    for (int i=0; i<state.Deleted.Length; i++) if (state.Deleted[i])
                    { writer.WriteStartElement("Delete"); writer.WriteAttributeString("id", i.ToString(CultureInfo.InvariantCulture)); writer.WriteEndElement(); }
                    foreach (BorderEdge edge in state.Bridges)
                    {
                        writer.WriteStartElement("Bridge");
                        writer.WriteAttributeString("a", edge.A.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("b", edge.B.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("left", edge.Left.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("right", edge.Right.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("authored",XmlConvert.ToString(edge.Authored)); writer.WriteEndElement();
                    }
                    foreach(FillPatch fill in state.Fills)
                    {
                        writer.WriteStartElement("Fill");writer.WriteAttributeString("color",fill.Color.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("localColors",XmlConvert.ToString(fill.LocalColors));
                        writer.WriteAttributeString("clipToLand",XmlConvert.ToString(fill.ClipToLand));
                        foreach(Point2 point in fill.Points)
                        {
                            writer.WriteStartElement("Vertex");writer.WriteAttributeString("x",point.X.ToString("R",CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("y",point.Y.ToString("R",CultureInfo.InvariantCulture));writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                if(new FileInfo(temporary).Length>32000000)throw new InvalidDataException("Draft exceeds the 32 MB file size safeguard. Previous saved edits are retained.");
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static EditSnapshot Load(string path, string topology, BorderGraph graph)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 32000000) throw new InvalidDataException("Draft exceeds size limit.");
            var xml = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32000000 })) xml.Load(reader);
            XmlElement root = xml.DocumentElement;
            string version=root?.GetAttribute("version");
            bool topologyMatches=root != null && root.GetAttribute("topology") == topology;
            if (root == null || root.Name != "BorderDraft" || (version!="1" && version!="2") || !topologyMatches
                || ReadInt(root,"edges") != graph.Edges.Length)
                throw new InvalidDataException("Draft does not match current border generation.");
            int count=ReadInt(root,"points"),original=graph.Original.Points.Length;
            if(count<original || count!=root.SelectNodes("Point").Count || (version=="1" ? count!=original : ReadInt(root,"originalPoints")!=original))
                throw new InvalidDataException("Draft point counts do not match current border generation.");
            EditSnapshot result = graph.Original.Copy(); result.Points=new Point2[count]; var seen = new bool[count];
            var bridges = new System.Collections.Generic.List<BorderEdge>();
            var fills = new System.Collections.Generic.List<FillPatch>();
            foreach (XmlNode node in root.ChildNodes)
            {
                var element = node as XmlElement; if (element == null) continue;
                if (element.Name == "Bridge")
                {
                    bridges.Add(new BorderEdge { A=ReadInt(element,"a"), B=ReadInt(element,"b"), Row=-1,
                        Authored=version=="2" && element.HasAttribute("authored") && XmlConvert.ToBoolean(element.GetAttribute("authored")),
                        Left=uint.Parse(element.GetAttribute("left"),CultureInfo.InvariantCulture), Right=uint.Parse(element.GetAttribute("right"),CultureInfo.InvariantCulture) });
                    continue;
                }
                if(element.Name=="Fill" && version=="2")
                {
                    if(fills.Count>=32)throw new InvalidDataException("Too many fill areas.");
                    var points=new System.Collections.Generic.List<Point2>();
                    foreach(XmlNode child in element.ChildNodes)
                    {
                        var vertex=child as XmlElement;if(vertex==null)continue;
                        if(vertex.Name!="Vertex" || points.Count>=32)throw new InvalidDataException("Invalid fill vertices.");
                        points.Add(new Point2(float.Parse(vertex.GetAttribute("x"),CultureInfo.InvariantCulture),float.Parse(vertex.GetAttribute("y"),CultureInfo.InvariantCulture)));
                    }
                    fills.Add(new FillPatch{Points=points.ToArray(),Color=uint.Parse(element.GetAttribute("color"),CultureInfo.InvariantCulture),
                        ClipToLand=!element.HasAttribute("clipToLand") || XmlConvert.ToBoolean(element.GetAttribute("clipToLand")),
                        LocalColors=!element.HasAttribute("localColors") || XmlConvert.ToBoolean(element.GetAttribute("localColors"))});
                    continue;
                }
                int id = ReadInt(element, "id");
                if (element.Name == "Point")
                {
                    if (id < 0 || id >= seen.Length || seen[id]) throw new InvalidDataException("Duplicate or invalid point.");
                    seen[id] = true; result.Points[id] = new Point2(float.Parse(element.GetAttribute("x"), CultureInfo.InvariantCulture), float.Parse(element.GetAttribute("y"), CultureInfo.InvariantCulture));
                }
                else if (element.Name == "Delete")
                { if (id < 0 || id >= result.Deleted.Length || result.Deleted[id]) throw new InvalidDataException("Invalid deletion."); result.Deleted[id] = true; }
                else throw new InvalidDataException("Unknown draft element.");
            }
            if (Array.IndexOf(seen, false) >= 0) throw new InvalidDataException("Draft is incomplete.");
            result.Bridges = bridges.ToArray(); result.Fills=fills.ToArray();
            string reason; if (!graph.Validate(result, out reason)) throw new InvalidDataException(reason);
            return result;
        }
        private static int ReadInt(XmlElement node, string key) => int.Parse(node.GetAttribute(key), CultureInfo.InvariantCulture);
    }
}
