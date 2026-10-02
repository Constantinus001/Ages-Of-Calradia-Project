using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Persistent, campaign-side reserve store. Battle behaviours will spend from
    /// this store instead of creating unlimited ammunition.
    /// </summary>
    public sealed class LogisticsReserveBehavior : CampaignBehaviorBase
    {
        public const int MaximumReserve = LogisticsSupplyMath.MaximumReserve;
        public const int ReservePerSupplyCrate = LogisticsSupplyMath.ReservePerSupplyCrate;

        private const int CurrentSchemaVersion = 1;
        private const int StartingReserve = 20;
        internal const string SupplyItemId = "aoc_logistics_supply";
        private const string ReserveStateKey = "aoc_logistics_reserves";
        private const string DebtStateKey = "aoc_logistics_supply_debt";
        private const string LastProcessedDayStateKey = "aoc_logistics_last_supply_day";
        private const string SchemaVersionStateKey = "aoc_logistics_schema_version";
        private Dictionary<string, int> _reservesByPartyId = new Dictionary<string, int>();
        private Dictionary<string, float> _fractionalDebtByPartyId = new Dictionary<string, float>();
        private Dictionary<string, int> _lastProcessedDayByPartyId = new Dictionary<string, int>();
        public static LogisticsReserveBehavior Active { get; private set; }

        public LogisticsReserveBehavior()
        {
            Active = this;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, ProcessDailySupply);
            CampaignEvents.OnPartyRemovedEvent.AddNonSerializedListener(this, RemovePartyReserve);
            CampaignEvents.DailyTickTownEvent.AddNonSerializedListener(this, RestockTownMarket);
        }

        public override void SyncData(IDataStore dataStore)
        {
            int schemaVersion = dataStore.IsLoading ? 0 : CurrentSchemaVersion;
            dataStore.SyncData(ReserveStateKey, ref _reservesByPartyId);
            dataStore.SyncData(DebtStateKey, ref _fractionalDebtByPartyId);
            dataStore.SyncData(LastProcessedDayStateKey, ref _lastProcessedDayByPartyId);
            dataStore.SyncData(SchemaVersionStateKey, ref schemaVersion);
            NormalizeLoadedState(schemaVersion);
        }

        public int GetReserve(MobileParty party)
        {
            if (!IsEligible(party))
            {
                return 0;
            }

            EnsureEligiblePartyHasReserve(party);
            return _reservesByPartyId[party.StringId];
        }

        public float GetDailyUse(MobileParty party)
        {
            return party == null
                ? 0f
                : LogisticsSupplyMath.CalculateDailyUse(party.Party.NumberOfHealthyMembers);
        }

        internal int ReadExistingReserve(string partyId)
        {
            int value;
            return !string.IsNullOrWhiteSpace(partyId) && _reservesByPartyId.TryGetValue(partyId,out value) ? value : -1;
        }

        public float GetProjectedDays(MobileParty party)
        {
            return LogisticsSupplyMath.CalculateDaysRemaining(GetReserve(party), GetDailyUse(party));
        }

        public float GetSpeedFactor(MobileParty party)
        {
            return IsEligible(party)
                ? LogisticsSupplyMath.GetSpeedFactor(GetReserve(party), GetDailyUse(party))
                : 1f;
        }

        /// <summary>
        /// Converts Supply crates in the party inventory into reserve capacity.
        /// The caller supplies the intended number of crates, enabling a later
        /// menu or quartermaster interaction to ask for player confirmation.
        /// </summary>
        public int LoadSupplyCrates(MobileParty party, int requestedCrates)
        {
            if (!IsEligible(party) || requestedCrates <= 0)
            {
                return 0;
            }

            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(SupplyItemId);
            if (supply == null)
            {
                return 0;
            }

            EnsureEligiblePartyHasReserve(party);
            int capacity = MaximumReserve - _reservesByPartyId[party.StringId];
            int cratesToUse = System.Math.Min(requestedCrates, party.ItemRoster.GetItemNumber(supply));
            cratesToUse = System.Math.Min(cratesToUse, capacity / ReservePerSupplyCrate);
            if (cratesToUse <= 0)
            {
                return 0;
            }

            party.ItemRoster.AddToCounts(supply, -cratesToUse);
            _reservesByPartyId[party.StringId] += cratesToUse * ReservePerSupplyCrate;
            LogisticsDiagnostics.Info(string.Format("Loaded {0} Supply crate(s) into {1}; reserve is now {2}/{3}.", cratesToUse, party.StringId, _reservesByPartyId[party.StringId], MaximumReserve));
            return cratesToUse;
        }

        public int LoadAllAvailableSupplyCrates(MobileParty party)
        {
            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(SupplyItemId);
            return supply == null ? 0 : LoadSupplyCrates(party, party.ItemRoster.GetItemNumber(supply));
        }

        public bool TryConsumeReserve(MobileParty party, int amount)
        {
            if (!IsEligible(party) || amount <= 0 || GetReserve(party) < amount)
            {
                return false;
            }

            _reservesByPartyId[party.StringId] -= amount;
            return true;
        }

        internal void AddPurchasedSupplyCrates(MobileParty party, int crates)
        {
            if (!IsEligible(party) || crates <= 0)
            {
                return;
            }

            EnsureEligiblePartyHasReserve(party);
            _reservesByPartyId[party.StringId] = LogisticsSupplyMath.ClampReserve(
                _reservesByPartyId[party.StringId] + crates * ReservePerSupplyCrate);
        }

        public bool TryConsumeReserve(IEnumerable<MobileParty> parties, int amount)
        {
            if (parties == null || amount <= 0)
            {
                return false;
            }

            List<MobileParty> contributors = parties
                .Where(IsEligible)
                .Distinct()
                .OrderBy(party => party.StringId, StringComparer.Ordinal)
                .ToList();
            return TryConsumeReserve(contributors.ToArray(), amount);
        }

        /// <summary>
        /// Debits a mission-captured, pre-sorted coalition without allocating or
        /// reordering the contributor list for each ammunition transfer.
        /// </summary>
        internal bool TryConsumeReserve(MobileParty[] contributors, int amount)
        {
            int ignoredCursor = 0;
            return TryConsumeReserve(contributors, amount, ref ignoredCursor);
        }

        /// <summary>
        /// Debits a side-local battle ledger in round-robin order. Each source
        /// party therefore contributes before a coalition leader is exhausted.
        /// </summary>
        internal bool TryConsumeReserve(MobileParty[] contributors, int amount, ref int nextContributorIndex)
        {
            if (contributors == null || amount <= 0)
            {
                return false;
            }

            for (int candidateIndex = 0; candidateIndex < contributors.Length; candidateIndex++)
            {
                if (!IsEligible(contributors[candidateIndex]))
                {
                    return false;
                }
            }

            int available = 0;
            for (int availableIndex = 0; availableIndex < contributors.Length && available < amount; availableIndex++)
            {
                available += GetReserve(contributors[availableIndex]);
            }

            if (available < amount)
            {
                return false;
            }

            int remaining = amount;
            int index = contributors.Length == 0 ? 0 : nextContributorIndex % contributors.Length;
            while (remaining > 0)
            {
                MobileParty contributor = contributors[index];
                int reserve = _reservesByPartyId[contributor.StringId];
                if (reserve > 0)
                {
                    _reservesByPartyId[contributor.StringId] = reserve - 1;
                    remaining--;
                }

                index = (index + 1) % contributors.Length;
            }

            nextContributorIndex = index;

            return remaining == 0;
        }

        public static bool IsEligible(MobileParty party)
        {
            return party != null && party.IsActive && !party.IsBandit &&
                (party == MobileParty.MainParty || party.IsLordParty || party.IsCaravan);
        }

        private void EnsureEligiblePartyHasReserve(MobileParty party)
        {
            if (IsEligible(party) && !_reservesByPartyId.ContainsKey(party.StringId))
            {
                _reservesByPartyId.Add(party.StringId, StartingReserve);
                LogisticsDiagnostics.Info(string.Format("Created reserve for {0}: {1}/{2}.", party.StringId, StartingReserve, MaximumReserve));
            }

            if (IsEligible(party))
            {
                if (!_fractionalDebtByPartyId.ContainsKey(party.StringId))
                {
                    _fractionalDebtByPartyId[party.StringId] = 0f;
                }

                if (!_lastProcessedDayByPartyId.ContainsKey(party.StringId))
                {
                    _lastProcessedDayByPartyId[party.StringId] = CurrentDay;
                }
            }
        }

        private void ProcessDailySupply(MobileParty party)
        {
            if (!IsEligible(party))
            {
                return;
            }

            EnsureEligiblePartyHasReserve(party);
            string partyId = party.StringId;
            int elapsedDays = LogisticsSupplyMath.CalculateElapsedDays(
                _lastProcessedDayByPartyId[partyId],
                CurrentDay);
            if (elapsedDays > 0)
            {
                int remainingReserve;
                float remainingDebt;
                LogisticsSupplyMath.AdvanceReserve(
                    _reservesByPartyId[partyId],
                    _fractionalDebtByPartyId[partyId],
                    IsProvisionedBySettlement(party) ? 0f : GetDailyUse(party),
                    elapsedDays,
                    out remainingReserve,
                    out remainingDebt);
                _reservesByPartyId[partyId] = remainingReserve;
                _fractionalDebtByPartyId[partyId] = remainingDebt;
                _lastProcessedDayByPartyId[partyId] = CurrentDay;
            }

            TryProcureAiSupply(party);
        }

        private void TryProcureAiSupply(MobileParty party)
        {
            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(SupplyItemId);
            int purchasedCrates = LogisticsAiProcurementService.TryPurchase(
                party,
                supply,
                _reservesByPartyId[party.StringId],
                GetDailyUse(party));
            if (purchasedCrates <= 0)
            {
                return;
            }

            _reservesByPartyId[party.StringId] = LogisticsSupplyMath.ClampReserve(
                _reservesByPartyId[party.StringId] + purchasedCrates * ReservePerSupplyCrate);
            LogisticsDiagnostics.Info(string.Format(
                "AI party {0} bought {1} Supply crate(s); reserve is now {2}/{3}.",
                party.StringId,
                purchasedCrates,
                _reservesByPartyId[party.StringId],
                MaximumReserve));
        }

        private void RemovePartyReserve(PartyBase party)
        {
            if (party != null && party.MobileParty != null)
            {
                _reservesByPartyId.Remove(party.MobileParty.StringId);
                _fractionalDebtByPartyId.Remove(party.MobileParty.StringId);
                _lastProcessedDayByPartyId.Remove(party.MobileParty.StringId);
            }
        }

        private void RestockTownMarket(Town town)
        {
            if (town == null || !town.IsTown || town.Owner == null)
            {
                return;
            }

            ItemObject supply = MBObjectManager.Instance.GetObject<ItemObject>(SupplyItemId);
            if (supply == null)
            {
                return;
            }

            if (town.Settlement.IsUnderSiege || town.Settlement.IsStarving
                || town.Security < 20f || CurrentDay % 7 != GetRestockDay(town.StringId))
            {
                return;
            }

            int desiredStock = Math.Max(1, Math.Min(5, 1 + (int)(town.Prosperity / 2000f)));
            int currentStock = town.Settlement.ItemRoster.GetItemNumber(supply);
            if (currentStock < desiredStock)
            {
                int produced = Math.Min(
                    desiredStock - currentStock,
                    Math.Max(1, 1 + (int)(town.Prosperity / 4000f)));
                LogisticsSupplyMarketService.Produce(town.Settlement, supply, produced);
                LogisticsDiagnostics.Info(string.Format(
                    "Produced {0} Supply crate(s) in {1}: {2}/{3}.",
                    produced,
                    town.Name,
                    currentStock + produced,
                    desiredStock));
            }
        }

        private void NormalizeLoadedState(int loadedSchemaVersion)
        {
            if (_reservesByPartyId == null) _reservesByPartyId = new Dictionary<string, int>();
            if (_fractionalDebtByPartyId == null) _fractionalDebtByPartyId = new Dictionary<string, float>();
            if (_lastProcessedDayByPartyId == null) _lastProcessedDayByPartyId = new Dictionary<string, int>();

            foreach (string partyId in new List<string>(_reservesByPartyId.Keys))
            {
                _reservesByPartyId[partyId] = LogisticsSupplyMath.ClampReserve(_reservesByPartyId[partyId]);
            }

            foreach (string partyId in new List<string>(_fractionalDebtByPartyId.Keys))
            {
                if (!_reservesByPartyId.ContainsKey(partyId))
                {
                    _fractionalDebtByPartyId.Remove(partyId);
                }
                else
                {
                    _fractionalDebtByPartyId[partyId] = LogisticsSupplyMath.ClampDebt(
                        _fractionalDebtByPartyId[partyId]);
                }
            }

            foreach (string partyId in new List<string>(_lastProcessedDayByPartyId.Keys))
            {
                if (!_reservesByPartyId.ContainsKey(partyId))
                {
                    _lastProcessedDayByPartyId.Remove(partyId);
                }
            }

            if (loadedSchemaVersion < CurrentSchemaVersion && _reservesByPartyId.Count > 0)
            {
                LogisticsDiagnostics.Info(string.Format(
                    "Migrated {0} legacy logistics reserve record(s) to schema {1}; existing reserve values were preserved and clamped.",
                    _reservesByPartyId.Count,
                    CurrentSchemaVersion));
            }
        }

        private static int GetRestockDay(string townId)
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(townId))
            {
                for (int index = 0; index < townId.Length; index++)
                {
                    hash = unchecked(hash * 31 + townId[index]);
                }
            }

            return (hash & int.MaxValue) % 7;
        }

        private static bool IsProvisionedBySettlement(MobileParty party)
        {
            return party != null && party.CurrentSettlement != null
                && party.CurrentSettlement.IsFortification;
        }

        private static int CurrentDay
        {
            get { return (int)Math.Floor(CampaignTime.Now.ToDays); }
        }
    }
}
