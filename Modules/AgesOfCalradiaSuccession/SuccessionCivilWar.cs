using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace AgesOfCalradiaSuccession
{
    internal static class SuccessionCivilWar
    {
        internal static bool TryStart(Kingdom original, Hero pretender, IEnumerable<Clan> supporters,
            SuccessionCampaignBehavior behavior, out string result)
        {
            result = string.Empty;
            if (behavior == null || !SuccessionResolver.IsEligibleClanLeader(original, pretender))
            {
                result = "The selected realm has no valid claimant.";
                return false;
            }
            Clan claimantClan = pretender.Clan;
            if (claimantClan.Kingdom != original || claimantClan == original.RulingClan)
            {
                result = "The claimant must lead a non-ruling clan inside the selected realm.";
                return false;
            }

            List<Clan> defectors = (supporters ?? Enumerable.Empty<Clan>())
                .Where(c => c != null && c != original.RulingClan && SuccessionResolver.IsEligibleClanLeader(original, c.Leader)
                    && c.Leader.Clan == c)
                .Distinct()
                .ToList();
            if (!defectors.Contains(claimantClan)) defectors.Insert(0, claimantClan);

            bool started = false;
            try
            {
                int day = (int)Math.Floor(CampaignTime.Now.ToDays);
                string id = "aoc_claimant_" + SafeId(original.StringId) + "_" + SafeId(claimantClan.StringId) + "_" + day;
                if (Kingdom.All.Any(k => k != null && k.StringId == id))
                {
                    result = "A claimant realm with this identity already exists today.";
                    return false;
                }

                if (!behavior.TryBeginCrisis(original, pretender, id))
                {
                    result = "This realm already has an unresolved succession or claimant crisis.";
                    return false;
                }
                started = true;

                Kingdom claimantRealm = Kingdom.CreateKingdom(id);
                TextObject name = new TextObject(pretender.Name + "'s Realm");
                Settlement home = claimantClan.Fiefs.Count > 0 ? claimantClan.Fiefs[0].Settlement : original.InitialHomeSettlement;
                uint claimantPrimary = VariantOfKingdomColor(original.Color);
                uint claimantSecondary = VariantOfKingdomColor(original.Color2);
                Banner clanBanner = claimantClan.ClanOriginalBanner ?? claimantClan.Banner;
                Banner banner = clanBanner == null ? new Banner() : new Banner(clanBanner, claimantPrimary, claimantSecondary);
                claimantRealm.InitializeKingdom(name, name, original.Culture, banner, claimantPrimary, claimantSecondary,
                    home, name, name, new TextObject("A succession claimant realm formed to press " + pretender.Name + "'s right to the crown."));

                ChangeKingdomAction.ApplyByCreateKingdom(claimantClan, claimantRealm, true);
                foreach (Clan clan in defectors)
                {
                    if (clan == claimantClan || clan.Kingdom != original) continue;
                    ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, original, claimantRealm, CampaignTime.Now, true);
                }
                if (claimantRealm.RulingClan != claimantClan) ChangeRulingClanAction.Apply(claimantRealm, claimantClan);
                if (claimantRealm.Leader != pretender || claimantClan.Kingdom != claimantRealm
                    || defectors.Any(c => c.Kingdom != claimantRealm))
                    throw new InvalidOperationException("A native or external action prevented claimant formation.");
                DeclareWarAction.ApplyByClaimOnThrone(claimantRealm, original);
                behavior.RegisterClaimantRealm(original, claimantRealm, pretender);
                behavior.FinishCrisisCreation(original, true);
                started = false;

                string borderRefresh;
                bool borderRefreshRequested = SuccessionCampaignMapBorderBridge.RequestRefresh(out borderRefresh);

                result = pretender.Name + " formed " + claimantRealm.Name + " with " + defectors.Count
                    + " clan(s) and declared a succession war on " + original.Name + ". Campaign-map borders "
                    + (borderRefreshRequested ? "are rebuilding now." : "will refresh at the next ownership audit.");
                SuccessionDiagnostics.Info("SUCCESSION WAR: " + result + " Border bridge: " + borderRefresh
                    + "; claimantPrimary=0x" + claimantPrimary.ToString("X8")
                    + "; claimantSecondary=0x" + claimantSecondary.ToString("X8") + ".");
                return true;
            }
            catch (Exception exception)
            {
                if (started) behavior.FinishCrisisCreation(original, false);
                SuccessionDiagnostics.Error("Succession war creation failed.", exception);
                result = "The succession war could not be created safely. Check the succession log.";
                return false;
            }
        }

        // Bannerlord 1.4.8 native action boundary. Only current members return;
        // third-party defections and third-party conquests are never undone.
        internal static void Reunify(Kingdom original, Kingdom claimantRealm, Hero claimant, bool claimantWins)
        {
            if (FactionManager.IsAtWarAgainstFaction(original, claimantRealm)) MakePeaceAction.Apply(original, claimantRealm);
            Clan claimantClan = claimant == null ? null : claimant.Clan;
            foreach (Clan clan in claimantRealm.Clans.Where(c => c != null && !c.IsEliminated)
                .OrderBy(c => c == claimantRealm.RulingClan ? 1 : 0).ToList())
            {
                if (clan.Kingdom != claimantRealm) continue;
                ChangeKingdomAction.ApplyByJoinToKingdom(clan, original, CampaignTime.Now, true);
                if (clan.Kingdom != original) throw new InvalidOperationException("A native or external action prevented claimant reunification.");
            }
            if (claimantWins)
            {
                if (!SuccessionResolver.IsEligibleClanLeader(original, claimant))
                    throw new InvalidOperationException("Claimant eligibility changed during reunification.");
                ChangeRulingClanAction.Apply(original, claimantClan);
                if (original.Leader != claimant) throw new InvalidOperationException("A native or external action prevented claimant accession.");
            }
            if (claimantRealm.Clans.Any(c => c != null && !c.IsEliminated))
                throw new InvalidOperationException("Claimant kingdom still contains clans after reunification.");
            if (!claimantRealm.IsEliminated) DestroyKingdomAction.Apply(claimantRealm);
            string refresh;
            SuccessionCampaignMapBorderBridge.RequestRefresh(out refresh);
        }

        private static string SafeId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "realm";
            return new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        private static uint VariantOfKingdomColor(uint source)
        {
            int red = (int)((source >> 16) & 0xFFu);
            int green = (int)((source >> 8) & 0xFFu);
            int blue = (int)(source & 0xFFu);
            int brightness = (red * 299 + green * 587 + blue * 114) / 1000;
            if (brightness < 145)
            {
                // Preserve the parent hue while making dark realms visibly lighter.
                red += (255 - red) * 35 / 100;
                green += (255 - green) * 35 / 100;
                blue += (255 - blue) * 35 / 100;
            }
            else
            {
                // Preserve the parent hue while making bright realms visibly darker.
                red = red * 65 / 100;
                green = green * 65 / 100;
                blue = blue * 65 / 100;
            }
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | (uint)blue;
        }
    }
}
