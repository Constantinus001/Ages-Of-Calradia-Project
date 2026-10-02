using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace AgesOfCalradiaSuccession
{
    // Bannerlord 1.4.8: AddDecision raises its event BEFORE enqueue/start.
    // Prefix that entry point, filtering exclusively KingSelectionKingdomDecision.
    // ApplyChosenOutcome is a second guard for elections already present in a
    // loaded save or invoked directly by another mod, not another resolver.
    // Missing targets abort installation; partial installation is removed.
    // Foreign patch owners are logged. No ordering can make arbitrary competing
    // ruler mutations safe; disposable-campaign compatibility tests remain required.
    internal static class SuccessionElectionPatches
    {
        internal const string Owner = "agesofcalradia.succession.ruler-election";
        private static readonly Harmony Patcher = new Harmony(Owner);

        internal static void Install()
        {
            MethodInfo add = AccessTools.DeclaredMethod(typeof(Kingdom), "AddDecision", new[] { typeof(KingdomDecision), typeof(bool) });
            MethodInfo apply = AccessTools.DeclaredMethod(typeof(KingSelectionKingdomDecision), "ApplyChosenOutcome", new[] { typeof(DecisionOutcome) });
            if (add == null || apply == null || add.ReturnType != typeof(void) || apply.ReturnType != typeof(void))
                throw new MissingMethodException("Bannerlord ruler-election signatures differ from the audited 1.4.8 targets.");
            try
            {
                InstallPrefix(add, "BeforeAddDecision");
                InstallPrefix(apply, "BeforeApplyChosenOutcome");
            }
            catch (Exception exception)
            {
                Patcher.Unpatch(add, HarmonyPatchType.All, Owner);
                Patcher.Unpatch(apply, HarmonyPatchType.All, Owner);
                SuccessionDiagnostics.Error("Ruler-election patch installation failed; own partial patches removed. Succession startup aborted.", exception);
                throw;
            }
        }

        private static void InstallPrefix(MethodInfo target, string prefix)
        {
            Patches patches = Harmony.GetPatchInfo(target);
            if (patches != null)
            {
                foreach (string owner in patches.Owners.Where(o => o != Owner))
                    SuccessionDiagnostics.Info("Ruler-election compatibility: " + target.DeclaringType.FullName + "." + target.Name + " also patched by " + owner + ".");
                if (patches.Prefixes.Any(p => p.owner == Owner)) return;
            }
            Patcher.Patch(target, prefix: new HarmonyMethod(typeof(SuccessionElectionPatches), prefix) { priority = Priority.First });
        }

        private static bool BeforeAddDecision(KingdomDecision __0)
        {
            KingSelectionKingdomDecision rulerDecision = __0 as KingSelectionKingdomDecision;
            if (rulerDecision == null) return true;
            SuccessionCampaignBehavior behavior = Campaign.Current == null ? null : Campaign.Current.GetCampaignBehavior<SuccessionCampaignBehavior>();
            return behavior == null || !behavior.InterceptRulerDecision(rulerDecision);
        }

        private static bool BeforeApplyChosenOutcome(KingSelectionKingdomDecision __instance)
        {
            SuccessionCampaignBehavior behavior = Campaign.Current == null ? null : Campaign.Current.GetCampaignBehavior<SuccessionCampaignBehavior>();
            return behavior == null || !behavior.InterceptRulerDecision(__instance);
        }
    }
}
