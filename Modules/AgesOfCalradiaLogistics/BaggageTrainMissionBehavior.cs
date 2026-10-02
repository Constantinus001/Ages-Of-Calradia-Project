using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaLogistics
{
    /// <summary>
    /// Creates a static baggage train for each non-bandit campaign side in a
    /// player field battle. It deliberately uses Bannerlord's own caravan prop
    /// and pack animals so no custom scene assets are required.
    /// </summary>
    public sealed class BaggageTrainMissionBehavior : MissionLogic
    {
        private const int SupplyRadiusMeters = 6;
        private const float RearDeploymentDistanceMeters = 20f;
        private const int MinimumWagonCount = 2;
        private const int MaximumWagonCount = 6;
        private const float WagonSpacingMeters = 12f;
        private const float WagonRowDepthMeters = 7f;
        private const string GroundSupplyPrefabName = "caravan_scattered_goods_prop";
        private static readonly string[] IntactWagonPrefabNames =
        {
            "bd_cart_a",
            "bd_cart_b",
            "bd_cart_c",
            "bd_cart_heap_a",
            "bd_cart_heap_b",
            "bd_cart_heap_c",
            "bd_cart_heap_d",
            "bd_cart_heap_e",
            "bd_cart_heap_f",
            "bd_cart_heap_h",
            "bd_cart_heap_l",
            "bd_hay_cart_a",
            "bd_hay_cart_b",
            "olive_cart_a"
        };

        public override void AfterStart()
        {
            base.AfterStart();

            MapEvent battle = PlayerEncounter.Battle;
            if (battle == null || !Mission.IsFieldBattle)
            {
                LogisticsDiagnostics.Info("Skipped baggage train: no player map event or not a field battle.");
                return;
            }

            TrySpawnForSide(battle, BattleSideEnum.Attacker);
            TrySpawnForSide(battle, BattleSideEnum.Defender);
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            BaggageTrainRegistry.Clear();
        }

        private void TrySpawnForSide(MapEvent battle, BattleSideEnum side)
        {
            try
            {
                SpawnForSideCore(battle, side);
            }
            catch (Exception exception)
            {
                // Native scene/prefab loading is version-sensitive. A failed
                // visual train leaves the battle playable with no registry
                // entry, and therefore no resupply or guard order for that side.
                LogisticsDiagnostics.Error(
                    "Skipped baggage train for " + side + " after native scene/prefab failure.",
                    exception);
            }
        }

        private void SpawnForSideCore(MapEvent battle, BattleSideEnum side)
        {
            if (!SideHasEligibleParty(battle, side))
            {
                LogisticsDiagnostics.Info("Skipped baggage train for " + side + ": no eligible non-bandit party.");
                return;
            }

            int troopCount = battle.PartiesOnSide(side).Sum(mapParty => mapParty.Party.NumberOfHealthyMembers);
            int reserve = CalculateSideReserve(battle, side);
            int wagonCount = CalculateWagonCount(troopCount, reserve);
            int groundSupplyPileCount = Math.Max(1, wagonCount / 2);
            float pathOffset = Mission.ComputeSpawnPathDeploymentOffset(
                (int)(troopCount * 1.5f),
                Mission.GetInitialSpawnPath());
            LogisticsDiagnostics.Info(
                string.Format(
                    "Preparing {0} baggage train: troops={1}, deploymentOffset={2:F1}.",
                    side,
                    troopCount,
                    pathOffset));

            WorldFrame frame = Mission.GetSpawnPathFrame(side, pathOffset);
            if (!frame.IsValid)
            {
                LogisticsDiagnostics.Warning("Skipped baggage train for " + side + ": no valid spawn-path frame.");
                return;
            }

            Vec3 deploymentPosition = frame.Origin.GetGroundVec3();
            Vec3 groundPosition = deploymentPosition - frame.Rotation.f * RearDeploymentDistanceMeters;
            Mission.Scene.GetTerrainHeightAndNormal(groundPosition.AsVec2, out float terrainHeight, out Vec3 terrainNormal);
            groundPosition.z = terrainHeight;
            GameEntity train = null;
            for (int wagonIndex = 0; wagonIndex < wagonCount; wagonIndex++)
            {
                float seed = (wagonIndex + 1) * 17f + (side == BattleSideEnum.Attacker ? 3f : 11f);
                int column = wagonIndex / 2;
                float centeredColumn = column - ((wagonCount / 2 - 1) / 2f);
                float lineOffset = centeredColumn * WagonSpacingMeters;
                float rowDepth = (wagonIndex % 2 == 0 ? -WagonRowDepthMeters : WagonRowDepthMeters);
                float depthJitter = (seed % 5f - 2f) * 0.7f;
                Vec3 wagonPosition = groundPosition + frame.Rotation.s * lineOffset + frame.Rotation.f * (rowDepth + depthJitter);
                Mission.Scene.GetTerrainHeightAndNormal(wagonPosition.AsVec2, out float wagonHeight, out Vec3 wagonNormal);
                wagonPosition.z = wagonHeight;
                Mat3 wagonRotation = frame.Rotation;
                wagonRotation.RotateAboutUp((seed % 31f - 15f) * 0.035f);
                MatrixFrame wagonFrame = new MatrixFrame(wagonRotation, wagonPosition);
                string wagonPrefab = IntactWagonPrefabNames[
                    (wagonIndex + (side == BattleSideEnum.Attacker ? 0 : 3)) % IntactWagonPrefabNames.Length];
                GameEntity wagon = GameEntity.Instantiate(Mission.Scene, wagonPrefab, wagonFrame);
                if (wagonIndex == wagonCount / 2) train = wagon;
            }

            for (int pileIndex = 0; pileIndex < groundSupplyPileCount; pileIndex++)
            {
                float seed = (pileIndex + 1) * 23f + (side == BattleSideEnum.Attacker ? 7f : 13f);
                float lateralOffset = (seed % 67f) - 33f;
                float depthOffset = (seed % 17f) - 8f;
                Vec3 pilePosition = groundPosition + frame.Rotation.s * lateralOffset + frame.Rotation.f * depthOffset;
                Mission.Scene.GetTerrainHeightAndNormal(pilePosition.AsVec2, out float pileHeight, out Vec3 pileNormal);
                pilePosition.z = pileHeight;
                Mat3 pileRotation = frame.Rotation;
                pileRotation.RotateAboutUp((seed % 31f - 15f) * 0.04f);
                GameEntity.Instantiate(
                    Mission.Scene,
                    GroundSupplyPrefabName,
                    new MatrixFrame(pileRotation, pilePosition));
            }

            if (train == null)
            {
                LogisticsDiagnostics.Warning("Skipped baggage train for " + side + ": forced wagon prefabs were unavailable.");
                return;
            }

            BaggageTrainRegistry.Register(side, groundPosition, SupplyRadiusMeters, train);
            LogisticsDiagnostics.Info(string.Format(
                "Spawned {0}-wagon, {1}-pile {2} baggage convoy for {3} troops and {4} reserve at ({5:F1}, {6:F1}, {7:F1}), radius={8}m.",
                wagonCount,
                groundSupplyPileCount,
                side,
                troopCount,
                reserve,
                groundPosition.x,
                groundPosition.y,
                groundPosition.z,
                SupplyRadiusMeters));
        }

        private bool SideHasEligibleParty(MapEvent battle, BattleSideEnum side)
        {
            return battle.PartiesOnSide(side).Any(mapParty =>
                mapParty.Party != null &&
                LogisticsReserveBehavior.IsEligible(mapParty.Party.MobileParty));
        }

        private static int CalculateSideReserve(MapEvent battle, BattleSideEnum side)
        {
            LogisticsReserveBehavior reserves = LogisticsReserveBehavior.Active;
            if (reserves == null)
            {
                return 0;
            }

            return battle.PartiesOnSide(side)
                .Where(mapParty => mapParty.Party != null
                    && LogisticsReserveBehavior.IsEligible(mapParty.Party.MobileParty))
                .Sum(mapParty => reserves.GetReserve(mapParty.Party.MobileParty));
        }

        private static int CalculateWagonCount(int troopCount, int reserve)
        {
            int scale = Math.Max(troopCount / 100, reserve / 25);
            return Math.Max(MinimumWagonCount, Math.Min(MaximumWagonCount, MinimumWagonCount + scale));
        }

    }
}
