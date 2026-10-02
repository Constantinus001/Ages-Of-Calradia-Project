using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    // Campaign-wide, sticky first-failure sentinel. Unlike operation payloads this key is
    // always written, even when recovery deliberately preserves older payloads unchanged.
    // No automatic clearing: uncertain native membership requires the pre-failure backup.
    internal sealed class InternalWarRecoveryState
    {
        internal const string SaveKey = "AOC_InternalConflict_RecoveryBlocker_v1";
        internal string Reason { get; private set; } = string.Empty;

        internal bool TryBlock(string reason)
        {
            if (!string.IsNullOrEmpty(Reason)) return false;
            Reason = string.IsNullOrWhiteSpace(reason) ? "Private-war recovery requires inspection." : reason;
            return true;
        }

        internal void SyncData(IDataStore store)
        {
            string payload = store.IsLoading ? string.Empty : Reason;
            store.SyncData(SaveKey, ref payload);
            if (store.IsLoading && !string.IsNullOrEmpty(payload)) TryBlock(payload);
        }
    }
}
