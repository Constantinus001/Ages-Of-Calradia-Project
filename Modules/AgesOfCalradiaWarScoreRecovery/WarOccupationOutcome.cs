using System;
using System.Collections.Generic;

namespace AgesOfCalradia.WarScoreRecovery
{
    // Pure settlement policy: Core remains the authority for recording and
    // applying occupations. This only determines whether an active war has a
    // clear occupation winner when peace is proposed before +/-100 score.
    internal static class WarOccupationOutcome
    {
        internal const int SurrenderScore = 100;

        internal static int Score(string firstKingdomId, string secondKingdomId, IEnumerable<string> records, Func<string, int?> settlementValue)
        {
            if (string.IsNullOrEmpty(firstKingdomId) || string.IsNullOrEmpty(secondKingdomId) || records == null || settlementValue == null) return 0;
            string warKey = CanonicalWarKey(firstKingdomId, secondKingdomId);
            int total = 0;
            HashSet<string> countedSettlements = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in records)
            {
                string settlementId;
                string victorKingdomId;
                if (!TryParse(raw, warKey, out settlementId, out victorKingdomId)) continue;
                // Core stores one record per war/fief. Keep a damaged or
                // externally duplicated record from inflating an outcome.
                if (!countedSettlements.Add(settlementId)) continue;
                int? value = settlementValue(settlementId);
                if (!value.HasValue || value.Value <= 0) continue;
                if (string.Equals(victorKingdomId, firstKingdomId, StringComparison.Ordinal)) total += value.Value;
                else if (string.Equals(victorKingdomId, secondKingdomId, StringComparison.Ordinal)) total -= value.Value;
            }
            return total > 0 ? SurrenderScore : (total < 0 ? -SurrenderScore : 0);
        }

        internal static string CanonicalWarKey(string firstKingdomId, string secondKingdomId)
        {
            return string.CompareOrdinal(firstKingdomId, secondKingdomId) <= 0
                ? firstKingdomId + "|" + secondKingdomId
                : secondKingdomId + "|" + firstKingdomId;
        }

        private static bool TryParse(string raw, string requiredWarKey, out string settlementId, out string victorKingdomId)
        {
            settlementId = string.Empty;
            victorKingdomId = string.Empty;
            if (string.IsNullOrEmpty(raw)) return false;
            string[] fields = raw.Split(new[] { '\t' }, 4);
            if (fields.Length != 4 || !string.Equals(fields[0], requiredWarKey, StringComparison.Ordinal)) return false;
            if (string.IsNullOrEmpty(fields[1]) || string.IsNullOrEmpty(fields[3])) return false;
            settlementId = fields[1];
            victorKingdomId = fields[3];
            return true;
        }
    }
}
