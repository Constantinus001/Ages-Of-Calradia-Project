using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using HarmonyLib;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

namespace AgesOfCalradia.PoliticalFillAlphaFix
{
    public sealed class PoliticalFillAlphaFixSubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            FillAlphaPatch.Initialize();
        }

        protected override void OnSubModuleUnloaded()
        {
            FillAlphaPatch.Stop();
            base.OnSubModuleUnloaded();
        }
    }

    // Approved AOC 560 renderer on Bannerlord v1.4.8 only. Augments the forceAlpha
    // input of ApplyPoliticalEntityVisibility; preserves its native visibility,
    // entity iteration and SetAlpha calls. No material, geometry or save writes.
    internal static class FillAlphaPatch
    {
        private const string Owner = "aoc.political-fill-applied-alpha.v1";
        private const string ApprovedHash = "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";
        private static readonly ConditionalWeakTable<object, FillAlphaPublicationTracker> States =
            new ConditionalWeakTable<object, FillAlphaPublicationTracker>();
        private static Harmony _harmony;
        private static FieldInfo _alpha, _ready, _entities;
        private static bool _enabled;

        private sealed class Invocation
        {
            internal FillAlphaPublicationTracker Tracker;
            internal float Requested;
            internal bool VisibilityChanged, HasEligibleEntity;
        }

        internal static void Initialize()
        {
            try
            {
                Assembly core = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "AgesOfCalradia");
                string hash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(core.Location))
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (hash != ApprovedHash) throw new InvalidOperationException("Unsupported Core hash; alpha correction disabled.");
                Type behavior = core.GetType("TwelveMonthCalendar.CampaignKingdomBorderBehavior", true);
                MethodInfo target = AccessTools.DeclaredMethod(behavior, "ApplyPoliticalEntityVisibility", new[] { typeof(bool) });
                if (target == null || target.ReturnType != typeof(void) || target.IsStatic)
                    throw new MissingMethodException("Expected approved ApplyPoliticalEntityVisibility(bool) instance target.");
                _alpha = RequiredField(behavior, "_politicalLayerAlpha", typeof(float));
                _ready = RequiredField(behavior, "_politicalEntitiesReady", typeof(bool));
                _entities = RequiredField(behavior, "_territoryFillEntities", typeof(List<GameEntity>));
                var owners = Harmony.GetPatchInfo(target);
                Log("Initializing applied-alpha correction; Core=" + hash + ";otherOwners=" +
                    (owners == null ? "none" : string.Join("|", owners.Owners)));
                _harmony = new Harmony(Owner);
                _harmony.Patch(target, new HarmonyMethod(typeof(FillAlphaPatch), nameof(BeforeVisibility)),
                    new HarmonyMethod(typeof(FillAlphaPatch), nameof(AfterVisibility)));
                _enabled = true;
                Log("Applied-alpha correction active; threshold=0.002; exact endpoints preserved; renderer geometry unchanged.");
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static FieldInfo RequiredField(Type type, string name, Type fieldType)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null || field.FieldType != fieldType) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void BeforeVisibility(object __instance, ref bool forceAlpha, out Invocation __state)
        {
            __state = null;
            if (!_enabled) return;
            try
            {
                float requested = (float)_alpha.GetValue(__instance);
                if (float.IsNaN(requested) || float.IsInfinity(requested)) return;
                bool visible = requested > 0.001f;
                // Hidden fill has no native alpha assignment. Preserve its normal
                // visibility transition without forcing an entity loop every tick.
                if (!visible) return;
                bool visibilityChanged = visible != (bool)_ready.GetValue(__instance);
                var entities = (List<GameEntity>)_entities.GetValue(__instance);
                bool hasEligibleEntity = entities != null && entities.Any(entity => entity != null);
                var tracker = States.GetValue(__instance, ignored => new FillAlphaPublicationTracker());
                __state = new Invocation { Tracker = tracker, Requested = requested,
                    VisibilityChanged = visibilityChanged, HasEligibleEntity = hasEligibleEntity };
                // Set only after all reflection reads have succeeded. Keep explicit
                // native force/readiness transitions and cumulative publication lag.
                forceAlpha = tracker.NeedsPublication(requested, forceAlpha, visibilityChanged);
            }
            catch (Exception ex) { __state = null; Disable(ex); }
        }

        private static void AfterVisibility(bool forceAlpha, bool __runOriginal, Invocation __state)
        {
            if (!_enabled || !__runOriginal || __state == null) return;
            // Postfix is not reached if the original throws. Zero hides the fill;
            // the approved original does not assign native alpha in that branch.
            if (__state.Requested > 0.001f && __state.HasEligibleEntity &&
                (forceAlpha || __state.VisibilityChanged))
                __state.Tracker.CommitPublished(__state.Requested);
        }

        private static void Disable(Exception ex)
        {
            _enabled = false;
            Log("DISABLED; approved original remains available; " + ex);
        }

        private static void Log(string message)
        {
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(typeof(FillAlphaPatch).Assembly.Location), "PoliticalFillAlphaFix");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "status.log"), DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (Exception ex) { Trace.TraceError("Political fill alpha log failed: " + ex + "; " + message); }
        }

        internal static void Stop()
        {
            _enabled = false;
            try { _harmony?.UnpatchAll(Owner); }
            catch (Exception ex) { Log("Unpatch failed during module unload: " + ex); }
        }
    }
}
