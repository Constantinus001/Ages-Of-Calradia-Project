using System;
using System.Collections.Generic;
using System.Linq;

namespace AgesOfCalradia.CampaignSystems
{
    public sealed class AccountChange
    {
        public string Name { get; private set; }
        public string Resource { get; private set; }
        public Func<int> Read { get; private set; }
        public Action<int> Add { get; private set; }
        public int Delta { get; private set; }
        public AccountChange(string name, string resource, Func<int> read, Action<int> add, int delta)
        { Name=name;Resource=resource;Read=read;Add=add;Delta=delta; }
    }
    public sealed class EconomicTransferFailure : Exception
    {
        public bool Restored { get; private set; }
        internal EconomicTransferFailure(bool restored, Exception inner)
            : base("Economic transfer failed; restored=" + restored, inner) { Restored = restored; }
    }
    public static class EconomicTransfer
    {
        [ThreadStatic] private static bool _active;
        // Native mutation boundary. Main thread only; reads must be side-effect free.
        // Caller retains its own domain/save ledger and quarantines unrestored failures.
        public static void Execute(IList<AccountChange> legs)
        {
            if (_active) throw new EconomicTransferFailure(true, new InvalidOperationException("Reentrant economic transfer rejected"));
            _active=true;
            try { ExecutePlan(legs == null ? null : legs.ToArray()); }
            finally { _active=false; }
        }
        private static void ExecutePlan(IList<AccountChange> legs)
        {
            int[] before, after;
            try
            {
                if (legs == null || legs.Count == 0) throw new ArgumentException("Empty transfer");
                if (legs.Any(l => l == null || l.Read == null || l.Add == null || string.IsNullOrWhiteSpace(l.Name) || string.IsNullOrWhiteSpace(l.Resource))
                    || legs.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != legs.Count)
                    throw new ArgumentException("Invalid or duplicate account identity");
                if (legs.Any(l=>l.Name!=l.Name.Trim() || l.Resource!=l.Resource.Trim())
                    || legs.Select(l=>l.Read).Distinct().Count()!=legs.Count)
                    throw new ArgumentException("Ambiguous identity or repeated account accessor");
                if (legs.GroupBy(l => l.Resource, StringComparer.Ordinal).Any(g => g.Sum(l => (long)l.Delta) != 0))
                    throw new InvalidOperationException("Unbalanced resource transfer; every debit requires a matching credit");
                before = legs.Select(l => l.Read()).ToArray();
                after = before.Select((v,i) => checked(v + legs[i].Delta)).ToArray();
                if (before.Any(v => v < 0) || after.Any(v => v < 0)) throw new InvalidOperationException("Unfunded transfer");
            }
            catch (Exception ex) { throw new EconomicTransferFailure(true,ex); }
            try
            {
                for (int i=0;i<legs.Count;i++) legs[i].Add(legs[i].Delta);
                for (int i=0;i<legs.Count;i++)
                    if (legs[i].Read()!=after[i]) throw new InvalidOperationException("Transfer mismatch: " + legs[i].Name);
            }
            catch (Exception ex)
            {
                bool restored=true;
                for (int i=legs.Count-1;i>=0;i--)
                {
                    try { int delta=checked(before[i]-legs[i].Read()); if(delta!=0) legs[i].Add(delta); }
                    catch (Exception rollback) { System.Diagnostics.Trace.WriteLine("Economic rollback: " + rollback); }
                }
                for (int i=0;i<legs.Count;i++)
                {
                    try { if(legs[i].Read()!=before[i]) restored=false; }
                    catch (Exception read) { restored=false;System.Diagnostics.Trace.WriteLine(read); }
                }
                throw new EconomicTransferFailure(restored,ex);
            }
        }
    }
}
