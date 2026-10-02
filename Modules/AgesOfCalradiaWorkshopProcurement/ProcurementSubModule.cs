using System;
using AgesOfCalradia.CampaignSystems;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.WorkshopProcurement
{
    public sealed class ProcurementSubModule : MBSubModuleBase
    {
        internal const string Owner = "AgesOfCalradia.WorkshopProcurement.v1";
        private Harmony _harmony;
        private static ProcurementSubModule _owner;
        private CampaignGameStarter _registeredStarter;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            if(_owner!=null){ProcurementLog.Write("DUPLICATE_MODULE_IGNORED","Procurement already registered; disable the old candidate module.");return;}
            _owner=this;
            try
            {
                ProcurementPatches.Validate();
                _harmony = new Harmony(Owner);
                _harmony.PatchAll(typeof(ProcurementSubModule).Assembly);
            }
            catch (Exception ex)
            {
                if (_harmony != null) _harmony.UnpatchAll(Owner);
                _harmony = null;
                ProcurementLog.Write("PATCH_REJECTED", ex.ToString());
            }
        }
        protected override void OnGameStart(Game game, IGameStarter starter)
        {
            base.OnGameStart(game, starter);
            if(!ReferenceEquals(_owner,this))return;
            var campaign = starter as CampaignGameStarter;
            // Register even when hooks failed, so an existing ledger is preserved.
            if (campaign != null)
            {
                if(ReferenceEquals(_registeredStarter,campaign))return;
                _registeredStarter=campaign;
                ProcurementPolicy.Settings = CoreSystemsSubModule.Current == null ? ProcurementSettings.Defaults : CoreSystemsSubModule.Current.Supply;
                if (CoreSystemsSubModule.Current == null) ProcurementLog.Write("CORE_UNAVAILABLE", "Core Campaign Systems missing; ledger preserved, actions disabled.");
                if (!CoreSystemsSubModule.ConfigurationValid) ProcurementLog.Write("CONFIG_REJECTED", CoreSystemsSubModule.ConfigurationError ?? "Core unavailable");
                ProcurementLog.Write("CORE_SETTINGS", "valid="+CoreSystemsSubModule.ConfigurationValid+" revision="+ProcurementPolicy.Settings.Revision);
                campaign.AddBehavior(new ProcurementBehavior(_harmony != null && CoreSystemsSubModule.Current != null,
                    CoreSystemsSubModule.ConfigurationValid));
            }
        }
        protected override void OnSubModuleUnloaded()
        {
            if(!ReferenceEquals(_owner,this)){base.OnSubModuleUnloaded();return;}
            if (_harmony != null) _harmony.UnpatchAll(Owner);
            ProcurementPatches.Active = null;
            ProcurementPolicy.Settings = ProcurementSettings.Defaults;
            _owner=null;_registeredStarter=null;
            base.OnSubModuleUnloaded();
        }
        public override void OnGameEnd(Game game)
        {
            if(ReferenceEquals(_owner,this))_registeredStarter=null;
            base.OnGameEnd(game);
        }
    }
}
