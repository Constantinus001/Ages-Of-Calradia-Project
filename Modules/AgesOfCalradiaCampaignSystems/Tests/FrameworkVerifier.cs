using System;
using System.Collections.Generic;
using System.IO;
using AgesOfCalradia.CampaignSystems;
public static class FrameworkVerifier
{
    static int checks;
    static void Check(bool value,string label) { checks++;if(!value)throw new Exception(label); }
    static void Reject(Action action,string label) { bool failed=false;try{action();}catch(Exception){failed=true;}Check(failed,label); }
    public static class Provider { public static int ApiVersion {get{return 1;}} public static int Value=50;public static int ReadReserve(string id){return Value;} }
    public static class WrongVersion { public static int ApiVersion {get{return 2;}} public static int ReadReserve(string id){return 50;} }
    public static class Broken { public static int ApiVersion {get{return 1;}} public static int ReadReserve(string id){throw new IOException("provider failure");} }
    public static class PrivateVersion { public static int ApiVersion {private get{return 1;}set{}} public static int ReadReserve(string id){return 50;} }
    public static string Run()
    {
        checks=0;
        var defaults=ProcurementSettings.Defaults;
        Check(defaults.BatchesPerOrder==3 && defaults.CapitalReserveDays==7 && defaults.BlockedReturnDays==14 && defaults.StockLifetimeDays==30,"default policy parity");
        Check(typeof(ProcurementSettings).GetConstructors().Length==0,"no validation bypass constructor");
        Check(defaults.IronSupplierReserveDays==0,"legacy settings retain batch reserves");
        Check(defaults.MaximumAdaptiveBatches==0 && defaults.DeliveryDelayCostWeight==0,"legacy lot and supplier ranking unchanged");
        Check(defaults.WineExpenseCoverage==0,"legacy native wine hurdle unchanged");
        foreach(string value in new[]{"-1","0.5","3.1","NaN","Infinity"})
            Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement WineExpenseCoverage='"+value+"'/></CampaignSystems>"),"invalid wine coverage");
        Check(ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement WineExpenseCoverage='1.25'/></CampaignSystems>").WineExpenseCoverage==1.25,"wine coverage configurable");
        foreach(string attribute in new[]{"MaximumAdaptiveBatches='2'","MaximumAdaptiveBatches='101'","MaximumAdaptiveBatches='1.5'","DeliveryDelayCostWeight='NaN'","DeliveryDelayCostWeight='-1'","DeliveryDelayCostWeight='11'"})
            Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement "+attribute+" /></CampaignSystems>"),"invalid replenishment policy");
        Check(ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='14'/></CampaignSystems>").IronSupplierReserveDays==14,"iron reserve configurable");
        Check(defaults.Revision!=ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='14'/></CampaignSystems>").Revision,"iron reserve policy fingerprint");
        foreach(string value in new[]{"-1","366","1.5","NaN"})
            Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement IronSupplierReserveDays='"+value+"'/></CampaignSystems>"),"invalid iron reserve");
        Check(defaults.GrapeCategoryFloor==10 && defaults.ReorderBufferDays==1,"bounded procurement defaults");
        Check(defaults.Revision!=ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement GrapeCategoryFloor='20'/></CampaignSystems>").Revision,"category reserve has policy provenance");
        foreach(string attribute in new[]{"BatchesPerOrder='0'","BatchesPerOrder='2.5'","MaximumDistance='NaN'","HandlingDays='Infinity'",
            "Unknown='1'","MaximumDistance='10000' FreightDistancePerDay='0.01'",
            "GrapeCategoryFloor='0'","GrapeCategoryFloor='2.5'","ReorderBufferDays='NaN'","ReorderBufferDays='31'"})
            Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement "+attribute+" /></CampaignSystems>"),attribute);
        Reject(()=>ProcurementSettings.Parse("<!DOCTYPE a [<!ENTITY x 'bad'>]><CampaignSystems schema='1'><Procurement /></CampaignSystems>"),"DTD disabled");
        Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='2'><Procurement /></CampaignSystems>"),"future schema rejected");
        Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement /><Procurement /></CampaignSystems>"),"duplicate section rejected");
        Reject(()=>ProcurementSettings.Load(""),"invalid path is not defaults");
        Reject(()=>ProcurementSettings.Parse("<CampaignSystems schema='1'>BatchesPerOrder=5<Procurement /></CampaignSystems>"),"misplaced settings text cannot be silently ignored");
        Check(defaults.Revision==ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement BatchesPerOrder='3'/></CampaignSystems>").Revision,"equivalent overrides have identical revision");
        var edited=ProcurementSettings.Parse("<CampaignSystems schema='1'><Procurement BatchesPerOrder='5' StockLifetimeDays='60' /></CampaignSystems>");
        Check(edited.BatchesPerOrder==5 && edited.StockLifetimeDays==60 && edited.CapitalReserveDays==7,"overrides retain unspecified defaults");
        double now=100;var system=new CampaignSystem(()=>now,defaults);
        Check(system.Time.Now==100 && system.Quests.RemainingDays(110)==10,"one authoritative clock");
        now=105;Check(system.Quests.RemainingDays(110)==5,"quest remaining follows campaign time");
        Check(system.Quests.RemainingDays(100)==0 && system.Quests.RemainingDays(null)==null,"expired and never deadlines");
        now=90;Check(system.Quests.RemainingDays(110)==20,"reload to earlier save no stale accumulator");
        now=double.NaN;Reject(()=>{var ignored=system.Time.Now;},"nonfinite clock rejected");
        int a=100,b=20;
        Func<AccountChange[]> plan=()=>new[]{new AccountChange("a","gold",()=>a,x=>a+=x,-10),new AccountChange("b","gold",()=>b,x=>b+=x,10)};
        EconomicTransfer.Execute(plan());Check(a==90 && b==30,"cash conserved");
        Reject(()=>EconomicTransfer.Execute(new[]{new AccountChange("mint","gold",()=>a,x=>a+=x,10)}),"unmatched credit rejected");
        Check(a==90,"unmatched credit no mutation");
        Func<int> sameAccount=()=>a;
        Reject(()=>EconomicTransfer.Execute(new[]{new AccountChange("a","gold",sameAccount,x=>a+=x,-10),new AccountChange("alias","gold",sameAccount,x=>a+=x,10)}),"same accessor cannot hide behind different identities");
        Check(a==90,"alias rejected before any debit");
        Reject(()=>EconomicTransfer.Execute(new[]{new AccountChange("a","gold",()=>a,x=>a+=x,-10),new AccountChange("a","gold",()=>b,x=>b+=x,10)}),"duplicate accounts rejected");
        Reject(()=>EconomicTransfer.Execute(new[]{new AccountChange("a","gold",()=>a,x=>a+=x,-10),new AccountChange("b","iron",()=>b,x=>b+=x,10)}),"different resources cannot offset");
        bool once=true,restored=false;
        try {EconomicTransfer.Execute(new[]{new AccountChange("a","gold",()=>a,x=>a+=x,-10),new AccountChange("b","gold",()=>b,x=>{b+=x;if(once){once=false;throw new IOException("after mutation");}},10)});}
        catch(EconomicTransferFailure ex){restored=ex.Restored;}
        Check(restored && a==90 && b==30,"compensation restores exact wallets");
        restored=true;
        try {EconomicTransfer.Execute(new[]{new AccountChange("a","gold",()=>a,x=>{if(x>0)throw new IOException("rollback blocked");a+=x;},-10),new AccountChange("b","gold",()=>b,x=>{throw new IOException("blocked");},10)});}
        catch(EconomicTransferFailure ex){restored=ex.Restored;}
        Check(!restored,"failed rollback is not certified restored");
        a=100;b=20;
        Reject(()=>EconomicTransfer.Execute(new[]{new AccountChange("a","gold",()=>a,x=>{a+=x;EconomicTransfer.Execute(plan());},-10),new AccountChange("b","gold",()=>b,x=>b+=x,10)}),"reentrant transfer rejected");
        Check(a==100 && b==20,"reentrant failure restored");
        EconomicTransfer.Execute(plan());Check(a==90 && b==30,"transaction guard resets after failure");
        Check(new LogisticsConnection(()=>null).Read("party").Status=="not-installed-or-no-api","optional provider absent");
        Check(new LogisticsConnection(()=>typeof(WrongVersion)).Read("party").Status=="unsupported-api","unsupported provider rejected");
        Check(new LogisticsConnection(()=>typeof(PrivateVersion)).Read("party").Status=="unsupported-api","nonpublic version getter cannot qualify as public ABI");
        Check(new LogisticsConnection(()=>typeof(Broken)).Read("party").Status=="provider-error","throwing provider isolated");
        var connection=new LogisticsConnection(()=>typeof(Provider));
        Check(connection.Read("party").Reserve==50 && !connection.Read("party").SupportsWorkshopTransport,"reserve capability not mistaken for transport");
        Provider.Value=-1;Check(!connection.Read("party").Reserve.HasValue,"unknown reserve not zero");
        Provider.Value=101;Check(connection.Read("party").Status=="provider-error","invalid reserve rejected");
        Provider.Value=0;Check(connection.Read("party").Reserve==0,"known zero reserve distinguished");
        return "PASS: "+checks+" Core framework configuration, time, quests, accounting and optional logistics assertions.";
    }
}
