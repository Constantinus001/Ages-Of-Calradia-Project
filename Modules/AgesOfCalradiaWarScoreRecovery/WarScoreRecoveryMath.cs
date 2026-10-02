using System;

namespace AgesOfCalradia.WarScoreRecovery
{
    internal static class WarScoreRecoveryMath
    {
        internal const int CastleCapturePoints = 15;
        internal const int TownCapturePoints = 25;
        internal const int MinimumBattlePoints = 3;
        internal const int MaximumBattlePoints = 10;
        internal const int TroopsPerBattlePoint = 200;
        internal const int BySiegeDetail = 1;

        internal static int BattlePoints(int losingSideStartingTroops)
        {
            int normalizedTroops = Math.Max(0, losingSideStartingTroops);
            return Math.Max(MinimumBattlePoints, Math.Min(MaximumBattlePoints,
                MinimumBattlePoints + normalizedTroops / TroopsPerBattlePoint));
        }

        internal static bool NeedsRecovery(int beforeScore, int afterScore)
        {
            // A score already at either clamp cannot visibly change and must not
            // receive a duplicate award.
            return beforeScore == afterScore && Math.Abs(beforeScore) < 100;
        }

        internal static bool IsSiegeCaptureDetail(int detail)
        {
            return detail == BySiegeDetail;
        }
    }
}
