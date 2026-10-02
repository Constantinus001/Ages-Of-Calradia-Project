using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Optional ABI: no compile-time dependency and no economy mutations. Missing
    // module is zero private inventory; present but incompatible is a capture
    // failure via the observer's existing boundary, never fabricated zero stock.
    internal static class ProcurementObservation
    {
        private static Type _type;
        internal static bool OpeningLedgerObserved { get; private set; }
        private static EventInfo _ledgerEvent;
        private static EventInfo _movementEvent;
        private static EventInfo _accountingEvent;
        private static EventInfo _transferEvent;
        private static EventInfo _planEvent;
        private static EventInfo _candidateEvent;
        private static EventInfo _holdEvent;
        private static readonly Action<string,string,string> HoldListener = WriteHold;
        private static void WriteHold(string shop, string reason, string detail)
        {
            if (SupplyCapture.Active) SupplyCapture.Write("PROCUREMENT_HOLD", 0, 0, shop, reason, 0, 0, detail);
        }
        private static readonly Action<string,string,string> CandidateListener = WriteCandidate;
        private static void WriteCandidate(string shop, string reason, string detail)
        {
            if (SupplyCapture.Active) SupplyCapture.Write("PROCUREMENT_CANDIDATE", 0, 0, shop, reason, 0, 0, detail);
        }
        private static readonly Action<string,string,string> PlanListener = WritePlan;
        private static void WritePlan(string shop, string reason, string detail)
        {
            if (SupplyCapture.Active)
                SupplyCapture.Write("PROCUREMENT_PLAN", 0, 0, shop, reason, 0, 0,
                    detail + "; procurementMvid=" + _type.Module.ModuleVersionId);
        }
        [ThreadStatic] private static string _transfer;
        internal static string CashContext { get { return "; procurementTransfer="+(_transfer??"none"); } }
        private static readonly Action<string,string,string,string,string> TransferListener=WriteTransfer;
        private static void WriteTransfer(string transaction,string order,string stage,string shop,string outcome)
        {
            if(!SupplyCapture.Active)return;
            if(outcome=="begin")
            {
                if(_transfer!=null){SupplyCapture.Fail("nested procurement transfer",new InvalidOperationException("Unexpected nested transaction"));return;}
                _transfer=transaction;
            }
            else if(_transfer!=transaction)
            {
                SupplyCapture.Fail("procurement transfer identity",new InvalidOperationException("Unmatched transaction completion"));return;
            }
            SupplyCapture.Write("PROCUREMENT_TRANSFER",0,0,shop,outcome,0,0,
                "transaction="+transaction+"; order="+order+"; stage="+stage);
            if(outcome!="begin")_transfer=null;
        }
        private static readonly Action<string,string,string,string,int,int,int,int,int> AccountingListener=WriteAccounting;
        private static readonly Action<string,string,string,string,string,string,int,int,int,int> MovementListener=WriteMovement;
        private static readonly Action<string, string> LedgerListener = WriteLedger;
        internal static void BeginLedgerObservation()
        {
            OpeningLedgerObserved = false;
            if (!SupplyCapture.Active) return;
            try
            {
                var method = Method("LoadedLedger");
                SupplyCapture.Write("FEATURE_COVERAGE",0,0,"procurement",method==null?"absent":"present",0,0,"optional_observer_abi; absence_not_failure");
                if (method == null)
                {
                    SupplyCapture.Write("PROCUREMENT_LEDGER", 0, 0, "campaign", "missing_module", 0, 0, "not_exercised");
                    return;
                }
                EndLedgerObservation();
                _ledgerEvent = _type.GetEvent("LedgerObserved", BindingFlags.Public | BindingFlags.Static);
                if (_ledgerEvent == null) throw new MissingMemberException("Procurement ledger event unavailable");
                _ledgerEvent.AddEventHandler(null, LedgerListener);
                _movementEvent=_type.GetEvent("MovementObserved",BindingFlags.Public|BindingFlags.Static);
                if(_movementEvent==null)throw new MissingMemberException("Procurement movement event unavailable");
                _movementEvent.AddEventHandler(null,MovementListener);
                _accountingEvent=_type.GetEvent("AccountingObserved",BindingFlags.Public|BindingFlags.Static);
                if(_accountingEvent==null)throw new MissingMemberException("Procurement accounting event unavailable");
                _accountingEvent.AddEventHandler(null,AccountingListener);
                _transferEvent=_type.GetEvent("TransferObserved",BindingFlags.Public|BindingFlags.Static);
                if(_transferEvent==null)throw new MissingMemberException("Procurement transfer event unavailable");
                _transferEvent.AddEventHandler(null,TransferListener);
                _planEvent = _type.GetEvent("PlanObserved", BindingFlags.Public | BindingFlags.Static);
                if (_planEvent != null) _planEvent.AddEventHandler(null, PlanListener);
                _candidateEvent = _type.GetEvent("CandidateObserved", BindingFlags.Public | BindingFlags.Static);
                if (_candidateEvent != null) _candidateEvent.AddEventHandler(null, CandidateListener);
                _holdEvent = _type.GetEvent("HoldObserved", BindingFlags.Public | BindingFlags.Static);
                if (_holdEvent != null) _holdEvent.AddEventHandler(null, HoldListener);
                SupplyCapture.Write("FEATURE_COVERAGE", 0, 0, "procurement_holds", _holdEvent == null ? "absent" : "present",
                    0, 0, "execution_requires_PROCUREMENT_HOLD; old_module_is_coverage_gap");
                SupplyCapture.Write("FEATURE_COVERAGE", 0, 0, "procurement_candidates", _candidateEvent == null ? "absent" : "present",
                    0, 0, "optional_additive_abi; execution_requires_PROCUREMENT_CANDIDATE");
                SupplyCapture.Write("FEATURE_COVERAGE", 0, 0, "procurement_plans", _planEvent == null ? "absent" : "present",
                    0, 0, "optional_additive_abi; absent_is_missing_decision_coverage");
                WriteLedger("loaded_payload", (string)method.Invoke(null, null));
                Snapshot("current_opening");
            }
            catch (Exception ex) { SupplyCapture.Fail("procurement ledger subscription", ex); }
        }
        internal static void EndLedgerObservation()
        {
            _transfer=null;
            var holdEvent = _holdEvent; _holdEvent = null;
            if (holdEvent != null) holdEvent.RemoveEventHandler(null, HoldListener);
            var candidateEvent = _candidateEvent; _candidateEvent = null;
            if (candidateEvent != null) candidateEvent.RemoveEventHandler(null, CandidateListener);
            var planEvent = _planEvent; _planEvent = null;
            if (planEvent != null) planEvent.RemoveEventHandler(null, PlanListener);
            var transferEvent=_transferEvent;_transferEvent=null;
            if(transferEvent!=null)transferEvent.RemoveEventHandler(null,TransferListener);
            var observed = _ledgerEvent;
            _ledgerEvent = null;
            if (observed != null) observed.RemoveEventHandler(null, LedgerListener);
            observed=_movementEvent;_movementEvent=null;
            if(observed!=null)observed.RemoveEventHandler(null,MovementListener);
            observed=_accountingEvent;_accountingEvent=null;
            if(observed!=null)observed.RemoveEventHandler(null,AccountingListener);
        }
        private static void WriteAccounting(string receipt,string order,string stage,string shop,int before,int after,int cash,int goods,int freight)
        {
            if(!SupplyCapture.Active)return;
            SupplyCapture.Write("PROCUREMENT_ACCOUNTING",0,0,shop,stage,before,after,
                "receipt="+receipt+"; order="+order+"; cashDelta="+cash+"; originalGoods="+goods+"; originalFreight="+freight
                +"; committed=true; basis_not_cash; procurementMvid="+_type.Module.ModuleVersionId);
        }
        internal static void Snapshot(string stage)
        {
            if (!SupplyCapture.Active) return;
            try
            {
                var method = Method("CurrentLedger");
                if (method == null) return; // Missing module was reported at start.
                WriteLedger(stage, (string)method.Invoke(null, null));
            }
            catch (Exception ex) { SupplyCapture.Fail("current procurement ledger", ex); }
        }
        private static void WriteMovement(string receipt,string order,string stage,string town,string item,string category,
            int marketBefore,int marketAfter,int cargoBefore,int cargoAfter)
        {
            if(!SupplyCapture.Active)return;
            try{
                if(Campaign.Current==null)throw new InvalidOperationException("Missing campaign for procurement movement");
                SupplyCapture.Write("PROCUREMENT_MOVEMENT",0,0,Campaign.Current.UniqueGameId,stage,marketBefore,marketAfter,
                    "receipt="+receipt+"; order="+order+"; settlement="+town+"; item="+item+"; category="+category
                    +"; cargoBefore="+cargoBefore+"; cargoAfter="+cargoAfter+"; committed=true; procurementMvid="+_type.Module.ModuleVersionId);
            }catch(Exception ex){SupplyCapture.Fail("procurement movement observation",ex);}
        }
        private static void WriteLedger(string stage, string payload)
        {
            if (!SupplyCapture.Active) return;
            try
            {
                var campaign = Campaign.Current;
                if (campaign == null) throw new InvalidOperationException("Missing procurement campaign identity");
                double day = CampaignTime.Now.ToDays;
                if (payload == null)
                {
                    SupplyCapture.Write("PROCUREMENT_LEDGER", 0, 0, campaign.UniqueGameId, "unavailable", day, day, "no_valid_ledger");
                    return;
                }
                string hash;
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "");
                SupplyCapture.Write("PROCUREMENT_LEDGER", 0, 0, campaign.UniqueGameId, stage, day, day,
                    "procurementMvid=" + _type.Module.ModuleVersionId + "; sha256=" + hash
                    + "; payloadBase64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
                if (stage == "current_opening" && SupplyCapture.Active) OpeningLedgerObserved = true;
            }
            catch (Exception ex) { SupplyCapture.Fail("procurement ledger observation", ex); }
        }
        private static MethodInfo Method(string name, params Type[] args)
        {
            if (_type == null)
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "AgesOfCalradia.WorkshopProcurement");
                if (assembly == null) return null;
                _type = assembly.GetType("AgesOfCalradia.WorkshopProcurement.ProcurementDiagnostics", true);
            }
            return _type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, args, null)
                ?? throw new MissingMethodException("Procurement observation ABI changed: " + name);
        }
        internal static int Stock(Workshop shop, string category)
        {
            var method = Method("PrivateStock", typeof(Workshop), typeof(string));
            int value = method == null ? 0 : (int)method.Invoke(null, new object[] { shop, category });
            if (value < 0) throw new InvalidOperationException("Private procurement stock is quarantined/unknown");
            return value;
        }
        internal static string Gate(Workshop shop)
        {
            var method = Method("PrepaidInputCost", typeof(Workshop));
            int value = method == null ? -1 : (int)method.Invoke(null, new object[] { shop });
            return value < 0 ? "" : "; prepaidInputCost=" + value;
        }
        internal static string InputEvidence(Workshop shop, string category)
        {
            if (Method("PrivateStock", typeof(Workshop), typeof(string)) == null) return "; privateState=module_absent";
            var method = _type.GetMethod("InputEvidence", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(Workshop), typeof(string) }, null);
            return method == null ? "; privateState=unsupported" : "; " + (string)method.Invoke(null, new object[] { shop, category });
        }
        internal static string ApprovalHurdle(Workshop shop, int input)
        {
            if (Method("PrepaidInputCost", typeof(Workshop)) == null) return "";
            var method = _type.GetMethod("ObservedApprovalMargin", BindingFlags.Public | BindingFlags.Static);
            if (method == null) return ""; // Legacy optional ABI: only native reference exists.
            double margin = shop.WorkshopType.IsHidden ? 0 : (double)method.Invoke(null, new object[] { shop });
            return "; effectiveProfitHurdle=" + SupplyCapture.N(input + margin)
                + "; marginEvidence=current_native_expression; unknown_is_not_native_fallback";
        }
    }
}
