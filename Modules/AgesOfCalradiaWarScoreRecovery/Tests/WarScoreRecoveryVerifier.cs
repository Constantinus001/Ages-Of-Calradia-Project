using System;
using AgesOfCalradia.WarScoreRecovery;
public static class WarScoreRecoveryVerifier
{
    public static string Run()
    {
        Check(WarScoreRecoveryMath.BattlePoints(-1) == 3, "negative troop count clamps");
        Check(WarScoreRecoveryMath.BattlePoints(0) == 3, "small battle floor");
        Check(WarScoreRecoveryMath.BattlePoints(799) == 6, "battle step calculation");
        Check(WarScoreRecoveryMath.BattlePoints(99999) == 10, "battle cap");
        Check(WarScoreRecoveryMath.IsSiegeCaptureDetail(1), "siege capture detail accepted");
        Check(!WarScoreRecoveryMath.IsSiegeCaptureDetail(4), "king decision detail rejected");
        string[] occupations = { "a|b\tcastle\told\ta", "a|b\ttown\told\ta", "other|war\ttown\told\tother", "a|b\tbroken" };
        Func<string, int?> value = id => id == "town" ? (int?)25 : (id != null && id.StartsWith("castle", StringComparison.Ordinal) ? (int?)15 : null);
        Check(WarOccupationOutcome.Score("a", "b", occupations, value) == 100, "net occupation decides surrender");
        Check(WarOccupationOutcome.Score("b", "a", occupations, value) == -100, "occupation sign follows caller perspective");
        Check(WarOccupationOutcome.Score("a", "b", new[] { "a|b\tcastle-a\told\ta", "a|b\tcastle-b\told\tb" }, value) == 0, "equal occupations remain white peace");
        Check(WarOccupationOutcome.Score("a", "b", new[] { "a|b\ttown\told\ta", "a|b\ttown\told\ta" }, value) == 100, "duplicated fief record cannot inflate outcome");
        Check(WarOccupationOutcome.Score("a", "b", new[] { "a|b\tunknown\told\ta" }, value) == 0, "missing settlement does not decide peace");
        string[] fullSequence = { "a|b\tcastle\told\ta" };
        Check(WarOccupationOutcome.Score("a", "b", fullSequence, value) == 100, "capture gives early-peace victor");
        Check(WarOccupationOutcome.Score("a", "b", fullSequence, value) == 100, "next-day persistence retains occupation outcome");
        Check(WarScoreRecoveryMath.NeedsRecovery(0, 0), "unchanged normal score recovers");
        Check(WarScoreRecoveryMath.NeedsRecovery(-99, -99), "unchanged non-clamped negative score recovers");
        Check(!WarScoreRecoveryMath.NeedsRecovery(0, 3), "normal Core award not duplicated");
        Check(!WarScoreRecoveryMath.NeedsRecovery(-30, -29), "opponent Core award not duplicated");
        Check(!WarScoreRecoveryMath.NeedsRecovery(100, 100), "positive cap not duplicated");
        Check(!WarScoreRecoveryMath.NeedsRecovery(-100, -100), "negative cap not duplicated");
        return "PASS: 19 war-score recovery contracts.";
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
}
