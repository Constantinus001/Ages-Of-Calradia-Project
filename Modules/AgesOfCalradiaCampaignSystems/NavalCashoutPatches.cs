using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.CampaignSystems
{
    // Version-bound 1.4.8 integration. See NAVAL_CASHOUT.md for instruction
    // contracts, ownership, failure semantics, and deterministic verification.
    internal static class NavalCashoutPatches
    {
        internal const string Owner = "AgesOfCalradia.CampaignSystems.NavalCashout.v1";
        private static readonly Harmony Harmony = new Harmony(Owner);
        private static readonly List<MethodBase> Installed = new List<MethodBase>();
        private static readonly List<MethodBase> Audited = new List<MethodBase>();
        private static readonly Dictionary<MethodBase, string> OwnBindings = new Dictionary<MethodBase, string>();
        private static long _nextCheck;
        private static MethodInfo _baseValue;
        internal static string Status = "disabled";

        internal static void Start(string path)
        {
            Stop();
            try
            {
                var settings = NavalCashoutSettings.Load(path);
                if (!settings.Enabled) { Status = "disabled; missing file or Enabled=false"; return; }
                Install(settings);
            }
            catch (Exception ex)
            {
                // File/reflection/Harmony boundary. Unpatch only this owner's
                // touched methods; leave procurement and duplicate guard intact.
                Stop(); Status = "rejected: " + ex.Message;
                System.Diagnostics.Trace.WriteLine("AOC naval cash-out disabled: " + ex);
            }
        }
        internal static void Install(NavalCashoutSettings settings)
        {
            var naval = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "NavalDLC");
            if (naval == null) throw new InvalidOperationException("NavalDLC absent");
            if (naval.ManifestModule.ModuleVersionId != new Guid("b60485da-883c-4a91-b58c-040702fe3ce9")
                || typeof(MapEvent).Module.ModuleVersionId != new Guid("886629fe-6e60-40d7-9a57-8d46017179d9"))
                throw new InvalidOperationException("Unsupported native build; all routes remain native");
            var model = naval.GetType("NavalDLC.GameComponents.NavalDLCShipCostModel", true);
            _baseValue = Require(model, "GetShipBaseValue", "42A88CE59B95A12A3A538DC1C6F530DFA01EAA3FD18B39BA5F0907AB5EE14C23");
            var trade = Require(model, "GetShipTradeValue", "F454FC87A68989F65FBCD437B30F40CF9A3A340EADDA13CC69C9F44C268BD8F1");
            Require(model, "GetShipRepairCost", "3E2080F20F87583FE9FE718B430FD73BF490B9C1C0F07891B0334A84EFA02BA7");
            Require(model, "GetShipUpgradePieceValueInternal", "0682DEC4D04AA668742F7CD5BB019B54CC827E0E1CDE265ACE03B2B3744AD2FA");
            var battle = Require(typeof(MapEvent), "LootDefeatedPartyShips", "04318B85CE580AF46318625E0AA5283824313EBA686FBB5A48D700F44C4C498C");
            var recovery = Require(naval.GetType("NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior", true),
                "RecoverGoldFromRemainingShipsAfterDistribution", "06DD0C596A5249F0F8752F5849C79559F602E792CEAC5C7F54C159B88489335A");
            // Evaluate both transformations before installing anything.
            TradeInstructions(PatchProcessor.GetOriginalInstructions(trade)).ToArray();
            BattleInstructions(PatchProcessor.GetOriginalInstructions(battle)).ToArray();
            NavalCashoutRuntime.NativeBase = (Func<Ship, bool, PartyBase, PartyBase, float>)Delegate.CreateDelegate(
                typeof(Func<Ship, bool, PartyBase, PartyBase, float>), _baseValue);
            NavalCashoutRuntime.ModelType = model;
            Patch(trade, "BeginQuote", "QuoteResult", "TradeInstructions", "EndQuote");
            Patch(recovery, "BeginRecovery", null, null, "EndRecovery");
            Patch(battle, "BeginBattle", null, "BattleInstructions", "EndBattle");
            NavalCashoutRuntime.Settings = settings; // Publish only after all installed.
            Status = "enabled; revision=" + settings.Revision + "; hullBasis=" + settings.HullBasis.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            System.Diagnostics.Trace.WriteLine("AOC naval cash-out " + Status);
        }
        internal static void Stop()
        {
            NavalCashoutRuntime.Reset();
            foreach (var method in Installed) Harmony.Unpatch(method, HarmonyPatchType.All, Owner);
            Installed.Clear(); Audited.Clear(); OwnBindings.Clear(); _nextCheck = 0; Status = "disabled";
        }
        // Application-thread idle boundary: never remove a patch while an active
        // naval policy scope is executing. Checks at most once per wall second.
        internal static void Tick()
        {
            if (!NavalCashoutRuntime.Settings.Enabled || NavalCashoutRuntime.HasOpenScope) return;
            long now = Stopwatch.GetTimestamp();
            if (now < _nextCheck) return;
            _nextCheck = now + Stopwatch.Frequency;
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.Models == null || campaign.Models.ShipCostModel == null) return;
            Revalidate(campaign.Models.ShipCostModel.GetType());
        }
        internal static void Revalidate(Type activeModel)
        {
            if (!NavalCashoutRuntime.Settings.Enabled || NavalCashoutRuntime.HasOpenScope) return;
            try
            {
                if (activeModel != NavalCashoutRuntime.ModelType) throw new InvalidOperationException("Active ship model changed");
                foreach (var method in Audited) CheckOwners(method);
                foreach (var method in Installed)
                {
                    var info = HarmonyLib.Harmony.GetPatchInfo(method);
                    if (info == null || BindingFingerprint(info) != OwnBindings[method]) throw new InvalidOperationException("Policy binding changed: " + method.Name);
                }
            }
            catch (Exception ex)
            {
                // Reflection/Harmony compatibility boundary. Latch off until next
                // campaign start; never remove the competing owner's patches.
                Stop(); Status = "rejected_late: " + ex.Message;
                System.Diagnostics.Trace.WriteLine("AOC naval cash-out " + Status);
                NavalCashoutObservation.Emit("compatibility", null, Status);
            }
        }
        private static MethodInfo Require(Type type, string name, string hash)
        {
            var method = AccessTools.Method(type, name);
            if (method == null || method.GetMethodBody() == null) throw new MissingMethodException(type.FullName, name);
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(method.GetMethodBody().GetILAsByteArray())).Replace("-", "") != hash)
                    throw new InvalidOperationException("Native IL changed: " + name);
            CheckOwners(method);
            Audited.Add(method);
            return method;
        }
        private static void CheckOwners(MethodBase method)
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(method);
            if (info != null && (info.Transpilers.Any(p => p.owner != Owner)
                || info.Owners.Any(o => o != Owner && o != "aoc.soak.economy.observer.v1" && o != "aoc.soak.supply.observer.v1")))
                throw new InvalidOperationException("Unreviewed competing patch: " + method.Name);
        }
        private static void Patch(MethodInfo target, string prefix, string postfix, string transpiler, string finalizer)
        {
            Installed.Add(target); // Track even a partially failed patch attempt.
            Harmony.Patch(target, H(prefix), H(postfix), transpiler == null ? null : new HarmonyMethod(typeof(NavalCashoutPatches), transpiler), H(finalizer));
            OwnBindings[target] = BindingFingerprint(HarmonyLib.Harmony.GetPatchInfo(target));
        }
        private static string BindingFingerprint(Patches info)
        {
            return string.Join("|", new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers }
                .Select((group, index) => index + ":" + string.Join(",", group.Where(p => p.owner == Owner)
                    .Select(p => p.PatchMethod.DeclaringType.FullName + "." + p.PatchMethod.Name).OrderBy(s => s))));
        }
        private static HarmonyMethod H(string name)
        { return name == null ? null : new HarmonyMethod(typeof(NavalCashoutRuntime), name); }

        internal static IEnumerable<CodeInstruction> TradeInstructions(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            var calls = code.Where(c => c.operand is MethodInfo && (MethodInfo)c.operand == _baseValue).ToArray();
            if (calls.Length != 1) throw new InvalidOperationException("Expected one native trade base calculation");
            calls[0].opcode = OpCodes.Call;
            calls[0].operand = AccessTools.Method(typeof(NavalCashoutRuntime), "BaseValue");
            return code;
        }
        internal static IEnumerable<CodeInstruction> BattleInstructions(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            int quote = code.FindIndex(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "GetShipTradeValue");
            var setter = AccessTools.PropertySetter(typeof(MapEventParty), "PlunderedGold");
            int allocation = code.FindIndex(c => Equals(c.operand, setter));
            if (quote < 0 || allocation < 0 || code.Count(c => Equals(c.operand, setter)) != 1
                || code.Count(c => c.operand is MethodInfo && ((MethodInfo)c.operand).Name == "GetShipTradeValue") != 1)
                throw new InvalidOperationException("Battle cash-out instruction contract changed");
            code[quote].opcode = OpCodes.Call;
            code[quote].operand = AccessTools.Method(typeof(NavalCashoutRuntime), "BattleQuote");
            // Native IL is hash-bound: local 11 is the exact native contribution
            // sum. Do not invoke the reward model again to rebuild winner lists.
            var load = new CodeInstruction(OpCodes.Ldloc_S, (byte)11);
            load.labels.AddRange(code[allocation].labels); code[allocation].labels.Clear();
            load.blocks.AddRange(code[allocation].blocks); code[allocation].blocks.Clear();
            code.Insert(allocation, load);
            code[allocation + 1].opcode = OpCodes.Call;
            code[allocation + 1].operand = AccessTools.Method(typeof(NavalCashoutRuntime), "Allocate");
            return code;
        }
    }
}
