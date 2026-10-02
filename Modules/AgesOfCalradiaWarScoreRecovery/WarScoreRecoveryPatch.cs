using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.WarScoreRecovery
{
    // Bannerlord 1.4.8 / protected AOC v1.5.11 compatibility patch. The Core
    // handlers remain authoritative. This postfix supplies an award only when
    // their score did not move, preserving normal Core behavior and save data.
    internal static class WarScoreRecoveryPatch
    {
        internal const string HarmonyId = "AgesOfCalradia.WarScoreRecovery.v1";
        private const string LedgerTypeName = "TwelveMonthCalendar.CalendarWorldLedgerBehavior";
        private const string ApprovedCoreHash = "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private static Type _ledgerType;
        private static MethodInfo _readWarScore;
        private static MethodInfo _addWarScore;
        private static FieldInfo _activeLedger;
        private static FieldInfo _warScores;
        private static MethodBase _battleTarget;
        private static MethodBase _settlementTarget;
        private static int _settlementNewOwnerIndex = -1;
        private static int _settlementOldOwnerIndex = -1;
        private static int _settlementDetailIndex = -1;
        private static bool _reportedRuntimeFailure;

        internal static bool IsInstalled { get { return _battleTarget != null && _settlementTarget != null; } }

        internal static bool IsApprovedLedger(Type ledgerType)
        {
            return ledgerType != null && string.Equals(HashAssembly(ledgerType.Assembly), ApprovedCoreHash, StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetAuditText()
        {
            if (!IsInstalled || _activeLedger == null || _warScores == null) return "War-score recovery is inactive; no ledger audit is available.";
            try
            {
                object ledger = _activeLedger.GetValue(null);
                List<string> records = ledger == null ? null : _warScores.GetValue(ledger) as List<string>;
                if (records == null || records.Count == 0) return "War-score ledger is active with zero saved score records.";
                List<string> copy = new List<string>(records);
                copy.Sort(StringComparer.Ordinal);
                int visible = Math.Min(12, copy.Count);
                return "War-score ledger records=" + copy.Count + "; values=" + string.Join(" | ", copy.GetRange(0, visible).ToArray())
                    + (copy.Count > visible ? " | +" + (copy.Count - visible) + " more" : string.Empty);
            }
            catch (Exception ex)
            {
                ReportRuntimeFailure(ex);
                return "War-score ledger audit failed safely; see diagnostics.";
            }
        }

        // Diagnostics and the peace sidecar use the same private reader as the
        // recovery patch. A failed read is deliberately reported as unavailable
        // rather than inferred from a UI value.
        internal static bool TryReadScore(Kingdom first, Kingdom second, out int score)
        {
            score = 0;
            try
            {
                object ledger = _activeLedger == null ? null : _activeLedger.GetValue(null);
                if (!IsInstalled || ledger == null || first == null || second == null) return false;
                score = Read(ledger, first, second);
                return true;
            }
            catch (Exception ex) { ReportRuntimeFailure(ex); return false; }
        }

        internal sealed class ScoreState
        {
            internal Kingdom Winner;
            internal Kingdom Loser;
            internal int Before;
        }

        internal static void Install(Harmony harmony)
        {
            if (harmony == null || _battleTarget != null || _settlementTarget != null) return;
            try
            {
                Type ledgerType = AccessTools.TypeByName(LedgerTypeName);
                MethodInfo readWarScore = ledgerType == null ? null : AccessTools.Method(ledgerType, "ReadWarScore");
                MethodInfo addWarScore = ledgerType == null ? null : AccessTools.Method(ledgerType, "AddWarScore");
                FieldInfo activeLedger = ledgerType == null ? null : AccessTools.Field(ledgerType, "_active");
                FieldInfo warScores = ledgerType == null ? null : AccessTools.Field(ledgerType, "_warScores");
                MethodBase battleTarget = ledgerType == null ? null : AccessTools.Method(ledgerType, "OnMapEventEndedForWarScore");
                MethodBase settlementTarget = ledgerType == null ? null : AccessTools.Method(ledgerType, "OnSettlementOwnerChanged");
                if (!IsApprovedContract(ledgerType, readWarScore, addWarScore, activeLedger, warScores, battleTarget, settlementTarget))
                {
                    System.Diagnostics.Trace.WriteLine("AOC war-score recovery: approved v1.5.11 contract unavailable; no patch applied.");
                    return;
                }

                HarmonyMethod battlePrefix = Last(typeof(WarScoreRecoveryPatch), nameof(BattlePrefix));
                HarmonyMethod battlePostfix = Last(typeof(WarScoreRecoveryPatch), nameof(BattlePostfix));
                HarmonyMethod settlementPrefix = Last(typeof(WarScoreRecoveryPatch), nameof(SettlementPrefix));
                HarmonyMethod settlementPostfix = Last(typeof(WarScoreRecoveryPatch), nameof(SettlementPostfix));
                harmony.Patch(battleTarget, prefix: battlePrefix, postfix: battlePostfix);
                try
                {
                    harmony.Patch(settlementTarget, prefix: settlementPrefix, postfix: settlementPostfix);
                }
                catch
                {
                    harmony.Unpatch(battleTarget, HarmonyPatchType.All, HarmonyId);
                    throw;
                }
                _ledgerType = ledgerType; _readWarScore = readWarScore; _addWarScore = addWarScore;
                _activeLedger = activeLedger; _warScores = warScores;
                _battleTarget = battleTarget; _settlementTarget = settlementTarget;
                _settlementNewOwnerIndex = FindParameterIndex(settlementTarget, "newOwner");
                _settlementOldOwnerIndex = FindParameterIndex(settlementTarget, "oldOwner");
                _settlementDetailIndex = FindParameterIndex(settlementTarget, "detail");
                System.Diagnostics.Trace.WriteLine("AOC war-score recovery installed for protected v1.5.11 ledger handlers.");
            }
            catch (Exception ex)
            {
                Reset();
                // Reflection is a version-sensitive integration boundary. Native
                // behavior stays intact if the protected Core changes.
                System.Diagnostics.Trace.WriteLine("AOC war-score recovery unavailable: " + ex);
            }
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (_battleTarget != null) harmony.Unpatch(_battleTarget, HarmonyPatchType.All, HarmonyId);
            if (_settlementTarget != null) harmony.Unpatch(_settlementTarget, HarmonyPatchType.All, HarmonyId);
            Reset();
        }

        private static void Reset()
        {
            _ledgerType = null; _readWarScore = null; _addWarScore = null; _activeLedger = null; _warScores = null;
            _battleTarget = null; _settlementTarget = null;
            _settlementNewOwnerIndex = -1; _settlementOldOwnerIndex = -1; _settlementDetailIndex = -1;
            _reportedRuntimeFailure = false;
        }

        private static void BattlePrefix(object __instance, MapEvent __0, bool __runOriginal, out ScoreState __state)
        {
            if (!__runOriginal) { __state = null; WarScoreTrace.RecordSkipped("battle"); return; }
            try { __state = CreateBattleState(__instance, __0); }
            catch (Exception ex) { __state = null; ReportRuntimeFailure(ex); }
        }

        private static void BattlePostfix(object __instance, MapEvent __0, bool __runOriginal, ScoreState __state)
        {
            try
            {
                if (!__runOriginal) { WarScoreTrace.RecordSkipped("battle"); return; }
                if (__state == null || __0 == null) return;
                int afterCore = Read(__instance, __state.Winner, __state.Loser);
                if (!WarScoreRecoveryMath.NeedsRecovery(__state.Before, afterCore))
                {
                    WarScoreTrace.RecordEvent("battle", __state.Winner, __state.Loser, __state.Before, afterCore, null);
                    return;
                }
                MapEventSide loserSide = __0.Winner == __0.AttackerSide ? __0.DefenderSide : __0.AttackerSide;
                int points = WarScoreRecoveryMath.BattlePoints(loserSide == null ? 0 : loserSide.HealthyTroopCountAtMapEventStart);
                Award(__instance, __state.Winner, __state.Loser, points, "Recovered missing battle score");
                WarScoreTrace.RecordEvent("battle", __state.Winner, __state.Loser, __state.Before, Read(__instance, __state.Winner, __state.Loser), null);
            }
            catch (Exception ex) { ReportRuntimeFailure(ex); }
        }

        private static void SettlementPrefix(object __instance, object[] __args, bool __runOriginal, out ScoreState __state)
        {
            if (!__runOriginal) { __state = null; WarScoreTrace.RecordSkipped("siege-capture"); return; }
            try { __state = CreateSettlementState(__instance, __args); }
            catch (Exception ex) { __state = null; ReportRuntimeFailure(ex); }
        }

        private static void SettlementPostfix(object __instance, object[] __args, bool __runOriginal, ScoreState __state)
        {
            try
            {
                if (!__runOriginal) { WarScoreTrace.RecordSkipped("siege-capture"); return; }
                if (__state == null) return;
                Settlement settlement = __args != null && __args.Length > 0 ? __args[0] as Settlement : null;
                int afterCore = Read(__instance, __state.Winner, __state.Loser);
                if (!WarScoreRecoveryMath.NeedsRecovery(__state.Before, afterCore))
                {
                    WarScoreTrace.RecordEvent("siege-capture", __state.Winner, __state.Loser, __state.Before, afterCore, settlement);
                    return;
                }
                Award(__instance, __state.Winner, __state.Loser,
                    settlement != null && settlement.IsTown ? WarScoreRecoveryMath.TownCapturePoints : WarScoreRecoveryMath.CastleCapturePoints,
                    "Recovered missing fief-capture score");
                WarScoreTrace.RecordEvent("siege-capture", __state.Winner, __state.Loser, __state.Before, Read(__instance, __state.Winner, __state.Loser), settlement);
            }
            catch (Exception ex) { ReportRuntimeFailure(ex); }
        }

        private static ScoreState CreateBattleState(object instance, MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.HasWinner || !IsScoredBattle(mapEvent)) return null;
            MapEventSide winnerSide = mapEvent.Winner;
            MapEventSide loserSide = winnerSide == mapEvent.AttackerSide ? mapEvent.DefenderSide : mapEvent.AttackerSide;
            return CreateState(instance, ToKingdom(winnerSide == null ? null : winnerSide.MapFaction), ToKingdom(loserSide == null ? null : loserSide.MapFaction));
        }

        private static ScoreState CreateSettlementState(object instance, object[] args)
        {
            Settlement settlement = args != null && args.Length > 0 ? args[0] as Settlement : null;
            if (settlement == null || !settlement.IsFortification || !IsBySiege(args)) return null;

            Hero newOwner = ReadHeroArgument(args, _settlementNewOwnerIndex);
            Hero oldOwner = ReadHeroArgument(args, _settlementOldOwnerIndex);
            return CreateState(instance, ToKingdom(newOwner == null ? null : newOwner.MapFaction), ToKingdom(oldOwner == null ? null : oldOwner.MapFaction));
        }

        private static ScoreState CreateState(object instance, Kingdom winner, Kingdom loser)
        {
            if (winner == null || loser == null || winner == loser || !winner.IsAtWarWith(loser)) return null;
            return new ScoreState { Winner = winner, Loser = loser, Before = Read(instance, winner, loser) };
        }

        private static bool IsScoredBattle(MapEvent mapEvent)
        {
            return mapEvent.IsFieldBattle || mapEvent.IsSallyOut || mapEvent.IsSiegeOutside || mapEvent.IsBlockade || mapEvent.IsBlockadeSallyOut;
        }

        private static Kingdom ToKingdom(IFaction faction) { return faction as Kingdom; }
        private static int Read(object instance, Kingdom winner, Kingdom loser) { return (int)_readWarScore.Invoke(instance, new object[] { winner, loser }); }
        private static void Award(object instance, Kingdom winner, Kingdom loser, int points, string reason)
        {
            _addWarScore.Invoke(instance, new object[] { winner, loser, points, reason });
            System.Diagnostics.Trace.WriteLine("AOC war-score recovery awarded " + points + " points for " + winner.StringId + " against " + loser.StringId + ".");
        }

        private static bool IsApprovedContract(Type ledgerType, MethodInfo readWarScore, MethodInfo addWarScore, FieldInfo activeLedger, FieldInfo warScores, MethodBase battleTarget, MethodBase settlementTarget)
        {
            if (!IsApprovedLedger(ledgerType)) return false;
            return HasSignature(readWarScore, typeof(int), typeof(Kingdom), typeof(Kingdom))
                && HasSignature(addWarScore, typeof(void), typeof(Kingdom), typeof(Kingdom), typeof(int), typeof(string))
                && activeLedger != null && activeLedger.IsStatic && activeLedger.FieldType == ledgerType
                && warScores != null && !warScores.IsStatic && warScores.FieldType == typeof(List<string>)
                && HasSignature(battleTarget, typeof(void), typeof(MapEvent))
                && HasSignature(settlementTarget, typeof(void), typeof(Settlement), typeof(bool), typeof(Hero), typeof(Hero), typeof(Hero),
                    typeof(TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail))
                && FindParameterIndex(settlementTarget, "newOwner") == 2
                && FindParameterIndex(settlementTarget, "oldOwner") == 3
                && FindParameterIndex(settlementTarget, "detail") == 5;
        }

        private static bool HasSignature(MethodBase method, Type returnType, params Type[] parameterTypes)
        {
            MethodInfo methodInfo = method as MethodInfo;
            if (methodInfo == null || methodInfo.ReturnType != returnType) return false;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length) return false;
            for (int index = 0; index < parameters.Length; index++) if (parameters[index].ParameterType != parameterTypes[index]) return false;
            return true;
        }

        private static HarmonyMethod Last(Type type, string methodName)
        {
            HarmonyMethod method = new HarmonyMethod(type, methodName);
            method.priority = Priority.Last;
            return method;
        }

        private static string HashAssembly(Assembly assembly)
        {
            if (assembly == null || string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location)) return string.Empty;
            using (FileStream stream = File.OpenRead(assembly.Location))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static int FindParameterIndex(MethodBase method, string name)
        {
            if (method == null || string.IsNullOrEmpty(name)) return -1;
            ParameterInfo[] parameters = method.GetParameters();
            for (int index = 0; index < parameters.Length; index++)
                if (string.Equals(parameters[index].Name, name, StringComparison.Ordinal)) return index;
            return -1;
        }

        private static Hero ReadHeroArgument(object[] args, int index)
        {
            return args != null && index >= 0 && index < args.Length ? args[index] as Hero : null;
        }

        private static bool IsBySiege(object[] args)
        {
            object detail = args != null && _settlementDetailIndex >= 0 && _settlementDetailIndex < args.Length
                ? args[_settlementDetailIndex] : null;
            if (!(detail is TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail)) return false;
            return WarScoreRecoveryMath.IsSiegeCaptureDetail((int)(TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail)detail);
        }

        private static void ReportRuntimeFailure(Exception ex)
        {
            if (_reportedRuntimeFailure) return;
            _reportedRuntimeFailure = true;
            System.Diagnostics.Trace.WriteLine("AOC war-score recovery failed safely; Core score handling remains active: " + ex);
        }
    }
}
