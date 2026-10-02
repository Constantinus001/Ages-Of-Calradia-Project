using System;
using System.Collections.Generic;
using System.IO;
using AgesOfCalradia.WorkshopProcurement;

public static class ProcurementVerifier
{
    static int checks;
    static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Check(rejected, label);
    }
    static ProcurementOrder Order()
    {
        return new ProcurementOrder { Town="destination", Workshop="shop", Type="smithy", Source="source", Recipe=0,
            Quantity=3, OriginalQuantity=3, GoodsCost=100, FreightCost=7, DepartureDay=20, ArrivalDay=22,
            TransferComplete=true, Lines=new List<ProcurementLine> {
                new ProcurementLine { Item="iron", Category="iron", UnitsPerBatch=2, Remaining=6 },
                new ProcurementLine { Item="wood", Category="wood", UnitsPerBatch=1, Remaining=3 } } };
    }
    public static string Run()
    {
        checks=0;
        Check(ProcurementState.Decode("").Orders.Count==0,"old save defaults");
        var s=new ProcurementState(); var o=Order(); s.Orders.Add(o);
        var restored=ProcurementState.Decode(s.Encode());
        Check(restored.Orders.Count==1 && restored.Orders[0].Lines.Count==2,"multi input save roundtrip");
        Check(restored.Find("destination","shop")!=null,"pending duplicate key lookup survives reload");
        s.Orders.Add(Order()); Reject(()=>s.Validate(),"duplicate order rejected"); s.Orders.RemoveAt(1);
        s.Schema=2; Reject(()=>s.Validate(),"unknown schema rejected"); s.Schema=1;
        Reject(()=>ProcurementState.Decode("garbage"),"corrupt payload rejected");
        o.TransferComplete=false;
        Check(!string.IsNullOrEmpty(ProcurementState.Decode(s.Encode()).Fault),"inflight save quarantined");
        o.TransferComplete=true;
        Reject(()=>s.Consume(o,"iron",2),"in transit cannot consume");
        o.Arrived=true;
        int cost=0;
        for(int b=0;b<3;b++)
        {
            cost+=o.CostOf(1);
            Check(!s.Consume(o,"iron",2),"multi input not completed early");
            Reject(()=>s.Consume(o,"iron",2),"duplicate category consumption blocked");
            Check(s.Consume(o,"wood",1),"complete bundle consumption");
            Check(o.Quantity==2-b,"exactly one batch removed");
            if(b<2) { s=ProcurementState.Decode(s.Encode()); o=s.Orders[0]; }
        }
        Check(cost==107,"recognized costs equal exact original cash including freight");
        Check(s.Orders.Count==0,"consumed order removed and reorder possible");
        Check(o.Lines[0].Remaining==0 && o.Lines[1].Remaining==0,"all input units conserved");
        Reject(()=>s.Consume(o,"wood",1),"replay completed delivery blocked");
        Check(ProcurementPolicy.HasSurplus(106,6,1,true),"food reserve boundary");
        Check(!ProcurementPolicy.HasSurplus(105,6,1,true),"food depletion blocked");
        Check(ProcurementPolicy.HasSurplus(13,3,1,true,"grape"),"grape reserve uses measured category not blanket food floor");
        Check(!ProcurementPolicy.HasSurplus(12,3,1,true,"grape"),"grape minimum retained");
        Check(!ProcurementPolicy.HasSurplus(13,3,2,true,"grape"),"local recipe reserve still protects grape supplier");
        Check(!ProcurementPolicy.HasSurplus(13,3,1,true,"grain"),"unrelated food reserves unchanged");
        Check(ProcurementPolicy.ReorderBatches(0.75,2)==3,"route and buffer drive early reorder");
        Check(ProcurementPolicy.ReorderBatches(0.1,2)==1,"slow recipe not treated as native industrial speed");
        Check(ProcurementPolicy.ReorderBatches(100,4)==3,"reorder bound cannot hoard market stock");
        Check(ProcurementPolicy.ReorderBatches(double.NaN,2)==0 && ProcurementPolicy.ReorderBatches(1,double.PositiveInfinity)==0
            && ProcurementPolicy.ReorderBatches(0,2)==0,"unknown or inactive cadence fails closed");
        Check(!ProcurementPolicy.FoodExportSafe(99,10),"low aggregate food reserve blocks exports");
        Check(!ProcurementPolicy.FoodExportSafe(100,-4),"declining food buffer blocks exports");
        Check(ProcurementPolicy.FoodExportSafe(120,-4),"thirty-day aggregate food buffer boundary");
        Check(ProcurementPolicy.FoodExportSafe(100,0),"stable aggregate food buffer");
        Check(!ProcurementPolicy.FoodExportSafe(100,double.NaN),"unknown food balance blocks exports");
        Check(!ProcurementPolicy.HasSurplus(76,7,10,false),"supplier recipe reserve");
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='14'/></CampaignSystems>");
        Check(ProcurementPolicy.HasSurplus(29,3,21,false,"iron",1.854),"iron fourteen-day exact reserve boundary");
        Check(!ProcurementPolicy.HasSurplus(28,3,21,false,"iron",1.854),"iron reserve cannot be drained");
        Check(ProcurementPolicy.HasSurplus(13,3,12,false,"iron",0),"zero known demand retains minimum stock");
        foreach(double rate in new[]{double.NaN,double.PositiveInfinity,-1,double.MaxValue})
            Check(!ProcurementPolicy.HasSurplus(int.MaxValue,3,12,false,"iron",rate),"invalid or overflowing demand blocks export");
        Check(!ProcurementPolicy.HasSurplus(47,3,12,false,"wool",0.4),"wool unchanged");
        Check(!ProcurementPolicy.HasSurplus(47,3,12,true,"iron",0.4),"food-tagged iron not exempted");
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults;
        Check(!ProcurementPolicy.HasSurplus(47,3,12,false,"iron",0.4),"disabled iron policy retains old reserve");
        Check(ProcurementPolicy.OrderBatches(10,4)==3,"legacy fixed lot unchanged");
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement MaximumAdaptiveBatches='12' DeliveryDelayCostWeight='1'/></CampaignSystems>");
        Check(ProcurementPolicy.OrderBatches(2,4)==10,"lot covers lead plus buffer at actual recipe rate");
        Check(ProcurementPolicy.OrderBatches(100,4)==12,"adaptive lot capped");
        Check(ProcurementPolicy.OrderBatches(0.1,2)==3,"slow recipes retain small lot");
        Check(ProcurementPolicy.OrderBatches(double.NaN,2)==0,"unknown demand not converted into order");
        Check(ProcurementPolicy.OfferScore(100,5,3,23,1,0,1)<ProcurementPolicy.OfferScore(90,5,3,23,4,0,1),"affordable faster offer can outrank cheaper delayed offer");
        Check(ProcurementPolicy.OfferScore(100,5,3,23,1,10,1)>ProcurementPolicy.OfferScore(90,5,3,23,4,10,1),"covered stock does not invent delay expense");
        Check(double.IsPositiveInfinity(ProcurementPolicy.OfferScore(100,5,3,23,1,0,double.NaN)),"unknown forecast not selected");
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults;
        Check(ProcurementPolicy.Affordable(807,100,100,7),"capital exact reserve");
        Check(!ProcurementPolicy.Affordable(806,100,100,7),"wages protected");
        Check(!ProcurementPolicy.Affordable(int.MaxValue,int.MaxValue,100,7),"capital overflow safe");
        Check(!ProcurementPolicy.Profitable(100,7,3,86,50),"break even rejected");
        Check(ProcurementPolicy.Profitable(100,7,3,87,50),"freight counted in positive margin");
        Check(!ProcurementPolicy.Profitable(100,7,3,100,double.NaN),"invalid margin rejected");
        Check(ProcurementPolicy.TravelDays(100)==2,"distance affects ETA");
        Check(ProcurementPolicy.Freight(9,100)==11,"distance and quantity affect fee");
        Reject(()=>ProcurementPolicy.TravelDays(double.NaN),"invalid route rejected");
        Reject(()=>ProcurementPolicy.TravelDays(301),"unbounded route rejected");
        Check(!ProcurementPolicy.Due(21.99,22) && ProcurementPolicy.Due(22,22),"no early arrival");
        o=Order();
        ProcurementPolicy.BeginReturn(o,22,true);
        Check(ProcurementPolicy.BeginReturn(o,35.99,true)==0,"no premature return");
        Check(ProcurementPolicy.BeginReturn(o,36,true)==38,"return incurs travel time");
        Check(ProcurementPolicy.BeginReturn(o,36,false)==0,"unblocked route not cancelled");
        Check(o.BlockedSince==-1 && ProcurementPolicy.BeginReturn(o,100,true)==0,"new siege does not inherit old delivery age");
        o.ReturnDay=38;
        Check(ProcurementPolicy.BeginReturn(o,99,true)==38,"return not perpetually rescheduled");
        o=Order();
        Reject(()=>ProcurementPolicy.BeginReturn(o,double.NaN,true),"invalid clock cannot poison blockage state");
        Check(o.BlockedSince==-1,"invalid clock leaves state unchanged");
        Reject(()=>ProcurementPolicy.MarkArrived(o,21),"early arrival rejected");
        ProcurementPolicy.MarkArrived(o,40);
        Check(o.DeliveredDay==40 && o.Arrived,"actual delayed arrival recorded");
        Check(ProcurementPolicy.BeginReturn(o,60,true)==0,"delivered stock cannot return due to later supplier war");
        Check(!ProcurementPolicy.Expired(o,69.99) && ProcurementPolicy.Expired(o,70),"expiry uses actual arrival not ETA");
        s=new ProcurementState(); s.Orders.Add(o);
        Check(ProcurementState.Decode(s.Encode()).Orders[0].DeliveredDay==40,"actual arrival survives save load");
        o.TransferComplete=false; Reject(()=>s.Consume(o,"iron",2),"unfinished transfer cannot consume");
        o.TransferComplete=true; o.ReturnDay=65; Reject(()=>s.Consume(o,"iron",2),"returning cargo cannot consume");
        o.ReturnDay=0; o.DeliveredDay=0;
        Check(ProcurementPolicy.Expired(o,52),"legacy arrival retains ETA fallback");
        o.DeliveredDay=21; Reject(()=>s.Validate(),"arrival before ETA rejected");
        o=Order();o.ReturnDelayDays=21;o.StockLifetimeDays=60;
        s=new ProcurementState();s.Orders.Add(o);
        s=ProcurementState.Decode(s.Encode());o=s.Orders[0];
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement StockLifetimeDays='5' BlockedReturnDays='2' FreightBase='4' FreightDistancePerDay='50'/></CampaignSystems>");
        ProcurementPolicy.BeginReturn(o,22,true);
        Check(ProcurementPolicy.BeginReturn(o,42,true)==0 && ProcurementPolicy.BeginReturn(o,43,true)==45,"existing order retains saved delay despite edited configuration");
        ProcurementPolicy.MarkArrived(o,50);
        Check(!ProcurementPolicy.Expired(o,109) && ProcurementPolicy.Expired(o,110),"saved stock lifetime unaffected by configuration edit");
        Check(ProcurementPolicy.TravelDays(100)==3 && ProcurementPolicy.Freight(9,100)==13,"policy consumes editable travel and freight settings");
        ProcurementPolicy.Settings=AgesOfCalradia.CampaignSystems.ProcurementSettings.Defaults;
        o.PolicyRevision="invalid";Reject(()=>s.Validate(),"malformed saved policy revision rejected");
        o.PolicyRevision=null;o.ReturnDelayDays=0.5;Reject(()=>s.Validate(),"out-of-schema saved duration rejected");
        o.ReturnDelayDays=0;s.Validate();
        Check(!ProcurementPolicy.Due(22,double.NegativeInfinity) && !ProcurementPolicy.Due(22,-1)
            && !ProcurementPolicy.Due(double.NaN,22) && !ProcurementPolicy.Due(22,double.NaN),"invalid due times never become eligible");
        // Real accounting boundary in a fake market: the same journal used by native integration.
        int capital=1000, source=200, iron=106, wood=103;
        int cargoIron=0,cargoWood=0;
        var legs=new List<TransferLeg> {
            new TransferLeg {Name="capital",Read=()=>capital,Add=x=>capital+=x,Delta=-107},
            new TransferLeg {Name="source",Read=()=>source,Add=x=>source+=x,Delta=107},
            new TransferLeg {Name="iron",Resource="iron",Read=()=>iron,Add=x=>iron+=x,Delta=-6},
            new TransferLeg {Name="wood",Resource="wood",Read=()=>wood,Add=x=>wood+=x,Delta=-3},
            new TransferLeg {Name="cargoIron",Resource="iron",Read=()=>cargoIron,Add=x=>cargoIron+=x,Delta=6},
            new TransferLeg {Name="cargoWood",Resource="wood",Read=()=>cargoWood,Add=x=>cargoWood+=x,Delta=3} };
        var observations=new List<string>();
        ProcurementTransfer.Execute(legs,(id,outcome)=>observations.Add(id+"/"+outcome));
        Check(observations.Count==2 && observations[0].EndsWith("/begin") && observations[1].EndsWith("/committed")
            && observations[0].Split('/')[0]==observations[1].Split('/')[0],"transaction observer brackets commit with stable identity");
        Check(capital==893 && source==307 && capital+source==1200,"cash transferred not created");
        Check(iron+6==106 && wood+3==103,"source plus cargo conservation");
        for(int fail=0;fail<6;fail++)
        {
            capital=1000; source=200; iron=106; wood=103;
            cargoIron=cargoWood=0;
            var original=legs[fail].Add;
            bool once=true;
            legs[fail].Add=x=>{original(x); if(once){once=false;throw new IOException("callback after mutation");}};
            bool clean=false;
            observations.Clear();
            try{ProcurementTransfer.Execute(legs,(id,outcome)=>observations.Add(outcome));}catch(TransferFailure e){clean=e.Restored;}
            Check(observations.Count==2 && observations[1]=="rolled_back","restored failure is not reported as committed");
            Check(clean && capital==1000 && source==200 && iron==106 && wood==103 && cargoIron==0 && cargoWood==0,"rollback after each mutation point");
            legs[fail].Add=original;
        }
        capital=1;
        Reject(()=>ProcurementTransfer.Execute(legs),"unfunded dispatch rejected before mutation");
        Check(capital==1 && source==200 && iron==106 && wood==103,"rejected dispatch unchanged");
        bool safePreflight=false;
        try{ProcurementTransfer.Execute(legs);}catch(TransferFailure e){safePreflight=e.Restored;}
        Check(safePreflight,"unfunded preflight reports no mutation so ledger can discard pending order");
        var savedRead=legs[0].Read; legs[0].Read=()=>{throw new IOException("native read failed");};
        safePreflight=false;
        try{ProcurementTransfer.Execute(legs);}catch(TransferFailure e){safePreflight=e.Restored;}
        Check(safePreflight && source==200,"native read failure does not quarantine phantom cargo");
        legs[0].Read=savedRead;
        var oversized=Order();oversized.Type=new string('x',ProcurementState.MaximumPayloadCharacters);
        var admission=new ProcurementState();
        Check(!admission.CanAdmit(oversized) && admission.Orders.Count==0,"oversize admission rejected without ledger mutation");
        admission.Orders.Add(oversized);
        Reject(()=>admission.Encode(),"writer enforces same payload bound as reader");
        Reject(()=>ProcurementState.Decode(new string(' ',ProcurementState.MaximumPayloadCharacters+1)),"oversized input rejected before parsing");
        var nearLimit=Order();nearLimit.Type=new string('x',3900000);
        admission=new ProcurementState();Check(admission.CanAdmit(nearLimit),"large valid order with headroom admitted");
        admission.Orders.Add(nearLimit);
        nearLimit.Arrived=true;nearLimit.DeliveredDay=double.MaxValue;nearLimit.ReturnDay=double.MaxValue;nearLimit.BlockedSince=double.MaxValue;
        admission.RecordFault(new string('\u0001',ProcurementState.MaximumFaultCharacters*2));
        string largePayload=admission.Encode();
        Check(admission.Fault.Length==ProcurementState.MaximumFaultCharacters && largePayload.Length<=ProcurementState.MaximumPayloadCharacters,"worst-case escaped fault and lifecycle growth fit reserved space");
        Check(ProcurementState.Decode(largePayload).Orders[0].Lines[0].Remaining==6,"large lifecycle save preserves cargo");
        admission=new ProcurementState();nearLimit=Order();nearLimit.Type=new string('x',3940000);admission.Orders.Add(nearLimit);
        Check(admission.Encode().Length<ProcurementState.MaximumPayloadCharacters && !admission.HasLifecycleCapacity(),"readable legacy payload without growth room identified for hold");
        return "PASS: "+checks+" procurement state, multi-input, money/stock, freight, reserves, lifecycle and rollback assertions.";
    }
}
