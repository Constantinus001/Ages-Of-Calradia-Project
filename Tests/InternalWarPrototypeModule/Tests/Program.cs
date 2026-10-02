using System;
using AgesOfCalradiaInternalWarsTest;

internal static class Program
{
    private static int _assertions;
    private static int _suites;
    private static int _failures;

    private static int Main()
    {
        Run("escaped operation round trip", RoundTripEscapedIdentifiers);
        Run("malformed operation payloads", RejectMalformedPayloads);
        Run("legacy pending migration", VerifyLegacyMigration);
        Run("operational states", VerifyOperationalStates);
        Run("capture survives abort and retry", VerifyCaptureSurvivesAbort);
        Run("completion accepts terminal outcomes only", VerifyCompletionOutcomeValidation);
        Run("cleanup controls operation completion", VerifyCleanupCompletionGate);
        Run("continuing conflict counts each operation once", VerifyContinuingConflict);
        Run("peace between operations closes once", VerifyPeaceBetweenOperations);
        Run("peace waits for active cleanup", VerifyPeaceWaitsForCleanup);
        Run("legacy capture migration", VerifyLegacyCaptureMigration);
        Run("conflict round trip", VerifyConflictRoundTrip);
        Run("settlement claim goals", VerifySettlementClaimGoals);
        Run("negotiated peace policy", VerifyNegotiatedPeace);
        Run("compensation persistence and migration", VerifyCompensationPersistence);
        Run("immutable war history", VerifyWarHistory);
        Run("multiple conflict registry", () => { _assertions += ConflictRegistryTests.Run(); });
        Run("legacy conflict goal migration", VerifyLegacyConflictGoalMigration);
        Run("declaration before first operation", VerifyDeclarationWithoutOperation);
        Run("malformed conflict payloads", RejectMalformedConflictPayloads);
        Run("clean captured outcome consistency", RejectContradictoryCleanCapture);
        Run("raid serialization and validation", () => { _assertions += RaidRecordTests.Run(); });
        Run("sally outcomes never transfer ownership", VerifySallyOutcomes);
        Console.WriteLine("Internal-war lifecycle/save checks: " + _suites + " suites, " + _assertions
            + " assertions, " + _failures + " failures. Pure records only; native cleanup is not exercised.");
        return _failures == 0 ? 0 : 1;
    }

    private static void VerifySallyOutcomes()
    {
        InternalWarTestRecord record = NewOperation("sortie");
        record.State = InternalWarTestState.Assaulting;
        record.CaptureApplied = false; record.CleanupComplete = false; record.PendingFinalState = InternalWarTestState.Lifted;
        Assert(record.ApplyNonCapturingSiegeResult(true) && record.State == InternalWarTestState.Preparing && !record.CaptureApplied,
            "Besieger victory in a sally did not resume the siege without capture.");
        Assert(record.ApplyNonCapturingSiegeResult(false) && record.State == InternalWarTestState.RoyalPeacePending
            && record.PendingFinalState == InternalWarTestState.Defeated && !record.CaptureApplied,
            "Owner victory in a sally did not queue operation defeat without capture.");
        Assert(!record.ApplyNonCapturingSiegeResult(true) && record.PendingFinalState == InternalWarTestState.Defeated,
            "Repeated sally result overwrote pending cleanup.");
        foreach (InternalWarTestState state in new[] { InternalWarTestState.Marching, InternalWarTestState.Captured,
            InternalWarTestState.Lifted, InternalWarTestState.RoyalPeacePending })
        {
            record = NewOperation("late_sortie"); record.State = state;
            record.CaptureApplied = false; record.CleanupComplete = false;
            Assert(!record.ApplyNonCapturingSiegeResult(false) && record.State == state, "Late sally result changed a non-battling operation.");
        }
        record = NewOperation("committed_capture"); record.State = InternalWarTestState.Assaulting; record.CaptureApplied = true;
        Assert(!record.ApplyNonCapturingSiegeResult(false) && record.CaptureApplied, "Sally result erased a committed capture.");
        record = NewOperation("relief_defeat"); record.CaptureApplied = false; record.CleanupComplete = false;
        record.State = InternalWarTestState.Preparing;
        InternalConflictRecord war = NewConflict(record.ConflictId);
        Assert(record.ApplyNonCapturingSiegeResult(false), "Relief defeat was not queued.");
        record.State = InternalWarTestState.Defeated; record.CleanupComplete = true;
        war.CompleteOperation(record);
        Assert(war.Phase == InternalConflictPhase.Active && war.CompletedOperations == 1 && !war.GoalSatisfied,
            "Completing a noncapturing defeat ended the war or achieved a settlement claim.");
    }

