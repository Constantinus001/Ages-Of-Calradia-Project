using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.CampaignSystems
{
    // Public observer ABI; diagnostic subscribers must not own policy or payments.
    public static class NavalCashoutObservation
    {
        public static event Action<string, object, string> Observed;
        public static string Status { get { return NavalCashoutPatches.Status; } }
        internal static void Emit(string kind, object subject, string detail)
        {
            var handlers = Observed;
            if (handlers == null) return;
            foreach (Action<string, object, string> handler in handlers.GetInvocationList())
                try { handler(kind, subject, detail); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Naval policy diagnostic subscriber failed: " + ex); }
        }
    }

    internal static class NavalCashoutRuntime
    {
        internal sealed class BattleFrame
        {
            internal BattleFrame Previous;
            internal long Pool;
            internal bool Valid = true;
            internal int Quotes;
        }
        internal sealed class QuoteFrame
        {
            internal QuoteFrame Previous;
            internal string Route;
            internal Ship Ship;
            internal bool Seen, Valid = true;
            internal bool AlreadyDiscounted;
            internal float Hull;
            internal float NativeBase, EffectiveBase;
        }
        [ThreadStatic] private static BattleFrame _battle;
        [ThreadStatic] private static QuoteFrame _quote;
        [ThreadStatic] private static QuoteFrame _battleRequest;
        [ThreadStatic] private static MobileParty _recovery;
        internal static NavalCashoutSettings Settings = NavalCashoutSettings.Disabled;
        internal static Func<Ship, bool, PartyBase, PartyBase, float> NativeBase;
        internal static Type ModelType;
        internal static bool HasOpenScope { get { return _battle != null || _quote != null || _battleRequest != null || _recovery != null; } }
        private sealed class Identity { internal long Value; }
        private static ConditionalWeakTable<Ship, Identity> _ships = new ConditionalWeakTable<Ship, Identity>();
        private static long _nextShip;

        internal static void Reset()
        { _battle = null; _quote = null; _battleRequest = null; _recovery = null; _ships = new ConditionalWeakTable<Ship, Identity>(); _nextShip = 0; Settings = NavalCashoutSettings.Disabled; }
        internal static bool Eligible(PartyBase party)
        {
            var mobile = party == null ? null : party.MobileParty;
            var clan = mobile == null ? null : mobile.ActualClan;
            return clan != null && Clan.PlayerClan != null && clan != Clan.PlayerClan && !clan.IsBanditFaction
                && !mobile.IsCaravan && !mobile.IsVillager;
        }
        internal static void BeginBattle(out BattleFrame __state)
        { __state = new BattleFrame { Previous = _battle }; _battle = __state; }
        internal static Exception EndBattle(BattleFrame __state, Exception __exception)
        { _battle = __state.Previous; return __exception; }
        internal static void BeginRecovery(MobileParty __0, out MobileParty __state)
        { __state = _recovery; _recovery = __0; }
        internal static Exception EndRecovery(MobileParty __state, Exception __exception)
        { _recovery = __state; return __exception; }

        internal static void BeginQuote(object __instance, Ship __0, PartyBase __1, PartyBase __2, out QuoteFrame __state)
        {
            __state = _quote;
            _quote = null; // Nested unrelated getters cannot inherit another quote.
            if (!Settings.Enabled || __instance.GetType() != ModelType) return;
            if (_battleRequest != null && _battleRequest.Ship == __0 && !_battleRequest.Seen)
            { _quote = _battleRequest; _quote.Previous = __state; return; }
            if (!Eligible(__1)) return;
            string route = __2 != null && __2.IsSettlement ? "sale"
                : __2 == null && _recovery != null && _recovery.Party == __1 ? "recovery" : null;
            if (route != null) _quote = new QuoteFrame { Previous = __state, Route = route, Ship = __0 };
        }

        // This wrapper replaces only the base-value call inside GetShipTradeValue.
        // It never patches repair/upgrade getters, and evaluates native base once.
        internal static float BaseValue(Ship ship, bool discounted, PartyBase seller, PartyBase buyer)
        {
            float value = NativeBase(ship, discounted, seller, buyer);
            var q = _quote;
            if (q == null || q.Ship != ship) return value;
            if (q.Seen) { q.Valid = false; return value; }
            q.Seen = true; q.NativeBase = value;
            q.AlreadyDiscounted = discounted;
            q.Hull = ship != null && ship.ShipHull != null ? ship.ShipHull.Value : 0;
            q.Valid = ship != null && ship.ShipHull != null
                && NavalCashoutPolicy.TryBase(value, q.Hull, discounted, Settings.HullBasis, out q.EffectiveBase);
            return q.Valid && q.Route != "battle" ? q.EffectiveBase : value;
        }
        internal static void QuoteResult(ref float __result, bool __runOriginal)
        {
            var q = _quote;
            if (q == null) return;
            q.Valid &= __runOriginal && q.Seen && NavalCashoutPolicy.Finite(__result);
            if (!q.Valid) { Emit("rejected", q, __result); return; }
            // Native can produce a negative net resale value. Do not turn selling
            // a damaged hull into a reverse payment. No repair re-evaluation.
            if (q.Route != "battle") __result = Math.Max(0f, __result);
            Emit("quote", q, __result);
        }
        internal static Exception EndQuote(QuoteFrame __state, Exception __exception)
        { _quote = __state; return __exception; }

        internal static float BattleQuote(ShipCostModel model, Ship ship, PartyBase seller, PartyBase buyer)
        {
            if (!Settings.Enabled || _battle == null) return model.GetShipTradeValue(ship, seller, buyer);
            var previous = _battleRequest;
            var request = new QuoteFrame { Route = "battle", Ship = ship };
            _battleRequest = request;
            try
            {
                float native = model.GetShipTradeValue(ship, seller, buyer);
                // Buyer=null native branch is exactly base*1.5; unknown overrides
                // invalidate the entire shadow pool, never player/native payouts.
                float effective = request.EffectiveBase * 1.5f;
                if (!request.Seen || !request.Valid || buyer != null
                    || native != request.NativeBase * 1.5f || !NavalCashoutPolicy.Finite(effective)
                    || effective < 0 || effective >= int.MaxValue || native < 0 || native >= int.MaxValue)
                    _battle.Valid = false;
                else { _battle.Pool += (int)effective; _battle.Quotes++; }
                return native;
            }
            finally { _battleRequest = previous; }
        }

        // Replaces only LootDefeatedPartyShips' single PlunderedGold setter.
        // Native non-ship gold and mixed player-clan shares are preserved.
        internal static void Allocate(MapEventParty party, int nativeTotal, int contributionSum)
        {
            int priorGold = party.PlunderedGold;
            int effective = nativeTotal;
            var frame = _battle;
            bool applied = Settings.Enabled && frame != null && frame.Valid && frame.Quotes > 0 && Eligible(party.Party)
                && NavalCashoutPolicy.TryShare(priorGold, party.ContributionToBattle,
                    contributionSum, frame.Pool, out effective);
            if (!applied) effective = nativeTotal;
            party.PlunderedGold = effective;
            NavalCashoutObservation.Emit("allocation", party, "revision=" + Settings.Revision + "; party=" + party.Party.Id
                + "; playerClan=" + (party.Party.MobileParty != null && Clan.PlayerClan != null && party.Party.MobileParty.ActualClan == Clan.PlayerClan)
                + "; nativeTotal=" + nativeTotal + "; effectiveTotal=" + effective + "; applied=" + applied
                + "; priorGold=" + priorGold + "; contribution=" + party.ContributionToBattle + "; contributionSum=" + contributionSum
                + "; effectivePool=" + (frame == null ? 0 : frame.Pool) + "; poolValid=" + (frame != null && frame.Valid));
        }
        private static void Emit(string kind, QuoteFrame q, float result)
        {
            NavalCashoutObservation.Emit(kind, q.Ship, "revision=" + Settings.Revision + "; route=" + q.Route
                + "; policyShip=" + (q.Ship == null ? "null" : _ships.GetValue(q.Ship, s => new Identity { Value = ++_nextShip }).Value.ToString(CultureInfo.InvariantCulture))
                + "; nativeBase=" + q.NativeBase.ToString("R", CultureInfo.InvariantCulture)
                + "; effectiveBase=" + q.EffectiveBase.ToString("R", CultureInfo.InvariantCulture)
                + "; hull=" + q.Hull.ToString("R", CultureInfo.InvariantCulture) + "; alreadyDiscounted=" + q.AlreadyDiscounted
                + "; hullBasis=" + Settings.HullBasis.ToString("R", CultureInfo.InvariantCulture)
                + "; returnedQuote=" + result.ToString("R", CultureInfo.InvariantCulture)
                + "; basisValid=" + q.Valid + "; nativeUpgradeRulesPreserved=True; paymentNotImplied=True");
        }
    }
}
