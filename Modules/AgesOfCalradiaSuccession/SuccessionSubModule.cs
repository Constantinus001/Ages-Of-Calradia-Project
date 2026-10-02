using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaSuccession
{
    public sealed class SuccessionSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            SuccessionDiagnostics.Initialize();
            SuccessionDiagnostics.Info("Succession v0.4.3 entering OnSubModuleLoad.");
            try
            {
                base.OnSubModuleLoad();
                SuccessionElectionPatches.Install();
                SuccessionDiagnostics.Info("Succession v0.4.3 loaded. Native kingdom ruler votes resolve by hereditary law.");
            }
            catch (Exception exception)
            {
                SuccessionDiagnostics.Error("OnSubModuleLoad failed.", exception);
                throw;
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try
            {
                base.OnGameStart(game, gameStarterObject);
                CampaignGameStarter starter = gameStarterObject as CampaignGameStarter;
                if (starter != null)
                {
                    starter.AddBehavior(new SuccessionCampaignBehavior());
                    SuccessionDiagnostics.Info("Succession campaign behavior registered.");
                }
            }
            catch (Exception exception)
            {
                SuccessionDiagnostics.Error("OnGameStart failed.", exception);
                throw;
            }
        }
    }
}
