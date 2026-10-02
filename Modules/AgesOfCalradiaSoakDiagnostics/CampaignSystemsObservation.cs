using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Optional read-only reflection boundary. Runs only inside the bounded capture
    // on the campaign thread. Failures invalidate evidence, never alter deadlines.
    internal static class CampaignSystemsObservation
    {
        internal static string LastStatus { get; private set; }
        internal static void Snapshot()
        {
            if (!SupplyCapture.Active) return;
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(
                    a => a.GetName().Name == "AgesOfCalradia.CampaignSystems");
                var type = assembly == null ? null : assembly.GetType(
                    "AgesOfCalradia.CampaignSystems.CoreSystemsSubModule", true);
                var campaign = Campaign.Current;
                if (campaign == null) throw new InvalidOperationException("No campaign for framework observation");
                WriteSnapshot(type, CampaignTime.Now.ToDays, campaign.QuestManager.Quests.Select(q =>
                    new KeyValuePair<string, double?>(q.StringId,
                        q.QuestDueTime == CampaignTime.Never ? (double?)null : q.QuestDueTime.ToDays)));
            }
            catch (Exception ex) { SupplyCapture.Fail("campaign systems observation", ex); }
        }

        private static object Read(Type type, object instance, string name)
        {
            var property = type.GetProperty(name, BindingFlags.Public |
                (instance == null ? BindingFlags.Static : BindingFlags.Instance));
            if (property == null || property.GetGetMethod() == null || property.GetIndexParameters().Length != 0)
                throw new MissingMemberException(type.FullName, name);
            return property.GetValue(instance, null);
        }

        // Deterministic writer boundary shared with native tests; null due = Never.
        internal static void WriteSnapshot(Type core, double day, IEnumerable<KeyValuePair<string, double?>> quests)
        {
            if (!EconomyLedger.Finite(day) || day < 0) throw new ArgumentOutOfRangeException("day");
            string status = "missing";
            string detail = "coreMvid=missing; policyRevision=unavailable; read_only";
            if (core != null)
            {
                var current = Read(core, null, "Current");
                status = current == null ? "no_campaign" : "active";
                detail = "coreMvid=" + core.Module.ModuleVersionId + "; read_only";
                if (current != null)
                {
                    bool valid = (bool)Read(core, null, "ConfigurationValid");
                    var supply = Read(current.GetType(), current, "Supply");
                    string revision = (string)Read(supply.GetType(), supply, "Revision");
                    if (revision == null || revision.Length != 64 || revision.Any(c => !Uri.IsHexDigit(c)))
                        throw new InvalidOperationException("Invalid observed policy revision");
                    status = valid ? "valid" : "configuration_rejected";
                    detail += "; policyRevision=" + revision;
                }
            }
            SupplyCapture.Write("CORE_SYSTEMS", 0, 0, "framework", status, day, day, detail);
            LastStatus = status;
            int count = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var quest in quests)
            {
                if (string.IsNullOrWhiteSpace(quest.Key) || !ids.Add(quest.Key))
                    throw new InvalidOperationException("Missing or duplicate quest identity");
                double due = quest.Value ?? day;
                if (!EconomyLedger.Finite(due) || due < 0) throw new InvalidOperationException("Invalid quest deadline");
                SupplyCapture.Write("QUEST_DEADLINE", 0, 0, quest.Key,
                    quest.Value.HasValue ? "finite" : "never", day, due,
                    quest.Value.HasValue ? "remainingDays=" + SupplyCapture.N(Math.Max(0, due - day))
                        + "; authoritative_saved_deadline; no_rescaling" : "no_deadline; numeric_after_is_sample_day");
                count++;
            }
            SupplyCapture.Write("QUEST_COVERAGE", 0, 0, "campaign", count == 0 ? "none_active" : "observed",
                count, count, "read_only; zero_quests_is_not_timing_coverage");
        }
    }
}
