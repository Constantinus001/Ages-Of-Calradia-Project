using System;
using System.Linq;
using AgesOfCalradiaInternalWarsTest;
internal static class ControllerProgram
{
    private static int _checks;
    private static InternalConflictRecord War(string id)
    { return new InternalConflictRecord { Id = id, KingdomId = "realm", AttackerClanId = "a", DefenderClanId = id }; }
    private static InternalWarTestRecord Operation(string war)
    { return new InternalWarTestRecord { ConflictId = "op_" + war, KingdomId = "realm", AttackerClanId = "a", DefenderClanId = war,
        SettlementId = "town_" + war, LeaderPartyId = "party_" + war, State = InternalWarTestState.Marching }; }
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static string Key(string war, string key) { return Uri.EscapeDataString("AOC_War_" + war + "_") + ":" + key; }
    private static ControllerStore Snapshot(out InternalConflictRecord first, out InternalConflictRecord second)
    {
        first = War("b"); second = War("c");
        var root = new InternalWarTestBehavior();
        var one = root.Add(first); var two = root.Add(second);
        one.CurrentRecord = Operation("b"); two.CurrentRecord = Operation("c");
        first.ActiveOperationId = one.CurrentRecord.ConflictId; second.ActiveOperationId = two.CurrentRecord.ConflictId;
        root.Select(two);
        var store = new ControllerStore(); root.VerifySync(store); store.IsLoading = true; return store;
    }
    private static InternalWarTestBehavior Restore(ControllerStore store, InternalConflictRecord first, InternalConflictRecord second)
    {
        var root = new InternalWarTestBehavior();
        root.Add(InternalConflictRecord.Deserialize(first.Serialize())); root.Add(InternalConflictRecord.Deserialize(second.Serialize()));
        root.VerifySync(store); return root;
    }
    private static int Main()
    {
        try
        {
            InternalConflictRecord first, second;
            ControllerStore store = Snapshot(out first, out second);
            var root = Restore(store, first, second);
            Check(root.RecoveryBlocker.Length == 0, "Valid child controllers were rejected.");
            Check(root.SelectedController.Conflict.Id == "c", "Selected child did not round-trip.");
            Check(root.Controllers.Single(c => c.Conflict != null && c.Conflict.Id == "b").CurrentRecord.SettlementId == "town_b",
                "First operation lost its scoped identity.");
            Check(root.Controllers.Single(c => c.Conflict != null && c.Conflict.Id == "c").CurrentRecord.SettlementId == "town_c",
                "Second operation collided with first.");
            store = Snapshot(out first, out second);
            store.Values.Remove(Key("b", "conflict")); store.Values.Remove(Key("b", "operation"));
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Missing unfinished child was accepted.");
            store = Snapshot(out first, out second);
            var changed = War("b"); changed.StartDay = 7; store.Values[Key("b", "conflict")] = changed.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Registry/controller mismatch was accepted.");
            store = Snapshot(out first, out second);
            store.Values["AOC_InternalConflict_SelectedController"] = "missing";
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Missing selected controller was accepted.");
            store = Snapshot(out first, out second);
            first.ActiveOperationId = string.Empty;
            store.Values.Remove(Key("b", "conflict"));
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Orphan operation with no child conflict was adopted.");
            store = Snapshot(out first, out second);
            first.ActiveOperationId = string.Empty;
            store.Values.Remove(Key("b", "conflict")); store.Values.Remove(Key("b", "operation"));
            store.Values[Key("b", "raid")] = new InternalWarRaidRecord { Id = "raid_b", ConflictId = "b", SettlementId = "village_b",
                LeaderPartyId = "party_b", DefenderClanId = "b" }.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Orphan active raid with no child conflict was adopted.");
            store = Snapshot(out first, out second);
            var duplicate = Operation("c"); duplicate.LeaderPartyId = "party_b";
            store.Values[Key("c", "operation")] = duplicate.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Two controllers reserved the same party.");
            store = Snapshot(out first, out second);
            duplicate = Operation("c"); duplicate.SettlementId = "town_b";
            store.Values[Key("c", "operation")] = duplicate.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Two controllers reserved the same settlement.");
            store = Snapshot(out first, out second);
            store.Values[Key("c", "raid")] = new InternalWarRaidRecord { Id = "raid_c", ConflictId = "c", SettlementId = "village_c",
                LeaderPartyId = "party_b", DefenderClanId = "c" }.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Raid and siege reserved the same party.");
            store = Snapshot(out first, out second);
            store.Values[Key("b", "raid")] = new InternalWarRaidRecord { Id = "raid_b", ConflictId = "b", SettlementId = "village_shared",
                LeaderPartyId = "raider_b", DefenderClanId = "b" }.Serialize();
            store.Values[Key("c", "raid")] = new InternalWarRaidRecord { Id = "raid_c", ConflictId = "c", SettlementId = "village_shared",
                LeaderPartyId = "raider_c", DefenderClanId = "c" }.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "Two raids reserved the same village.");
            store = Snapshot(out first, out second);
            store.Values[Key("b", "raid")] = new InternalWarRaidRecord { Id = "same_war_raid", ConflictId = "b", SettlementId = "other_village",
                LeaderPartyId = "other_party", DefenderClanId = "b" }.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Length != 0, "A war loaded simultaneous unfinished siege and raid operations.");
            store = Snapshot(out first, out second);
            first.Phase = InternalConflictPhase.Closed;
            store.Values[Key("b", "conflict")] = first.Serialize();
            Check(Restore(store, first, second).RecoveryBlocker.Contains("closed war"), "A closed war resumed an unfinished operation.");
            Console.WriteLine("Actual controller persistence: " + _checks + " assertions passed; child transport stubbed, no native game calls.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
