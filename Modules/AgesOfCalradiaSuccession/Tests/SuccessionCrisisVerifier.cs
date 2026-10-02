using System;
using System.Collections.Generic;
using System.Linq;
using AgesOfCalradiaSuccession;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

internal static partial class SuccessionEngineVerifier
{
    private static void CrisisLifecycle()
    {
        foreach (string outcome in new[] { "peace", "victory", "defeat", "death", "failure", "foreign" }) CrisisOutcome(outcome);
        CrisisCreationFailure();
        AutomaticCrisis();
    }

    private static void CrisisOutcome(string outcome)
    {
        Reset(); var realm = new Kingdom { StringId = "parent" };
        Clan house = House(realm, "royal"); Hero king = Add(house, "king", 60); house.Fiefs.Add(new Town());
        Clan rebels = House(realm, "rebels"); Hero claimant = Add(rebels, "claimant", 40); rebels.Leader = claimant; rebels.Fiefs.Add(new Town());
        Clan supporters = House(realm, "supporters"); supporters.Leader = Add(supporters, "supporter", 40);
        var behavior = Start(realm, house, king);
        var interrupted = new Dictionary<string, string>();
        ChangeKingdomAction.AfterApply = delegate
        {
            behavior.SyncData(new Store(interrupted, true));
            CampaignEvents.TickEvent.Raise(0.1f);
        };
        string result;
        Check(SuccessionCivilWar.TryStart(realm, claimant, new[] { supporters }, behavior, out result), "production crisis forms " + outcome);
        Kingdom rebel = rebels.Kingdom;
        Check(behavior.GetCrisisStatus(realm) == "Active" && rebel != realm && supporters.Kingdom == rebel,
            "formation callbacks cannot prematurely settle crisis " + outcome);
        var failedLoad = new SuccessionCampaignBehavior(); failedLoad.SyncData(new Store(interrupted, false));
        Check(failedLoad.GetCrisisStatus(realm) == "Failed" && behavior.GetCrisisStatus(realm) == "Active",
            "mid-formation save quarantines only loaded copy " + outcome);
        Check(!SuccessionCivilWar.TryStart(realm, supporters.Leader, null, behavior, out result), "duplicate crisis refused " + outcome);
        ChangeKingdomAction.AfterApply = null;
        var saved = new Dictionary<string, string>(); behavior.SyncData(new Store(saved, true));
        CampaignEvents.Reset(); behavior = new SuccessionCampaignBehavior(); behavior.SyncData(new Store(saved, false));
        Campaign.Current = new Campaign { Behavior = behavior }; behavior.RegisterEvents();
        CampaignEvents.OnSessionLaunchedEvent.Raise(new CampaignGameStarter());
        Check(behavior.GetCrisisStatus(realm) == "Active" && behavior.GetCrisisStatus(rebel) == "Active", "active crisis round trips " + outcome);
        if (outcome == "peace")
        {
            claimant.IsActive = false;
            CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
            Check(behavior.GetCrisisStatus(realm) == "Active", "captivity alone does not extinguish claimant war");
            claimant.IsActive = true;
            var malformed = new SuccessionCampaignBehavior(); malformed.SyncData(new Store(saved, false));
            SetMap(malformed.Crises, "_crisisDay", realm.StringId, "invalid");
            var badSave = new Dictionary<string, string>(); malformed.SyncData(new Store(badSave, true));
            malformed = new SuccessionCampaignBehavior(); malformed.SyncData(new Store(badSave, false));
            Check(malformed.GetCrisisStatus(realm) == "Failed" && malformed.IsAuthorityTransferBlocked(realm),
                "malformed crisis timing quarantines rather than guessing a settlement");
        }
        Kingdom third = null;
        if (outcome == "victory") house.Fiefs.Clear();
        else if (outcome == "defeat") rebels.Fiefs.Clear();
        else if (outcome == "death") claimant.IsAlive = false;
        else MakePeaceAction.Apply(realm, rebel);
        if (outcome == "foreign")
        {
            third = new Kingdom { StringId = "third" }; Kingdom.All.Add(third);
            ChangeKingdomAction.ApplyByJoinToKingdom(supporters, third, CampaignTime.Now, true);
        }
        if (outcome == "failure") ChangeKingdomAction.AfterApply = delegate { throw new InvalidOperationException("partial reunification"); };
        else ChangeKingdomAction.AfterApply = delegate { behavior.SyncData(new Store(interrupted, true)); CampaignEvents.TickEvent.Raise(0.1f); };
        CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
        if (outcome == "failure")
        {
            Check(behavior.GetCrisisStatus(realm) == "Failed", "partial settlement quarantines original realm");
            int count = ChangeKingdomAction.ApplyCount;
            saved.Clear(); behavior.SyncData(new Store(saved, true));
            CampaignEvents.Reset(); behavior = new SuccessionCampaignBehavior(); behavior.SyncData(new Store(saved, false)); behavior.RegisterEvents();
            CampaignTime.Day++; CampaignEvents.DailyTickEvent.Raise(); CampaignEvents.TickEvent.Raise(0.1f);
            Check(ChangeKingdomAction.ApplyCount == count && behavior.GetCrisisStatus(realm) == "Failed", "failed settlement cannot retry after reload or daily tick");
            return;
        }
        Check(behavior.GetCrisisStatus(realm) == "Closed" && rebel.IsEliminated && rebels.Kingdom == realm,
            "crisis reunifies and eliminates empty shell " + outcome);
        Check(realm.Leader == (outcome == "victory" ? claimant : king), "settlement awards crown only for claimant victory " + outcome);
        if (third != null) Check(supporters.Kingdom == third, "settlement preserves third-party defections");
        failedLoad = new SuccessionCampaignBehavior(); failedLoad.SyncData(new Store(interrupted, false));
        Check(failedLoad.GetCrisisStatus(realm) == "Failed", "mid-settlement save is quarantined " + outcome);
        int completed = ChangeKingdomAction.ApplyCount;
        CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeKingdomAction.ApplyCount == completed, "completed settlement is not replayed " + outcome);
        if (outcome == "peace")
        {
            SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "30");
            SetMap(behavior, "_pretenderByKingdom", realm.StringId, claimant.StringId);
            SetMap(behavior, "_recognitionByRealmClan", realm.StringId + ":" + supporters.StringId, "SupportsPretender");
            CampaignTime.Day += 20; CampaignEvents.TickEvent.Raise(0.1f);
            Check(behavior.GetCrisisStatus(realm) == "Closed", "settlement cooldown blocks another otherwise eligible uprising");
        }
    }

    private static void CrisisCreationFailure()
    {
        Reset(); var realm = new Kingdom { StringId = "creation-failure" };
        Clan house = House(realm, "royal"); Hero king = Add(house, "king", 60);
        Clan rebels = House(realm, "rebels"); Hero claimant = Add(rebels, "claimant", 40); rebels.Leader = claimant;
        var behavior = Start(realm, house, king);
        ChangeKingdomAction.AfterApply = delegate { throw new InvalidOperationException("partial formation"); };
        string result;
        Check(!SuccessionCivilWar.TryStart(realm, claimant, null, behavior, out result)
            && behavior.GetCrisisStatus(realm) == "Failed", "native formation failure persists quarantine");
        int count = ChangeKingdomAction.ApplyCount;
        CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
        Check(ChangeKingdomAction.ApplyCount == count, "failed formation cannot auto-retry");
    }

    private static void AutomaticCrisis()
    {
        Reset(); var realm = new Kingdom { StringId = "automatic" };
        Clan house = House(realm, "royal"); Hero king = Add(house, "king", 60); house.Fiefs.Add(new Town());
        Clan rebels = House(realm, "rebels"); Hero claimant = Add(rebels, "claimant", 40); rebels.Leader = claimant; rebels.Fiefs.Add(new Town());
        Clan supporters = House(realm, "supporters"); supporters.Leader = Add(supporters, "supporter", 40);
        var behavior = Start(realm, house, king);
        SetMap(behavior, "_legitimacyByKingdom", realm.StringId, "30");
        SetMap(behavior, "_pretenderByKingdom", realm.StringId, claimant.StringId);
        SetMap(behavior, "_recognitionByRealmClan", realm.StringId + ":" + supporters.StringId, "SupportsPretender");
        CampaignTime.Day += 13; CampaignEvents.TickEvent.Raise(0.1f);
        Check(behavior.GetCrisisStatus(realm) == "None", "automatic crisis respects accession grace period");
        Hero.MainHero = supporters.Leader;
        CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
        Check(behavior.GetCrisisStatus(realm) == "None", "automatic crisis cannot force player clan into rebellion");
        Hero.MainHero = null;
        CampaignTime.Day++; CampaignEvents.TickEvent.Raise(0.1f);
        Check(behavior.GetCrisisStatus(realm) == "Active" && supporters.Kingdom == rebels.Kingdom && rebels.Kingdom != realm,
            "eligible opposition starts production crisis after grace period");
    }
}
