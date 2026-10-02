using System;
using System.Linq;
using AgesOfCalradiaSuccession;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

internal static class SuccessionDispatchVerifier
{
    private static int _checks;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _checks++;
    }
    private static int Main()
    {
        try
        {
            SuccessionElectionPatches.Install();
            SuccessionElectionPatches.Install();
            Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Kingdom), "AddDecision")).Prefixes.Count(p => p.owner == SuccessionElectionPatches.Owner) == 1, "idempotent installation");
            for (int order = 0; order < 2; order++) EventOrdering(order == 0);
            UnrelatedAndLifecycle();
            RecoveryAndFailure();
            QueueEdges();
            QuarantineEdges();
            CompetingPatch();
            SuccessionResolverVerifier.Run(Check);
            Console.WriteLine("Succession dispatch verifier passed: " + _checks + " behavioral assertions; real Harmony with native boundary doubles.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
    private static SuccessionCampaignBehavior Setup(out Kingdom realm, out Hero dead)
    {
        Kingdom.All.Clear();
        dead = new Hero { StringId = "old", IsAlive = false };
        realm = new Kingdom { StringId = "realm", Leader = new Hero { StringId = "new", IsAlive = true } };
        Kingdom.All.Add(realm);
        var behavior = new SuccessionCampaignBehavior();
        behavior.Record(realm, dead);
        Campaign.Current = new Campaign { Behavior = behavior };
        return behavior;
    }
    private static void EventOrdering(bool deathFirst)
    {
        Kingdom realm; Hero dead;
        var behavior = Setup(out realm, out dead);
        var decision = new KingSelectionKingdomDecision { Kingdom = realm };
        if (deathFirst) behavior.Death(dead);
        realm.AddDecision(decision, true);
        if (!deathFirst) behavior.Death(dead);
        behavior.Death(dead);
        realm.AddDecision(decision, true);
        decision.ApplyChosenOutcome(new DecisionOutcome());
        Check(behavior.Transfers == 0 && realm.NativeAdds == 0 && realm.NativeOutcomes == 0, "no native vote or transfer inside death stack");
        behavior.DuringTransfer = delegate { realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm }, true); behavior.Tick(); };
        behavior.AfterTransfer = delegate { realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm, IsEnforced = true }, true); behavior.Tick(); };
        behavior.Tick();
        behavior.Tick();
        Check(behavior.Transfers == 1, "single transfer despite duplicates and recursive tick");
        decision.ApplyChosenOutcome(new DecisionOutcome());
        realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm }, true);
        behavior.Death(dead);
        behavior.Tick();
        Check(behavior.Transfers == 1 && realm.NativeOutcomes == 0, "stale election cannot crown next heir or overwrite ruler");
        behavior.DuringTransfer = null;
        behavior.AfterTransfer = null;
        Hero nextDeath = realm.Leader;
        nextDeath.IsAlive = false;
        realm.Leader = new Hero { StringId = "third", IsAlive = true };
        behavior.Death(nextDeath);
        behavior.Tick();
        Check(behavior.Transfers == 2, "subsequent ruler death remains enabled");
    }
    private static void UnrelatedAndLifecycle()
    {
        Kingdom realm; Hero dead;
        var behavior = Setup(out realm, out dead);
        behavior.Death(new Hero { StringId = "unrelated", IsAlive = false });
        realm.AddDecision(new KingdomDecision { Kingdom = realm }, false);
        behavior.Tick();
        Check(behavior.Transfers == 0 && realm.NativeAdds == 1, "ordinary deaths and non-ruler decisions untouched");
        Campaign.Current = null;
        realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm }, true);
        Check(realm.NativeAdds == 2, "no campaign falls through");
        Campaign.Current = new Campaign();
        new KingSelectionKingdomDecision { Kingdom = realm }.ApplyChosenOutcome(new DecisionOutcome());
        Check(realm.NativeOutcomes == 1, "no succession behavior falls through");
        Campaign.Current = new Campaign { Behavior = behavior };
        behavior.Record(realm, realm.Leader);
        realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm, IsEnforced = true }, true);
        behavior.Tick();
        Check(behavior.Transfers == 1, "enforced abdication still dispatches");
    }
    private static void RecoveryAndFailure()
    {
        Kingdom realm; Hero dead;
        var behavior = Setup(out realm, out dead);
        behavior.Death(dead);
        string saved = behavior.SaveQueue();
        var loaded = new SuccessionCampaignBehavior();
        loaded.Record(realm, dead);
        loaded.LoadQueue(saved);
        Campaign.Current.Behavior = loaded;
        loaded.Recover(); loaded.Tick(); loaded.Tick();
        Check(loaded.Transfers == 1, "save between death and tick resumes once");
        behavior = Setup(out realm, out dead);
        behavior.LoadQueue(null);
        realm.UnresolvedDecisions.Add(new KingSelectionKingdomDecision { Kingdom = realm });
        realm.UnresolvedDecisions.Add(new KingdomDecision { Kingdom = realm });
        behavior.Recover(); behavior.Tick();
        Check(behavior.Transfers == 1 && realm.UnresolvedDecisions.Count == 1 && !(realm.UnresolvedDecisions[0] is KingSelectionKingdomDecision), "legacy recovery removes only ruler decision");
        behavior = Setup(out realm, out dead);
        behavior.ThrowOnTransfer = true;
        behavior.Death(dead); behavior.Tick();
        behavior.Death(dead); behavior.Tick();
        Check(behavior.Transfers == 1 && SuccessionDiagnostics.Messages.Any(m => m.Contains("quarantined")), "failure quarantines and logs without retry loop");
        loaded = new SuccessionCampaignBehavior(); loaded.Record(realm, dead);
        loaded.LoadQueue(behavior.SaveQueue()); loaded.Recover(); loaded.Tick();
        Check(loaded.Transfers == 0, "quarantine survives reload");
        behavior = Setup(out realm, out dead);
        behavior.Death(dead); realm.IsEliminated = true; behavior.Tick();
        Check(behavior.Transfers == 0, "eliminated realm has no transfer");
        behavior = Setup(out realm, out dead);
        behavior.Minors[realm.StringId] = new Hero { StringId = "child", IsAlive = true };
        var regent = new Hero { StringId = "regent", IsAlive = false };
        behavior.Regent(realm, regent);
        behavior.Death(regent); behavior.Death(regent); behavior.Tick();
        Check(behavior.Transfers == 1 && behavior.Minors[realm.StringId].StringId == "child", "regent death dispatched once with child identity retained");
    }
    private static void QueueEdges()
    {
        var queue = new SuccessionDispatchQueue();
        int calls = 0;
        Action<string,Exception> fail = delegate { throw new Exception("unexpected failure"); };
        queue.Request("one", "a"); queue.Request("two", "a");
        queue.Drain(delegate(string realm, string token) { calls++; if (realm == "one") queue.Request("one", "b"); }, fail);
        Check(calls == 2 && queue.Pending["one"] == "b", "independent realms and new reentrant request retained");
        queue.Drain(delegate { calls++; }, fail);
        Check(calls == 3, "new request runs on following drain");
        var loaded = new SuccessionDispatchQueue(); loaded.Deserialize(queue.Serialize());
        Check(!loaded.Request("one", "b") && !loaded.Owns("one"), "completed tokens persist without empty-cell locks");
        loaded.Deserialize("v999\ninvalid");
        Check(loaded.Pending.Count == 0 && loaded.Completed.Count == 0, "unknown dispatch version safely empty");
        queue = new SuccessionDispatchQueue(); queue.Request("realm|one", "hero%one");
        loaded.Deserialize(queue.Serialize());
        Check(loaded.Pending["realm|one"] == "hero%one", "escaped token round trip");
    }
    private static void CompetingPatch()
    {
        var foreign = new Harmony("succession.verifier.competing-mod");
        var target = AccessTools.Method(typeof(Kingdom), "AddDecision");
        foreign.Patch(target, postfix: new HarmonyMethod(typeof(SuccessionDispatchVerifier), "ForeignOutcome"));
        try
        {
            SuccessionElectionPatches.Install();
            Check(SuccessionDiagnostics.Messages.Any(m => m.Contains("succession.verifier.competing-mod")), "foreign owner diagnostic");
            Kingdom realm; Hero dead;
            var behavior = Setup(out realm, out dead);
            realm.AddDecision(new KingSelectionKingdomDecision { Kingdom = realm }, true);
            behavior.Tick();
            Check(realm.NativeOutcomes == 0 && realm.NativeAdds == 0 && behavior.Transfers == 1,
                "foreign postfix invoking outcome cannot overwrite succession");
        }
        finally { foreign.Unpatch(target, HarmonyPatchType.All, foreign.Id); }
    }
    private static void QuarantineEdges()
    {
        var queue = new SuccessionDispatchQueue();
        int calls = 0;
        queue.Request("realm", "first");
        queue.Drain(delegate
        {
            calls++;
            queue.Request("realm", "second");
            throw new InvalidOperationException("partial native transfer");
        }, delegate { });
        queue.Drain(delegate { calls++; }, delegate { });
        Check(calls == 1 && queue.Owns("realm"), "reentrant request cannot escape a failed transfer quarantine");
        Check(!queue.Request("realm", "third"), "different token cannot escape realm quarantine");
        var loaded = new SuccessionDispatchQueue();
        loaded.Deserialize(queue.Serialize());
        Check(!loaded.Request("realm", "fourth") && loaded.Pending.Count == 0, "realm quarantine survives serialization");
        queue = new SuccessionDispatchQueue();
        queue.Request("realm", "first");
        string savedInsideTransfer = null;
        queue.Drain(delegate
        {
            queue.Request("realm", "second");
            savedInsideTransfer = queue.Serialize();
        }, delegate { });
        loaded.Deserialize(savedInsideTransfer);
        int replayed = 0;
        loaded.Drain(delegate { replayed++; }, delegate { });
        Check(replayed == 0 && loaded.Owns("realm"), "in-transfer save quarantines uncertain native state instead of replaying");
        Check(queue.Pending.ContainsKey("realm") && !queue.Failed.ContainsKey("realm"), "taking a save does not quarantine the successful live campaign");
    }
    private static void ForeignOutcome(KingdomDecision __0)
    {
        var ruler = __0 as KingSelectionKingdomDecision;
        if (ruler != null) ruler.ApplyChosenOutcome(new DecisionOutcome());
    }
}
