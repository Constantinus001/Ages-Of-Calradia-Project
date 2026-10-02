using System;
using System.Collections.Generic;

namespace AgesOfCalradiaInternalWarsTest
{
    // Store serialized snapshots, never references to a live mutable conflict.
    internal sealed class InternalWarHistory
    {
        private readonly Dictionary<string, string> _records = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        internal int Count { get { return _order.Count; } }

        internal void Archive(InternalConflictRecord conflict)
        {
            if (conflict == null || conflict.IsOpen) throw new InvalidOperationException("Only closed wars can enter history.");
            string payload = conflict.Serialize();
            if (_records.ContainsKey(conflict.Id))
            {
                if (_records[conflict.Id] != payload) throw new InvalidOperationException("A closed war's archived result changed.");
                return;
            }
            _records.Add(conflict.Id, payload);
            _order.Add(conflict.Id);
        }

        internal string Serialize()
        {
            var rows = new List<string> { "history-v1" };
            foreach (string id in _order) rows.Add(_records[id]);
            return string.Join("\n", rows);
        }

        internal static InternalWarHistory Deserialize(string payload)
        {
            var history = new InternalWarHistory();
            if (string.IsNullOrEmpty(payload)) return history;
            string[] rows = payload.Split('\n');
            if (rows[0] != "history-v1") return null;
            for (int index = 1; index < rows.Length; index++)
            {
                InternalConflictRecord record = InternalConflictRecord.Deserialize(rows[index]);
                if (record == null || record.IsOpen || history._records.ContainsKey(record.Id)) return null;
                history.Archive(record);
            }
            return history;
        }

        internal string DescribeRecent(int maximum)
        {
            var lines = new List<string>();
            for (int index = _order.Count - 1; index >= 0 && lines.Count < maximum; index--)
            {
                InternalConflictRecord record = InternalConflictRecord.Deserialize(_records[_order[index]]);
                lines.Add(record.AttackerClanId + " vs " + record.DefenderClanId + ": " + record.DescribeGoal()
                    + "; completed operations=" + record.CompletedOperations);
            }
            return lines.Count == 0 ? "No previous closed wars." : string.Join("\n", lines);
        }
    }
}
