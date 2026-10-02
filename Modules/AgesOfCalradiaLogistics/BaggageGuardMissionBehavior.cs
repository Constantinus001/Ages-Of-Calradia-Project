using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Detaches a small existing infantry formation to hold around the central
    /// wagon. No troops are created; guard strength is paid from the army.
    /// </summary>
    public sealed class BaggageGuardMissionBehavior : MissionLogic
    {
        private const float SetupDelaySeconds = 4f;
        private const int MaximumGuardCount = 8;
        private float _elapsed;
        private bool _configured;

        public override void OnMissionTick(float dt)
        {
            if (_configured)
            {
                return;
            }

            _elapsed += dt;
            if (_elapsed < SetupDelaySeconds || !Mission.IsFieldBattle)
            {
                return;
            }

            _configured = true;
            ConfigureGuards(BattleSideEnum.Attacker);
            ConfigureGuards(BattleSideEnum.Defender);
        }

        private void ConfigureGuards(BattleSideEnum side)
        {
            BaggageTrainLocation train;
            if (!BaggageTrainRegistry.TryGet(side, out train))
            {
                return;
            }

            Team team = Mission.Teams.FirstOrDefault(candidate => candidate.Side == side);
            if (team == null)
            {
                return;
            }

            Formation source = team.FormationsIncludingEmpty
                .Where(formation => formation.CountOfDetachableNonPlayerUnits > 0
                    && !formation.HasPlayerControlledTroop
                    && (formation.LogicalClass == FormationClass.Infantry
                        || formation.LogicalClass == FormationClass.HeavyInfantry))
                .OrderByDescending(formation => formation.CountOfDetachableNonPlayerUnits)
                .FirstOrDefault();
            Formation guard = team.FormationsIncludingEmpty
                .FirstOrDefault(formation => formation != source && formation.CountOfUnits == 0);
            if (source == null || guard == null)
            {
                LogisticsDiagnostics.Warning("Could not allocate a baggage guard formation for " + side + ".");
                return;
            }

            int guardCount = System.Math.Min(MaximumGuardCount, source.CountOfDetachableNonPlayerUnits);
            if (guardCount <= 0)
            {
                return;
            }

            source.TransferUnits(guard, guardCount);
            WorldPosition guardPosition = new WorldPosition(Mission.Scene, train.Position);
            guard.SetMovementOrder(MovementOrder.MovementOrderMove(guardPosition));
            guard.SetControlledByAI(isControlledByAI: true);
            LogisticsDiagnostics.Info(
                string.Format(
                    "Assigned {0} existing {1} troops as baggage guards.",
                    guardCount,
                    side));
        }
    }
}
