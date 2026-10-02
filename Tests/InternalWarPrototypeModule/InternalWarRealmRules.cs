using System;
using System.Collections.Generic;

namespace AgesOfCalradiaInternalWarsTest
{
    // Pure per-kingdom policy, independent of a ruler's lifetime. Missing legacy data permits declarations.
    internal sealed class InternalWarRealmRules
    {
        private readonly SortedSet<string> _banned = new SortedSet<string>(StringComparer.Ordinal);
        internal bool IsBanned(string kingdomId) { return kingdomId != null && _banned.Contains(kingdomId); }
        internal void SetBanned(string kingdomId, bool banned)
        {
            if (string.IsNullOrWhiteSpace(kingdomId)) throw new ArgumentException("A kingdom identity is required.", "kingdomId");
            if (banned) _banned.Add(kingdomId); else _banned.Remove(kingdomId);
        }
        internal string Serialize()
        {
            var rows = new List<string> { "realm-rules-v1" };
            foreach (string id in _banned) rows.Add(Uri.EscapeDataString(id));
            return string.Join("\n", rows);
        }
        internal static InternalWarRealmRules Deserialize(string payload)
        {
            var rules = new InternalWarRealmRules();
            if (string.IsNullOrEmpty(payload)) return rules;
            string[] rows = payload.Split('\n');
            if (rows[0] != "realm-rules-v1") return null;
            for (int index = 1; index < rows.Length; index++)
            {
                string id = Uri.UnescapeDataString(rows[index]);
                if (string.IsNullOrWhiteSpace(id) || Uri.EscapeDataString(id) != rows[index] || !rules._banned.Add(id)) return null;
            }
            return rules;
        }
    }
}
