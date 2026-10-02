using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using HarmonyLib;
using TaleWorlds.GauntletUI.PrefabSystem;
using TaleWorlds.MountAndBlade;

namespace AgesOfCalradia.WorldEventsShellRepair
{
    /// <summary>
    /// Restores the approved World Events direct-PNG providers without changing
    /// the protected WorldCalendar prefab or protected primary assembly.
    /// </summary>
    public sealed class WorldEventsShellRepairSubModule : MBSubModuleBase
    {
        private const string HarmonyId = "AgesOfCalradia.WorldEventsShellRepair";
        private const string ApprovedRendererSha256 =
            "560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E";

        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            WorldEventsShellDiagnostics.Initialize();
            WorldEventsShellDiagnostics.Info(
                "World Events shell repair v1.0.0 entering OnSubModuleLoad.");

            try
            {
                Assembly rendererAssembly = RequireApprovedRenderer();
                WorldEventsShellPrefabPatch.VerifyProviders(rendererAssembly);

                MethodInfo target = AccessTools.Method(
                    typeof(WidgetTemplate),
                    "LoadFrom",
                    new[]
                    {
                        typeof(PrefabExtensionContext),
                        typeof(WidgetAttributeContext),
                        typeof(XmlNode)
                    });
                if (target == null)
                {
                    throw new MissingMethodException(
                        typeof(WidgetTemplate).FullName,
                        "LoadFrom(PrefabExtensionContext, WidgetAttributeContext, XmlNode)");
                }

                // Native target: Bannerlord v1.4.8 WidgetTemplate.LoadFrom.
                // Purpose: replace only five exact WorldCalendar shell sprite
                // nodes with the protected renderer's direct PNG providers.
                // Compatibility risk: the prefab loader signature may change.
                // Failure behavior: activation is abandoned and the original
                // XML/sprite path remains untouched. Verification is covered by
                // Tests/Verify-WorldEventsShellRepair.ps1.
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(
                        typeof(WorldEventsShellPrefabPatch),
                        nameof(WorldEventsShellPrefabPatch.BeforeWidgetTemplateLoad)));
                WorldEventsChronicleTextRepair.Install(_harmony, rendererAssembly);

