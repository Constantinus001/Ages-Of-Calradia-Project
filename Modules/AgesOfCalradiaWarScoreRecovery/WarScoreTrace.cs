using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.WarScoreRecovery
{
    // Opt-in, in-memory acceptance evidence. It deliberately does not write
    // campaign data and keeps a bounded record to avoid normal log spam.
    internal static class WarScoreTrace
    {
        private const int MaximumEntries = 64;
        private static readonly List<string> Entries = new List<string>();
        private static readonly Dictionary<string, WarPair> Pairs = new Dictionary<string, WarPair>(StringComparer.Ordinal);
        private static bool _enabled;

        internal static string Start()
        {
            Entries.Clear(); Pairs.Clear(); _enabled = true;
            return "War-score trace armed. Reproduce one battle/capture, advance one campaign day, then use aoc.war_score_trace report.";
        }

        internal static string Stop()
        {
            _enabled = false;
            return Report() + " Trace stopped.";
        }

        internal static string Report()
        {
            if (Entries.Count == 0) return _enabled ? "War-score trace is armed; no scored event has been observed." : "War-score trace is off; no retained events.";
            return "War-score trace entries=" + Entries.Count + "; " + string.Join(" | ", Entries.ToArray());
        }

        internal static void RecordEvent(string kind, Kingdom winner, Kingdom loser, int before, int after, Settlement settlement)
        {
            if (!_enabled || winner == null || loser == null) return;
            Track(winner, loser);
            Add(kind + " " + winner.StringId + "->" + loser.StringId + " score=" + before + "->" + after + SettlementText(settlement));
        }

        internal static void RecordPeace(Kingdom first, Kingdom second, int before, int after, int occupationOutcome)
        {
            if (!_enabled || first == null || second == null) return;
            Track(first, second);
            Add("peace " + first.StringId + " vs " + second.StringId + " score=" + before + "->" + after + " occupation=" + occupationOutcome);
        }

        internal static void RecordSkipped(string eventName)
        {
            if (_enabled) Add(eventName + " skipped-by-earlier-Harmony-prefix; recovery made no change");
        }

        internal static void RecordNextDay()
        {
            if (!_enabled) return;
            foreach (WarPair pair in new List<WarPair>(Pairs.Values))
            {
                int score;
                if (WarScoreRecoveryPatch.TryReadScore(pair.First, pair.Second, out score))
                    Add("next-day " + pair.First.StringId + " vs " + pair.Second.StringId + " score=" + score + " fiefs=" + OwnedFiefs(pair));
                else Add("next-day " + pair.First.StringId + " vs " + pair.Second.StringId + " score=unavailable");
            }
        }

        private static void Track(Kingdom first, Kingdom second)
        {
            string key = WarOccupationOutcome.CanonicalWarKey(first.StringId, second.StringId);
            if (!Pairs.ContainsKey(key)) Pairs.Add(key, new WarPair { First = first, Second = second });
        }

        private static string SettlementText(Settlement settlement)
        {
            if (settlement == null) return string.Empty;
            string owner = settlement.OwnerClan == null || settlement.OwnerClan.Kingdom == null ? "none" : settlement.OwnerClan.Kingdom.StringId;
            return " fief=" + settlement.StringId + " owner=" + owner;
        }

        private static string OwnedFiefs(WarPair pair)
        {
            List<string> fiefs = new List<string>();
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null || !settlement.IsFortification || settlement.OwnerClan == null || settlement.OwnerClan.Kingdom == null) continue;
                Kingdom owner = settlement.OwnerClan.Kingdom;
                if (owner == pair.First || owner == pair.Second) fiefs.Add(settlement.StringId + ":" + owner.StringId);
            }
            fiefs.Sort(StringComparer.Ordinal);
            return fiefs.Count == 0 ? "none" : string.Join(",", fiefs.ToArray());
        }

        private static void Add(string entry)
        {
            if (Entries.Count == MaximumEntries) Entries.RemoveAt(0);
            Entries.Add(entry);
            System.Diagnostics.Trace.WriteLine("AOC " + entry);
        }

        private sealed class WarPair { internal Kingdom First; internal Kingdom Second; }
    }
}
