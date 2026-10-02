using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaSuccession
{
    internal sealed class SuccessionCrisisController
    {
        private readonly SuccessionCampaignBehavior _behavior;
        internal SuccessionCrisisController(SuccessionCampaignBehavior behavior) { _behavior = behavior; }
        internal bool IsBusy { get { return _crisisBusy; } }
        private static int CurrentDay { get { return (int)Math.Floor(CampaignTime.Now.ToDays); } }

        private readonly Dictionary<string, string> _crisisRealm = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _crisisHero = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _crisisStage = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _crisisDay = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _crisisPayload = string.Empty;
        private bool _crisisBusy;
        private int _lastCrisisAudit = int.MinValue;

        internal void SyncData(IDataStore store)
        {
            if (store.IsSaving)
            {
                // A native callback can save halfway through formation or settlement.
                // Only the saved copy is quarantined; a successful live operation continues.
                var stages = new Dictionary<string, string>(_crisisStage, StringComparer.Ordinal);
                foreach (string id in stages.Keys.ToList())
                    if (stages[id] == "Forming" || stages[id] == "Settling") stages[id] = "Failed";
                _crisisPayload = SuccessionPoliticsPersistence.Serialize(_crisisRealm, _crisisHero, stages, _crisisDay);
            }
            store.SyncData("AOC_Succession_Crises_v1", ref _crisisPayload);
            if (store.IsLoading)
            {
                SuccessionPoliticsPersistence.Deserialize(_crisisPayload, _crisisRealm, _crisisHero, _crisisStage, _crisisDay);
                foreach (string id in _crisisStage.Keys.ToList())
                {
                    string stage = _crisisStage[id];
                    int day;
                    if ((stage != "Active" && stage != "Closed" && stage != "Failed")
                        || !int.TryParse(Get(_crisisDay, id), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)
                        || string.IsNullOrEmpty(Get(_crisisRealm, id)) || string.IsNullOrEmpty(Get(_crisisHero, id)))
                        _crisisStage[id] = "Failed";
                    if (_crisisStage[id] == "Failed") SuccessionDiagnostics.Info("Claimant crisis for " + id + " is quarantined; restore a pre-crisis save after diagnosing the failure.");
                }
                _lastCrisisAudit = int.MinValue;
            }
        }

        internal string GetCrisisStatus(Kingdom kingdom)
        {
            if (kingdom == null) return "None";
            string id = CrisisOrigin(kingdom.StringId);
            return id == null ? "None" : Get(_crisisStage, id);
        }

        private string CrisisOrigin(string realm)
        {
            if (_crisisStage.ContainsKey(realm)) return realm;
            return _crisisRealm.FirstOrDefault(p => p.Value == realm).Key;
        }

        internal bool CrisisBlocksRealm(Kingdom kingdom)
        {
            if (kingdom == null) return false;
            string stage = GetCrisisStatus(kingdom);
            return stage == "Forming" || stage == "Settling" || stage == "Failed";
        }

        internal bool TryBeginCrisis(Kingdom original, Hero claimant, string realmId)
        {
            if (_crisisBusy || original == null || claimant == null || _behavior.IsAuthorityTransferBlocked(original)) return false;
            string stage = GetCrisisStatus(original);
            if (stage != "None" && stage != "Closed") return false;
            _crisisBusy = true;
            _crisisRealm[original.StringId] = realmId;
            _crisisHero[original.StringId] = claimant.StringId;
            _crisisStage[original.StringId] = "Forming";
            _crisisDay[original.StringId] = CurrentDay.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal void FinishCrisisCreation(Kingdom original, bool success)
        {
            _crisisStage[original.StringId] = success ? "Active" : "Failed";
            _crisisBusy = false;
        }

        internal void Audit()
        {
            if (_crisisBusy || _lastCrisisAudit == CurrentDay) return;
            _lastCrisisAudit = CurrentDay;
            foreach (string id in _crisisStage.Keys.ToList())
            {
                if (Get(_crisisStage, id) != "Active") continue;
                Kingdom original = Kingdom.All.FirstOrDefault(k => k != null && k.StringId == id);
                Kingdom rebel = Kingdom.All.FirstOrDefault(k => k != null && k.StringId == Get(_crisisRealm, id));
                if (original == null || rebel == null)
                {
                    _crisisStage[id] = "Failed";
                    SuccessionDiagnostics.Info("Claimant crisis for " + id + " lost a recorded kingdom; quarantined.");
                    continue;
                }
                if (original.IsEliminated || rebel.IsEliminated)
                {
                    CloseCrisis(id); // Do not resurrect kingdoms eliminated by native or external systems.
                    continue;
                }
                if (_behavior.IsAuthorityTransferBlocked(original) || _behavior.IsAuthorityTransferBlocked(rebel)) continue;
                Hero claimant = FindHero(Get(_crisisHero, id));
                int day = int.Parse(Get(_crisisDay, id), CultureInfo.InvariantCulture);
                // Captivity alone does not extinguish a claim. Wait for release
                // before accession instead of treating IsActive=false as defeat.
                bool holdsClaim = claimant != null && claimant.IsAlive && claimant.Clan != null
                    && claimant.Clan.Kingdom == rebel && claimant.Clan.Leader == claimant && !claimant.Clan.IsEliminated;
                bool victory = holdsClaim && SuccessionResolver.IsEligibleClanLeader(rebel, claimant)
                    && original.Fiefs.Count == 0 && rebel.Fiefs.Count > 0;
                bool ended = !holdsClaim || (CurrentDay > day && rebel.Fiefs.Count == 0)
                    || (CurrentDay > day && !FactionManager.IsAtWarAgainstFaction(original, rebel));
                if (victory || ended) SettleCrisis(original, rebel, claimant, victory);
            }
            foreach (Kingdom realm in Kingdom.All.ToList())
            {
                if (realm == null || realm.IsEliminated || _behavior.IsAuthorityTransferBlocked(realm)) continue;
                string stage = GetCrisisStatus(realm);
                if (stage != "None" && stage != "Closed") continue;
                int previous;
                if (stage == "Closed" && int.TryParse(Get(_crisisDay, CrisisOrigin(realm.StringId)), out previous)
                    && CurrentDay - previous < 90) continue;
                int accession = _behavior.GetAccessionDay(realm);
                if (accession < 0 || CurrentDay - accession < 14 || _behavior.GetLegitimacy(realm) >= 45f) continue;
                Hero claimant = _behavior.GetPretender(realm);
                if (claimant == null || (Hero.MainHero != null && claimant.Clan == Hero.MainHero.Clan)) continue;
                List<Clan> supporters = _behavior.GetCivilWarSupporters(realm, claimant)
                    .Where(c => Hero.MainHero == null || c != Hero.MainHero.Clan).ToList();
                int eligibleClans = realm.Clans.Count(c => c != null && SuccessionResolver.IsEligibleClanLeader(realm, c.Leader));
                if (supporters.Count < 2 || supporters.Count * 3 < eligibleClans
                    || !supporters.Any(c => c.Fiefs.Count > 0)
                    || !realm.Clans.Any(c => c != null && !supporters.Contains(c) && c.Fiefs.Count > 0)) continue;
                string result;
                SuccessionCivilWar.TryStart(realm, claimant, supporters, _behavior, out result);
            }
        }

        private void SettleCrisis(Kingdom original, Kingdom rebel, Hero claimant, bool victory)
        {
            string id = original.StringId;
            _crisisBusy = true;
            _crisisStage[id] = "Settling";
            try
            {
                SuccessionCivilWar.Reunify(original, rebel, claimant, victory);
                _behavior.CommitClaimantSettlement(original, claimant, victory);
                CloseCrisis(id);
                SuccessionDiagnostics.Info("Claimant crisis in " + original.Name + " settled: " + (victory ? "claimant accession." : "incumbent retained."));
            }
            catch (Exception exception)
            {
                _crisisStage[id] = "Failed";
                SuccessionDiagnostics.Error("Claimant settlement failed for " + id + "; partial native changes quarantined. Restore a pre-crisis save.", exception);
            }
            finally { _crisisBusy = false; }
        }

        private static Hero FindHero(string id)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == id)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h != null && h.StringId == id);
        }

        private static string Get(IDictionary<string, string> values, string id)
        {
            string value;
            return id != null && values.TryGetValue(id, out value) ? value : string.Empty;
        }

        private void CloseCrisis(string id)
        {
            _crisisStage[id] = "Closed";
            _crisisDay[id] = CurrentDay.ToString(CultureInfo.InvariantCulture);
        }
    }
}
