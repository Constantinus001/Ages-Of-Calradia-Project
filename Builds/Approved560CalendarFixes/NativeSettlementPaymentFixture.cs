// Offline verifier only, excluded from the production project.
using System;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

public static class NativeSettlementPaymentFixture
{
    static int eventAmount, events;
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Field(object obj, string name, object value) { AccessTools.Field(obj.GetType(), name).SetValue(obj, value); }
    static bool Traded(ValueTuple<int, string> __2) { eventAmount = __2.Item1; events++; return false; }
    public static string Run()
    {
        var harmony = new Harmony("aoc.settlement.payment.fixture");
        var current = AccessTools.Field(typeof(Campaign), "<Current>k__BackingField");
        var previous = current.GetValue(null);
        try
        {
            var campaign = Blank<Campaign>();
            current.SetValue(null, campaign);
            Field(campaign, "<CampaignEventDispatcher>k__BackingField", Blank<CampaignEventDispatcher>());
            harmony.Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "OnHeroOrPartyTradedGold"),
                new HarmonyMethod(typeof(NativeSettlementPaymentFixture), "Traded"));
            var town = Blank<Town>(); var settlement = Blank<Settlement>(); var party = Blank<PartyBase>();
            settlement.Town = town;
            Field(settlement, "<SettlementComponent>k__BackingField", town);
            Field(party, "<Settlement>k__BackingField", settlement);
            Field(town, "_owner", party);
            Field(settlement, "<Party>k__BackingField", party);
            var hero = Blank<Hero>();
            // Bypass only our public wrapper prefix to reproduce the captured
            // native signed-transfer defect using the actual private method.
            Field(town, "<Gold>k__BackingField", 148); hero.Gold = 1000;
            AccessTools.Method(typeof(GiveGoldAction), "ApplyInternal").Invoke(null,
                new object[] {hero, null, null, party, -858, false, ""});
            if (town.Gold != 0 || hero.Gold != 1858)
                throw new Exception("Native signed-payment baseline changed; re-audit this patch");
            // requested, town opening cash, hero opening cash, expected payment.
            int[][] cases = { new[] {858,148,1000,148}, new[] {539,0,1000,0},
                new[] {486,0,1000,0}, new[] {100,100,1000,100}, new[] {100,200,1000,100},
                new[] {0,200,1000,0}, new[] {-100,200,1000,-100}, new[] {-100,200,25,-25} };
            foreach (var c in cases)
            {
                Field(town, "<Gold>k__BackingField", c[1]); hero.Gold = c[2]; events = 0;
                GiveGoldAction.ApplyForSettlementToCharacter(settlement, hero, c[0], true);
                if (town.Gold != c[1] - c[3] || hero.Gold != c[2] + c[3]
                    || (long)town.Gold + hero.Gold != (long)c[1] + c[2]
                    || events != 1 || eventAmount != -c[3])
                    throw new Exception("Native payment/event mismatch for request " + c[0] + ", cash " + c[1]);
            }
            return "PASS: native baseline reproduces 710 created gold; eight patched native settlement payments conserve cash; empty/full payer, zero, signed refunds, insufficient refund payer, native event amount/count.";
        }
        finally { harmony.UnpatchAll(harmony.Id); current.SetValue(null, previous); }
    }
}
