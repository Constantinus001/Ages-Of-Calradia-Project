using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AgesOfCalradiaSuccession
{
    /// <summary>
    /// Replaces only native kingdom ruler elections with deterministic hereditary
    /// resolution. It does not touch clan inheritance, politics UI, or map assets.
    /// </summary>
    public sealed partial class SuccessionCampaignBehavior : CampaignBehaviorBase
    {
        private const string StateKey = "AOC_Succession_State_v2";
        private const string PoliticsKey = "AOC_Succession_Politics_v1";
        private string _payload = string.Empty;
        private string _politicsPayload = string.Empty;
        private SuccessionCrisisController _crises;
        internal SuccessionCrisisController Crises { get { return _crises ?? (_crises = new SuccessionCrisisController(this)); } }
        private bool IsCrisisBusy { get { return Crises.IsBusy; } }
        internal bool CrisisBlocksRealm(Kingdom kingdom) { return Crises.CrisisBlocksRealm(kingdom); }
        internal string GetCrisisStatus(Kingdom kingdom) { return Crises.GetCrisisStatus(kingdom); }
        private void AuditCrises() { Crises.Audit(); }
        internal bool TryBeginCrisis(Kingdom kingdom, Hero claimant, string id) { return Crises.TryBeginCrisis(kingdom, claimant, id); }
        internal void FinishCrisisCreation(Kingdom kingdom, bool success) { Crises.FinishCrisisCreation(kingdom, success); }
        private readonly Dictionary<string, string> _lawByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _dynastyByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _monarchByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _minorHeirByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _regentByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _legitimacyByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _coronatedByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _accessionBasisByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _accessionDayByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pretenderByKingdom = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _recognitionByRealmClan = new Dictionary<string, string>(StringComparer.Ordinal);

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, OnKingdomCreated);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnSuccessionTick);
            CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
        }

        public override void SyncData(IDataStore dataStore)
        {
            Crises.SyncData(dataStore);
            SyncDispatchData(dataStore);
            if (dataStore.IsSaving)
                _payload = SuccessionPersistence.Serialize(_lawByKingdom, _dynastyByKingdom, _monarchByKingdom, _minorHeirByKingdom, _regentByKingdom);
            dataStore.SyncData(StateKey, ref _payload);
            if (dataStore.IsLoading)
                SuccessionPersistence.Deserialize(_payload, _lawByKingdom, _dynastyByKingdom, _monarchByKingdom, _minorHeirByKingdom, _regentByKingdom);
            if (dataStore.IsSaving)
                _politicsPayload = SuccessionPoliticsPersistence.Serialize(_legitimacyByKingdom, _coronatedByKingdom,
                    _accessionBasisByKingdom, _accessionDayByKingdom, _pretenderByKingdom, _recognitionByRealmClan);
            dataStore.SyncData(PoliticsKey, ref _politicsPayload);
            if (dataStore.IsLoading)
                SuccessionPoliticsPersistence.Deserialize(_politicsPayload, _legitimacyByKingdom, _coronatedByKingdom,
                    _accessionBasisByKingdom, _accessionDayByKingdom, _pretenderByKingdom, _recognitionByRealmClan);
        }

        internal SuccessionLaw GetLaw(Kingdom kingdom)
        {
            if (kingdom == null) return SuccessionLaw.AbsolutePrimogeniture;
            string value;
            SuccessionLaw law;
            if (_lawByKingdom.TryGetValue(kingdom.StringId, out value) && Enum.TryParse(value, out law)
                && Enum.IsDefined(typeof(SuccessionLaw), law)) return law;
            law = SuccessionResolver.DefaultLawFor(kingdom);
            _lawByKingdom[kingdom.StringId] = law.ToString();
            return law;
        }

        internal IReadOnlyList<SuccessionClaim> GetClaimants(Kingdom kingdom)
        {
            Clan dynasty = FindClan(Get(_dynastyByKingdom, kingdom == null ? null : kingdom.StringId));
            Hero monarch = FindHero(Get(_monarchByKingdom, kingdom == null ? null : kingdom.StringId));
            return SuccessionResolver.Rank(kingdom, dynasty, monarch, GetLaw(kingdom));
        }

        internal Hero GetMinorHeir(Kingdom kingdom)
        {
            return FindHero(Get(_minorHeirByKingdom, kingdom == null ? null : kingdom.StringId));
        }

        internal Hero GetRegent(Kingdom kingdom)
        {
            return FindHero(Get(_regentByKingdom, kingdom == null ? null : kingdom.StringId));
        }

        internal float GetLegitimacy(Kingdom kingdom)
        {
            string value = Get(_legitimacyByKingdom, kingdom == null ? null : kingdom.StringId);
            float parsed;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                || float.IsNaN(parsed) || float.IsInfinity(parsed)) return 50f;
            return Math.Max(0f, Math.Min(100f, parsed));
        }

        internal bool IsCoronated(Kingdom kingdom)
        {
            return string.Equals(Get(_coronatedByKingdom, kingdom == null ? null : kingdom.StringId), "true", StringComparison.Ordinal);
        }

        internal Hero GetPretender(Kingdom kingdom)
        {
            Hero pretender = FindHero(Get(_pretenderByKingdom, kingdom == null ? null : kingdom.StringId));
            return SuccessionResolver.IsEligibleClanLeader(kingdom, pretender)
                && pretender.Clan != kingdom.RulingClan && pretender != GetRegent(kingdom)
                && (GetLaw(kingdom) != SuccessionLaw.AgnaticPrimogeniture || !pretender.IsFemale)
                ? pretender : null;
        }

        internal ClanRecognition GetRecognition(Kingdom kingdom, Clan clan)
        {
            string value = Get(_recognitionByRealmClan, RecognitionKey(kingdom, clan));
            ClanRecognition parsed;
            return Enum.TryParse(value, out parsed) && Enum.IsDefined(typeof(ClanRecognition), parsed)
                ? parsed : ClanRecognition.Neutral;
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
#if DEBUG || SUCCESSION_DIAGNOSTICS
            RunSafely("settlement debug menu registration", delegate { SuccessionDebugMenu.Register(starter, this); });
#endif
            RunSafely("coronation menu registration", delegate { SuccessionCoronationMenu.Register(starter, this); });
            RunSafely("pending succession recovery", RecoverPendingSuccessions);
            RunSafely("regency recovery audit", AuditRegencies);
            RunSafely("kingdom snapshot initialization", EnsureKingdomSnapshots);
            RunSafely("political-state initialization", EnsurePoliticalStates);
            RunSafely("religious legitimacy startup audit", delegate
            {
                float playerLegitimacy = SuccessionReligionBridge.GetReligiousLegitimacy(Hero.MainHero);
                SuccessionDiagnostics.Info("Hereditary succession v0.4.3 initialized. Player religious legitimacy input="
                    + playerLegitimacy.ToString("0.0", CultureInfo.InvariantCulture) + ".");
            });
        }

        private void OnDailyTick()
        {
            RunSafely("daily regency audit", AuditRegencies);
            RunSafely("daily kingdom snapshot", EnsureKingdomSnapshots);
            RunSafely("daily vacant throne audit", AuditVacantThrones);
            RunSafely("daily political-state update", UpdatePoliticalStates);
        }

        private void OnKingdomCreated(Kingdom kingdom)
        {
            Snapshot(kingdom);
        }

        private void ResolveSuccession(Kingdom kingdom, Hero abdicator = null)
        {
            if (kingdom == null || kingdom.IsEliminated) return;

            string kingdomId = kingdom.StringId;
            Clan dynasty = FindClan(Get(_dynastyByKingdom, kingdomId));
            Hero previousMonarch = FindHero(Get(_monarchByKingdom, kingdomId));
            SuccessionLaw law = GetLaw(kingdom);
            Hero recordedMinor = FindHero(Get(_minorHeirByKingdom, kingdomId));
            if (recordedMinor != null && recordedMinor.IsAlive && recordedMinor.IsActive
                && recordedMinor.Clan != null && recordedMinor.Clan.Kingdom == kingdom && !recordedMinor.Clan.IsEliminated)
            {
                if (recordedMinor.Age < SuccessionResolver.AdultAge)
                    AppointRegent(kingdom, recordedMinor, dynasty, abdicator);
                else
                    CrownAdultHeir(kingdom, recordedMinor, law, "completion of the recorded regency during succession dispatch");
                return;
            }

            Hero dynasticHeir = SuccessionResolver.FindLawfulDynasticHeir(dynasty, previousMonarch, law, kingdom);
            if (dynasticHeir != null && (dynasticHeir.Clan == null || dynasticHeir.Clan.Kingdom != kingdom))
            {
                SuccessionDiagnostics.Info("Dynastic claimant " + dynasticHeir.Name + " is outside " + kingdom.Name + "; foreign-clan transfer deferred.");
                dynasticHeir = null;
            }
            if (dynasticHeir != null)
            {
                if (dynasticHeir.Age < SuccessionResolver.AdultAge)
                {
                    _minorHeirByKingdom[kingdomId] = dynasticHeir.StringId;
                    BeginAccession(kingdom, dynasticHeir, "Regency", true);
                    AppointRegent(kingdom, dynasticHeir, dynasty, abdicator);
                    return;
                }

                CrownAdultHeir(kingdom, dynasticHeir, law, "lawful dynastic heir");
                return;
            }

            bool emergency = false;
            List<SuccessionClaim> claims = SuccessionResolver.Rank(kingdom, dynasty, previousMonarch, law)
                .Where(c => c.Hero != previousMonarch && c.Hero != abdicator).ToList();
            if (claims.Count == 0)
            {
                emergency = true;
                claims = SuccessionResolver.RankEmergency(kingdom, dynasty).Where(c => c.Hero != previousMonarch && c.Hero != abdicator).ToList();
                SuccessionDiagnostics.Info("Normal claimant order exhausted for " + kingdom.Name + "; deterministic emergency order invoked.");
            }

            if (claims.Count == 0)
            {
                SuccessionDiagnostics.Info("No living clan leader exists for " + kingdom.Name + "; ruler vote cancelled without a transfer target.");
                return;
            }

            SuccessionClaim heir = claims[0];
            if (heir.Hero.Age < SuccessionResolver.AdultAge)
            {
                _dynastyByKingdom[kingdomId] = heir.Clan.StringId;
                _minorHeirByKingdom[kingdomId] = heir.Hero.StringId;
                BeginAccession(kingdom, heir.Hero, "Regency", true);
                AppointRegent(kingdom, heir.Hero, heir.Clan, abdicator);
                return;
            }
            if (kingdom.RulingClan != heir.Clan)
                ChangeRulingClanAction.Apply(kingdom, heir.Clan);
            VerifyNativeRuler(kingdom, heir.Hero);

            _dynastyByKingdom[kingdomId] = heir.Clan.StringId;
            _monarchByKingdom[kingdomId] = heir.Hero.StringId;
            _minorHeirByKingdom.Remove(kingdomId);
            BeginAccession(kingdom, heir.Hero, emergency ? "Emergency" : "Collateral", false);
            string message = kingdom.Name + " passes by " + LawName(law) + " to " + heir.Hero.Name + ".";
            SuccessionDiagnostics.Info(message + " Basis: " + heir.Explanation + ". Native ruler vote cancelled.");
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private void EnsureKingdomSnapshots()
        {
            foreach (Kingdom kingdom in Kingdom.All) Snapshot(kingdom);
        }

        private void Snapshot(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return;
            string id = kingdom.StringId;
            if (!_lawByKingdom.ContainsKey(id)) _lawByKingdom[id] = SuccessionResolver.DefaultLawFor(kingdom).ToString();
            if (_dispatch.Owns(id) || CrisisBlocksRealm(kingdom)) return;
            Hero recorded = FindHero(Get(_monarchByKingdom, id));
            if (recorded != null && !recorded.IsAlive) return;
            if (IsUnderageHeir(FindHero(Get(_minorHeirByKingdom, id)))) return;
            if (kingdom.RulingClan != null && kingdom.Leader != null && kingdom.Leader.IsAlive)
            {
                _dynastyByKingdom[id] = kingdom.RulingClan.StringId;
                _monarchByKingdom[id] = kingdom.Leader.StringId;
            }
        }

        private void AppointRegent(Kingdom kingdom, Hero heir, Clan dynasty, Hero abdicator = null)
        {
            List<SuccessionClaim> candidates = SuccessionResolver.Rank(kingdom, dynasty, FindHero(Get(_monarchByKingdom, kingdom.StringId)), GetLaw(kingdom));
            SuccessionClaim regentClaim = candidates.FirstOrDefault(c => c.Hero != heir && c.Hero != abdicator && c.Hero.Age >= SuccessionResolver.AdultAge);
            if (regentClaim == null)
                regentClaim = SuccessionResolver.RankEmergency(kingdom, dynasty).FirstOrDefault(c => c.Hero != heir && c.Hero != abdicator && c.Hero.Age >= SuccessionResolver.AdultAge);

            if (regentClaim == null)
            {
                _regentByKingdom.Remove(kingdom.StringId);
                SuccessionDiagnostics.Info("No adult regent exists for underage heir " + heir.Name + " of " + kingdom.Name + ". Vote cancelled; regency remains vacant.");
                return;
            }

            if (kingdom.RulingClan != regentClaim.Clan) ChangeRulingClanAction.Apply(kingdom, regentClaim.Clan);
            VerifyNativeRuler(kingdom, regentClaim.Hero);
            _regentByKingdom[kingdom.StringId] = regentClaim.Hero.StringId;
            if (string.IsNullOrEmpty(Get(_accessionBasisByKingdom, kingdom.StringId)))
                BeginAccession(kingdom, heir, "Regency", true);
            else
                EvaluatePoliticalState(kingdom, heir);
            string message = regentClaim.Hero.Name + " becomes Regent of " + kingdom.Name + " for the underage heir " + heir.Name + ".";
            SuccessionDiagnostics.Info(message);
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private void CrownAdultHeir(Kingdom kingdom, Hero heir, SuccessionLaw law, string basis)
        {
            if (kingdom == null || heir == null || heir.Clan == null || heir.Clan.Kingdom != kingdom || !heir.IsAlive || heir.Age < SuccessionResolver.AdultAge) return;
            if (heir.Clan.Leader != heir) ChangeClanLeaderAction.ApplyWithSelectedNewLeader(heir.Clan, heir);
            if (kingdom.RulingClan != heir.Clan) ChangeRulingClanAction.Apply(kingdom, heir.Clan);
            VerifyNativeRuler(kingdom, heir);
            string id = kingdom.StringId;
            _dynastyByKingdom[id] = heir.Clan.StringId;
            _monarchByKingdom[id] = heir.StringId;
            _minorHeirByKingdom.Remove(id);
            _regentByKingdom.Remove(id);
            BeginAccession(kingdom, heir, "Dynastic", false);
            string message = heir.Name + " assumes the crown of " + kingdom.Name + " by " + LawName(law) + ".";
            SuccessionDiagnostics.Info(message + " Basis: " + basis + ".");
            InformationManager.DisplayMessage(new InformationMessage(message));
        }

        private void OnHeroComesOfAge(Hero hero)
        {
            if (hero == null) return;
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (_dispatch.Owns(kingdom.StringId) || CrisisBlocksRealm(kingdom)) continue;
                if (Get(_minorHeirByKingdom, kingdom.StringId) == hero.StringId)
                {
                    RequestSuccession(kingdom, "maturity:" + hero.StringId);
                    return;
                }
            }
        }

        private void AuditRegencies()
        {
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom == null || kingdom.IsEliminated) continue;
                string id = kingdom.StringId;
                if (_dispatch.Owns(id) || CrisisBlocksRealm(kingdom)) continue;
                string heirId = Get(_minorHeirByKingdom, id);
                if (string.IsNullOrEmpty(heirId)) continue;
                Hero heir = FindHero(heirId);
                Hero regent = FindHero(Get(_regentByKingdom, id));
                if (heir == null || !heir.IsAlive || !heir.IsActive || heir.Age >= SuccessionResolver.AdultAge
                    || regent == null || !regent.IsAlive || !regent.IsActive || kingdom.Leader != regent)
                    // Retry vacant/ineligible regencies at most once per day;
                    // partial native failures remain realm-quarantined by dispatch.
                    RequestSuccession(kingdom, "regency-audit:" + heirId + ":" + CurrentDay.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static bool IsUnderageHeir(Hero hero)
        {
            return hero != null && hero.IsAlive && hero.IsActive && hero.Age < SuccessionResolver.AdultAge;
        }

        internal void HoldCoronation(Kingdom kingdom, Hero ruler, bool notify)
        {
            if (kingdom == null || kingdom.IsEliminated || ruler == null || !ruler.IsAlive
                || _dispatch.Owns(kingdom.StringId) || CrisisBlocksRealm(kingdom)
                || ruler.Age < SuccessionResolver.AdultAge || kingdom.Leader != ruler
                || GetMinorHeir(kingdom) != null || IsCoronated(kingdom)) return;
            _coronatedByKingdom[kingdom.StringId] = "true";
            EvaluatePoliticalState(kingdom, ruler);
            string message = ruler.Name + " is crowned ruler of " + kingdom.Name + ". Legitimacy is now "
                + GetLegitimacy(kingdom).ToString("0", CultureInfo.InvariantCulture) + ".";
            SuccessionDiagnostics.Info(message);
            if (notify) InformationManager.DisplayMessage(new InformationMessage(message));
        }

        internal void RegisterClaimantRealm(Kingdom original, Kingdom claimantRealm, Hero pretender)
        {
            _dynastyByKingdom[claimantRealm.StringId] = pretender.Clan.StringId;
            _monarchByKingdom[claimantRealm.StringId] = pretender.StringId;
            _lawByKingdom[claimantRealm.StringId] = GetLaw(original).ToString();
            BeginAccession(claimantRealm, pretender, "Claimant", false);
            EvaluatePoliticalState(original, GetMinorHeir(original) ?? original.Leader);
        }

        internal int GetAccessionDay(Kingdom kingdom)
        {
            int day;
            return kingdom != null && int.TryParse(Get(_accessionDayByKingdom, kingdom.StringId),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out day) ? day : -1;
        }

        internal void CommitClaimantSettlement(Kingdom original, Hero claimant, bool victory)
        {
            if (victory)
            {
                string id = original.StringId;
                _dynastyByKingdom[id] = claimant.Clan.StringId;
                _monarchByKingdom[id] = claimant.StringId;
                _minorHeirByKingdom.Remove(id);
                _regentByKingdom.Remove(id);
                BeginAccession(original, claimant, "Claimant", false);
            }
            else EvaluatePoliticalState(original, GetMinorHeir(original) ?? original.Leader);
        }

        internal List<Clan> GetCivilWarSupporters(Kingdom kingdom, Hero pretender)
        {
            List<Clan> supporters = kingdom == null ? new List<Clan>() : kingdom.Clans
                .Where(c => c != null && c != kingdom.RulingClan && SuccessionResolver.IsEligibleClanLeader(kingdom, c.Leader)
                    && c.Leader.Clan == c
                    && GetRecognition(kingdom, c) == ClanRecognition.SupportsPretender)
                .ToList();
            if (SuccessionResolver.IsEligibleClanLeader(kingdom, pretender) && pretender.Clan != kingdom.RulingClan
                && !supporters.Contains(pretender.Clan))
                supporters.Insert(0, pretender.Clan);
            return supporters;
        }

        private void EnsurePoliticalStates()
        {
            foreach (Kingdom kingdom in Kingdom.All)
            {
                if (kingdom == null || kingdom.IsEliminated || _dispatch.Owns(kingdom.StringId) || CrisisBlocksRealm(kingdom)) continue;
                if (string.IsNullOrEmpty(Get(_accessionBasisByKingdom, kingdom.StringId)))
                {
                    _accessionBasisByKingdom[kingdom.StringId] = "Established";
                    _accessionDayByKingdom[kingdom.StringId] = CurrentDay.ToString(CultureInfo.InvariantCulture);
                    _coronatedByKingdom[kingdom.StringId] = "true";
                }
                EvaluatePoliticalState(kingdom, GetMinorHeir(kingdom) ?? kingdom.Leader);
            }
        }

        private void UpdatePoliticalStates()
        {
            foreach (Kingdom kingdom in Kingdom.All.ToList())
            {
                if (kingdom == null || kingdom.IsEliminated || _dispatch.Owns(kingdom.StringId) || CrisisBlocksRealm(kingdom)) continue;
                Hero subject = GetMinorHeir(kingdom) ?? kingdom.Leader;
                if (subject == null) continue;
                int accessionDay;
                int.TryParse(Get(_accessionDayByKingdom, kingdom.StringId), NumberStyles.Integer, CultureInfo.InvariantCulture, out accessionDay);
                if (GetMinorHeir(kingdom) == null && !IsCoronated(kingdom) && kingdom.Leader != Hero.MainHero && CurrentDay - accessionDay >= 7)
                    HoldCoronation(kingdom, kingdom.Leader, true);
                else
                    EvaluatePoliticalState(kingdom, subject);
            }
        }

        private void BeginAccession(Kingdom kingdom, Hero subject, string basis, bool regency)
        {
            if (kingdom == null || subject == null) return;
            string id = kingdom.StringId;
            _accessionBasisByKingdom[id] = basis;
            _accessionDayByKingdom[id] = CurrentDay.ToString(CultureInfo.InvariantCulture);
            _coronatedByKingdom[id] = "false";
            if (!regency) _regentByKingdom.Remove(id);
            EvaluatePoliticalState(kingdom, subject);
        }

        private void EvaluatePoliticalState(Kingdom kingdom, Hero subject)
        {
            if (kingdom == null || subject == null) return;
            string id = kingdom.StringId;
            string basis = Get(_accessionBasisByKingdom, id);
            float legitimacy;
            switch (basis)
            {
                case "Dynastic": legitimacy = 60f; break;
                case "Regency": legitimacy = 48f; break;
                case "Collateral": legitimacy = 42f; break;
                case "Claimant": legitimacy = 30f; break;
                case "Emergency": legitimacy = 25f; break;
                default: legitimacy = 65f; break;
            }

            if (subject.Culture == kingdom.Culture) legitimacy += 10f;
            string officialFaith = SuccessionReligionBridge.GetOfficialFaith(kingdom);
            string personalFaith = SuccessionReligionBridge.GetPersonalFaith(subject);
            if (!string.IsNullOrEmpty(officialFaith) && officialFaith == personalFaith) legitimacy += 10f;
            legitimacy += Math.Max(-10f, Math.Min(10f, (SuccessionReligionBridge.GetReligiousLegitimacy(subject) - 50f) * 0.2f));
            legitimacy += subject.Age < SuccessionResolver.AdultAge ? -15f : (subject.Age >= 25f ? 5f : 0f);
            if (IsCoronated(kingdom)) legitimacy += 15f;
            if (IsUnderageHeir(GetMinorHeir(kingdom))) legitimacy -= 5f;
            legitimacy = Math.Max(0f, Math.Min(100f, legitimacy));
            _legitimacyByKingdom[id] = legitimacy.ToString("0.0", CultureInfo.InvariantCulture);

            Hero pretender = SelectPretender(kingdom, subject, legitimacy);
            _pretenderByKingdom[id] = pretender == null ? string.Empty : pretender.StringId;
            foreach (Clan clan in kingdom.Clans)
            {
                if (clan == null || clan.IsClanTypeMercenary || clan.IsMinorFaction) continue;
                float support = legitimacy;
                if (subject.Clan == clan) support += 35f;
                if (clan.Culture == subject.Culture) support += 8f;
                Hero leader = clan.Leader;
                if (leader != null && !string.IsNullOrEmpty(officialFaith)
                    && SuccessionReligionBridge.GetPersonalFaith(leader) == officialFaith) support += 8f;
                if (subject.Clan != null)
                    support += Math.Max(-15f, Math.Min(15f, FactionManager.GetRelationBetweenClans(subject.Clan, clan) * 0.15f));
                support -= Math.Max(0, clan.Tier - 3) * 3f;
                ClanRecognition recognition = support >= 65f ? ClanRecognition.Recognized
                    : support >= 42f ? ClanRecognition.Neutral
                    : pretender != null ? ClanRecognition.SupportsPretender
                    : ClanRecognition.Opposed;
                _recognitionByRealmClan[RecognitionKey(kingdom, clan)] = recognition.ToString();
            }
        }

        private Hero SelectPretender(Kingdom kingdom, Hero subject, float legitimacy)
        {
            if (kingdom == null || legitimacy >= 80f) return null;
            return GetClaimants(kingdom)
                .Where(c => c.Hero != subject && c.Hero != GetRegent(kingdom) && c.Clan != kingdom.RulingClan && c.Clan.Tier >= 2)
                .Select(c => c.Hero)
                .FirstOrDefault();
        }

        internal Hero GetCivilWarPretender(Kingdom kingdom)
        {
            if (kingdom == null || kingdom.IsEliminated) return null;
            Hero pretender = GetPretender(kingdom);
            if (pretender != null) return pretender;
            return GetClaimants(kingdom).Where(c => c.Hero != kingdom.Leader && c.Clan != kingdom.RulingClan)
                .Select(c => c.Hero).FirstOrDefault();
        }

        private static string RecognitionKey(Kingdom kingdom, Clan clan)
        {
            return kingdom == null || clan == null ? string.Empty : kingdom.StringId + ":" + clan.StringId;
        }

        private static int CurrentDay { get { return (int)Math.Floor(CampaignTime.Now.ToDays); } }

        private static Clan FindClan(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Clan.All.FirstOrDefault(c => c != null && c.StringId == id);
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrEmpty(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == id)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(h => h != null && h.StringId == id);
        }

        private static string Get(IDictionary<string, string> values, string key)
        {
            string value;
            return key != null && values.TryGetValue(key, out value) ? value : string.Empty;
        }

        private static void RunSafely(string operation, Action action)
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                SuccessionDiagnostics.Error(operation + " failed; the remaining succession systems will continue.", exception);
            }
        }

        private static string LawName(SuccessionLaw law)
        {
            switch (law)
            {
                case SuccessionLaw.MalePreferencePrimogeniture: return "male-preference primogeniture";
                case SuccessionLaw.AgnaticPrimogeniture: return "agnatic primogeniture";
                case SuccessionLaw.HouseSeniority: return "house seniority";
                case SuccessionLaw.NomadicHouseSeniority: return "nomadic house seniority";
                default: return "absolute primogeniture";
            }
        }
    }
}
