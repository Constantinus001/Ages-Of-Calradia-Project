using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOverrides
{
    internal static class PoliticalBorderGeneratedSnapshot
    {
        private const int Version = 1;
        private const int MaximumChains = 32768;
        internal static string ResolvePath()
        { return Path.Combine(PoliticalBorderEditorDocument.ResolveModuleRoot(), "ModuleData", "PoliticalBorderGeneratedLines.xml"); }
        internal static bool Exists { get { return File.Exists(ResolvePath()); } }

        internal static List<GeneratedBorderChain> Load()
        {
            List<GeneratedBorderChain> result = new List<GeneratedBorderChain>();
            string path = ResolvePath();
            if (!File.Exists(path)) return result;
            XmlDocument xml = new XmlDocument { XmlResolver = null };
            using (FileStream stream = File.OpenRead(path)) xml.Load(stream);
            XmlElement root = xml.DocumentElement;
            if (root == null || root.Name != "GeneratedBorderLines" || root.GetAttribute("version") != Version.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Generated-border snapshot format is unsupported.");
            foreach (XmlNode node in root.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element == null || element.Name != "Chain") continue;
                if (result.Count >= MaximumChains) throw new InvalidDataException("Generated-border snapshot has too many chains.");
                GeneratedBorderChain chain = new GeneratedBorderChain
                {
                    Id = element.GetAttribute("id"),
                    Closed = ParseBool(element.GetAttribute("closed")),
                    Coastal = ParseBool(element.GetAttribute("coastal"))
                };
                foreach (XmlNode pointNode in element.ChildNodes)
                {
                    XmlElement point = pointNode as XmlElement;
                    if (point == null || point.Name != "Point") continue;
                    float x = float.Parse(point.GetAttribute("x"), CultureInfo.InvariantCulture);
                    float y = float.Parse(point.GetAttribute("y"), CultureInfo.InvariantCulture);
                    chain.Points.Add(new Vec2(x, y));
                }
                if (chain.Points.Count >= 2) result.Add(chain);
            }
            return result;
        }

        internal static void Save(IList<GeneratedBorderChain> chains)
        {
            string path = ResolvePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) };
                using (XmlWriter writer = XmlWriter.Create(temporary, settings))
                {
                    writer.WriteStartElement("GeneratedBorderLines");
                    writer.WriteAttributeString("version", Version.ToString(CultureInfo.InvariantCulture));
                    foreach (GeneratedBorderChain chain in chains)
                    {
                        writer.WriteStartElement("Chain");
                        writer.WriteAttributeString("id", chain.Id ?? string.Empty);
                        writer.WriteAttributeString("closed", chain.Closed ? "true" : "false");
                        writer.WriteAttributeString("coastal", chain.Coastal ? "true" : "false");
                        foreach (Vec2 point in chain.Points)
                        {
                            writer.WriteStartElement("Point");
                            writer.WriteAttributeString("x", point.x.ToString("R", CultureInfo.InvariantCulture));
                            writer.WriteAttributeString("y", point.y.ToString("R", CultureInfo.InvariantCulture));
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static bool ParseBool(string value) { bool result; return bool.TryParse(value, out result) && result; }
    }

    internal sealed class GeneratedBorderChain
    {
        internal string Id;
        internal bool Closed;
        internal bool Coastal;
        internal readonly List<Vec2> Points = new List<Vec2>();
    }
}
