using System;
using System.Collections.Generic;
using System.Linq;

namespace AgesOfCalradiaSuccession
{
    // Campaign-thread only. A failed native mutation is quarantined rather than
    // retried against potentially half-mutated state. No background game calls.
    internal sealed class SuccessionDispatchQueue
    {
        internal readonly Dictionary<string, string> Pending = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Completed = new Dictionary<string, string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, string> Failed = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _active = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _draining;
        internal bool IsDraining { get { return _draining; } }

        internal bool Request(string realm, string token)
        {
            if (string.IsNullOrEmpty(realm) || string.IsNullOrEmpty(token)) return false;
            if (Failed.ContainsKey(realm)) return false;
            if (Matches(Pending, realm, token) || Matches(Completed, realm, token)
                || Matches(Failed, realm, token) || Matches(_active, realm, token)) return false;
            Pending[realm] = token;
            return true;
        }

        internal bool Owns(string realm)
        {
            return Pending.ContainsKey(realm) || _active.ContainsKey(realm) || Failed.ContainsKey(realm);
        }

        internal void Drain(Action<string, string> resolve, Action<string, Exception> onFailure)
        {
            if (_draining || Pending.Count == 0) return;
            _draining = true;
            try
            {
                foreach (KeyValuePair<string, string> request in Pending.ToArray())
                {
                    if (Failed.ContainsKey(request.Key) || !Matches(Pending, request.Key, request.Value)) continue;
                    Pending.Remove(request.Key);
                    _active[request.Key] = request.Value;
                    try
                    {
                        resolve(request.Key, request.Value);
                        Completed[request.Key] = request.Value;
                        Failed.Remove(request.Key);
                    }
                    catch (Exception exception)
                    {
                        // resolve crosses native campaign action/event boundaries.
                        Failed[request.Key] = request.Value;
                        Pending.Remove(request.Key);
                        onFailure(request.Key, exception);
                    }
                    finally { _active.Remove(request.Key); }
                }
            }
            finally { _draining = false; }
        }

        internal string Serialize()
        {
            Dictionary<string, string> pending = new Dictionary<string, string>(Pending, StringComparer.Ordinal);
            Dictionary<string, string> failed = new Dictionary<string, string>(Failed, StringComparer.Ordinal);
            // A save invoked reentrantly from a native action may contain a partial
            // crown transfer. Its loaded state must never automatically replay it.
            // Quarantine only the saved copy; the current campaign can finish.
            foreach (KeyValuePair<string, string> request in _active)
            {
                pending.Remove(request.Key);
                failed[request.Key] = request.Value;
            }
            return SuccessionPoliticsPersistence.Serialize(pending, Completed, failed);
        }

        internal void Deserialize(string payload)
        {
            _active.Clear();
            _draining = false;
            SuccessionPoliticsPersistence.Deserialize(payload, Pending, Completed, Failed);
            // The shared codec emits an empty cell for maps without a value.
            foreach (Dictionary<string, string> map in new[] { Pending, Completed, Failed })
                foreach (string key in map.Where(p => string.IsNullOrEmpty(p.Value)).Select(p => p.Key).ToArray())
                    map.Remove(key);
            foreach (string realm in Failed.Keys) Pending.Remove(realm);
            foreach (KeyValuePair<string, string> request in Pending.ToArray())
                if (Matches(Completed, request.Key, request.Value)) Pending.Remove(request.Key);
        }

        private static bool Matches(IDictionary<string, string> map, string realm, string token)
        {
            string value;
            return map.TryGetValue(realm, out value) && value == token;
        }
    }
}
