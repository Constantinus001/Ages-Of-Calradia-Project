using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
namespace TaleWorlds.CampaignSystem
{
    public interface IDataStore { bool IsLoading { get; } bool IsSaving { get; } bool SyncData<T>(string key, ref T data); }
}
namespace AgesOfCalradiaInternalWarsTest
{
    // Deliberately stub only the owner shell and child payload transport. Controller dispatch,
    // identity reconciliation, reservations, registry and record serialization are actual source.
    internal sealed partial class InternalWarTestBehavior
    {
        private readonly InternalWarConflictRegistry _conflicts = new InternalWarConflictRegistry();
        internal InternalConflictRecord Conflict;
        internal InternalWarTestRecord CurrentRecord;
        internal InternalWarRaidRecord Raid;
        internal bool HasActiveRaid { get { return Raid != null && !Raid.Closed; } }
        internal string RecoveryBlocker = string.Empty;
        internal InternalWarTestBehavior() : this(null) { }
        private InternalWarTestBehavior(InternalWarTestBehavior root)
        {
            _root = root ?? this;
            _controllers = root == null ? new List<InternalWarTestBehavior> { this } : null;
        }
        internal void BlockRecovery(string message) { _root.RecoveryBlocker = message; }
        internal InternalWarTestBehavior Add(InternalConflictRecord war)
        { if (!_conflicts.Add(war)) throw new InvalidOperationException("Invalid fixture war."); return ControllerFor(war); }
        internal void Select(InternalWarTestBehavior controller) { _selectedController = controller; }
        internal void VerifySync(IDataStore store) { SyncControllers(store); }
        internal void SyncData(IDataStore store)
        {
            string conflict = store.IsSaving && Conflict != null ? Conflict.Serialize() : string.Empty;
            string operation = store.IsSaving && CurrentRecord != null ? CurrentRecord.Serialize() : string.Empty;
            string raid = store.IsSaving && Raid != null ? Raid.Serialize() : string.Empty;
            store.SyncData("conflict", ref conflict); store.SyncData("operation", ref operation); store.SyncData("raid", ref raid);
            if (store.IsLoading)
            {
                Conflict = InternalConflictRecord.Deserialize(conflict);
                CurrentRecord = InternalWarTestRecord.Deserialize(operation);
                Raid = InternalWarRaidRecord.Deserialize(raid);
            }
        }
    }
}
internal sealed class ControllerStore : IDataStore
{
    internal readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    public bool IsLoading { get; set; }
    public bool IsSaving { get { return !IsLoading; } }
    public bool SyncData<T>(string key, ref T data)
    {
        if (IsSaving) { Values[key] = data; return true; }
        object value; if (!Values.TryGetValue(key, out value)) return false;
        data = (T)value; return true;
    }
}
