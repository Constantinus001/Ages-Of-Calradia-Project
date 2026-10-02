using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.SoakDiagnostics
{
    /// <summary>
    /// Diagnostics-only campaign module for the native long-run calendar soak.
    /// It owns no saveable state, does not alter core models, and is never part
    /// of the player release package.
    /// </summary>
    public sealed class AgesOfCalradiaSoakDiagnosticsSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            SoakLog.Initialize();
            PacingCalibration.Initialize();
            SoakLog.Write("MODULE_LOADED", "diagnostics-only; no production patches or save types");
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
            if (game == null || !(game.GameType is Campaign) || starter == null)
            {
                SoakLog.Write("NON_CAMPAIGN", "soak behavior was not registered");
                return;
            }

            if (!ShouldRegister(PacingCalibration.Enabled,CalendarSoakBehavior.IsArmed,
                System.IO.File.Exists(SupplyCapture.MarkerPath),System.IO.File.Exists(SupplyCapture.RollingMarkerPath))) return;
            starter.AddBehavior(new CalendarSoakBehavior());
            SoakLog.Write("BEHAVIOR_REGISTERED", "campaign soak behavior added");
        }

        internal static bool ShouldRegister(bool pacing,bool soak,bool oneShot,bool rolling)
        { return !pacing&&(soak||oneShot||rolling); }

        protected override void OnApplicationTick(float dt)
        {
            // CampaignEvents.TickEvent is not a per-frame callback in the
            // supported game build. Keep the diagnostics-only time controller
            // active at the engine boundary so campaign interruptions cannot
            // leave an unattended soak paused.
            base.OnApplicationTick(dt);
            if (PacingCalibration.Enabled) PacingCalibration.Tick();
            else CalendarSoakBehavior.ApplicationTick(dt);
        }

        protected override void OnSubModuleUnloaded()
        {
            SupplyCapture.Stop("module_unloaded_incomplete");
            SupplyChainObserver.Uninstall();
            CalendarSoakBehavior.ReleaseTimeControl();
            EconomyTransactionDiagnostics.Stop();
            EconomyDiagnosticPatches.Uninstall();
            SoakLog.Write("MODULE_UNLOADED", "diagnostics-only module unloaded");
            base.OnSubModuleUnloaded();
        }

        public override void OnGameEnd(Game game)
        {
            SupplyCapture.Stop("game_ended_incomplete");
            SupplyChainObserver.Uninstall();
            EconomyTransactionDiagnostics.Stop();
            EconomyDiagnosticPatches.Uninstall();
            base.OnGameEnd(game);
        }
    }
}
