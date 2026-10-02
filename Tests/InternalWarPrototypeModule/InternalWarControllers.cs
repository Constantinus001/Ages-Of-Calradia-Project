using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    internal sealed partial class InternalWarTestBehavior
    {
        private readonly InternalWarTestBehavior _root;
        private readonly List<InternalWarTestBehavior> _controllers;
        private InternalWarTestBehavior _selectedController;
        internal InternalWarTestBehavior SelectedController { get { return _root._selectedController ?? _root; } }
        internal IEnumerable<InternalWarTestBehavior> Controllers { get { return _root._controllers; } }
        private bool HasPendingWork { get { return (Conflict != null && Conflict.IsOpen) || HasActiveRaid
            || (CurrentRecord != null && (CurrentRecord.IsOperational || !CurrentRecord.CleanupComplete)); } }
        internal bool OwnsPartyOrder(string id)
        { return (CurrentRecord != null && CurrentRecord.IsOperational && CurrentRecord.LeaderPartyId == id)
            || (HasActiveRaid && Raid.LeaderPartyId == id); }
        internal bool OwnsSettlementOrder(string id)
        { return (CurrentRecord != null && CurrentRecord.IsOperational && CurrentRecord.SettlementId == id)
            || (HasActiveRaid && Raid.SettlementId == id); }
        private InternalWarTestBehavior ControllerFor(InternalConflictRecord war)
        {
            InternalWarTestBehavior controller = Controllers.FirstOrDefault(c => c.Conflict == war);
            if (controller != null) return controller;
            controller = new InternalWarTestBehavior(_root) { Conflict = war };
            _root._controllers.Add(controller);
            return controller;
        }
        private void SyncControllers(IDataStore store)
        {
            if (_root != this) return;
            string selected = SelectedController.Conflict == null ? string.Empty : SelectedController.Conflict.Id;
            store.SyncData("AOC_InternalConflict_SelectedController", ref selected);
            foreach (InternalConflictRecord war in _conflicts.Records.ToArray())
            {
                if (war == Conflict) continue;
                InternalWarTestBehavior controller = ControllerFor(war);
                controller.SyncData(new InternalWarScopedDataStore(store, "AOC_War_" + war.Id + "_"));
                if (store.IsLoading)
                {
                    if (controller.Conflict == null)
                    {
                        if (controller.CurrentRecord != null || controller.Raid != null
                            || (!string.IsNullOrEmpty(war.ActiveOperationId) && war.ActiveOperationId != war.LastCompletedOperationId))
                            BlockRecovery("A war controller is missing its unfinished operation: " + war.Id);
                        controller.Conflict = war;
                    }
                    else if (controller.Conflict.Serialize() != war.Serialize())
                        BlockRecovery("A war controller disagrees with its registry identity: " + war.Id);
                    else controller.Conflict = war;
                }
            }
            if (store.IsLoading)
            {
                InternalWarTestBehavior[] active = Controllers.ToArray();
                for (int index = 0; index < active.Length; index++)
                {
                    InternalWarTestBehavior owner = active[index];
                    if (owner.Conflict != null && owner.Conflict.Phase == InternalConflictPhase.Closed
                        && (owner.HasActiveRaid || (owner.CurrentRecord != null
                            && (owner.CurrentRecord.IsOperational || !owner.CurrentRecord.CleanupComplete))))
                        BlockRecovery("A closed war still contains unfinished native operations.");
                    if (owner.HasActiveRaid && owner.CurrentRecord != null
                        && (owner.CurrentRecord.IsOperational || !owner.CurrentRecord.CleanupComplete))
                        BlockRecovery("A saved war contains both an unfinished siege and a raid.");
                    string party = owner.CurrentRecord != null && owner.CurrentRecord.IsOperational ? owner.CurrentRecord.LeaderPartyId : null;
                    string target = owner.CurrentRecord != null && owner.CurrentRecord.IsOperational ? owner.CurrentRecord.SettlementId : null;
                    foreach (InternalWarTestBehavior other in active.Skip(index + 1))
                        if ((party != null && other.OwnsPartyOrder(party)) || (target != null && other.OwnsSettlementOrder(target))
                            || (owner.HasActiveRaid && (other.OwnsPartyOrder(owner.Raid.LeaderPartyId)
                                || other.OwnsSettlementOrder(owner.Raid.SettlementId))))
                            BlockRecovery("Saved war operations compete for the same party or settlement.");
                }
                InternalWarTestBehavior chosen = Controllers.FirstOrDefault(c => c.Conflict != null && c.Conflict.Id == selected);
                if (!string.IsNullOrEmpty(selected) && chosen == null) BlockRecovery("The selected war controller is missing.");
                _selectedController = chosen ?? this;
            }
        }
    }
}
