using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaInternalWarsTest
{
    public sealed class InternalWarTestSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "com.agesofcalradia.test.internalwars.v148";
        private Harmony _harmony;
        private string _startupFailure;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            InternalWarTestDiagnostics.Initialize();
            string failure;
            if (!InternalWarTestPatchSafety.Validate(out failure))
            {
                _startupFailure = failure;
                InternalWarTestDiagnostics.Info("Test module disabled safely: " + failure);
                return;
            }

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                InternalWarTestDiagnostics.Info("Test-only internal-war patches registered for the inspected Bannerlord v1.4.8 build.");
            }
            catch (Exception exception)
            {
                if (_harmony != null) _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
                _startupFailure = exception.Message;
                InternalWarTestDiagnostics.Error("Patch registration failed; every test patch was removed.", exception);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (!string.IsNullOrEmpty(_startupFailure))
                InformationManager.DisplayMessage(new InformationMessage(
                    "TEST ONLY - Internal Wars disabled safely: " + _startupFailure));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
            if (starter != null && _harmony != null) starter.AddBehavior(new InternalWarTestBehavior());
        }

        protected override void OnSubModuleUnloaded()
        {
            InternalWarTestService.Detach();
            if (_harmony != null)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
            }
            base.OnSubModuleUnloaded();
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_harmony != null) InternalWarTestService.DrainCleanup(dt);
        }
    }
}
