using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaLogistics
{
    /// <summary>Mission-local train locations shared by supply, guard, and raid behaviours.</summary>
    public static class BaggageTrainRegistry
    {
        private static readonly Dictionary<BattleSideEnum, BaggageTrainLocation> Locations =
            new Dictionary<BattleSideEnum, BaggageTrainLocation>();

        public static void Register(BattleSideEnum side, Vec3 position, float radius, GameEntity entity)
        {
            Locations[side] = new BaggageTrainLocation(position, radius, entity);
        }

        public static bool TryGet(BattleSideEnum side, out BaggageTrainLocation location)
        {
            return Locations.TryGetValue(side, out location);
        }

        public static void Clear()
        {
            Locations.Clear();
        }
    }

    public sealed class BaggageTrainLocation
    {
        public BaggageTrainLocation(Vec3 position, float radius, GameEntity entity)
        {
            Position = position;
            Radius = radius;
            Entity = entity;
        }

        public Vec3 Position { get; private set; }
        public float Radius { get; private set; }
        public GameEntity Entity { get; private set; }
        public bool IsCaptured { get; private set; }
        public BattleSideEnum CapturingSide { get; private set; }

        internal bool TryCapture(BattleSideEnum capturingSide)
        {
            if (IsCaptured)
            {
                return false;
            }

            IsCaptured = true;
            CapturingSide = capturingSide;
            return true;
        }
    }
}
