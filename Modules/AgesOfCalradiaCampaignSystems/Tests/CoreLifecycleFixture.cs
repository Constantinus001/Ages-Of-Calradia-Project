using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using AgesOfCalradia.CampaignSystems;
using TaleWorlds.CampaignSystem;

public static class CoreLifecycleFixture
{
    static string path;
    static int checks;
    static bool SettingsPath(ref string __result) { __result=path;return false; }
    static void Check(bool condition,string label) { checks++;if(!condition)throw new Exception(label); }
    public static string Run()
    {
        checks=0;
        // Private fixture directory only; never reads or changes the user's config/save.
        string directory=Path.Combine(Path.GetTempPath(),"AocCoreLifecycle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);path=Path.Combine(directory,"CampaignSystems.xml");
        var harmony=new Harmony("aoc.core.lifecycle.fixture");
        var module=(CoreSystemsSubModule)FormatterServices.GetUninitializedObject(typeof(CoreSystemsSubModule));
        var start=AccessTools.Method(typeof(CoreSystemsSubModule),"OnGameStart");
        var unload=AccessTools.Method(typeof(CoreSystemsSubModule),"OnSubModuleUnloaded");
        var starter=(CampaignGameStarter)FormatterServices.GetUninitializedObject(typeof(CampaignGameStarter));
        try
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(CoreSystemsSubModule),"SettingsPath"),new HarmonyMethod(typeof(CoreLifecycleFixture),"SettingsPath"));
            start.Invoke(module,new object[]{null,starter});
            Check(CoreSystemsSubModule.ConfigurationValid && CoreSystemsSubModule.Current.Supply.BatchesPerOrder==3,"missing config defaults");
            string initial=CoreSystemsSubModule.Current.Supply.Revision;
            File.WriteAllText(path,"<CampaignSystems schema='1'><Procurement BatchesPerOrder='5'/></CampaignSystems>");
            Check(CoreSystemsSubModule.Current.Supply.BatchesPerOrder==3,"no accidental mid-campaign reload");
            module.OnGameEnd(null);
            Check(CoreSystemsSubModule.Current==null && !CoreSystemsSubModule.ConfigurationValid,"game end clears singleton");
            start.Invoke(module,new object[]{null,starter});
            Check(CoreSystemsSubModule.ConfigurationValid && CoreSystemsSubModule.Current.Supply.BatchesPerOrder==5
                && CoreSystemsSubModule.Current.Supply.Revision!=initial,"next campaign applies edited policy");
            Check(CoreSystemsSubModule.Status(null).Contains("revision="),"status records effective policy");
            Check(CoreSystemsSubModule.LogisticsStatus(new List<string>{" "}).StartsWith("Usage:"),"blank party ID safe");
            File.WriteAllText(path,"<invalid>");start.Invoke(module,new object[]{null,starter});
            Check(!CoreSystemsSubModule.ConfigurationValid && CoreSystemsSubModule.Current!=null
                && !string.IsNullOrEmpty(CoreSystemsSubModule.ConfigurationError),"malformed config retains services but marks new actions unsafe");
            File.WriteAllText(path,"<CampaignSystems schema='1'><Procurement /></CampaignSystems>");
            using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            {
                start.Invoke(module,new object[]{null,starter});
                Check(!CoreSystemsSubModule.ConfigurationValid,"unreadable file not silently accepted as defaults");
            }
            start.Invoke(module,new object[]{null,starter});
            Check(CoreSystemsSubModule.ConfigurationValid && CoreSystemsSubModule.ConfigurationError==null,"valid restart clears prior rejection");
            unload.Invoke(module,null);Check(CoreSystemsSubModule.Current==null,"unload resets singleton");
            start.Invoke(module,new object[]{null,null});Check(CoreSystemsSubModule.Current==null,"noncampaign mode no services");
            return "PASS: "+checks+" native Core lifecycle/configuration assertions; temporary fixture config only.";
        }
        finally
        {
            module.OnGameEnd(null);harmony.UnpatchAll("aoc.core.lifecycle.fixture");
            if(File.Exists(path))File.Delete(path);
            Directory.Delete(directory); // Nonrecursive, exact newly-created fixture directory.
        }
    }
}
