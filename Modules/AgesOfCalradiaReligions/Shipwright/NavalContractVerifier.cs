using System;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace AgesOfCalradiaReligions.Shipwright
{
    /// <summary>
    /// Audits the public Bannerlord/War Sails v1.4.8/v1.2.8 surface used by the
    /// sidecar. A failed audit disables its menu action instead of opening a
    /// partially compatible native screen.
    /// </summary>
    internal static class NavalContractVerifier
    {
        private const string DromonHullId = "empire_heavy_ship";
        private static bool _hasResult;
        private static bool _isValid;
        private static string _failure;

        internal static bool TryValidate(out string failure)
        {
            if (!_hasResult)
            {
                ValidateOnce();
            }

            failure = _failure;
            return _isValid;
        }

        private static void ValidateOnce()
        {
            _hasResult = true;
            try
            {
                Type[] portConstructor =
                {
                    typeof(PartyBase),
                    typeof(PartyBase),
                    typeof(Action),
                    typeof(PortScreenModes)
                };

                if (typeof(Ship).GetConstructor(new[] { typeof(ShipHull) }) == null)
                {
                    SetFailure("Ship(ShipHull) is missing");
                    return;
                }

                if (typeof(PortState).GetConstructor(portConstructor) == null)
                {
                    SetFailure("the callback PortState constructor is missing");
                    return;
                }

                if (!Enum.IsDefined(typeof(PortScreenModes), PortScreenModes.TradeMode))
                {
                    SetFailure("PortScreenModes.TradeMode is missing");
                    return;
                }

                ShipHull hull = MBObjectManager.Instance.GetObject<ShipHull>(DromonHullId);
                if (hull == null || hull.AvailableSlots == null || hull.AvailableSlots.Count == 0)
                {
                    SetFailure("the Dromon hull or its upgrade slots are missing");
                    return;
                }

                _isValid = true;
                _failure = null;
                ShipwrightDiagnostics.Info("War Sails v1.2.8 commissioning contract verified for hull " + DromonHullId + ".");
            }
            catch (Exception exception)
            {
                _isValid = false;
                _failure = exception.GetType().Name + " while auditing the native contract";
                ShipwrightDiagnostics.Error("War Sails commissioning contract audit failed; the menu action will remain disabled.", exception);
            }
        }

        private static void SetFailure(string failure)
        {
            _isValid = false;
            _failure = failure;
            ShipwrightDiagnostics.Warning("War Sails commissioning contract rejected: " + failure + ".");
        }
    }
}
