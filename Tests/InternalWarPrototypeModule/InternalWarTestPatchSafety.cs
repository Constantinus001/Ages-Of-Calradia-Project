using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace AgesOfCalradiaInternalWarsTest
{
    /// <summary>
    /// Compatibility boundary: exact inspected Bannerlord v1.4.8 CampaignSystem assembly only.
    /// Failure occurs before Harmony registration, leaving the campaign completely native.
    /// Covered by Verify-InternalWarTestModule.ps1 and the isolated Release build.
    /// </summary>
    internal static class InternalWarTestPatchSafety
    {
        private static readonly Guid SupportedCampaignSystemMvid = new Guid("886629fe-6e60-40d7-9a57-8d46017179d9");

        internal static bool Validate(out string failure)
        {
            Guid actual = typeof(Campaign).Assembly.ManifestModule.ModuleVersionId;
            if (actual != SupportedCampaignSystemMvid)
            {
                failure = "unsupported CampaignSystem MVID " + actual + "; expected " + SupportedCampaignSystemMvid + ".";
                return false;
            }
            Type conquestQuest = AccessTools.TypeByName(InternalWarNonCapturingQuestPatch.NativeTypeName);
            MethodBase[] required =
            {
                conquestQuest == null ? null : AccessTools.Method(conquestQuest, "OnSiegeCompleted",
                    new[] { typeof(Settlement), typeof(MobileParty), typeof(bool), typeof(MapEvent.BattleTypes) }),
                AccessTools.Method(typeof(SiegeEvent), nameof(SiegeEvent.CanPartyJoinSide), new[] { typeof(PartyBase), typeof(BattleSideEnum) }),
                AccessTools.PropertySetter(typeof(MobileParty), nameof(MobileParty.BesiegerCamp)),
                AccessTools.PropertySetter(typeof(PartyBase), nameof(PartyBase.MapEventSide)),
                AccessTools.Method(typeof(Town), nameof(Town.GetDefenderParties), new[] { typeof(MapEvent.BattleTypes) }),
                AccessTools.Method(typeof(Town), nameof(Town.GetNextDefenderParty), new[] { typeof(int).MakeByRefType(), typeof(MapEvent.BattleTypes) }),
                AccessTools.Method(typeof(MapEvent), "Update"),
                AccessTools.Method(typeof(MapEvent), nameof(MapEvent.CanPartyJoinBattle), new[] { typeof(PartyBase), typeof(BattleSideEnum) }),
                AccessTools.Method(typeof(ChangeOwnerOfSettlementAction), nameof(ChangeOwnerOfSettlementAction.ApplyBySiege)),
                AccessTools.Method(typeof(SettlementClaimantCampaignBehavior), nameof(SettlementClaimantCampaignBehavior.OnSettlementOwnerChanged)),
                AccessTools.Method(typeof(PlayerEncounter), nameof(PlayerEncounter.LeaveSettlement)),
                AccessTools.Method(typeof(PlayerEncounter), nameof(PlayerEncounter.Finish), new[] { typeof(bool) }),
                // Player entry, not AI siege movement: audit exact signatures before enabling the test UI.
                AccessTools.Method(typeof(MobileParty), nameof(MobileParty.SetMoveGoToSettlement), new[] { typeof(Settlement), typeof(MobileParty.NavigationType), typeof(bool) }),
                AccessTools.Method(typeof(SiegeEventManager), nameof(SiegeEventManager.StartSiegeEvent), new[] { typeof(Settlement), typeof(MobileParty) }),
                AccessTools.Method(typeof(PlayerSiege), nameof(PlayerSiege.StartPlayerSiege), new[] { typeof(BattleSideEnum), typeof(bool), typeof(Settlement) }),
                AccessTools.Method(typeof(PlayerSiege), nameof(PlayerSiege.StartSiegePreparation), Type.EmptyTypes),
                AccessTools.Method(typeof(PlayerEncounter), nameof(PlayerEncounter.SetupFields), new[] { typeof(PartyBase), typeof(PartyBase) })
                ,AccessTools.Method(typeof(DefaultEncounterGameMenuModel), nameof(DefaultEncounterGameMenuModel.GetEncounterMenu),
                    new[] { typeof(PartyBase), typeof(PartyBase), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() })
                ,AccessTools.Method(typeof(DefaultMobilePartyAIModel), nameof(DefaultMobilePartyAIModel.GetBestInitiativeBehavior))
                ,AccessTools.Method(typeof(DefaultMobilePartyAIModel), "IsEnemy", new[] { typeof(PartyBase), typeof(MobileParty) })
                ,AccessTools.Method(typeof(DefaultMobilePartyAIModel), "CalculateInitiativeScoresForEnemy",
                    new[] { typeof(MobileParty), typeof(MobileParty), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(float), typeof(float) })
                ,AccessTools.Method(typeof(DefaultMobilePartyAIModel), "CalculateStanceScore", new[] { typeof(MobileParty), typeof(MobileParty) })
                ,AccessTools.Method(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter), new[] { typeof(PartyBase), typeof(PartyBase) })
                ,AccessTools.Method(typeof(EncounterManager), nameof(EncounterManager.StartSettlementEncounter), new[] { typeof(MobileParty), typeof(Settlement) })
                ,AccessTools.Method(typeof(BeHostileAction), "ApplyInternal", new[] { typeof(PartyBase), typeof(PartyBase), typeof(float) })
                ,AccessTools.Method(typeof(EncounterGameMenuBehavior), "UpdateVillageHostileActionEncounter")
                ,AccessTools.Method(typeof(MobileParty), nameof(MobileParty.SetMoveBesiegeSettlement), new[] { typeof(Settlement), typeof(MobileParty.NavigationType) })
                ,AccessTools.Method(typeof(MobileParty), nameof(MobileParty.SetMoveRaidSettlement), new[] { typeof(Settlement), typeof(MobileParty.NavigationType), typeof(bool) })
                ,AccessTools.Method(typeof(StartBattleAction), nameof(StartBattleAction.ApplyStartSallyOut), new[] { typeof(Settlement), typeof(MobileParty) })
                ,AccessTools.Method(typeof(PlayerEncounter), "Init", new[] { typeof(PartyBase), typeof(PartyBase), typeof(Settlement) })
                ,AccessTools.Method(typeof(EncounterGameMenuBehavior), "menu_sally_out_from_gate_on_condition")
                ,AccessTools.Method(typeof(EncounterGameMenuBehavior), "sally_out_consequence")
                ,AccessTools.Method(typeof(SallyOutsCampaignBehavior), "CheckSallyOut", new[] { typeof(Settlement), typeof(bool), typeof(bool).MakeByRefType() })
            };
            foreach (MethodBase method in required)
            {
                if (method == null)
                {
                    failure = "one or more required siege test targets are missing.";
                    return false;
                }
            }
            foreach (string name in new[] { "<PlayerSide>k__BackingField", "<OpponentSide>k__BackingField" })
            {
                FieldInfo field = AccessTools.Field(typeof(PlayerEncounter), name);
                if (field == null || field.FieldType != typeof(BattleSideEnum) || field.IsStatic || field.IsInitOnly)
                {
                    failure = "the exact native player-encounter side field changed: " + name;
                    return false;
                }
            }
            failure = string.Empty;
            return true;
        }
    }
}
