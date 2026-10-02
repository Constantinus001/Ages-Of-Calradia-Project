using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AgesOfCalradiaReligions.Shipwright
{
    /// <summary>
    /// Optional NavalDLC reflection boundary. The Shipwright assembly remains
    /// loadable without War Sails, while an active v1.2.8 campaign supplies its
    /// authoritative culture- and shipyard-level-filtered merchandise list.
    /// </summary>
    internal static class NavalUpgradeCatalog
    {
        internal static bool TryGetAvailablePieces(Town town, out List<ShipUpgradePiece> pieces, out string failure)
        {
            pieces = new List<ShipUpgradePiece>();
            failure = null;
            if (town == null)
            {
                failure = "the current port has no town data";
                return false;
            }

            try
            {
                Assembly navalAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "NavalDLC", StringComparison.Ordinal));
                Type extensionsType = navalAssembly == null ? null : navalAssembly.GetType("NavalDLC.NavalDLCExtensions", false);
                MethodInfo method = extensionsType == null
                    ? null
                    : extensionsType.GetMethod(
                        "GetAvailableShipUpgradePieces",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Town) },
                        null);
                if (method == null)
                {
                    failure = "NavalDLCExtensions.GetAvailableShipUpgradePieces(Town) is missing";
                    return false;
                }

                IEnumerable result = method.Invoke(null, new object[] { town }) as IEnumerable;
                if (result == null)
                {
                    failure = "War Sails returned no ship-upgrade catalog";
                    return false;
                }

                foreach (object item in result)
                {
                    ShipUpgradePiece piece = item as ShipUpgradePiece;
                    if (piece != null)
                    {
                        pieces.Add(piece);
                    }
                }

                return true;
            }
            catch (TargetInvocationException exception)
            {
                failure = "War Sails rejected the ship-upgrade catalog request";
                ShipwrightDiagnostics.Error(failure + ".", exception.InnerException ?? exception);
                return false;
            }
            catch (Exception exception)
            {
                failure = exception.GetType().Name + " while reading the optional War Sails upgrade catalog";
                ShipwrightDiagnostics.Error(failure + ".", exception);
                return false;
            }
        }
    }
}
