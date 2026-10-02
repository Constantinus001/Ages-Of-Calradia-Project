using System;

namespace AgesOfCalradiaInternalWarsTest
{
    internal enum InternalWarTestState
    {
        None,
        Marching,
        Preparing,
        Assaulting,
        RoyalPeacePending,
        Captured,
        Lifted,
        Defeated,
        TimedOut,
        Failed,
        Invalidated
    }

    internal sealed class InternalWarTestRecord
    {
        internal string ConflictId = string.Empty;
        internal string KingdomId = string.Empty;
        internal string AttackerClanId = string.Empty;
        internal string DefenderClanId = string.Empty;
        internal string SettlementId = string.Empty;
        internal string LeaderPartyId = string.Empty;
        internal InternalWarTestState State;
        internal int StartDay;
        internal bool CaptureApplied;
        internal InternalWarTestState PendingFinalState = InternalWarTestState.Lifted;
        internal bool CleanupComplete;

        // A committed capture is an outcome, not an abort/cleanup reason.
        internal void RequestCompletion(InternalWarTestState outcome)
        {
            if (!IsTerminal(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
            PendingFinalState = CaptureApplied ? InternalWarTestState.Captured : outcome;
            State = InternalWarTestState.RoyalPeacePending;
            CleanupComplete = false;
        }

        internal bool IsOperational
        {
            get
            {
                return State == InternalWarTestState.Marching || State == InternalWarTestState.Preparing
                    || State == InternalWarTestState.Assaulting || State == InternalWarTestState.RoyalPeacePending;
            }
        }

        // Sally and canonical relief callbacks report besieger victory. A successful defense
        // lifts this operation only; it never captures a fief or ends diplomacy.
        internal bool ApplyNonCapturingSiegeResult(bool besiegerWon)
        {
            if (!IsOperational || State == InternalWarTestState.Marching
                || State == InternalWarTestState.RoyalPeacePending || CaptureApplied) return false;
            if (besiegerWon) State = InternalWarTestState.Preparing;
            else RequestCompletion(InternalWarTestState.Defeated);
            return true;
        }

        internal string Serialize()
        {
            return string.Join("|", new[]
            {
                "v3", Escape(ConflictId), Escape(KingdomId), Escape(AttackerClanId), Escape(DefenderClanId),
                Escape(SettlementId), Escape(LeaderPartyId), State.ToString(), StartDay.ToString(), CaptureApplied ? "1" : "0",
                PendingFinalState.ToString(), CleanupComplete ? "1" : "0"
            });
        }

        internal static InternalWarTestRecord Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string[] parts = payload.Split('|');
            bool legacy = parts.Length == 10 && parts[0] == "v1";
            bool v2 = parts.Length == 11 && parts[0] == "v2";
            bool v3 = parts.Length == 12 && parts[0] == "v3";
            if (!legacy && !v2 && !v3) return null;
            if (parts[9] != "0" && parts[9] != "1") return null;
            if (v3 && parts[11] != "0" && parts[11] != "1") return null;
            for (int index = 1; index <= 6; index++)
                if (string.IsNullOrWhiteSpace(Unescape(parts[index]))) return null;
            if (Unescape(parts[3]) == Unescape(parts[4])) return null;
            InternalWarTestState state;
            InternalWarTestState pendingFinalState = InternalWarTestState.Lifted;
            int startDay;
            if (!Enum.TryParse(parts[7], out state) || !Enum.IsDefined(typeof(InternalWarTestState), state)
                || state == InternalWarTestState.None || !int.TryParse(parts[8], out startDay) || startDay < 0) return null;
            if (!legacy && (!Enum.TryParse(parts[10], out pendingFinalState)
                || !Enum.IsDefined(typeof(InternalWarTestState), pendingFinalState) || !IsTerminal(pendingFinalState))) return null;
            bool captured = parts[9] == "1";
            bool clean = v3 && parts[11] == "1";
            if (clean && !IsTerminal(state)) return null;
            if (clean && captured && state != InternalWarTestState.Captured) return null;
            if (v3 && ((state == InternalWarTestState.Captured && !captured)
                || (captured && pendingFinalState != InternalWarTestState.Captured))) return null;
            // Legacy capture+abort records retain the conquest fact and require recovery inspection.
            if (captured) pendingFinalState = InternalWarTestState.Captured;
            return new InternalWarTestRecord
            {
                ConflictId = Unescape(parts[1]), KingdomId = Unescape(parts[2]), AttackerClanId = Unescape(parts[3]),
                DefenderClanId = Unescape(parts[4]), SettlementId = Unescape(parts[5]), LeaderPartyId = Unescape(parts[6]),
                State = state, StartDay = startDay, CaptureApplied = captured, PendingFinalState = pendingFinalState,
                CleanupComplete = clean
            };
        }

        internal static bool IsTerminal(InternalWarTestState state)
        {
            return state == InternalWarTestState.Captured || state == InternalWarTestState.Lifted
                || state == InternalWarTestState.Defeated || state == InternalWarTestState.TimedOut
                || state == InternalWarTestState.Failed || state == InternalWarTestState.Invalidated;
        }

        private static string Escape(string value) { return Uri.EscapeDataString(value ?? string.Empty); }
        private static string Unescape(string value)
        {
            try { return Uri.UnescapeDataString(value ?? string.Empty); }
            catch (UriFormatException) { return string.Empty; }
        }
    }
}
