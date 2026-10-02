using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8: MobileParty.RemoveParty() encloses DestroyShipAction.Apply.
    // Ship.set_Owner(PartyBase) observes transfers; wider removal catches inlined
    // setters. Prefix/finalizer only, no mutations/model calls/exception suppression.
    // Missing target fails capture setup. Cleared ownership is NOT destruction.
    // Verify-ShipLifecycle.ps1 checks binding, skipped calls and exception safety.
    internal static class SupplyShipLifecycleObserver
    {
        internal sealed class Removal
        {
            internal string Id;
            internal MobileParty Party;
            internal Ship[] Ships;
        }
        internal sealed class Ownership { internal string Before; }
        internal static MethodInfo[] Targets()
        {
            return new[] {
                SupplyChainObserver.Require(typeof(MobileParty), "RemoveParty"),
                SupplyChainObserver.Require(typeof(Ship), "set_Owner", typeof(PartyBase))
            };
        }
        internal static void Install(Harmony harmony)
        {
            var targets = Targets();
            harmony.Patch(targets[0], new HarmonyMethod(typeof(SupplyShipLifecycleObserver), "BeforeRemoval"),
                null, null, new HarmonyMethod(typeof(SupplyShipLifecycleObserver), "AfterRemoval"));
            harmony.Patch(targets[1], new HarmonyMethod(typeof(SupplyShipLifecycleObserver), "BeforeOwner"),
                null, null, new HarmonyMethod(typeof(SupplyShipLifecycleObserver), "AfterOwner"));
        }
        internal static string Owner(Ship ship)
        {
            var owner = ship.Owner;
            return owner == null ? "none" : owner.MobileParty != null ? "party:" + owner.MobileParty.StringId
                : owner.Settlement != null ? "settlement:" + owner.Settlement.StringId : "other_owner";
        }
        private static void BeforeOwner(Ship __instance, out Ownership __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try { __state = new Ownership { Before = Owner(__instance) }; }
            catch (Exception ex) { SupplyCapture.Fail("ship owner before", ex); }
        }
        private static void AfterOwner(Ship __instance, Ownership __state, bool __runOriginal, Exception __exception)
        {
            if (__state == null || !SupplyCapture.Active) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native ship owner exception", __exception); return; }
                string after = Owner(__instance);
                SupplyCapture.Write("SHIP_OWNER_CHANGE", 0, 0, "ship:" + SupplyRewardObserver.ShipId(__instance),
                    __runOriginal ? "executed" : "skipped", 0, 0,
                    "ship=" + SupplyRewardObserver.ShipId(__instance) + "; from=" + __state.Before + "; to=" + after
                    + "; originalRan=" + __runOriginal + SupplyRewardObserver.Context + NavalSaleObserver.Context + "; ownership_not_destruction");
            }
            catch (Exception ex) { SupplyCapture.Fail("ship owner after", ex); }
        }
        private static void BeforeRemoval(MobileParty __instance, out Removal __state)
        {
            __state = null;
            if (!SupplyCapture.Active) return;
            try
            {
                __state = new Removal { Id = Guid.NewGuid().ToString("N"), Party = __instance, Ships = __instance.Ships.ToArray() };
                SupplyCapture.Write("SHIP_LIFECYCLE_BEGIN", 0, 0, __instance.StringId, "RemoveParty", 0, __state.Ships.Length,
                    "removal=" + __state.Id + SupplyRewardObserver.Context);
                foreach (var ship in __state.Ships) Membership(__state, ship, "before", true);
            }
            catch (Exception ex) { SupplyCapture.Fail("ship removal before", ex); }
        }
        private static void AfterRemoval(Removal __state, bool __runOriginal, Exception __exception)
        {
            if (__state == null || !SupplyCapture.Active) return;
            try
            {
                if (__exception != null) { SupplyCapture.Fail("native party removal exception", __exception); return; }
                var remaining = __state.Party.Ships.ToArray();
                foreach (var ship in __state.Ships.Union(remaining))
                    Membership(__state, ship, "after", remaining.Contains(ship));
                SupplyCapture.Write("SHIP_LIFECYCLE_END", 0, 0, __state.Party.StringId, "RemoveParty", __state.Ships.Length,
                    remaining.Length, "removal=" + __state.Id + "; originalRan=" + __runOriginal
                    + "; partyActive=" + __state.Party.IsActive + "; membership_and_owner_not_destruction_proof");
            }
            catch (Exception ex) { SupplyCapture.Fail("ship removal after", ex); }
        }
        private static void Membership(Removal removal, Ship ship, string stage, bool member)
        {
            SupplyCapture.Write("SHIP_MEMBERSHIP", 0, 0, removal.Party.StringId, stage, 0, member ? 1 : 0,
                "removal=" + removal.Id + "; ship=" + SupplyRewardObserver.ShipId(ship) + "; owner=" + Owner(ship));
        }
    }
}
