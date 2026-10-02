using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Separate opt-in evidence stream: never starts the long-soak controller or
    // legacy money ledger. v7 canonicalizes wallet evidence. No save state.
    // All game reads occur on the campaign thread.
    internal static class SupplyCapture
    {
        internal const string MarkerName = "AocSupplyCapture.enabled";
        internal const long ByteLimit = 5368709120L;
        internal const int TargetDays = 30;
        private static string _rollingDirectory;
        private static RollingLogStore _rollingStore;
        internal static string RollingMarkerPath { get { return Path.Combine(SoakLog.DirectoryPath,"AocFrameworkDiagnostics.enabled"); } }
        private static StreamWriter _writer;
        private static IncidentRecorder _incidents;
        private static string _session;
        private static long _sequence, _bytes, _startTicks, _flushTicks;
        private static double _startDay, _lastDay;
        private static double _sessionStartDay;
        private static int _acceptanceDays;
        private static double _sampleDay;
        private static long _sampleTicks;
        private static int _thread;
        private static Func<double> _clock;
        private static readonly List<string> _pending = new List<string>();
        private static int _depth;
        private static long _pendingBytes;
        private static long _writeTicks, _flushCostTicks, _writeCalls, _discardedRows, _peakPendingBytes;
        private static int _writeDepth;
        private static double WriterSeconds { get { return (_writeTicks + _flushCostTicks) / (double)Stopwatch.Frequency; } }
        internal static bool Active { get { return _writer != null; } }
        internal static string MarkerPath { get { return Path.Combine(SoakLog.DirectoryPath, MarkerName); } }
        internal static string CloseRequestPath { get { return Path.Combine(SoakLog.DirectoryPath,"AocFramework.close.request"); } }

        // Called only at an application update boundary, before campaign teardown.
        // The request affects diagnostics only. No game pause, save or shutdown.
        internal static bool TryRequestedClose(Action snapshots)
        {
            if(!Active||!File.Exists(CloseRequestPath))return false;
            if(Thread.CurrentThread.ManagedThreadId!=_thread)throw new InvalidOperationException("Unexpected close-request thread");
            if(_depth!=0||ProcurementObservation.CashContext!="; procurementTransfer=none"||SupplyRewardObserver.Context!="; reward=none"
                ||SupplyBattleAllocationObserver.HasOpenScope||NavalSaleObserver.HasOpenScope
                ||QuestLifecycleObserver.HasOpenScope||LogisticsLifecycleObserver.HasOpenScope||CashPurposeContext.HasOpenScope)return false;
            snapshots();
            if(!Active)return true; // Snapshot failure already preserved incomplete evidence.
            Stop("requested_close_snapshots_taken_coverage_not_certified");
            File.Delete(CloseRequestPath); // Acknowledge only this owned protocol request.
            return true;
        }

        internal static string LimitReason(double seconds, double days, long bytes)
        {
            if (!EconomyLedger.Finite(seconds) || !EconomyLedger.Finite(days) || seconds < 0 || days < 0) return "invalid_clock";
            if (bytes >= ByteLimit) return "byte_limit_incomplete";
            if (days >= TargetDays) return "day_target_reached_coverage_not_certified";
            if (seconds >= 3600d) return "wall_limit_incomplete";
            return null;
        }

        internal static void Start()
        {
            Stop("session_replaced_incomplete");
            try
            {
                if(File.Exists(MarkerPath))
                {
                    if(File.Exists(RollingMarkerPath))throw new InvalidOperationException("Two diagnostic activation markers; resolve before capture");
                    File.Move(MarkerPath,RollingMarkerPath);
                }
                string path = BeginRollingSession(SoakLog.DirectoryPath, () => CampaignTime.Now.ToDays);
                SupplyCategories.Initialize();
                SupplyChainObserver.Install();
                Write("SESSION_START", 0, 0, Campaign.Current.UniqueGameId, "supply_v7", 0, 0,
                    "targetDays=" + TargetDays + "; rolling=true; wallLimitSeconds=none; byteLimit=" + ByteLimit
                    + "; no_speed_save_or_quit; assemblyMvid=" + typeof(Campaign).Module.ModuleVersionId
                    + "; diagnosticsMvid=" + typeof(SupplyCapture).Module.ModuleVersionId + "; acceptanceDays=" + _acceptanceDays);
                CaptureReadiness.ValidateHooks();
                SupplyMarketObserver.Provenance();
                SupplyChainObserver.Snapshot();
                CampaignSystemsObservation.Snapshot();
                ProcurementObservation.BeginLedgerObservation();
                NavalCashoutObserver.Start();
                if (!Active) return;
                if (_acceptanceDays > 0 && (CampaignSystemsObservation.LastStatus != "valid" || !ProcurementObservation.OpeningLedgerObserved))
                    throw new InvalidOperationException("Acceptance requires valid framework configuration and an observed opening procurement ledger");
                Flush();
                CaptureReadiness.MarkReady();
                PulseReadiness();
                SoakLog.Write("SUPPLY_CAPTURE_STARTED", path + "; no automatic saves, speed changes or quit");
            }
            catch (Exception ex) { Fail("start", ex); }
        }

        // IO/clock boundary shared by native lifecycle and deterministic tests.
        internal static string BeginSession(string directory, Func<double> clock)
        {
            return OpenSession(directory, clock, false);
        }

        internal static string BeginRollingSession(string directory, Func<double> clock)
        {
            return OpenSession(directory, clock, true);
        }

        private static string OpenSession(string directory, Func<double> clock, bool rolling)
        {
            Stop("session_replaced_incomplete");
            _rollingDirectory = rolling ? directory : null;
            _rollingStore=null;
            _thread = Thread.CurrentThread.ManagedThreadId;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _startDay = _lastDay = clock();
            _sessionStartDay = _startDay;
            _acceptanceDays = 0;
            _sampleDay = _startDay;
            if (!EconomyLedger.Finite(_startDay)) throw new ArgumentException("Invalid initial campaign time");
            _session = Guid.NewGuid().ToString("N");
            _incidents = new IncidentRecorder(directory, _session);
            _sequence = _bytes = 0;
            DiagnosticEventCoverage.Reset();
            DiagnosticObserverCost.Reset();
            _writeTicks = _flushCostTicks = _writeCalls = _discardedRows = _peakPendingBytes = 0;
            _writeDepth = 0;
            _pending.Clear();
            _depth = 0;
            _pendingBytes = 0;
            _startTicks = _flushTicks = Stopwatch.GetTimestamp();
            _sampleTicks = _startTicks;
            string path = Path.Combine(directory, rolling ? "AocFramework-current.tsv" : "AocSupply-" + _session + ".tsv");
            if(rolling)
            {
                CaptureReadiness.Begin(directory, _session, path);
                _acceptanceDays = ReadAcceptanceDays(directory);
                if (_acceptanceDays > 0 && (File.Exists(path) || File.Exists(path + ".state")))
                    throw new InvalidDataException("Acceptance requires an explicitly archived fresh session; existing evidence preserved");
                _rollingStore=new RollingLogStore(directory,Campaign.Current==null?"fixture":Campaign.Current.UniqueGameId,_startDay);
                _startDay=_rollingStore.Anchor;_bytes=_rollingStore.Length;
            }
            _writer = new StreamWriter(rolling?_rollingStore.Open():new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            if(!rolling||!_rollingStore.Append)
                _writer.WriteLine("utc\tsession\tsequence\tday\tkind\tscope\tparent\towner\tmetric\tbefore\tafter\tdetail");
            _writer.Flush();
            if(rolling)_rollingStore.Checkpoint(_lastDay);
            if(rolling) FrameworkRuntimeSummary.Begin(directory, _session);
            return path;
        }

        internal static void SampleDay()
        {
            if (!Active) return;
            try
            {
                long now = Stopwatch.GetTimestamp();
                double day = _clock();
                double elapsedDays = day - _sampleDay;
                double seconds = (now - _sampleTicks) / (double)Stopwatch.Frequency;
                Write("DAY_TIMING", 0, 0, "campaign", "sample_interval", _sampleDay, day,
                    "wallSeconds=" + N(seconds) + "; secondsPerCampaignDay=" + (elapsedDays > 0 ? N(seconds / elapsedDays) : "unavailable")
                    + "; bytes=" + _bytes + "; rows=" + _sequence + "; managedBytes=" + GC.GetTotalMemory(false)
                    + "; includes_pauses_and_diagnostics_overhead; not_configured_speed");
                _sampleDay = day;
                _sampleTicks = now;
                WriteCost();
                CampaignSystemsObservation.Snapshot();
                ProcurementObservation.Snapshot("current_daily");
            }
            catch (Exception ex) { Fail("day timing", ex); }
        }

        internal static void Tick()
        {
            if (!Active) return;
            if (QuestLifecycleObserver.HasOpenScope || LogisticsLifecycleObserver.HasOpenScope || CashPurposeContext.HasOpenScope) return;
            try
            {
                double day = _clock();
                if (day < _lastDay) throw new InvalidOperationException("Campaign clock moved backwards");
                _lastDay = day;
                if(Campaign.Current!=null&&TryRequestedClose(()=>{
                    SupplyChainObserver.Snapshot();
                    CampaignSystemsObservation.Snapshot();
                    ProcurementObservation.Snapshot("current_terminal");
                }))return;
                long now = Stopwatch.GetTimestamp();
                string reason = LimitReason((now - _startTicks) / (double)Stopwatch.Frequency, day - _startDay, _bytes);
                if (_acceptanceDays > 0 && day - _sessionStartDay >= _acceptanceDays && (reason == null || reason == "wall_limit_incomplete"))
                    reason = "acceptance_window_complete_coverage_not_certified";
                // Continuous framework logging is bounded by campaign days and
                // bytes, not time spent paused/alt-tabbed. One-shot tests retain
                // their wall limit; never rotate merely because the game paused.
                if (_rollingDirectory != null && reason == "wall_limit_incomplete") reason = null;
                if (reason != null)
                {
                    if (_rollingDirectory != null && reason == "day_target_reached_coverage_not_certified")
                    {
                        string directory = _rollingDirectory;
                        var clock = _clock;
                        Stop("rolling_30_day_replacement");
                        BeginRollingSession(directory, clock);
                        SupplyCashObserver.ResetPeriod();
                        Write("SESSION_START", 0, 0, Campaign.Current == null ? "fixture" : Campaign.Current.UniqueGameId,
                            "supply_v7", 0, 0, "targetDays=30; rolling=true; previous_cycle_overwritten; diagnosticsMvid=" + typeof(SupplyCapture).Module.ModuleVersionId);
                        if (Campaign.Current != null)
                        {
                            CaptureReadiness.ValidateHooks();
                            SupplyMarketObserver.Provenance();
                            SupplyChainObserver.Snapshot();
                            CampaignSystemsObservation.Snapshot();
                            ProcurementObservation.BeginLedgerObservation();
                            NavalCashoutObserver.Start();
                        }
                        if (Active)
                        {
                            Flush();
                            if (Campaign.Current != null) { CaptureReadiness.MarkReady(); PulseReadiness(); }
                        }
                        return;
                    }
                    // Campaign tick is outside the synchronous native operation
                    // scopes. Terminal stock aligns with this final flow window.
                    if (Campaign.Current != null)
                    {
                        SupplyChainObserver.Snapshot();
                        CampaignSystemsObservation.Snapshot();
                        ProcurementObservation.Snapshot("current_terminal");
                    }
                    Stop(reason);
                    return;
                }
                if (now - _flushTicks >= Stopwatch.Frequency)
                {
                    Flush(); _flushTicks = now;
                    if(_rollingStore!=null)_rollingStore.Checkpoint(_lastDay);
                    PulseReadiness();
                }
            }
            catch (Exception ex) { Fail("tick", ex); }
        }

        internal static void Write(string kind, long scope, long parent, string owner, string metric, double before, double after, string detail)
        {
            if (!Active) return;
            long started = Stopwatch.GetTimestamp();
            bool root = _writeDepth++ == 0;
            _writeCalls++;
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != _thread) throw new InvalidOperationException("Unexpected observer thread");
                if (!EconomyLedger.Finite(before) || !EconomyLedger.Finite(after)) throw new InvalidOperationException("Nonfinite supply value");
                double day=_clock();
                if(!EconomyLedger.Finite(day)||day<_lastDay)throw new InvalidOperationException("Invalid or backward observation clock");
                // Persist the latest observed time, including mutations between
                // campaign ticks. Reload must not append behind an emitted row.
                _lastDay=day;
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\t" + _session + "\t" + (++_sequence)
                    + "\t" + N(day) + "\t" + Clean(kind) + "\t" + scope + "\t" + parent + "\t" + Clean(owner)
                    + "\t" + Clean(metric) + "\t" + N(before) + "\t" + N(after) + "\t" + Clean(detail);
                long size = Encoding.UTF8.GetByteCount(line) + 2;
                if (kind == "BEGIN") _depth++;
                if (_depth > 0)
                {
                    // Commit a complete native root scope or none of it. A byte
                    // limit must not leave half a transaction in the evidence.
                    _pending.Add(line);
                    _pendingBytes += size;
                    _peakPendingBytes = Math.Max(_peakPendingBytes, _pendingBytes);
                    if (kind == "END") _depth--;
                    if (_pendingBytes > 1048576 || _bytes + _pendingBytes >= ByteLimit - 8192)
                    { Stop("byte_limit_incomplete"); return; }
                    if (_depth == 0)
                    {
                        foreach (string entry in _pending) _writer.WriteLine(entry);
                        _bytes += _pendingBytes;
                        foreach (string entry in _pending) ObserveCommitted(entry);
                        _pending.Clear();
                        _pendingBytes = 0;
                    }
                    return;
                }
                if (_bytes + size >= ByteLimit - 8192 && kind != "SESSION_END")
                { _sequence--; Stop("byte_limit_incomplete"); return; }
                _writer.WriteLine(line);
                _bytes += size;
                ObserveCommitted(line);
            }
            catch (Exception ex) { Fail("write", ex); }
            finally { _writeDepth--; if (root) _writeTicks += Stopwatch.GetTimestamp() - started; }
        }

        internal static void Stop(string reason)
        {
            if (!Active) return;
            int discarded = _pending.Count;
            _discardedRows += discarded;
            _sequence -= discarded;
            _pending.Clear();
            _pendingBytes = 0;
            _depth = 0;
            if (_writeDepth == 0 && _bytes < ByteLimit - 16384) WriteCost();
            Write("SESSION_END", 0, 0, "capture", reason, _startDay, _lastDay,
                "game_left_running; discardedUncommittedRows=" + discarded
                + "; wallSeconds=" + N((Stopwatch.GetTimestamp() - _startTicks) / (double)Stopwatch.Frequency));
            Close(true, reason);
            SoakLog.Write("SUPPLY_CAPTURE_STOPPED", reason + "; game_left_running");
        }

        internal static void Fail(string boundary, Exception ex)
        {
            // Do not recursively attempt the failed writer. Missing SESSION_END
            // invalidates the capture; gameplay exceptions are never suppressed.
            _discardedRows += _pending.Count;
            _incidents?.Trigger("capture_failure:" + boundary, ex.GetType().FullName + ": " + ex.Message);
            Close(false, boundary + ": " + ex.Message);
            SoakLog.Write("SUPPLY_CAPTURE_FAILURE", boundary + ": " + ex);
        }

        private static void Close(bool closed=false, string reason="incomplete")
        {
            _incidents?.Flush(reason);
            // Optional observer detaches even if capture failed; never accumulates
            // subscribers across campaign reloads.
            try { ProcurementObservation.EndLedgerObservation(); }
            catch (Exception ex) { SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "ledger unsubscribe: " + ex); }
            try { NavalCashoutObserver.Stop(); }
            catch (Exception ex) { SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "naval policy unsubscribe: " + ex); }
            StreamWriter writer = _writer;
            _writer = null;
            if (writer == null)
            {
                CaptureReadiness.Close("BLOCKED", reason, _lastDay, _sequence, _bytes, _discardedRows, WriterSeconds);
                return;
            }
            try
            {
                writer.Dispose();
                if(_rollingStore!=null)
                {
                    _rollingStore.Checkpoint(_lastDay,closed);
                    FrameworkRuntimeSummary.Close(reason, _startDay, _lastDay, _bytes, _sequence, closed);
                }
                CaptureReadiness.Close(closed ? "CLOSED" : "BLOCKED", reason, _lastDay, _sequence, _bytes, _discardedRows, WriterSeconds);
            }
            catch (Exception ex)
            {
                CaptureReadiness.Close("BLOCKED", "close: " + ex.Message, _lastDay, _sequence, _bytes, _discardedRows, WriterSeconds);
                SoakLog.Write("SUPPLY_CAPTURE_FAILURE", "close: " + ex);
            }
        }
        private static void Flush()
        {
            long start = Stopwatch.GetTimestamp();
            try { _writer.Flush(); }
            finally { _flushCostTicks += Stopwatch.GetTimestamp() - start; }
        }
        internal static int ReadAcceptanceDays(string directory)
        {
            string path = Path.Combine(directory, "AocAcceptance.days");
            if (!File.Exists(path)) return 0; // Unchanged normal 30-day rolling mode.
            int days;
            if (!int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out days)
                || days < 1 || days >= TargetDays)
                throw new InvalidDataException("Acceptance days must be an integer from 1 to 29; no game settings changed");
            return days;
        }
        private static void PulseReadiness()
        {
            CaptureReadiness.Pulse(_lastDay, _sequence - _pending.Count, _writer.BaseStream.Length,
                _pending.Count, _discardedRows, WriterSeconds);
        }
        private static void WriteCost()
        {
            _incidents?.Flush("cost_checkpoint");
            Write("DIAGNOSTIC_COST", 0, 0, "capture", "cumulative_lower_bound", 0, WriterSeconds,
                "writeCalls=" + _writeCalls + "; flushSeconds=" + N(_flushCostTicks / (double)Stopwatch.Frequency)
                + "; discardedRows=" + _discardedRows + "; pendingRows=" + _pending.Count
                + "; peakPendingBytes=" + _peakPendingBytes + "; bytes=" + _bytes
                + "; wallSeconds=" + N((Stopwatch.GetTimestamp() - _startTicks) / (double)Stopwatch.Frequency)
                + "; measuredObserverCosts=" + DiagnosticObserverCost.Json()
                + "; incidentRecorder=" + (_incidents?.StatusJson() ?? "null")
                + "; observer_costs_overlap_writer_do_not_add; uninstrumented_callbacks_unknown"
                + "; excludes_observer_snapshots_stack_traces_and_status_IO; not_total_diagnostic_overhead");
        }
        private static void ObserveCommitted(string line)
        {
            string[] fields = line.Split('\t');
            if(fields.Length == 12)
            {
                FrameworkRuntimeSummary.Observe(fields[4], fields[8], fields[11]);
                DiagnosticEventCoverage.Observe(fields[4], long.Parse(fields[2], CultureInfo.InvariantCulture));
                _incidents?.Observe(line, fields);
            }
        }
        internal static string N(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static string Clean(string value) { return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
