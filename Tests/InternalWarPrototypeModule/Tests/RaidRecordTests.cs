using System;
using AgesOfCalradiaInternalWarsTest;

// Exercises the actual raid record only. No native raid lifecycle, loot, or village damage is simulated.
internal static class RaidRecordTests
{
    private static int _checks;

    internal static int Run()
    {
        _checks = 0;
        VerifyEscapedRoundTrip();
        VerifyFlagRoundTrips();
        VerifyDayBoundaries();
        RejectMalformedPayloads();
        return _checks;
    }

    private static InternalWarRaidRecord NewRecord()
    {
        return new InternalWarRaidRecord
        {
            Id = "raid|one/%", ConflictId = "war|ü", SettlementId = "village/one",
            LeaderPartyId = "lord party%", DefenderClanId = "clan|two", StartDay = 123,
            EventObserved = true, StopRequested = true, Closed = false
        };
    }

    private static void VerifyEscapedRoundTrip()
    {
        InternalWarRaidRecord original = NewRecord();
        string payload = original.Serialize();
        InternalWarRaidRecord restored = InternalWarRaidRecord.Deserialize(payload);
        Assert(restored != null, "Escaped raid record could not reload.");
        Assert(restored.Id == original.Id && restored.ConflictId == original.ConflictId
            && restored.SettlementId == original.SettlementId && restored.LeaderPartyId == original.LeaderPartyId
            && restored.DefenderClanId == original.DefenderClanId, "Raid identities did not round-trip.");
        Assert(restored.StartDay == original.StartDay && restored.EventObserved == original.EventObserved
            && restored.StopRequested == original.StopRequested && restored.Closed == original.Closed,
            "Raid observation/stop/closure flags did not round-trip.");
        Assert(restored.Serialize() == payload, "Raid record serialization was not stable after reload.");
    }

    private static void VerifyFlagRoundTrips()
    {
        for (int flags = 0; flags < 8; flags++)
        {
            InternalWarRaidRecord original = NewRecord();
            original.EventObserved = (flags & 1) != 0;
            original.StopRequested = (flags & 2) != 0;
            original.Closed = (flags & 4) != 0;
            InternalWarRaidRecord restored = InternalWarRaidRecord.Deserialize(original.Serialize());
            Assert(restored != null && restored.EventObserved == original.EventObserved
                && restored.StopRequested == original.StopRequested && restored.Closed == original.Closed,
                "Valid independent raid flags were lost or reinterpreted: " + flags + ".");
        }
    }

    private static void VerifyDayBoundaries()
    {
        foreach (int day in new[] { 0, int.MaxValue })
        {
            InternalWarRaidRecord record = NewRecord();
            record.StartDay = day;
            InternalWarRaidRecord restored = InternalWarRaidRecord.Deserialize(record.Serialize());
            Assert(restored != null && restored.StartDay == day, "Valid nonnegative day did not round-trip: " + day + ".");
        }
    }

    private static void RejectMalformedPayloads()
    {
        string valid = NewRecord().Serialize();
        foreach (string payload in new[] { null, string.Empty, "   ", "v1|too|short", valid + "|extra", ReplaceField(valid, 0, "v2") })
            Assert(InternalWarRaidRecord.Deserialize(payload) == null, "Malformed raid envelope was accepted: " + payload);

        for (int index = 1; index <= 5; index++)
        {
            foreach (string blank in new[] { string.Empty, "  ", "%20%09" })
                Assert(InternalWarRaidRecord.Deserialize(ReplaceField(valid, index, blank)) == null,
                    "Blank raid identity was accepted at field " + index + ".");
        }

        for (int index = 7; index <= 9; index++)
        {
            foreach (string flag in new[] { string.Empty, "true", "False", "2", "-1", "01" })
                Assert(InternalWarRaidRecord.Deserialize(ReplaceField(valid, index, flag)) == null,
                    "Malformed raid flag '" + flag + "' was accepted at field " + index + ".");
        }

        foreach (string day in new[] { string.Empty, "-1", "-2147483648", "2147483648", "day", "1.5" })
            Assert(InternalWarRaidRecord.Deserialize(ReplaceField(valid, 6, day)) == null,
                "Malformed/negative raid day was accepted: " + day + ".");
    }

    private static string ReplaceField(string payload, int index, string replacement)
    {
        string[] fields = payload.Split('|');
        fields[index] = replacement;
        return string.Join("|", fields);
    }

    private static void Assert(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException("Raid record: " + message);
    }
}
