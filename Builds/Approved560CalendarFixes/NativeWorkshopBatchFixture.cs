// Offline verifier only; NOT included in the production project.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

public static class NativeWorkshopBatchFixture
{
    static Campaign campaign;
    static CampaignEventDispatcher dispatcher;
    static ItemObject input, output;
    static int quoteCalls, produced, consumed, selections;
    static int[] livePrices;
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Field(object o, string name, object value) { AccessTools.Field(o.GetType(), name).SetValue(o, value); }
    static bool Selected(ref EquipmentElement __result) { selections++; __result = new EquipmentElement(output); return false; }
    static bool Price(EquipmentElement __0, ref int __result)
    {
        if (__0.Item == input) __result = 10;
        else { __result = quoteCalls < 2 ? (quoteCalls == 0 ? 40 : 60) : livePrices[Math.Min(quoteCalls - 2, 1)]; quoteCalls++; }
        return false;
    }
    static bool Consumed() { consumed++; return false; }
    static bool InputPrice(ref int __result) { __result = 10; return false; }
    static bool Produced() { produced++; return false; }
    static bool Fits(ref bool __result) { __result = true; return false; }
    static bool NotFull(ref bool __result) { __result = false; return false; }
    static bool NoSkill() { return false; }
    static void Patch(Harmony harmony, MethodBase target, string method)
    {
        if (target == null) throw new Exception("Fixture native boundary missing: " + method);
        harmony.Patch(target, new HarmonyMethod(typeof(NativeWorkshopBatchFixture), method));
    }
    public static string Run(Action installDiagnostics)
    {
        var h = new Harmony("aoc.native.batch.fixture.boundaries");
        var currentField = AccessTools.Field(typeof(Campaign), "<Current>k__BackingField");
        var previousCampaign = currentField.GetValue(null);
        try
        {
            campaign = Blank<Campaign>(); dispatcher = Blank<CampaignEventDispatcher>();
            currentField.SetValue(null, campaign);
            Field(campaign, "GameStarted", true);
            Field(campaign, "<CampaignEventDispatcher>k__BackingField", dispatcher);
            Patch(h, AccessTools.Method(typeof(WorkshopsCampaignBehavior), "GetRandomItemAux"), "Selected");
            Patch(h, AccessTools.Method(typeof(Town), "GetItemPrice", new[] {typeof(EquipmentElement), typeof(MobileParty), typeof(bool)}), "Price");
            Patch(h, AccessTools.Method(typeof(Town), "GetItemPrice", new[] {typeof(ItemObject), typeof(MobileParty), typeof(bool)}), "InputPrice");
            Patch(h, AccessTools.Method(typeof(CampaignEventDispatcher), "OnItemConsumed"), "Consumed");
            Patch(h, AccessTools.Method(typeof(CampaignEventDispatcher), "OnItemProduced"), "Produced");
            Patch(h, AccessTools.Method(typeof(WorkshopsCampaignBehavior), "CanItemFitInWarehouse"), "Fits");
            Patch(h, AccessTools.Method(typeof(WorkshopsCampaignBehavior), "IsWarehouseAtLimit"), "NotFull");
            Patch(h, AccessTools.Method(typeof(TaleWorlds.CampaignSystem.CharacterDevelopment.SkillLevelingManager), "OnProductionProducedToWarehouse"), "NoSkill");
            if (installDiagnostics != null)
            {
                // Hook registration logs must not query an uninitialized campaign clock.
                currentField.SetValue(null, null);
                try { installDiagnostics(); }
                finally { currentField.SetValue(null, campaign); }
            }
            var categoryIn = new ItemCategory(); var categoryOut = new ItemCategory();
            input = new ItemObject(); output = new ItemObject();
            Field(input, "<ItemCategory>k__BackingField", categoryIn);
            Field(output, "<ItemCategory>k__BackingField", categoryOut);
            var town = Blank<Town>(); var settlement = Blank<Settlement>();
            settlement.Town = town;
            Field(settlement, "<SettlementComponent>k__BackingField", town);
            var party = Blank<PartyBase>();
            Field(party, "<Settlement>k__BackingField", settlement);
            Field(town, "_owner", party);
            var shop = Blank<Workshop>(); Field(shop, "_settlement", settlement);
            var workshopType = Blank<WorkshopType>(); Field(workshopType, "<IsHidden>k__BackingField", true);
            Field(shop, "<WorkshopType>k__BackingField", workshopType);
            var recipe = new WorkshopType.Production(4f);
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(), "_inputs").GetValue(recipe)).Add(ValueTuple.Create(categoryIn, 1));
            ((List<ValueTuple<ItemCategory,int>>)AccessTools.Field(recipe.GetType(), "_outputs").GetValue(recipe)).Add(ValueTuple.Create(categoryOut, 2));
            var behavior = Blank<WorkshopsCampaignBehavior>();
            var cycle = AccessTools.Method(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForNotableWorkshop");
            foreach (int cash in new[] {100, 99, 0})
            foreach (var prices in new[] {new[] {400,600}, new[] {4,6}})
            {
                var roster = new ItemRoster(); roster.AddToCounts(input, 1);
                Field(party, "<ItemRoster>k__BackingField", roster);
                Field(town, "<Gold>k__BackingField", cash); Field(shop, "<Capital>k__BackingField", 1000);
                quoteCalls = produced = consumed = selections = 0; livePrices = prices;
                bool accepted = (bool)cycle.Invoke(behavior, new object[] {recipe, shop, true});
                bool expected = cash >= 100;
                if (accepted != expected) throw new Exception("Native gate verdict differs");
                if (selections != 2) throw new Exception("Selected outputs more than once");
                if (town.Gold + shop.Capital != cash + 1000) throw new Exception("Native cash not conserved");
                if (expected)
                {
                    if (consumed != 1 || produced != 2 || quoteCalls != 4 || roster.GetItemNumber(input) != 0
                        || roster.GetItemNumber(output) != 2 || shop.Capital != 1090 || town.Gold != cash - 90)
                        throw new Exception("Native full batch failed inventory, events, quotes or full payment");
                }
                else if (consumed != 0 || produced != 0 || quoteCalls != 2 || roster.GetItemNumber(input) != 1
                         || roster.GetItemNumber(output) != 0 || shop.Capital != 1000 || town.Gold != cash)
                    throw new Exception("Native rejected batch mutated inventory/cash/events");
            }
            AccessTools.Property(typeof(ItemObject), "ItemType").SetValue(output, ItemObject.ItemTypeEnum.Goods, null);
            var dataType = typeof(WorkshopsCampaignBehavior).GetNestedType("WorkshopData", BindingFlags.NonPublic);
            var playerCycle = AccessTools.Method(typeof(WorkshopsCampaignBehavior), "TickOneProductionCycleForPlayerWorkshop");
            foreach (bool warehouseOnly in new[] {false, true})
            foreach (int cash in new[] {2000, 99})
            {
                var data = FormatterServices.GetUninitializedObject(dataType);
                Field(data, "Workshop", shop);
                Field(data, "IsGettingInputsFromWarehouse", warehouseOnly);
                Field(data, "StockProductionInWarehouseRatio", warehouseOnly ? 1f : 0f);
                var dataArray = Array.CreateInstance(dataType, 1); dataArray.SetValue(data, 0);
                Field(behavior, "_workshopData", dataArray);
                var market = new ItemRoster(); var warehouse = new ItemRoster();
                (warehouseOnly ? warehouse : market).AddToCounts(input, 1);
                Field(party, "<ItemRoster>k__BackingField", market);
                Field(behavior, "_warehouseRosterPerSettlement", new[] {new KeyValuePair<Settlement, ItemRoster>(settlement, warehouse)});
                Field(town, "<Gold>k__BackingField", cash); Field(shop, "<Capital>k__BackingField", 1000);
                quoteCalls = produced = consumed = selections = 0; livePrices = new[] {400, 600};
                bool accepted = (bool)playerCycle.Invoke(behavior, new object[] {recipe, shop, true});
                // Preserve the native player affordability gate, including warehouse behavior.
                bool expected = cash >= 100;
                if (accepted != expected || town.Gold + shop.Capital != cash + 1000)
                    throw new Exception("Native player gate/cash contract changed");
                if (!accepted)
                {
                    if ((warehouseOnly ? warehouse : market).GetItemNumber(input) != 1 || consumed != 0 || produced != 0)
                        throw new Exception("Rejected player cycle consumed inputs");
                }
                else if (warehouseOnly)
                {
                    if (warehouse.GetItemNumber(input) != 0 || warehouse.GetItemNumber(output) != 2
                        || market.GetItemNumber(output) != 0 || shop.Capital != 1000 || town.Gold != cash || quoteCalls != 2)
                        throw new Exception("Warehouse cycle leaked market payment or inventory");
                }
                else if (market.GetItemNumber(input) != 0 || market.GetItemNumber(output) != 2
                         || shop.Capital != 1990 || town.Gold != cash - 990 || quoteCalls != 4)
                    throw new Exception("Player market inherited AI locked prices");
            }
            return "PASS: six native AI and four native player cycles; cash gates, rising/falling AI prices, player dynamic prices, warehouse inventories, conservation and rejection safety.";
        }
        finally { currentField.SetValue(null, previousCampaign); h.UnpatchAll("aoc.native.batch.fixture.boundaries"); }
    }
}
