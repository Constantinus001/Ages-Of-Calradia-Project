using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements.Workshops;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Bannerlord 1.4.8: change only the base-speed getter immediately feeding
    // the native model in RunTownWorkshop and both warehouse estimates.
    // Production is a struct: the replacement preserves its managed-ref stack
    // signature, labels and exception blocks. Native perks, loops, input tests,
    // progress caps and batch ratios remain in the original method.
    // Preflight all three targets before PatchAll/legacy unpatching. A changed
    // call pattern throws and the existing module-load boundary logs/rolls back.
    [HarmonyPatch]
    internal static class WorkshopRecipeCadenceFix
    {
        private static readonly MethodInfo Getter = AccessTools.PropertyGetter(typeof(WorkshopType.Production), "ConversionSpeed");
        private static readonly MethodInfo Model = AccessTools.Method(typeof(WorkshopModel), "GetEffectiveConversionSpeedOfProduction");
        private static readonly MethodInfo Replacement = AccessTools.Method(typeof(WorkshopRecipeCadenceFix), "RecipeBaseSpeed");

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var type = typeof(WorkshopsCampaignBehavior);
            yield return AccessTools.Method(type, "RunTownWorkshop") ?? throw new MissingMethodException(type.FullName, "RunTownWorkshop");
            foreach (string suffix in new[] { ".GetInputDailyChange", ".GetOutputDailyChange" })
            {
                var method = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                    .SingleOrDefault(x => x.Name.EndsWith(suffix, StringComparison.Ordinal));
                if (method == null) throw new MissingMethodException(type.FullName, suffix);
                yield return method;
            }
        }

        internal static void ValidateTargets()
        {
            foreach (var method in TargetMethods())
                Transpiler(PatchProcessor.GetOriginalInstructions(method)).ToArray();
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(x => new CodeInstruction(x)).ToList();
            var matches = Enumerable.Range(0, code.Count).Where(i => code[i].Calls(Getter)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Recipe cadence requires exactly one native base-speed getter per target.");
            int index = matches[0];
            if (index + 2 >= code.Count || code[index + 1].opcode != OpCodes.Ldc_I4_0 || !code[index + 2].Calls(Model))
                throw new InvalidOperationException("Recipe cadence native model-call pattern changed; refusing partial adjustment.");
            code[index].opcode = OpCodes.Call;
            code[index].operand = Replacement;
            return code;
        }

        private static float RecipeBaseSpeed(ref WorkshopType.Production production)
        {
            // No shared mutable recipe context and no second model invocation.
            return ScaleRecipe(production, ApprovedCalendarBridge.AnnualEnabled, ApprovedCalendarBridge.Factor);
        }

        internal static float ScaleRecipe(WorkshopType.Production production, bool enabled, float factor)
        {
            if (!enabled) return production.ConversionSpeed;
            bool food = production.Outputs.Any(x => VanillaFoodCadence.IsFood(x.Item1));
            return WorkshopProductionFix.ScaleBaseSpeedForVerification(production.ConversionSpeed, food, factor);
        }
    }
}