    private static void RoundTripEscapedIdentifiers()
    {
        InternalWarTestRecord original = new InternalWarTestRecord
        {
            ConflictId = "war|one/%",
            KingdomId = "kingdom ü",
            AttackerClanId = "player|clan",
            DefenderClanId = "defender/clan",
            SettlementId = "town%one",
            LeaderPartyId = "main party",
            State = InternalWarTestState.Assaulting,
            StartDay = 123,
            CaptureApplied = true,
            PendingFinalState = InternalWarTestState.Captured
        };
        InternalWarTestRecord restored = InternalWarTestRecord.Deserialize(original.Serialize());
        Assert(restored != null, "Round-trip payload was rejected.");
        Assert(restored.ConflictId == original.ConflictId && restored.KingdomId == original.KingdomId,
            "Escaped record identifiers did not round-trip.");
        Assert(restored.AttackerClanId == original.AttackerClanId && restored.DefenderClanId == original.DefenderClanId,
            "Clan identifiers did not round-trip.");
        Assert(restored.SettlementId == original.SettlementId && restored.LeaderPartyId == original.LeaderPartyId,
            "Settlement or party identifiers did not round-trip.");
        Assert(restored.State == original.State && restored.StartDay == 123 && restored.CaptureApplied
            && restored.PendingFinalState == InternalWarTestState.Captured && !restored.CleanupComplete,
            "Record state did not round-trip.");
    }

    private static void RejectMalformedPayloads()
    {
        string[] invalid =
        {
            null, string.Empty, "v2|wrong", "v1|too|short",
            "v1|a|k|c1|c2|s|p|999|1|0",
            "v1|a|k|c1|c2|s|p|Marching|-1|0",
            "v1|a|k|c1|c2|s|p|Marching|day|0",
            "v2|a|k|c1|c2|s|p|Marching|1|0|Assaulting",
            "v2|a|k|c1|c2|s|p|Marching|1|0|999"
        };
        foreach (string payload in invalid)
            Assert(InternalWarTestRecord.Deserialize(payload) == null, "Malformed payload was accepted: " + payload);

        string valid = NewOperation("op1").Serialize();
        for (int index = 1; index <= 6; index++)
        {
            Assert(InternalWarTestRecord.Deserialize(ReplaceField(valid, index, string.Empty)) == null,
                "Empty operation identity accepted at field " + index + ".");
            Assert(InternalWarTestRecord.Deserialize(ReplaceField(valid, index, "%20%09")) == null,
                "Encoded whitespace operation identity accepted at field " + index + ".");
        }
        string[] malformed =
        {
            ReplaceField(valid, 0, "v4"), valid + "|extra", ReplaceField(valid, 4, "attacker"),
            ReplaceField(valid, 7, "None"), ReplaceField(valid, 7, "999"), ReplaceField(valid, 8, "-1"),
            ReplaceField(valid, 8, "not_a_day"), ReplaceField(valid, 9, "true"), ReplaceField(valid, 9, "2"),
            ReplaceField(valid, 10, "Marching"), ReplaceField(valid, 10, "999"), ReplaceField(valid, 11, "true"),
            ReplaceField(valid, 11, "2"), ReplaceField(valid, 7, "Assaulting"), ReplaceField(valid, 9, "0"),
            ReplaceField(valid, 10, "Lifted")
        };
        foreach (string payload in malformed)
            Assert(InternalWarTestRecord.Deserialize(payload) == null, "Malformed v3 payload accepted: " + payload);
    }

