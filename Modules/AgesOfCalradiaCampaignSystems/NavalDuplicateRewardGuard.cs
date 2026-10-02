using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.CampaignSystems
{
    // Bannerlord 1.4.8 NavalDLC calls distribution from both disband and destroy
    // callbacks while the same party still owns its ships. Gate only a completed
    // distribution for that party; do not price ships, alter the first native
    // distribution, or suppress a native exception.
    internal static class NavalDuplicateRewardGuard
    {
        internal const string HarmonyId = "AgesOfCalradia.CampaignSystems.NavalDuplicateRewardGuard.v1";
        private sealed class Gate { internal bool Entered; }
        private static ConditionalWeakTable<object, Gate> _gates = new ConditionalWeakTable<object, Gate>();
        private static MethodBase _target;

        internal static void Reset() { _gates = new ConditionalWeakTable<object, Gate>(); }

        // Kept object-based for deterministic fixture coverage without creating
        // a Bannerlord MobileParty. Campaign calls remain main-thread only.
        internal static bool TryEnter(object party)
        {
            if (party == null) return true;
            Gate ignored;
            if (_gates.TryGetValue(party, out ignored)) return false;
            _gates.Add(party, new Gate { Entered = true });
            return true;
        }

        internal static void Finish(object party, Exception error)
        {
            if (party == null || error == null) return;
            // A failed native first attempt must remain visible and may be retried
            // by native control flow; never convert its exception into success.
            _gates.Remove(party);
        }

        internal static void Install(Harmony harmony)
        {
            Reset();
            if (_target != null) return;
            try
            {
                Assembly naval = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (assembly.GetName().Name == "NavalDLC") { naval = assembly; break; }
                if (naval == null) { System.Diagnostics.Trace.WriteLine("AOC naval duplicate-reward guard: NavalDLC absent."); return; }
                var behavior = naval.GetType("NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior", false);
                var target = behavior == null ? null : AccessTools.Method(behavior, "DistributePartyShipsAndRecoverGold", new[] { typeof(MobileParty) });
                if (target == null) { System.Diagnostics.Trace.WriteLine("AOC naval duplicate-reward guard: v1.4.8 target unavailable; no patch applied."); return; }
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(NavalDuplicateRewardGuard), "Prefix"),
                    finalizer: new HarmonyMethod(typeof(NavalDuplicateRewardGuard), "Finalizer"));
                _target = target;
                System.Diagnostics.Trace.WriteLine("AOC naval duplicate-reward guard installed: " + target.DeclaringType.FullName + "." + target.Name);
            }
            catch (Exception ex)
            {
                // Optional, version-sensitive native integration: preserve native
                // behavior rather than applying a partial or guessed patch.
                System.Diagnostics.Trace.WriteLine("AOC naval duplicate-reward guard unavailable: " + ex);
            }
        }

        internal static void Uninstall(Harmony harmony)
        {
            if (_target != null) harmony.Unpatch(_target, HarmonyPatchType.All, HarmonyId);
            _target = null;
            Reset();
        }

        private static bool Prefix(MobileParty __0, out bool __state)
        {
            __state = TryEnter(__0);
            return __state;
        }

        // Harmony 2.4.2 may run finalizers even when a later prefix skipped the
        // original. Release only this call's new gate, never an earlier completed
        // distribution. Verify-NavalPatchCoexistence.ps1 exercises both patch orders.
        private static Exception Finalizer(MobileParty __0, bool __state, Exception __exception, bool __runOriginal)
        {
            if (__state && !__runOriginal && __0 != null) _gates.Remove(__0);
            else if (__state) Finish(__0, __exception);
            return __exception;
        }
    }
}
