using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.WorkshopProcurement
{
    // Optional read-only diagnostic ABI v1. No models, RNG, allocation of cargo,
    // or mutations. -1 stock is unknown/quarantined, NOT zero inventory.
    public static class ProcurementDiagnostics
    {
        // Actual expression evaluated in the current native gate; no second model call.
        // NaN means no active observation, not a zero hurdle. Optional additive ABI.
        public static double ObservedApprovalMargin(Workshop shop) { return WineOperatingMargin.Observed(shop); }
        // Optional additive ABI: one read-only planner decision per workshop/day.
        // Candidate filter counts are not counts of independent shortages.
        public static event Action<string,string,string> PlanObserved;
        internal static bool PlanObservationEnabled { get { return PlanObserved != null; } }
        public static event Action<string,string,string> CandidateObserved;
        public static event Action<string,string,string> HoldObserved;
        internal static void Hold(ProcurementOrder order, string reason)
        {
            var listeners = HoldObserved;
            if (listeners == null) return;
            foreach (Action<string,string,string> listener in listeners.GetInvocationList())
                try { listener(order.Key, reason, "order=" + Identity(order) + "; actual_lifecycle_hold; once_per_workshop_reason_day"); }
                catch (Exception ex) { ProcurementLog.Write("OBSERVER_FAILED", ex.ToString()); }
        }
        public static string InputEvidence(Workshop shop, string category)
        {
            var behavior = ProcurementBehavior.Current;
            return behavior == null ? "privateState=no_behavior; eligiblePrivate=unknown" : behavior.InputEvidence(shop, category);
        }
        internal static bool CandidateObservationEnabled { get { return CandidateObserved != null; } }
        internal static void Candidate(string shop, string reason, string detail)
        {
            var listeners = CandidateObserved;
            if (listeners == null) return;
            foreach (Action<string,string,string> listener in listeners.GetInvocationList())
                try { listener(shop, reason, detail); }
                catch (Exception ex) { ProcurementLog.Write("OBSERVER_FAILED", ex.ToString()); }
        }
        internal static void Plan(string shop, string reason, string detail)
        {
            var listeners = PlanObserved;
            if (listeners == null) return;
            foreach (Action<string,string,string> listener in listeners.GetInvocationList())
                try { listener(shop, reason, detail); }
                catch (Exception ex) { ProcurementLog.Write("OBSERVER_FAILED", ex.ToString()); }
        }
        // receipt, order, stage, town, item, category, market before/after,
        // cargo before/after. Only committed transitions are published.
        public static event Action<string,string,string,string,string,string,int,int,int,int> MovementObserved;
        // receipt/order/stage/workshop; remaining basis before/after; actual
        // workshop cash delta; original goods/freight basis. One per commit,
        // not one per item, so multi-input recipes cannot duplicate the charge.
        public static event Action<string,string,string,string,int,int,int,int,int> AccountingObserved;
        // transaction, order, stage, workshop key, begin/committed/rolled_back/failed.
        public static event Action<string,string,string,string,string> TransferObserved;
        internal static void Transfer(string transaction,ProcurementOrder order,string stage,string outcome)
        {
            var listeners=TransferObserved;
            if(listeners==null||order==null)return;
            foreach(Action<string,string,string,string,string> listener in listeners.GetInvocationList())
                try{listener(transaction,Identity(order),stage,order.Key,outcome);}
                catch(Exception ex){ProcurementLog.Write("OBSERVER_FAILED",ex.ToString());}
        }
        internal static int RemainingBasis(ProcurementOrder order)
        {
            long total=(long)order.GoodsCost+order.FreightCost;
            return checked((int)(total-total*(order.OriginalQuantity-order.Quantity)/order.OriginalQuantity));
        }
        internal static void Accounting(ProcurementOrder order,string stage,int before,int after,int cash)
        {
            var listeners=AccountingObserved;
            if(listeners==null)return;
            string receipt=Guid.NewGuid().ToString("N");
            foreach(Action<string,string,string,string,int,int,int,int,int> listener in listeners.GetInvocationList())
            {
                try{listener(receipt,Identity(order),stage,order.Key,before,after,cash,order.GoodsCost,order.FreightCost);}
                catch(Exception ex){ProcurementLog.Write("OBSERVER_FAILED",ex.ToString());}
            }
        }
        internal static string Identity(ProcurementOrder order)
        {
            if(order.OrderId != null) return order.OrderId;
            // Legacy saves remain byte-compatible; derive without mutating them.
            string seed=order.Key+"|"+order.Source+"|"+order.DepartureDay.ToString("R",CultureInfo.InvariantCulture)
                +"|"+order.Recipe.ToString(CultureInfo.InvariantCulture);
            using(var sha=SHA256.Create()) return "legacy-"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).Replace("-","");
        }
        internal static void Movement(ProcurementOrder order, ProcurementLine line, string stage, string town,
            int marketBefore, int marketAfter, int cargoBefore, int cargoAfter)
        {
            var listeners=MovementObserved;
            if(listeners==null)return;
            string receipt=Guid.NewGuid().ToString("N");
            foreach(Action<string,string,string,string,string,string,int,int,int,int> listener in listeners.GetInvocationList())
            {
                try{listener(receipt,Identity(order),stage,town,line.Item,line.Category,marketBefore,marketAfter,cargoBefore,cargoAfter);}
                catch(Exception ex){ProcurementLog.Write("OBSERVER_FAILED",ex.ToString());}
            }
        }
        // Optional observer boundary: a failing listener must never break saving.
        public static event Action<string, string> LedgerObserved;
        internal static void ObserveLedger(string stage, string payload)
        {
            var listeners = LedgerObserved;
            if (listeners == null) return;
            foreach (Action<string, string> listener in listeners.GetInvocationList())
            {
                try { listener(stage, payload); }
                catch (Exception ex) { ProcurementLog.Write("OBSERVER_FAILED", ex.ToString()); }
            }
        }
        public static string LoadedLedger()
        {
            var behavior = ProcurementBehavior.Current;
            return behavior == null ? null : behavior.LoadedLedger();
        }
        public static string CurrentLedger()
        {
            var behavior = ProcurementBehavior.Current;
            return behavior == null ? null : behavior.CurrentLedger();
        }
        public static int PrivateStock(Workshop shop, string category)
        {
            var behavior = ProcurementBehavior.Current;
            return behavior == null ? 0 : behavior.PrivateStock(shop, category);
        }
        public static int PrepaidInputCost(Workshop shop)
        {
            var c = ProcurementPatches.Active;
            return c == null || c.Shop != shop ? -1 : c.BatchCost;
        }
    }
}
