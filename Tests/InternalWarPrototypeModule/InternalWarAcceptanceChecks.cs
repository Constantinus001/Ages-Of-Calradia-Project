using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradiaInternalWarsTest
{
    // Read-only campaign-thread observations. REVIEW is not a definitive failure: a native
    // callback may be mid-transition. Unexercised cases never become passes. No repairs or saves.
    internal static partial class InternalWarDiagnosticsReport
    {
        private static void ReadAcceptanceChecks(DiagnosticSnapshot snapshot, InternalWarTestBehavior behavior)
        {
            if (behavior == null || Campaign.Current == null) return;
            snapshot.Check("session.recovery", string.IsNullOrEmpty(behavior.RecoveryBlocker), behavior.RecoveryBlocker);
            var parties = new HashSet<string>();
            var settlements = new HashSet<string>();
            int reservations = 0;
            bool unique = true;
            foreach (InternalWarTestBehavior controller in behavior.Controllers)
            {
                InternalWarTestRecord operation = controller.CurrentRecord;
                if (operation != null && operation.IsOperational)
                { reservations++; unique &= parties.Add(operation.LeaderPartyId) & settlements.Add(operation.SettlementId); }
                if (controller.HasActiveRaid)
                { reservations++; unique &= parties.Add(controller.Raid.LeaderPartyId) & settlements.Add(controller.Raid.SettlementId); }
                string prefix = "war." + (controller.Conflict == null ? "legacy" : controller.Conflict.Id);
                snapshot.Read(prefix + " acceptance", () => ReadControllerChecks(snapshot, prefix, controller));
            }
            snapshot.Check("session.concurrent_reservations", reservations > 1 ? (bool?)unique : null,
                "active operations=" + reservations + "; checks party/settlement exclusivity, not native battle joining");
            snapshot.Check("manual.ui_clicks", null, "Confirm visible controls, settlement clicking and movement yourself.");
            snapshot.Check("manual.mission_play", null, "Battle visuals, controls, quest outcomes and reinforcements require gameplay observation.");
            snapshot.Check("manual.save_roundtrip", null, "Save/load markers show payload callbacks, not proof that native state survived correctly.");
        }

        private static void ReadControllerChecks(DiagnosticSnapshot snapshot, string prefix, InternalWarTestBehavior controller)
        {
            InternalConflictRecord war = controller.Conflict;
            InternalWarTestRecord operation = controller.CurrentRecord;
            Clan attacker = war == null ? null : InternalWarTestBehavior.FindClan(war.AttackerClanId);
            Clan defender = war == null ? null : InternalWarTestBehavior.FindClan(war.DefenderClanId);
            bool active = war != null && war.Phase == InternalConflictPhase.Active;
            bool nativeHostility = attacker != null && defender != null && FactionManager.IsAtWarAgainstFaction(attacker, defender);
            snapshot.Check(prefix + ".clan_contract", !active ? null : (bool?)(attacker != null && defender != null
                && attacker.Kingdom != null && attacker.Kingdom == defender.Kingdom && attacker.Kingdom.StringId == war.KingdomId
                && nativeHostility), "active war: both original kingdom identities and native clan hostility");
            InternalConflictRecord currentPair = attacker == null || defender == null ? null : controller.FindConflict(attacker.StringId, defender.StringId);
            bool closed = war != null && war.Phase == InternalConflictPhase.Closed && (currentPair == null || currentPair == war);
            snapshot.Check(prefix + ".closed_peace", closed && attacker != null && defender != null ? (bool?)!nativeHostility : null,
                "closed war: native clan hostility ended; excludes a later war for the same pair");
            snapshot.Check(prefix + ".payment_receipt", war != null && war.CompensationGold > 0
                ? (bool?)(war.CompensationGold == InternalWarTestBehavior.PeaceCompensationGold
                    && (war.CompensationPayerId == war.AttackerClanId || war.CompensationPayerId == war.DefenderClanId)) : null,
                "receipt only; COMPENSATION event records the payment path's immediate debit/credit verification");
            snapshot.Check(prefix + ".operation_identity", operation != null && war != null
                ? (bool?)(war.Matches(operation) && war.ActiveOperationId == operation.ConflictId) : null,
                "operation=" + (operation == null ? "none" : operation.ConflictId));
            Settlement target = operation == null ? null : InternalWarTestBehavior.FindSettlement(operation.SettlementId);
            MobileParty leader = operation == null ? null : InternalWarTestBehavior.FindParty(operation.LeaderPartyId);
            bool ownsCamp = target != null && target.SiegeEvent != null && target.SiegeEvent.BesiegerCamp != null
                && leader != null && target.SiegeEvent.BesiegerCamp.LeaderParty == leader;
            bool preparing = operation != null && (operation.State == InternalWarTestState.Preparing || operation.State == InternalWarTestState.Assaulting);
            snapshot.Check(prefix + ".siege_camp", preparing ? (bool?)(ownsCamp && leader.BesiegerCamp == target.SiegeEvent.BesiegerCamp) : null,
                "preparing/assaulting owned camp; transient native teardown may need review");
            snapshot.Check(prefix + ".capture_owner", operation != null && operation.CaptureApplied
                ? (bool?)(target != null && target.OwnerClan != null && target.OwnerClan.StringId == operation.AttackerClanId) : null,
                "current owner vs capturer; later legitimate transfers can explain REVIEW");
            snapshot.Check(prefix + ".cleanup_residue", operation != null && operation.CleanupComplete
                ? (bool?)(!operation.IsOperational && !ownsCamp) : null,
                "terminal record and no matching target camp; later native reuse can explain REVIEW");
            var battle = leader == null ? null : leader.Party.MapEvent;
            if (controller.HasActiveRaid)
            {
                MobileParty raider = InternalWarTestBehavior.FindParty(controller.Raid.LeaderPartyId);
                if (raider != null) battle = raider.Party.MapEvent;
            }
            snapshot.Check(prefix + ".battle_leaders", battle != null && war != null
                ? (bool?)InternalWarCombatService.IsBattleOfConflict(battle, war) : null,
                "battle=" + (battle == null ? "none" : battle.EventType.ToString()) + "; checks leaders only, not every reinforcement");
            snapshot.Check(prefix + ".raid_record", controller.Raid != null && war != null
                ? (bool?)(controller.Raid.ConflictId == war.Id) : null,
                "raid=" + (controller.Raid == null ? "none" : controller.Raid.Id + "; event_observed="
                    + controller.Raid.EventObserved + "; closed=" + controller.Raid.Closed));
        }
    }
}
