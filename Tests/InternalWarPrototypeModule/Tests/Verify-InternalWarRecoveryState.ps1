$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Actual sentinel source, data-only native save interface. Run with pwsh.
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarRecoveryState.cs') -Raw
$tests = @'
namespace TaleWorlds.CampaignSystem {
 public interface IDataStore { bool IsLoading {get;} bool IsSaving {get;} bool SyncData<T>(string key, ref T value); }
}
namespace AgesOfCalradiaInternalWarsTest {
 public static class RecoveryStateVerification {
  private static int checks;
  private static void Check(bool value, string message) { checks++; if (!value) throw new System.InvalidOperationException(message); }
  private sealed class Store : TaleWorlds.CampaignSystem.IDataStore {
   public bool IsLoading {get;set;} public bool IsSaving {get;set;}
   internal string Payload; internal bool Present; internal int Calls;
   public bool SyncData<T>(string key, ref T value) {
    Check(key == InternalWarRecoveryState.SaveKey, "Unexpected save key."); Calls++;
    if (IsSaving) { Payload = (string)(object)value; Present = true; }
    if (IsLoading && Present) value = (T)(object)Payload;
    return Present;
   }
  }
  public static int Run() {
   var state = new InternalWarRecoveryState();
   state.SyncData(new Store { IsLoading = true });
   Check(state.Reason.Length == 0, "Missing legacy key blocked a clean campaign.");
   Check(state.TryBlock("native membership uncertain"), "First failure was not retained.");
   Check(!state.TryBlock("secondary parse error") && state.Reason == "native membership uncertain", "Secondary error hid root cause.");
   var save = new Store { IsSaving = true }; state.SyncData(save);
   Check(save.Calls == 1 && save.Present && save.Payload == state.Reason, "Blocked save omitted sentinel.");
   var restored = new InternalWarRecoveryState();
   restored.SyncData(new Store { IsLoading = true, Present = true, Payload = save.Payload });
   Check(restored.Reason == state.Reason, "Reload re-enabled unsafe actions.");
   restored.SyncData(new Store { IsLoading = true });
   Check(restored.Reason == state.Reason, "Missing key cleared a runtime safety stop.");
   restored.SyncData(new Store { IsLoading = true, Present = true, Payload = "" });
   Check(restored.Reason == state.Reason, "Empty key cleared a runtime safety stop.");
   var again = new Store { IsSaving = true }; restored.SyncData(again);
   Check(again.Payload == save.Payload, "Second save lost original evidence.");
   foreach (string value in new string[] { null, "", "  " }) {
    var invalid = new InternalWarRecoveryState();
    Check(invalid.TryBlock(value) && invalid.Reason.Length > 0, "Empty failure silently unblocked recovery.");
   }
   var clean = new InternalWarRecoveryState(); var cleanSave = new Store { IsSaving = true };
   clean.SyncData(cleanSave); Check(cleanSave.Payload == "", "Clean save invented a failure.");
   var nextCampaign = new InternalWarRecoveryState(); nextCampaign.SyncData(cleanSave = new Store { IsLoading = true, Present = true, Payload = "" });
   Check(nextCampaign.Reason == "", "Clean campaign inherited another campaign's blocker.");
   return checks;
  }
 }
}
'@
Add-Type -TypeDefinition ($source + [Environment]::NewLine + $tests)
$count = [AgesOfCalradiaInternalWarsTest.RecoveryStateVerification]::Run()
# Verify actual owner wiring too: children share the root, restoration precedes operations,
# and saving the sentinel is outside the payload-preservation guard.
$behavior = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarTestBehavior.cs') -Raw
$recovery = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarTestRecovery.cs') -Raw
if ($recovery -notmatch '_root\._recoveryState\.Reason' -or $recovery -notmatch '_root\._recoveryState\.TryBlock\(reason\)') { throw 'Controllers do not share the root recovery sentinel.' }
if ($behavior -notmatch '(?s)if \(_root == this && dataStore.IsLoading\) _recoveryState.SyncData\(dataStore\);.*?SyncPaymentSafety') { throw 'Recovery is not restored before operational payloads.' }
if ($behavior -notmatch '(?s)SyncControllers\(dataStore\);.*?if \(_root == this && dataStore.IsSaving\) _recoveryState.SyncData\(dataStore\);') { throw 'Recovery sentinel is not saved independently after controllers.' }
$monitor = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarDiagnosticMonitor.cs') -Raw
if ($monitor -notmatch 'registry.Length == 0\s*&& string.IsNullOrEmpty\(_owner.RecoveryBlocker\)') { throw 'Empty malformed campaigns suppress recovery diagnostics.' }
Write-Host "Actual recovery sentinel: $count assertions and 4 owner-wiring contracts passed; no native game calls."
