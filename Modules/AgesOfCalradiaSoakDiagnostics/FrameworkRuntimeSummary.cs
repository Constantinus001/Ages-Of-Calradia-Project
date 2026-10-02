using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Compact, diagnostics-only closure receipt. It is deliberately a navigation
    // aid, not a reconciliation or balance verdict; offline analyzers remain the
    // authority for wallet, cargo and lifecycle joins.
    internal static class FrameworkRuntimeSummary
    {
        private static string _path, _session;
        private static long _workshopCycles, _workshopSucceeded, _workshopFailed;
        private static long _procurementCommitted, _procurementArrivals, _procurementConsumptions;
        private static long _navalScopes, _playerNavalScopes, _valuedShips;
        internal static string CountersJson()
        {
            return "{\"workshopCycles\":" + _workshopCycles + ",\"workshopFailedAttempts\":" + _workshopFailed
                + ",\"procurementCommitted\":" + _procurementCommitted + ",\"procurementArrivals\":" + _procurementArrivals
                + ",\"procurementConsumptions\":" + _procurementConsumptions + ",\"navalExecutedScopes\":" + _navalScopes
                + ",\"playerNavalScopes\":" + _playerNavalScopes + ",\"valuedShips\":" + _valuedShips + "}";
        }

        internal static void Begin(string directory, string session)
        {
            _path = Path.Combine(directory, "AocFramework-current.summary.json");
            _session = session;
            _workshopCycles = _workshopSucceeded = _workshopFailed = 0;
            _procurementCommitted = _procurementArrivals = _procurementConsumptions = 0;
            _navalScopes = _playerNavalScopes = _valuedShips = 0;
        }

        internal static void Observe(string kind, string metric, string detail)
        {
            if (_path == null) return;
            if (kind == "WORKSHOP_CYCLE")
            {
                _workshopCycles++;
                if (metric == "succeeded") _workshopSucceeded++;
                // SupplyWorkshopObserver emits failed_see_gates when the native
                // cycle returns false. This is a rejected attempt, not a crash.
                if (metric == "failed_see_gates" || metric == "failed") _workshopFailed++;
            }
            else if (kind == "PROCUREMENT_TRANSFER" && metric == "committed") _procurementCommitted++;
            else if (kind == "PROCUREMENT_MOVEMENT" && metric == "arrival") _procurementArrivals++;
            else if (kind == "PROCUREMENT_MOVEMENT" && metric == "consumption") _procurementConsumptions++;
            else if (kind == "REWARD_END" && IsNavalStage(metric)
                && (detail ?? string.Empty).IndexOf("originalRan=True", StringComparison.Ordinal) >= 0)
            {
                _navalScopes++;
                if ((detail ?? string.Empty).IndexOf("playerClan=True", StringComparison.Ordinal) >= 0) _playerNavalScopes++;
            }
            else if (kind == "REWARD_VALUATION") _valuedShips++;
        }

        internal static void Close(string reason, double startDay, double endDay, long bytes, long rows, bool closed)
        {
            string path = _path;
            if (path == null) return;
            try
            {
                string temporary = path + ".new-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, Json(reason, startDay, endDay, bytes, rows, closed), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (Exception ex) { SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "runtime summary: " + ex); }
            finally { _path = null; _session = null; }
        }

        private static bool IsNavalStage(string stage)
        {
            return stage == "DistributePartyShipsAndRecoverGold"
                || stage == "RecoverGoldFromRemainingShipsAfterDistribution";
        }

        private static string Json(string reason, double startDay, double endDay, long bytes, long rows, bool closed)
        {
            string playerStatus = _playerNavalScopes == 0 ? "NOT_EXERCISED"
                : "NATIVE_RESULT_OBSERVED_FORMULA_NOT_INDEPENDENTLY_CERTIFIED";
            string valuationStatus = _valuedShips == 0 ? "NOT_EXERCISED" : "OBSERVED_REQUIRES_OFFLINE_RECONCILIATION";
            return "{\n"
                + "  \"schema\": 1,\n"
                + "  \"status\": \"" + (closed ? "CLOSED_OBSERVED_ONLY" : "INCOMPLETE") + "\",\n"
                + "  \"sessionId\": \"" + Escape(_session) + "\",\n"
                + "  \"stopReason\": \"" + Escape(reason) + "\",\n"
                + "  \"campaignDays\": " + Number(endDay - startDay) + ",\n"
                + "  \"rows\": " + rows + ",\n"
                + "  \"bytes\": " + bytes + ",\n"
                + "  \"workshops\": {\"cycles\": " + _workshopCycles + ", \"succeeded\": " + _workshopSucceeded + ", \"failed\": " + _workshopFailed + "},\n"
                + "  \"procurement\": {\"committed\": " + _procurementCommitted + ", \"arrivals\": " + _procurementArrivals + ", \"consumptions\": " + _procurementConsumptions + "},\n"
                + "  \"naval\": {\"scopesObserved\": " + _navalScopes + ", \"playerScopesObserved\": " + _playerNavalScopes
                + ", \"playerPenaltyStatus\": \"" + playerStatus + "\", \"valuedShips\": " + _valuedShips
                + ", \"shipValuationStatus\": \"" + valuationStatus + "\"},\n"
                + "  \"limit\": \"Observed counters only; use offline reconciliation before balancing.\"\n"
                + "}\n";
        }

        private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Escape(string value) { return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\""); }
    }
}
