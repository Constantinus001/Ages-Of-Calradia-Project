using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

public static class DiagnosticExtensionsFixture
{
    static bool skip, fail;
    static bool Skip() { return !skip; }
    static void ThrowAfter() { if (fail) throw new InvalidOperationException("quest-extension-sentinel"); }
    sealed class Quest : QuestBase
    {
        private Quest() : base("unused", null, CampaignTime.Never, 0) { }
        public override TextObject Title { get { return new TextObject("fixture"); } }
        public override bool IsRemainingTimeHidden { get { return false; } }
        protected override void SetDialogs() { }
        protected override void InitializeQuestOnGameLoad() { }
    }
    static readonly BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
    static void Invoke(Type type, string name, params object[] args) { type.GetMethod(name, Flags).Invoke(null, args); }
    static string Begin(Type capture, string root)
    { return (string)capture.GetMethod("BeginSession", Flags).Invoke(null, new object[] { root, new Func<double>(() => 42.5) }); }
    static bool Active(Type capture) { return (bool)capture.GetProperty("Active", Flags).GetValue(null, null); }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static string Run(Assembly diagnostics, Assembly logistics, string root)
    {
        var capture = diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.SupplyCapture", true);
        var questObserver = diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.QuestLifecycleObserver", true);
        var logisticsObserver = diagnostics.GetType("AgesOfCalradia.SoakDiagnostics.LogisticsLifecycleObserver", true);
        var ticks = AccessTools.Field(typeof(CampaignTime), "TimeTicksPerDay");
        var oldTicks = ticks.GetValue(null); ticks.SetValue(null, 100000L);
        var observer = new Harmony("aoc.extensions.observer.fixture");
        var injection = new Harmony("aoc.extensions.failure.fixture");
        try
        {
            Invoke(questObserver, "Install", observer);
            foreach (var target in (IEnumerable<MethodInfo>)questObserver.GetMethod("Targets", Flags).Invoke(null, null))
                Require(Harmony.GetPatchInfo(target).Owners.Contains(observer.Id), "Native quest target not patched");
            var q = (Quest)FormatterServices.GetUninitializedObject(typeof(Quest));
            AccessTools.PropertySetter(typeof(QuestBase), "StringId").Invoke(q, new object[] { "fixture_quest" });
            var change = AccessTools.Method(typeof(QuestBase), "ChangeQuestDueTime");
            injection.Patch(change, new HarmonyMethod(typeof(DiagnosticExtensionsFixture), "Skip") { priority = Priority.First },
                new HarmonyMethod(typeof(DiagnosticExtensionsFixture), "ThrowAfter"));
            string path = Begin(capture, root);
            skip = false; fail = false;
            change.Invoke(q, new object[] { CampaignTime.Days(50) });
            Require(q.QuestDueTime == CampaignTime.Days(50), "Observed native deadline changed");
            skip = true; change.Invoke(q, new object[] { CampaignTime.Days(60) });
            Require(q.QuestDueTime == CampaignTime.Days(50), "Skipped original changed quest deadline");
            skip = false; change.Invoke(q, new object[] { CampaignTime.Never });
            Require(q.QuestDueTime == CampaignTime.Never, "Never deadline altered");
            Invoke(capture, "Stop", "fixture_complete");
            string text = File.ReadAllText(path);
            Require(text.Contains("QUEST_LIFECYCLE_END") && text.Contains("originalRan=False")
                && text.Contains("due=50") && text.Contains("due=never"), "Quest lifecycle endpoints absent");
            Begin(capture, root); fail = true;
            bool propagated = false;
            try { change.Invoke(q, new object[] { CampaignTime.Days(70) }); }
            catch (TargetInvocationException e) { propagated = e.InnerException.Message == "quest-extension-sentinel"; }
            Require(propagated && !Active(capture), "Observer hid native exception or kept failed capture active");
            fail = false;
            path = Begin(capture, root);
            Invoke(logisticsObserver, "Install", observer);
            var reserveType = logistics.GetType("AgesOfCalradiaLogistics.LogisticsReserveBehavior", true);
            var reserve = Activator.CreateInstance(reserveType);
            var dictionary = (Dictionary<string, int>)AccessTools.Field(reserveType, "_reservesByPartyId").GetValue(reserve);
            var party = (MobileParty)FormatterServices.GetUninitializedObject(typeof(MobileParty));
            AccessTools.PropertySetter(typeof(MobileParty), "StringId").Invoke(party, new object[] { "unknown_party" });
            var consume = AccessTools.Method(reserveType, "TryConsumeReserve", new[] { typeof(MobileParty), typeof(int) });
            Require(!(bool)consume.Invoke(reserve, new object[] { party, 10 }), "Invalid party unexpectedly consumed reserve");
            Require(dictionary.Count == 0, "Observer created phantom reserve");
            dictionary.Add("unknown_party", 0);
            consume.Invoke(reserve, new object[] { party, 10 });
            var coalition = AccessTools.Method(reserveType, "TryConsumeReserve", new[] { typeof(MobileParty[]), typeof(int), typeof(int).MakeByRefType() });
            object[] args = { new[] { party }, 10, 7 };
            coalition.Invoke(reserve, args);
            Require((int)args[2] == 7 && dictionary["unknown_party"] == 0, "Observer changed coalition cursor or zero reserve");
            var market = logistics.GetType("AgesOfCalradiaLogistics.LogisticsSupplyMarketService", true);
            AccessTools.Method(market, "TryBuy").Invoke(null, new object[] { null, party, null, null, 1, 10 });
            Require(Active(capture), "Optional logistics observer failed valid capture");
            Invoke(capture, "Stop", "fixture_complete");
            text = File.ReadAllText(path);
            Require(text.Contains("unknown_party:-1") && text.Contains("unknown_party:0")
                && text.Contains("TryConsumeReserveCoalition") && text.Contains("result=False"), "Missing logistics unknown/known zero/result witnesses");
            return "PASS: native quest finite/Never/skip/exception preservation; all lifecycle signatures; optional logistics reserve reads, no phantom stock, ref cursor preservation and failed-buy receipts.";
        }
        finally
        {
            skip = fail = false;
            Invoke(capture, "Stop", "fixture_cleanup");
            injection.UnpatchAll(injection.Id); observer.UnpatchAll(observer.Id);
            Invoke(questObserver, "Reset"); Invoke(logisticsObserver, "Reset");
            ticks.SetValue(null, oldTicks);
        }
    }
}
