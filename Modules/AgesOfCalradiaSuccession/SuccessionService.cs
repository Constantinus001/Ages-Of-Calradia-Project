using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradiaSuccession
{
    public static class SuccessionService
    {
        // Resolve against the active campaign; never retain a previous save's
        // behavior (and hero references) in a process-wide static field.
        internal static SuccessionCampaignBehavior CurrentBehavior
        {
            get { return Campaign.Current == null ? null : Campaign.Current.GetCampaignBehavior<SuccessionCampaignBehavior>(); }
        }

        public static SuccessionLaw GetLaw(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? SuccessionResolver.DefaultLawFor(kingdom) : behavior.GetLaw(kingdom);
        }

        public static string GetCrisisStatus(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? "None" : behavior.GetCrisisStatus(kingdom);
        }

        public static IReadOnlyList<SuccessionClaim> GetClaimants(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? new List<SuccessionClaim>() : behavior.GetClaimants(kingdom);
        }

        public static Hero GetUnderageHeir(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? null : behavior.GetMinorHeir(kingdom);
        }

        public static Hero GetRegent(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? null : behavior.GetRegent(kingdom);
        }

        public static float GetLegitimacy(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? 50f : behavior.GetLegitimacy(kingdom);
        }

        public static bool IsCoronated(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior != null && behavior.IsCoronated(kingdom);
        }

        public static Hero GetPretender(Kingdom kingdom)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? null : behavior.GetPretender(kingdom);
        }

        public static ClanRecognition GetRecognition(Kingdom kingdom, Clan clan)
        {
            SuccessionCampaignBehavior behavior = CurrentBehavior;
            return behavior == null ? ClanRecognition.Neutral : behavior.GetRecognition(kingdom, clan);
        }
    }
}
