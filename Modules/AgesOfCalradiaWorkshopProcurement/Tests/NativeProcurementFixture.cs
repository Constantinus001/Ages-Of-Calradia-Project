// Offline only. Native production/rosters/payments and private-stock consumption
// execute; object lookup, campaign environment, prices, RNG and events are fixtures.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using AgesOfCalradia.WorkshopProcurement;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

public static class NativeProcurementFixture
{
    sealed class Store : IDataStore
    {
        public bool IsSaving { get; set; }
        public bool IsLoading { get { return !IsSaving; } }
        public string Payload;
        public bool SyncData<T>(string key, ref T data)
        {
            if(key!="aoc_workshop_procurement_v1") throw new Exception("Save key changed");
            if(IsSaving) Payload=(string)(object)data; else data=(T)(object)Payload;
            return true;
        }
    }
    static ProcurementBehavior active;
    static ItemObject iron, wood, output;
    static int quotes, consumed, produced;
    static bool throwConsumed;
    static Workshop quotedShop;
    static int observedBasis;
    static Town supplier;
    static float routeDistance;
    static int outputPrice;
    static int inputQuotes;
    static bool rejectIronQuote;
    static double day;
    static bool trade=true, missingSource;
    static Settlement destinationLookup, sourceLookup;
    static bool Clock(ref double __result) { __result=day;return false; }
    static bool SettlementLookup(string __0,ref Settlement __result)
    { __result=__0=="destination"?destinationLookup:missingSource?null:sourceLookup;return false; }
    static bool Trade(ref bool __result) { __result=trade;return false; }
    static bool PlayerOwned(ref bool __result) { __result=false;return false; }
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Field(object obj,string key,object value) { AccessTools.Field(obj.GetType(),key).SetValue(obj,value); }
    static void Id(object obj,string value) { AccessTools.Property(obj.GetType(),"StringId").SetValue(obj,value,null); }
    static bool Current(ref ProcurementBehavior __result) { __result=active; return false; }
    static bool Eligible(ref bool __result) { __result=true; return false; }
    static bool Lookup(string __0,ref ItemObject __result) { __result=__0=="iron"?iron:__0=="wood"?wood:null; return false; }
    static bool Silent() { return false; }
    static bool Selected(ref EquipmentElement __result) { __result=new EquipmentElement(output); return false; }
    static bool Price(ref int __result) { observedBasis=ProcurementDiagnostics.PrepaidInputCost(quotedShop); __result=(quotes++%2)==0?40:60; return false; }
    static bool InputPrice(ItemObject __0,ref int __result) { if(__0!=output) inputQuotes++; __result=__0==output?outputPrice:__0==iron&&rejectIronQuote?0:10;return false; }
    static bool Suppliers(ref IEnumerable<Town> __result) { __result=new[]{supplier};return false; }
    static bool Catalog(ref ItemObject[] __result) { __result=new[]{output};return false; }
    static bool Distance(ref float __result) { __result=routeDistance;return false; }
    static bool Reserve(ref int __result) { __result=1;return false; }
    static bool cadenceUnavailable;
    static double cadenceRate=0.75;
    static bool Cadence(ref double __result) { if(cadenceUnavailable)throw new MissingMethodException("synthetic cadence bridge failure");__result=cadenceRate;return false; }
    static bool Expense(ref int __result) { __result=23;return false; }
    static bool WineEnabled(ref bool __result) { __result=Campaign.Current.GameStarted;return false; }
    static Hero fixturePlayer;
    static bool MainHero(ref Hero __result) { __result=fixturePlayer;return false; }
    static double actualMargin;
    static void ReadMargin(Workshop __1) { actualMargin=ProcurementDiagnostics.ObservedApprovalMargin(__1); }
    static bool Consumed() { consumed++; if(throwConsumed) throw new InvalidOperationException("injected native consumption callback failure"); return false; }
    static bool Produced() { produced++; return false; }
    static void Patch(Harmony h,MethodBase target,string name) { h.Patch(target,new HarmonyMethod(typeof(NativeProcurementFixture),name)); }
    public static string Run(Assembly module, Func<string> baselineRun)
    {
        var plans=new List<string>();
        Action<string,string,string> plan=(shopKey,reason,detail)=>plans.Add(shopKey+" "+reason+" "+detail);
        ProcurementDiagnostics.PlanObserved+=plan;
        var movementStages=new HashSet<string>();var receiptIds=new HashSet<string>();var movementErrors=new List<string>();
        Action<string,string,string,string,string,string,int,int,int,int> movement=(id,order,stage,town,item,category,mb,ma,cb,ca)=>{
            movementStages.Add(stage);
            if(!receiptIds.Add(id)||string.IsNullOrEmpty(order)||string.IsNullOrEmpty(item)||string.IsNullOrEmpty(category))movementErrors.Add("identity");
            if(stage=="dispatch"||stage=="return"||stage=="liquidation"){
                if(ma-mb+ca-cb!=0||string.IsNullOrEmpty(town))movementErrors.Add("conservation");
            }else if(stage=="arrival"){if(ma!=mb||ca!=cb)movementErrors.Add("arrival minted stock");}
            else if(stage=="consumption"){if(ma!=mb||ca>=cb)movementErrors.Add("consumption");}
            else movementErrors.Add("stage");
        };
        ProcurementDiagnostics.MovementObserved+=movement;
        var accountingStages=new HashSet<string>();
        Action<string,string,string,string,int,int,int,int,int> accounting=(id,order,stage,shop,before,after,cash,goods,freight)=>{
            accountingStages.Add(stage);
            if(!receiptIds.Add(id)||string.IsNullOrEmpty(order)||string.IsNullOrEmpty(shop)||before<0||after<0)
                movementErrors.Add("accounting identity/basis");
            if(stage=="dispatch" && (before!=0||after!=goods+freight||cash!=-after))movementErrors.Add("dispatch accounting");
            if(stage=="consumption" && (cash!=0||after>before))movementErrors.Add("consumption accounting");
            if((stage=="return"||stage=="liquidation") && (after!=0||cash<0))movementErrors.Add("return accounting");
        };
        ProcurementDiagnostics.AccountingObserved+=accounting;
        var h=new Harmony("aoc.procurement.fixture");
        var current=AccessTools.Field(typeof(Campaign),"<Current>k__BackingField");
        var previous=current.GetValue(null);
        try
        {
            Patch(h,AccessTools.PropertyGetter(typeof(ProcurementBehavior),"Current"),"Current");
            Patch(h,AccessTools.Method(typeof(ProcurementBehavior),"ResolveItem"),"Lookup");
            Patch(h,AccessTools.Method(typeof(ProcurementBehavior),"Notice"),"Silent");
            Patch(h,AccessTools.Method(module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementPlanner"),"Eligible"),"Eligible");
            Patch(h,AccessTools.Method(module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementLog"),"Write"),"Silent");
            active=null;
            // Verify every existing AI/player/warehouse fixture with the new hooks installed but no ledger.
            string baseline=baselineRun();
            var campaign=Blank<Campaign>(); current.SetValue(null,campaign);
            Field(campaign,"GameStarted",true);
            Field(campaign,"<CampaignEventDispatcher>k__BackingField",Blank<CampaignEventDispatcher>());
            Patch(h,AccessTools.Method(typeof(WorkshopsCampaignBehavior),"GetRandomItemAux"),"Selected");
            Patch(h,AccessTools.Method(typeof(Town),"GetItemPrice",new[]{typeof(EquipmentElement),typeof(MobileParty),typeof(bool)}),"Price");
            Patch(h,AccessTools.Method(typeof(Town),"GetItemPrice",new[]{typeof(ItemObject),typeof(MobileParty),typeof(bool)}),"InputPrice");
            Patch(h,AccessTools.Method(typeof(CampaignEventDispatcher),"OnItemConsumed"),"Consumed");
            Patch(h,AccessTools.Method(typeof(CampaignEventDispatcher),"OnItemProduced"),"Produced");
            var ci=new ItemCategory();Id(ci,"iron");var cw=new ItemCategory();Id(cw,"wood");var co=new ItemCategory();Id(co,"output");
            Field(ci,"<IsTradeGood>k__BackingField",true);Field(cw,"<IsTradeGood>k__BackingField",true);Field(co,"<IsTradeGood>k__BackingField",true);
            iron=new ItemObject();Id(iron,"iron");Field(iron,"<ItemCategory>k__BackingField",ci);
            wood=new ItemObject();Id(wood,"wood");Field(wood,"<ItemCategory>k__BackingField",cw);
            output=new ItemObject();Id(output,"output");Field(output,"<ItemCategory>k__BackingField",co);
            var settlement=Blank<Settlement>();Id(settlement,"destination");var town=Blank<Town>();
            settlement.Town=town;Field(settlement,"<SettlementComponent>k__BackingField",town);
            var party=Blank<PartyBase>();Field(party,"<Settlement>k__BackingField",settlement);Field(town,"_owner",party);
            var shop=Blank<Workshop>();Field(shop,"_settlement",settlement);Field(shop,"_tag","shop");
            quotedShop=shop;
            var type=Blank<WorkshopType>();Id(type,"smithy");Field(type,"<IsHidden>k__BackingField",false);Field(shop,"<WorkshopType>k__BackingField",type);
            var recipe=new WorkshopType.Production(4f);
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_inputs").GetValue(recipe)).Add(ValueTuple.Create(ci,2));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_inputs").GetValue(recipe)).Add(ValueTuple.Create(cw,1));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_outputs").GetValue(recipe)).Add(ValueTuple.Create(co,2));
            Field(type,"_productions",new TaleWorlds.Library.MBList<WorkshopType.Production>{recipe});
            var native=Blank<WorkshopsCampaignBehavior>();
            var cycle=AccessTools.Method(typeof(WorkshopsCampaignBehavior),"TickOneProductionCycleForNotableWorkshop");
            // Exercise actual dispatch and cash-funded return with native wallets/rosters.
            active=new ProcurementBehavior(true);
            var sourceTown=Blank<Town>();var sourceSettlement=Blank<Settlement>();Id(sourceSettlement,"source");
            sourceSettlement.Town=sourceTown;Field(sourceSettlement,"<SettlementComponent>k__BackingField",sourceTown);
            var sourceParty=Blank<PartyBase>();Field(sourceParty,"<Settlement>k__BackingField",sourceSettlement);
            Field(sourceTown,"_owner",sourceParty);var sourceRoster=new ItemRoster();
            sourceRoster.AddToCounts(iron,100);sourceRoster.AddToCounts(wood,100);Field(sourceParty,"<ItemRoster>k__BackingField",sourceRoster);
            Field(sourceTown,"<Gold>k__BackingField",500);Field(shop,"<Capital>k__BackingField",1000);
            Field(town,"<Gold>k__BackingField",1000);Field(party,"<ItemRoster>k__BackingField",new ItemRoster());
            supplier=sourceTown;routeDistance=100;outputPrice=100;
            var planner=module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementPlanner");
            Patch(h,AccessTools.Method(planner,"Suppliers"),"Suppliers");
            Patch(h,AccessTools.Method(planner,"Catalog"),"Catalog");
            Patch(h,AccessTools.Method(planner,"RouteDistance"),"Distance");
            trade=true;missingSource=false;
            Patch(h,AccessTools.Method(planner,"CanTrade"),"Trade");
            Patch(h,AccessTools.Method(planner,"LocalRecipeUnits"),"Reserve");
            Patch(h,AccessTools.Method(module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementCadence"),"DailyRate"),"Cadence");
            var cadenceType=module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementCadence");
            float authoritativeBase=(float)AccessTools.Method(cadenceType,"BaseSpeed").Invoke(null,new object[]{recipe});
            if(authoritativeBase<=0 || float.IsNaN(authoritativeBase) || float.IsInfinity(authoritativeBase))
                throw new Exception("Calendar recipe base bridge did not bind actual sidecar");
            Patch(h,AccessTools.PropertyGetter(typeof(Workshop),"Expense"),"Expense");
            // Exact native margin gate, not a replacement implementation. Shared
            // policy must also reach planning while cash/player/init gates survive.
            var wineType=module.GetType("AgesOfCalradia.WorkshopProcurement.WineOperatingMargin",true);
            var calculate=AccessTools.Method(wineType,"Calculate");
            if((float)calculate.Invoke(null,new object[]{80f,23,0.6d,1.25d})!=48f
                || (float)calculate.Invoke(null,new object[]{80f,23,0.1d,1.25d})!=80f
                || (float)calculate.Invoke(null,new object[]{80f,23,double.NaN,1.25d})!=80f
                || (float)calculate.Invoke(null,new object[]{80f,23,0.6d,0d})!=80f)
                throw new Exception("Wine operating coverage or conservative fallback failed");
            Patch(h,AccessTools.Method(wineType,"Enabled"),"WineEnabled");
            Patch(h,AccessTools.PropertyGetter(typeof(Hero),"MainHero"),"MainHero");
            var wineRecipe=new WorkshopType.Production(2.5f);
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_inputs").GetValue(wineRecipe)).Add(ValueTuple.Create(ci,2));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_inputs").GetValue(wineRecipe)).Add(ValueTuple.Create(cw,1));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(),"_outputs").GetValue(wineRecipe)).Add(ValueTuple.Create(co,2));
            Field(shop,"_owner",Blank<Hero>());Id(co,"wine");
            Field(type,"_productions",new TaleWorlds.Library.MBList<WorkshopType.Production>{wineRecipe});
            cadenceRate=0.6;
            var policyType=module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementPolicy",true);
            var settingsField=AccessTools.Field(policyType,"Settings");
            var gate=AccessTools.Method(typeof(WorkshopsCampaignBehavior),"CanNotableWorkshopProduceThisCycle");
            h.Patch(gate,postfix:new HarmonyMethod(typeof(NativeProcurementFixture),"ReadMargin"));
            Func<int,int,bool,bool> approved=(input,value,capital)=> (bool)gate.Invoke(native,new object[]{wineRecipe,shop,input,value,capital});
            if(approved(20,70,true))throw new Exception("Disabled wine policy changed native margin");
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement WineExpenseCoverage='1.25'/></CampaignSystems>"));
            if(!approved(20,70,true))throw new Exception("Native wine rejected expense-covering batch; margin="+AccessTools.Method(wineType,"Margin").Invoke(null,new object[]{wineRecipe,shop,true}));
            if(actualMargin!=48 || !double.IsNaN(ProcurementDiagnostics.ObservedApprovalMargin(shop)))
                throw new Exception("Margin diagnostics not observed or scope leaked");
            if(approved(20,68,true) || approved(20,70,false))throw new Exception("Wine strict comparison or noncapital exclusion failed");
            Field(town,"<Gold>k__BackingField",69);
            if(approved(20,70,true))throw new Exception("Wine bypassed town cash");
            Field(town,"<Gold>k__BackingField",1000);Field(shop,"<Capital>k__BackingField",19);
            if(approved(20,70,true))throw new Exception("Wine bypassed input capital");
            Field(shop,"<Capital>k__BackingField",1000);
            cadenceUnavailable=true;
            if(approved(20,70,true))throw new Exception("Wine guessed missing cadence");
            cadenceUnavailable=false;
            fixturePlayer=shop.Owner;
            if(approved(20,70,true))throw new Exception("Wine policy changed player workshop");
            fixturePlayer=null;
            Field(type,"<IsHidden>k__BackingField",true);
            if(!approved(20,21,true))throw new Exception("Hidden native hurdle changed");
            Field(type,"<IsHidden>k__BackingField",false);
            Field(campaign,"GameStarted",false);
            if(!approved(20,21,true) || actualMargin!=80)throw new Exception("World initialization margin changed");
            Field(campaign,"GameStarted",true);
            Id(co,"output");
            if(approved(20,70,true))throw new Exception("Wine policy affected non-wine recipe");
            Id(co,"wine");
            var wineOffer=AccessTools.Method(planner,"Find").Invoke(null,new object[]{shop,20d,null});
            if(wineOffer==null || (float)AccessTools.Field(wineOffer.GetType(),"RequiredMargin").GetValue(wineOffer)!=48f)
                throw new Exception("Planner does not use native wine margin policy");
            Field(shop,"_owner",null);
            if(approved(20,70,true))throw new Exception("Ownerless wine production changed");
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults);
            Id(co,"output");Field(type,"_productions",new TaleWorlds.Library.MBList<WorkshopType.Production>{recipe});
            cadenceRate=0.75;
            var find=AccessTools.Method(planner,"Find");
            object[] query={shop,20d,null};
            cadenceUnavailable=true;
            if(find.Invoke(null,query)!=null || !(bool)AccessTools.Property(typeof(ProcurementBehavior),"Enabled").GetValue(active,null))
                throw new Exception("Unavailable cadence must stop new offers, not disable saved cargo service");
            cadenceUnavailable=false;
            inputQuotes=0;rejectIronQuote=false;
            var selected=find.Invoke(null,query);
            if(inputQuotes!=2)throw new Exception("Supplier input quote queried more than once per candidate item");
            if(selected==null)throw new Exception("Funded profitable multi-input bundle rejected: "+query[2]);
            var selectedOrder=(ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected);
            if(selectedOrder.GoodsCost!=90 || selectedOrder.FreightCost!=11 || selectedOrder.ArrivalDay!=22 || selectedOrder.Lines.Count!=2)
                throw new Exception("Supplier quote, freight or ETA incorrect");
            var policy=module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementPolicy");
            // Full Find regression: seven batches violate the supplier reserve,
            // three fail the strict margin, but intermediate batches are viable.
            var intermediateRecipe=new WorkshopType.Production(2.5f);
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(intermediateRecipe.GetType(),"_inputs").GetValue(intermediateRecipe)).Add(ValueTuple.Create(cw,1));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(intermediateRecipe.GetType(),"_outputs").GetValue(intermediateRecipe)).Add(ValueTuple.Create(co,1));
            Field(type,"_productions",new TaleWorlds.Library.MBList<WorkshopType.Production>{intermediateRecipe});
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement MaximumAdaptiveBatches='12'/></CampaignSystems>"));
            cadenceRate=2;routeDistance=150;outputPrice=93;sourceRoster.AddToCounts(wood,-84);inputQuotes=0;
            selected=find.Invoke(null,query);
            if(selected==null)throw new Exception("Intermediate-lot gap: Find rejected feasible batches four through six");
            var intermediateOrder=(ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected);
            if(intermediateOrder.Quantity!=6 || intermediateOrder.GoodsCost!=60 || intermediateOrder.FreightCost!=11
                || intermediateOrder.Lines[0].Remaining!=6 || intermediateOrder.ArrivalDay!=22.5)
                throw new Exception("Intermediate-lot selection or cost/stock/ETA accounting incorrect");
            if(inputQuotes!=1)throw new Exception("Adaptive search repeated the same supplier item quote");
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement MaximumAdaptiveBatches='100'/></CampaignSystems>"));
            cadenceRate=40;inputQuotes=0;
            var candidateWitnesses=new List<string>();
            Action<string,string,string> witnessListener=(s,r,d)=>candidateWitnesses.Add(r+"; "+d);
            string selectedPlanEvidence = null;
            Action<string,string,string> planListener=(s,r,d)=>selectedPlanEvidence=d;
            ProcurementDiagnostics.CandidateObserved+=witnessListener;
            ProcurementDiagnostics.PlanObserved+=planListener;
            try { selected=find.Invoke(null,query); }
            finally { ProcurementDiagnostics.CandidateObserved-=witnessListener; ProcurementDiagnostics.PlanObserved-=planListener; }
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Quantity!=6 || inputQuotes!=1)
                throw new Exception("Maximum bounded search missed feasible lot or repeated native quotes");
            string selectedPlanId=(string)AccessTools.Field(selected.GetType(),"DiagnosticPlanId").GetValue(selected);
            if(string.IsNullOrEmpty(selectedPlanId) || selectedPlanEvidence==null || !selectedPlanEvidence.Contains("plan="+selectedPlanId+";"))
                throw new Exception("Selected offer lacks exact ephemeral plan correlation");
            if(!candidateWitnesses.Exists(x=>x.StartsWith("supplier_reserve;",StringComparison.Ordinal))
                || !candidateWitnesses.Exists(x=>x.StartsWith("witness_budget;",StringComparison.Ordinal)))
                throw new Exception("Real planner did not emit bounded causal rejection evidence");
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement MaximumAdaptiveBatches='12'/></CampaignSystems>"));
            cadenceRate=2;
            sourceRoster.AddToCounts(wood,-3);
            if(find.Invoke(null,query)!=null)throw new Exception("Intermediate search bypassed reserve or strict margin");
            sourceRoster.AddToCounts(wood,3);
            Field(shop,"<Capital>k__BackingField",231);
            selected=find.Invoke(null,query);
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Quantity!=5)
                throw new Exception("Intermediate search failed capital-reserve boundary");
            if(shop.Capital!=231 || sourceTown.Gold!=500 || sourceRoster.GetItemNumber(wood)!=16)
                throw new Exception("Intermediate search mutated cash or supplier stock");
            Field(shop,"<Capital>k__BackingField",1000);sourceRoster.AddToCounts(wood,84);
            Field(type,"_productions",new TaleWorlds.Library.MBList<WorkshopType.Production>{recipe});
            settingsField.SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults);
            cadenceRate=0.75;routeDistance=100;outputPrice=100;
            Field(sourceTown,"<Workshops>k__BackingField",new[]{shop});
            cadenceRate=2;
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='14' MaximumAdaptiveBatches='12' DeliveryDelayCostWeight='1'/></CampaignSystems>"));
            selected=find.Invoke(null,query);
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Quantity!=6)
                throw new Exception("Combined policy did not select a reserve-safe lead-time-sized order");
            Field(shop,"<Capital>k__BackingField",300);
            selected=find.Invoke(null,query);
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Quantity!=4)
                throw new Exception("Combined policy failed best affordable intermediate lot while protecting wages");
            Field(shop,"<Capital>k__BackingField",1000);
            cadenceRate=0.75;
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement MaximumAdaptiveBatches='6' ReorderBufferDays='5' DeliveryDelayCostWeight='1'/></CampaignSystems>"));
            selected=find.Invoke(null,query);
            var adaptiveOrder=(ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected);
            if(adaptiveOrder.Quantity!=6 || adaptiveOrder.GoodsCost!=180 || adaptiveOrder.FreightCost!=20)
                throw new Exception("Adaptive multi-input lot failed to carry all cost/quantity fields");
            adaptiveOrder.TransferComplete=true;
            var adaptiveLedger=new ProcurementState();adaptiveLedger.Orders.Add(adaptiveOrder);
            var adaptiveReload=ProcurementState.Decode(adaptiveLedger.Encode()).Orders[0];
            if(adaptiveReload.OriginalQuantity!=6 || adaptiveReload.Lines[0].Remaining!=12 || adaptiveReload.CostOf(6)!=200)
                throw new Exception("Adaptive lot lost stock or cost basis across save serialization");
            sourceRoster.AddToCounts(iron,-80);
            selected=find.Invoke(null,query);
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Quantity!=5)
                throw new Exception("Large lot must fall back to best funded reserve-safe intermediate lot");
            sourceRoster.AddToCounts(iron,80);
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults);
            Field(sourceTown,"<Workshops>k__BackingField",new[]{shop});
            var supplierDemand=AccessTools.Method(planner,"LocalDailyUnits");
            Field(type,"<IsHidden>k__BackingField",true);
            if((double)supplierDemand.Invoke(null,new object[]{sourceTown,ci})!=1.5)
                throw new Exception("Supplier demand must include hidden artisans and per-recipe input units");
            Field(type,"<IsHidden>k__BackingField",false);
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='14'/></CampaignSystems>"));
            // 1.5 iron/day * 14 = 21 retained; this recipe orders six iron.
            sourceRoster.AddToCounts(iron,-73);
            if(find.Invoke(null,query)==null)throw new Exception("Iron day reserve exact boundary rejected");
            sourceRoster.AddToCounts(iron,-1);
            if(find.Invoke(null,query)!=null)throw new Exception("Iron day reserve drained");
            sourceRoster.AddToCounts(iron,74);
            cadenceUnavailable=true;
            if(!double.IsNaN((double)supplierDemand.Invoke(null,new object[]{sourceTown,ci})))
                throw new Exception("Unknown supplier cadence became known zero demand");
            cadenceUnavailable=false;
            Field(sourceTown,"<Workshops>k__BackingField",new Workshop[0]);
            if((double)supplierDemand.Invoke(null,new object[]{sourceTown,ci})!=0)
                throw new Exception("Known no local recipes should have zero demand");
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults);
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement BatchesPerOrder='5' StockLifetimeDays='60' BlockedReturnDays='21'/></CampaignSystems>"));
            selected=find.Invoke(null,query);
            selectedOrder=(ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected);
            if(selectedOrder.Quantity!=5 || selectedOrder.GoodsCost!=150 || selectedOrder.StockLifetimeDays!=60 || selectedOrder.ReturnDelayDays!=21)
                throw new Exception("Planner ignored Core configuration or failed to snapshot order policy");
            AccessTools.Field(policy,"Settings").SetValue(null,AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults);
            var alternative=new ItemObject();Id(alternative,"iron_alt");Field(alternative,"<ItemCategory>k__BackingField",ci);
            sourceRoster.AddToCounts(alternative,100);rejectIronQuote=true;
            selected=find.Invoke(null,query);
            if(selected==null || ((ProcurementOrder)AccessTools.Field(selected.GetType(),"Order").GetValue(selected)).Lines[0].Item!="iron_alt")
                throw new Exception("Invalid cheapest quote masked viable supplier input");
            sourceRoster.AddToCounts(alternative,-100);rejectIronQuote=false;
            sourceRoster.AddToCounts(iron,-85);
            if(find.Invoke(null,query)!=null)throw new Exception("Supplier reserves drained");
            sourceRoster.AddToCounts(iron,85);Field(shop,"<Capital>k__BackingField",200);
            if(find.Invoke(null,query)!=null)throw new Exception("Working capital reserve spent");
            Field(shop,"<Capital>k__BackingField",1000);outputPrice=20;
            if(find.Invoke(null,query)!=null)throw new Exception("Unprofitable freight approved");
            outputPrice=100;routeDistance=float.NaN;
            if(find.Invoke(null,query)!=null)throw new Exception("Invalid route approved");
            routeDistance=100;party.ItemRoster.AddToCounts(iron,2);party.ItemRoster.AddToCounts(wood,1);
            if(find.Invoke(null,query)==null)throw new Exception("One batch cannot cover route lead time; early reorder missing");
            party.ItemRoster.AddToCounts(iron,4);party.ItemRoster.AddToCounts(wood,2);
            if(find.Invoke(null,query)!=null)throw new Exception("Lead-time-sufficient market stock caused redundant shipment");
            if(!plans.Exists(p=>p.Contains("selectedThresholdBatches=3") && p.Contains("selectedDailyRate=0.75"))
                || !plans.Exists(p=>p.Contains("categoryReserveRejected=1"))
                || !plans.Exists(p=>p.Contains("capitalRejected=1")))
                throw new Exception("Planner ABI omitted forecast or controlling supplier filter evidence");
            party.ItemRoster.AddToCounts(iron,-4);party.ItemRoster.AddToCounts(wood,-2);
            if(shop.Capital!=1000 || sourceTown.Gold!=500 || sourceRoster.GetItemNumber(iron)!=100 || sourceRoster.GetItemNumber(wood)!=100)
                throw new Exception("Read-only planning mutated accounts");
            var dispatchOrder=new ProcurementOrder {Town="destination",Workshop="shop",Type="smithy",Source="source",Recipe=0,
                Quantity=3,OriginalQuantity=3,GoodsCost=60,FreightCost=6,DepartureDay=20,ArrivalDay=22,
                Lines=new List<ProcurementLine>{new ProcurementLine{Item="iron",Category="iron",UnitsPerBatch=2,Remaining=6},
                    new ProcurementLine{Item="wood",Category="wood",UnitsPerBatch=1,Remaining=3}}};
            var offer=Activator.CreateInstance(module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementOffer"),true);
            Field(offer,"Order",dispatchOrder);Field(offer,"Source",sourceTown);Field(offer,"Capital",1000);Field(offer,"SourceGold",500);
            Field(offer,"Items",new List<ItemObject>{iron,wood});Field(offer,"Stocks",new List<int>{100,100});
            var dispatch=AccessTools.Method(typeof(ProcurementBehavior),"Dispatch");
            dispatchOrder.Lines[0].Item="wrong-item";
            bool invalidRejected=false;
            try{dispatch.Invoke(active,new[]{(object)shop,offer});}catch(TargetInvocationException){invalidRejected=true;}
            if(!invalidRejected || shop.Capital!=1000 || sourceTown.Gold!=500 || ProcurementDiagnostics.PrivateStock(shop,"iron")!=0)
                throw new Exception("Invalid offer created cargo or moved money");
            dispatchOrder.Lines[0].Item="iron";
            dispatchOrder.Type=new string('x',4000000);
            dispatch.Invoke(active,new[]{(object)shop,offer});
            if(shop.Capital!=1000 || sourceTown.Gold!=500 || sourceRoster.GetItemNumber(iron)!=100
                || ProcurementDiagnostics.PrivateStock(shop,"iron")!=0)
                throw new Exception("Capacity rejection charged wallets or removed inventory");
            dispatchOrder.Type="smithy";
            dispatch.Invoke(active,new[]{(object)shop,offer});
            if(shop.Capital!=934 || sourceTown.Gold!=566 || sourceRoster.GetItemNumber(iron)!=94 || sourceRoster.GetItemNumber(wood)!=97)
                throw new Exception("Native dispatch money/stock conservation failed");
            bool duplicateRejected=false;
            try{dispatch.Invoke(active,new[]{(object)shop,offer});}catch(TargetInvocationException){duplicateRejected=true;}
            if(!duplicateRejected || shop.Capital!=934 || sourceTown.Gold!=566)throw new Exception("Duplicate dispatch charged again");
            var liquidate=AccessTools.Method(typeof(ProcurementBehavior),"Liquidate");
            Field(sourceTown,"<Gold>k__BackingField",0);
            liquidate.Invoke(active,new object[]{shop,dispatchOrder,sourceTown,true});
            if(shop.Capital!=934 || sourceRoster.GetItemNumber(iron)!=94)throw new Exception("Unfunded refund manufactured gold");
            Field(sourceTown,"<Gold>k__BackingField",566);
            liquidate.Invoke(active,new object[]{shop,dispatchOrder,sourceTown,true});
            if(shop.Capital!=994 || sourceTown.Gold!=506 || sourceRoster.GetItemNumber(iron)!=100 || sourceRoster.GetItemNumber(wood)!=100)
                throw new Exception("Native return did not conserve goods/cash or charged freight twice");
            bool replayRejected=false;
            try{liquidate.Invoke(active,new object[]{shop,dispatchOrder,sourceTown,true});}catch(TargetInvocationException){replayRejected=true;}
            if(!replayRejected || shop.Capital!=994 || sourceTown.Gold!=506)throw new Exception("Refund replay allowed");
            Patch(h,AccessTools.Method(typeof(ProcurementBehavior),"Now"),"Clock");
            Patch(h,AccessTools.Method(typeof(ProcurementBehavior),"ResolveSettlement"),"SettlementLookup");
            Patch(h,AccessTools.Method(planner,"Safe"),"Eligible");
            Patch(h,AccessTools.Method(typeof(ProcurementBehavior),"PlayerOwned"),"PlayerOwned");
            destinationLookup=settlement;sourceLookup=sourceSettlement;
            Field(town,"<Workshops>k__BackingField",new[]{shop});
            Field(shop,"_owner",Blank<Hero>());
            active=new ProcurementBehavior(true);
            dispatchOrder.TransferComplete=true;
            dispatchOrder.Lines[0].Remaining=6;dispatchOrder.Lines[1].Remaining=3;
            var deliveryState=new ProcurementState();deliveryState.Orders.Add(dispatchOrder);Field(active,"_state",deliveryState);
            Field(party,"<ItemRoster>k__BackingField",new ItemRoster());
            var hourly=AccessTools.Method(typeof(ProcurementBehavior),"Hourly");
            day=21;hourly.Invoke(active,null);
            if(dispatchOrder.Arrived)throw new Exception("Hourly delivered before ETA");
            day=40;hourly.Invoke(active,null);
            if(!dispatchOrder.Arrived || dispatchOrder.DeliveredDay!=40 || deliveryState.Orders.Count!=1 || deliveryState.Fault!=null)
                throw new Exception("Hourly delayed arrival failed: "+deliveryState.Fault);
            trade=false;day=60;hourly.Invoke(active,null);
            if(dispatchOrder.ReturnDay!=0 || deliveryState.Orders.Count!=1)throw new Exception("Delivered cargo returned after supplier war");
            missingSource=true;day=69.99;hourly.Invoke(active,null);
            if(deliveryState.Orders.Count!=1)throw new Exception("Stock expired before actual-arrival lifetime");
            day=70;int beforeGold=town.Gold,beforeCapital=shop.Capital;hourly.Invoke(active,null);
            if(deliveryState.Fault!=null || deliveryState.Orders.Count!=0 || shop.Capital!=beforeCapital+90 || town.Gold!=beforeGold-90
                || party.ItemRoster.GetItemNumber(iron)!=6 || party.ItemRoster.GetItemNumber(wood)!=3)
                throw new Exception("Missing supplier blocked funded local expiry liquidation: "+deliveryState.Fault);
            trade=true;missingSource=false;
            int cases=0;
            foreach(int cash in new[]{99,1000}) foreach(bool arrived in new[]{false,true}) foreach(bool capital in new[]{false,true})
            foreach(int startingCapital in new[]{1,1000})
            {
                active=new ProcurementBehavior(true);
                var state=new ProcurementState();
                var order=new ProcurementOrder {Town="destination",Workshop="shop",Type="smithy",Source="source",Recipe=0,
                    Quantity=3,OriginalQuantity=3,GoodsCost=60,FreightCost=6,DepartureDay=20,ArrivalDay=22,
                    Arrived=arrived,TransferComplete=true,Lines=new List<ProcurementLine>{
                        new ProcurementLine{Item="iron",Category="iron",UnitsPerBatch=2,Remaining=6},
                        new ProcurementLine{Item="wood",Category="wood",UnitsPerBatch=1,Remaining=3}}};
                state.Orders.Add(order);Field(active,"_state",state);
                string ledgerBeforeEvidence = state.Encode();
                int quotesBeforeEvidence = inputQuotes;
                string inputEvidence = ProcurementDiagnostics.InputEvidence(shop,"iron");
                if(!inputEvidence.Contains(arrived ? "reservedPrivate=6" : "inTransitPrivate=6")
                    || !inputEvidence.Contains("eligiblePrivate=0") || state.Encode()!=ledgerBeforeEvidence || inputQuotes!=quotesBeforeEvidence)
                    throw new Exception("Private cargo observation invented eligibility, changed save or queried a quote");
                order.TransferComplete=false;
                if(!ProcurementDiagnostics.InputEvidence(shop,"iron").Contains("unknownPrivate=6"))throw new Exception("Partial stock fabricated as usable");
                order.TransferComplete=true; order.ReturnDay=25;
                if(!ProcurementDiagnostics.InputEvidence(shop,"iron").Contains("blockedPrivate=6"))throw new Exception("Returning stock fabricated as available");
                order.ReturnDay=0;
                Field(active,"_invalidSave",true);
                if(!ProcurementDiagnostics.InputEvidence(shop,"iron").Contains("eligiblePrivate=unknown"))throw new Exception("Quarantined stock fabricated as zero");
                Field(active,"_invalidSave",false);
                if(state.Encode()!=ledgerBeforeEvidence || inputQuotes!=quotesBeforeEvidence)throw new Exception("Evidence changed native queries or saved ledger");
                string observedPayload=null; int saveObservations=0;
                Action<string,string> ledgerListener=(stage,payload)=>{
                    if(stage!="serialized_for_save") throw new Exception("Incorrect ledger observation stage");
                    observedPayload=payload; saveObservations++;
                };
                ProcurementDiagnostics.LedgerObserved+=ledgerListener;
                var store=new Store{IsSaving=true};
                try { active.SyncData(store); }
                finally { ProcurementDiagnostics.LedgerObserved-=ledgerListener; }
                if(saveObservations!=1 || observedPayload!=store.Payload)
                    throw new Exception("Save observation differs from native stored payload");
                active=new ProcurementBehavior(true);store.IsSaving=false;active.SyncData(store);
                if(ProcurementDiagnostics.LoadedLedger()!=store.Payload)
                    throw new Exception("Loaded ledger observation changed saved obligations");
                state=(ProcurementState)AccessTools.Field(typeof(ProcurementBehavior),"_state").GetValue(active);
                order=state.Orders[0];
                string snapshot=ProcurementDiagnostics.CurrentLedger();
                if(snapshot!=state.Encode() || ProcurementDiagnostics.LoadedLedger()!=store.Payload)
                    throw new Exception("Current observation changed ledger/save payload");
                var observedOrder=ProcurementState.Decode(snapshot).Orders[0];
                if(observedOrder.GoodsCost!=order.GoodsCost || observedOrder.FreightCost!=order.FreightCost
                    || observedOrder.Quantity!=order.Quantity || observedOrder.Lines[0].Remaining!=order.Lines[0].Remaining)
                    throw new Exception("Current observation omitted cargo/cost basis");
                var roster=new ItemRoster();Field(party,"<ItemRoster>k__BackingField",roster);
                Field(town,"<Gold>k__BackingField",cash);Field(shop,"<Capital>k__BackingField",startingCapital);
                quotes=consumed=produced=0;throwConsumed=false;observedBasis=-1;
                bool result=(bool)cycle.Invoke(native,new object[]{recipe,shop,capital});
                bool expected=arrived&&capital&&cash>=100;
                if(result!=expected || !string.IsNullOrEmpty(state.Fault)) throw new Exception("Native procurement gate failed: "+state.Fault
                    + " cash="+cash+" arrived="+arrived+" capital="+capital+" result="+result+" quotes="+quotes
                    + " ready="+(AccessTools.Method(typeof(ProcurementBehavior),"Ready").Invoke(active,new object[]{shop,recipe})!=null));
                if(expected)
                {
                    if(shop.Capital!=startingCapital+100 || town.Gold!=cash-100 || order.Quantity!=2 || consumed!=2 || produced!=2
                        || roster.GetItemNumber(iron)!=0 || roster.GetItemNumber(wood)!=0 || roster.GetItemNumber(output)!=2)
                        throw new Exception("Prepaid inputs charged twice, wrong payment or private inventory mismatch");
                    if(observedBasis!=22 || ProcurementDiagnostics.PrivateStock(shop,"iron")!=4)
                        throw new Exception("Read-only prepaid diagnostic ABI missed cost or cargo");
                }
                else if(shop.Capital!=startingCapital || town.Gold!=cash || order.Quantity!=3 || consumed!=0 || produced!=0)
                    throw new Exception("Rejected/unarrived/noncapital cycle mutated goods or gold");
                if(AccessTools.Field(module.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementPatches"),"Active").GetValue(null)!=null)
                    throw new Exception("Cycle context leaked");
                cases++;
            }
            // Last case has a usable remaining bundle. A native callback failure must
            // quarantine partial consumption, persist it and prevent another attempt.
            throwConsumed=true;quotes=consumed=produced=0;
            try{cycle.Invoke(native,new object[]{recipe,shop,true});throw new Exception("Expected injected failure");}
            catch(TargetInvocationException){ }
            var failed=(ProcurementState)AccessTools.Field(typeof(ProcurementBehavior),"_state").GetValue(active);
            if(string.IsNullOrEmpty(failed.Fault) || string.IsNullOrEmpty(ProcurementState.Decode(failed.Encode()).Fault))
                throw new Exception("Partial native failure did not persist quarantine");
            var corruptStore=new Store{Payload="{unknown broken payload}"};
            var disabled=new ProcurementBehavior(false);disabled.SyncData(corruptStore);
            corruptStore.IsSaving=true;disabled.SyncData(corruptStore);
            if(corruptStore.Payload!="{unknown broken payload}") throw new Exception("Corrupt save was overwritten");
            var missing=new Store{Payload=null};var old=new ProcurementBehavior(false);old.SyncData(missing);
            missing.IsSaving=true;old.SyncData(missing);
            if(ProcurementState.Decode(missing.Payload).Orders.Count!=0) throw new Exception("Old save acquired cargo");
            var fullLedger=ProcurementState.Decode(missing.Payload);
            var heldOrder=new ProcurementOrder {Town="destination",Workshop="shop",Type=new string('x',3940000),Source="source",Recipe=0,
                Quantity=1,OriginalQuantity=1,GoodsCost=10,FreightCost=2,DepartureDay=20,ArrivalDay=22,TransferComplete=true,
                Lines=new List<ProcurementLine>{new ProcurementLine{Item="iron",Category="iron",UnitsPerBatch=1,Remaining=1}}};
            fullLedger.Orders.Add(heldOrder);string heldPayload=fullLedger.Encode();
            var heldStore=new Store{Payload=heldPayload};var heldBehavior=new ProcurementBehavior(true);heldBehavior.SyncData(heldStore);
            var status=(string)AccessTools.Method(typeof(ProcurementBehavior),"Status").Invoke(heldBehavior,null);
            if(!status.Contains("enabled=False"))throw new Exception("No-headroom legacy ledger remained active");
            heldStore.IsSaving=true;heldBehavior.SyncData(heldStore);
            if(heldStore.Payload!=heldPayload)throw new Exception("Capacity hold did not preserve original save payload");
            if(movementErrors.Count!=0 || !movementStages.IsSupersetOf(new[]{"dispatch","return","liquidation","arrival","consumption"})
                || !accountingStages.IsSupersetOf(new[]{"dispatch","return","liquidation","consumption"}))
                throw new Exception("Movement receipt coverage/conservation failed: "+string.Join(",",movementErrors));
            return "PASS: committed receipt identities and all five lifecycle stages; supplier selection including intermediate lots, maximum bounded search, quote reuse and editable Core policy; native dispatch/duplicate rejection/cash-limited return/refund replay; hourly delayed arrival, supplier war/removal and funded local expiry; "+cases+" production cases with behavior save/load, missing/corrupt saves and partial-failure quarantine; "+baseline;
        }
        finally {ProcurementDiagnostics.PlanObserved-=plan;ProcurementDiagnostics.AccountingObserved-=accounting;ProcurementDiagnostics.MovementObserved-=movement;active=null;quotedShop=null;throwConsumed=false;current.SetValue(null,previous);h.UnpatchAll("aoc.procurement.fixture");}
    }
}
