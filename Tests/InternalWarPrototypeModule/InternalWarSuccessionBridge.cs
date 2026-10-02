using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// Optional, campaign-thread-only reader of the loaded succession sidecar.
    /// A pretender has a disputed crown claim, not a title to any settlement.
    /// This bridge grants no crown, fief, recognition, or declaration itself.
    /// GetClaimants is intentionally avoided: its GetLaw path initializes state.
    /// Missing or incompatible APIs fail closed. No assembly is loaded here.
    /// </summary>
    internal static class InternalWarSuccessionBridge
    {
        // Process-wide diagnostic throttle only; no campaign objects are cached.
        private static DateTime _nextFailureLogUtc;

        internal static bool HasDisputedClaim(Kingdom kingdom, Clan claimant, Clan rival)
        {
            try
            {
                if (kingdom == null || claimant == null || rival == null || claimant == rival
                    || kingdom.IsEliminated || claimant.IsEliminated || rival.IsEliminated
                    || claimant.Kingdom != kingdom || rival.Kingdom != kingdom
                    || kingdom.RulingClan != rival) return false;
                Type service = FindService();
                if (service == null) return false;
                Hero pretender = ReadPretender(service, kingdom);
                return pretender != null && pretender.IsAlive && pretender.Clan == claimant;
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
                return false;
            }
        }

        internal static string Describe(Kingdom kingdom)
        {
            if (kingdom == null) return "succession=unavailable; reason=no kingdom";
            try
            {
                Type service = FindService();
                if (service == null) return "succession=unavailable; reason=sidecar not loaded";
                Hero pretender = ReadPretender(service, kingdom);
                Clan claimant = pretender == null ? null : pretender.Clan;
                object recognition = claimant == null ? null : RequiredMethod(service,
                    "GetRecognition", new[] { typeof(Kingdom), typeof(Clan) })
                    .Invoke(null, new object[] { kingdom, claimant });
                bool dispute = pretender != null && pretender.IsAlive && claimant != null
                    && !kingdom.IsEliminated && !claimant.IsEliminated
                    && claimant.Kingdom == kingdom && kingdom.RulingClan != null
                    && !kingdom.RulingClan.IsEliminated && kingdom.RulingClan.Kingdom == kingdom
                    && kingdom.RulingClan != claimant;
                return "succession=available; crown_dispute=" + dispute
                    + "; pretender=" + (pretender == null ? "none" : pretender.StringId)
                    + "; claimant_clan=" + (claimant == null ? "none" : claimant.StringId)
                    + "; recognition=" + (recognition == null ? "none" : recognition.ToString())
                    + "; settlement_title=not inferred";
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
                return "succession=unavailable; reason=optional API failure";
            }
        }

        private static Type FindService()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, "AgesOfCalradiaSuccession",
                    StringComparison.Ordinal)) continue;
                Type service = assembly.GetType("AgesOfCalradiaSuccession.SuccessionService", false);
                if (service == null) throw new TypeLoadException("SuccessionService is missing.");
                return service;
            }
            return null;
        }

        private static Hero ReadPretender(Type service, Kingdom kingdom)
        {
            MethodInfo method = RequiredMethod(service, "GetPretender", new[] { typeof(Kingdom) });
            if (method.ReturnType != typeof(Hero))
                throw new MissingMethodException("SuccessionService.GetPretender must return Hero.");
            return (Hero)method.Invoke(null, new object[] { kingdom });
        }

        private static MethodInfo RequiredMethod(Type service, string name, Type[] parameters)
        {
            MethodInfo method = service.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                null, parameters, null);
            if (method == null) throw new MissingMethodException(service.FullName, name);
            return method;
        }

        private static void ReportFailure(Exception exception)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _nextFailureLogUtc) return;
            _nextFailureLogUtc = now.AddMinutes(1);
            InternalWarTestDiagnostics.Error("Optional succession read failed; no disputed claim authorized.", exception);
        }
    }
}