    private static void VerifyLegacyMigration()
    {
        InternalWarTestRecord record = InternalWarTestRecord.Deserialize("v1|a|k|c1|c2|s|p|RoyalPeacePending|4|0");
        Assert(record != null && record.PendingFinalState == InternalWarTestState.Lifted,
            "Legacy v1 pending cleanup did not migrate to Lifted.");
    }

    private static void VerifyOperationalStates()
    {
        foreach (InternalWarTestState state in (InternalWarTestState[])Enum.GetValues(typeof(InternalWarTestState)))
        {
            InternalWarTestRecord record = new InternalWarTestRecord { State = state };
            bool expected = state == InternalWarTestState.Marching || state == InternalWarTestState.Preparing
                || state == InternalWarTestState.Assaulting || state == InternalWarTestState.RoyalPeacePending;
            Assert(record.IsOperational == expected, "Operational-state mismatch for " + state + ".");
        }
    }

    private static InternalWarTestRecord NewOperation(string id)
    {
        return new InternalWarTestRecord
        {
            ConflictId = id, KingdomId = "kingdom", AttackerClanId = "attacker", DefenderClanId = "defender",
            SettlementId = "castle", LeaderPartyId = "main_party", State = InternalWarTestState.Captured,
            StartDay = 12, CaptureApplied = true, PendingFinalState = InternalWarTestState.Captured, CleanupComplete = true
        };
    }

    private static InternalConflictRecord NewConflict(string activeOperationId)
    {
        return new InternalConflictRecord
        {
            Id = "political_conflict", KingdomId = "kingdom", AttackerClanId = "attacker", DefenderClanId = "defender",
            Phase = InternalConflictPhase.Active, StartDay = 12, ActiveOperationId = activeOperationId
        };
    }

    private static void VerifyCaptureSurvivesAbort()
    {
        InternalWarTestRecord operation = NewOperation("op1");
        operation.RequestCompletion(InternalWarTestState.Lifted);
        Assert(operation.CaptureApplied && operation.PendingFinalState == InternalWarTestState.Captured,
            "Emergency cleanup replaced an already-committed capture with Lifted.");
        Assert(operation.State == InternalWarTestState.RoyalPeacePending && operation.IsOperational && !operation.CleanupComplete,
            "Requesting cleanup claimed completion or disabled operational recovery.");
        operation.RequestCompletion(InternalWarTestState.Failed);
        Assert(operation.CaptureApplied && operation.PendingFinalState == InternalWarTestState.Captured && !operation.CleanupComplete,
            "A failed cleanup retry erased the committed capture outcome.");
        InternalWarTestRecord restored = InternalWarTestRecord.Deserialize(operation.Serialize());
        Assert(restored != null && restored.CaptureApplied && restored.PendingFinalState == InternalWarTestState.Captured
            && restored.State == InternalWarTestState.RoyalPeacePending && !restored.CleanupComplete,
            "Pending captured cleanup did not survive serialization.");
    }

    private static void VerifyCompletionOutcomeValidation()
    {
        foreach (InternalWarTestState outcome in (InternalWarTestState[])Enum.GetValues(typeof(InternalWarTestState)))
        {
            InternalWarTestRecord operation = NewOperation("op1");
            operation.CaptureApplied = false;
            operation.State = InternalWarTestState.Preparing;
            operation.CleanupComplete = false;
            string before = operation.Serialize();
            bool terminal = outcome == InternalWarTestState.Captured || outcome == InternalWarTestState.Lifted
                || outcome == InternalWarTestState.Defeated || outcome == InternalWarTestState.TimedOut
                || outcome == InternalWarTestState.Failed || outcome == InternalWarTestState.Invalidated;
            if (terminal)
            {
                operation.RequestCompletion(outcome);
                Assert(operation.PendingFinalState == outcome && operation.State == InternalWarTestState.RoyalPeacePending
                    && !operation.CleanupComplete, "Valid terminal request did not enter pending cleanup: " + outcome + ".");
            }
            else
            {
                ExpectException<ArgumentOutOfRangeException>(() => operation.RequestCompletion(outcome),
                    "Nonterminal completion request accepted: " + outcome + ".");
                Assert(operation.Serialize() == before, "Rejected completion request mutated its record.");
            }
        }
        InternalWarTestRecord unknown = NewOperation("op1");
        ExpectException<ArgumentOutOfRangeException>(() => unknown.RequestCompletion((InternalWarTestState)999),
            "Undefined completion outcome was accepted.");
    }

