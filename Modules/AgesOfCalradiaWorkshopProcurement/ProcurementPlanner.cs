using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using System.Runtime.CompilerServices;

namespace AgesOfCalradia.WorkshopProcurement
{
    internal sealed class ProcurementOffer
    {
        internal ProcurementOrder Order;
        internal Town Source;
        internal List<ItemObject> Items = new List<ItemObject>();
        internal List<int> Stocks = new List<int>();
        internal int SourceGold;
        internal int Capital;
        internal double ForecastDailyRate;
        internal int ReorderThreshold;
        internal double SelectionScore;
        internal int ConservativeOutput;
        internal int Expense;
        internal float RequiredMargin;
        internal string DiagnosticPlanId; // Ephemeral observer correlation; never serialized or used for selection.
    }

    internal static class ProcurementPlanner
    {
        // Isolated native query boundaries, also used by deterministic planner fixtures.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static IEnumerable<Town> Suppliers() { return Town.AllTowns; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static ItemObject[] Catalog(ItemCategory category)
        { return MBObjectManager.Instance.GetObjectTypeList<ItemObject>().Where(i => i.ItemCategory == category).ToArray(); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float RouteDistance(Town source, Town destination)
        { return Campaign.Current.Models.MapDistanceModel.GetDistance(source.Settlement, destination.Settlement, false, false, MobileParty.NavigationType.Default); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool Safe(Town town)
        {
            return town != null && town.Settlement.IsTown && !town.Settlement.IsUnderSiege
                && town.FoodStocks > 0 && town.Settlement.MapFaction != null;
        }
        internal static int Count(Town town, ItemCategory category)
        {
            return town.Owner.ItemRoster.Where(x => x.EquipmentElement.Item.ItemCategory == category).Sum(x => x.Amount);
        }
        internal static bool CanTrade(Town source, Town destination)
        {
            return Safe(source) && Safe(destination)
                && !FactionManager.IsAtWarAgainstFaction(source.Settlement.MapFaction, destination.Settlement.MapFaction);
        }
        internal static int LocalRecipeUnits(Town town, ItemCategory category)
        {
            return town.Workshops.Where(w => w.WorkshopType != null)
                .SelectMany(w => w.WorkshopType.Productions).SelectMany(p => p.Inputs)
                .Where(i => i.Item1 == category).Sum(i => i.Item2);
        }
        // Read-only supplier demand, including hidden artisans. Reuse authoritative
        // calendar/model cadence; failed input attempts must not erase potential demand.
        // No native patch or persistent state. An unavailable rate blocks new offers.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static double LocalDailyUnits(Town town, ItemCategory category)
        {
            double total = 0;
            foreach (var localShop in town.Workshops.Where(w => w.WorkshopType != null))
                foreach (var recipe in localShop.WorkshopType.Productions)
                {
                    int units = recipe.Inputs.Where(i => i.Item1 == category).Sum(i => i.Item2);
                    if (units == 0) continue;
                    double rate = ProcurementCadence.SafeDailyRate(localShop, recipe);
                    if (units < 0 || double.IsNaN(rate) || double.IsInfinity(rate) || rate < 0) return double.NaN;
                    total += units * rate;
                    if (double.IsInfinity(total) || total > int.MaxValue) return double.NaN;
                }
            return total;
        }
        internal static bool Eligible(Workshop shop)
        {
            return shop != null && shop.Owner != null && shop.Owner != Hero.MainHero
                && shop.WorkshopType != null && !shop.WorkshopType.IsHidden && Safe(shop.Settlement.Town);
        }
        internal static ProcurementOffer Find(Workshop shop, double now, out string reason)
        {
            reason = "ineligible";
            if (!Eligible(shop)) return null;
            var computeTimer = System.Diagnostics.Stopwatch.StartNew();
            var witnesses = ProcurementDiagnostics.CandidateObservationEnabled
                ? new ProcurementWitnesses(shop.Settlement.StringId + "/" + shop.Tag, now) : null;
            int lotsEvaluated = 0;
            reason = "no_supported_shortage";
            ProcurementOffer best = null;
            var supplierIronRates = new Dictionary<Town, double>();
            int supplierCadenceRejected = 0;
            int foodRejected = 0, reserveRejected = 0, stockRejected = 0, leadSatisfied = 0;
            int tradeRejected = 0, routeRejected = 0, capitalRejected = 0, profitRejected = 0, capacityRejected = 0;
            int outputRejected = 0, townCashRejected = 0, supplierCashRejected = 0;
            for (int recipeIndex = 0; recipeIndex < shop.WorkshopType.Productions.Count; recipeIndex++)
            {
                var recipe = shop.WorkshopType.Productions[recipeIndex];
                if (recipe.Inputs.Count == 0 || recipe.Inputs.Count > 32
                    || recipe.Inputs.Any(x => x.Item2 <= 0 || x.Item2 > 333 || !x.Item1.IsTradeGood)
                    || recipe.Inputs.Select(x => x.Item1).Distinct().Count() != recipe.Inputs.Count
                    || recipe.Outputs.Count == 0 || recipe.Outputs.Any(x => !x.Item1.IsTradeGood || x.Item2 <= 0)
                    || recipe.ConversionSpeed <= 0 || float.IsNaN(recipe.ConversionSpeed) || float.IsInfinity(recipe.ConversionSpeed)) continue;
                // Never reserve more than one order's worth of shared market stock.
                if (ProcurementPolicy.Settings.MaximumAdaptiveBatches == 0
                    && recipe.Inputs.All(x => Count(shop.Settlement.Town, x.Item1) >= x.Item2 * ProcurementPolicy.BatchesPerOrder)) continue;
                double dailyRate = ProcurementCadence.SafeDailyRate(shop, recipe);
                if (double.IsNaN(dailyRate) || double.IsInfinity(dailyRate) || dailyRate <= 0)
                { reason = "invalid_or_inactive_recipe_cadence"; continue; }
                reason = "shortage_no_viable_supplier";
                // Conservative bound: every possible unmodified output must cover
                // landed cost plus the native profitability hurdle. No RNG call.
                int output = 0;
                bool knownOutput = true;
                foreach (var part in recipe.Outputs)
                {
                    var items = Catalog(part.Item1);
                    if (items.Length == 0) { knownOutput = false; break; }
                    int quote = items.Min(i => Math.Min(1000, shop.Settlement.Town.GetItemPrice(i)));
                    if (quote <= 0) { knownOutput = false; break; }
                    output = checked(output + quote * part.Item2);
                }
                if (!knownOutput) { outputRejected++; witnesses?.Record(recipeIndex, "none", 0, "output_quote_unavailable", "knownOutput", knownOutput); continue; }
                if (shop.Settlement.Town.Gold < output) { townCashRejected++; witnesses?.Record(recipeIndex, "none", 0, "destination_cash", "townGold", shop.Settlement.Town.Gold, "output", output); continue; }
                float requiredMargin = WineOperatingMargin.Margin(recipe, shop, true);
                foreach (Town source in Suppliers().OrderBy(t => t.Settlement.StringId, StringComparer.Ordinal))
                {
                    if (source == shop.Settlement.Town) continue;
                    if (!CanTrade(source, shop.Settlement.Town)) { tradeRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, 0,
                        "trade_safety", "predicate", "CanTrade_false_not_specific_war_proof"); continue; }
                    // Land cache, not straight-line distance; disconnected routes fail closed.
                    float distance = RouteDistance(source, shop.Settlement.Town);
                    if (float.IsNaN(distance) || distance <= 0 || distance > ProcurementPolicy.MaximumDistance)
                    { routeRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, 0,
                        float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0 ? "invalid_distance" : "distance_limit",
                        "distance", distance, "maximumDistance", ProcurementPolicy.MaximumDistance); continue; }
                    int threshold = ProcurementPolicy.ReorderBatches(dailyRate, ProcurementPolicy.TravelDays(distance));
                    if (recipe.Inputs.All(x => Count(shop.Settlement.Town, x.Item1) >= x.Item2 * threshold))
                    { leadSatisfied++; witnesses?.Record(recipeIndex, source.Settlement.StringId, 0, "lead_stock_satisfied", "thresholdBatches", threshold); continue; }
                    int requested = ProcurementPolicy.OrderBatches(dailyRate, ProcurementPolicy.TravelDays(distance));
                    // Search every bounded lot down to the configured minimum. Reserve and
                    // rounded freight/margin gates can reject BOTH endpoints while a middle
                    // lot is viable. Descending order retains larger lots on equal scores.
                    // Cache native quotes only within this read-only supplier/recipe search;
                    // additional lot sizes must not multiply calls for the same item.
                    var inputQuotes = new Dictionary<ItemObject, int>();
                    Func<ItemObject, int> quoteInput = item =>
                    {
                        int quote;
                        if (!inputQuotes.TryGetValue(item, out quote))
                            inputQuotes[item] = quote = source.GetItemPrice(item);
                        return quote;
                    };
                    for (int batches = requested; batches >= ProcurementPolicy.BatchesPerOrder; batches--)
                    {
                        lotsEvaluated++;
                        var offer = new ProcurementOffer { Source = source, SourceGold = source.Gold, Capital = shop.Capital,
                            ForecastDailyRate = dailyRate, ReorderThreshold = threshold, ConservativeOutput = output, Expense = shop.Expense,
                            RequiredMargin = requiredMargin,
                            Order = new ProcurementOrder { Town = shop.Settlement.StringId, Workshop = shop.Tag,
                                Type = shop.WorkshopType.StringId, Source = source.Settlement.StringId,
                                Recipe = recipeIndex, Quantity = batches,
                                OriginalQuantity = batches,
                                DepartureDay = now,
                                ReturnDelayDays = ProcurementPolicy.Settings.BlockedReturnDays,
                                StockLifetimeDays = ProcurementPolicy.Settings.StockLifetimeDays,
                                PolicyRevision = ProcurementPolicy.Settings.Revision,
                                ArrivalDay = now + ProcurementPolicy.TravelDays(distance) } };
                        int goods = 0;
                        int totalUnits = 0;
                        foreach (var input in recipe.Inputs)
                        {
                            int quantity = input.Item2 * batches;
                            bool food = input.Item1.Properties == ItemCategory.Property.BonusToFoodStores;
                            if (food && !ProcurementPolicy.FoodExportSafe(source.FoodStocks, source.FoodChange))
                            { foodRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches, "food_safety", "category", input.Item1.StringId, "foodStocks", source.FoodStocks, "foodChange", source.FoodChange); break; }
                            double supplierRate = double.NaN;
                            if (!food && input.Item1.StringId == "iron" && ProcurementPolicy.Settings.IronSupplierReserveDays > 0)
                            {
                                if (!supplierIronRates.TryGetValue(source, out supplierRate))
                                    supplierIronRates[source] = supplierRate = LocalDailyUnits(source, input.Item1);
                                if (double.IsNaN(supplierRate) || double.IsInfinity(supplierRate) || supplierRate < 0)
                                { supplierCadenceRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches,
                                    "supplier_cadence_unavailable", "category", input.Item1.StringId, "supplierDailyRate", supplierRate); break; }
                            }
                            int categoryStock = Count(source, input.Item1), localUnits = LocalRecipeUnits(source, input.Item1);
                            if (!ProcurementPolicy.HasSurplus(categoryStock, quantity,
                                localUnits, food, input.Item1.StringId, supplierRate))
                            { reserveRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches, "supplier_reserve",
                                "category", input.Item1.StringId, "categoryStock", categoryStock, "requestedUnits", quantity,
                                "localRecipeUnits", localUnits, "supplierDailyRate", supplierRate,
                                "policyRevision", ProcurementPolicy.Settings.Revision); break; }
                            var available = source.Owner.ItemRoster.Where(e => e.EquipmentElement.Item.ItemCategory == input.Item1
                                && e.Amount >= quantity && e.EquipmentElement.ItemModifier == null)
                                .Select(e => new { Entry=e, Quote=quoteInput(e.EquipmentElement.Item) })
                                .Where(e=>e.Quote>0).OrderBy(e => e.Quote)
                                .ThenBy(e => e.Entry.EquipmentElement.Item.StringId, StringComparer.Ordinal).ToArray();
                            if (available.Length == 0) { stockRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches,
                                "single_stack_or_quote", "category", input.Item1.StringId, "categoryStock", categoryStock,
                                "requestedUnits", quantity, "eligibleQuotedStacks", available.Length); break; }
                            var entry = available[0].Entry;
                            var item = entry.EquipmentElement.Item;
                            int unit = available[0].Quote;
                            if (unit <= 0 || (long)goods + (long)unit * quantity > int.MaxValue) { capacityRejected++; break; }
                            goods += unit * quantity;
                            totalUnits += quantity;
                            offer.Items.Add(item);
                            offer.Stocks.Add(entry.Amount);
                            offer.Order.Lines.Add(new ProcurementLine { Item = item.StringId, Category = input.Item1.StringId,
                                UnitsPerBatch = input.Item2, Remaining = quantity });
                        }
                        if (offer.Order.Lines.Count != recipe.Inputs.Count) continue;
                        if (totalUnits > 1000) { capacityRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches,
                            "cargo_limit", "totalUnits", totalUnits, "maximumUnits", 1000); continue; }
                        int freight = ProcurementPolicy.Freight(totalUnits, distance);
                        if (!ProcurementPolicy.Affordable(shop.Capital, shop.Expense, goods, freight))
                        { capitalRejected++; reason = "capital_reserve"; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches,
                            reason, "capital", shop.Capital, "expense", shop.Expense, "reserveDays", ProcurementPolicy.CapitalReserveDays,
                            "goods", goods, "freight", freight); continue; }
                        if (!ProcurementPolicy.Profitable(goods, freight, batches,
                            output, requiredMargin)) { profitRejected++; reason = "landed_cost_unprofitable";
                            witnesses?.Record(recipeIndex, source.Settlement.StringId, batches, reason, "goods", goods,
                                "freight", freight, "output", output, "requiredMargin", requiredMargin); continue; }
                        if ((long)source.Gold + goods + freight > int.MaxValue) { supplierCashRejected++; witnesses?.Record(recipeIndex, source.Settlement.StringId, batches,
                            "supplier_wallet_bound", "supplierGold", source.Gold, "goods", goods, "freight", freight); continue; }
                        int availableBatches = recipe.Inputs.Min(x => Count(shop.Settlement.Town, x.Item1) / x.Item2);
                        offer.SelectionScore = ProcurementPolicy.OfferScore(goods, freight, batches, shop.Expense,
                            ProcurementPolicy.TravelDays(distance), availableBatches, dailyRate);
                        if (double.IsInfinity(offer.SelectionScore) || (best != null && offer.SelectionScore >= best.SelectionScore)) continue;
                        offer.Order.GoodsCost = goods;
                        offer.Order.FreightCost = freight;
                        best = offer;
                    }
                }
            }
            if (best != null) reason = "offer";
            if (best != null && ProcurementDiagnostics.PlanObservationEnabled) best.DiagnosticPlanId = Guid.NewGuid().ToString("N");
            witnesses?.Finish();
            // Counts are rejected candidates, NOT independent shortages. One concise
            // daily decision receipt; preserve individual transaction accounting.
            string key = shop.Settlement.StringId + "/" + shop.Tag;
            string detail = "lotsEvaluated=" + lotsEvaluated + "; foodSafetyRejected=" + foodRejected + "; categoryReserveRejected=" + reserveRejected
                + "; supplierCadenceRejected=" + supplierCadenceRejected
                + "; ironSupplierReserveDays=" + ProcurementPolicy.Settings.IronSupplierReserveDays
                + "; maximumAdaptiveBatches=" + ProcurementPolicy.Settings.MaximumAdaptiveBatches
                + "; wineExpenseCoverage=" + ProcurementPolicy.Settings.WineExpenseCoverage.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "; deliveryDelayCostWeight=" + ProcurementPolicy.Settings.DeliveryDelayCostWeight.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "; plannerComputeMilliseconds=" + computeTimer.Elapsed.TotalMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + "; itemStockOrQuoteRejected=" + stockRejected + "; leadStockSatisfied=" + leadSatisfied
                + "; tradeSafetyRejected=" + tradeRejected + "; routeRejected=" + routeRejected
                + "; capitalRejected=" + capitalRejected + "; profitabilityRejected=" + profitRejected
                + "; cargoOrCostBoundRejected=" + capacityRejected + "; supplierWalletBoundRejected=" + supplierCashRejected
                + "; recipeOutputQuoteRejected=" + outputRejected + "; recipeTownCashRejected=" + townCashRejected
                + (best == null ? "" : "; plan=" + (best.DiagnosticPlanId ?? "unobserved") + "; selectedDailyRate=" + best.ForecastDailyRate.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "; selectedThresholdBatches=" + best.ReorderThreshold
                    + "; selectedSource=" + best.Order.Source + "; selectedRecipe=" + best.Order.Recipe
                    + "; selectedBatches=" + best.Order.OriginalQuantity
                    + "; selectedScoreEstimate=" + best.SelectionScore.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "; selectedConservativeOutput=" + best.ConservativeOutput
                    + "; selectedDailyExpense=" + best.Expense
                    + "; selectedRequiredMargin=" + best.RequiredMargin.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + "; selectedLandedCost=" + ((long)best.Order.GoodsCost + best.Order.FreightCost)
                    + "; selectedLeadDays=" + (best.Order.ArrivalDay - best.Order.DepartureDay).ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                + "; candidate_counts_not_shortages; policy=" + ProcurementPolicy.Settings.Revision;
            ProcurementLog.Write("PLAN", "shop=" + key + " reason=" + reason + "; " + detail);
            ProcurementDiagnostics.Plan(key, reason, detail);
            return best;
        }
    }
}
