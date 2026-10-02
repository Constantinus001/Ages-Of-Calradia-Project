$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarDiagnosticMonitor.cs') -Raw
$tests = @'
namespace TaleWorlds.CampaignSystem {
 public class CampaignTime { public static CampaignTime Now = new CampaignTime(); public double ToDays = 1; }
}
namespace TaleWorlds.InputSystem {
 public enum InputKey { F10, LeftControl, LeftShift }
 public static class Input { public static bool IsKeyPressed(InputKey k) {return false;} public static bool IsKeyDown(InputKey k) {return false;} }
}
namespace AgesOfCalradiaInternalWarsTest {
 internal sealed class Record {
  internal string Id = "war", ConflictId = "operation", Phase = "Active", State = "Preparing", LeaderPartyId = "party";
  internal bool CaptureApplied = false, CleanupComplete = false, Closed = false, EventObserved = false, StopRequested = false, IsOperational = true;
 }
 internal sealed class PartyStub { internal PartyStub Party {get {return this;}} internal Battle MapEvent = null; }
 internal sealed class Battle { internal string EventType = "Siege", State = "Active"; internal bool IsFinalized = false; }
 internal sealed class InternalWarTestBehavior {
  internal string RegistrySignature = "war", RecoveryBlocker = "";
  internal Record Conflict = new Record(), CurrentRecord = null, Raid = null;
  internal bool HasActiveRaid {get {return Raid != null;}}
  internal System.Collections.Generic.IEnumerable<InternalWarTestBehavior> Controllers {get {yield return this;}}
  internal static PartyStub FindParty(string id) {return null;}
 }
 internal static class InternalWarTestDiagnostics {
  internal static System.Collections.Generic.List<string> Messages = new System.Collections.Generic.List<string>();
  internal static void Info(string s) {Messages.Add(s);}
  internal static void Error(string s, System.Exception e) {Messages.Add(s);}
 }
 internal static class InternalWarDiagnosticsReport {
  internal static bool Success; internal static int Calls; internal static string LastReason;
  internal static bool TryWrite(InternalWarTestBehavior b, string reason, out string path, out string result) {
   Calls++; LastReason = reason; path = ""; result = Success ? "written" : "disk unavailable"; return Success;
  }
 }
 public static class MonitorVerification {
  private static int checks;
  private static void Check(bool b, string s) {checks++; if (!b) throw new System.Exception(s);}
  public static int Run() {
   var owner = new InternalWarTestBehavior(); var monitor = new InternalWarDiagnosticMonitor(owner);
   monitor.Tick(1); Check(InternalWarDiagnosticsReport.Calls == 1, "Initial snapshot missing");
   owner.Conflict.Phase = "PeacePending";
   for (int i=0;i<59;i++) monitor.Tick(1);
   Check(InternalWarDiagnosticsReport.Calls == 1, "Failure retried before backoff elapsed");
   Check(InternalWarTestDiagnostics.Messages.Exists(s => s.Contains("PeacePending")), "Backoff suppressed state observation");
   monitor.Tick(1); Check(InternalWarDiagnosticsReport.Calls == 2, "Failure never retried");
   Check(InternalWarTestDiagnostics.Messages.FindAll(s => s.StartsWith("Automatic snapshot deferred:")).Count == 1, "Repeated failure flooded logs");
   InternalWarDiagnosticsReport.Success = true; owner.Conflict.Phase = "Closed";
   monitor.Tick(60); Check(InternalWarDiagnosticsReport.Calls == 3 && InternalWarDiagnosticsReport.LastReason.Contains("Closed"), "Retry did not capture latest state");
   monitor.Tick(1); Check(InternalWarDiagnosticsReport.Calls == 3, "Unchanged successful state rewritten too early");
   monitor.Tick(59); Check(InternalWarDiagnosticsReport.Calls == 4 && InternalWarDiagnosticsReport.LastReason.StartsWith("periodic health:"), "Stable native state was never health-checked");
   InternalWarDiagnosticsReport.Success = false; owner.Conflict.Id = "next-war"; monitor.Tick(1);
   Check(InternalWarDiagnosticsReport.Calls == 5, "New transition ignored");
   Check(InternalWarTestDiagnostics.Messages.FindAll(s => s.StartsWith("Automatic snapshot deferred:")).Count == 2, "New failure episode was hidden");
   var empty = new InternalWarTestBehavior {Conflict = null, RegistrySignature = "", RecoveryBlocker = "unsafe"};
   new InternalWarDiagnosticMonitor(empty).Tick(1);
   Check(InternalWarDiagnosticsReport.LastReason.Contains("unsafe"), "Empty blocked campaign lacked snapshot");
   return checks;
  }
 }
}
'@
# Actual monitor; fake native input, time and report sink perform no game or file operations.
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $tests)
$count = [AgesOfCalradiaInternalWarsTest.MonitorVerification]::Run()
Write-Host "Actual diagnostic monitor: $count assertions passed."
