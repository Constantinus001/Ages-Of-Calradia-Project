using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

public static partial class NavalCashoutVerifier
{
    static Assembly Module;
    static int Count, Bases, Repairs;
    static float RepairValue = 12.5f;
    static Clan Player, Ai;
    static readonly Dictionary<PartyBase, MobileParty> Mobiles = new Dictionary<PartyBase, MobileParty>();
    static readonly Dictionary<MobileParty, Clan> Clans = new Dictionary<MobileParty, Clan>();
    static readonly Dictionary<MapEventParty, PartyBase> Parties = new Dictionary<MapEventParty, PartyBase>();
    static readonly Dictionary<MapEventParty, int> Contributions = new Dictionary<MapEventParty, int>();
    static PartyBase Seller;
    static Settlement Port;
    static Campaign World;
    static GameModels Models;
    static ShipCostModel Cost;
    static Type T(string name) { return Module.GetType("AgesOfCalradia.CampaignSystems." + name, true); }
    static object Call(string type, string name, params object[] args)
    { return AccessTools.Method(T(type), name).Invoke(null, args); }
    static void Set(string type, string field, object value) { AccessTools.Field(T(type), field).SetValue(null, value); }
    static object Get(string type, string field) { return AccessTools.Field(T(type), field).GetValue(null); }
    static T Blank<T>() { return (T)FormatterServices.GetUninitializedObject(typeof(T)); }
    static void Check(bool test, string name) { if (!test) throw new Exception(name); Count++; }
    static void Equal(float expected, float actual, string name) { Check(Math.Abs(expected - actual) < 0.001f, name + " expected=" + expected + " actual=" + actual); }
    static object Settings(bool enabled, float factor)
    { return Activator.CreateInstance(T("NavalCashoutSettings"), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { enabled, factor }, null); }
    static void Prefix(Harmony h, Type type, string getter, string fixture)
    { h.Patch(AccessTools.PropertyGetter(type, getter), new HarmonyMethod(typeof(NavalCashoutVerifier), fixture)); }
    static bool PlayerClan(ref Clan __result) { __result = Player; return false; }
    static bool ActualClan(MobileParty __instance, ref Clan __result) { __result = Clans[__instance]; return false; }
    static bool Mobile(PartyBase __instance, ref MobileParty __result) { Mobiles.TryGetValue(__instance, out __result); return false; }
    static bool IsMobile(PartyBase __instance, ref bool __result) { __result = Mobiles.ContainsKey(__instance); return false; }
    static bool IsSettlement(PartyBase __instance, ref bool __result) { __result = !Mobiles.ContainsKey(__instance); return false; }
    static bool Settlement(ref Settlement __result) { __result = Port; return false; }
    static bool OwnerClan(ref Clan __result) { __result = Ai; return false; }
    static bool Kingdom(ref Kingdom __result) { __result = null; return false; }
    static bool False(ref bool __result) { __result = false; return false; }
    static bool ShipOwner(Ship __instance, ref PartyBase __result) { if (!ShipOwners.TryGetValue(__instance, out __result)) __result = Seller; return false; }
    static bool Hull(ref int __result) { __result = 10000; return false; }
    static bool Repair(ref float __result) { Repairs++; __result = RepairValue; return false; }
    static bool Party(MapEventParty __instance, ref PartyBase __result) { __result = Parties[__instance]; return false; }
    static bool Contribution(MapEventParty __instance, ref int __result) { __result = Contributions[__instance]; return false; }
    static bool MobilePartyBase(MobileParty __instance, ref PartyBase __result) { __result = Mobiles.First(p => p.Value == __instance).Key; return false; }
    static bool Id(ref string __result) { __result = "fixture"; return false; }
    static bool SkipQuote(ref float __result) { __result = 123; return false; }
    static void ThrowQuote() { throw new InvalidOperationException("fixture native boundary failure"); }
    static void Recompile() { }
    static bool Current(ref Campaign __result) { __result = World; return false; }
    static bool GameModels(ref GameModels __result) { __result = Models; return false; }
    static bool ShipModel(ref ShipCostModel __result) { __result = Cost; return false; }
    static float Base(Ship ship, bool discount, PartyBase owner, PartyBase buyer) { Bases++; return discount ? 600 : 10500; }
    static PartyBase MakeParty(Clan clan)
    { var p = Blank<PartyBase>(); var m = Blank<MobileParty>(); Mobiles[p] = m; Clans[m] = clan; return p; }
    static MapEventParty Winner(PartyBase p, int contribution, int gold)
    { var w = Blank<MapEventParty>(); Parties[w] = p; Contributions[w] = contribution; w.PlunderedGold = gold; return w; }

