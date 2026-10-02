using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Captures an undefended baggage train after sustained enemy occupation.
    /// Capture is mission-local, happens once, disables resupply, and burns a
    /// bounded quarter of the defending side's remaining reserve.
    /// </summary>
    public sealed class BaggageTrainRaidMissionBehavior : MissionLogic
    {
        private const float CaptureRadiusMeters = 8f;
        private const float CaptureSeconds = 12f;
        private const float CheckIntervalSeconds = 1f;
        private readonly Dictionary<BattleSideEnum, float> _captureProgressBySide =
            new Dictionary<BattleSideEnum, float>();
        private readonly Dictionary<BattleSideEnum, MobileParty[]> _reservePartiesBySide =
            new Dictionary<BattleSideEnum, MobileParty[]>();
        private readonly Dictionary<BattleSideEnum, int> _nextContributorIndexBySide =
            new Dictionary<BattleSideEnum, int>();
        private float _elapsed;

        public override void AfterStart()
        {
            base.AfterStart();
            MapEvent battle = PlayerEncounter.Battle;
            if (battle == null || !Mission.IsFieldBattle)
            {
                return;
            }

            CaptureContributors(battle, BattleSideEnum.Attacker);
            CaptureContributors(battle, BattleSideEnum.Defender);
        }

        public override void OnMissionTick(float dt)
        {
            _elapsed += dt;
            if (_elapsed < CheckIntervalSeconds)
            {
                return;
            }

            _elapsed = 0f;
            CheckTrain(BattleSideEnum.Attacker);
            CheckTrain(BattleSideEnum.Defender);
        }

        private void CaptureContributors(MapEvent battle, BattleSideEnum side)
        {
            _reservePartiesBySide[side] = battle.PartiesOnSide(side)
                .Where(mapParty => mapParty.Party != null
                    && LogisticsReserveBehavior.IsEligible(mapParty.Party.MobileParty))
                .Select(mapParty => mapParty.Party.MobileParty)
                .Distinct()
                .OrderBy(party => party.StringId, StringComparer.Ordinal)
                .ToArray();
        }

        private void CheckTrain(BattleSideEnum defendingSide)
        {
            BaggageTrainLocation train;
            if (!BaggageTrainRegistry.TryGet(defendingSide, out train) || train.IsCaptured)
            {
                return;
            }

            BattleSideEnum attackingSide = defendingSide == BattleSideEnum.Attacker
                ? BattleSideEnum.Defender
                : BattleSideEnum.Attacker;
            Team defenders = Mission.Teams.FirstOrDefault(team => team.Side == defendingSide);
            Team attackers = Mission.Teams.FirstOrDefault(team => team.Side == attackingSide);
            if (defenders == null || attackers == null)
            {
                return;
            }

            float radiusSquared = CaptureRadiusMeters * CaptureRadiusMeters;
            bool attackersPresent = attackers.ActiveAgents.Any(agent => agent.Health > 0f
                && agent.Position.DistanceSquared(train.Position) <= radiusSquared);
            bool defendersPresent = defenders.ActiveAgents.Any(agent => agent.Health > 0f
                && agent.Position.DistanceSquared(train.Position) <= radiusSquared);
            float progress = _captureProgressBySide.ContainsKey(defendingSide)
                ? _captureProgressBySide[defendingSide]
                : 0f;
            progress = attackersPresent && !defendersPresent
                ? progress + CheckIntervalSeconds
                : Math.Max(0f, progress - CheckIntervalSeconds);
            _captureProgressBySide[defendingSide] = progress;
            if (progress < CaptureSeconds || !train.TryCapture(attackingSide))
            {
                return;
            }

            BurnReserve(defendingSide);
            LogisticsDiagnostics.Warning("The " + defendingSide + " baggage train was captured by " + attackingSide + ".");
            if (Mission.PlayerTeam != null)
            {
                if (Mission.PlayerTeam.Side == defendingSide)
                {
                    InformationManager.DisplayMessage(new InformationMessage("Your baggage train has been captured. Resupply is disabled."));
                }
                else if (Mission.PlayerTeam.Side == attackingSide)
                {
                    InformationManager.DisplayMessage(new InformationMessage("Enemy baggage train captured. Their resupply is disabled."));
                }
            }
        }

        private void BurnReserve(BattleSideEnum defendingSide)
        {
            MobileParty[] contributors;
            LogisticsReserveBehavior reserves = LogisticsReserveBehavior.Active;
            if (reserves == null || !_reservePartiesBySide.TryGetValue(defendingSide, out contributors))
            {
                return;
            }

            int remainingReserve = contributors.Sum(reserves.GetReserve);
            int loss = Math.Max(1, remainingReserve / 4);
            int nextContributorIndex = _nextContributorIndexBySide.ContainsKey(defendingSide)
                ? _nextContributorIndexBySide[defendingSide]
                : 0;
            reserves.TryConsumeReserve(contributors, loss, ref nextContributorIndex);
            _nextContributorIndexBySide[defendingSide] = nextContributorIndex;
        }
    }
}
