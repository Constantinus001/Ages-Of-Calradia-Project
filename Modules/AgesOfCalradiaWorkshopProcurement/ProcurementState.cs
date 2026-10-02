using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using AgesOfCalradia.CampaignSystems;

namespace AgesOfCalradia.WorkshopProcurement
{
    [DataContract]
    public sealed class ProcurementLine
    {
        [DataMember] public string Item;
        [DataMember] public string Category;
        [DataMember] public int UnitsPerBatch;
        [DataMember] public int Remaining;
    }
    [DataContract]
    public sealed class ProcurementOrder
    {
        [DataMember] public string OrderId;
        [DataMember] public string Town;
        [DataMember] public string Workshop;
        [DataMember] public string Type;
        [DataMember] public string Source;
        [DataMember] public List<ProcurementLine> Lines = new List<ProcurementLine>();
        [DataMember] public int Recipe;
        [DataMember] public int Quantity;
        [DataMember] public int OriginalQuantity;
        [DataMember] public int GoodsCost;
        [DataMember] public int FreightCost;
        [DataMember] public double ArrivalDay;
        [DataMember] public bool Arrived;
        [DataMember] public bool TransferComplete;
        [DataMember] public double DepartureDay;
        [DataMember] public double ReturnDay;
        [DataMember] public double BlockedSince = -1;
        // Zero denotes a legacy candidate ledger whose actual arrival was not recorded.
        [DataMember] public double DeliveredDay;
        // Zero is a legacy ledger: preserve historical 14/30-day behavior.
        [DataMember] public double ReturnDelayDays;
        [DataMember] public double StockLifetimeDays;
        [DataMember] public string PolicyRevision;
        public string Key { get { return Town + "/" + Workshop; } }
        public int CostOf(int count)
        {
            if (count <= 0 || count > Quantity) throw new ArgumentOutOfRangeException("count");
            // Allocate rounding once across the original shipment, not per cycle.
            long cost = (long)GoodsCost + FreightCost;
            int used = OriginalQuantity - Quantity;
            return checked((int)(cost * (used + count) / OriginalQuantity - cost * used / OriginalQuantity));
        }
    }

