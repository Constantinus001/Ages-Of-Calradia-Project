using System;
using System.Collections.Generic;
using System.Linq;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Session-local correlation only. Reloads deliberately begin with unknown history.
    internal sealed class PeaceEventHistory
    {
        private readonly Dictionary<Tuple<string, string>, WarObservation> _wars =
            new Dictionary<Tuple<string, string>, WarObservation>();
        private readonly int _capacity;
        private long _sequence;

        internal PeaceEventHistory(int capacity = 4096) { _capacity = Math.Max(1, capacity); }

        internal void Reset() { _wars.Clear(); _sequence = 0; }

        internal long RecordWar(string first, string second, double day, double monotonicSeconds, string reason)
        {
            Tuple<string, string> pair = Pair(first, second);
            if (pair == null || !Finite(day) || !Finite(monotonicSeconds)) return 0;
            if (!_wars.ContainsKey(pair) && _wars.Count >= _capacity)
                _wars.Remove(_wars.OrderBy(entry => entry.Value.Sequence).First().Key);
            var war = new WarObservation { Sequence = ++_sequence, Day = day, Seconds = monotonicSeconds, Reason = reason };
            _wars[pair] = war;
            return war.Sequence;
        }

        internal PeaceCorrelation RecordPeace(string first, string second, double day, double monotonicSeconds)
        {
            var result = new PeaceCorrelation();
            Tuple<string, string> pair = Pair(first, second);
            WarObservation war;
            if (pair == null || !_wars.TryGetValue(pair, out war)) return result;
            result.WarSequence = war.Sequence;
            result.WarReason = war.Reason;
            result.DuplicatePeace = war.PeaceSeen;
            result.ElapsedDays = day - war.Day;
            result.ElapsedSeconds = monotonicSeconds - war.Seconds;
            result.TimingKnown = Finite(result.ElapsedDays) && Finite(result.ElapsedSeconds)
                && result.ElapsedDays >= 0 && result.ElapsedSeconds >= 0;
            // Label is a diagnostic threshold, not a rule or a bug verdict.
            result.RapidReversal = result.TimingKnown && !war.PeaceSeen
                && result.ElapsedSeconds <= 1d && result.ElapsedDays <= 1d / 24d;
            war.PeaceSeen = true;
            return result;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static Tuple<string, string> Pair(string first, string second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || first == second) return null;
            return string.CompareOrdinal(first, second) <= 0 ? Tuple.Create(first, second) : Tuple.Create(second, first);
        }
        private sealed class WarObservation
        {
            internal long Sequence;
            internal double Day;
            internal double Seconds;
            internal string Reason;
            internal bool PeaceSeen;
        }
    }

    internal sealed class PeaceCorrelation
    {
        internal long WarSequence;
        internal string WarReason;
        internal bool TimingKnown;
        internal double ElapsedDays;
        internal double ElapsedSeconds;
        internal bool DuplicatePeace;
        internal bool RapidReversal;
    }
}
