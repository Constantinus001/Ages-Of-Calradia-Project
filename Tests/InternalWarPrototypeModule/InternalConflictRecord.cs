using System;
using System.Globalization;

namespace AgesOfCalradiaInternalWarsTest
{
    internal enum InternalConflictPhase { Active, PeacePending, Closed }
    internal enum InternalWarGoal { Feud, SettlementClaim }

    // Political lifetime is independent of the current siege operation. No native object references.
    internal sealed class InternalConflictRecord
    {
        internal string Id;
        internal string KingdomId;
        internal string AttackerClanId;
        internal string DefenderClanId;
        internal InternalConflictPhase Phase;
        internal int StartDay;
        internal string LastCompletedOperationId = string.Empty;
        internal int CompletedOperations;
        internal string ActiveOperationId = string.Empty;
        // A political feud may exist without a territorial claim. The first selected fortification
        // can become a claim; the claim constrains later siege selection until it is resolved.
        internal InternalWarGoal Goal = InternalWarGoal.Feud;
        internal string GoalSettlementId = string.Empty;
        internal bool GoalSatisfied;
        internal int CompensationGold;
        internal string CompensationPayerId = string.Empty;
        internal bool IsOpen { get { return Phase != InternalConflictPhase.Closed; } }
        internal string OpponentOf(string clanId)
        { return clanId == AttackerClanId ? DefenderClanId : clanId == DefenderClanId ? AttackerClanId : null; }
        internal bool HasUnresolvedSettlementClaim
        {
            get { return Goal == InternalWarGoal.SettlementClaim && !GoalSatisfied; }
        }

