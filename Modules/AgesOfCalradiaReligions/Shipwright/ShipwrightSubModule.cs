using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradiaReligions.Shipwright
{
    public sealed class ShipwrightSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            ShipwrightDiagnostics.Info("Shipwright v0.2.0 entering OnSubModuleLoad.");
            try
            {
                base.OnSubModuleLoad();
                ShipwrightDiagnostics.Info("Shipwright sidecar loaded; waiting for a campaign session.");
            }
            catch (Exception exception)
            {
                ShipwrightDiagnostics.Error("OnSubModuleLoad failed.", exception);
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
                    starter.AddBehavior(new ShipwrightCampaignBehavior());
                    ShipwrightDiagnostics.Info("Shipwright campaign behavior registered.");
                }
            }
            catch (Exception exception)
            {
                ShipwrightDiagnostics.Error("OnGameStart failed.", exception);
                throw;
            }
        }
    }
}
