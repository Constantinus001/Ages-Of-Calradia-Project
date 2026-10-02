using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// Diagnostic boundary for the inspected v1.4.8 managed campaign API. Call only on the game main thread.
    /// Reads existing references and properties; never advances time, repairs state, invokes missions, or changes menus.
    /// Native-read failures remain visible in a partial snapshot. File failures return an explanation and Trace evidence.
    /// Verification: Release compilation against installed APIs, pure formatting checks, then manual runtime snapshot.
    /// </summary>
    internal static partial class InternalWarDiagnosticsReport
    {
        private const int MinimumWriteIntervalSeconds = 10;
        // Deliberate process-wide diagnostic throttle, independent of campaign time and retained across reloads.
        private static readonly object WriteGate = new object();
        private static long _nextWriteTimestamp;

        internal static bool TryWrite(InternalWarTestBehavior behavior, string reason, out string reportPath, out string result)
        {
            reportPath = string.Empty;
            lock (WriteGate)
            {
                long now = Stopwatch.GetTimestamp();
                if (now < _nextWriteTimestamp)
                {
                    result = "A diagnostic report was requested recently. Wait ten seconds before requesting another.";
                    return false;
                }
                _nextWriteTimestamp = now + Stopwatch.Frequency * MinimumWriteIntervalSeconds;
            }

            string attemptedPath = string.Empty;
            try
            {
                string report = BuildText(behavior, reason);
                string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                DirectoryInfo binaryDirectory = string.IsNullOrEmpty(assemblyDirectory) ? null : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = binaryDirectory == null ? null : binaryDirectory.Parent;
                if (moduleDirectory == null) throw new IOException("The test module's diagnostics directory could not be resolved.");
                string directory = Path.Combine(moduleDirectory.FullName, "Logs", "Diagnostics");
                Directory.CreateDirectory(directory);
                attemptedPath = Path.Combine(directory, "InternalWar_" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
                    + "_" + Guid.NewGuid().ToString("N") + ".txt");
                using (FileStream stream = new FileStream(attemptedPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(report);
                reportPath = attemptedPath;
                result = "Diagnostic report written: " + reportPath;
                Trace.WriteLine(result);
                return true;
            }
            catch (Exception exception)
            {
                // Entire operation is a diagnostic/native-read/file boundary; a failed report must not interrupt gameplay.
                result = "Diagnostic report failed: " + exception.GetType().Name + ": " + DiagnosticSnapshot.OneLine(exception.Message)
                    + (string.IsNullOrEmpty(attemptedPath) ? string.Empty : ". A partial file may remain at " + attemptedPath);
                Trace.WriteLine("Internal-war diagnostic report failed: " + exception);
                return false;
            }
        }

        internal static string BuildText(InternalWarTestBehavior behavior, string reason)
        {
            DiagnosticSnapshot snapshot = new DiagnosticSnapshot();
            snapshot.Add("utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            snapshot.Add("reason", reason);
            snapshot.Add("module.build_id", typeof(InternalWarDiagnosticsReport).Assembly.ManifestModule.ModuleVersionId);
            snapshot.Read("records", delegate { ReadRecords(snapshot, behavior); });
            snapshot.Read("kingdom governance", delegate { if (behavior != null) snapshot.Add("realm.overview", behavior.GetRealmOverview()); });
            snapshot.Read("campaign/ui", delegate { ReadUi(snapshot); });
            snapshot.Read("parties/siege", delegate { ReadCampaign(snapshot, behavior); });
            snapshot.Read("all controllers", delegate { ReadControllerNative(snapshot, behavior); });
            snapshot.Read("automatic acceptance checks", delegate { ReadAcceptanceChecks(snapshot, behavior); });
            return snapshot.Render();
        }

        private static void ReadControllerNative(DiagnosticSnapshot snapshot, InternalWarTestBehavior behavior)
        {
            if (behavior == null || Campaign.Current == null) return;
            snapshot.Add("selected.controller", behavior.SelectedController.Conflict == null ? "legacy" : behavior.SelectedController.Conflict.Id);
            foreach (InternalWarTestBehavior controller in behavior.Controllers)
            {
                string prefix = "controller." + (controller.Conflict == null ? "legacy" : controller.Conflict.Id);
                snapshot.Read(prefix, delegate
                {
                    snapshot.Add(prefix + ".ai", controller.AutonomousAi);
                    InternalWarTestRecord record = controller.CurrentRecord;
                    if (record != null && record.IsOperational)
                    {
                        MobileParty party = InternalWarTestBehavior.FindParty(record.LeaderPartyId);
                        Settlement target = InternalWarTestBehavior.FindSettlement(record.SettlementId);
                        snapshot.Add(prefix + ".operation", record.ConflictId);
                        snapshot.Add(prefix + ".party", record.LeaderPartyId);
                        snapshot.Add(prefix + ".target", record.SettlementId);
                        snapshot.Add(prefix + ".capture_committed", record.CaptureApplied);
                        snapshot.Add(prefix + ".native_order", party == null ? "missing" : party.DefaultBehavior.ToString());
                        snapshot.Add(prefix + ".native_target", party == null ? "missing" : Id(party.TargetSettlement));
                        snapshot.Add(prefix + ".native_owner", target == null || target.OwnerClan == null ? "missing" : target.OwnerClan.StringId);
                        snapshot.Add(prefix + ".camp_leader", target == null || target.SiegeEvent == null
                            || target.SiegeEvent.BesiegerCamp == null || target.SiegeEvent.BesiegerCamp.LeaderParty == null
                            ? "none" : target.SiegeEvent.BesiegerCamp.LeaderParty.StringId);
                        ReadMapEvent(snapshot, prefix + ".battle", party == null ? null : party.Party.MapEvent);
                    }
                    if (controller.HasActiveRaid)
                    {
                        MobileParty raider = InternalWarTestBehavior.FindParty(controller.Raid.LeaderPartyId);
                        snapshot.Add(prefix + ".raid", controller.Raid.Id);
                        snapshot.Add(prefix + ".raid_target", controller.Raid.SettlementId);
                        snapshot.Add(prefix + ".raid_party", controller.Raid.LeaderPartyId);
                        snapshot.Add(prefix + ".raid_stop", controller.Raid.StopRequested);
                        ReadMapEvent(snapshot, prefix + ".raid_battle", raider == null ? null : raider.Party.MapEvent);
                    }
                });
            }
        }

        private static void ReadRecords(DiagnosticSnapshot snapshot, InternalWarTestBehavior behavior)
        {
            snapshot.Add("module.version", Assembly.GetExecutingAssembly().GetName().Version);
            snapshot.Add("behavior.present", behavior != null);
            if (behavior == null) return;
            snapshot.Add("recovery.blocker", behavior.RecoveryBlocker);
            snapshot.Add("ai.autonomous_sieges", behavior.AutonomousAi);
            snapshot.Add("ai.political_declarations", behavior.PoliticalAiEnabled);
            foreach (InternalConflictRecord war in behavior.Conflicts)
                snapshot.Add("registry." + war.Id, war.AttackerClanId + " / " + war.DefenderClanId + " / " + war.Phase);
            foreach (InternalWarTestBehavior controller in behavior.Controllers)
                if (controller.CurrentRecord != null)
                    snapshot.Add("controller." + controller.CurrentRecord.ConflictId, controller.CurrentRecord.SettlementId
                        + " / " + controller.CurrentRecord.State + " / clean=" + controller.CurrentRecord.CleanupComplete);
            snapshot.Add("succession", InternalWarSuccessionBridge.Describe(Clan.PlayerClan == null ? null : Clan.PlayerClan.Kingdom));
            if (behavior.Raid != null)
            {
                snapshot.Add("raid.id", behavior.Raid.Id);
                snapshot.Add("raid.conflict", behavior.Raid.ConflictId);
                snapshot.Add("raid.target", behavior.Raid.SettlementId);
                snapshot.Add("raid.event_observed", behavior.Raid.EventObserved);
                snapshot.Add("raid.stop_requested", behavior.Raid.StopRequested);
                snapshot.Add("raid.closed", behavior.Raid.Closed);
            }
            InternalConflictRecord conflict = behavior.Conflict;
            InternalWarTestRecord operation = behavior.CurrentRecord;
            snapshot.Add("conflict.present", conflict != null);
            if (conflict != null)
            {
                snapshot.Add("conflict.id", conflict.Id);
                snapshot.Add("conflict.phase", conflict.Phase);
                snapshot.Add("conflict.kingdom", conflict.KingdomId);
                snapshot.Add("conflict.attacker", conflict.AttackerClanId);
                snapshot.Add("conflict.defender", conflict.DefenderClanId);
                snapshot.Add("conflict.completed_operations", conflict.CompletedOperations);
                snapshot.Add("conflict.active_operation", conflict.ActiveOperationId);
                snapshot.Add("conflict.last_completed_operation", conflict.LastCompletedOperationId);
                snapshot.Add("conflict.goal", conflict.Goal);
                snapshot.Add("conflict.goal_settlement", conflict.GoalSettlementId);
                snapshot.Add("conflict.goal_satisfied", conflict.GoalSatisfied);
                snapshot.Add("conflict.compensation_gold", conflict.CompensationGold);
                snapshot.Add("conflict.compensation_payer", conflict.CompensationPayerId);
                if (operation != null && !conflict.Matches(operation)) snapshot.Violation("Conflict and operation participant identities disagree.");
                if (operation != null && conflict.ActiveOperationId != operation.ConflictId)
                    snapshot.Violation("Conflict active-operation ID differs from the current operation record.");
                if (operation == null && !string.IsNullOrEmpty(conflict.ActiveOperationId)
                    && (conflict.CompletedOperations == 0 || conflict.ActiveOperationId != conflict.LastCompletedOperationId))
                    snapshot.Violation("Conflict references an operation that is missing.");
                if (operation != null && operation.AttackerClanId == conflict.AttackerClanId && conflict.HasUnresolvedSettlementClaim
                    && operation.SettlementId != conflict.GoalSettlementId)
                    snapshot.Violation("Active operation does not target the unresolved settlement claim.");
            }
            snapshot.Add("operation.present", operation != null);
            if (operation == null) return;
            snapshot.Add("operation.id", operation.ConflictId);
            snapshot.Add("operation.state", operation.State);
            snapshot.Add("operation.pending_outcome", operation.PendingFinalState);
            snapshot.Add("operation.capture_committed", operation.CaptureApplied);
            snapshot.Add("operation.cleanup_complete", operation.CleanupComplete);
            snapshot.Add("operation.target", operation.SettlementId);
            snapshot.Add("operation.party", operation.LeaderPartyId);
            snapshot.Add("operation.kingdom", operation.KingdomId);
            snapshot.Add("operation.attacker", operation.AttackerClanId);
            snapshot.Add("operation.defender", operation.DefenderClanId);
            snapshot.Add("operation.start_campaign_day", operation.StartDay);
            if (operation.CaptureApplied && operation.PendingFinalState != InternalWarTestState.Captured)
                snapshot.Violation("Committed capture has a different pending outcome.");
            if (operation.CleanupComplete && operation.IsOperational)
                snapshot.Violation("An operational record claims cleanup is complete.");
            if (operation.State == InternalWarTestState.Captured && !operation.CaptureApplied)
                snapshot.Violation("Captured terminal state lacks a committed capture.");
        }

        private static void ReadUi(DiagnosticSnapshot snapshot)
        {
            Campaign campaign = Campaign.Current;
            snapshot.Add("campaign.present", campaign != null);
            Game game = Game.Current;
            TaleWorlds.Core.GameState state = game == null ? null : game.GameStateManager.ActiveState;
            snapshot.Add("game.active_state", state == null ? "none" : state.GetType().FullName);
            MapState map = state as MapState;
            snapshot.Add("map.active", map != null);
            if (map != null) snapshot.Add("map.at_menu", map.AtMenu);
            Mission mission = Mission.Current;
            snapshot.Add("mission.present", mission != null);
            if (mission != null)
            {
                snapshot.Add("mission.mode", mission.Mode);
                snapshot.Add("mission.state", mission.CurrentState);
                snapshot.Add("mission.scene", mission.SceneName);
            }
            if (campaign == null) return;
            snapshot.Add("campaign.time_control", campaign.TimeControlMode);
            snapshot.Add("campaign.day", CampaignTime.Now.ToDays);
            MenuContext menu = campaign.CurrentMenuContext;
            snapshot.Add("menu.present", menu != null);
            snapshot.Add("menu.id", menu == null || menu.GameMenu == null ? "none" : menu.GameMenu.StringId);
            PlayerEncounter encounter = PlayerEncounter.Current;
            snapshot.Add("encounter.present", encounter != null);
            snapshot.Add("encounter.target", Id(PlayerEncounter.EncounterSettlement));
            snapshot.Add("encounter.party", PartyId(PlayerEncounter.EncounteredParty));
            snapshot.Add("encounter.party_clan", PartyClanId(PlayerEncounter.EncounteredParty));
            if (encounter != null)
            {
                snapshot.Add("encounter.state", encounter.EncounterState);
                snapshot.Add("encounter.player_side", encounter.PlayerSide);
                snapshot.Add("encounter.opponent_side", encounter.OpponentSide);
            }
            ReadMapEvent(snapshot, "encounter.battle", PlayerEncounter.Battle);
            ReadMapEvent(snapshot, "player.map_event", MapEvent.PlayerMapEvent);
        }

        private static void ReadCampaign(DiagnosticSnapshot snapshot, InternalWarTestBehavior behavior)
        {
            if (Campaign.Current == null) return;
            InternalWarTestRecord operation = behavior == null ? null : behavior.CurrentRecord;
            InternalConflictRecord conflict = behavior == null ? null : behavior.Conflict;
            Clan playerClan = Clan.PlayerClan;
            MobileParty mainParty = MobileParty.MainParty;
            snapshot.Add("player.clan", playerClan == null ? "none" : playerClan.StringId);
            snapshot.Add("player.kingdom", playerClan == null || playerClan.Kingdom == null ? "none" : playerClan.Kingdom.StringId);
            if (playerClan != null)
            {
                snapshot.Add("player.clan_is_minor", playerClan.IsMinorFaction);
                snapshot.Add("player.clan_is_mercenary_type", playerClan.IsClanTypeMercenary);
                snapshot.Add("player.clan_in_mercenary_service", playerClan.IsUnderMercenaryService);
            }
            snapshot.Add("player.party", mainParty == null ? "none" : mainParty.StringId);
            if (mainParty != null)
            {
                snapshot.Add("player.party_active", mainParty.IsActive);
                snapshot.Add("player.healthy_members", mainParty.Party.NumberOfHealthyMembers);
                snapshot.Add("player.army_present", mainParty.Army != null);
                snapshot.Add("player.current_settlement", Id(mainParty.CurrentSettlement));
                snapshot.Add("player.target_settlement", Id(mainParty.TargetSettlement));
                snapshot.Add("player.behavior", mainParty.DefaultBehavior);
                snapshot.Add("player.camp_present", mainParty.BesiegerCamp != null);
                snapshot.Add("player.camp_leader", mainParty.BesiegerCamp == null || mainParty.BesiegerCamp.LeaderParty == null
                    ? "none" : mainParty.BesiegerCamp.LeaderParty.StringId);
                ReadMapEvent(snapshot, "main_party.map_event", mainParty.Party.MapEvent);
            }
            if (operation == null) return;
            Clan attacker = InternalWarTestBehavior.FindClan(operation.AttackerClanId);
            Clan defender = InternalWarTestBehavior.FindClan(operation.DefenderClanId);
            MobileParty leader = InternalWarTestBehavior.FindParty(operation.LeaderPartyId);
            Settlement target = InternalWarTestBehavior.FindSettlement(operation.SettlementId);
            snapshot.Add("attacker.exists", attacker != null);
            snapshot.Add("defender.exists", defender != null);
            snapshot.Add("attacker.leader", attacker == null || attacker.Leader == null ? "none" : attacker.Leader.StringId);
            snapshot.Add("defender.leader", defender == null || defender.Leader == null ? "none" : defender.Leader.StringId);
            snapshot.Add("attacker.kingdom", attacker == null || attacker.Kingdom == null ? "none" : attacker.Kingdom.StringId);
            snapshot.Add("defender.kingdom", defender == null || defender.Kingdom == null ? "none" : defender.Kingdom.StringId);
            bool originalKingdom = attacker != null && defender != null && attacker.Kingdom != null
                && attacker.Kingdom == defender.Kingdom && attacker.Kingdom.StringId == operation.KingdomId;
            snapshot.Add("clans.same_original_kingdom", originalKingdom);
            snapshot.Add("operation.party_is_main", leader != null && leader == mainParty);
            snapshot.Add("operation.attacker_is_player", attacker != null && attacker == playerClan);
            bool hostility = attacker != null && defender != null && FactionManager.IsAtWarAgainstFaction(attacker, defender);
            snapshot.Add("clans.native_stance_at_war", hostility);
            if (conflict != null && conflict.Phase == InternalConflictPhase.Active && (!originalKingdom || !hostility))
                snapshot.Violation("Active political conflict has lost its original kingdom or clan-hostility contract.");
            if (operation.IsOperational && (target == null || leader == null || attacker == null || defender == null))
                snapshot.Violation("Operational record has a missing native identity.");
            if (target == null) return;
            SiegeEvent siege = target.SiegeEvent;
            BesiegerCamp camp = siege == null ? null : siege.BesiegerCamp;
            bool ownsCamp = camp != null && leader != null && camp.LeaderParty == leader && leader.BesiegerCamp == camp;
            snapshot.Add("target.id", target.StringId);
            snapshot.Add("target.owner", target.OwnerClan == null ? "none" : target.OwnerClan.StringId);
            snapshot.Add("target.siege_present", siege != null);
            snapshot.Add("target.siege_camp_present", camp != null);
            snapshot.Add("target.camp_leader", camp == null || camp.LeaderParty == null ? "none" : camp.LeaderParty.StringId);
            snapshot.Add("target.camp_matches_operation_party", ownsCamp);
            snapshot.Add("target.siege_is_player_siege", siege != null && PlayerSiege.PlayerSiegeEvent == siege);
            snapshot.Add("target.event_matches_main_party", target.Party.MapEvent != null && mainParty != null
                && mainParty.Party.MapEvent == target.Party.MapEvent);
            ReadMapEvent(snapshot, "target.map_event", target.Party.MapEvent);
            if ((operation.State == InternalWarTestState.Preparing || operation.State == InternalWarTestState.Assaulting) && !ownsCamp)
                snapshot.Violation("Preparing/assaulting operation has no matching operation-led camp; native transition timing must be checked.");
            if (operation.CleanupComplete && ownsCamp)
                snapshot.Violation("Cleanup-complete operation still owns a live camp.");
            if (operation.CaptureApplied && target.OwnerClan != attacker)
                snapshot.Add("observation.capture_owner_changed", "Current owner differs from recorded capturer; later legitimate transfers are possible.");
        }

        private static void ReadMapEvent(DiagnosticSnapshot snapshot, string prefix, MapEvent mapEvent)
        {
            snapshot.Add(prefix + ".present", mapEvent != null);
            if (mapEvent == null) return;
            snapshot.Add(prefix + ".type", mapEvent.EventType);
            snapshot.Add(prefix + ".state", mapEvent.State);
            snapshot.Add(prefix + ".finalized", mapEvent.IsFinalized);
            snapshot.Add(prefix + ".is_siege_assault", mapEvent.IsSiegeAssault);
            snapshot.Add(prefix + ".target", Id(mapEvent.MapEventSettlement));
            PartyBase attacker = mapEvent.AttackerSide == null ? null : mapEvent.AttackerSide.LeaderParty;
            PartyBase defender = mapEvent.DefenderSide == null ? null : mapEvent.DefenderSide.LeaderParty;
            snapshot.Add(prefix + ".attacker_party", PartyId(attacker));
            snapshot.Add(prefix + ".attacker_clan", PartyClanId(attacker));
            snapshot.Add(prefix + ".defender_party", PartyId(defender));
            snapshot.Add(prefix + ".defender_clan", PartyClanId(defender));
        }

        private static string Id(Settlement settlement) { return settlement == null ? "none" : settlement.StringId; }
        private static string PartyId(PartyBase party)
        {
            return party == null ? "none" : party.MobileParty != null ? party.MobileParty.StringId
                : party.Settlement != null ? party.Settlement.StringId : "nonmobile/nonsettlement";
        }

        private static string PartyClanId(PartyBase party)
        {
            Clan clan = party == null ? null : InternalWarTestService.ResolveClan(party);
            return clan == null ? "none" : clan.StringId;
        }

        // Plain diagnostic data and formatting: independently testable without accessing campaign/native objects.
        internal sealed class DiagnosticSnapshot
        {
            private const int MaximumValueLength = 512;
            private readonly List<string> _values = new List<string>();
            private readonly List<string> _violations = new List<string>();
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _checks = new List<string>();

            internal void Check(string id, bool? consistent, string detail)
            {
                _checks.Add("CHECK " + OneLine(id).Replace('|', '/') + "|"
                    + (!consistent.HasValue ? "NOT_EXERCISED" : consistent.Value ? "OBSERVED_OK" : "REVIEW")
                    + "|" + OneLine(detail).Replace('|', '/'));
            }

            internal void Add(string key, object value)
            {
                _values.Add(OneLine(key) + "=" + OneLine(Convert.ToString(value, CultureInfo.InvariantCulture)));
            }

            internal void Violation(string message) { _violations.Add(OneLine(message)); }

            internal void Read(string section, Action read)
            {
                try { read(); }
                catch (Exception exception)
                {
                    // Read-only Bannerlord boundary. Preserve successful sections and explicitly mark unavailable evidence.
                    _errors.Add(OneLine(section) + ": " + exception.GetType().FullName + ": " + OneLine(exception.Message));
                    Trace.WriteLine("Internal-war diagnostic snapshot section failed: " + section + ": " + exception);
                }
            }

            internal string Render()
            {
                StringBuilder text = new StringBuilder();
                text.AppendLine("AOC INTERNAL WAR DIAGNOSTIC SNAPSHOT v1");
                text.AppendLine("Read-only observations; no campaign state changed. Not an atomic snapshot or proof of runtime success.");
                text.AppendLine("snapshot.complete=" + (_errors.Count == 0));
                foreach (string value in _values) text.AppendLine(value);
                foreach (string check in _checks) text.AppendLine(check);
                text.AppendLine("invariant_findings.count=" + _violations.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string violation in _violations) text.AppendLine("INVARIANT: " + violation);
                text.AppendLine("read_errors.count=" + _errors.Count.ToString(CultureInfo.InvariantCulture));
                foreach (string error in _errors) text.AppendLine("UNAVAILABLE: " + error);
                text.AppendLine("A zero findings count means only that these checks found none; unobserved invariants may still fail.");
                return text.ToString();
            }

            internal static string OneLine(string value)
            {
                if (string.IsNullOrEmpty(value)) return "(empty)";
                StringBuilder text = new StringBuilder(Math.Min(value.Length, MaximumValueLength));
                for (int index = 0; index < value.Length && index < MaximumValueLength; index++)
                    text.Append(char.IsControl(value[index]) ? ' ' : value[index]);
                if (value.Length > MaximumValueLength) text.Append(" [truncated]");
                return text.ToString();
            }
        }
    }
}
