using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Localization;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 QuestBase lifecycle boundaries. Prefix/finalizer only;
    // no setters, model calls, result replacement or exception suppression.
    // Missing signatures fail capture setup. Inline setters are not relied on:
    // ChangeQuestDueTime and broad lifecycle boundaries witness actual endpoints.
    // Verify-DiagnosticExtensions.ps1 covers native binding, skip/throw/finite/Never.
    internal static class QuestLifecycleObserver
    {
        internal sealed class Call
        {
            internal string Id, Parent, Before, Name, Owner;
            internal QuestBase Quest;
            internal Call Previous;
        }
        [ThreadStatic] private static Call _current;
        private static EconomyObjectIdentity _identity = new EconomyObjectIdentity();
        internal static bool HasOpenScope { get { return _current != null; } }
        internal static void Reset() { _current = null; _identity = new EconomyObjectIdentity(); }
        internal static IEnumerable<MethodInfo> Targets()
        {
            foreach (string name in new[] { "InitializeQuestOnCreation", "StartQuest", "CompleteQuestWithSuccess", "FinalizeQuest" })
                yield return SupplyChainObserver.Require(typeof(QuestBase), name);
            yield return SupplyChainObserver.Require(typeof(QuestBase), "ChangeQuestDueTime", typeof(CampaignTime));
            foreach (string name in new[] { "CompleteQuestWithTimeOut", "CompleteQuestWithFail", "CompleteQuestWithBetrayal", "CompleteQuestWithCancel" })
                yield return SupplyChainObserver.Require(typeof(QuestBase), name, typeof(TextObject));
        }
        internal static void Install(Harmony harmony)
        {
            foreach (var target in Targets()) harmony.Patch(target,
                new HarmonyMethod(typeof(QuestLifecycleObserver), "Before"), null, null,
                new HarmonyMethod(typeof(QuestLifecycleObserver), "After") { priority = Priority.Last });
        }
        private static string State(QuestBase quest)
        {
            return "due=" + (quest.QuestDueTime == CampaignTime.Never ? "never" : SupplyCapture.N(quest.QuestDueTime.ToDays))
                + ",ongoing=" + quest.IsOngoing + ",finalized=" + quest.IsFinalized;
        }
        private static void Before(QuestBase __instance, MethodBase __originalMethod, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                using (DiagnosticObserverCost.Measure("questLifecycle"))
                {
                    var call = new Call { Id = Guid.NewGuid().ToString("N"), Parent = _current?.Id ?? "none",
                        Previous = _current, Quest = __instance, Name = __originalMethod.Name, Before = State(__instance),
                        Owner = "quest:" + _identity.Get(__instance) };
                    __state = call; _current = call;
                    SupplyCapture.Write("QUEST_LIFECYCLE_BEGIN", 0, 0, call.Owner, call.Name, 0, 0,
                        "operation=" + call.Id + "; parentOperation=" + call.Parent + "; quest=" + __instance.StringId + "; state=" + call.Before);
                }
            }
            catch (Exception ex) { SupplyCapture.Fail("quest lifecycle before", ex); }
        }
        private static void After(Call __state, bool __runOriginal, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (!SupplyCapture.Active) return;
                using (DiagnosticObserverCost.Measure("questLifecycle"))
                    SupplyCapture.Write("QUEST_LIFECYCLE_END", 0, 0, __state.Owner, __state.Name, 0, 0,
                        "operation=" + __state.Id + "; parentOperation=" + __state.Parent + "; quest=" + __state.Quest.StringId + "; beforeState=" + __state.Before
                        + "; afterState=" + State(__state.Quest) + "; originalRan=" + __runOriginal
                        + "; error=" + (__exception == null ? "none" : __exception.GetType().FullName)
                        + "; endpoint_observation_not_repeated_model_or_save_mutation");
                if (__exception != null) SupplyCapture.Fail("native quest lifecycle exception", __exception);
            }
            catch (Exception ex) { SupplyCapture.Fail("quest lifecycle after", ex); }
            finally { _current = __state.Previous; }
        }
    }
}
