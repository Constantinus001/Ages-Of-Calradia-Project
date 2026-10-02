using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.PoliticalBorderEditor
{
    public sealed class PoliticalBorderEditorSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "AgesOfCalradia.PoliticalBorderEditor";
        private Harmony _harmony;
        private int _bindCountdown;
        private bool _bound;
        private bool _bindingFailed;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            PoliticalBorderEditorDiagnostics.Initialize();
            PoliticalBorderEditorState.Initialize();
            _harmony = new Harmony(HarmonyId);
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            if (_bound || _bindingFailed || _harmony == null) return;
            if (_bindCountdown-- > 0) return;
            _bindCountdown = 60;
            TryBindMapInput();
        }

        protected override void OnSubModuleUnloaded()
        {
            PoliticalBorderEditorState.SetActive(false);
            if (_harmony != null)
            {
                try { _harmony.UnpatchAll(HarmonyId); }
                catch (Exception exception)
                {
                    PoliticalBorderEditorDiagnostics.Error(
                        "Editor patch cleanup failed.", exception);
                }
            }
            _harmony = null;
            base.OnSubModuleUnloaded();
        }

        /// <summary>
        /// Native targets: SandBox.View.Map.MapScreen.HandleLeftMouseButtonClick
        /// and MapScreen.OnFrameTick. Purpose: consume exact CampaignVec2 clicks
        /// only while editor mode is active and render bounded editor input once
        /// per map frame. Compatibility risk: both targets are private/version
        /// sensitive. Failure behavior: no patches remain, normal map input is
        /// untouched, and diagnostics record the binding failure. Verification:
        /// Tests/Verify-PoliticalBorderEditor.ps1 asserts the target/signature,
        /// fail-closed return path, and separate-sidecar manifest registration.
        /// </summary>
        private void TryBindMapInput()
        {
            Type mapScreen = AccessTools.TypeByName("SandBox.View.Map.MapScreen");
            if (mapScreen == null) return;
            try
            {
                MethodInfo click = mapScreen.GetMethods(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .SingleOrDefault(method => method.Name == "HandleLeftMouseButtonClick"
                        && method.GetParameters().Any(parameter =>
                            parameter.ParameterType == typeof(CampaignVec2)));
                MethodInfo tick = mapScreen.GetMethod("OnFrameTick",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(float) }, null);
                if (click == null || tick == null)
                    throw new MissingMethodException(
                        "Bannerlord MapScreen editor targets were not found.");
                _harmony.Patch(click,
                    prefix: new HarmonyMethod(typeof(PoliticalBorderEditorPatches),
                        nameof(PoliticalBorderEditorPatches.BeforeMapClick)));
                _harmony.Patch(tick,
                    postfix: new HarmonyMethod(typeof(PoliticalBorderEditorPatches),
                        nameof(PoliticalBorderEditorPatches.AfterMapFrameTick)));
                TryBindSettlementVisibility();
                TryBindCloseZoomFill();
                _bound = true;
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor map input bound: click=" + click + "; tick=" + tick + ".");
            }
            catch (Exception exception)
            {
                _bindingFailed = true;
                try { _harmony.UnpatchAll(HarmonyId); } catch { }
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor map input binding failed; normal map input remains active.",
                    exception);
            }
        }

        /// <summary>
        /// Native target: SettlementNameplateVM.UpdateNameplateMT. Purpose:
        /// provide an editor-only town/castle/village visibility cycle at any
        /// drawing zoom. Compatibility risk: the native private visibility and
        /// settlement-kind fields are version-sensitive. Failure behavior:
        /// label filtering stays native while map editing remains available.
        /// Verification: Verify-PoliticalBorderEditor.ps1 checks the target,
        /// field contract, last-postfix ordering, and editor-active guard.
        /// </summary>
        private void TryBindSettlementVisibility()
        {
            try
            {
                Type type = AccessTools.TypeByName(
                    "SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM");
                MethodInfo target = type == null ? null : AccessTools.Method(
                    type, "UpdateNameplateMT");
                if (target == null)
                    throw new MissingMethodException(
                        "SettlementNameplateVM.UpdateNameplateMT was not found.");
                HarmonyMethod postfix = new HarmonyMethod(
                    typeof(PoliticalBorderEditorPatches),
                    nameof(PoliticalBorderEditorPatches.AfterSettlementNameplateUpdate));
                postfix.priority = Priority.Last;
                _harmony.Patch(target, postfix: postfix);
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor settlement visibility control bound: " + target + ".");
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor settlement visibility binding failed; native labels remain active.",
                    exception);
            }
        }

        /// <summary>
        /// Native target: CampaignKingdomBorderBehavior.SetPoliticalOverlayAlpha.
        /// Purpose: make the existing light-gray political fill very transparent
        /// while F10 planning is active and optionally retain it at close zoom.
        /// Compatibility risk: this internal sidecar
        /// boundary is version-sensitive. Failure behavior: the native altitude
        /// fade remains active while every other editor tool keeps working.
        /// Verification: editor source/runtime checks validate the float prefix
        /// without modifying the protected renderer assembly.
        /// </summary>
        private void TryBindCloseZoomFill()
        {
            try
            {
                Type type = AccessTools.TypeByName(
                    "TwelveMonthCalendar.CampaignKingdomBorderBehavior");
                MethodInfo target = type == null ? null : AccessTools.Method(
                    type, "SetPoliticalOverlayAlpha", new[] { typeof(float) });
                if (target == null)
                    throw new MissingMethodException(
                        "CampaignKingdomBorderBehavior.SetPoliticalOverlayAlpha(float) was not found.");
                _harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(PoliticalBorderEditorPatches),
                        nameof(PoliticalBorderEditorPatches.BeforePoliticalOverlayAlpha)));
                PoliticalBorderEditorDiagnostics.Info(
                    "Editor close-zoom political fill control bound: " + target + ".");
            }
            catch (Exception exception)
            {
                PoliticalBorderEditorDiagnostics.Error(
                    "Editor close-zoom fill binding failed; native altitude fading remains active.",
                    exception);
            }
        }
    }

    internal static class PoliticalBorderEditorPatches
    {
        internal static bool BeforeMapClick(object[] __args)
        {
            if (!PoliticalBorderEditorState.IsActive) return true;
            foreach (object argument in __args)
            {
                if (!(argument is CampaignVec2)) continue;
                return !PoliticalBorderEditorState.HandleMapClick((CampaignVec2)argument);
            }
            return true;
        }

        internal static void AfterMapFrameTick(object __instance, float __0)
        {
            PoliticalBorderEditorState.Tick(__instance, __0);
        }

        internal static void AfterSettlementNameplateUpdate(
            ref bool ____bindIsVisibleOnMap,
            bool ____isTown,
            bool ____isCastle,
            bool ____isVillage)
        {
            PoliticalBorderEditorState.ApplySettlementVisibility(
                ref ____bindIsVisibleOnMap, ____isTown, ____isCastle, ____isVillage);
        }

        internal static void BeforePoliticalOverlayAlpha(ref float alpha)
        {
            PoliticalBorderEditorState.ApplyCloseZoomFill(ref alpha);
        }
    }
}
