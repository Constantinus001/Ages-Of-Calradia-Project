using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Observes native 1.4.8 WarDeclared/MakePeace events synchronously. No patches,
    // actions, model overrides or save types. Event handlers may be nested or
    // reordered by other mods; capture observed post-event state, not a verdict.
    internal sealed class PeaceEventDiagnostics
    {
        private readonly PeaceEventHistory _history = new PeaceEventHistory();
        private string _sessionId;
        private static readonly FieldInfo StancesField = typeof(FactionManager).GetField(
            "_stances", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ReadStance = StancesField == null ? null : StancesField.FieldType.GetMethod(
            "GetStance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(IFaction), typeof(IFaction) }, null);

        internal void Reset()
        {
            _history.Reset();
            _sessionId = Guid.NewGuid().ToString("N");
            try
            {
                SoakLog.Write("PEACE_DIAGNOSTICS_SESSION", "id=" + _sessionId
                    + "; schema=1; history=session-only; rapidThreshold=1_wall_second_and_1_campaign_hour"
                    + "; diplomacyModel=" + Campaign.Current.Models.DiplomacyModel.GetType().AssemblyQualifiedName);
            }
            catch (Exception exception) { Failure(exception); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void OnWar(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail)
        {
            if (!CalendarSoakBehavior.IsArmed) return;
            try
            {
                double day = CampaignTime.Now.ToDays;
                long sequence = _history.RecordWar(Id(first), Id(second), day, Seconds(), detail.ToString());
                SoakLog.Write("WAR_DIAGNOSTIC", "session=" + _sessionId + "; warSequence=" + sequence
                    + "; day=" + Number(day) + "; reason=" + detail + Factions(first, second)
                    + StanceContext(first, second) + "; " + TreatyStateDiagnostics.Read() + "; callerStack=" + CallerStack());
            }
            catch (Exception exception) { Failure(exception); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void OnPeace(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail)
        {
            if (!CalendarSoakBehavior.IsArmed) return;
            try
            {
                double day = CampaignTime.Now.ToDays;
                PeaceCorrelation match = _history.RecordPeace(Id(first), Id(second), day, Seconds());
                string record = "session=" + _sessionId + "; day=" + Number(day) + "; reason=" + detail
                    + Factions(first, second) + "; warSequence=" + match.WarSequence
                    + "; warReason=" + (match.WarReason ?? "unobserved")
                    + "; timingKnown=" + match.TimingKnown
                    + "; elapsedCampaignDays=" + (match.TimingKnown ? Number(match.ElapsedDays) : "unavailable")
                    + "; elapsedWallMilliseconds=" + (match.TimingKnown ? Number(match.ElapsedSeconds * 1000d) : "unavailable")
                    + "; duplicatePeace=" + match.DuplicatePeace + "; rapidReversal=" + match.RapidReversal;
                string caller = CallerStack();
                bool openingPeace = caller.Contains("AgesOfCalradiaReligions.OpeningPeaceBehavior");
                record += "; classification=" + (openingPeace ? "opening_peace_handler_observed" : "unclassified")
                    + "; classificationIsCallerEvidence=True; " + TreatyStateDiagnostics.Read();
                SoakLog.Write("PEACE_DIAGNOSTIC", record + StanceContext(first, second) + "; callerStack=" + caller);
                if (match.RapidReversal) SoakLog.Write("PEACE_RAPID_REVERSAL", record + "; reviewRequired=True; bugConfirmed=False");
            }
            catch (Exception exception) { Failure(exception); }
        }

        private static string Factions(IFaction first, IFaction second)
        {
            return Faction("first", first) + Faction("second", second);
        }
        private static string Faction(string label, IFaction faction)
        {
            if (faction == null) return "; " + label + "Id=unavailable";
            return "; " + label + "Id=" + Id(faction) + "; " + label + "Name=" + faction.Name
                + "; " + label + "Leader=" + (faction.Leader == null ? "unavailable" : faction.Leader.StringId)
                + "; " + label + "Kingdom=" + faction.IsKingdomFaction
                + "; " + label + "Strength=" + Number(faction.CurrentTotalStrength);
        }

        private static string StanceContext(IFaction first, IFaction second)
        {
            try
            {
                if (first == null || second == null || ReadStance == null)
                    return "; stance=unavailable; stanceRead=unsupported_or_missing_faction";
                // GetStanceWith creates missing stances. Read the native backing
                // collection instead; absent records stay absent.
                object collection = StancesField.GetValue(Campaign.Current.FactionManager);
                StanceLink stance = (StanceLink)ReadStance.Invoke(collection, new object[] { first, second });
                if (stance == null) return "; stance=absent";
                return "; stance=observed_after_event; isAtWar=" + stance.IsAtWar
                    + "; nativeWarStartDay=" + Number(stance.WarStartDate.ToDays)
                    + "; nativePeaceDay=" + Number(stance.PeaceDeclarationDate.ToDays)
                    + "; firstDailyTributeToPay=" + stance.GetDailyTributeToPay(first)
                    + "; secondDailyTributeToPay=" + stance.GetDailyTributeToPay(second)
                    + "; remainingTributePayments=" + stance.GetRemainingTributePaymentCount()
                    + "; firstCasualties=" + stance.GetCasualties(first)
                    + "; secondCasualties=" + stance.GetCasualties(second);
            }
            catch (Exception exception)
            {
                Failure(exception);
                return "; stance=unavailable; stanceRead=failed";
            }
        }

        private static string CallerStack()
        {
            // No local file paths/arguments. Inlining may omit native/mod frames.
            return string.Join(" <- ", (new StackTrace(false).GetFrames() ?? new StackFrame[0]).Take(24).Select(frame =>
            {
                MethodBase method = frame.GetMethod();
                if (method == null) return "unknown";
                Type owner = method.DeclaringType;
                return (owner == null ? "dynamic" : owner.FullName) + "." + method.Name
                    + " [" + method.Module.Assembly.GetName().Name + "]";
            }));
        }
        private static double Seconds() { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; }
        private static string Id(IFaction faction) { return faction == null ? null : faction.StringId; }
        private static string Number(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static void Failure(Exception exception)
        {
            SoakLog.Write("PEACE_DIAGNOSTIC_FAILURE", exception.GetType().Name + ": " + exception.Message);
        }
    }
}
