using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Daily state/recipe definitions only. No native production/finance re-evaluation.
    internal static class EconomyStateDiagnostics
    {
        private static double _lastDay;
        private static long _lastWall;
        private static EconomyStateHistory _history = new EconomyStateHistory();
        internal static void Reset() { _lastWall = 0; _lastDay = 0; _history = new EconomyStateHistory(); }
        internal static void Snapshot()
        {
            double day = CampaignTime.Now.ToDays;
            long wall = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastWall != 0 && day > _lastDay)
            {
                double seconds = (wall - _lastWall) / (double)System.Diagnostics.Stopwatch.Frequency;
                State("ECONOMY_PACING", "campaign", "wall_seconds_per_campaign_day", seconds / (day - _lastDay),
                    "daySpan=" + (day - _lastDay).ToString("R", CultureInfo.InvariantCulture) + "; wallSeconds=" + seconds.ToString("R", CultureInfo.InvariantCulture)
                    + "; includes_pauses_and_stalls; mode=" + Campaign.Current.TimeControlMode);
            }
            _lastDay = day; _lastWall = wall;
            foreach (object model in new object[] { Campaign.Current.Models.WorkshopModel, Campaign.Current.Models.ClanFinanceModel, Campaign.Current.Models.SettlementFoodModel })
                State("MODEL_IDENTITY", model.GetType().FullName, "identity", 0, "assembly=" + model.GetType().Assembly.FullName + "; mvid=" + model.GetType().Module.ModuleVersionId);
            int towns = 0, shops = 0;
            var categories = new HashSet<ItemCategory>();
            foreach (Town town in Town.AllTowns)
                foreach (var shop in town.Workshops)
                    foreach (var recipe in shop.WorkshopType.Productions)
                    {
                        foreach (var input in recipe.Inputs) categories.Add(input.Item1);
                        foreach (var output in recipe.Outputs) categories.Add(output.Item1);
                    }
            categories.Add(DefaultItemCategories.Grain); categories.Add(DefaultItemCategories.Fish);
            foreach (Town town in Town.AllTowns)
            {
                towns++;
                string owner = EconomyTransactionDiagnostics.Id(town);
                State("TOWN_CONTEXT", owner, "loyalty", town.Loyalty, "rebellion=" + town.InRebelliousState
                    + "; siege=" + (town.Settlement.SiegeEvent != null) + "; owner=" + EconomyTransactionDiagnostics.Id(town.Settlement.OwnerClan));
                foreach (ItemCategory category in categories)
                {
                    var entries = town.Settlement.ItemRoster.Where(x => x.EquipmentElement.Item != null && x.EquipmentElement.Item.ItemCategory == category).ToArray();
                    int count = entries.Sum(x => x.Amount);
                    // Quotes are for concrete roster entries, never invented for missing stock.
                    string quotes = string.Join(",", entries.Select(x => x.EquipmentElement.Item.StringId + ":stock=" + x.Amount
                        + ":buy=" + town.GetItemPrice(x.EquipmentElement.Item, null, false) + ":sell=" + town.GetItemPrice(x.EquipmentElement.Item, null, true)));
                    State("MARKET_STOCK", owner, category.StringId, count, "category=" + category.Properties + "; entries=" + entries.Length
                        + "; quotes=" + (entries.Length == 0 ? "unavailable" : quotes));
                    double span = _history.Observe(owner + "/" + category.StringId, day, count);
                    State("MARKET_SHORTAGE", owner, category.StringId, span, "zero=" + (count == 0) + "; span_between_consecutive_zero_samples_not_continuous_proof");
                }
                foreach (var shop in town.Workshops)
                {
                    shops++;
                    string id = EconomyTransactionDiagnostics.Id(shop);
                    State("WORKSHOP_STATE", id, "capital", shop.Capital, "type=" + shop.WorkshopType.StringId + "; owner=" + EconomyTransactionDiagnostics.Id(shop.Owner)
                        + "; hidden=" + shop.WorkshopType.IsHidden + "; initialCapital=" + shop.InitialCapital + "; capitalSurplusOverInitial=" + shop.ProfitMade);
                    for (int i = 0; i < shop.WorkshopType.Productions.Count; i++)
                    {
                        var p = shop.WorkshopType.Productions[i];
                        State("RECIPE_DEFINITION", id, "recipe:" + i, p.ConversionSpeed, "type=" + shop.WorkshopType.StringId
                            + "; inputs=" + string.Join(",", p.Inputs.Select(x => x.Item1.StringId + ":" + x.Item2 + ":" + x.Item1.Properties))
                            + "; outputs=" + string.Join(",", p.Outputs.Select(x => x.Item1.StringId + ":" + x.Item2 + ":" + x.Item1.Properties)));
                    }
                }
            }
            foreach (Village village in Village.All)
                State("VILLAGE_STATE", EconomyTransactionDiagnostics.Id(village), "hearth", village.Hearth, "state=" + village.VillageState
                    + "; tradeBound=" + EconomyTransactionDiagnostics.Id(village.TradeBound)
                    + "; outputs=" + string.Join(",", village.VillageType.Productions.Select(x => x.Item1.StringId + ":base=" + x.Item2.ToString("R", CultureInfo.InvariantCulture))));
            foreach (Clan clan in Clan.All)
                if (clan.Leader != null) State("CLAN_BALANCE", EconomyTransactionDiagnostics.Id(clan), "leaderGold", clan.Leader.Gold,
                    "leader=" + EconomyTransactionDiagnostics.Id(clan.Leader) + "; kingdom=" + EconomyTransactionDiagnostics.Id(clan.Kingdom));
            foreach (Kingdom kingdom in Kingdom.All)
                State("KINGDOM_BALANCE", EconomyTransactionDiagnostics.Id(kingdom), "budget", kingdom.KingdomBudgetWallet, "");
            State("STATE_COVERAGE", "campaign", "towns", towns, "workshops=" + shops + "; categories=" + categories.Count + "; complete=True");
            State("TREATY_STATE", "opening-peace", "context", 0, TreatyStateDiagnostics.Read());
        }
        private static void State(string kind, string owner, string metric, double value, string detail)
        {
            EconomyTrace.Write(kind, 0, 0, owner, metric, value, value, "daily-state", detail);
        }
    }
}
