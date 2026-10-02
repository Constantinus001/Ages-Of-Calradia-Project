using System;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;

namespace AgesOfCalradiaSuccession
{
    public sealed partial class SuccessionCampaignBehavior
    {
        private readonly SuccessionDispatchQueue _dispatch = new SuccessionDispatchQueue();
        private readonly ConditionalWeakTable<KingSelectionKingdomDecision, object> _seenDecisions
            = new ConditionalWeakTable<KingSelectionKingdomDecision, object>();
        private string _dispatchPayload = string.Empty;

        private void SyncDispatchData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) _dispatchPayload = _dispatch.Serialize();
            dataStore.SyncData("AOC_Succession_Dispatch_v1", ref _dispatchPayload);
            if (dataStore.IsLoading) _dispatch.Deserialize(_dispatchPayload);
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null) return;
            // The native clan leader may already have changed. Use our persisted
            // monarch/regent identity, never only victim == kingdom.Leader.
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                string id = kingdom.StringId;
                if (Get(_monarchByKingdom, id) == victim.StringId || Get(_regentByKingdom, id) == victim.StringId
                    || GetMinorHeir(kingdom) == victim)
                    RequestSuccession(kingdom, victim.StringId);
            }
        }

        internal bool InterceptRulerDecision(KingSelectionKingdomDecision decision)
        {
            if (decision == null || decision.Kingdom == null) return false;
            object seen;
            if (_seenDecisions.TryGetValue(decision, out seen)) return true;
            _seenDecisions.Add(decision, new object());
            Kingdom kingdom = decision.Kingdom;
            if (kingdom.IsEliminated) return true;
            if (CrisisBlocksRealm(kingdom)) return true;
            string id = kingdom.StringId;
            if (_dispatch.Owns(id)) return true;
            Hero monarch = FindHero(Get(_monarchByKingdom, id));
            Hero regent = GetRegent(kingdom);
            Hero minor = GetMinorHeir(kingdom);
            if (decision.IsEnforced && kingdom.Leader != null && kingdom.Leader.IsAlive)
            {
                RequestSuccession(kingdom, "abdication:" + Uri.EscapeDataString(kingdom.Leader.StringId)
                    + ":" + CurrentDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }
            // Suppress stale/new duplicate elections after an accession. Native
            // abdication is enforced and is still a deliberate succession trigger.
            if (monarch == null || (!monarch.IsAlive && minor == null) || (regent != null && !regent.IsAlive)
                || (minor != null && !minor.IsAlive)
                || decision.IsEnforced)
                RequestSuccession(kingdom);
            return true;
        }

        private void RequestSuccession(Kingdom kingdom, string token = null)
        {
            string id = kingdom.StringId;
            string monarch = Get(_monarchByKingdom, id);
            if (string.IsNullOrEmpty(monarch))
            {
                Snapshot(kingdom);
                monarch = Get(_monarchByKingdom, id);
            }
            Hero regent = GetRegent(kingdom);
            Hero minor = GetMinorHeir(kingdom);
            if (string.IsNullOrEmpty(token))
                token = minor != null && !minor.IsAlive ? minor.StringId
                    : regent != null && !regent.IsAlive ? regent.StringId : monarch;
            if (string.IsNullOrEmpty(token)) token = "unrecorded-incumbent";
            if (_dispatch.Request(id, token))
                SuccessionDiagnostics.Info("Queued hereditary succession for " + id + ", incumbent token " + token + ".");
        }

        private void RecoverPendingSuccessions()
        {
            foreach (string realm in _dispatch.Failed.Keys)
                SuccessionDiagnostics.Info("Succession for " + realm
                    + " remains quarantined after loading a failed or interrupted transfer. Reload a pre-event save after diagnosing the failure.");
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                Hero monarch = FindHero(Get(_monarchByKingdom, kingdom.StringId));
                Hero regent = GetRegent(kingdom);
                Hero minor = GetMinorHeir(kingdom);
                if ((monarch != null && !monarch.IsAlive && GetMinorHeir(kingdom) == null)
                    || (regent != null && !regent.IsAlive) || (minor != null && !minor.IsAlive)) RequestSuccession(kingdom);
                foreach (KingSelectionKingdomDecision decision in kingdom.UnresolvedDecisions.OfType<KingSelectionKingdomDecision>().ToList())
                {
                    InterceptRulerDecision(decision);
                    kingdom.RemoveDecision(decision);
                }
            }
        }

        private void OnSuccessionTick(float dt)
        {
            if (IsCrisisBusy || _dispatch.IsDraining) return;
            _dispatch.Drain(delegate(string id, string token)
            {
                Kingdom kingdom = Kingdom.All.FirstOrDefault(k => k != null && k.StringId == id);
                if (kingdom == null || kingdom.IsEliminated || CrisisBlocksRealm(kingdom)) return;
                string[] fields = token.Split(':');
                Hero abdicator = fields.Length == 3 && fields[0] == "abdication" ? FindHero(Uri.UnescapeDataString(fields[1])) : null;
                ResolveSuccession(kingdom, abdicator);
            }, delegate(string id, Exception exception)
            {
                SuccessionDiagnostics.Error("Succession for " + id
                    + " failed at the native action boundary; request quarantined, ruler election remains blocked. Reload a pre-event save before retrying.", exception);
            });
            AuditCrises();
        }

        private static void VerifyNativeRuler(Kingdom kingdom, Hero expected)
        {
            if (kingdom.Leader != expected || kingdom.RulingClan != expected.Clan || expected.Clan.Leader != expected)
                throw new InvalidOperationException("A native or external action prevented the selected ruler transfer; political state was not committed.");
        }

        internal bool IsAuthorityTransferBlocked(Kingdom kingdom)
        {
            return kingdom == null || kingdom.IsEliminated || _dispatch.Owns(kingdom.StringId) || CrisisBlocksRealm(kingdom);
        }

        private void AuditVacantThrones()
        {
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom == null || kingdom.IsEliminated || _dispatch.Owns(kingdom.StringId)
                    || CrisisBlocksRealm(kingdom) || GetMinorHeir(kingdom) != null) continue;
                Hero monarch = FindHero(Get(_monarchByKingdom, kingdom.StringId));
                if (monarch != null && !monarch.IsAlive)
                    RequestSuccession(kingdom, "vacancy:" + CurrentDay.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
