using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Optional public observer ABI. Decisions are not wallet receipts. Existing
    // reward/lifecycle observers retain ownership of payment and cleanup evidence.
    internal static class NavalCashoutObserver
    {
        private static EventInfo _event;
        private static readonly Action<string, object, string> Listener = Observe;
        internal static void Start()
        {
            Stop();
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AgesOfCalradia.CampaignSystems");
                var type = assembly == null ? null : assembly.GetType("AgesOfCalradia.CampaignSystems.NavalCashoutObservation");
                _event = type == null ? null : type.GetEvent("Observed", BindingFlags.Public | BindingFlags.Static);
                if (_event == null)
                {
                    SupplyCapture.Write("NAVAL_POLICY_STATUS", 0, 0, "naval", "unavailable", 0, 0, "policy_v1; legacy_build_not_certified");
                    return;
                }
                _event.AddEventHandler(null, Listener);
                SupplyCapture.Write("NAVAL_POLICY_STATUS", 0, 0, "naval", "available", 0, 0,
                    "policy_v1; " + type.GetProperty("Status").GetValue(null, null));
            }
            catch (Exception ex) { SupplyCapture.Fail("naval policy observer attach", ex); }
        }
        internal static void Stop()
        {
            var prior = _event; _event = null;
            if (prior != null) prior.RemoveEventHandler(null, Listener);
        }
        private static void Observe(string kind, object subject, string detail)
        {
            if (!SupplyCapture.Active) return;
            try
            {
                if (kind == "compatibility")
                {
                    SupplyCapture.Write("NAVAL_POLICY_STATUS", 0, 0, "naval", "rejected", 0, 0, "policy_v1; " + detail);
                    return;
                }
                var ship = subject as Ship;
                var party = subject as MapEventParty;
                if (ship != null) detail += "; ship=" + SupplyRewardObserver.ShipId(ship);
                if (party != null) detail += SupplyBattleAllocationObserver.PartyContext(party);
                detail += SupplyRewardObserver.Context + SupplyBattleAllocationObserver.Context + NavalSaleObserver.Context;
                SupplyCapture.Write("NAVAL_POLICY_DECISION", 0, 0, "naval", kind, 0, 0, "policy_v1; " + detail);
            }
            catch (Exception ex) { SupplyCapture.Fail("naval policy observation", ex); }
        }
    }
}
