// Offline-only verifier. Prices, party role lookup and event delivery are stubs;
// SellItemsAction, inventory mutations, payment wrappers and gold changes are native.
using System;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

public static class NativeAiSaleFixture
{
    static Hero hero;
    static MobileParty main;
    static bool lord;
    static int calls, eventAmount, events;
    static int[] prices;
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Field(object obj, string name, object value) { AccessTools.Field(obj.GetType(), name).SetValue(obj, value); }
    static bool Price(ref int __result) { __result = prices[Math.Min(calls++, prices.Length - 1)]; return false; }
    static bool Leader(ref Hero __result) { __result = hero; return false; }
    static bool MainParty(ref MobileParty __result) { __result = main; return false; }
    static bool MainHero(ref Hero __result) { __result = null; return false; }
    static bool Lord(ref bool __result) { __result = lord; return false; }
    static bool Traded(ValueTuple<int, string> __2) { eventAmount = __2.Item1; events++; return false; }
    static void NoNotification(ref bool __3) { __3 = true; }
    static void Patch(Harmony h, MethodBase method, string prefix) { h.Patch(method, new HarmonyMethod(typeof(NativeAiSaleFixture), prefix)); }
    public static string Run(Action installDiagnostics)
    {
        var h = new Harmony("aoc.sale.quantity.fixture");
        var current = AccessTools.Field(typeof(Campaign), "<Current>k__BackingField");
        var previous = current.GetValue(null);
        try
        {
            var campaign = Blank<Campaign>(); current.SetValue(null, campaign);
            Field(campaign, "GameStarted", true);
            Field(campaign, "<CampaignEventDispatcher>k__BackingField", Blank<CampaignEventDispatcher>());
            Patch(h, AccessTools.Method(typeof(Town), "GetItemPrice", new[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool) }), "Price");
            Patch(h, AccessTools.PropertyGetter(typeof(MobileParty), "LeaderHero"), "Leader");
            Patch(h, AccessTools.PropertyGetter(typeof(MobileParty), "MainParty"), "MainParty");
            Patch(h, AccessTools.PropertyGetter(typeof(Hero), "MainHero"), "MainHero");
            Patch(h, AccessTools.PropertyGetter(typeof(MobileParty), "IsLordParty"), "Lord");
            Patch(h, AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroOrPartyTradedGold"), "Traded");
            Patch(h, AccessTools.Method(typeof(GiveGoldAction), "ApplyForSettlementToCharacter"), "NoNotification");
            if (installDiagnostics != null)
            {
                current.SetValue(null, null);
                try { installDiagnostics(); } finally { current.SetValue(null, campaign); }
            }
            var town = Blank<Town>(); var settlement = Blank<Settlement>();
            var buyer = Blank<PartyBase>(); var seller = Blank<PartyBase>(); var mobile = Blank<MobileParty>();
            hero = Blank<Hero>();
            settlement.Town = town;
            Field(settlement, "<SettlementComponent>k__BackingField", town);
            Field(settlement, "<Party>k__BackingField", buyer);
            Field(buyer, "<Settlement>k__BackingField", settlement);
            Field(town, "_owner", buyer);
            Field(seller, "<MobileParty>k__BackingField", mobile);
            Field(mobile, "<Party>k__BackingField", seller);
            var item = new ItemObject();
            Field(item, "<ItemCategory>k__BackingField", new ItemCategory());
            // cash, expected units, expected price evaluations, initialization,
            // main player, non-lord, free-price scenario.
            int[][] cases = { new[] {180,2,3,0,0,0,0}, new[] {0,0,1,0,0,0,0},
                new[] {79,0,1,0,0,0,0}, new[] {300,3,3,0,0,0,0}, new[] {1000,3,3,0,0,0,0},
                new[] {0,3,3,1,0,0,0}, new[] {0,3,3,0,1,0,0}, new[] {0,3,3,0,0,1,0},
                new[] {0,3,3,0,0,0,1} };
            foreach (var c in cases)
            {
                main = c[4] == 1 ? mobile : null; lord = c[5] == 0;
                Field(campaign, "GameStarted", c[3] == 0);
                prices = c[6] == 1 ? new[] {0,0,0} : new[] {80,100,120};
                var stock = new ItemRoster(); stock.AddToCounts(item, 5);
                var market = new ItemRoster();
                Field(seller, "<ItemRoster>k__BackingField", stock);
                Field(buyer, "<ItemRoster>k__BackingField", market);
                Field(town, "<Gold>k__BackingField", c[0]); hero.Gold = 1000;
                calls = events = 0;
                SellItemsAction.Apply(seller, buyer, stock.GetElementCopyAtIndex(0), 3, settlement);
                int quote = 0; for (int i=0; i<c[1]; i++) quote += prices[i];
                int paid = Math.Min(quote, c[0]);
                if (stock.GetItemNumber(item) != 5-c[1] || market.GetItemNumber(item) != c[1]
                    || calls != c[2] || town.Gold != c[0]-paid || hero.Gold != 1000+paid
                    || events != 1 || eventAmount != -paid)
                    throw new Exception("Native sale mismatch: cash=" + c[0] + ", expectedUnits=" + c[1] + ", calls=" + calls);
            }
            return "PASS: nine native AI sales; partial/empty/exact/abundant cash, dynamic per-item quotes, retained unsold inventory, paired payment/events; initialization, player, non-lord and zero-price bypasses.";
        }
        finally { h.UnpatchAll(h.Id); current.SetValue(null, previous); hero = null; main = null; }
    }
}