    [DataContract]
    public sealed class ProcurementState
    {
        internal const int MaximumPayloadCharacters = 4000000;
        internal const int MaximumFaultCharacters = 8192;
        // Worst-case escaped fault text plus metadata; mutable numeric/boolean
        // fields can grow by less than 256 characters per existing order.
        private const int FaultHeadroomCharacters = 65536;
        private const int LifecycleHeadroomPerOrder = 256;
        [DataMember] public int Schema = 1;
        [DataMember] public List<ProcurementOrder> Orders = new List<ProcurementOrder>();
        // A fault is persisted: loading is not a way to retry a partial mutation.
        [DataMember] public string Fault;
        [DataMember] public bool AcceptNewOrders = true;
        public void Validate()
        {
            if (Schema != 1 || Orders == null || Orders.Count > 10000
                || (Fault != null && Fault.Length > MaximumFaultCharacters))
                throw new InvalidDataException("Unsupported procurement ledger");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var orderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Orders)
            {
                if(o != null && o.OrderId != null && (o.OrderId.Length != 32 || o.OrderId.Any(c => !Uri.IsHexDigit(c))))
                    throw new InvalidDataException("Invalid procurement order identity");
                if(o != null && o.OrderId != null && !orderIds.Add(o.OrderId))throw new InvalidDataException("Duplicate procurement identity");
                if (o == null || string.IsNullOrWhiteSpace(o.Town) || string.IsNullOrWhiteSpace(o.Workshop)
                    || string.IsNullOrWhiteSpace(o.Type) || string.IsNullOrWhiteSpace(o.Source)
                    || o.Lines == null || o.Lines.Count == 0 || o.Lines.Count > 32 || o.Source == o.Town || o.Recipe < 0
                    || o.Quantity <= 0 || o.OriginalQuantity < o.Quantity || o.OriginalQuantity > 1000
                    || o.GoodsCost <= 0 || o.FreightCost <= 0 || (long)o.GoodsCost + o.FreightCost > int.MaxValue
                    || !Finite(o.ArrivalDay) || !Finite(o.DepartureDay) || !Finite(o.ReturnDay) || !Finite(o.BlockedSince) || !Finite(o.DeliveredDay)
                    || o.DepartureDay < 0 || o.ArrivalDay <= o.DepartureDay || o.ReturnDay < 0 || o.BlockedSince < -1
                    || o.DeliveredDay < 0 || (!o.Arrived && o.DeliveredDay != 0)
                    || (o.DeliveredDay != 0 && o.DeliveredDay < o.ArrivalDay)
                    || !Finite(o.ReturnDelayDays) || !Finite(o.StockLifetimeDays)
                    || o.ReturnDelayDays < 0 || o.ReturnDelayDays > 3650 || o.StockLifetimeDays < 0 || o.StockLifetimeDays > 3650
                    || (o.ReturnDelayDays > 0 && o.ReturnDelayDays < 1) || (o.StockLifetimeDays > 0 && o.StockLifetimeDays < 1)
                    || (o.PolicyRevision != null && (o.PolicyRevision.Length != 64 || o.PolicyRevision.Any(c=>!Uri.IsHexDigit(c))))
                    || !keys.Add(o.Key)) throw new InvalidDataException("Invalid or duplicate procurement order");
                var categories = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in o.Lines)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.Item) || string.IsNullOrWhiteSpace(line.Category)
                        || line.UnitsPerBatch <= 0 || line.UnitsPerBatch > 333 || !categories.Add(line.Category)
                        || line.Remaining > (long)o.Quantity * line.UnitsPerBatch
                        || line.Remaining < 0
                        || (o.TransferComplete && line.Remaining < (long)(o.Quantity - 1) * line.UnitsPerBatch)
                        || (string.IsNullOrEmpty(Fault) && o.TransferComplete && line.Remaining != o.Quantity * line.UnitsPerBatch))
                        throw new InvalidDataException("Invalid private input bundle");
                }
            }
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        public string Encode()
        {
            Validate();
            string payload = Serialize();
            if (payload.Length > MaximumPayloadCharacters) throw new InvalidDataException("Procurement ledger exceeds bound");
            return payload;
        }
        private string Serialize()
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ProcurementState)).WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        internal bool HasLifecycleCapacity()
        {
            Validate();
            return (long)Serialize().Length + FaultHeadroomCharacters
                + (long)Orders.Count * LifecycleHeadroomPerOrder <= MaximumPayloadCharacters;
        }
        internal bool CanAdmit(ProcurementOrder order)
        {
            if (Orders.Count >= 10000) return false;
            return new ProcurementState { Orders=Orders.Concat(new[]{order}).ToList(), Fault=Fault,
                AcceptNewOrders=AcceptNewOrders }.HasLifecycleCapacity();
        }
        internal void RecordFault(string message)
        {
            message = message ?? "Unspecified procurement failure";
            Fault = message.Length <= MaximumFaultCharacters ? message : message.Substring(0,MaximumFaultCharacters);
        }
        public static ProcurementState Decode(string data)
        {
            if (string.IsNullOrEmpty(data)) return new ProcurementState();
            if (data.Length > MaximumPayloadCharacters) throw new InvalidDataException("Procurement ledger exceeds bound");
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
            {
                var result = (ProcurementState)new DataContractJsonSerializer(typeof(ProcurementState)).ReadObject(stream);
                if (stream.Position != stream.Length || result == null) throw new InvalidDataException("Invalid ledger payload");
                if (result.Orders != null && result.Orders.Any(o => o != null && !o.TransferComplete))
                    result.Fault = "Save contains an unfinished native transfer; automatic replay disabled.";
                result.Validate();
                return result;
            }
        }
        public ProcurementOrder Find(string town, string workshop)
        {
            return Orders.SingleOrDefault(o => o.Town == town && o.Workshop == workshop);
        }
        public bool IsPartial { get { return Orders.Any(o => o.Lines.Any(l => l.Remaining != o.Quantity * l.UnitsPerBatch)); } }
        public bool Consume(ProcurementOrder order, string category, int count)
        {
            if (!string.IsNullOrEmpty(Fault) || !Orders.Contains(order) || !order.Arrived
                || !order.TransferComplete || order.ReturnDay > 0 || count <= 0) throw new InvalidOperationException("Invalid private-stock consumption");
            var line = order.Lines.SingleOrDefault(l => l.Category == category);
            if (line == null || count != line.UnitsPerBatch || line.Remaining != line.UnitsPerBatch * order.Quantity)
                throw new InvalidOperationException("Duplicate or mismatched input consumption");
            line.Remaining -= count;
            bool completed = order.Lines.All(l => l.Remaining == l.UnitsPerBatch * (order.Quantity - 1));
            if (!completed) return false;
            order.Quantity--;
            if (order.Quantity == 0) Orders.Remove(order);
            return true;
        }
    }

    internal static class ProcurementPolicy
    {
        // Snapshot selected at campaign start by the Core companion; reset by module unload.
        internal static ProcurementSettings Settings = ProcurementSettings.Defaults;
        internal static int BatchesPerOrder { get { return Settings.BatchesPerOrder; } }
        internal static int CapitalReserveDays { get { return Settings.CapitalReserveDays; } }
        internal static int SupplierReserveBatches { get { return Settings.SupplierReserveBatches; } }
        internal static int FoodCategoryFloor { get { return Settings.FoodCategoryFloor; } }
        internal static double MaximumDistance { get { return Settings.MaximumDistance; } }
        // Explicit merchant-service abstraction; not a claim about party speed.
        internal static double FreightDistancePerDay { get { return Settings.FreightDistancePerDay; } }
        internal static bool Due(double now, double due)
        { return !double.IsNaN(now) && !double.IsInfinity(now) && now >= 0
            && !double.IsNaN(due) && !double.IsInfinity(due) && due >= 0 && now >= due; }
        internal static double BeginReturn(ProcurementOrder order, double now, bool blocked)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0)
                throw new ArgumentOutOfRangeException("now");
            if (order.ReturnDay > 0) return order.ReturnDay;
            // Goods already delivered are a local asset, not an international shipment.
            if (order.Arrived) return 0;
            if (!blocked) { order.BlockedSince = -1; return 0; }
            if (order.BlockedSince < 0) order.BlockedSince = now;
            return Due(now, Math.Max(order.ArrivalDay, order.BlockedSince) + (order.ReturnDelayDays > 0 ? order.ReturnDelayDays : 14))
                ? now + order.ArrivalDay - order.DepartureDay : 0;
        }
        internal static void MarkArrived(ProcurementOrder order, double now)
        {
            if (order.Arrived || !order.TransferComplete || order.ReturnDay > 0 || !Due(now, order.ArrivalDay))
                throw new InvalidOperationException("Shipment cannot arrive in its current state");
            order.Arrived = true;
            order.DeliveredDay = now;
            order.BlockedSince = -1;
        }
        internal static bool Expired(ProcurementOrder order, double now)
        {
            return order.Arrived && Due(now, (order.DeliveredDay > 0 ? order.DeliveredDay : order.ArrivalDay)
                + (order.StockLifetimeDays > 0 ? order.StockLifetimeDays : 30));
        }
        internal static double TravelDays(double distance)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0 || distance > MaximumDistance)
                throw new ArgumentOutOfRangeException("distance");
            return Settings.HandlingDays + distance / FreightDistancePerDay;
        }
        internal static int Freight(int quantity, double distance)
        {
            TravelDays(distance);
            if (quantity <= 0 || quantity > 1000) throw new ArgumentOutOfRangeException("quantity");
            return checked(Settings.FreightBase + (int)Math.Ceiling(quantity * distance / Settings.FreightUnitDistance));
        }
        internal static bool Affordable(int capital, int expense, int goods, int freight)
        {
            return expense >= 0 && goods > 0 && freight > 0
                && (long)goods + freight + (long)expense * CapitalReserveDays <= capital;
        }
        internal static bool Profitable(int goods, int freight, int batches, int conservativeOutput, double nativeMargin)
        {
            return goods > 0 && freight > 0 && batches > 0 && conservativeOutput > 0
                && !double.IsNaN(nativeMargin) && !double.IsInfinity(nativeMargin) && nativeMargin >= 0
                && conservativeOutput > Math.Ceiling(((double)goods + freight) / batches) + nativeMargin;
        }
        // A bounded trigger, not a production-rate change or unlimited demand forecast.
        // DailyTown refuses every new order while any private/in-flight order exists.
        internal static int ReorderBatches(double dailyRate, double leadDays)
        {
            if (double.IsNaN(dailyRate) || double.IsInfinity(dailyRate) || dailyRate <= 0
                || double.IsNaN(leadDays) || double.IsInfinity(leadDays) || leadDays < 0)
                return 0;
            return (int)Math.Max(1, Math.Min(Settings.MaximumAdaptiveBatches > 0 ? Settings.MaximumAdaptiveBatches : BatchesPerOrder,
                Math.Ceiling(dailyRate * (leadDays + Settings.ReorderBufferDays))));
        }
        internal static int OrderBatches(double dailyRate, double leadDays)
        {
            int threshold = ReorderBatches(dailyRate, leadDays);
            return threshold == 0 ? 0 : Math.Max(BatchesPerOrder, threshold);
        }
        internal static double OfferScore(int goods, int freight, int batches, int expense,
            double leadDays, int availableBatches, double dailyRate)
        {
            if (goods <= 0 || freight <= 0 || batches <= 0 || expense < 0 || availableBatches < 0
                || double.IsNaN(leadDays) || double.IsInfinity(leadDays) || leadDays < 0
                || double.IsNaN(dailyRate) || double.IsInfinity(dailyRate) || dailyRate <= 0)
                return double.PositiveInfinity;
            double gapDays = Math.Max(0, leadDays - availableBatches / dailyRate);
            // Selection estimate only: never charged, booked as a loss, or called realized profit.
            return ((double)goods + freight + expense * gapDays * Settings.DeliveryDelayCostWeight) / batches;
        }
        internal static bool HasSurplus(int stock, int quantity, int localRecipeUnits, bool food, string category = null,
            double dailyUnits = double.NaN)
        {
            long reserve = Math.Max((long)Settings.MinimumSupplierStock, (long)Math.Max(0, localRecipeUnits) * SupplierReserveBatches);
            if (category == "iron" && !food && Settings.IronSupplierReserveDays > 0)
            {
                double projected = dailyUnits * Settings.IronSupplierReserveDays;
                if (double.IsNaN(projected) || double.IsInfinity(projected) || projected < 0 || projected > int.MaxValue)
                    return false;
                reserve = Math.Max((long)Settings.MinimumSupplierStock, (long)Math.Ceiling(projected));
            }
            if (food) reserve = Math.Max(reserve, category == "grape" ? Settings.GrapeCategoryFloor : FoodCategoryFloor);
            return quantity > 0 && localRecipeUnits >= 0 && (long)stock - quantity >= reserve;
        }
        internal static bool FoodExportSafe(double stores, double dailyChange)
        {
            return !double.IsNaN(stores) && !double.IsInfinity(stores) && !double.IsNaN(dailyChange)
                && !double.IsInfinity(dailyChange) && stores >= Settings.FoodStoresFloor
                && (dailyChange >= 0 || stores >= -dailyChange * Settings.FoodBufferDays);
        }
    }
}
