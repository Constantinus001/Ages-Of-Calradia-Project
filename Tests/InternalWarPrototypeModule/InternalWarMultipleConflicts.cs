using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior
    {
        private InternalWarConflictRegistry _conflicts = new InternalWarConflictRegistry();
        private string _registryPayload = string.Empty;
        internal IEnumerable<InternalConflictRecord> Conflicts { get { return _conflicts.Records; } }
        internal string RegistrySignature { get { return string.Join(";", Conflicts.Select(c => c.Id + "/" + c.Phase
            + "/" + c.GoalSettlementId + "/" + c.GoalSatisfied + "/" + c.CompensationGold)); } }
        internal InternalConflictRecord FindConflict(string first, string second) { return _conflicts.Find(first, second); }

        private void SyncRegistry(IDataStore store)
        {
            if (store.IsSaving && string.IsNullOrEmpty(RecoveryBlocker)) _registryPayload = _conflicts.Serialize();
            store.SyncData("AOC_InternalConflict_Registry_v1", ref _registryPayload);
            if (!store.IsLoading) return;
            InternalWarConflictRegistry restored = InternalWarConflictRegistry.Deserialize(_registryPayload);
            if (restored == null) { BlockRecovery("Conflict registry is invalid; original payload retained."); return; }
            _conflicts = restored;
            if (Conflict == null) return;
            if (string.IsNullOrEmpty(_registryPayload))
            { if (!_conflicts.Add(Conflict)) BlockRecovery("Legacy conflict could not migrate into the registry."); return; }
            InternalConflictRecord selected = _conflicts.GetById(Conflict.Id);
            if (selected == null || selected.Serialize() != Conflict.Serialize())
            { BlockRecovery("Selected conflict disagrees with the saved registry."); return; }
            Conflict = selected;
        }

        internal bool SelectConflict(string id, out string result)
        {
            result = string.Empty;
            if (!string.IsNullOrEmpty(RecoveryBlocker)) { result = RecoveryBlocker; return false; }
            InternalConflictRecord selected = _conflicts.GetById(id);
            if (selected == null || !selected.IsOpen) { result = "That conflict is no longer open."; return false; }
            _root._selectedController = ControllerFor(selected);
            return true;
        }
    }
}
