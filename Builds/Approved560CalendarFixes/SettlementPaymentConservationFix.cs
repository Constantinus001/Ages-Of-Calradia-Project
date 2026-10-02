using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace AgesOfCalradia.Approved560CalendarFixes
{
    // Native 1.4.8 ApplyForSettlementToCharacter encodes payment as a negative
    // ApplyInternal transfer. Its giver-side Min then caps the wrong wallet.
    // Prefix bounds positive payments to the actual settlement payer, preserving
    // native events, signed refunds and partial-payment (not escrow) semantics.
    // No save state. Other amount-changing prefixes remain a compatibility risk.
    // Missing signature fails module preflight and uses the logged rollback path.
    // Verify-SettlementPayment.ps1 executes native transfers, not just cap math.
    [HarmonyPatch]
    internal static class SettlementPaymentConservationFix
    {
        internal static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GiveGoldAction), "ApplyForSettlementToCharacter",
                new[] { typeof(Settlement), typeof(Hero), typeof(int), typeof(bool) })
                ?? throw new MissingMethodException("Native settlement payment target changed");
        }
        internal static void ValidateTargets() { TargetMethod(); }
        internal static int BoundPayment(int requested, int available)
        {
            return requested <= 0 ? requested : Math.Min(requested, Math.Max(0, available));
        }
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Settlement __0, Hero __1, ref int __2)
        {
            if (__0?.SettlementComponent != null && __1 != null)
                __2 = BoundPayment(__2, __0.SettlementComponent.Gold);
        }
    }
}
