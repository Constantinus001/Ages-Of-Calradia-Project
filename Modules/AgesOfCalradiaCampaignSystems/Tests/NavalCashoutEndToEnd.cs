using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

public static partial class NavalCashoutVerifier
{
    static readonly Dictionary<Ship, PartyBase> ShipOwners = new Dictionary<Ship, PartyBase>();
    static readonly Dictionary<PartyBase, MBList<Ship>> Fleets = new Dictionary<PartyBase, MBList<Ship>>();
    static readonly Dictionary<Clan, Hero> Leaders = new Dictionary<Clan, Hero>();
    static readonly Dictionary<Hero, int> Money = new Dictionary<Hero, int>();
    static MBList<MapEventParty> NativeWinners;
    static MBList<KeyValuePair<Ship, MapEventParty>> Distribution;
    static MapEventSide WinningSide;
    static BattleRewardModel RewardModel;
    static bool FailAfterCredit;
    static int PaymentCalls, DestroyCalls, TransferCalls;
    static bool Leader(Clan __instance, ref Hero __result) { __result = Leaders[__instance]; return false; }
    static bool PartyLeader(PartyBase __instance, ref Hero __result) { __result = Leaders[Clans[Mobiles[__instance]]]; return false; }
    static bool HeroGold(Hero __instance, ref int __result) { __result = Money[__instance]; return false; }
    static bool Active(ref bool __result) { __result = true; return false; }
    static bool NoOwner(ref Hero __result) { __result = null; return false; }
    static bool PartyShips(PartyBase __instance, ref MBReadOnlyList<Ship> __result) { __result = Fleets[__instance]; return false; }
    static bool MobileShips(MobileParty __instance, ref MBReadOnlyList<Ship> __result) { __result = Fleets[Mobiles.First(p => p.Value == __instance).Key]; return false; }
    static bool Pay(Hero __0, Hero __1, int __2)
    {
        PaymentCalls++;
        if (__0 != null) Money[__0] -= __2;
        if (__1 != null) Money[__1] += __2;
        if (FailAfterCredit) throw new InvalidOperationException("injected after credit");
        return false;
    }
    static bool Naval(ref bool __result) { __result = true; return false; }
    static bool Retreat(ref BattleSideEnum __result) { __result = (BattleSideEnum)(-1); return false; }
    static bool NoSettlement(ref Settlement __result) { __result = null; return false; }
    static bool WinnerSide(ref MapEventSide __result) { __result = WinningSide; return false; }
    static bool LeaderParty(ref PartyBase __result) { __result = Seller; return false; }
    static bool MainParty(ref PartyBase __result) { __result = null; return false; }
    static bool BattleModel(ref BattleRewardModel __result) { __result = RewardModel; return false; }
    static bool Winners(ref MBReadOnlyList<MapEventParty> __result) { __result = NativeWinners; return false; }
    static bool Distribute(ref MBReadOnlyList<KeyValuePair<Ship, MapEventParty>> __result) { __result = Distribution; return false; }
    static bool Damage(ref float __2) { __2 = 0; return false; }
    static bool NoDamage(ref float __result) { __result = 0; return false; }
    static bool HitPoints(ref float __result) { __result = 100; return false; }
    static bool Destroy(Ship __0) { DestroyCalls++; Fleets[ShipOwners[__0]].Remove(__0); ShipOwners[__0] = null; return false; }
    static bool Transfer(PartyBase __0, Ship __1) { TransferCalls++; Fleets[ShipOwners[__1]].Remove(__1); Fleets[__0].Add(__1); ShipOwners[__1] = __0; return false; }

