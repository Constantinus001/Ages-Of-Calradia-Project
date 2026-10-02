using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradiaInternalWarsTest
{
    // v1.4.8 StartPartyEncounter: normalize only an eligible recorded relief encounter before
    // native player Init or NPC battle creation. SiegeOutside Initialize attaches camp parties
    // to its first argument; FinalizeEventAux likewise interprets attacker victory as siege victory.
    // No battle is created here. Missing target disables the patch set; actual-prefix tests verify
    // both input orders and neutral no-op. Native API/IL and live battle tests remain required.
    [HarmonyPatch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter), new[] { typeof(PartyBase), typeof(PartyBase) })]
    internal static class InternalWarReliefEncounterPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ref PartyBase attackerParty, ref PartyBase defenderParty)
        { Normalize(ref attackerParty, ref defenderParty); }

        internal static void Normalize(ref PartyBase first, ref PartyBase second)
        {
            if (!InternalWarReliefService.CanStart(first, second) || first.MobileParty.BesiegerCamp != null) return;
            PartyBase swap = first; first = second; second = swap;
        }
    }

    // Direct native player Init callers must normalize before BOTH SetupFields and the later
    // argument-dependent Init logic. A SetupFields-only correction would leave inconsistent sides.
    [HarmonyPatch(typeof(PlayerEncounter), "Init", new[] { typeof(PartyBase), typeof(PartyBase), typeof(Settlement) })]
    internal static class InternalWarReliefPlayerInitPatch
    {
        [HarmonyPrefix]
        internal static void Prefix(ref PartyBase attackerParty, ref PartyBase defenderParty)
        { InternalWarReliefEncounterPatch.Normalize(ref attackerParty, ref defenderParty); }
    }
}
