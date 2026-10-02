using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using AgesOfCalradia.PoliticalBorderOverrides;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    /// <summary>
    /// Incrementally captures an exact, north-up sample package for the native
    /// external editor. The package covers the NavalDLC Main_map campaign-world
    /// square directly; it never passes through the legacy strategic-map affine.
    /// Native scene calls are Bannerlord v1.4.8 compatibility boundaries. A
    /// failure disables only this optional snapshot and is recorded in the
    /// political-border editor diagnostic log.
    /// </summary>
    internal static class PoliticalBorderTerrainSnapshot
    {
        internal const int Width = 1041;
        internal const int Height = 1041;
        internal const int SamplesPerFrame = 256;
        internal const float WorldMinimum = 0f;
        internal const float WorldMaximum = 1040f;
        internal const string CoordinateMode = "bannerlord-world-v1";
        internal const string SceneToken = "{893C6F3B-95F2-43CB-9DB0-22A22EB56F41}";
        internal const string SceneRevision = "1776846426947";
        private const byte UnknownTerrain = byte.MaxValue;

        private static readonly float[] Heights = new float[Width * Height];
        private static readonly byte[] Terrain = new byte[Width * Height];
        private static int _next;
        private static bool _started;
        private static bool _complete;
        private static bool _failed;

        internal static string StatusText
        {
            get
            {
                if (_failed) return "3D TERRAIN: UNAVAILABLE";
                if (_complete) return "3D TERRAIN: READY";
                if (!_started) return "3D TERRAIN: WAITING";
                return "3D TERRAIN: " + (100 * _next / Heights.Length) + "%";
            }
        }

        internal static void Begin()
        {
            if (_complete || _failed) return;
            string path = ResolvePath();
            if (IsCurrentSnapshot(path))
            {
                _complete = true;
                PoliticalBorderEditorDiagnostics.Info(
                    "Existing exact-world terrain snapshot retained: " + path + ".");
                return;
            }
            TryStart();
        }

        internal static void Tick()
        {
            if (_complete || _failed) return;
            if (!_started && !TryStart()) return;
            try
            {
                Campaign campaign = Campaign.Current;
                if (campaign == null || campaign.MapSceneWrapper == null) return;
                int end = Math.Min(Heights.Length, _next + SamplesPerFrame);
                for (; _next < end; _next++)
                {
                    int column = _next % Width;
                    int row = _next / Width;
                    Vec2 point = new Vec2(WorldMinimum + column, WorldMaximum - row);
                    CampaignVec2 campaignPoint = new CampaignVec2(point, false);
                    float terrainHeight = 0f;
                    campaign.MapSceneWrapper.GetHeightAtPoint(campaignPoint, ref terrainHeight);
                    Heights[_next] = terrainHeight;
                    Terrain[_next] = campaignPoint.Face.IsValid()
                        ? checked((byte)campaign.MapSceneWrapper
                            .GetTerrainTypeAtPosition(campaignPoint))
                        : UnknownTerrain;
                }
                if (_next < Heights.Length) return;
                Save();
                _complete = true;
                PoliticalBorderEditorDiagnostics.Info(
                    "Exact-world external terrain snapshot completed: samples="
                    + Heights.Length + "; bounds=0..1040; path=" + ResolvePath() + ".");
            }
            catch (Exception exception)
            {
                _failed = true;
                PoliticalBorderEditorDiagnostics.Error(
                    "Exact-world terrain snapshot failed at the Bannerlord scene boundary; "
                    + "2D world-coordinate editing remains available.", exception);
            }
        }

        private static bool TryStart()
        {
            if (Campaign.Current == null || Campaign.Current.MapSceneWrapper == null)
                return false;
            _started = true;
            _next = 0;
            PoliticalBorderEditorDiagnostics.Info(
                "Exact-world terrain snapshot started: grid=" + Width + "x" + Height
                + "; bounds=0..1040; samplesPerFrame=" + SamplesPerFrame + ".");
            return true;
        }

        private static bool IsCurrentSnapshot(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (XmlReader reader = XmlReader.Create(path,
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                {
                    reader.MoveToContent();
                    return reader.Name == "PoliticalBorderTerrainHeights"
                        && reader.GetAttribute("version") == "2"
                        && reader.GetAttribute("coordinateMode") == CoordinateMode
                        && reader.GetAttribute("sceneToken") == SceneToken
                        && reader.GetAttribute("width") == Width.ToString(CultureInfo.InvariantCulture)
                        && reader.GetAttribute("height") == Height.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception exception) when (exception is IOException
                || exception is XmlException
                || exception is UnauthorizedAccessException)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Existing terrain snapshot could not be validated and will be recaptured.",
                    exception);
                return false;
            }
        }

        private static void Save()
        {
            string path = ResolvePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    Encoding = new UTF8Encoding(false)
                };
                using (XmlWriter writer = XmlWriter.Create(temporary, settings))
                {
                    writer.WriteStartElement("PoliticalBorderTerrainHeights");
                    writer.WriteAttributeString("version", "2");
                    writer.WriteAttributeString("coordinateMode", CoordinateMode);
                    writer.WriteAttributeString("sceneToken", SceneToken);
                    writer.WriteAttributeString("sceneRevision", SceneRevision);
                    writer.WriteAttributeString("worldMinX", "0");
                    writer.WriteAttributeString("worldMinY", "0");
                    writer.WriteAttributeString("worldMaxX", "1040");
                    writer.WriteAttributeString("worldMaxY", "1040");
                    writer.WriteAttributeString("orientation", "north-up");
                    writer.WriteAttributeString("sampleStep", "1");
                    writer.WriteAttributeString("width", Width.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("height", Height.ToString(CultureInfo.InvariantCulture));
                    for (int row = 0; row < Height; row++)
                    {
                        WriteHeightRow(writer, row);
                        WriteTerrainRow(writer, row);
                    }
                    writer.WriteEndElement();
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void WriteHeightRow(XmlWriter writer, int row)
        {
            writer.WriteStartElement("Row");
            writer.WriteAttributeString("index", row.ToString(CultureInfo.InvariantCulture));
            StringBuilder values = new StringBuilder(Width * 8);
            for (int column = 0; column < Width; column++)
            {
                if (column > 0) values.Append(',');
                values.Append(Heights[row * Width + column]
                    .ToString("R", CultureInfo.InvariantCulture));
            }
            writer.WriteString(values.ToString());
            writer.WriteEndElement();
        }

        private static void WriteTerrainRow(XmlWriter writer, int row)
        {
            writer.WriteStartElement("TerrainRow");
            writer.WriteAttributeString("index", row.ToString(CultureInfo.InvariantCulture));
            StringBuilder values = new StringBuilder(Width * 3);
            for (int column = 0; column < Width; column++)
            {
                if (column > 0) values.Append(',');
                values.Append(Terrain[row * Width + column]
                    .ToString(CultureInfo.InvariantCulture));
            }
            writer.WriteString(values.ToString());
            writer.WriteEndElement();
        }

        private static string ResolvePath()
        {
            return Path.Combine(PoliticalBorderEditorDocument.ResolveModuleRoot(),
                "ModuleData", "PoliticalBorderTerrainHeights.xml");
        }
    }
}
