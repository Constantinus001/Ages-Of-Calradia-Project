using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderOverrides
{
    internal sealed class PoliticalBorderEditorDocument
    {
        internal const int FormatVersion = 1;
        internal const int MaximumPaths = 256;
        internal const int MaximumPolygons = 256;
        internal const int MaximumPointsPerShape = 4096;
        internal const long MaximumFileBytes = 4L * 1024L * 1024L;

        internal readonly List<BorderPath> Borders = new List<BorderPath>();
        internal readonly List<FillPolygon> Fills = new List<FillPolygon>();
        internal readonly List<FillBrush> Brushes = new List<FillBrush>();
        internal float BorderWidthScale = 1f;

        internal static string ResolveModuleRoot()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                typeof(PoliticalBorderEditorDocument).Assembly.Location);
            DirectoryInfo shipping = string.IsNullOrWhiteSpace(assemblyDirectory)
                ? null : new DirectoryInfo(assemblyDirectory);
            DirectoryInfo binary = shipping == null ? null : shipping.Parent;
            DirectoryInfo module = binary == null ? null : binary.Parent;
            if (module == null)
                throw new InvalidOperationException("AOC CORE module root could not be resolved.");
            return module.FullName;
        }

        internal static string ResolvePath()
        {
            return Path.Combine(ResolveModuleRoot(), "ModuleData",
                "PoliticalBorderOverrides.xml");
        }

        internal static PoliticalBorderEditorDocument Load()
        {
            string path = ResolvePath();
            if (!File.Exists(path)) return new PoliticalBorderEditorDocument();
            FileInfo file = new FileInfo(path);
            if (file.Length <= 0 || file.Length > MaximumFileBytes)
                throw new InvalidDataException("Political border override file size is invalid.");

            XmlDocument xml = new XmlDocument { XmlResolver = null };
            using (FileStream stream = File.OpenRead(path)) xml.Load(stream);
            XmlElement root = xml.DocumentElement;
            if (root == null || root.Name != "PoliticalBorderOverrides"
                || ParseInt(root.GetAttribute("version"), 0) != FormatVersion)
                throw new InvalidDataException("Political border override format is unsupported.");

            PoliticalBorderEditorDocument result = new PoliticalBorderEditorDocument();
            string widthScale = root.GetAttribute("borderWidthScale");
            if (!string.IsNullOrWhiteSpace(widthScale))
                result.BorderWidthScale = ParseFloat(widthScale);
            if (result.BorderWidthScale < 0.5f || result.BorderWidthScale > 2.5f)
                throw new InvalidDataException(
                    "The global political border width scale is invalid.");
            foreach (XmlNode child in root.ChildNodes)
            {
                XmlElement element = child as XmlElement;
                if (element == null) continue;
                if (element.Name == "BorderPath")
                {
                    if (result.Borders.Count >= MaximumPaths)
                        throw new InvalidDataException("Too many authored border paths.");
                    BorderPath pathValue = new BorderPath
                    {
                        Id = element.GetAttribute("id"),
                        Closed = ParseBool(element.GetAttribute("closed")),
                        FollowCoast = ParseBool(element.GetAttribute("followCoast")),
                        ReplacesGenerated = ParseBool(element.GetAttribute("replacesGenerated")),
                        Style = NormalizeStyle(element.GetAttribute("style")),
                        FactionId = element.GetAttribute("faction"),
                        Color = ParseUInt(element.GetAttribute("color"), 0u)
                    };
                    ReadPoints(element, pathValue.Points);
                    if (pathValue.Points.Count >= 2) result.Borders.Add(pathValue);
                }
                else if (element.Name == "FillPolygon")
                {
                    if (result.Fills.Count >= MaximumPolygons)
                        throw new InvalidDataException("Too many authored fill polygons.");
                    FillPolygon polygon = new FillPolygon
                    {
                        Id = element.GetAttribute("id"),
                        FactionId = element.GetAttribute("faction"),
                        Color = ParseUInt(element.GetAttribute("color"), 0xFFFFFFFFu)
                    };
                    ReadPoints(element, polygon.Points);
                    if (polygon.Points.Count >= 3) result.Fills.Add(polygon);
                }
                else if (element.Name == "FillBrush")
                {
                    if (result.Brushes.Count >= MaximumPolygons)
                        throw new InvalidDataException("Too many authored fill brush strokes.");
                    FillBrush brush = new FillBrush
                    {
                        Id = element.GetAttribute("id"),
                        FactionId = element.GetAttribute("faction"),
                        Color = ParseUInt(element.GetAttribute("color"), 0xFFFFFFFFu),
                        Radius = ParseFloat(element.GetAttribute("radius"))
                    };
                    if (brush.Radius < 0.25f || brush.Radius > 64f)
                        throw new InvalidDataException("An authored fill brush radius is invalid.");
                    ReadPoints(element, brush.Points);
                    if (brush.Points.Count > 0) result.Brushes.Add(brush);
                }
            }
            return result;
        }

        internal void Save()
        {
            SaveTo(ResolvePath());
        }

        /// <summary>
        /// Saves the same validated document contract to an explicitly selected
        /// module path. This is used by the editor-only Scene Studio adapter so
        /// it can remain a separate module while authoring AOC CORE's sidecar.
        /// </summary>
        internal void SaveTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("An override output path is required.", "path");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = new System.Text.UTF8Encoding(false),
                    NewLineChars = Environment.NewLine
                };
                using (XmlWriter writer = XmlWriter.Create(temporary, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("PoliticalBorderOverrides");
                    writer.WriteAttributeString("version", FormatVersion.ToString(
                        CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("borderWidthScale",
                        BorderWidthScale.ToString("R", CultureInfo.InvariantCulture));
                    foreach (BorderPath border in Borders)
                    {
                        writer.WriteStartElement("BorderPath");
                        writer.WriteAttributeString("id", border.Id ?? string.Empty);
                        writer.WriteAttributeString("closed", border.Closed ? "true" : "false");
                        writer.WriteAttributeString("followCoast", border.FollowCoast ? "true" : "false");
                        writer.WriteAttributeString("replacesGenerated", border.ReplacesGenerated ? "true" : "false");
                        writer.WriteAttributeString("style", NormalizeStyle(border.Style));
                        writer.WriteAttributeString("faction", border.FactionId ?? string.Empty);
                        writer.WriteAttributeString("color", "0x" + border.Color.ToString("X8"));
                        WritePoints(writer, border.Points);
                        writer.WriteEndElement();
                    }
                    foreach (FillPolygon fill in Fills)
                    {
                        writer.WriteStartElement("FillPolygon");
                        writer.WriteAttributeString("id", fill.Id ?? string.Empty);
                        writer.WriteAttributeString("faction", fill.FactionId ?? string.Empty);
                        writer.WriteAttributeString("color", "0x" + fill.Color.ToString("X8"));
                        WritePoints(writer, fill.Points);
                        writer.WriteEndElement();
                    }
                    foreach (FillBrush brush in Brushes)
                    {
                        writer.WriteStartElement("FillBrush");
                        writer.WriteAttributeString("id", brush.Id ?? string.Empty);
                        writer.WriteAttributeString("faction", brush.FactionId ?? string.Empty);
                        writer.WriteAttributeString("color", "0x" + brush.Color.ToString("X8"));
                        writer.WriteAttributeString("radius", brush.Radius.ToString("R", CultureInfo.InvariantCulture));
                        WritePoints(writer, brush.Points);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void ReadPoints(XmlElement parent, ICollection<Vec2> output)
        {
            foreach (XmlNode child in parent.ChildNodes)
            {
                XmlElement point = child as XmlElement;
                if (point == null || point.Name != "Point") continue;
                if (output.Count >= MaximumPointsPerShape)
                    throw new InvalidDataException("An authored shape contains too many points.");
                float x = ParseFloat(point.GetAttribute("x"));
                float y = ParseFloat(point.GetAttribute("y"));
                if (float.IsNaN(x) || float.IsInfinity(x)
                    || float.IsNaN(y) || float.IsInfinity(y))
                    throw new InvalidDataException("An authored point is non-finite.");
                output.Add(new Vec2(x, y));
            }
        }

        private static void WritePoints(XmlWriter writer, IEnumerable<Vec2> points)
        {
            foreach (Vec2 point in points)
            {
                writer.WriteStartElement("Point");
                writer.WriteAttributeString("x", point.x.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("y", point.y.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
        }

        private static float ParseFloat(string value)
        {
            float result;
            if (!float.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out result))
                throw new InvalidDataException("An authored coordinate is invalid.");
            return result;
        }

        private static int ParseInt(string value, int fallback)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static uint ParseUInt(string value, uint fallback)
        {
            if (value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);
            uint result;
            return uint.TryParse(value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static bool ParseBool(string value)
        {
            bool result;
            return bool.TryParse(value, out result) && result;
        }

        private static string NormalizeStyle(string value)
        {
            if (string.Equals(value, "Dashed", StringComparison.OrdinalIgnoreCase))
                return "Dashed";
            if (string.Equals(value, "Double", StringComparison.OrdinalIgnoreCase))
                return "Double";
            return "Solid";
        }
    }

    internal sealed class BorderPath
    {
        internal string Id;
        internal bool Closed;
        internal bool FollowCoast;
        internal bool ReplacesGenerated;
        internal string Style = "Solid";
        internal string FactionId;
        internal uint Color;
        internal readonly List<Vec2> Points = new List<Vec2>();
    }

    internal sealed class FillPolygon
    {
        internal string Id;
        internal string FactionId;
        internal uint Color;
        internal readonly List<Vec2> Points = new List<Vec2>();
    }

    internal sealed class FillBrush
    {
        internal string Id;
        internal string FactionId;
        internal uint Color;
        internal float Radius;
        internal readonly List<Vec2> Points = new List<Vec2>();
    }
}