        internal bool TryAssignSettlementClaim(string settlementId, out string result)
        {
            result = string.Empty;
            if (Phase != InternalConflictPhase.Active)
            {
                result = "A settlement claim cannot be changed while peace or cleanup is pending.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(settlementId))
            {
                result = "The selected claim settlement is unavailable.";
                return false;
            }
            if (HasUnresolvedSettlementClaim && GoalSettlementId != settlementId)
            {
                result = "This war already has an unresolved claim on another fortification.";
                return false;
            }
            Goal = InternalWarGoal.SettlementClaim;
            GoalSettlementId = settlementId;
            GoalSatisfied = false;
            return true;
        }

        // The declaration's attacker owns this claim. Defenders may counterattack other fiefs.
        internal bool CanTargetSettlement(string attackingClanId, string ownerClanId, string settlementId)
        {
            if (Phase != InternalConflictPhase.Active || string.IsNullOrWhiteSpace(settlementId)) return false;
            if (attackingClanId == DefenderClanId && ownerClanId == AttackerClanId) return true;
            return attackingClanId == AttackerClanId && ownerClanId == DefenderClanId
                && (!HasUnresolvedSettlementClaim || GoalSettlementId == settlementId);
        }

        internal bool MarkGoalSatisfied(string settlementId, string capturingClanId)
        {
            if (Phase == InternalConflictPhase.Closed || capturingClanId != AttackerClanId
                || !HasUnresolvedSettlementClaim || GoalSettlementId != settlementId) return false;
            GoalSatisfied = true;
            return true;
        }

        internal string DescribeGoal()
        {
            return Goal == InternalWarGoal.SettlementClaim
                ? "Settlement claim: " + GoalSettlementId + (GoalSatisfied ? " (achieved)" : " (unresolved)")
                : "Feud: no territorial claim selected";
        }

        // Initial test balance: either side can concede; a status-quo offer is accepted
        // after fourteen campaign days or after the declared territorial goal is achieved.
        // Native peace is still performed by the campaign coordinator after battle cleanup.
        internal string GetPeaceOfferBlocker(string proposerClanId, int campaignDay, bool concede)
        {
            if (Phase != InternalConflictPhase.Active) return "This conflict is not accepting new peace offers.";
            if (proposerClanId != AttackerClanId && proposerClanId != DefenderClanId)
                return "Only a participating clan can negotiate this peace.";
            if (campaignDay < StartDay) return "The campaign date is earlier than the declaration.";
            if (concede || GoalSatisfied || (long)campaignDay - StartDay >= 14) return string.Empty;
            return "The rival refuses status-quo peace before fourteen days unless the declared claim is achieved. You may concede instead.";
        }

        internal void RequestPeace()
        {
            if (Phase != InternalConflictPhase.Closed) Phase = InternalConflictPhase.PeacePending;
        }

        internal bool Matches(InternalWarTestRecord operation)
        {
            return operation != null && operation.KingdomId == KingdomId
                && ((operation.AttackerClanId == AttackerClanId && operation.DefenderClanId == DefenderClanId)
                    || (operation.AttackerClanId == DefenderClanId && operation.DefenderClanId == AttackerClanId));
        }

        internal void CompleteOperation(InternalWarTestRecord operation)
        {
            if (!Matches(operation) || !operation.CleanupComplete || !InternalWarTestRecord.IsTerminal(operation.State)
                || string.IsNullOrEmpty(operation.ConflictId) || operation.ConflictId != ActiveOperationId)
                throw new InvalidOperationException("Only a verified clean operation of this conflict can complete.");
            if (LastCompletedOperationId != operation.ConflictId)
            {
                CompletedOperations++;
                LastCompletedOperationId = operation.ConflictId;
            }
            if (Phase == InternalConflictPhase.PeacePending) Phase = InternalConflictPhase.Closed;
        }

        internal string Serialize()
        {
            return string.Join("|", new[] { "v3", Uri.EscapeDataString(Id), Uri.EscapeDataString(KingdomId),
                Uri.EscapeDataString(AttackerClanId), Uri.EscapeDataString(DefenderClanId), Phase.ToString(),
                StartDay.ToString(CultureInfo.InvariantCulture), CompletedOperations.ToString(CultureInfo.InvariantCulture),
                Uri.EscapeDataString(LastCompletedOperationId), Uri.EscapeDataString(ActiveOperationId), Goal.ToString(),
                Uri.EscapeDataString(GoalSettlementId), GoalSatisfied ? "1" : "0",
                CompensationGold.ToString(CultureInfo.InvariantCulture), Uri.EscapeDataString(CompensationPayerId) });
        }

        internal static InternalConflictRecord Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string[] parts = payload.Split('|');
            bool legacy = parts.Length == 10 && parts[0] == "v1";
            bool claimsVersion = parts.Length == 13 && parts[0] == "v2";
            bool compensationVersion = parts.Length == 15 && parts[0] == "v3";
            if ((!legacy && !claimsVersion && !compensationVersion)
                || (parts[9].Length > 0 && string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[9])))) return null;
            InternalConflictPhase phase;
            int day, count;
            if (!Enum.TryParse(parts[5], out phase) || !Enum.IsDefined(typeof(InternalConflictPhase), phase)
                || !int.TryParse(parts[6], out day) || day < 0 || !int.TryParse(parts[7], out count) || count < 0) return null;
            for (int index = 1; index <= 4; index++)
                if (string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[index]))) return null;
            string attacker = Uri.UnescapeDataString(parts[3]);
            string defender = Uri.UnescapeDataString(parts[4]);
            if (attacker == defender || (count == 0) != string.IsNullOrEmpty(parts[8])) return null;
            if (count > 0 && string.IsNullOrWhiteSpace(Uri.UnescapeDataString(parts[8]))) return null;
            if (count > 0 && string.IsNullOrEmpty(parts[9])) return null;
            InternalWarGoal goal = InternalWarGoal.Feud;
            string goalSettlementId = string.Empty;
            bool goalSatisfied = false;
            int compensationGold = 0;
            string compensationPayerId = string.Empty;
            if (!legacy)
            {
                if (!Enum.TryParse(parts[10], out goal) || !Enum.IsDefined(typeof(InternalWarGoal), goal)
                    || (parts[12] != "0" && parts[12] != "1")) return null;
                goalSettlementId = Uri.UnescapeDataString(parts[11]);
                goalSatisfied = parts[12] == "1";
                if (goal == InternalWarGoal.Feud && (!string.IsNullOrEmpty(goalSettlementId) || goalSatisfied)) return null;
                if (goal == InternalWarGoal.SettlementClaim && string.IsNullOrWhiteSpace(goalSettlementId)) return null;
            }
            if (compensationVersion)
            {
                if (!int.TryParse(parts[13], NumberStyles.None, CultureInfo.InvariantCulture, out compensationGold) || compensationGold < 0) return null;
                compensationPayerId = Uri.UnescapeDataString(parts[14]);
                if ((compensationGold == 0 && compensationPayerId.Length != 0)
                    || (compensationGold > 0 && compensationPayerId != attacker && compensationPayerId != defender)) return null;
            }
            return new InternalConflictRecord { Id = Uri.UnescapeDataString(parts[1]), KingdomId = Uri.UnescapeDataString(parts[2]),
                AttackerClanId = attacker, DefenderClanId = defender, Phase = phase, StartDay = day,
                CompletedOperations = count, LastCompletedOperationId = Uri.UnescapeDataString(parts[8]),
                ActiveOperationId = Uri.UnescapeDataString(parts[9]), Goal = goal, GoalSettlementId = goalSettlementId,
                GoalSatisfied = goalSatisfied, CompensationGold = compensationGold, CompensationPayerId = compensationPayerId };
        }
    }
}
