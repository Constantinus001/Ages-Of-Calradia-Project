using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.CampaignSystems
{
    // Core companion entry point; no independent time accumulator. Its only
    // optional Harmony integrations are isolated NavalDLC guard/cash-out policies.
    public sealed class CoreSystemsSubModule : MBSubModuleBase
    {
        private Harmony _navalHarmony;
        public static CampaignSystem Current { get; private set; }
        public static bool ConfigurationValid { get; private set; }
        public static string ConfigurationError { get; private set; }
        public static string SettingsPath { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Configs", "AgesOfCalradia", "CampaignSystems.xml"); } }

        protected override void OnGameStart(Game game, IGameStarter starter)
        {
            base.OnGameStart(game, starter);
            Current=null;ConfigurationValid=false;ConfigurationError=null;
            NavalCashoutPatches.Stop();
            if (!(starter is CampaignGameStarter)) return;
            if (_navalHarmony == null) _navalHarmony = new Harmony(NavalDuplicateRewardGuard.HarmonyId);
            NavalDuplicateRewardGuard.Install(_navalHarmony);
            NavalCashoutPatches.Start(Path.Combine(Path.GetDirectoryName(SettingsPath), "NavalEconomy.xml"));
            var settings=ProcurementSettings.Defaults;
            try { settings=ProcurementSettings.Load(SettingsPath);ConfigurationValid=true; }
            catch (Exception ex)
            {
                // File/XML boundary: retain old saved obligations, block new orders.
                ConfigurationError=ex.Message;
                System.Diagnostics.Trace.WriteLine("AOC Campaign Systems configuration rejected: " + ex);
            }
            Current=new CampaignSystem(()=>CampaignTime.Now.ToDays,settings);
        }
        public override void OnGameEnd(Game game)
        { Current=null;ConfigurationValid=false;ConfigurationError=null;NavalCashoutPatches.Stop();NavalDuplicateRewardGuard.Reset();base.OnGameEnd(game); }
        protected override void OnSubModuleUnloaded()
        { Current=null;ConfigurationValid=false;ConfigurationError=null;NavalCashoutPatches.Stop();if(_navalHarmony!=null)NavalDuplicateRewardGuard.Uninstall(_navalHarmony);base.OnSubModuleUnloaded(); }

        protected override void OnApplicationTick(float dt)
        { base.OnApplicationTick(dt); NavalCashoutPatches.Tick(); }

        [CommandLineFunctionality.CommandLineArgumentFunction("systems_status", "aoc")]
        public static string Status(List<string> arguments)
        {
            if(Current==null) return "No Core Campaign Systems campaign loaded.";
            return "Core Campaign Systems v1; configuration=" + (ConfigurationValid?"valid":"REJECTED")
                + "; settings="+SettingsPath+"; revision="+Current.Supply.Revision+"; error="+ConfigurationError+Environment.NewLine
                + "Naval cash-out: " + NavalCashoutObservation.Status + Environment.NewLine
                + string.Join(Environment.NewLine,Current.Owners.Select(x=>x.Key+" -> "+x.Value));
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("quest_times", "aoc")]
        public static string QuestTimes(List<string> arguments)
        {
            if(Current==null || Campaign.Current==null) return "No campaign loaded.";
            try
            {
                return string.Join(Environment.NewLine,Campaign.Current.QuestManager.Quests.Select(q=> {
                    double? remaining=Current.Quests.RemainingDays(q.QuestDueTime==CampaignTime.Never ? (double?)null : q.QuestDueTime.ToDays);
                    return q.StringId+": "+(remaining.HasValue?remaining.Value.ToString("0.###",CultureInfo.InvariantCulture)+" campaign days":"no deadline");
                }));
            }
            catch(Exception ex)
            { System.Diagnostics.Trace.WriteLine("Quest time observation failed: "+ex);return "Quest observation failed; no deadlines changed: "+ex.Message; }
        }
        [CommandLineFunctionality.CommandLineArgumentFunction("logistics_status", "aoc")]
        public static string LogisticsStatus(List<string> arguments)
        {
            if(Current==null) return "No Core Campaign Systems campaign loaded.";
            if(arguments==null || arguments.Count!=1 || string.IsNullOrWhiteSpace(arguments[0])) return "Usage: aoc.logistics_status <party-id>";
            var snapshot=Current.Logistics.Read(arguments[0]);
            return "status="+snapshot.Status+" reserve="+(snapshot.Reserve.HasValue?snapshot.Reserve.Value.ToString(CultureInfo.InvariantCulture):"unknown")
                +" workshopTransport=False; party reserve and workshop cargo retain separate owners.";
        }
    }
}
