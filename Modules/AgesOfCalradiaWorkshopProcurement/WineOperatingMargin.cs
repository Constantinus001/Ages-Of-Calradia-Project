using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using AgesOfCalradia.CampaignSystems;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.WorkshopProcurement
{
    // Bannerlord 1.4.8 CanNotableWorkshopProduceThisCycle: replace only the
    // native 200 / ConversionSpeed expression. All input, town cash, capital,
    // initialization and strict output comparison branches remain native.
    // Optional balance policy, NOT an accounting correction. No saved state.
    // Preflight requires the exact expression and rejects internal branch/EH
    // entry points. Loader rolls back ALL procurement hooks on mismatch.
    // Verify-NativeProcurement covers native gates and default-off behavior.
    [HarmonyPatch]
    internal static class WineOperatingMargin
    {
        [ThreadStatic] private static Workshop _approvalShop;
        [ThreadStatic] private static float _observedMargin;
        internal struct State { internal Workshop Shop; internal float Margin; }
        internal static void Prefix(Workshop __1, out State __state)
        {
            __state = new State { Shop = _approvalShop, Margin = _observedMargin };
            _approvalShop = __1; _observedMargin = float.NaN;
        }
        internal static void Finalizer(State __state)
        { _approvalShop = __state.Shop; _observedMargin = __state.Margin; }
        internal static double Observed(Workshop shop)
        { return shop != null && ReferenceEquals(shop, _approvalShop) ? _observedMargin : double.NaN; }
        internal static MethodInfo TargetMethod() { return PrepaidCapital.TargetMethod(); }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Enabled()
        {
            return CoreSystemsSubModule.Current != null && CoreSystemsSubModule.ConfigurationValid
                && Campaign.Current != null && Campaign.Current.GameStarted;
        }

        internal static float Margin(WorkshopType.Production recipe, Workshop shop, bool capitalCycle)
        {
            float result = Evaluate(recipe, shop, capitalCycle);
            if (ReferenceEquals(shop, _approvalShop)) _observedMargin = result;
            return result;
        }

        private static float Evaluate(WorkshopType.Production recipe, Workshop shop, bool capitalCycle)
        {
            float native = 200f / recipe.ConversionSpeed;
            double coverage = ProcurementPolicy.Settings.WineExpenseCoverage;
            if (coverage == 0 || !capitalCycle || !Enabled()) return native;
            try // Native object/model integration boundary; default native hurdle on failure.
            {
                if (shop == null || shop.Owner == null || shop.Owner == Hero.MainHero
                    || shop.WorkshopType == null || shop.WorkshopType.IsHidden
                    || shop.WorkshopType.Productions.Count != 1
                    || !ProcurementBehavior.SameRecipe(shop.WorkshopType.Productions[0], recipe)
                    || recipe.Outputs.Count != 1 || recipe.Outputs[0].Item1.StringId != "wine") return native;
                return Calculate(native, shop.Expense, ProcurementCadence.SafeDailyRate(shop, recipe), coverage);
            }
            catch (Exception ex)
            {
                ProcurementLog.Write("WINE_MARGIN_NATIVE_FALLBACK", ex.ToString());
                return native;
            }
        }

        internal static float Calculate(float native, int expense, double rate, double coverage)
        {
            if (coverage < 1 || coverage > 3 || double.IsNaN(coverage)
                || expense <= 0 || rate <= 0 || double.IsNaN(rate) || double.IsInfinity(rate)
                || native <= 0 || float.IsNaN(native) || float.IsInfinity(native)) return native;
            // Ceiling keeps the configured coverage floor despite float conversion.
            // Never increase the native hurdle or assume potential rate is realized output.
            return (float)Math.Min(native, Math.Ceiling(expense * coverage / rate));
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            var getter = AccessTools.PropertyGetter(typeof(WorkshopType.Production), "ConversionSpeed");
            var matches = Enumerable.Range(0, Math.Max(0, code.Count - 3)).Where(i =>
                code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, 200f)
                && code[i+1].opcode == OpCodes.Ldarga_S && Convert.ToInt32(code[i+1].operand) == 1
                && code[i+2].Calls(getter) && code[i+3].opcode == OpCodes.Div).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Wine margin native expression changed");
            int index = matches[0];
            if (code.Skip(index).Take(4).Any(i => i.blocks.Count != 0)
                || code.Skip(index+1).Take(3).Any(i => i.labels.Count != 0))
                throw new InvalidOperationException("Wine margin expression has internal control-flow entry");
            var first = new CodeInstruction(OpCodes.Ldarg_1);
            first.labels.AddRange(code[index].labels);
            code.RemoveRange(index, 4);
            code.InsertRange(index, new[] { first, new CodeInstruction(OpCodes.Ldarg_2),
                new CodeInstruction(OpCodes.Ldarg_S, (byte)5),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WineOperatingMargin), "Margin")) });
            return code;
        }
    }
}
