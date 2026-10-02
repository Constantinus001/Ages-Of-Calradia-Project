using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;

namespace AgesOfCalradia.CampaignSystems
{
    // Immutable validated snapshot. A malformed file is rejected in full, never partially applied.
    public sealed class ProcurementSettings
    {
        private ProcurementSettings() { }
        public int BatchesPerOrder { get; private set; }
        public int CapitalReserveDays { get; private set; }
        public int SupplierReserveBatches { get; private set; }
        public int IronSupplierReserveDays { get; private set; }
        public int MaximumAdaptiveBatches { get; private set; }
        public double DeliveryDelayCostWeight { get; private set; }
        public double WineExpenseCoverage { get; private set; }
        public int MinimumSupplierStock { get; private set; }
        public int FoodCategoryFloor { get; private set; }
        public int GrapeCategoryFloor { get; private set; }
        public double ReorderBufferDays { get; private set; }
        public int FoodStoresFloor { get; private set; }
        public double FoodBufferDays { get; private set; }
        public double MaximumDistance { get; private set; }
        public double FreightDistancePerDay { get; private set; }
        public double HandlingDays { get; private set; }
        public int FreightBase { get; private set; }
        public double FreightUnitDistance { get; private set; }
        public double BlockedReturnDays { get; private set; }
        public double StockLifetimeDays { get; private set; }
        private string _revision;
        public string Revision { get { return _revision; } }
        private string CalculateRevision()
        {
                string canonical="schema=1;"+string.Join(";",GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance)
                    .Where(p=>p.Name!="Revision").OrderBy(p=>p.Name,StringComparer.Ordinal)
                    .Select(p=>p.Name+"="+Convert.ToString(p.GetValue(this,null),CultureInfo.InvariantCulture)));
                using(var hash=SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-","");
        }
        public static ProcurementSettings Defaults { get { return Parse("<CampaignSystems schema='1'><Procurement /></CampaignSystems>"); } }

        public static ProcurementSettings Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required", "path");
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > 65536) throw new InvalidDataException("CampaignSystems settings exceed 64 KiB");
                    using (var reader = new StreamReader(stream)) return Parse(reader.ReadToEnd());
                }
            }
            catch (FileNotFoundException) { return Defaults; }
            catch (DirectoryNotFoundException) { return Defaults; }
        }
        public static ProcurementSettings Parse(string text)
        {
            if (text == null || text.Length > 65536) throw new InvalidDataException("Invalid settings size");
            XElement root;
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 };
            using (var reader = XmlReader.Create(new StringReader(text), options)) root = XDocument.Load(reader).Root;
            if (root == null || root.Name != "CampaignSystems" || (string)root.Attribute("schema") != "1"
                || root.Attributes().Any(a => a.Name != "schema") || root.Elements().Count() != 1
                || root.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))
                throw new InvalidDataException("Expected CampaignSystems schema 1 and one Procurement section");
            var node = root.Element("Procurement");
            if (node == null || node.HasElements || !string.IsNullOrWhiteSpace(node.Value)) throw new InvalidDataException("Invalid Procurement section");
            string[] keys = {"BatchesPerOrder","CapitalReserveDays","SupplierReserveBatches","MinimumSupplierStock",
                "FoodCategoryFloor","FoodStoresFloor","FoodBufferDays","MaximumDistance","FreightDistancePerDay",
                "HandlingDays","FreightBase","FreightUnitDistance","BlockedReturnDays","StockLifetimeDays",
                "GrapeCategoryFloor","ReorderBufferDays","IronSupplierReserveDays",
                "MaximumAdaptiveBatches","DeliveryDelayCostWeight","WineExpenseCoverage"};
            if (node.Attributes().Any(a => !keys.Contains(a.Name.ToString()))) throw new InvalidDataException("Unknown procurement setting; check spelling");
            var result = new ProcurementSettings {
                BatchesPerOrder = Integer(node,"BatchesPerOrder",3,1,100),
                CapitalReserveDays = Integer(node,"CapitalReserveDays",7,0,365),
                SupplierReserveBatches = Integer(node,"SupplierReserveBatches",7,0,365),
                IronSupplierReserveDays = Integer(node,"IronSupplierReserveDays",0,0,365),
                MaximumAdaptiveBatches = Integer(node,"MaximumAdaptiveBatches",0,0,100),
                DeliveryDelayCostWeight = Number(node,"DeliveryDelayCostWeight",0,0,10),
                WineExpenseCoverage = Number(node,"WineExpenseCoverage",0,0,3),
                MinimumSupplierStock = Integer(node,"MinimumSupplierStock",10,0,100000),
                FoodCategoryFloor = Integer(node,"FoodCategoryFloor",100,1,100000),
                GrapeCategoryFloor = Integer(node,"GrapeCategoryFloor",10,1,100000),
                ReorderBufferDays = Number(node,"ReorderBufferDays",1,0,30),
                FoodStoresFloor = Integer(node,"FoodStoresFloor",100,1,100000),
                FoodBufferDays = Number(node,"FoodBufferDays",30,0,3650),
                MaximumDistance = Number(node,"MaximumDistance",300,1,10000),
                FreightDistancePerDay = Number(node,"FreightDistancePerDay",100,0.01,10000),
                HandlingDays = Number(node,"HandlingDays",1,0.01,365),
                FreightBase = Integer(node,"FreightBase",2,1,100000),
                FreightUnitDistance = Number(node,"FreightUnitDistance",100,1,100000),
                BlockedReturnDays = Number(node,"BlockedReturnDays",14,1,3650),
                StockLifetimeDays = Number(node,"StockLifetimeDays",30,1,3650)
            };
            // These combined values are legal individually but must not strand cargo
            // for centuries or overflow a native wallet at the supported 1000-unit cap.
            if ((result.WineExpenseCoverage > 0 && result.WineExpenseCoverage < 1)
                || result.HandlingDays + result.MaximumDistance / result.FreightDistancePerDay > 365
                || (result.MaximumAdaptiveBatches > 0 && result.MaximumAdaptiveBatches < result.BatchesPerOrder)
                || result.FreightBase + Math.Ceiling(1000 * result.MaximumDistance / result.FreightUnitDistance) > int.MaxValue)
                throw new InvalidDataException("Combined settings exceed 365 travel days or the native wallet bound");
            result._revision=result.CalculateRevision();
            return result;
        }
        private static double Number(XElement node, string key, double fallback, double minimum, double maximum)
        {
            string value = (string)node.Attribute(key); double result;
            if (value == null) return fallback;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || double.IsNaN(result) || double.IsInfinity(result) || result < minimum || result > maximum)
                throw new InvalidDataException("Invalid " + key + "; allowed " + minimum + ".." + maximum);
            return result;
        }
        private static int Integer(XElement node, string key, int fallback, int minimum, int maximum)
        {
            double value = Number(node,key,fallback,minimum,maximum);
            if (value != Math.Truncate(value)) throw new InvalidDataException(key + " must be an integer");
            return (int)value;
        }
    }
}
