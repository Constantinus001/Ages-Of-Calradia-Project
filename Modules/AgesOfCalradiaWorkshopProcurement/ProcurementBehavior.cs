using System;
using System.Collections.Generic;
using System.Linq;
using AgesOfCalradia.CampaignSystems;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AgesOfCalradia.WorkshopProcurement
{
    public sealed class ProcurementBehavior : CampaignBehaviorBase
    {
        private ProcurementState _state = new ProcurementState();
        private string _raw = "";
        private bool _invalidSave;
        private readonly bool _hooksReady;
        private readonly Dictionary<string, int> _noticeDays = new Dictionary<string, int>();
        private readonly bool _configurationValid;
        public ProcurementBehavior(bool hooksReady) : this(hooksReady, true) { }
        public ProcurementBehavior(bool hooksReady, bool configurationValid) { _hooksReady = hooksReady; _configurationValid = configurationValid; }
        internal bool Enabled { get { return _hooksReady && !_invalidSave && string.IsNullOrEmpty(_state.Fault); } }
        internal static ProcurementBehavior Current
        { get { return Campaign.Current == null ? null : Campaign.Current.GetCampaignBehavior<ProcurementBehavior>(); } }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static ItemObject ResolveItem(string id) { return MBObjectManager.Instance.GetObject<ItemObject>(id); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static Settlement ResolveSettlement(string id) { return Settlement.Find(id); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static double Now() { return CoreSystemsSubModule.Current == null ? CampaignClock.ValidDay(CampaignTime.Now.ToDays) : CoreSystemsSubModule.Current.Time.Now; }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool PlayerOwned(Workshop shop) { return shop.Owner == Hero.MainHero; }
        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickTownEvent.AddNonSerializedListener(this, DailyTown);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, Hourly);
        }
        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving && !_invalidSave && _state.IsPartial)
                _state.Fault = "Save occurred during a partial production batch; replay disabled.";
            if (dataStore.IsSaving && !_invalidSave) _raw = _state.Encode();
            dataStore.SyncData("aoc_workshop_procurement_v1", ref _raw);
            if (dataStore.IsSaving && !_invalidSave)
                ProcurementDiagnostics.ObserveLedger("serialized_for_save", _raw);
            if (dataStore.IsLoading)
            {
                try
                {
                    _state = ProcurementState.Decode(_raw);
                    _invalidSave = !_state.HasLifecycleCapacity();
                    if (_invalidSave) ProcurementLog.Write("SAVE_CAPACITY_HOLD", "Existing ledger retained byte-for-byte; insufficient lifecycle headroom, actions disabled.");
                }
                catch (Exception ex)
                {
                    // Save boundary: preserve the exact payload on subsequent saves.
                    _invalidSave = true;
                    ProcurementLog.Write("SAVE_REJECTED", ex.ToString());
                }
            }
        }
        internal void Fault(Exception ex)
        {
            string message=ex.ToString();
            _state.RecordFault(message);
            ProcurementLog.Write("FAULT_DISABLED", message);
        }
        internal string LoadedLedger() { return _invalidSave ? null : _raw; }
        internal string CurrentLedger()
        {
            // Observation must not normalize a partial native batch or touch the
            // raw save payload. Missing evidence is safer than a fabricated zero.
            return _invalidSave || _state.IsPartial ? null : _state.Encode();
        }
        internal string Status()
        {
            return "enabled=" + Enabled + " newOrders=" + (_state.AcceptNewOrders && _configurationValid) + " configurationValid=" + _configurationValid + " orders=" + _state.Orders.Count
                + " batches=" + _state.Orders.Sum(o => o.Quantity) + " invalidSave=" + _invalidSave
                + " fault=" + !string.IsNullOrEmpty(_state.Fault)
                + " safeToRemove=" + (Enabled && _state.Orders.Count == 0 && !_state.AcceptNewOrders);
        }
        internal int PrivateStock(Workshop shop, string category)
        {
            if (_invalidSave) return -1;
            var order = _state.Find(shop.Settlement.StringId, shop.Tag);
            if (order == null) return 0;
            if (!order.TransferComplete) return -1;
            return order.Lines.Where(l => l.Category == category).Sum(l => l.Remaining);
        }
        internal string SetOrdering(bool enabled)
        {
            if (!Enabled) return "Procurement is quarantined or hooks are unavailable; no state changed. " + Status();
            _state.AcceptNewOrders = enabled;
            ProcurementLog.Write("ORDERING", Status());
            return Status();
        }
        // Read only saved cargo and the already accepted native attempt. Never
        // call Ready/Eligible, a price model, or normalize the save to explain it.
        internal string InputEvidence(Workshop shop, string category)
        {
            if (_invalidSave) return "privateState=quarantined; eligiblePrivate=unknown; unknownPrivate=unknown";
            var order = _state.Find(shop.Settlement.StringId, shop.Tag);
            int units = order == null ? 0 : order.Lines.Where(l => l.Category == category).Sum(l => l.Remaining);
            var active = ProcurementPatches.Active;
            bool eligible = order != null && active != null && active.Shop == shop && active.Order == order;
            bool unknown = order != null && !order.TransferComplete;
            bool blocked = order != null && (!Enabled || order.ReturnDay > 0);
            bool transit = order != null && !order.Arrived;
            string state = order == null ? "no_order" : unknown ? "partial_unknown" : blocked ? "blocked_or_returning"
                : eligible ? "accepted_current_attempt" : transit ? "in_transit" : "reserved_not_accepted_this_attempt";
            return "privateState=" + state + "; order=" + (order == null ? "none" : ProcurementDiagnostics.Identity(order))
                + "; eligiblePrivate=" + (eligible && !unknown && !blocked ? units : 0)
                + "; unknownPrivate=" + (unknown ? units : 0)
                + "; blockedPrivate=" + (!unknown && blocked ? units : 0)
                + "; inTransitPrivate=" + (!unknown && !blocked && !eligible && transit ? units : 0)
                + "; reservedPrivate=" + (!unknown && !blocked && !eligible && !transit ? units : 0);
        }
        private void DailyTown(Town town)
        {
            if (!Enabled || !Campaign.Current.GameStarted) return;
            try
            {
                if (!ProcurementPlanner.Safe(town)) return;
                foreach (var shop in town.Workshops)
                {
                    if (!Enabled) break;
                    var pending = _state.Find(town.Settlement.StringId, shop.Tag);
                    if (pending != null) { ProcurementLog.Order("DAILY_PENDING", pending, "capital=" + shop.Capital); continue; }
                    if (!_state.AcceptNewOrders || !_configurationValid) continue;
                    string reason;
                    var offer = ProcurementPlanner.Find(shop, Now(), out reason);
                    if (offer != null) Dispatch(shop, offer);
                    else ProcurementLog.Write("NO_ORDER", "shop=" + town.Settlement.StringId + "/" + shop.Tag
                        + " reason=" + reason + " capital=" + shop.Capital + " townGold=" + town.Gold);
                }
            }
            catch (Exception ex) { Fault(ex); } // Native roster/model boundary; do not repeat a partial transfer.
        }
        private void Dispatch(Workshop shop, ProcurementOffer offer)
        {
            var order = offer.Order;
            if (order == null || order.Lines == null || order.TransferComplete || order.Arrived || order.ReturnDay != 0
                || offer.Items.Count != order.Lines.Count || offer.Stocks.Count != order.Lines.Count
                || order.Town != shop.Settlement.StringId || order.Workshop != shop.Tag
                || order.Source != offer.Source.Settlement.StringId
                || offer.Items.Where((item,i)=>item == null || order.Lines[i] == null
                    || item.StringId != order.Lines[i].Item || item.ItemCategory.StringId != order.Lines[i].Category
                    || order.Lines[i].Remaining != (long)order.Quantity * order.Lines[i].UnitsPerBatch).Any())
                throw new InvalidOperationException("Invalid procurement dispatch plan");
            // Validate the proposed ledger before touching real balances or existing state.
            if(order.OrderId == null) order.OrderId=Guid.NewGuid().ToString("N");
            if (!_state.CanAdmit(order)) { Notice(order,"ledger_capacity_no_charge"); return; }
            int cost = checked(order.GoodsCost + order.FreightCost);
            if (_state.Find(order.Town, order.Workshop) != null || shop.Capital != offer.Capital
                || offer.Source.Gold != offer.SourceGold
                || offer.Items.Where((item, i) => offer.Source.Owner.ItemRoster.GetItemNumber(item) != offer.Stocks[i]).Any())
                throw new InvalidOperationException("Procurement quote became stale before dispatch");
            _state.Orders.Add(order);
            if (ProcurementDiagnostics.PlanObservationEnabled)
                ProcurementDiagnostics.Plan(order.Key, "dispatch_link", "plan=" + (offer.DiagnosticPlanId ?? "unobserved")
                    + "; order=" + ProcurementDiagnostics.Identity(order) + "; selected_offer_not_yet_committed");
            var legs = new List<TransferLeg> {
                new TransferLeg { Name = "workshop", Read = () => shop.Capital, Add = shop.ChangeGold, Delta = -cost },
                new TransferLeg { Name = "sourceGold", Read = () => offer.Source.Gold, Add = offer.Source.ChangeGold, Delta = cost } };
            for (int i = 0; i < offer.Items.Count; i++)
            {
                var item = offer.Items[i];
                var line = order.Lines[i];
                int quantity = line.Remaining;
                line.Remaining = 0;
                legs.Add(new TransferLeg { Name = "source/" + item.StringId, Resource = "item/" + item.StringId,
                    Read = () => offer.Source.Owner.ItemRoster.GetItemNumber(item),
                    Add = delta => offer.Source.Owner.ItemRoster.AddToCounts(item, delta), Delta = -quantity });
                legs.Add(new TransferLeg { Name = "cargo/" + item.StringId, Resource = "item/" + item.StringId,
                    Read = () => line.Remaining, Add = delta => line.Remaining += delta, Delta = quantity });
            }
            try { ProcurementTransfer.Execute(legs,(id,outcome)=>ProcurementDiagnostics.Transfer(id,order,"dispatch",outcome)); }
            catch (TransferFailure ex) { if (ex.Restored) _state.Orders.Remove(order); throw; }
            order.TransferComplete = true;
            ProcurementDiagnostics.Accounting(order,"dispatch",0,cost,-cost);
            for(int i=0;i<order.Lines.Count;i++)
                ProcurementDiagnostics.Movement(order,order.Lines[i],"dispatch",offer.Source.Settlement.StringId,
                    offer.Stocks[i],offer.Stocks[i]-order.Lines[i].Remaining,0,order.Lines[i].Remaining);
            ProcurementLog.Order("DISPATCH", order, "capital=" + shop.Capital + " sourceGold=" + offer.Source.Gold);
        }
        private void Hourly()
        {
            if (!Enabled || !Campaign.Current.GameStarted) return;
            try
            {
                foreach (var order in _state.Orders.ToArray())
                {
                    var destination = ResolveSettlement(order.Town);
                    if (destination == null || destination.Town == null)
                    { Notice(order, "missing_settlement"); continue; }
                    var shop = destination.Town.Workshops.SingleOrDefault(w => w.Tag == order.Workshop);
                    if (shop == null) { Notice(order, "missing_workshop"); continue; }
                    // Arrived cargo no longer depends on supplier existence, food,
                    // or diplomacy. Honor an already-committed legacy return only.
                    if (!order.Arrived || order.ReturnDay > 0)
                    {
                        var source = ResolveSettlement(order.Source);
                        if (source == null || source.Town == null) { Notice(order, "missing_supplier"); continue; }
                        bool blocked = !ProcurementPlanner.CanTrade(source.Town, destination.Town);
                        double returnDay = ProcurementPolicy.BeginReturn(order, Now(), blocked);
                        if (order.ReturnDay == 0 && returnDay > 0)
                        {
                            order.ReturnDay = returnDay;
                            ProcurementLog.Order("RETURN_STARTED", order, "blocked_delay_days=" + (order.ReturnDelayDays > 0 ? order.ReturnDelayDays : 14));
                        }
                        if (order.ReturnDay > 0)
                        {
                            if (ProcurementPolicy.Due(Now(), order.ReturnDay) && ProcurementPlanner.Safe(source.Town))
                                Liquidate(shop, order, source.Town, true);
                            continue;
                        }
                        if (blocked) { Notice(order, "route_blocked_by_war_siege_or_food"); continue; }
                        if (!ProcurementPolicy.Due(Now(), order.ArrivalDay)) continue;
                        ProcurementPolicy.MarkArrived(order, Now());
                        foreach(var line in order.Lines)
                            ProcurementDiagnostics.Movement(order,line,"arrival","",0,0,line.Remaining,line.Remaining);
                        ProcurementLog.Order("ARRIVED", order, "");
                    }
                    if (!ProcurementPlanner.Safe(destination.Town)) { Notice(order, "destination_unsafe_for_liquidation"); continue; }
                    // Production/ownership changes cannot strand private inputs forever.
                    // Liquidate only against real town cash, leaving the goods in its market.
                    if (!Matches(shop, order) || PlayerOwned(shop) || !_state.AcceptNewOrders
                        || ProcurementPolicy.Expired(order, Now()))
                        Liquidate(shop, order, destination.Town, false);
                }
            }
            catch (Exception ex) { Fault(ex); }
        }
        private void Notice(ProcurementOrder order, string reason)
        {
            int day = (int)Math.Floor(Now());
            string key = order.Key + "/" + reason;
            int last;
            if (_noticeDays.TryGetValue(key, out last) && last == day) return;
            _noticeDays[key] = day;
            ProcurementDiagnostics.Hold(order, reason);
            ProcurementLog.Order("HELD", order, "reason=" + reason);
        }
        private void Liquidate(Workshop shop, ProcurementOrder order, Town town, bool returning)
        {
            if (!Enabled || !ReferenceEquals(_state.Find(order.Town, order.Workshop), order) || !order.TransferComplete)
                throw new InvalidOperationException("Duplicate or quarantined liquidation");
            var items = order.Lines.Select(l => ResolveItem(l.Item)).ToArray();
            if (items.Any(i => i == null)) { Notice(order, "missing_item"); return; }
            var prices = items.Select(i => town.GetItemPrice(i, null, true)).ToArray();
            // A returned shipment refunds its remaining goods basis only. Freight
            // was spent on transport; it is never minted as a refund.
            long payment = returning ? (long)order.GoodsCost * order.Quantity / order.OriginalQuantity
                : prices.Select((p, i) => (long)p * order.Lines[i].Remaining).Sum();
            if (prices.Any(p => p <= 0) || payment > town.Gold || payment > int.MaxValue - (long)shop.Capital)
            { Notice(order, "liquidation_unfunded_or_invalid_quote"); return; }
            var marketBefore=items.Select(i=>town.Owner.ItemRoster.GetItemNumber(i)).ToArray();
            var cargoBefore=order.Lines.Select(l=>l.Remaining).ToArray();
            int remainingBasis=ProcurementDiagnostics.RemainingBasis(order);
            order.TransferComplete = false;
            var legs = new List<TransferLeg> {
                new TransferLeg { Name = "townGold", Read = () => town.Gold, Add = town.ChangeGold, Delta = -(int)payment },
                new TransferLeg { Name = "workshop", Read = () => shop.Capital, Add = shop.ChangeGold, Delta = (int)payment } };
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                var line = order.Lines[i];
                legs.Add(new TransferLeg { Name = "town/" + item.StringId, Resource = "item/" + item.StringId,
                    Read = () => town.Owner.ItemRoster.GetItemNumber(item),
                    Add = delta => town.Owner.ItemRoster.AddToCounts(item, delta), Delta = line.Remaining });
                legs.Add(new TransferLeg { Name = "cargo/" + item.StringId, Resource = "item/" + item.StringId,
                    Read = () => line.Remaining, Add = delta => line.Remaining += delta, Delta = -line.Remaining });
            }
            try { ProcurementTransfer.Execute(legs,(id,outcome)=>ProcurementDiagnostics.Transfer(id,order,returning?"return":"liquidation",outcome)); }
            catch (TransferFailure ex) { if (ex.Restored) order.TransferComplete = true; throw; }
            _state.Orders.Remove(order);
            ProcurementDiagnostics.Accounting(order,returning?"return":"liquidation",remainingBasis,0,(int)payment);
            for(int i=0;i<order.Lines.Count;i++)
                ProcurementDiagnostics.Movement(order,order.Lines[i],returning?"return":"liquidation",town.Settlement.StringId,
                    marketBefore[i],marketBefore[i]+cargoBefore[i],cargoBefore[i],order.Lines[i].Remaining);
            ProcurementLog.Order(returning ? "RETURNED" : "LIQUIDATED", order, "payment=" + payment);
        }
        internal ProcurementOrder Ready(Workshop shop, WorkshopType.Production recipe)
        {
            if (!Enabled || !ProcurementPlanner.Eligible(shop)) return null;
            var order = _state.Find(shop.Settlement.StringId, shop.Tag);
            if (order == null || !order.Arrived || order.ReturnDay > 0 || !order.TransferComplete || !Matches(shop, order)
                || !SameRecipe(shop.WorkshopType.Productions[order.Recipe], recipe) || order.Quantity <= 0) return null;
            return order;
        }
        internal static bool SameRecipe(WorkshopType.Production left, WorkshopType.Production right)
        {
            // Production is a value type; compare the underlying recipe lists,
            // not separately boxed struct references.
            return ReferenceEquals(left.Inputs, right.Inputs) && ReferenceEquals(left.Outputs, right.Outputs)
                && left.ConversionSpeed == right.ConversionSpeed;
        }
        private static bool Matches(Workshop shop, ProcurementOrder order)
        {
            if (shop.WorkshopType == null || order.Type != shop.WorkshopType.StringId
                || order.Recipe >= shop.WorkshopType.Productions.Count) return false;
            var recipe = shop.WorkshopType.Productions[order.Recipe];
            if (recipe.Inputs.Count != order.Lines.Count) return false;
            foreach (var input in recipe.Inputs)
            {
                var line = order.Lines.SingleOrDefault(l => l.Category == input.Item1.StringId);
                if (line == null || line.UnitsPerBatch != input.Item2 || line.Remaining != order.Quantity * input.Item2) return false;
                var item = ResolveItem(line.Item);
                if (item == null || item.ItemCategory != input.Item1) return false;
            }
            return true;
        }
        internal void Consume(Workshop shop, ProcurementOrder order, ItemCategory category, int quantity)
        {
            var line = order.Lines.Single(l => l.Category == category.StringId);
            var item = ResolveItem(line.Item);
            if (item == null || item.ItemCategory != category) throw new InvalidOperationException("Private stock item changed");
            int cost = order.CostOf(1);
            int cargoBefore=line.Remaining;
            int basisBefore=ProcurementDiagnostics.RemainingBasis(order);
            bool batchCompleted = _state.Consume(order, category.StringId, quantity);
            if(batchCompleted)
                ProcurementDiagnostics.Accounting(order,"consumption",basisBefore,ProcurementDiagnostics.RemainingBasis(order),0);
            ProcurementDiagnostics.Movement(order,line,"consumption","",0,0,cargoBefore,line.Remaining);
            ProcurementLog.Order("CONSUMED", order, "item=" + item.StringId + " units=" + quantity
                + " recognizedBatchCost=" + (batchCompleted ? cost : 0) + " cashCharged=0");
            CampaignEventDispatcher.Instance.OnItemConsumed(item, shop.Settlement, quantity);
        }
    }
}