    public static void Run(string path)
    {
        Module = Assembly.LoadFrom(path);
        foreach (float factor in new[] { 0.01f, 0.3f, 1f })
        {
            object[] args = { 10500f, 10000f, false, factor, 0f };
            Check((bool)Call("NavalCashoutPolicy", "TryBase", args), "valid base");
            Equal(500 + 10000 * factor, (float)args[4], "upgrades preserved separately");
        }
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1f, 9999f })
        { object[] args = { value, 10000f, false, 0.01f, 0f }; Check(!(bool)Call("NavalCashoutPolicy", "TryBase", args), "bad base rejected"); }
        object[] discounted = { 600f, 10000f, true, 0.01f, 0f };
        Check((bool)Call("NavalCashoutPolicy", "TryBase", discounted), "already discounted valid");
        Equal(600, (float)discounted[4], "no double discount");
        foreach (float factor in new[] { 0f, 1.1f, float.NaN, float.PositiveInfinity })
        { bool rejected = false; try { Settings(true, factor); } catch (TargetInvocationException) { rejected = true; } Check(rejected, "invalid setting"); }
        foreach (string xml in new[] { "<NavalEconomy SchemaVersion='2' Enabled='true' HullBasis='.01'/>",
            "<NavalEconomy SchemaVersion='1' Enabled='true' HullBasis='.01' Extra='x'/>",
            "<NavalEconomy SchemaVersion='1' Enabled='true' HullBasis='.01'><Procurement/></NavalEconomy>",
            "<NavalEconomy SchemaVersion='1' Enabled='true'/>" })
        { bool rejected = false; try { Call("NavalCashoutSettings", "Parse", XDocument.Parse(xml)); } catch (TargetInvocationException) { rejected = true; } Check(rejected, "invalid schema"); }
        var a = Settings(true, .01f); var b = Call("NavalCashoutSettings", "Parse", XDocument.Parse("<NavalEconomy SchemaVersion='1' Enabled='true' HullBasis='0.010'/ >".Replace("/ >", "/>")));
        Check(AccessTools.Field(T("NavalCashoutSettings"), "Revision").GetValue(a).Equals(AccessTools.Field(T("NavalCashoutSettings"), "Revision").GetValue(b)), "canonical revision");
        object[] share = { 100, 1, 3, (long)901, 0 };
        Check((bool)Call("NavalCashoutPolicy", "TryShare", share) && (int)share[4] == 400, "native floor and prior balance");
        object[] overflow = { int.MaxValue, 1, 1, (long)1, 0 };
        Check(!(bool)Call("NavalCashoutPolicy", "TryShare", overflow), "overflow rejected");

        var h = new Harmony("aoc.naval.cashout.fixture");
        try
        {
            // Real native IL is transformed and Harmony compiles both transpilers.
            Player = Blank<Clan>(); Ai = Blank<Clan>(); Port = Blank<Settlement>();
            Prefix(h, typeof(Clan), "PlayerClan", "PlayerClan"); Prefix(h, typeof(Clan), "IsBanditFaction", "False");
            Prefix(h, typeof(Clan), "Kingdom", "Kingdom"); Prefix(h, typeof(MobileParty), "ActualClan", "ActualClan");
            Prefix(h, typeof(MobileParty), "IsCaravan", "False"); Prefix(h, typeof(MobileParty), "IsVillager", "False");
            Prefix(h, typeof(MobileParty), "Party", "MobilePartyBase");
            Prefix(h, typeof(PartyBase), "MobileParty", "Mobile"); Prefix(h, typeof(PartyBase), "IsMobile", "IsMobile");
            Prefix(h, typeof(PartyBase), "IsSettlement", "IsSettlement"); Prefix(h, typeof(PartyBase), "Settlement", "Settlement");
            Prefix(h, typeof(PartyBase), "Id", "Id"); Prefix(h, typeof(Settlement), "OwnerClan", "OwnerClan");
            Prefix(h, typeof(Ship), "Owner", "ShipOwner"); Prefix(h, typeof(ShipHull), "Value", "Hull");
            Prefix(h, typeof(MapEventParty), "Party", "Party"); Prefix(h, typeof(MapEventParty), "ContributionToBattle", "Contribution");
            Call("NavalCashoutPatches", "Install", a);
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("enabled"), "all routes installed");
            Set("NavalCashoutRuntime", "NativeBase", new Func<Ship, bool, PartyBase, PartyBase, float>(Base));
            var modelType = (Type)Get("NavalCashoutRuntime", "ModelType");
            var model = (ShipCostModel)FormatterServices.GetUninitializedObject(modelType);
            Cost = model; World = Blank<Campaign>(); Models = Blank<GameModels>();
            Prefix(h, typeof(Campaign), "Current", "Current"); Prefix(h, typeof(Campaign), "Models", "GameModels");
            Prefix(h, typeof(GameModels), "ShipCostModel", "ShipModel");
            h.Patch(AccessTools.Method(modelType, "GetShipRepairCost"), new HarmonyMethod(typeof(NavalCashoutVerifier), "Repair"));
            h.Patch(AccessTools.Method(modelType, "GetShipTradeValue"), postfix: new HarmonyMethod(typeof(NavalCashoutVerifier), "Recompile"));
            var ship = Blank<Ship>(); AccessTools.Field(typeof(Ship), "ShipHull").SetValue(ship, Blank<ShipHull>());
            Seller = MakeParty(Ai); var player = MakeParty(Player); var town = Blank<PartyBase>();
            Equal(257.5f, model.GetShipTradeValue(ship, Seller, town), "real native resale path, repair once");
            Check(Bases == 1 && Repairs == 1, "no duplicate base/repair getters");
            RepairValue = 0;
            Equal(270, model.GetShipTradeValue(ship, Seller, town), "undamaged resale");
            RepairValue = 1000;
            Equal(0, model.GetShipTradeValue(ship, Seller, town), "damage cannot reverse payment");
            RepairValue = 12.5f;
            Equal(4712.5f, model.GetShipTradeValue(ship, player, town), "player resale unchanged");
            Equal(900f, model.GetShipTradeValue(ship, town, Seller), "AI purchase unchanged");
            Equal(15750f, model.GetShipTradeValue(ship, Seller, null), "unscoped null-buyer unchanged");
            object[] recovery = { Mobiles[Seller], null };
            Call("NavalCashoutRuntime", "BeginRecovery", recovery);
            Equal(900, model.GetShipTradeValue(ship, Seller, null), "scoped recovery");
            var error = new Exception("fixture original failure");
            Check(ReferenceEquals(error, Call("NavalCashoutRuntime", "EndRecovery", recovery[1], error)), "recovery exception preserved");
            Equal(15750f, model.GetShipTradeValue(ship, Seller, null), "recovery scope cleaned");
            object[] battle = { null }; Call("NavalCashoutRuntime", "BeginBattle", battle);
            Equal(15750, (float)Call("NavalCashoutRuntime", "BattleQuote", model, ship, Seller, null), "battle native pool preserved");
            var aiWinner = Winner(Seller, 1, 100); var playerWinner = Winner(player, 2, 50);
            Call("NavalCashoutRuntime", "Allocate", aiWinner, 5350, 3);
            Call("NavalCashoutRuntime", "Allocate", playerWinner, 3200, 3);
            Check(aiWinner.PlunderedGold == 400, "AI corrected allocation retains nonship gold");
            Check(playerWinner.PlunderedGold == 3200, "player native allocation unchanged");
            object[] nested = { null }; Call("NavalCashoutRuntime", "BeginBattle", nested);
            Call("NavalCashoutRuntime", "EndBattle", nested[0], null);
            Check(ReferenceEquals(Get("NavalCashoutRuntime", "_battle"), battle[0]), "nested battle restores outer");
            Check(ReferenceEquals(error, Call("NavalCashoutRuntime", "EndBattle", battle[0], error)), "battle exception preserved");
            Check(Get("NavalCashoutRuntime", "_quote") == null && Get("NavalCashoutRuntime", "_battle") == null, "scopes cleared");
            EndToEnd(h, modelType, model, Seller, player, ship);
            h.Patch(AccessTools.Method(modelType, "GetShipTradeValue"), new HarmonyMethod(typeof(NavalCashoutVerifier), "SkipQuote"));
            Equal(123, model.GetShipTradeValue(ship, Seller, town), "original skip preserved");
            Check(Get("NavalCashoutRuntime", "_quote") == null, "skip leaves no quote context");
            object[] skippedBattle = { null }; Call("NavalCashoutRuntime", "BeginBattle", skippedBattle);
            Equal(123, (float)Call("NavalCashoutRuntime", "BattleQuote", model, ship, Seller, null), "skipped quote not reevaluated");
            Call("NavalCashoutRuntime", "Allocate", aiWinner, 999, 3);
            Check(aiWinner.PlunderedGold == 999, "invalid shadow pool keeps entire native allocation");
            Call("NavalCashoutRuntime", "EndBattle", skippedBattle[0], null);
            h.Unpatch(AccessTools.Method(modelType, "GetShipTradeValue"), AccessTools.Method(typeof(NavalCashoutVerifier), "SkipQuote"));
            h.Patch(AccessTools.Method(modelType, "GetShipTradeValue"), prefix: new HarmonyMethod(typeof(NavalCashoutVerifier), "ThrowQuote"));
            bool failed = false;
            try { Call("NavalCashoutRuntime", "BattleQuote", model, ship, Seller, null); }
            catch (TargetInvocationException ex) { failed = ex.InnerException is InvalidOperationException; }
            Check(failed, "native failure not suppressed");
            Check(Get("NavalCashoutRuntime", "_quote") == null && Get("NavalCashoutRuntime", "_battleRequest") == null, "exception cleans contexts");
            object[] busy = { null }; Call("NavalCashoutRuntime", "BeginBattle", busy);
            Call("NavalCashoutPatches", "Revalidate", modelType);
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("enabled"), "late check deferred inside active operation");
            Call("NavalCashoutRuntime", "EndBattle", busy[0], null);
            Call("NavalCashoutPatches", "Revalidate", modelType);
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("rejected_late:"), "late competing patch latches off");
            Check(Harmony.GetPatchInfo(AccessTools.Method(modelType, "GetShipTradeValue")).Owners.Contains(h.Id), "late shutdown preserves competing patch");
            // Startup rejects a competing writer before installing any route.
            string config = Path.GetTempFileName();
            try
            {
                File.WriteAllText(config, "<NavalEconomy SchemaVersion='1' Enabled='true' HullBasis='.01'/>");
                Call("NavalCashoutPatches", "Start", config);
                Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("rejected:"), "conflicting patch fails closed");
                Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains("AgesOfCalradia.CampaignSystems.NavalCashout.v1")), "conflict leaves no partial policy");
                Check(Harmony.GetPatchInfo(AccessTools.Method(modelType, "GetShipTradeValue")).Owners.Contains(h.Id), "other owner's patches preserved");
                File.WriteAllText(config, "<broken/>"); Call("NavalCashoutPatches", "Start", config);
                Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("rejected:"), "invalid file disables naval only");
                File.Delete(config); Call("NavalCashoutPatches", "Start", config);
                Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("disabled"), "missing settings native");
            }
            finally { if (File.Exists(config)) File.Delete(config); }
        }
        finally { Call("NavalCashoutPatches", "Stop"); h.UnpatchAll(h.Id); }
        Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains("AgesOfCalradia.CampaignSystems.NavalCashout.v1")), "own patches removed");
        try
        {
            Call("NavalCashoutPatches", "Install", a);
            var actualType = (Type)Get("NavalCashoutRuntime", "ModelType");
            Call("NavalCashoutPatches", "Revalidate", actualType);
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("enabled"), "unchanged binding check retains policy");
            h.Unpatch(AccessTools.Method(actualType, "GetShipTradeValue"), AccessTools.Method(T("NavalCashoutRuntime"), "BeginQuote"));
            Call("NavalCashoutPatches", "Revalidate", actualType);
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("rejected_late:"), "missing individual prefix detected while other owned patches remain");
            Call("NavalCashoutPatches", "Install", a);
            Call("NavalCashoutPatches", "Revalidate", typeof(object));
            Check(((string)Get("NavalCashoutPatches", "Status")).StartsWith("rejected_late:"), "replacement active model rejected");
        }
        finally { Call("NavalCashoutPatches", "Stop"); }
        Console.WriteLine("PASS: " + Count + " naval cash-out assertions; native trade pipeline and actual transpiler installation exercised.");
    }
}
