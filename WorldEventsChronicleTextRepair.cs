using System;
using System.Reflection;
using HarmonyLib;

namespace AgesOfCalradia.WorldEventsShellRepair
{
    // Protected target: CalendarWorldLedgerBehavior.GetCharacterMilestoneStory(Hero).
    // UI-only repair of two recorded default-clan phrases; no save/history writes.
    // Pinning the approved renderer hash is owned by the shell submodule. A changed
    // signature disables this optional patch; runtime reflection errors retain the
    // original text and log once. Verify-WorldEventsStoryReadability.ps1 covers it.
    internal static class WorldEventsChronicleTextRepair
    {
        private static bool _failed;

        internal static void Install(Harmony harmony, Assembly renderer)
        {
            Type behavior = renderer.GetType("TwelveMonthCalendar.CalendarWorldLedgerBehavior", true);
            MethodInfo target = AccessTools.Method(behavior, "GetCharacterMilestoneStory");
            if (target == null || target.ReturnType != typeof(string)
                || target.GetParameters().Length != 1
                || target.GetParameters()[0].ParameterType.FullName != "TaleWorlds.CampaignSystem.Hero")
            {
                WorldEventsShellDiagnostics.Info("Chronicle text repair disabled: unsupported milestone signature.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(WorldEventsChronicleTextRepair), nameof(Postfix)));
        }

        private static void Postfix(object __0, ref string __result)
        {
            if (_failed || __0 == null || string.IsNullOrEmpty(__result)) return;
            try
            {
                object clan = AccessTools.Property(__0.GetType(), "Clan").GetValue(__0, null);
                if (clan == null) return;
                object name = AccessTools.Property(clan.GetType(), "Name").GetValue(clan, null);
                __result = RepairPlaceholder(__result, name == null ? null : name.ToString());
            }
            catch (Exception error)
            {
                _failed = true;
                WorldEventsShellDiagnostics.Error("Chronicle label repair failed; original history retained.", error);
            }
        }

        internal static string RepairPlaceholder(string text, string clanName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(clanName)
                || string.Equals(clanName, "Playerland", StringComparison.OrdinalIgnoreCase)) return text;
            return text.Replace("Clan Playerland stands at tier ", "Clan " + clanName + " stands at tier ")
                .Replace("Clan Playerland is independent.", "Clan " + clanName + " is independent.");
        }
    }
}