    private static void VerifyCleanupCompletionGate()
    {
        InternalConflictRecord conflict = NewConflict("op1");
        InternalWarTestRecord operation = NewOperation("op1");
        operation.CleanupComplete = false;
        ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(operation),
            "Unverified native cleanup was counted as complete.");
        Assert(conflict.CompletedOperations == 0 && conflict.Phase == InternalConflictPhase.Active,
            "Rejected cleanup completion changed the conflict.");
        operation.CleanupComplete = true;
        conflict.CompleteOperation(operation);
        Assert(conflict.CompletedOperations == 1 && conflict.LastCompletedOperationId == "op1" && conflict.IsOpen,
            "Verified operation completion incorrectly closed its continuing political conflict.");

        foreach (InternalWarTestState state in new[] { InternalWarTestState.None, InternalWarTestState.Marching,
            InternalWarTestState.Preparing, InternalWarTestState.Assaulting, InternalWarTestState.RoyalPeacePending })
        {
            operation.State = state;
            ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(operation),
                "Nonterminal operation with a forged cleanup bit was accepted: " + state + ".");
        }
        ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(null), "Null operation was accepted.");
        foreach (int field in new[] { 2, 3, 4 })
        {
            InternalWarTestRecord foreign = InternalWarTestRecord.Deserialize(ReplaceField(NewOperation("op1").Serialize(), field, "foreign"));
            ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(foreign),
                "Operation with mismatched kingdom/clan field " + field + " was accepted.");
        }
        Assert(conflict.CompletedOperations == 1, "Rejected operations changed completed-operation count.");
    }

    private static void VerifyContinuingConflict()
    {
        InternalConflictRecord conflict = NewConflict("operation_one");
        InternalWarTestRecord first = NewOperation("operation_one");
        conflict.CompleteOperation(first);
        conflict.CompleteOperation(first);
        Assert(conflict.CompletedOperations == 1 && conflict.Phase == InternalConflictPhase.Active,
            "Duplicate completion was counted or ended the continuing conflict.");
        conflict = InternalConflictRecord.Deserialize(conflict.Serialize());
        first = InternalWarTestRecord.Deserialize(first.Serialize());
        Assert(conflict != null && first != null, "Completed operation/conflict could not reload.");
        conflict.CompleteOperation(first);
        Assert(conflict.CompletedOperations == 1, "Duplicate callback after reload was counted again.");

        // Distinct fixture IDs verify accounting; production GUID generation is outside this pure-record test.
        InternalWarTestRecord second = NewOperation("operation_two");
        conflict.ActiveOperationId = second.ConflictId;
        Assert(first.ConflictId != second.ConflictId && conflict.Matches(first) && conflict.Matches(second),
            "Two operations of one political conflict did not have distinct test identities.");
        conflict.CompleteOperation(second);
        conflict.CompleteOperation(second);
        Assert(conflict.CompletedOperations == 2 && conflict.LastCompletedOperationId == second.ConflictId && conflict.IsOpen,
            "Second operation was lost, double-counted, or prematurely closed the conflict.");
        ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(first),
            "Stale first-operation completion was accepted after the second operation became active.");
        Assert(conflict.CompletedOperations == 2, "Stale callback altered operation count.");
    }

    private static void VerifyPeaceBetweenOperations()
    {
        InternalConflictRecord conflict = NewConflict("op1");
        InternalWarTestRecord operation = NewOperation("op1");
        conflict.CompleteOperation(operation);
        conflict.RequestPeace();
        Assert(conflict.Phase == InternalConflictPhase.PeacePending && conflict.IsOpen,
            "Peace did not retain pending recovery until clean operation confirmation.");
        conflict.CompleteOperation(operation);
        Assert(conflict.Phase == InternalConflictPhase.Closed && !conflict.IsOpen && conflict.CompletedOperations == 1,
            "Peace between operations did not close without counting the last operation twice.");
        conflict.RequestPeace();
        conflict.CompleteOperation(operation);
        Assert(conflict.Phase == InternalConflictPhase.Closed && conflict.CompletedOperations == 1,
            "Repeated peace reopened a closed conflict or double-counted cleanup.");
    }

    private static void VerifyPeaceWaitsForCleanup()
    {
        InternalConflictRecord conflict = NewConflict("op1");
        InternalWarTestRecord operation = NewOperation("op1");
        operation.RequestCompletion(InternalWarTestState.Lifted);
        conflict.RequestPeace();
        ExpectException<InvalidOperationException>(() => conflict.CompleteOperation(operation),
            "Peace finalized while operation cleanup was still pending.");
        Assert(conflict.Phase == InternalConflictPhase.PeacePending && conflict.CompletedOperations == 0,
            "Rejected pending cleanup lost the political peace request.");
        operation.State = operation.PendingFinalState;
        operation.CleanupComplete = true;
        conflict.CompleteOperation(operation);
        Assert(conflict.Phase == InternalConflictPhase.Closed && conflict.CompletedOperations == 1
            && operation.State == InternalWarTestState.Captured && operation.CaptureApplied,
            "Completed peace erased capture or failed to close.");
    }

    private static void VerifyLegacyCaptureMigration()
    {
        string[] payloads =
        {
            "v1|op1|kingdom|attacker|defender|castle|main_party|Captured|12|1",
            "v1|op1|kingdom|attacker|defender|castle|main_party|Lifted|12|1",
            "v2|op1|kingdom|attacker|defender|castle|main_party|RoyalPeacePending|12|1|Lifted",
            "v2|op1|kingdom|attacker|defender|castle|main_party|Lifted|12|1|Lifted"
        };
        foreach (string payload in payloads)
        {
            InternalWarTestRecord operation = InternalWarTestRecord.Deserialize(payload);
            Assert(operation != null && operation.CaptureApplied && operation.PendingFinalState == InternalWarTestState.Captured,
                "Legacy capture+abort lost the conquest fact: " + payload);
            Assert(!operation.CleanupComplete, "Legacy record claimed newly verified cleanup: " + payload);
            operation.RequestCompletion(InternalWarTestState.Lifted);
            InternalWarTestRecord restored = InternalWarTestRecord.Deserialize(operation.Serialize());
            Assert(restored != null && restored.CaptureApplied && restored.PendingFinalState == InternalWarTestState.Captured
                && !restored.CleanupComplete && restored.State == InternalWarTestState.RoyalPeacePending,
                "Legacy capture did not survive v3 recovery round trip.");
        }
    }

    private static void VerifyConflictRoundTrip()
    {
        InternalConflictRecord original = new InternalConflictRecord
        {
            Id = "conflict|one/%", KingdomId = "kingdom ü", AttackerClanId = "attacker|clan", DefenderClanId = "defender/clan",
            Phase = InternalConflictPhase.PeacePending, StartDay = 123, CompletedOperations = 2,
            LastCompletedOperationId = "operation|previous/%", ActiveOperationId = "operation|active/%",
            Goal = InternalWarGoal.SettlementClaim, GoalSettlementId = "castle|claim/%", GoalSatisfied = true
        };
        InternalConflictRecord restored = InternalConflictRecord.Deserialize(original.Serialize());
        Assert(restored != null && restored.Id == original.Id && restored.KingdomId == original.KingdomId
            && restored.AttackerClanId == original.AttackerClanId && restored.DefenderClanId == original.DefenderClanId,
            "Political conflict identities did not round-trip.");
        Assert(restored.Phase == original.Phase && restored.StartDay == original.StartDay
            && restored.CompletedOperations == original.CompletedOperations
            && restored.LastCompletedOperationId == original.LastCompletedOperationId
            && restored.ActiveOperationId == original.ActiveOperationId && restored.Goal == original.Goal
            && restored.GoalSettlementId == original.GoalSettlementId && restored.GoalSatisfied && restored.IsOpen,
            "Political phase, operation history, active identity, or goal did not round-trip.");
    }

    private static void VerifyWarHistory()
    {
        var history = new InternalWarHistory();
        InternalConflictRecord conflict = NewConflict(string.Empty);
        ExpectException<InvalidOperationException>(() => history.Archive(conflict), "Open conflict archived.");
        conflict.Phase = InternalConflictPhase.Closed;
        history.Archive(conflict);
        history.Archive(conflict);
        Assert(history.Count == 1, "Archive callback duplicated a war.");
        string payload = history.Serialize();
        Assert(InternalWarHistory.Deserialize(payload).Serialize() == payload, "History failed round trip.");
        conflict.GoalSettlementId = "changed";
        Assert(history.Serialize() == payload, "Live record mutation altered history.");
        ExpectException<InvalidOperationException>(() => history.Archive(conflict), "Changed archived result silently overwritten.");
        Assert(InternalWarHistory.Deserialize(payload + "\n" + payload.Split('\n')[1]) == null, "Duplicate saved history accepted.");
        Assert(InternalWarHistory.Deserialize("history-v1\ninvalid") == null, "Corrupted history accepted.");
        Assert(InternalWarHistory.Deserialize(string.Empty).Count == 0, "Legacy campaign did not get empty history.");
    }

    private static void VerifyNegotiatedPeace()
    {
        InternalConflictRecord conflict = NewConflict(string.Empty);
        Assert(conflict.GetPeaceOfferBlocker("attacker", 25, false).Length > 0, "Early status quo accepted.");
        Assert(conflict.GetPeaceOfferBlocker("attacker", 26, false).Length == 0, "Fourteen-day status quo refused.");
        Assert(conflict.GetPeaceOfferBlocker("defender", 12, true).Length == 0, "Defender concession refused.");
        Assert(conflict.GetPeaceOfferBlocker("neutral", 26, true).Length > 0, "Neutral clan negotiated another war.");
        Assert(conflict.GetPeaceOfferBlocker("attacker", 11, true).Length > 0, "Backward date accepted.");
        string result;
        conflict.TryAssignSettlementClaim("castle", out result);
        conflict.MarkGoalSatisfied("castle", "attacker");
        Assert(conflict.GetPeaceOfferBlocker("attacker", 12, false).Length == 0, "Achieved claim peace refused.");
        string before = conflict.Serialize();
        conflict.GetPeaceOfferBlocker("attacker", 26, false);
        Assert(before == conflict.Serialize(), "Peace preview mutated campaign state.");
        conflict.RequestPeace();
        Assert(conflict.GetPeaceOfferBlocker("attacker", 26, true).Length > 0, "Duplicate pending offer accepted.");
        conflict.Phase = InternalConflictPhase.Closed;
        Assert(conflict.GetPeaceOfferBlocker("defender", 26, true).Length > 0, "Closed war accepted offer.");
    }

    private static void VerifySettlementClaimGoals()
    {
        InternalConflictRecord conflict = NewConflict(string.Empty);
        string result;
        Assert(conflict.TryAssignSettlementClaim("vostrum", out result) && string.IsNullOrEmpty(result),
            "A valid first settlement claim was rejected.");
        Assert(conflict.Goal == InternalWarGoal.SettlementClaim && conflict.GoalSettlementId == "vostrum"
            && !conflict.GoalSatisfied && conflict.HasUnresolvedSettlementClaim,
            "A first settlement claim did not become the active war goal.");
        string before = conflict.Serialize();
        Assert(!conflict.TryAssignSettlementClaim("jaculan", out result) && conflict.Serialize() == before,
            "A different unresolved claim replaced the active war goal.");
        Assert(conflict.CanTargetSettlement("defender", "attacker", "jaculan"), "Defender counterattack was blocked by attacker claim.");
        Assert(!conflict.CanTargetSettlement("attacker", "defender", "jaculan"), "Attacker bypassed unresolved claim.");
        Assert(conflict.CanTargetSettlement("attacker", "defender", "vostrum"), "Claimant cannot target its claim.");
        Assert(!conflict.CanTargetSettlement("neutral", "attacker", "jaculan"), "Neutral clan gained target authority.");
        Assert(!conflict.MarkGoalSatisfied("vostrum", "defender") && !conflict.GoalSatisfied,
            "Defender capture satisfied attacker claim.");
        Assert(!conflict.MarkGoalSatisfied("jaculan", "attacker") && !conflict.GoalSatisfied,
            "An unrelated settlement satisfied the claim.");
        Assert(conflict.MarkGoalSatisfied("vostrum", "attacker") && conflict.GoalSatisfied && !conflict.HasUnresolvedSettlementClaim,
            "Capturing the claimed settlement did not satisfy the goal.");
        Assert(conflict.TryAssignSettlementClaim("jaculan", out result) && conflict.GoalSettlementId == "jaculan"
            && !conflict.GoalSatisfied, "A completed claim did not permit a later distinct claim.");
        conflict.RequestPeace();
        Assert(!conflict.CanTargetSettlement("defender", "attacker", "jaculan"), "Peace allowed a new counterattack.");
        before = conflict.Serialize();
        Assert(!conflict.TryAssignSettlementClaim("poros", out result) && conflict.Serialize() == before,
            "Peace-pending conflict allowed its settlement claim to change.");
    }

    private static void VerifyLegacyConflictGoalMigration()
    {
        InternalConflictRecord legacy = InternalConflictRecord.Deserialize("v1|conflict|kingdom|attacker|defender|Active|12|0||");
        Assert(legacy != null && legacy.Goal == InternalWarGoal.Feud && legacy.GoalSettlementId == string.Empty
            && !legacy.GoalSatisfied, "Legacy conflict did not migrate to the safe no-claim goal.");
        InternalConflictRecord roundTrip = InternalConflictRecord.Deserialize(legacy.Serialize());
        Assert(roundTrip != null && roundTrip.Goal == InternalWarGoal.Feud && !roundTrip.HasUnresolvedSettlementClaim,
            "Migrated legacy no-claim goal did not survive the v2 round trip.");
    }

    private static void VerifyCompensationPersistence()
    {
        foreach (string payer in new[] { "attacker", "defender" })
        {
            InternalConflictRecord paid = NewConflict(string.Empty);
            paid.RequestPeace();
            paid.CompensationGold = 5000;
            paid.CompensationPayerId = payer;
            InternalConflictRecord restored = InternalConflictRecord.Deserialize(paid.Serialize());
            Assert(restored != null && restored.CompensationGold == 5000 && restored.CompensationPayerId == payer
                && restored.Phase == InternalConflictPhase.PeacePending, "Paid compensation lost its amount, payer, or pending peace.");
            Assert(restored.Serialize() == paid.Serialize(), "Paid compensation round trip was not stable.");
        }
        InternalConflictRecord record = NewConflict(string.Empty);
        record.CompensationGold = 5000;
        record.CompensationPayerId = "attacker";
        string valid = record.Serialize();
        foreach (string amount in new[] { "-1", "gold", "5000.0", "2147483648", "", "+5000", " 5000" })
            Assert(InternalConflictRecord.Deserialize(ReplaceField(valid, 13, amount)) == null,
                "Malformed compensation amount accepted: " + amount);
        Assert(InternalConflictRecord.Deserialize(ReplaceField(valid, 13, "0")) == null,
            "Unpaid compensation retained an orphan payer.");
        foreach (string payer in new[] { "", "%20%09", "foreign_clan" })
            Assert(InternalConflictRecord.Deserialize(ReplaceField(valid, 14, payer)) == null,
                "Paid compensation accepted missing or foreign payer: " + payer);
        InternalConflictRecord legacy = InternalConflictRecord.Deserialize(
            "v2|conflict|kingdom|attacker|defender|Active|12|0|||SettlementClaim|vostrum|1");
        Assert(legacy != null && legacy.CompensationGold == 0 && legacy.CompensationPayerId == string.Empty,
            "Legacy v2 incorrectly inferred compensation payment.");
        Assert(legacy.GoalSettlementId == "vostrum" && legacy.GoalSatisfied,
            "Compensation migration changed the legacy territorial claim.");
        InternalConflictRecord migrated = InternalConflictRecord.Deserialize(legacy.Serialize());
        Assert(migrated != null && migrated.CompensationGold == 0 && migrated.CompensationPayerId == string.Empty,
            "Migrated v2 zero payment did not survive new schema round trip.");
    }

    private static void RejectMalformedConflictPayloads()
    {
        string valid = NewConflict("op1").Serialize();
        string[] malformed =
        {
            null, string.Empty, "v1|short", valid + "|extra", ReplaceField(valid, 0, "v4"),
            ReplaceField(valid, 4, "attacker"), ReplaceField(valid, 5, "999"), ReplaceField(valid, 5, "Missing"),
            ReplaceField(valid, 6, "-1"), ReplaceField(valid, 6, "day"), ReplaceField(valid, 7, "-1"),
            ReplaceField(valid, 7, "count"), ReplaceField(valid, 7, "1"), ReplaceField(valid, 8, "unexpected_history"),
            ReplaceField(valid, 9, "%20%09"), ReplaceField(valid, 10, "Unknown"),
            ReplaceField(valid, 11, "%20%09"), ReplaceField(valid, 12, "2"),
            ReplaceField(valid, 10, "SettlementClaim")
        };
        foreach (string payload in malformed)
            Assert(InternalConflictRecord.Deserialize(payload) == null, "Malformed political conflict payload accepted: " + payload);
        for (int index = 1; index <= 4; index++)
        {
            Assert(InternalConflictRecord.Deserialize(ReplaceField(valid, index, string.Empty)) == null,
                "Empty political identity accepted at field " + index + ".");
            Assert(InternalConflictRecord.Deserialize(ReplaceField(valid, index, "%20%09")) == null,
                "Encoded blank political identity accepted at field " + index + ".");
        }
        InternalConflictRecord completed = NewConflict("op1");
        completed.CompleteOperation(NewOperation("op1"));
        Assert(InternalConflictRecord.Deserialize(ReplaceField(completed.Serialize(), 8, "%20%09")) == null,
            "Encoded blank completed-operation identity was accepted.");
        Assert(InternalConflictRecord.Deserialize(ReplaceField(completed.Serialize(), 9, string.Empty)) == null,
            "A conflict with completed operations accepted an empty active operation identity.");
    }

    private static void VerifyDeclarationWithoutOperation()
    {
        InternalConflictRecord declared = NewConflict(string.Empty);
        InternalConflictRecord restored = InternalConflictRecord.Deserialize(declared.Serialize());
        Assert(restored != null && restored.Phase == InternalConflictPhase.Active && restored.IsOpen
            && restored.ActiveOperationId == string.Empty && restored.LastCompletedOperationId == string.Empty
            && restored.CompletedOperations == 0,
            "A valid declaration before the first siege could not round-trip with an explicit empty operation ID.");
        restored.RequestPeace();
        Assert(restored.Phase == InternalConflictPhase.PeacePending && restored.IsOpen && restored.CompletedOperations == 0,
            "Peace before the first operation did not enter pending coordination.");
        InternalConflictRecord pending = InternalConflictRecord.Deserialize(restored.Serialize());
        Assert(pending != null && pending.Phase == InternalConflictPhase.PeacePending && pending.ActiveOperationId == string.Empty,
            "Pending peace with no operation did not survive reload.");
        ExpectException<InvalidOperationException>(() => pending.CompleteOperation(null),
            "A nonexistent operation was counted to close an idle declaration.");
        Assert(pending.Phase == InternalConflictPhase.PeacePending && pending.CompletedOperations == 0,
            "Record-only logic closed the no-operation conflict; the coordinator must own that native cleanup decision.");
    }

    private static void RejectContradictoryCleanCapture()
    {
        string valid = NewOperation("op1").Serialize();
        Assert(InternalWarTestRecord.Deserialize(ReplaceField(valid, 7, "Lifted")) == null,
            "A v3 record claimed completed cleanup as Lifted despite a committed Captured outcome.");
    }

    private static string ReplaceField(string payload, int index, string replacement)
    {
        string[] fields = payload.Split('|');
        fields[index] = replacement;
        return string.Join("|", fields);
    }

    private static void ExpectException<T>(Action action, string message) where T : Exception
    {
        _assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Run(string name, Action action)
    {
        _suites++;
        try { action(); }
        catch (Exception exception)
        {
            _failures++;
            Console.Error.WriteLine(name + ": " + exception.Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
}
