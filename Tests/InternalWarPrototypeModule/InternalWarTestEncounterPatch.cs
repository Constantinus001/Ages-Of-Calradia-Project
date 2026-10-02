using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// Native v1.4.8 PlayerEncounter.SetupFields(PartyBase, PartyBase) postfix: shared kingdom MapFaction
    /// misclassifies the player besieger as Defender. Correct only the current registered player-led
    /// siege encounter, before a player map event exists. Other encounters and existing battles stay native.
    /// Private side fields are exact-version contracts, checked before enabling the module. Field-ref
    /// creation is forced during Harmony preparation; failure aborts registration and removes all test patches.
    /// Covered by native IL/field audit and actual guard/postfix tests against explicit data stubs;
    /// native Lead an assault/mission/cleanup still require disposable-campaign runtime verification.
    /// </summary>
    [HarmonyPatch(typeof(PlayerEncounter), nameof(PlayerEncounter.SetupFields), new[] { typeof(PartyBase), typeof(PartyBase) })]
    internal static class TestPlayerEncounterSidePatch
    {
        private static readonly AccessTools.FieldRef<PlayerEncounter, BattleSideEnum> PlayerSideField =
            AccessTools.FieldRefAccess<PlayerEncounter, BattleSideEnum>("<PlayerSide>k__BackingField");
        private static readonly AccessTools.FieldRef<PlayerEncounter, BattleSideEnum> OpponentSideField =
            AccessTools.FieldRefAccess<PlayerEncounter, BattleSideEnum>("<OpponentSide>k__BackingField");

        [HarmonyPrepare]
        private static bool Prepare()
        {
            if (PlayerSideField == null || OpponentSideField == null)
                throw new InvalidOperationException("Player encounter side access could not be prepared safely.");
            return true;
        }

        [HarmonyPostfix]
        internal static void Postfix(PlayerEncounter __instance, PartyBase attackerParty, PartyBase defenderParty)
        {
            if (__instance == null || __instance != PlayerEncounter.Current
                || (!InternalWarTestService.IsPlayerSiegeEncounter(attackerParty, defenderParty)
                    && !InternalWarTestService.IsPlayerRaidEncounter(attackerParty, defenderParty))) return;
            if (PlayerSideField(__instance) != BattleSideEnum.Defender || OpponentSideField(__instance) != BattleSideEnum.Attacker) return;
            PlayerSideField(__instance) = BattleSideEnum.Attacker;
            OpponentSideField(__instance) = BattleSideEnum.Defender;
        }
    }
}
