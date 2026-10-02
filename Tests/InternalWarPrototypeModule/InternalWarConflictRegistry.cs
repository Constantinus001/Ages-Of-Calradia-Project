using System;
using System.Collections.Generic;

namespace AgesOfCalradiaInternalWarsTest
{
    // Pure campaign persistence boundary. Keep live record identities so selecting a war never
    // creates a second authority. Closed wars remain available; only open pairs must be unique.
    internal sealed class InternalWarConflictRegistry
    {
        private readonly List<InternalConflictRecord> _records = new List<InternalConflictRecord>();
        internal IEnumerable<InternalConflictRecord> Records { get { return _records.AsReadOnly(); } }
        internal int Count { get { return _records.Count; } }

        internal InternalConflictRecord GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return _records.Find(record => record.Id == id);
        }

        internal InternalConflictRecord Find(string clanA, string clanB)
        {
            if (string.IsNullOrWhiteSpace(clanA) || string.IsNullOrWhiteSpace(clanB) || clanA == clanB) return null;
            return _records.Find(record => record.IsOpen
                && ((record.AttackerClanId == clanA && record.DefenderClanId == clanB)
                    || (record.AttackerClanId == clanB && record.DefenderClanId == clanA)));
        }

        internal bool Add(InternalConflictRecord record)
        {
            if (!IsValid(record) || GetById(record.Id) != null
                || (record.IsOpen && Find(record.AttackerClanId, record.DefenderClanId) != null)) return false;
            _records.Add(record);
            return true;
        }

        internal string Serialize()
        {
            // Records are intentionally mutable. Revalidate the complete set at every save;
            // a changed ID or reopened historical pair must not produce a corrupt snapshot.
            var validated = new InternalWarConflictRegistry();
            var rows = new List<string> { "v1" };
            foreach (InternalConflictRecord record in _records)
            {
                if (!validated.Add(record)) throw new InvalidOperationException("The conflict registry contains invalid or duplicate war records.");
                rows.Add(Uri.EscapeDataString(record.Serialize()));
            }
            return string.Join("\n", rows);
        }

        internal static InternalWarConflictRegistry Deserialize(string payload)
        {
            var registry = new InternalWarConflictRegistry();
            if (string.IsNullOrEmpty(payload)) return registry;
            string[] rows = payload.Split('\n');
            if (rows[0] != "v1") return null;
            for (int index = 1; index < rows.Length; index++)
            {
                // Canonical escaping rejects malformed percent escapes and unexpected row delimiters.
                string row = Uri.UnescapeDataString(rows[index]);
                if (Uri.EscapeDataString(row) != rows[index]
                    || !registry.Add(InternalConflictRecord.Deserialize(row))) return null;
            }
            return registry;
        }

        private static bool IsValid(InternalConflictRecord record)
        {
            if (record == null || record.Id == null || record.KingdomId == null
                || record.AttackerClanId == null || record.DefenderClanId == null
                || record.LastCompletedOperationId == null || record.ActiveOperationId == null
                || record.GoalSettlementId == null || record.CompensationPayerId == null) return false;
            return InternalConflictRecord.Deserialize(record.Serialize()) != null;
        }
    }
}
