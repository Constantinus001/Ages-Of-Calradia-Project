using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Bannerlord 1.4.8 read-only boundaries. Battle CommitGoldChanges is exact;
    // optional NavalDLC recovery/distribution and its evaluated-value lambda are
    // validated before patching. No model/RNG reevaluation or native mutation.
    // Finalizers preserve native exceptions. Reflection/observation failure closes
    // capture; absent NavalDLC is explicit missing coverage, not a failure.
    internal static class SupplyRewardObserver
    {
        internal sealed class Call
        {
            internal Call Previous;
            internal string Id, Stage, Party;
            internal Hero Recipient;
            internal int Gold;
            internal MapEventParty Battle;
            internal MobileParty Mobile;
            internal Ship[] Ships;
            internal string RecoveryEligibility;
        }
        [ThreadStatic] private static Call _current;
        private static EconomyObjectIdentity _ships = new EconomyObjectIdentity();
        private static bool _navalAvailable;
        private static MethodInfo _penaltyTarget;
        internal static MethodInfo PenaltyTarget { get { return _penaltyTarget; } }
        internal static long ShipId(Ship ship) { return _ships.Get(ship); }
        internal static void Provenance()
        { SupplyCapture.Write("FEATURE_COVERAGE",0,0,"naval_rewards",_navalAvailable?"present":"absent",0,0,"native_hooks_installed; absence_not_failure"); }
        internal static string Context { get { return "; reward=" + (_current == null ? "none" : _current.Id); } }
        internal static void Reset() { _current=null; _ships=new EconomyObjectIdentity(); _navalAvailable=false; _penaltyTarget=null; }
        internal static MethodInfo[] Targets(Assembly naval)
        {
            var battle=SupplyChainObserver.Require(typeof(MapEventParty),"CommitGoldChanges");
            if(naval==null)return new[]{battle};
            var type=naval.GetType("NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior",true);
            var recovery=SupplyChainObserver.Require(type,"RecoverGoldFromRemainingShipsAfterDistribution",typeof(MobileParty));
            var outer=SupplyChainObserver.Require(type,"DistributePartyShipsAndRecoverGold",typeof(MobileParty));
            var values=type.GetNestedTypes(BindingFlags.NonPublic).SelectMany(t=>t.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public))
                .Where(m=>m.Name.StartsWith("<RecoverGoldFromRemainingShipsAfterDistribution>b__",StringComparison.Ordinal)
                    && m.ReturnType==typeof(float) && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType==typeof(Ship)).ToArray();
            if(values.Length!=1)throw new MissingMethodException("Naval recovery evaluated-value lambda changed");
            return new[]{battle,recovery,outer,values[0],SupplyChainObserver.Require(typeof(DestroyShipAction),"Apply",typeof(Ship))};
        }
        internal static void Install(Harmony harmony)
        {
            Reset();
            var naval=AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a=>a.GetName().Name=="NavalDLC");
            var targets=Targets(naval);
            foreach(var method in targets)
            {
                if(method.DeclaringType==typeof(DestroyShipAction))
                    harmony.Patch(method,finalizer:new HarmonyMethod(typeof(SupplyRewardObserver),"ShipDestroyed"));
                else if(method.ReturnType==typeof(float))
                    harmony.Patch(method,postfix:new HarmonyMethod(typeof(SupplyRewardObserver),"Valuation"));
                else harmony.Patch(method,prefix:new HarmonyMethod(typeof(SupplyRewardObserver),method.DeclaringType==typeof(MapEventParty)?"BattleBefore":"NavalBefore"),
                    finalizer:new HarmonyMethod(typeof(SupplyRewardObserver),"After"));
                SoakLog.Write("SUPPLY_REWARD_HOOK",method.DeclaringType.FullName+"."+method.Name+"; mvid="+method.Module.ModuleVersionId);
            }
            if(naval==null)SoakLog.Write("SUPPLY_REWARD_COVERAGE","NavalDLC absent; naval rewards not exercised");
            _navalAvailable=naval!=null;
            // Actual installed model, not an assumed vanilla class. Read its one
            // evaluated result; never invoke the model a second time. Missing or
            // incompatible concrete float getter fails setup rather than guesses.
            if (naval != null && Campaign.Current != null)
            {
                var model = Campaign.Current.Models.ShipCostModel;
                if (model == null) throw new MissingMemberException("Active ship cost model missing");
                _penaltyTarget = SupplyChainObserver.Require(model.GetType(), "GetShipSellingPenalty");
                if (_penaltyTarget.ReturnType != typeof(float)) throw new InvalidOperationException("Ship selling penalty signature changed");
                harmony.Patch(_penaltyTarget, postfix: new HarmonyMethod(typeof(SupplyRewardObserver), "Penalty"));
            }
        }
        private static void BattleBefore(MapEventParty __instance,out Call __state)
        {
            __state=null;if(!SupplyCapture.Active)return;
            try
            {
                __state=new Call{Battle=__instance,Recipient=__instance.Party.LeaderHero,
                    Mobile=__instance.Party.MobileParty,Stage="battle_gold"};
                Begin(__state);
                Write("REWARD_INPUT",__state,"allocated_gold",__instance.GoldLost,__instance.PlunderedGold,
                    "before_is_allocated_loss; after_is_allocated_gain; allocation_not_world_conservation");
            }catch(Exception ex){SupplyCapture.Fail("battle reward before",ex);}
        }
        private static void NavalBefore(MobileParty __0,MethodBase __originalMethod,out Call __state)
        {
            __state=null;if(!SupplyCapture.Active)return;
            try
            {
                __state=new Call{Mobile=__0,Recipient=__0.ActualClan==null?null:__0.ActualClan.Leader,
                    Stage=__originalMethod.Name,Ships=__0.Ships.ToArray()};
                var clan = __0.ActualClan;
                __state.RecoveryEligibility = clan == null ? "no_clan" : clan.IsBanditFaction ? "bandit_clan"
                    : clan.Leader == null ? "no_leader" : !clan.Leader.IsActive ? "inactive_leader"
                    : __state.Ships.Length == 0 ? "no_ships" : "eligible";
                Begin(__state);
                foreach(var ship in __state.Ships)
                    Write("REWARD_SHIP",__state,"before",ship.HitPoints,ship.SailHitPoints,
                        "ship="+_ships.Get(ship)+"; hull="+ship.ShipHull.StringId
                        +"; ownerIdentity="+SupplyShipLifecycleObserver.Owner(ship)+"; hitpoints_and_sail_not_value");
            }catch(Exception ex){SupplyCapture.Fail("naval reward before",ex);}
        }
        private static void Begin(Call call)
        {
            call.Id=Guid.NewGuid().ToString("N");call.Previous=_current;_current=call;
            call.Party=call.Mobile==null?"nonmobile":call.Mobile.StringId;
            call.Gold=call.Recipient==null?(call.Mobile==null?0:call.Mobile.PartyTradeGold):call.Recipient.Gold;
            Write("REWARD_BEGIN",call,call.Stage,call.Gold,call.Gold,"recipient="+(call.Recipient==null?"none":call.Recipient.StringId)
                +"; clan="+(call.Recipient==null?"none":call.Recipient.Clan?.StringId)
                +"; wallet="+SupplyCashObserver.RewardWalletId(call.Recipient,call.Mobile)
                +"; playerClan="+(call.Mobile!=null&&call.Mobile.ActualClan!=null&&call.Mobile.ActualClan==Clan.PlayerClan)
                +"; battlePaymentEligible="+(call.Recipient!=null||(call.Mobile!=null&&call.Mobile.IsPartyTradeActive))
                +"; recoveryEligibility="+call.RecoveryEligibility + SupplyBattleAllocationObserver.PartyContext(call.Battle));
        }
        private static void Valuation(Ship __0,float __result)
        {
            if(!SupplyCapture.Active||_current==null)return;
            try{Write("REWARD_VALUATION",_current,"native_ship_value",0,__result,"ship="+_ships.Get(__0)+"; evaluated_once; pre_player_penalty");}
            catch(Exception ex){SupplyCapture.Fail("ship valuation observation",ex);}
        }
        private static void Penalty(float __result, bool __runOriginal)
        {
            if (!SupplyCapture.Active || _current == null || _current.Stage != "RecoverGoldFromRemainingShipsAfterDistribution") return;
            try { Write("REWARD_PENALTY", _current, "evaluated_selling_penalty", 0, __result,
                "originalRan=" + __runOriginal + "; actual_model_result_not_recomputed; no_formula_certification"); }
            catch (Exception ex) { SupplyCapture.Fail("ship penalty observation", ex); }
        }
        // Native 1.4.8 DestroyShipAction.Apply(Ship), successful completion only.
        // Cleanup follows the reward scope. Preserve weak object identity for the
        // later join; do not retain ships, invoke cleanup or suppress exceptions.
        private static void ShipDestroyed(Ship __0,Exception __exception, bool __runOriginal)
        {
            if(!SupplyCapture.Active || !__runOriginal)return;
            try
            {
                if(__exception!=null){SupplyCapture.Fail("native ship destruction",__exception);return;}
                SupplyCapture.Write("SHIP_DESTRUCTION",0,0,"ship:"+_ships.Get(__0),"native_action_completed",0,1,
                    "ship="+_ships.Get(__0)+"; ownerParty="+__0.Owner?.MobileParty?.StringId
                    +"; ownerRemaining="+(__0.Owner!=null)+"; native_completion_not_independent_world_inventory"+Context);
            }catch(Exception ex){SupplyCapture.Fail("ship destruction observation",ex);}
        }
        // Original-run evidence is essential: another Harmony prefix can skip the
        // native body after our prefix. Finalizers still run in that case.
        private static void After(Call __state,Exception __exception, bool __runOriginal, MethodBase __originalMethod)
        {
            if(__state==null)
            {
                if (SupplyCapture.Active && !__runOriginal && __exception == null)
                    SupplyCapture.Write("REWARD_SKIPPED",0,0,"unobserved_prefix",__originalMethod.Name,0,0,"originalRan=False; no_reward_scope_opened");
                return;
            }
            try
            {
                if(__exception!=null){SupplyCapture.Fail("native reward exception",__exception);return;}
                if(!SupplyCapture.Active)return;
                if(__state.Ships!=null)
                    foreach(var ship in __state.Ships)
                        Write("REWARD_SHIP",__state,"after_membership",1,__state.Mobile.Ships.Contains(ship)?1:0,
                            "ship="+_ships.Get(ship)+"; ownerParty="+ship.Owner?.MobileParty?.StringId
                            +"; ownerIdentity="+SupplyShipLifecycleObserver.Owner(ship)
                            +"; ownerContainsShip="+(ship.Owner!=null&&ship.Owner.Ships.Contains(ship))+"; membership_not_destruction_proof");
                if(__state.Battle!=null)
                    Write("REWARD_INPUT",__state,"remaining_allocations",__state.Battle.GoldLost,__state.Battle.PlunderedGold,"expected_native_reset; not_cash");
                int gold=__state.Recipient==null?(__state.Mobile==null?0:__state.Mobile.PartyTradeGold):__state.Recipient.Gold;
                Write("REWARD_END",__state,__state.Stage,__state.Gold,gold,"gross_context_not_additive_cash_flow; originalRan="+__runOriginal
                    +"; playerClan="+(__state.Mobile!=null&&__state.Mobile.ActualClan!=null&&__state.Mobile.ActualClan==Clan.PlayerClan));
            }catch(Exception ex){SupplyCapture.Fail("reward after",ex);}
            finally{_current=__state.Previous;}
        }
        private static void Write(string kind,Call call,string metric,double before,double after,string detail)
        {
            SupplyCapture.Write(kind,0,0,call.Party,metric,before,after,"reward="+call.Id+"; parentReward="+(call.Previous==null?"none":call.Previous.Id)+"; "+detail);
        }
    }
}
