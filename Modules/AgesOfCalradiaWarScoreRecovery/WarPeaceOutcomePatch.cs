using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.WarScoreRecovery
{
    // Bannerlord 1.4.8 / protected AOC v1.5.11. Before the protected peace
    // handler resolves occupations, promote a clear net occupation advantage
    // to +/-100. Tied occupations preserve Core white-peace behavior.
    internal static class WarPeaceOutcomePatch
    {
        private static Type _ledgerType;
        private static FieldInfo _occupations;
        private static MethodInfo _setWarScore;
        private static MethodBase _peaceTarget;
        private static MethodBase _scoreTarget;
        private static MethodBase _concludeTarget;
        private static bool _reportedFailure;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null || _peaceTarget != null) return;
            try
            {
                Type ledgerType = AccessTools.TypeByName("TwelveMonthCalendar.CalendarWorldLedgerBehavior");
                FieldInfo occupations = ledgerType == null ? null : AccessTools.Field(ledgerType, "_warOccupations");
                MethodInfo setWarScore = ledgerType == null ? null : AccessTools.Method(ledgerType, "SetWarScore");
                MethodBase peaceTarget = ledgerType == null ? null : AccessTools.Method(ledgerType, "OnPeaceMade");
                MethodBase scoreTarget = ledgerType == null ? null : AccessTools.Method(ledgerType, "GetWarScore");
                MethodBase concludeTarget = ledgerType == null ? null : AccessTools.Method(ledgerType, "TryConcludeWar");
                if (!IsApprovedContract(ledgerType, occupations, setWarScore, peaceTarget, scoreTarget, concludeTarget))
                {
                    System.Diagnostics.Trace.WriteLine("AOC war-peace outcome: approved Core contract unavailable; Core white-peace behavior retained.");
                    return;
                }
                harmony.Patch(peaceTarget, prefix: Last(nameof(PeacePrefix)));
                try
                {
                    harmony.Patch(scoreTarget, postfix: Last(nameof(ScorePostfix)));
                    harmony.Patch(concludeTarget, prefix: Last(nameof(ConcludePrefix)));
                }
                catch
                {
                    harmony.Unpatch(peaceTarget, HarmonyPatchType.All, WarScoreRecoveryPatch.HarmonyId);
                    harmony.Unpatch(scoreTarget, HarmonyPatchType.All, WarScoreRecoveryPatch.HarmonyId);
                    throw;
                }
                _ledgerType = ledgerType; _occupations = occupations; _setWarScore = setWarScore;
                _peaceTarget = peaceTarget; _scoreTarget = scoreTarget; _concludeTarget = concludeTarget;
                System.Diagnostics.Trace.WriteLine("AOC war-peace outcome installed: net occupied fiefs decide early peace.");
            }
            catch (Exception ex)
            {
                Reset();
                System.Diagnostics.Trace.WriteLine("AOC war-peace outcome unavailable: " + ex);
            }
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (harmony != null)
            {
                if (_peaceTarget != null) harmony.Unpatch(_peaceTarget, HarmonyPatchType.All, WarScoreRecoveryPatch.HarmonyId);
                if (_scoreTarget != null) harmony.Unpatch(_scoreTarget, HarmonyPatchType.All, WarScoreRecoveryPatch.HarmonyId);
                if (_concludeTarget != null) harmony.Unpatch(_concludeTarget, HarmonyPatchType.All, WarScoreRecoveryPatch.HarmonyId);
            }
            Reset();
        }

        private static void PeacePrefix(object __instance, IFaction __0, IFaction __1, bool __runOriginal)
        {
            if (!__runOriginal) { WarScoreTrace.RecordSkipped("peace"); return; }
            Kingdom first = __0 as Kingdom;
            Kingdom second = __1 as Kingdom;
            int before;
            WarScoreRecoveryPatch.TryReadScore(first, second, out before);
            TryPromoteOutcome(__instance, first, second);
            int after;
            WarScoreRecoveryPatch.TryReadScore(first, second, out after);
            WarScoreTrace.RecordPeace(first, second, before, after, CalculateOutcome(__instance, first, second));
        }

        private static void ScorePostfix(Kingdom __0, Kingdom __1, ref int __result)
        {
            if (Math.Abs(__result) >= WarOccupationOutcome.SurrenderScore) return;
            int occupationScore = CalculateOutcome(ReadActiveLedger(), __0, __1);
            if (occupationScore != 0) __result = occupationScore;
        }

        private static void ConcludePrefix(Kingdom first, Kingdom second, ref bool surrender)
        {
            int current;
            if (WarScoreRecoveryPatch.TryReadScore(first, second, out current) && Math.Abs(current) >= WarOccupationOutcome.SurrenderScore) return;
            object ledger = ReadActiveLedger();
            int occupationScore = CalculateOutcome(ledger, first, second);
            if (occupationScore == 0) return;
            TryPromoteOutcome(ledger, first, second);
            surrender = true;
        }

        private static void TryPromoteOutcome(object ledger, Kingdom first, Kingdom second)
        {
            try
            {
                if (ledger == null || first == null || second == null) return;
                int current;
                if (WarScoreRecoveryPatch.TryReadScore(first, second, out current) && Math.Abs(current) >= WarOccupationOutcome.SurrenderScore) return;
                int occupationScore = CalculateOutcome(ledger, first, second);
                if (occupationScore != 0) _setWarScore.Invoke(ledger, new object[] { first, second, occupationScore });
            }
            catch (Exception ex) { ReportFailure(ex); }
        }

        private static int CalculateOutcome(object ledger, Kingdom first, Kingdom second)
        {
            try
            {
                if (first == null || second == null || _occupations == null) return 0;
                List<string> records = ledger == null ? null : _occupations.GetValue(ledger) as List<string>;
                return WarOccupationOutcome.Score(first.StringId, second.StringId, records, GetSettlementValue);
            }
            catch (Exception ex) { ReportFailure(ex); return 0; }
        }

        private static object ReadActiveLedger()
        {
            FieldInfo active = _ledgerType == null ? null : AccessTools.Field(_ledgerType, "_active");
            return active == null ? null : active.GetValue(null);
        }

        private static int? GetSettlementValue(string settlementId)
        {
            foreach (Settlement settlement in Settlement.All)
                if (settlement != null && string.Equals(settlement.StringId, settlementId, StringComparison.Ordinal))
                    return settlement.IsTown ? WarScoreRecoveryMath.TownCapturePoints : WarScoreRecoveryMath.CastleCapturePoints;
            return null;
        }

        private static bool IsApprovedContract(Type ledgerType, FieldInfo occupations, MethodInfo setWarScore, MethodBase peace, MethodBase score, MethodBase conclude)
        {
            return WarScoreRecoveryPatch.IsApprovedLedger(ledgerType) && occupations != null && occupations.FieldType == typeof(List<string>)
                && HasSignature(setWarScore, typeof(void), typeof(Kingdom), typeof(Kingdom), typeof(int))
                && HasSignature(peace, typeof(void), typeof(IFaction), typeof(IFaction), typeof(TaleWorlds.CampaignSystem.Actions.MakePeaceAction.MakePeaceDetail))
                && HasSignature(score, typeof(int), typeof(Kingdom), typeof(Kingdom))
                && HasSignature(conclude, typeof(bool), typeof(Kingdom), typeof(Kingdom), typeof(bool), typeof(string).MakeByRefType());
        }

        private static bool HasSignature(MethodBase method, Type result, params Type[] parameters)
        {
            MethodInfo methodInfo = method as MethodInfo;
            if (methodInfo == null || methodInfo.ReturnType != result) return false;
            ParameterInfo[] actual = method.GetParameters();
            if (actual.Length != parameters.Length) return false;
            for (int index = 0; index < actual.Length; index++) if (actual[index].ParameterType != parameters[index]) return false;
            return true;
        }

        private static void ReportFailure(Exception ex)
        {
            if (_reportedFailure) return;
            _reportedFailure = true;
            System.Diagnostics.Trace.WriteLine("AOC war-peace outcome failed safely; Core white-peace behavior retained: " + ex);
        }

        private static HarmonyMethod Last(string methodName)
        {
            HarmonyMethod method = new HarmonyMethod(typeof(WarPeaceOutcomePatch), methodName);
            method.priority = Priority.Last;
            return method;
        }

        private static void Reset()
        {
            _ledgerType = null; _occupations = null; _setWarScore = null;
            _peaceTarget = null; _scoreTarget = null; _concludeTarget = null; _reportedFailure = false;
        }
    }
}
