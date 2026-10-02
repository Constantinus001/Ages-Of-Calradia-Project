$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Compile the actual adapter against a data-only interface. Never load Bannerlord.
$adapterSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\InternalWarScopedDataStore.cs') -Raw
$verificationSource = @'
namespace TaleWorlds.CampaignSystem
{
    public interface IDataStore
    {
        bool IsLoading { get; }
        bool IsSaving { get; }
        bool SyncData<T>(string key, ref T data);
    }
}
namespace AgesOfCalradiaInternalWarsTest
{
    public static class ScopedDataStoreVerification
    {
        private sealed class Store : TaleWorlds.CampaignSystem.IDataStore
        {
            internal readonly System.Collections.Generic.Dictionary<string, object> Values =
                new System.Collections.Generic.Dictionary<string, object>();
            public bool IsLoading { get; set; }
            public bool IsSaving { get; set; }
            internal bool Result = true;
            public bool SyncData<T>(string key, ref T data)
            {
                if (IsSaving) Values[key] = data;
                if (IsLoading && Values.ContainsKey(key)) data = (T)Values[key];
                return Result;
            }
        }
        private static int _checks;
        private static void Check(bool value, string message)
        { _checks++; if (!value) throw new System.InvalidOperationException(message); }
        private static void Reject(System.Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (System.ArgumentException) { rejected = true; }
            Check(rejected, message);
        }
        public static int Run()
        {
            _checks = 0;
            var store = new Store { IsSaving = true };
            var first = new InternalWarScopedDataStore(store, "warA");
            var second = new InternalWarScopedDataStore(store, "warB");
            int value = 11;
            Check(first.SyncData("operation", ref value), "True sync return was lost.");
            value = 22;
            second.SyncData("operation", ref value);
            Check(store.Values.Count == 2, "War namespaces collided.");
            Check(first.IsSaving && !first.IsLoading, "Saving flags not forwarded.");
            store.IsSaving = false; store.IsLoading = true;
            Check(!first.IsSaving && first.IsLoading, "Changed loading flags not forwarded.");
            value = 0; first.SyncData("operation", ref value);
            Check(value == 11, "First scope value did not flow back through ref.");
            value = 0; second.SyncData("operation", ref value);
            Check(value == 22, "Second scope value did not flow back through ref.");
            store.Result = false;
            Check(!first.SyncData("operation", ref value), "False sync return was lost.");
            store.IsSaving = true; store.IsLoading = false;
            var colonScope = new InternalWarScopedDataStore(store, "a:b");
            var ordinaryScope = new InternalWarScopedDataStore(store, "a");
            var escapedScope = new InternalWarScopedDataStore(store, "a%3Ab");
            colonScope.SyncData("c", ref value);
            ordinaryScope.SyncData("b:c", ref value);
            escapedScope.SyncData("c", ref value);
            Check(store.Values.Count == 5, "Delimiter or literal percent names collided.");
            string text = "saved text";
            first.SyncData("text", ref text);
            store.IsSaving = false; store.IsLoading = true;
            text = null; first.SyncData("text", ref text);
            Check(text == "saved text", "Generic reference-type value was not forwarded.");
            Reject(() => new InternalWarScopedDataStore(null, "war"), "Null store accepted.");
            Reject(() => new InternalWarScopedDataStore(store, null), "Null scope accepted.");
            Reject(() => new InternalWarScopedDataStore(store, ""), "Empty scope accepted.");
            Reject(() => new InternalWarScopedDataStore(store, " \t"), "Whitespace scope accepted.");
            Reject(() => { int n = 0; first.SyncData(null, ref n); }, "Null key accepted.");
            Reject(() => { int n = 0; first.SyncData("", ref n); }, "Empty key accepted.");
            Reject(() => { int n = 0; first.SyncData(" \t", ref n); }, "Whitespace key accepted.");
            return _checks;
        }
    }
}
'@
Add-Type -TypeDefinition ($adapterSource + [Environment]::NewLine + $verificationSource)
$checks = [AgesOfCalradiaInternalWarsTest.ScopedDataStoreVerification]::Run()
Write-Output "Scoped datastore actual-source checks passed: $checks assertions; no native game calls."
