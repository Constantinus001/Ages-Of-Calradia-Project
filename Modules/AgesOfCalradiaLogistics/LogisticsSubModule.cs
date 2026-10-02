using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace AgesOfCalradiaLogistics
{
    /// <summary>Entry point for the standalone Ages of Calradia Logistics add-on.</summary>
    public sealed class LogisticsSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            LogisticsDiagnostics.Info("Module loaded. Version=v0.3.0");
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);

            CampaignGameStarter campaignStarter = gameStarterObject as CampaignGameStarter;
            if (campaignStarter != null)
            {
                PartySpeedModel installedSpeedModel = campaignStarter.GetModel<PartySpeedModel>();
                if (installedSpeedModel == null)
                {
                    LogisticsDiagnostics.Info("The 4/8 campaign speed model was not installed because no PartySpeedModel was available.");
                }
                else if (installedSpeedModel is LogisticsPartySpeedModel)
                {
                    LogisticsDiagnostics.Info("The 4/8 campaign speed model was already installed.");
                }
                else
                {
                    campaignStarter.AddModel(new LogisticsPartySpeedModel(installedSpeedModel));
                    LogisticsDiagnostics.Info("Campaign speed model installed: base=4.0, maximum=8.0, native debuffs preserved.");
                }

                LogisticsReserveBehavior reserveBehavior = new LogisticsReserveBehavior();
                campaignStarter.AddBehavior(reserveBehavior);
                campaignStarter.AddBehavior(new LogisticsMenuBehavior(reserveBehavior));
                campaignStarter.AddBehavior(new LogisticsPlayerSupplyNotificationBehavior(reserveBehavior));
                LogisticsDiagnostics.Info("Campaign reserve behaviour registered.");
            }
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            base.OnMissionBehaviorInitialize(mission);

            if (mission != null)
            {
                // MissionTeamAIType is assigned after this initialization hook.
                // Add lightweight behaviours now; each one verifies field-battle
                // state in AfterStart before it can spawn or modify anything.
                mission.AddMissionBehavior(new BaggageTrainMissionBehavior());
                mission.AddMissionBehavior(new BaggageResupplyMissionBehavior());
                mission.AddMissionBehavior(new BaggageGuardMissionBehavior());
                mission.AddMissionBehavior(new BaggageTrainRaidMissionBehavior());
                LogisticsDiagnostics.Info("Baggage and resupply behaviours registered for mission initialization.");
            }
        }
    }
}
