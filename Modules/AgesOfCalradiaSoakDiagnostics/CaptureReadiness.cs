using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Atomic operator receipt, never a gameplay setting or save record. RECORDING
    // requires installed hooks and a successful capture flush. The monitor must
    // match PID/start, session and loaded MVIDs and reject stale heartbeats.
    internal static class CaptureReadiness
    {
        private const string PatchOwner = "aoc.soak.supply.observer.v1";
        private static string _directory, _session, _log, _assemblies;
        private static bool _ready;
        private static int _hooks;
        private static long _lastPulse;
        private static readonly Dictionary<MethodBase, string> HookSignatures = new Dictionary<MethodBase, string>();
        internal static void Begin(string directory, string session, string log)
        {
            _directory = directory; _session = session; _log = log; _ready = false; _hooks = 0; _lastPulse = 0;
            HookSignatures.Clear();
            _assemblies = string.Join(",", AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => new[] { "AgesOfCalradia.SoakDiagnostics", "AgesOfCalradia.CampaignSystems", "AgesOfCalradia.WorkshopProcurement", "AgesOfCalradiaLogistics", "AgesOfCalradia.Approved560CalendarFixes", "AgesOfCalradia", "TaleWorlds.CampaignSystem", "NavalDLC" }.Contains(a.GetName().Name))
                .Select(a => "{\"name\":" + Json(a.GetName().Name) + ",\"mvid\":" + Json(a.ManifestModule.ModuleVersionId.ToString())
                    + ",\"path\":" + Json(a.Location) + ",\"diskSha256\":" + Json(Hash(a.Location)) + "}"));
            Publish("PREPARING", "Hooks and writable capture not yet verified", 0, 0, 0, 0, 0, 0);
        }
        internal static void ValidateHooks()
        {
            var naval = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "NavalDLC");
            IEnumerable<MethodBase> expected = SupplyChainObserver.ScopeTargets()
                .Concat(SupplyCashObserver.Targets()).Concat(SupplyCashObserver.BoundaryTargets())
                .Concat(new[] { SupplyCashObserver.WithdrawalTarget() })
                .Concat(MoneyPurposeObserver.Targets())
                .Concat(SupplyWorkshopObserver.Targets()).Concat(SupplyCaravanObserver.Targets())
                .Concat(SupplyMarketObserver.Targets()).Concat(SupplyRewardObserver.Targets(naval))
                .Concat(SupplyBattleAllocationObserver.Targets()).Concat(SupplyBattleAllocationObserver.ModelTargets)
                .Concat(SupplyShipLifecycleObserver.Targets())
                .Concat(QuestLifecycleObserver.Targets()).Concat(new[] { SupplyTownCashObserver.Target() })
                .Concat(new[] { SupplyWoolSaleObserver.SellTarget(), SupplyWoolSaleObserver.LookupTarget() });
            if (SupplyRewardObserver.PenaltyTarget != null) expected = expected.Concat(new[] { SupplyRewardObserver.PenaltyTarget });
            _hooks = 0;
            foreach (var target in expected.Concat(SupplyChainObserver.InstalledTargets).Distinct())
            {
                var patches = Harmony.GetPatchInfo(target);
                if (patches == null || !patches.Owners.Contains(PatchOwner))
                    throw new InvalidOperationException("Required capture hook missing: " + target.DeclaringType.FullName + "." + target);
                _hooks++;
                HookSignatures[target] = Signature(patches);
                SupplyCapture.Write("CAPTURE_HOOK", 0, 0, target.DeclaringType.FullName, target.Name, 0, 1,
                    "mvid=" + target.Module.ModuleVersionId + "; token=" + target.MetadataToken
                    + "; owners=" + string.Join(",", patches.Owners) + "; installation_not_execution_proof");
            }
            if (!SupplyCapture.Active) throw new IOException("Capture failed during hook receipts");
        }
        internal static void MarkReady()
        {
            if (_hooks == 0) throw new InvalidOperationException("Cannot mark capture ready without required hooks");
            _ready = true;
        }
        internal static void Pulse(double day, long rows, long bytes, long pending, long discarded, double writerSeconds)
        {
            if (_directory == null || !_ready) return;
            long now = Stopwatch.GetTimestamp();
            if (_lastPulse != 0 && now - _lastPulse < 5 * Stopwatch.Frequency) return;
            foreach (var hook in HookSignatures)
                if (Signature(Harmony.GetPatchInfo(hook.Key)) != hook.Value)
                    throw new InvalidOperationException("Capture hook changed after startup: " + hook.Key);
            Publish("RECORDING", "Flushed capture; execution coverage requires event receipts", day, rows, bytes, pending, discarded, writerSeconds);
            _lastPulse = now;
        }
        internal static void Close(string status, string reason, double day, long rows, long bytes, long discarded, double writerSeconds)
        {
            if (_directory == null) return;
            try { Publish(status, reason, day, rows, bytes, 0, discarded, writerSeconds); }
            catch (Exception ex) { SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "readiness receipt: " + ex); }
            finally { _directory = null; _ready = false; }
        }
        private static void Publish(string status, string reason, double day, long rows, long bytes, long pending, long discarded, double writerSeconds)
        {
            using (DiagnosticObserverCost.Measure("readiness_publication"))
            using (var process = Process.GetCurrentProcess())
            {
                string text = "{\"schema\":1,\"utc\":" + Json(DateTime.UtcNow.ToString("O"))
                    + ",\"status\":" + Json(status) + ",\"reason\":" + Json(reason)
                    + ",\"pid\":" + process.Id + ",\"processStartUtc\":" + Json(process.StartTime.ToUniversalTime().ToString("O"))
                    + ",\"session\":" + Json(_session) + ",\"log\":" + Json(_log)
                    + ",\"campaign\":" + Json(Campaign.Current == null ? "fixture" : Campaign.Current.UniqueGameId)
                    + ",\"day\":" + SupplyCapture.N(day) + ",\"requiredHooks\":" + _hooks
                    + ",\"committedRows\":" + rows + ",\"bytes\":" + bytes + ",\"pendingRows\":" + pending
                    + ",\"discardedRows\":" + discarded + ",\"writerSecondsLowerBound\":" + SupplyCapture.N(writerSeconds)
                    + ",\"observedCountersNotReconciliation\":" + FrameworkRuntimeSummary.CountersJson()
                    + ",\"captureMode\":\"combined_supply\",\"causalitySchema\":1"
                    + ",\"committedEventCoverage\":" + DiagnosticEventCoverage.Json()
                    + ",\"assemblies\":[" + _assemblies + "]}";
                string path = Path.Combine(_directory, "AocFramework-readiness.json");
                string temporary = path + ".new-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
        }
        private static string Hash(string path)
        {
            using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
        private static string Signature(Patches patches)
        {
            if (patches == null) return string.Empty;
            return string.Join("|", patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                .Where(p => p.owner == PatchOwner).Select(p => p.PatchMethod.Module.ModuleVersionId + ":" + p.PatchMethod.MetadataToken).OrderBy(s => s));
        }
        internal static string Json(string value)
        {
            var result = new StringBuilder("\"");
            foreach (char ch in value ?? string.Empty)
            {
                if (ch == '\\' || ch == '"') result.Append('\\').Append(ch);
                else if (ch < 32) result.Append("\\u").Append(((int)ch).ToString("x4"));
                else result.Append(ch);
            }
            return result.Append('"').ToString();
        }
    }
}
