using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Refills depleted ammunition only while a friendly agent is in the train's
    /// marked range. Every three rounds transferred consumes one campaign
    /// reserve point; no reserve means no refill.
    /// </summary>
    public sealed class BaggageResupplyMissionBehavior : MissionLogic
    {
        private const float ResupplyIntervalSeconds = 3f;
        private const int RoundsPerReservePoint = 3;
        private float _elapsed;
        private float _summaryElapsed;
        private int _transferredRounds;
        private int _consumedReserve;
        private readonly Dictionary<BattleSideEnum, int> _nextContributorIndexBySide =
            new Dictionary<BattleSideEnum, int>();
        private readonly Dictionary<BattleSideEnum, MobileParty[]> _reservePartiesBySide =
            new Dictionary<BattleSideEnum, MobileParty[]>();

        public override void AfterStart()
        {
            base.AfterStart();

            MapEvent battle = PlayerEncounter.Battle;
            if (battle == null || !Mission.IsFieldBattle)
            {
                return;
            }

            _reservePartiesBySide[BattleSideEnum.Attacker] = GetReserveParties(battle, BattleSideEnum.Attacker);
            _reservePartiesBySide[BattleSideEnum.Defender] = GetReserveParties(battle, BattleSideEnum.Defender);
        }

        public override void OnMissionTick(float dt)
        {
            _elapsed += dt;
            if (_elapsed < ResupplyIntervalSeconds)
            {
                return;
            }

            _elapsed = 0f;
            ResupplySide(BattleSideEnum.Attacker);
            ResupplySide(BattleSideEnum.Defender);
            _summaryElapsed += ResupplyIntervalSeconds;
            if (_summaryElapsed >= 30f)
            {
                if (_consumedReserve > 0)
                {
                    LogisticsDiagnostics.Info(string.Format("Battle resupply summary: {0} round(s) transferred using {1} reserve point(s) in the last {2:F0}s.", _transferredRounds, _consumedReserve, _summaryElapsed));
                }
                _summaryElapsed = 0f;
                _transferredRounds = 0;
                _consumedReserve = 0;
            }
        }

        private void ResupplySide(BattleSideEnum side)
        {
            BaggageTrainLocation location;
            if (!BaggageTrainRegistry.TryGet(side, out location) || location.IsCaptured)
            {
                return;
            }

            MobileParty[] reserveParties;
            if (!_reservePartiesBySide.TryGetValue(side, out reserveParties))
            {
                return;
            }
            LogisticsReserveBehavior reserves = LogisticsReserveBehavior.Active;
            if (reserveParties.Length == 0 || reserves == null
                || reserveParties.Sum(party => reserves.GetReserve(party)) <= 0)
            {
                return;
            }

            Team team = Mission.Teams.FirstOrDefault(candidate => candidate.Side == side);
            if (team == null)
            {
                return;
            }

            float radiusSquared = location.Radius * location.Radius;
            foreach (Agent agent in team.ActiveAgents)
            {
                if (agent.Health <= 0f || agent.Position.DistanceSquared(location.Position) > radiusSquared)
                {
                    continue;
                }

                int nextContributorIndex = _nextContributorIndexBySide.ContainsKey(side)
                    ? _nextContributorIndexBySide[side]
                    : 0;
                if (!TryResupplyAgent(agent, reserveParties, reserves, ref nextContributorIndex))
                {
                    return;
                }

                _nextContributorIndexBySide[side] = nextContributorIndex;
            }
        }

        private static MobileParty[] GetReserveParties(MapEvent battle, BattleSideEnum side)
        {
            return battle.PartiesOnSide(side)
                .Where(mapParty => mapParty.Party != null
                    && LogisticsReserveBehavior.IsEligible(mapParty.Party.MobileParty))
                .Select(mapParty => mapParty.Party.MobileParty)
                .Distinct()
                .OrderBy(party => party.StringId, System.StringComparer.Ordinal)
                .ToArray();
        }

        private bool TryResupplyAgent(
            Agent agent,
            MobileParty[] reserveParties,
            LogisticsReserveBehavior reserves,
            ref int nextContributorIndex)
        {
            for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
                slot < EquipmentIndex.NumAllWeaponSlots;
                slot++)
            {
                MissionWeapon weapon = agent.Equipment[slot];
                if (weapon.IsEmpty || !weapon.IsAnyAmmo())
                {
                    continue;
                }

                short desiredAmount = weapon.ModifiedMaxAmount;
                if (desiredAmount > 1)
                {
                    desiredAmount--;
                }

                if (weapon.Amount >= desiredAmount)
                {
                    continue;
                }

                int transferAmount = System.Math.Min(RoundsPerReservePoint, desiredAmount - weapon.Amount);
                if (!reserves.TryConsumeReserve(reserveParties, 1, ref nextContributorIndex))
                {
                    return false;
                }

                agent.SetWeaponAmountInSlot(
                    slot,
                    (short)(weapon.Amount + transferAmount),
                    enforcePrimaryItem: false);
                _transferredRounds += transferAmount;
                _consumedReserve++;
            }

            return true;
        }
    }
}
