using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.SoakDiagnostics
{
    internal static class EconomyWarehouseDiagnostics
    {
        private static readonly FieldInfo Rosters = typeof(WorkshopsCampaignBehavior).GetField("_warehouseRosterPerSettlement", BindingFlags.NonPublic | BindingFlags.Instance);
        internal static void Snapshot()
        {
            var behavior = Campaign.Current.GetCampaignBehavior<WorkshopsCampaignBehavior>();
            if (behavior == null) return;
            if (Rosters == null || Rosters.FieldType != typeof(KeyValuePair<Settlement, ItemRoster>[]))
                throw new MissingFieldException("Native warehouse backing collection changed; cannot reconcile warehouse stock");
            var pairs = (KeyValuePair<Settlement, ItemRoster>[])Rosters.GetValue(behavior);
            if (pairs == null) return;
            foreach (var pair in pairs)
            {
                if (pair.Value == null) continue;
                string owner = EconomyTransactionDiagnostics.Id(pair.Key) + "/warehouse";
                EconomyTransactionDiagnostics.Roster(pair.Value, owner);
                EconomyTrace.Write("WAREHOUSE_STATE", 0, 0, owner, "entries", pair.Value.Count, pair.Value.Count, "readonly-native-storage", "existing warehouse only; no getter creates storage");
                foreach (var entry in pair.Value)
                    EconomyTrace.Write("WAREHOUSE_STOCK", 0, 0, owner, entry.EquipmentElement.Item.StringId, entry.Amount, entry.Amount,
                        "readonly-native-storage", "actual inventory; modifiers separate in transaction ledger");
            }
        }
    }
}
