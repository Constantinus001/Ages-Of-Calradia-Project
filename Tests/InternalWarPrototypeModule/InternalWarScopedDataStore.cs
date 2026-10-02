using System;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    // Persistence adapter: every controller retains its existing schema keys inside a stable
    // war-ID namespace. Escape both components so delimiter characters cannot alias another scope.
    internal sealed class InternalWarScopedDataStore : IDataStore
    {
        private readonly IDataStore _store;
        private readonly string _prefix;

        internal InternalWarScopedDataStore(IDataStore store, string prefix)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (prefix == null) throw new ArgumentNullException(nameof(prefix));
            if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A save scope requires a stable war identity.", nameof(prefix));
            _store = store;
            _prefix = Uri.EscapeDataString(prefix) + ":";
        }

        public bool IsLoading { get { return _store.IsLoading; } }
        public bool IsSaving { get { return _store.IsSaving; } }

        public bool SyncData<T>(string key, ref T data)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A scoped save key cannot be blank.", nameof(key));
            return _store.SyncData(_prefix + Uri.EscapeDataString(key), ref data);
        }
    }
}
