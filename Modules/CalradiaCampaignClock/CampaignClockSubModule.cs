using System;
using System.Reflection;
using Bannerlord.UIExtenderEx;
using TaleWorlds.MountAndBlade;

namespace CalradiaCampaignClock
{
    /// <summary>
    /// Owns only the standalone campaign-map clock UI extension. It does not
    /// alter campaign time, pacing, dates, weather, saves, or simulation rules.
    /// </summary>
    public sealed class CampaignClockSubModule : MBSubModuleBase
    {
        internal const string ModuleId = "CalradiaCampaignClock";

        private UIExtender _uiExtender;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            CampaignClockDiagnostics.Initialize();
            CampaignClockSettings.Load();

            if (ClockOwnerCompatibility.HasAgesOfCalradiaClock())
            {
                CampaignClockDiagnostics.Info(
                    "Ages of Calradia already owns the campaign-map clock; the standalone clock was disabled to prevent duplicate UI and refresh owners.");
                return;
            }

            try
            {
                _uiExtender = UIExtender.Create(ModuleId);
                _uiExtender.Register(Assembly.GetExecutingAssembly());
                _uiExtender.Enable();
                CampaignClockDiagnostics.Info(
                    "Standalone clock enabled through an additive MapBar prefab extension and MapTimeControlVM mixin.");
            }
            catch (Exception exception)
            {
                CampaignClockDiagnostics.Error(
                    "Standalone clock registration failed; the native campaign map UI remains active.",
                    exception);
                DisableUiExtension();
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            DisableUiExtension();
            base.OnSubModuleUnloaded();
        }

        private void DisableUiExtension()
        {
            if (_uiExtender == null)
            {
                return;
            }

            try
            {
                _uiExtender.Deregister();
            }
            catch (Exception exception)
            {
                CampaignClockDiagnostics.Error(
                    "Standalone clock UI deregistration failed during module shutdown.",
                    exception);
            }
            finally
            {
                _uiExtender = null;
            }
        }
    }
}
