using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace AgesOfCalradiaReligions.Shipwright
{
    internal static class ShipwrightPortLauncher
    {
        private const string DromonHullId = "empire_heavy_ship";

        internal static void OpenDromonCommissioning()
        {
            try
            {
                string contractFailure;
                if (!NavalContractVerifier.TryValidate(out contractFailure))
                {
                    FailForPlayer("The shipwright is unavailable because the War Sails API contract changed: " + contractFailure);
                    return;
                }

                Settlement port = Settlement.CurrentSettlement;
                MobileParty mainParty = MobileParty.MainParty;
                if (port == null || !port.IsTown || !port.HasPort || port.Party == null || mainParty == null || mainParty.Party == null)
                {
                    FailForPlayer("A custom ship can only be commissioned while visiting a functioning port town.");
                    return;
                }

                ShipHull hull = MBObjectManager.Instance.GetObject<ShipHull>(DromonHullId);
                if (hull == null)
                {
                    FailForPlayer("War Sails did not register the Dromon hull '" + DromonHullId + "'.");
                    return;
                }

                DromonBuilderSession.Start(port, mainParty, hull);
            }
            catch (Exception exception)
            {
                ShipwrightDiagnostics.Error("Starting the Dromon builder failed safely.", exception);
                InformationManager.DisplayMessage(new InformationMessage("The shipwright could not start the Dromon builder. See AgesOfCalradiaReligions.Shipwright.log."));
            }
        }

        internal static void OpenConfiguredCommissioning(Ship preview, Settlement port, MobileParty mainParty)
        {
            PartyBase portParty = null;
            try
            {
                if (preview == null || port == null || port.Party == null || mainParty == null || mainParty.Party == null)
                {
                    FailForPlayer("The configured Dromon could not be handed to the current port.");
                    return;
                }

                portParty = port.Party;
                preview.Owner = portParty;
                Action onClosed = delegate { OnCommissioningClosed(preview, mainParty.Party, port); };
                PortState portState = GameStateManager.Current.CreateState<PortState>(new object[]
                {
                    portParty,
                    mainParty.Party,
                    onClosed,
                    PortScreenModes.TradeMode
                });

                ShipwrightDiagnostics.Info("Opening manager-created native Trade-mode port screen for Dromon commissioning at " + port.StringId + ".");
                GameStateManager.Current.PushState(portState);
            }
            catch (Exception exception)
            {
                if (preview != null && preview.Owner == portParty)
                {
                    preview.Owner = null;
                }
                ShipwrightDiagnostics.Error("Opening the configured War Sails commissioning screen failed safely.", exception);
                InformationManager.DisplayMessage(new InformationMessage("The shipwright could not open the War Sails ship builder. See AgesOfCalradiaReligions.Shipwright.log."));
            }
        }

        private static void OnCommissioningClosed(Ship preview, PartyBase mainParty, Settlement port)
        {
            if (preview.Owner == mainParty)
            {
                ShipwrightDiagnostics.Info("Dromon commissioned at " + port.StringId + " through native War Sails trade ownership.");
                InformationManager.DisplayMessage(new InformationMessage("Your custom Dromon has joined the fleet."));
            }
            else
            {
                if (preview.Owner == port.Party)
                {
                    preview.Owner = null;
                }
                ShipwrightDiagnostics.Info("Dromon commissioning cancelled at " + port.StringId + "; no ship ownership changed.");
            }
        }

        private static void FailForPlayer(string message)
        {
            ShipwrightDiagnostics.Warning(message);
            InformationManager.DisplayMessage(new InformationMessage(message));
        }
    }
}
