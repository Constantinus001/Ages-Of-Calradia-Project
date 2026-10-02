using HarmonyLib;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.WarScoreRecovery
{
    public sealed class WarScoreRecoverySubModule : MBSubModuleBase
    {
        private Harmony _harmony;
        protected override void OnGameStart(TaleWorlds.Core.Game game, TaleWorlds.Core.IGameStarter starter)
        {
            base.OnGameStart(game, starter);
            CampaignGameStarter campaignStarter = starter as CampaignGameStarter;
            if (campaignStarter == null) return;
            _harmony = new Harmony(WarScoreRecoveryPatch.HarmonyId);
            WarScoreRecoveryPatch.Install(_harmony);
            WarPeaceOutcomePatch.Install(_harmony);
            campaignStarter.AddBehavior(new WarScoreTraceBehavior());
        }
        protected override void OnSubModuleUnloaded()
        {
            if (_harmony != null) { WarPeaceOutcomePatch.Uninstall(_harmony); WarScoreRecoveryPatch.Uninstall(_harmony); }
            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("war_score_recovery", "aoc")]
        public static string Status(List<string> arguments)
        {
            return WarScoreRecoveryPatch.IsInstalled
                ? "War-score recovery is installed for approved AOC Core 560F1B. Battle and fief awards recover only when Core leaves the score unchanged."
                : "War-score recovery is inactive: approved Core hash or private target contract did not match. Core behavior was not changed.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("war_score_audit", "aoc")]
        public static string Audit(List<string> arguments)
        {
            return WarScoreRecoveryPatch.GetAuditText();
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("war_score_trace", "aoc")]
        public static string Trace(List<string> arguments)
        {
            string command = arguments != null && arguments.Count > 0 ? arguments[0].ToLowerInvariant() : "report";
            if (command == "on") return WarScoreTrace.Start();
            if (command == "off") return WarScoreTrace.Stop();
            return WarScoreTrace.Report();
        }
    }
}