                WorldEventsShellDiagnostics.Info(
                    "Repair active: five exact World Events shell widgets will use direct CustomUI PNG providers.");
            }
            catch (Exception exception)
            {
                // Reflection, Harmony, and the protected provider assembly are
                // version-sensitive mod boundaries. Fail open to the existing UI.
                WorldEventsShellDiagnostics.Error(
                    "Repair activation failed; the protected prefab and its original sprite path remain active.",
                    exception);
                DisablePatch();
            }
        }

        protected override void OnSubModuleUnloaded()
        {
            DisablePatch();
            base.OnSubModuleUnloaded();
        }

        private static Assembly RequireApprovedRenderer()
        {
            Type providerType = AccessTools.TypeByName(
                "TwelveMonthCalendar.WorldEventsFullBorderShellTextureProvider");
            if (providerType == null)
            {
                throw new TypeLoadException(
                    "The protected World Events shell provider is not loaded.");
            }

            Assembly assembly = providerType.Assembly;
            string actualHash = ComputeSha256(assembly.Location);
            if (!string.Equals(
                actualHash,
                ApprovedRendererSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Protected renderer hash mismatch. Expected "
                    + ApprovedRendererSha256 + " but found " + actualHash + ".");
            }

            return assembly;
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("X2"));
                }

                return builder.ToString();
            }
        }

        private void DisablePatch()
        {
            if (_harmony == null)
            {
                return;
            }

            _harmony.UnpatchAll(HarmonyId);
            _harmony = null;
        }
    }

    internal static class WorldEventsShellPrefabPatch
    {
        private sealed class ShellWidgetReplacement
        {
            internal ShellWidgetReplacement(
                string id,
                string expectedSprite,
                string providerTypeName)
            {
                Id = id;
                ExpectedSprite = expectedSprite;
                ProviderTypeName = providerTypeName;
            }

            internal string Id { get; private set; }
            internal string ExpectedSprite { get; private set; }
            internal string ProviderTypeName { get; private set; }
        }

        private static readonly Dictionary<string, ShellWidgetReplacement> Replacements =
            new Dictionary<string, ShellWidgetReplacement>(StringComparer.Ordinal)
            {
                {
                    "WorldEventsFullBorderShell",
                    new ShellWidgetReplacement(
                        "WorldEventsFullBorderShell",
                        "aoc_world_events_shell_v8",
                        "WorldEventsFullBorderShellTextureProvider")
                },
                {
                    "WorldEventsSelectedCalendarShell",
                    new ShellWidgetReplacement(
                        "WorldEventsSelectedCalendarShell",
                        "aoc_world_events_shell_calendar_selected_v6",
                        "WorldEventsGoldFrameCalendarTextureProvider")
                },
                {
                    "WorldEventsSelectedStoryShell",
                    new ShellWidgetReplacement(
                        "WorldEventsSelectedStoryShell",
                        "aoc_world_events_shell_story_selected_v6",
                        "WorldEventsGoldFrameStoryTextureProvider")
                },
                {
                    "WorldEventsSelectedRealmShell",
                    new ShellWidgetReplacement(
                        "WorldEventsSelectedRealmShell",
                        "aoc_world_events_shell_realm_selected_v6",
                        "WorldEventsGoldFrameDiplomacyTextureProvider")
                },
                {
                    "WorldEventsSelectedStrategicShell",
                    new ShellWidgetReplacement(
                        "WorldEventsSelectedStrategicShell",
                        "aoc_world_events_shell_strategic_selected_v6",
                        "WorldEventsGoldFrameMapTextureProvider")
                }
            };

        private static readonly object LogSync = new object();
        private static readonly HashSet<string> LoggedReplacements =
            new HashSet<string>(StringComparer.Ordinal);
        private static bool _failureLogged;

        internal static void VerifyProviders(Assembly rendererAssembly)
        {
            foreach (ShellWidgetReplacement replacement in Replacements.Values)
            {
                string fullName = "TwelveMonthCalendar." + replacement.ProviderTypeName;
                if (rendererAssembly.GetType(fullName, false) == null)
                {
                    throw new TypeLoadException(
                        "Required protected texture provider is missing: " + fullName);
                }
            }
        }

        internal static void BeforeWidgetTemplateLoad(ref XmlNode __2)
        {
            XmlNode originalNode = __2;
            try
            {
                if (originalNode == null
                    || originalNode.Attributes == null)
                {
                    return;
                }

                XmlAttribute idAttribute = originalNode.Attributes["Id"];
                // The open screen captures map input, so its transparent close
                // button must exactly overlay the visible 80% MapBar W button.
                if (originalNode.Name == "ButtonWidget" && idAttribute?.Value == "WorldCalendarOpenOverlayToggle"
                    && originalNode.Attributes["Command.Click"]?.Value == "ExecuteClose"
                    && originalNode.Attributes["SuggestedWidth"]?.Value == "34"
                    && originalNode.Attributes["SuggestedHeight"]?.Value == "31")
                {
                    XmlElement toggle = (XmlElement)originalNode.CloneNode(true);
                    toggle.SetAttribute("SuggestedWidth", "37.6");
                    toggle.SetAttribute("SuggestedHeight", "34.4");
                    toggle.SetAttribute("PositionXOffset", "248.8");
                    toggle.SetAttribute("MarginBottom", "11.2");
                    __2 = toggle;
                    return;
                }
                if (originalNode.Name != "Widget") return;
                // Same verified prefab-loader target: remove the legacy 23-unit
                // cabinet shift for the approved 80% MapBar. Its dial center is
                // 0.8 units right of screen center. Clone only the exact cabinet;
                // the protected on-disk prefab and all child commands stay intact.
                if (idAttribute != null && idAttribute.Value == "WorldEventsFrame"
                    && originalNode.Attributes["SuggestedWidth"]?.Value == "1220"
                    && originalNode.Attributes["SuggestedHeight"]?.Value == "871"
                    && originalNode.SelectSingleNode("Children/*[@Id='WorldEventsFullBorderShell']") != null)
                {
                    XmlElement alignedFrame = (XmlElement)originalNode.CloneNode(true);
                    alignedFrame.SetAttribute("PositionXOffset", "0.8");
                    // Bottom-anchor to the 80% MapBar crown: center height 77.6,
                    // bottom inset 1.6, crown rise 48, plus a 4-unit clear gap.
                    alignedFrame.SetAttribute("VerticalAlignment", "Bottom");
                    alignedFrame.SetAttribute("PositionYOffset", "-131.2");
                    ImproveStoryReadability(alignedFrame);
                    WorldEventsReadability.Apply(alignedFrame);
                    __2 = alignedFrame;
                    return;
                }
                ShellWidgetReplacement replacement;
                if (idAttribute == null
                    || !Replacements.TryGetValue(idAttribute.Value, out replacement))
                {
                    return;
                }

                XmlAttribute spriteAttribute = originalNode.Attributes["Sprite"];
                if (spriteAttribute == null
                    || !string.Equals(
                        spriteAttribute.Value,
                        replacement.ExpectedSprite,
                        StringComparison.Ordinal))
                {
                    return;
                }

                XmlDocument document = originalNode.OwnerDocument;
                if (document == null)
                {
                    return;
                }

                XmlElement textureWidget = document.CreateElement("TextureWidget");
                foreach (XmlAttribute attribute in originalNode.Attributes)
                {
                    if (!string.Equals(attribute.Name, "Sprite", StringComparison.Ordinal))
                    {
                        textureWidget.SetAttribute(attribute.Name, attribute.Value);
                    }
                }

                textureWidget.SetAttribute(
                    "TextureProviderName",
                    replacement.ProviderTypeName);
                foreach (XmlNode child in originalNode.ChildNodes)
                {
                    textureWidget.AppendChild(child.CloneNode(true));
                }

                __2 = textureWidget;
                LogReplacementOnce(replacement);
            }
            catch (Exception exception)
            {
                // XML transformation is a Gauntlet loading boundary. Restore the
                // untouched node so a repair failure cannot suppress the prefab.
                __2 = originalNode;
                lock (LogSync)
                {
                    if (_failureLogged)
                    {
                        return;
                    }

                    _failureLogged = true;
                    WorldEventsShellDiagnostics.Error(
                        "In-memory shell widget replacement failed; the original widget remains active.",
                        exception);
                }
            }
        }

        internal static void ImproveStoryReadability(XmlElement frame)
        {
            // Only the screenshot-identified scrollable story bodies and subtitle.
            // CoverChildren heights and existing scrollers retain all long text.
            foreach (string id in new[] { "CharacterStoryBody", "CharacterMilestoneBody", "PersonalChronicleSubtitle" })
            {
                XmlElement text = frame.SelectSingleNode(".//TextWidget[@Id='" + id + "']") as XmlElement;
                if (text == null) continue;
                text.SetAttribute("Brush.FontSize", id == "PersonalChronicleSubtitle" ? "16" : "19");
            }
        }

        private static void LogReplacementOnce(ShellWidgetReplacement replacement)
        {
            lock (LogSync)
            {
                if (!LoggedReplacements.Add(replacement.Id))
                {
                    return;
                }

                WorldEventsShellDiagnostics.Info(
                    "Replaced " + replacement.Id + " sprite "
                    + replacement.ExpectedSprite + " with provider "
                    + replacement.ProviderTypeName + ".");
            }
        }
    }

    internal static class WorldEventsShellDiagnostics
    {
        private static readonly object SyncRoot = new object();
        private static string _logPath;

        internal static void Initialize()
        {
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(
                    typeof(WorldEventsShellRepairSubModule).Assembly.Location);
                DirectoryInfo binaryDirectory = string.IsNullOrWhiteSpace(assemblyDirectory)
                    ? null
                    : Directory.GetParent(assemblyDirectory);
                DirectoryInfo moduleDirectory = binaryDirectory == null
                    ? null
                    : binaryDirectory.Parent;
                string root = moduleDirectory == null
                    ? assemblyDirectory
                    : moduleDirectory.FullName;
                string logDirectory = Path.Combine(root, "Logs");
                Directory.CreateDirectory(logDirectory);
                _logPath = Path.Combine(logDirectory, "WorldEventsShellRepair.log");
                File.AppendAllText(_logPath, string.Empty, Encoding.UTF8);
            }
            catch
            {
                // Diagnostics are best-effort and must not prevent module load.
                _logPath = null;
            }
        }

        internal static void Info(string message)
        {
            Write("INFO  " + message);
        }

        internal static void Error(string message, Exception exception)
        {
            Write("ERROR " + message + Environment.NewLine + exception);
        }

        private static void Write(string message)
        {
            if (string.IsNullOrWhiteSpace(_logPath))
            {
                return;
            }

            try
            {
                lock (SyncRoot)
                {
                    File.AppendAllText(
                        _logPath,
                        DateTime.Now.ToString("O") + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch
            {
                // Logging failure must not affect the World Events UI.
            }
        }
    }
}
