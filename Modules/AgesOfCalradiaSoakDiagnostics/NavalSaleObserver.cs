using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Native 1.4.8 ChangeShipOwnerAction.ApplyInternal(PartyBase, Ship,
    // ShipOwnerChangeDetail), trade=0 only. Observe the actual operation, not
    // shopping quotes. No mutation/repricing/retry; finalizer preserves errors.
    internal static class NavalSaleObserver
    {
        internal sealed class Call
        {
            internal Call Previous;
            internal string Id, Wallet;
            internal Ship Ship;
            internal PartyBase Buyer;
            internal Hero Recipient;
            internal int Before;
        }
        [ThreadStatic] private static Call _current;
        internal static string Context { get { return "; navalSale=" + (_current == null ? "none" : _current.Id); } }
        internal static bool HasOpenScope { get { return _current != null; } }
        internal static void Reset() { _current = null; }
        internal static void Install(Harmony harmony)
        {
            Reset();
            var method = SupplyChainObserver.Require(typeof(ChangeShipOwnerAction), "ApplyInternal",
                typeof(PartyBase), typeof(Ship), typeof(ChangeShipOwnerAction.ShipOwnerChangeDetail));
            harmony.Patch(method, new HarmonyMethod(typeof(NavalSaleObserver), "Before") { priority = Priority.First },
                null, null, new HarmonyMethod(typeof(NavalSaleObserver), "After") { priority = Priority.Last });
        }
        private static void Before(PartyBase __0, Ship __1, ChangeShipOwnerAction.ShipOwnerChangeDetail __2, out Call __state)
        {
            __state = null;
            if (!SupplyCapture.Active || (int)__2 != 0) return;
            try
            {
                var seller = __1.Owner;
                var mobile = seller == null ? null : seller.MobileParty;
                if (__0 == null || !__0.IsSettlement || mobile == null || mobile.IsCaravan || mobile.IsVillager) return;
                var clan = mobile.ActualClan;
                var hero = clan != null && clan.Leader != null ? clan.Leader : seller.LeaderHero;
                if (hero == null) return; // Unhandled native party fallback not claimed as hero evidence.
                __state = new Call { Previous = _current, Id = Guid.NewGuid().ToString("N"), Ship = __1,
                    Buyer = __0, Recipient = hero, Before = hero.Gold, Wallet = SupplyCashObserver.RewardWalletId(hero, mobile) };
                _current = __state;
                Write("NAVAL_SALE_BEGIN", __state, __state.Before, "playerClan=" + (clan != null && clan == Clan.PlayerClan));
            }
            catch (Exception ex) { SupplyCapture.Fail("naval sale before", ex); }
        }
        private static void After(Call __state, bool __runOriginal, Exception __exception)
        {
            if (__state == null) return;
            try
            {
                if (SupplyCapture.Active)
                    Write("NAVAL_SALE_END", __state, __state.Recipient.Gold, "originalRan=" + __runOriginal
                        + "; error=" + (__exception == null ? "none" : __exception.GetType().Name)
                        + "; transferred=" + (__state.Ship.Owner == __state.Buyer));
            }
            catch (Exception ex) { SupplyCapture.Fail("naval sale after", ex); }
            finally { _current = __state.Previous; }
        }
        private static void Write(string kind, Call call, int gold, string detail)
        {
            SupplyCapture.Write(kind, 0, 0, call.Wallet, "trade", call.Before, gold,
                "sale=" + call.Id + "; ship=" + SupplyRewardObserver.ShipId(call.Ship) + "; wallet=" + call.Wallet
                + "; buyer=" + call.Buyer.Id + "; owner=" + SupplyShipLifecycleObserver.Owner(call.Ship) + "; " + detail);
        }
    }
}