    static void EndToEnd(Harmony h, Type modelType, ShipCostModel model, PartyBase ai, PartyBase player, Ship ship)
    {
        Leaders[Ai] = Blank<Hero>(); Leaders[Player] = Blank<Hero>();
        Money[Leaders[Ai]] = Money[Leaders[Player]] = 1000;
        Prefix(h, typeof(Clan), "Leader", "Leader"); Prefix(h, typeof(PartyBase), "LeaderHero", "PartyLeader");
        Prefix(h, typeof(Hero), "Gold", "HeroGold"); Prefix(h, typeof(Hero), "IsActive", "Active");
        Prefix(h, typeof(Hero), "MainHero", "NoOwner");
        Prefix(h, typeof(MobileParty), "Owner", "NoOwner"); Prefix(h, typeof(MobileParty), "Ships", "MobileShips");
        Prefix(h, typeof(PartyBase), "Ships", "PartyShips");
        h.Patch(AccessTools.Method(typeof(GiveGoldAction), "ApplyBetweenCharacters"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Pay"));
        Fleets[ai] = new MBList<Ship>(); Fleets[player] = new MBList<Ship>();
        ShipOwners[ship] = ai; Fleets[ai].Add(ship);
        var naval = modelType.Assembly;
        var type = naval.GetType("NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior", true);
        foreach (var lambda in type.GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            .Where(m => m.Name.StartsWith("<RecoverGoldFromRemainingShipsAfterDistribution>b__")))
            h.Patch(lambda, postfix: new HarmonyMethod(typeof(NavalCashoutVerifier), "Recompile"));
        var recovery = AccessTools.Method(type, "RecoverGoldFromRemainingShipsAfterDistribution");
        h.Patch(recovery, postfix: new HarmonyMethod(typeof(NavalCashoutVerifier), "Recompile"));
        var behavior = FormatterServices.GetUninitializedObject(type);
        int beforeCalls = PaymentCalls;
        recovery.Invoke(behavior, new object[] { Mobiles[ai] });
        Check(Money[Leaders[Ai]] == 1900 && PaymentCalls == beforeCalls + 1, "actual native recovery quote to one payment");
        Check(Get("NavalCashoutRuntime", "_recovery") == null, "real recovery scope cleaned");
        // Already transferred ships are absent from native remaining-ship sum.
        Transfer(player, ship);
        recovery.Invoke(behavior, new object[] { Mobiles[ai] });
        Check(Money[Leaders[Ai]] == 1900, "transferred ship not paid twice by recovery");
        recovery.Invoke(behavior, new object[] { Mobiles[player] });
        Check(Money[Leaders[Player]] == 5725, "native player recovery penalty preserved");
        Transfer(ai, ship);
        FailAfterCredit = true;
        beforeCalls = PaymentCalls;
        bool failure = false;
        try { recovery.Invoke(behavior, new object[] { Mobiles[ai] }); }
        catch (TargetInvocationException ex) { failure = ex.InnerException is InvalidOperationException; }
        finally { FailAfterCredit = false; }
        Check(failure && Money[Leaders[Ai]] == 2800 && PaymentCalls == beforeCalls+1, "partial payment failure preserved without retry or corrective debit");
        Check(Get("NavalCashoutRuntime", "_recovery") == null && Get("NavalCashoutRuntime", "_quote") == null, "partial payment failure scopes cleaned");

        // Execute the actual patched LootDefeatedPartyShips body. World-facing
        // damage, distribution choice, destruction and wallet endpoints are
        // deterministic fixture boundaries; the native loop/allocation runs.
        Prefix(h, typeof(MapEvent), "IsNavalMapEvent", "Naval"); Prefix(h, typeof(MapEvent), "RetreatingSide", "Retreat");
        Prefix(h, typeof(MapEvent), "MapEventSettlement", "NoSettlement"); Prefix(h, typeof(MapEvent), "IsPlayerMapEvent", "False");
        Prefix(h, typeof(MapEvent), "Winner", "WinnerSide"); Prefix(h, typeof(MapEventSide), "LeaderParty", "LeaderParty");
        Prefix(h, typeof(PartyBase), "MainParty", "MainParty"); Prefix(h, typeof(Ship), "HitPoints", "HitPoints");
        Prefix(h, typeof(GameModels), "BattleRewardModel", "BattleModel");
        var rewardType = naval.GetType("NavalDLC.GameComponents.NavalDLCBattleRewardModel", true);
        RewardModel = (BattleRewardModel)FormatterServices.GetUninitializedObject(rewardType);
        h.Patch(AccessTools.Method(rewardType, "CalculateShipDamageAfterDefeat"), new HarmonyMethod(typeof(NavalCashoutVerifier), "NoDamage"));
        h.Patch(AccessTools.Method(rewardType, "DistributeDefeatedPartyShipsAmongWinners"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Distribute"));
        h.Patch(AccessTools.Method(rewardType, "GetWinnerPartiesThatCanPlunderGoldFromShips"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Winners"));
        h.Patch(AccessTools.Method(typeof(Ship), "OnShipDamaged"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Damage"));
        h.Patch(AccessTools.Method(typeof(DestroyShipAction), "Apply"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Destroy"));
        h.Patch(AccessTools.Method(typeof(ChangeShipOwnerAction), "ApplyByLooting"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Transfer"));
        var aiWinner = Winner(ai, 1, 10); var playerWinner = Winner(player, 1, 20);
        NativeWinners = new MBList<MapEventParty> { aiWinner, playerWinner };
        var loser = MakeParty(Ai); Fleets[loser] = new MBList<Ship>(); Transfer(loser, ship);
        var kept = Blank<Ship>(); AccessTools.Field(typeof(Ship), "ShipHull").SetValue(kept, Blank<ShipHull>());
        ShipOwners[kept] = loser; Fleets[loser].Add(kept);
        var losers = new MBList<MapEventParty> { Winner(loser, 0, 0) };
        Distribution = new MBList<KeyValuePair<Ship, MapEventParty>> {
            new KeyValuePair<Ship, MapEventParty>(ship, null), new KeyValuePair<Ship, MapEventParty>(kept, aiWinner) };
        WinningSide = Blank<MapEventSide>();
        var loot = AccessTools.Method(typeof(MapEvent), "LootDefeatedPartyShips");
        h.Patch(loot, postfix: new HarmonyMethod(typeof(NavalCashoutVerifier), "Recompile"));
        int beforeDestroy = DestroyCalls, beforeTransfer = TransferCalls;
        loot.Invoke(Blank<MapEvent>(), new object[] { NativeWinners, losers });
        Check(aiWinner.PlunderedGold == 460 && playerWinner.PlunderedGold == 2382, "actual native mixed battle preserves player pool and prior loot");
        Check(DestroyCalls == beforeDestroy+1 && TransferCalls == beforeTransfer+1 && ShipOwners[kept] == ai, "actual native battle selects one destruction and one transfer");
        var commit = AccessTools.Method(typeof(MapEventParty), "CommitGoldChanges");
        h.Patch(commit, postfix: new HarmonyMethod(typeof(NavalCashoutVerifier), "Recompile"));
        int aiGold = Money[Leaders[Ai]], playerGold = Money[Leaders[Player]];
        commit.Invoke(aiWinner, new object[0]); commit.Invoke(playerWinner, new object[0]);
        Check(Money[Leaders[Ai]] == aiGold+460 && Money[Leaders[Player]] == playerGold+2382, "native commit pays corrected AI and untouched player allocation");
        Check(aiWinner.PlunderedGold == 0 && playerWinner.PlunderedGold == 0, "native commit resets allocations");
        Check(!((bool)AccessTools.PropertyGetter(T("NavalCashoutRuntime"), "HasOpenScope").Invoke(null, null)), "complete native battle leaves no policy scope");
    }
}
