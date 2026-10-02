using System;
using System.Collections.Generic;
using System.Linq;
using AgesOfCalradia.CampaignSystems;
namespace AgesOfCalradia.WorkshopProcurement
{
    internal sealed class TransferLeg
    {
        internal string Name;
        internal string Resource = "gold";
        internal Func<int> Read;
        internal Action<int> Add;
        internal int Delta;
    }
    internal sealed class TransferFailure : Exception
    {
        internal readonly bool Restored;
        internal TransferFailure(bool restored, Exception inner)
            : base("Procurement native transfer failed; restored=" + restored, inner) { Restored = restored; }
    }
    internal static class ProcurementTransfer
    {
        internal static void Execute(IList<TransferLeg> legs,Action<string,string> observe=null)
        {
            string transaction=Guid.NewGuid().ToString("N"),outcome="failed";
            Observe(observe,transaction,"begin");
            try {
                EconomicTransfer.Execute(legs.Select(l => new AccountChange(l.Name,l.Resource,l.Read,l.Add,l.Delta)).ToArray());
                outcome="committed";
            }
            catch(EconomicTransferFailure ex) { outcome=ex.Restored?"rolled_back":"failed";throw new TransferFailure(ex.Restored,ex); }
            finally{Observe(observe,transaction,outcome);}
        }
        private static void Observe(Action<string,string> observer,string transaction,string outcome)
        {
            if(observer==null)return;
            try{observer(transaction,outcome);}
            catch(Exception ex){System.Diagnostics.Trace.WriteLine("Procurement transfer observer failed: "+ex);}
        }
    }
}
