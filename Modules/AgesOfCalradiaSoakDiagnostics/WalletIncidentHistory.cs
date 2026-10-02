using System.Collections.Generic;
using System.Text;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Investigative detail only. Snapshot sweeps cannot evict transaction history.
    // Fixed global bytes, wallet count and per-wallet rows; no game objects retained.
    internal sealed class WalletIncidentHistory
    {
        internal const int ByteLimit = 1048576, WalletLimit = 512, RowsPerWallet = 8;
        private sealed class History
        {
            internal string Owner;
            internal readonly Queue<string> Rows = new Queue<string>();
        }
        private readonly Dictionary<string, LinkedListNode<History>> _wallets = new Dictionary<string, LinkedListNode<History>>();
        private readonly LinkedList<History> _lru = new LinkedList<History>();
        internal int Bytes { get; private set; }
        internal long EvictedRows { get; private set; }
        private static int Size(string row) { return Encoding.UTF8.GetByteCount(row) + 2; }
        internal void Observe(string owner, string row)
        {
            LinkedListNode<History> node;
            if (!_wallets.TryGetValue(owner, out node))
            {
                node = new LinkedListNode<History>(new History { Owner = owner });
                _wallets.Add(owner, node);
            }
            else _lru.Remove(node);
            _lru.AddLast(node);
            node.Value.Rows.Enqueue(row); Bytes += Size(row);
            while (node.Value.Rows.Count > RowsPerWallet) RemoveRow(node.Value);
            while (Bytes > ByteLimit || _wallets.Count > WalletLimit)
            {
                var first = _lru.First.Value;
                while (first.Rows.Count > 0) RemoveRow(first);
                _wallets.Remove(first.Owner); _lru.RemoveFirst();
            }
        }
        private void RemoveRow(History history)
        {
            Bytes -= Size(history.Rows.Dequeue()); EvictedRows++;
        }
        internal string[] Get(string owner)
        {
            LinkedListNode<History> node;
            return _wallets.TryGetValue(owner, out node) ? node.Value.Rows.ToArray() : new string[0];
        }
    }
}
