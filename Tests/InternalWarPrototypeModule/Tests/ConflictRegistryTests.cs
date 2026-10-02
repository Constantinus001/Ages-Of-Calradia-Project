using System;
using AgesOfCalradiaInternalWarsTest;

internal static class ConflictRegistryTests
{
    private static int _checks;
    internal static int Run()
    {
        _checks = 0;
        var registry = new InternalWarConflictRegistry();
        var first = NewRecord("war|%\nü", "a", "b");
        Assert(registry.Add(first), "First war accepted");
        Assert(ReferenceEquals(first, registry.Find("b", "a")), "Unordered lookup preserves live identity");
        Assert(ReferenceEquals(first, registry.GetById(first.Id)), "ID lookup preserves live identity");
        Assert(!registry.Add(NewRecord("duplicate-pair", "b", "a")), "Reversed open pair rejected");
        Assert(!registry.Add(NewRecord(first.Id, "c", "d")), "Duplicate ID rejected");
        Assert(!registry.Add(null), "Null rejected");
        Assert(registry.Add(NewRecord("other", "a", "c")), "Shared clan in distinct war supported");
        first.RequestPeace();
        Assert(ReferenceEquals(first, registry.Find("a", "b")), "Pending peace remains open");
        Assert(!registry.Add(NewRecord("pending", "a", "b")), "Pending pair blocks redeclaration");
        first.Phase = InternalConflictPhase.Closed;
        Assert(registry.Find("a", "b") == null, "Closed war excluded from open lookup");
        var renewed = NewRecord("renewed", "b", "a");
        Assert(registry.Add(renewed), "Closed pair can declare a new war");
        Assert(registry.Count == 3, "Closed war retained");
        var restored = InternalWarConflictRegistry.Deserialize(registry.Serialize());
        Assert(restored != null && restored.Count == 3, "All records round trip");
        Assert(restored.GetById(first.Id).Phase == InternalConflictPhase.Closed, "Escaped historical ID round trips");
        Assert(restored.Find("a", "b").Id == renewed.Id, "Reload selects current war");
        Assert(restored.Serialize() == registry.Serialize(), "Serialization stable");
        Assert(InternalWarConflictRegistry.Deserialize(null).Count == 0, "Missing save key initializes empty");
        Assert(InternalWarConflictRegistry.Deserialize("").Count == 0, "Empty save initializes empty");
        Assert(InternalWarConflictRegistry.Deserialize("v1").Count == 0, "Empty envelope accepted");
        foreach (string bad in new[] { " ", "v2", "v1\n", "v1\n%ZZ", "v1\nbogus", "v1\r\n" })
            Assert(InternalWarConflictRegistry.Deserialize(bad) == null, "Malformed envelope rejected: " + bad);
        string row = Uri.EscapeDataString(renewed.Serialize());
        Assert(InternalWarConflictRegistry.Deserialize("v1\n" + row + "\n" + row) == null, "Duplicate persisted ID rejected");
        Assert(InternalWarConflictRegistry.Deserialize("v1\n" + row + "\n"
            + Uri.EscapeDataString(NewRecord("reverse", "a", "b").Serialize())) == null, "Duplicate persisted pair rejected");
        var invalid = NewRecord("invalid", "a", "b");
        invalid.StartDay = -1;
        Assert(!registry.Add(invalid), "Invalid record rejected before insertion");
        invalid.Id = null;
        Assert(!registry.Add(invalid), "Null record field rejected without exception");
        Assert(registry.Find(null, "a") == null && registry.Find("a", "a") == null, "Invalid pair lookups return none");
        first.Phase = InternalConflictPhase.Active;
        bool threw = false;
        try { registry.Serialize(); } catch (InvalidOperationException) { threw = true; }
        Assert(threw, "Mutable record reopening duplicate pair refuses save");
        first.Phase = InternalConflictPhase.Closed;
        first.Id = renewed.Id;
        threw = false;
        try { registry.Serialize(); } catch (InvalidOperationException) { threw = true; }
        Assert(threw, "Mutable duplicate ID refuses save");
        return _checks;
    }

    private static InternalConflictRecord NewRecord(string id, string a, string b)
    {
        return new InternalConflictRecord { Id = id, KingdomId = "realm", AttackerClanId = a,
            DefenderClanId = b, StartDay = 5, Phase = InternalConflictPhase.Active };
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Conflict registry: " + message);
    }
}
