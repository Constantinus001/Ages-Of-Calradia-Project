using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.SoakDiagnostics
{
    /// <summary>
    /// Native campaign observer and checkpoint controller. It only changes
    /// TimeControlMode and SpeedUpMultiplier to reduce wall-clock duration;
    /// Bannerlord still performs its normal campaign ticks and model calls.
    /// Version-sensitive reflection is isolated to optional diagnostic fields.
    /// </summary>
    internal sealed class CalendarSoakBehavior : CampaignBehaviorBase
    {
        internal const double DefaultTargetCalendarDays = 731d;
        private const float SoakSpeedMultiplier = 2f;
        private static bool _normalSpeedSelected;
        private const string CheckpointSaveName = "AOC_SOAK_LONGRUN";
        private const string FinalSaveName = "AOC_SOAK_LONGRUN_FINAL";

        private SoakControl _control;
        private static CalendarSoakBehavior _active;
        private bool _saveRequested;
        private bool _saveSucceeded;
        private bool _stopped;
        private bool _captureOnly;
        private bool _supplyOnly;
        private long _captureStarted;
        private double _captureDay;
        private string _requestedSaveName;
        private bool _finalSave;
        private bool _wallClockCheckpointSave;
        private int _lastMonthlyReport;
        private readonly PeaceEventDiagnostics _peaceDiagnostics = new PeaceEventDiagnostics();

        internal static bool IsArmed
        {
            get { return File.Exists(System.IO.Path.Combine(SoakLog.DirectoryPath, "AocSoakRun.enabled")); }
        }

        private static bool IsSafeMap()
        {
            GameStateManager manager = Game.Current == null ? null : Game.Current.GameStateManager;
            MapState map = manager == null ? null : manager.ActiveState as MapState;
            return map != null && !manager.ActiveStateDisabledByUser && !map.AtMenu
                && !map.MapConversationActive && !map.IsSimulationActive && Mission.Current == null
                && !InformationManager.IsAnyInquiryActive() && Campaign.Current.SaveHandler != null
                && !Campaign.Current.SaveHandler.IsSaving;
        }

        public override void RegisterEvents()
        {
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, OnSaveOver);
            CampaignEvents.TournamentStarted.AddNonSerializedListener(this, OnTournamentStarted);
            CampaignEvents.TournamentCancelled.AddNonSerializedListener(this, OnTournamentCancelled);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnPeaceMade);
            CampaignEvents.SiegeCompletedEvent.AddNonSerializedListener(this, OnSiegeCompleted);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Deliberately empty: this diagnostics module adds no save contract.
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            _active = this;
            _normalSpeedSelected = false;
            _peaceDiagnostics.Reset();
            if (File.Exists(SupplyCapture.MarkerPath) || File.Exists(SupplyCapture.RollingMarkerPath))
            {
                _supplyOnly = true;
                SupplyCapture.Start();
                return; // Never resume the completed long-soak controller.
            }
            string captureMarker = System.IO.Path.Combine(SoakLog.DirectoryPath, "AocEconomyShortCapture.enabled");
            if (File.Exists(captureMarker))
            {
                _captureOnly = true;
                try
                {
                File.Move(captureMarker, captureMarker + ".started-" + Guid.NewGuid().ToString("N"));
                EconomyDiagnosticPatches.Install();
                _captureStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                _captureDay = CampaignTime.Now.ToDays;
                if (EconomyDiagnosticPatches.Ready)
                    SoakLog.Write("SHORT_CAPTURE_STARTED", "raw=" + EconomyTransactionDiagnostics.StartShortCapture()
                        + "; startDay=" + _captureDay.ToString("R", CultureInfo.InvariantCulture) + "; no_auto_save_or_quit; no_speed_override");
                else SoakLog.Write("SHORT_CAPTURE_FAILURE", "mandatory hooks unavailable");
                }
                catch (Exception ex)
                {
                    _stopped = true;
                    EconomyTrace.Fail("short capture start: " + ex);
                    EconomyTransactionDiagnostics.Stop();
                    SoakLog.Write("SHORT_CAPTURE_FAILURE", ex.ToString());
                }
                return;
            }
            if (!EnsureControl()) return;
            bool reloaded;
            if (!SoakReloadReceipt.Confirm(CampaignTime.Now.ToDays, Campaign.Current.UniqueGameId,
                Hero.MainHero == null ? 0 : Hero.MainHero.Gold,
                Hero.MainHero == null ? 0d : Hero.MainHero.BirthDay.ToDays, out reloaded))
            {
                _stopped = true;
                ReleaseTimeControl();
                return;
            }
            if (reloaded)
            {
                _control.ReloadsCompleted++;
                _control.Save();
                SoakLog.Write("RELOAD_CONFIRMED", _control.Describe());
            }
            LogMonthlyDetails();
            EconomyDiagnosticPatches.Install();
            if (EconomyDiagnosticPatches.Ready) EconomyTransactionDiagnostics.Start();
            SoakLog.Write("ECONOMY_MODE", "bounded daily flow summaries; raw ledger requires AocEconomyTransactions.enabled; no balance certification");
        }

        internal static void ApplicationTick(float dt)
        {
            if (_active != null && _active._supplyOnly)
            {
                SupplyCapture.Tick();
                return;
            }
            if (_active != null && _active._captureOnly)
            {
                _active.TickShortCapture();
                return;
            }
            if (_active == null || _active._control == null || Campaign.Current == null || PacingCalibration.Enabled) return;
            try
            {
                bool enabled = IsArmed && !_active._stopped && !_active._control.Completed;
                bool safeMap = enabled && IsSafeMap();
                EconomyTrace.Flush();
                if (!enabled) { EconomyTransactionDiagnostics.Stop(); ReleaseTimeControl(); return; }
                if (_active._saveRequested || safeMap) _active.OnFrameTick(dt);
                if (safeMap && !_active._saveRequested) KeepCampaignAdvancing();
                else ReleaseTimeControl();
            }
            catch (Exception exception)
            {
                _active._stopped = true;
                ReleaseTimeControl();
                SoakLog.Write("CONTROLLER_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void OnSaveOver(bool successful, string saveName)
        {
            if (EconomyTransactionDiagnostics.Enabled)
                EconomyTrace.Write("SAVE_RESULT", 0, 0, saveName, successful ? "success" : "failure", 0, successful ? 1 : 0,
                    "native-save-event", "observed only; reload must be verified in another session");
            // Native 1.4.8 dequeues before dispatch: IsSaving=false is not success.
            if (!_saveRequested || !string.Equals(saveName, _requestedSaveName, StringComparison.Ordinal)) return;
            _saveSucceeded = successful;
            SoakLog.Write(successful ? "SAVE_CONFIRMED" : "SAVE_FAILURE", saveName);
            if (!successful) { _stopped = true; ReleaseTimeControl(); }
        }

        private void OnHourlyTick()
        {
            if (_supplyOnly) return;
            if (_captureOnly) { if (EconomyTransactionDiagnostics.Enabled) LogHourlyState(); return; }
            if (_stopped || !EnsureControl()) return;
            KeepCampaignAdvancing();
            LogHourlyState();
        }

        private void OnDailyTick()
        {
            if (_supplyOnly) { SupplyCapture.SampleDay(); SupplyChainObserver.Snapshot(); return; }
            if (_captureOnly) { if (EconomyTransactionDiagnostics.Enabled) EconomyTransactionDiagnostics.Snapshot(); return; }
            if (_stopped || !EnsureControl() || Campaign.Current == null) return;
            LogDailyEconomy();
            EconomyTransactionDiagnostics.Snapshot();
            double day = CampaignTime.Now.ToDays;
            int month = (int)Math.Floor((day - _control.StartDay) / 30d);
            if (month > _lastMonthlyReport)
            {
                _lastMonthlyReport = month;
                LogMonthlyDetails();
            }

            if (_saveRequested || Campaign.Current.SaveHandler == null) return;
            if (_control.DeadlineUtc == DateTime.MinValue && day >= _control.TargetDay)
            {
                RequestSave(true);
            }
        }

        internal static bool CaptureLimitReached(double seconds, double days, long bytes)
        {
            return seconds >= 180d || days >= 3d || bytes >= 268435456L;
        }

        private void TickShortCapture()
        {
            if (_stopped) return;
            try
            {
                EconomyTrace.Flush();
                double seconds = (System.Diagnostics.Stopwatch.GetTimestamp() - _captureStarted)
                    / (double)System.Diagnostics.Stopwatch.Frequency;
                double days = Campaign.Current == null ? 0 : CampaignTime.Now.ToDays - _captureDay;
                if (!EconomyTrace.Healthy || CaptureLimitReached(seconds, days, EconomyTrace.RawBytes))
                {
                    bool healthy = EconomyTrace.Healthy;
                    EconomyTransactionDiagnostics.Stop();
                    _stopped = true;
                    SoakLog.Write(healthy ? "SHORT_CAPTURE_STOPPED" : "SHORT_CAPTURE_FAILURE",
                        "wallSeconds=" + seconds.ToString("R", CultureInfo.InvariantCulture) + "; campaignDays="
                        + days.ToString("R", CultureInfo.InvariantCulture) + "; game_left_running; inspect_session_closure");
                }
            }
            catch (Exception ex)
            {
                _stopped = true;
                EconomyTrace.Fail("short capture: " + ex);
                EconomyTransactionDiagnostics.Stop();
                SoakLog.Write("SHORT_CAPTURE_FAILURE", ex.ToString());
            }
        }

        private void OnFrameTick(float dt)
        {
            // Bannerlord resets the requested mode during regular frame handling.
            // Reapply the diagnostics-only acceleration every frame so the soak
            // advances at the configured rate without changing campaign rules.
            if (!_saveRequested && EnsureControl() && _control.IsDeadlineReached(DateTime.UtcNow))
                RequestSave(true);

            if (!_saveRequested || Campaign.Current == null || Campaign.Current.SaveHandler == null) return;
            if (!_saveSucceeded || Campaign.Current.SaveHandler.IsSaving) return;

            if (_finalSave)
            {
                _control.Completed = true;
                SoakLog.Write("SOAK_COMPLETE", _control.Describe());
            }
            else
            {
                if (_wallClockCheckpointSave)
                {
                    SoakLog.Write("WALL_CLOCK_CHECKPOINT_COMPLETE", _control.Describe());
                }
                else
                {
                    _control.NextCheckpointDay += _control.CheckpointIntervalDays;
                    SoakLog.Write("CHECKPOINT_COMPLETE", _control.Describe());
                }
            }
            if (!_finalSave) SoakReloadReceipt.MarkReady();
            _stopped = true;
            _control.Save();
            // Close evidence before native quit; module unload is not a durable
            // flush guarantee during process termination.
            EconomyTransactionDiagnostics.Stop();
            SoakLog.Write("QUIT_AFTER_SAVE", "save completed; the runner will relaunch the dedicated save");
            Utilities.QuitGame();
        }

        private bool EnsureControl()
        {
            if (_control != null) return true;
            if (Campaign.Current == null) return false;
            _control = SoakControl.LoadOrCreate(CampaignTime.Now.ToDays);
            _lastMonthlyReport = -1;
            SoakLog.Write("SESSION_START", _control.Describe());
            LogProfileFingerprint();
            return true;
        }

        internal static void KeepCampaignAdvancing()
        {
            if (PacingCalibration.Enabled || !IsArmed || _active == null
                || _active._stopped || _active._saveRequested || _active._control.Completed) return;
            if (Campaign.Current == null) return;
            if (Campaign.Current.SaveHandler == null || Campaign.Current.SaveHandler.IsSaving) return;
            try
            {
                if (!_normalSpeedSelected)
                {
                    Type speedOwner = AppDomain.CurrentDomain.GetAssemblies()
                        .Where(assembly => assembly.GetName().Name == "AgesOfCalradia.Approved560CalendarFixes")
                        .Select(assembly => assembly.GetType("AgesOfCalradia.Approved560CalendarFixes.CampaignSimulationTimeFix", false))
                        .FirstOrDefault(type => type != null);
                    MethodInfo select = speedOwner == null ? null : speedOwner.GetMethod(
                        "SelectFastForwardMultiplier", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (select == null) throw new MissingMethodException("AOC normal 2x speed selector unavailable.");
                    select.Invoke(null, new object[] { 2 });
                    _normalSpeedSelected = true;
                    SoakLog.Write("SPEED_SELECTED", "normal AOC 2x; target 40 seconds/day; no 64x override");
                }
                // The stock TimeControlMode setter ignores changes while this
                // lock is held. Apply the requested unattended-test mode first,
                // then prevent map UI and campaign interruptions from resetting
                // it. This lock is released before every save and on unload.
                Campaign.Current.SetTimeControlModeLock(false);
                Campaign.Current.SpeedUpMultiplier = SoakSpeedMultiplier;
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.UnstoppableFastForward;
                Campaign.Current.SetTimeControlModeLock(true);
            }
            catch (Exception exception)
            {
                _active._stopped = true;
                SoakLog.Write("TIME_CONTROL_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private void RequestSave(bool finalSave, bool wallClockCheckpoint = false)
        {
            try
            {
                _saveRequested = true;
                _finalSave = finalSave;
                _wallClockCheckpointSave = wallClockCheckpoint;
                _saveSucceeded = false;
                Campaign.Current.SetTimeControlModeLock(false);
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                string name = finalSave ? FinalSaveName : CheckpointSaveName;
                _requestedSaveName = name;
                if (!finalSave) SoakReloadReceipt.Capture(CampaignTime.Now.ToDays, Campaign.Current.UniqueGameId,
                    Hero.MainHero == null ? 0 : Hero.MainHero.Gold,
                    Hero.MainHero == null ? 0d : Hero.MainHero.BirthDay.ToDays);
                SoakLog.Write(finalSave ? "FINAL_SAVE_REQUEST" : "CHECKPOINT_SAVE_REQUEST", name + "; " + _control.Describe());
                Campaign.Current.SaveHandler.SaveAs(name);
            }
            catch (Exception exception)
            {
                _saveRequested = false;
                _stopped = true;
                SoakLog.Write("SAVE_REQUEST_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        internal static void ReleaseTimeControl()
        {
            if (Campaign.Current == null) return;
            try
            {
                Campaign.Current.SetTimeControlModeLock(false);
            }
            catch (Exception exception)
            {
                SoakLog.Write("TIME_CONTROL_RELEASE_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void LogHourlyState()
        {
            try
            {
                int tournaments = Town.AllTowns.Count(town => town != null && town.HasTournament);
                int sieges = Settlement.All.Count(settlement => settlement != null && settlement.SiegeEvent != null);
                int wars = CountWars();
                MobileParty party = MobileParty.MainParty;
                int wage = party == null ? 0 : party.TotalWage;
                Hero hero = Hero.MainHero;
                string heroAge = hero == null ? "none" : hero.Age.ToString("F3", CultureInfo.InvariantCulture);
                SoakLog.Write("HOURLY", "day=" + CampaignTime.Now.ToDays.ToString("F3", CultureInfo.InvariantCulture)
                    + "; tournaments=" + tournaments
                    + "; sieges=" + sieges
                    + "; wars=" + wars
                    + "; mainWageNative=" + wage
                    + "; heroAge=" + heroAge);
            }
            catch (Exception exception)
            {
                SoakLog.Write("HOURLY_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static int CountWars()
        {
            int count = 0;
            IList<Kingdom> kingdoms = Kingdom.All;
            for (int left = 0; left < kingdoms.Count; left++)
            {
                for (int right = left + 1; right < kingdoms.Count; right++)
                {
                    if (kingdoms[left].IsAtWarWith(kingdoms[right])) count++;
                }
            }
            return count;
        }

        private static void LogMonthlyDetails()
        {
            try
            {
                LogProfileFingerprint();
                LogWorkshopSpeeds();
                LogTownEconomy();
                LogHeroAges();
                LogClanExpenses();
                LogSiegePreparation();
            }
            catch (Exception exception)
            {
                SoakLog.Write("MONTHLY_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void LogDailyEconomy()
        {
            try
            {
                SoakLog.Write("ECONOMY_SNAPSHOT", "day=" + CampaignTime.Now.ToDays.ToString("R", CultureInfo.InvariantCulture));
                LogTownEconomy();
                LogWorkshopSpeeds();
                LogClanExpenses();
                foreach (MobileParty party in MobileParty.All.Where(p => p != null && p.TotalWage > 0))
                    SoakLog.Write("AI_PAYROLL_OBSERVATION", "party=" + party.StringId + "; nativeWage=" + party.TotalWage
                        + "; leaderGold=" + (party.LeaderHero == null ? "unavailable" : party.LeaderHero.Gold.ToString(CultureInfo.InvariantCulture)));
            }
            catch (Exception exception)
            {
                SoakLog.Write("ECONOMY_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void LogWorkshopSpeeds()
        {
            WorkshopModel model = Campaign.Current.Models.WorkshopModel;
            foreach (Workshop workshop in Town.AllTowns
                .Where(town => town != null && town.Workshops != null)
                .SelectMany(town => town.Workshops)
                .Where(workshop => workshop != null))
            {
                ExplainedNumber speed = model.GetEffectiveConversionSpeedOfProduction(workshop, 1f, false);
                string id = workshop.WorkshopType == null ? "unknown" : workshop.WorkshopType.StringId;
                SoakLog.Write("WORKSHOP", "id=" + id + "; effectiveSpeed="
                    + speed.ResultNumber.ToString("F5", CultureInfo.InvariantCulture)
                    + "; town=" + workshop.Settlement.StringId + "; slot=" + workshop.Tag
                    + "; capital=" + workshop.Capital + "; initialCapital=" + workshop.InitialCapital
                    + "; capitalSurplusOverInitial=" + workshop.ProfitMade
                    + "; accumulatedProfitAboveInitial=" + workshop.ProfitMade);
            }
        }

        private static void LogTownEconomy()
        {
            foreach (Town town in Town.AllTowns.Where(town => town != null))
            {
                SoakLog.Write("TOWN_ECONOMY", "id=" + town.StringId
                    + "; prosperity=" + town.Prosperity.ToString("R", CultureInfo.InvariantCulture)
                    + "; food=" + town.FoodStocks.ToString("R", CultureInfo.InvariantCulture)
                    + "; gold=" + town.Gold.ToString(CultureInfo.InvariantCulture)
                    + "; foodChange=" + town.FoodChange.ToString("R", CultureInfo.InvariantCulture)
                    + "; prosperityChange=" + town.ProsperityChange.ToString("R", CultureInfo.InvariantCulture));
                foreach (var item in town.Settlement.ItemRoster.Where(entry => entry.EquipmentElement.Item != null
                    && (entry.EquipmentElement.Item.StringId == "grain" || entry.EquipmentElement.Item.StringId == "fish"
                        || entry.EquipmentElement.Item.StringId == "hardwood" || entry.EquipmentElement.Item.StringId == "iron")))
                {
                    SoakLog.Write("MARKET_SAMPLE", "town=" + town.Settlement.StringId
                        + "; item=" + item.EquipmentElement.Item.StringId + "; stock=" + item.Amount
                        + "; buyQuote=" + town.GetItemPrice(item.EquipmentElement.Item, null, false)
                        + "; sellQuote=" + town.GetItemPrice(item.EquipmentElement.Item, null, true));
                }
            }
        }

        private static void LogHeroAges()
        {
            foreach (Hero hero in Hero.AllAliveHeroes.Where(hero => hero != null))
            {
                SoakLog.Write("HERO_AGE", "id=" + (hero.StringId ?? "unknown")
                    + "; age=" + hero.Age.ToString("F4", CultureInfo.InvariantCulture)
                    + "; birthDay=" + hero.BirthDay.ToDays.ToString("F3", CultureInfo.InvariantCulture));
            }
        }

        private static void LogClanExpenses()
        {
            foreach (Clan clan in Clan.All.Where(clan => clan != null))
            {
                try
                {
                    ExplainedNumber expenses = Campaign.Current.Models.ClanFinanceModel.CalculateClanExpenses(
                        clan, includeDescriptions: true, applyWithdrawals: false, includeDetails: true);
                    SoakLog.Write("CLAN_ECONOMY", "id=" + clan.StringId
                        + "; leaderGold=" + (clan.Leader == null ? "unavailable" : clan.Leader.Gold.ToString(CultureInfo.InvariantCulture))
                        + "; tributeWallet=" + clan.TributeWallet.ToString(CultureInfo.InvariantCulture)
                        + "; debtToKingdom=" + clan.DebtToKingdom.ToString(CultureInfo.InvariantCulture)
                        + "; expenses=" + expenses.ResultNumber.ToString("F4", CultureInfo.InvariantCulture)
                        + "; observationOnly=True; applyWithdrawals=False");
                    if (clan == Clan.PlayerClan)
                        SoakLog.Write("CLAN_EXPENSES", "result=" + expenses.ResultNumber.ToString("F4", CultureInfo.InvariantCulture)
                            + "; mainPartyNativeWage=" + (MobileParty.MainParty == null ? 0 : MobileParty.MainParty.TotalWage)
                            + "; playerGold=" + (Hero.MainHero == null ? 0 : Hero.MainHero.Gold)
                            + "; observationOnly=True; applyWithdrawals=False");
                }
                catch (Exception exception)
                {
                    SoakLog.Write("CLAN_ECONOMY_FAILURE", "id=" + clan.StringId + "; " + exception.GetType().Name + ": " + exception.Message);
                }
            }
        }

        private static void LogSiegePreparation()
        {
            foreach (Settlement settlement in Settlement.All.Where(s => s != null && s.SiegeEvent != null))
            {
                object camp = settlement.SiegeEvent.BesiegerCamp;
                PropertyInfo progress = camp == null ? null : camp.GetType().GetProperty(
                    "PreparationProgress", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string value = progress == null ? "unavailable" : Convert.ToString(
                    progress.GetValue(camp, null), CultureInfo.InvariantCulture);
                SoakLog.Write("SIEGE", "settlement=" + settlement.StringId + "; preparation=" + value);
            }
        }

        private static void LogProfileFingerprint()
        {
            try
            {
                Assembly calendar = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                    assembly => string.Equals(assembly.GetName().Name, "AgesOfCalradia", StringComparison.Ordinal));
                Type profile = calendar == null ? null : calendar.GetType("TwelveMonthCalendar.CalendarCampaignProfile", false);
                MethodInfo capture = profile == null ? null : profile.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public);
                object value = capture == null ? null : capture.Invoke(null, null);
                FieldInfo fingerprint = profile == null ? null : profile.GetField("Fingerprint", BindingFlags.Instance | BindingFlags.Public);
                SoakLog.Write("PROFILE", "fingerprint=" + (value == null || fingerprint == null ? "unavailable" : fingerprint.GetValue(value)));
            }
            catch (Exception exception)
            {
                SoakLog.Write("PROFILE_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }
        }

        private static void OnTournamentStarted(Town town)
        {
            SoakLog.Write("TOURNAMENT_STARTED", town == null ? "unknown" : town.StringId);
        }

        private static void OnTournamentCancelled(Town town)
        {
            SoakLog.Write("TOURNAMENT_CANCELLED", town == null ? "unknown" : town.StringId);
        }

        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            SoakLog.Write("WAR_DECLARED", FactionName(first) + " vs " + FactionName(second) + "; " + detail);
            _peaceDiagnostics.OnWar(first, second, detail);
        }

        private void OnPeaceMade(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            SoakLog.Write("PEACE_MADE", FactionName(first) + " vs " + FactionName(second) + "; " + detail);
            _peaceDiagnostics.OnPeace(first, second, detail);
        }

        private static void OnSiegeCompleted(Settlement settlement, MobileParty party, bool isWin, MapEvent.BattleTypes battleType)
        {
            SoakLog.Write("SIEGE_COMPLETED", (settlement == null ? "unknown" : settlement.StringId)
                + "; winner=" + isWin + "; type=" + battleType);
        }

        private static string FactionName(IFaction faction)
        {
            return faction == null ? "unknown" : faction.StringId;
        }
    }

    internal sealed class SoakControl
    {
        internal double StartDay;
        internal double TargetDay;
        internal double NextCheckpointDay;
        internal int ReloadsCompleted;
        internal bool Completed;
        internal DateTime DeadlineUtc;

        internal double CheckpointIntervalDays
        {
            get { return (TargetDay - StartDay) / 5d; }
        }

        internal bool IsDeadlineReached(DateTime utcNow)
        {
            return DeadlineUtc != DateTime.MinValue && utcNow >= DeadlineUtc;
        }

        internal static SoakControl LoadOrCreate(double currentDay)
        {
            try
            {
                if (File.Exists(SoakLog.ControlPath))
                {
                    Dictionary<string, string> entries = File.ReadAllLines(SoakLog.ControlPath)
                        .Select(line => line.Split(new[] { '=' }, 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
                    SoakControl loaded = new SoakControl();
                    loaded.StartDay = Parse(entries, "StartDay");
                    loaded.TargetDay = Parse(entries, "TargetDay");
                    loaded.NextCheckpointDay = Parse(entries, "NextCheckpointDay");
                    loaded.ReloadsCompleted = (int)Parse(entries, "ReloadsCompleted");
                    loaded.Completed = string.Equals(entries["Completed"], "true", StringComparison.OrdinalIgnoreCase);
                    string deadline;
                    loaded.DeadlineUtc = entries.TryGetValue("DeadlineUtc", out deadline)
                        ? DateTime.Parse(deadline, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                        : DateTime.MinValue;
                    return loaded;
                }
            }
            catch (Exception exception)
            {
                SoakLog.Write("CONTROL_LOAD_FAILURE", exception.GetType().Name + ": " + exception.Message);
            }

            SoakControl created = new SoakControl
            {
                StartDay = currentDay,
                TargetDay = currentDay + TargetDays(),
                NextCheckpointDay = currentDay + TargetDays() / 5d,
                ReloadsCompleted = 0,
                Completed = false,
                DeadlineUtc = File.Exists(SoakLog.DeadlinePath)
                    ? DateTime.Parse(File.ReadAllText(SoakLog.DeadlinePath).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    : DateTime.MinValue
            };
            created.Save();
            return created;
        }

        internal void Save()
        {
            string[] lines =
            {
                "StartDay=" + StartDay.ToString("R", CultureInfo.InvariantCulture),
                "TargetDay=" + TargetDay.ToString("R", CultureInfo.InvariantCulture),
                "NextCheckpointDay=" + NextCheckpointDay.ToString("R", CultureInfo.InvariantCulture),
                "ReloadsCompleted=" + ReloadsCompleted.ToString(CultureInfo.InvariantCulture),
                "Completed=" + Completed.ToString(),
                "DeadlineUtc=" + DeadlineUtc.ToString("O", CultureInfo.InvariantCulture)
            };
            File.WriteAllLines(SoakLog.ControlPath, lines);
        }

        internal string Describe()
        {
            return "start=" + StartDay.ToString("F3", CultureInfo.InvariantCulture)
                + "; target=" + TargetDay.ToString("F3", CultureInfo.InvariantCulture)
                + "; checkpoint=" + NextCheckpointDay.ToString("F3", CultureInfo.InvariantCulture)
                + "; reloads=" + ReloadsCompleted
                + "; completed=" + Completed;
        }

        private static double Parse(IDictionary<string, string> entries, string key)
        {
            return double.Parse(entries[key], CultureInfo.InvariantCulture);
        }

        private static double TargetDays()
        {
            if (!File.Exists(SoakLog.TargetDaysPath)) return CalendarSoakBehavior.DefaultTargetCalendarDays;
            double days;
            if (!double.TryParse(File.ReadAllText(SoakLog.TargetDaysPath).Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out days) || days < 1d || days > 731d)
                throw new InvalidDataException("AocSoakTargetDays.txt must be a campaign-day target from 1 to 731.");
            return days;
        }
    }
}
