using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgesOfCalradia.WorkshopProcurement
{
    // Per-decision evidence budget, not accounting sampling. No game/model calls.
    // Retains the first deterministic example of each rejection per recipe and
    // exact omitted totals. Optional observer ABI; never persisted in a save.
    internal sealed class ProcurementWitnesses
    {
        private readonly string _shop;
        private readonly double _day;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private int _emitted, _omitted;
        internal ProcurementWitnesses(string shop, double day) { _shop = shop; _day = day; }
        internal void Record(int recipe, string source, int batches, string reason, params object[] values)
        {
            if (!ProcurementDiagnostics.CandidateObservationEnabled) return;
            if (_emitted >= 32 || !_seen.Add(recipe + "/" + reason)) { _omitted++; return; }
            _emitted++;
            var detail = new System.Text.StringBuilder("recipe=" + recipe + "; source=" + source + "; batches=" + batches
                + "; decisionDay=" + _day.ToString("R", CultureInfo.InvariantCulture));
            for (int i = 0; i + 1 < values.Length; i += 2)
                detail.Append("; ").Append(values[i]).Append('=').Append(Convert.ToString(values[i + 1], CultureInfo.InvariantCulture));
            detail.Append("; attribution=witnessed_planner_predicate; candidate_not_independent_shortage");
            ProcurementDiagnostics.Candidate(_shop, reason, detail.ToString());
        }
        internal void Finish()
        {
            if (ProcurementDiagnostics.CandidateObservationEnabled)
                ProcurementDiagnostics.Candidate(_shop, "witness_budget", "emitted=" + _emitted + "; omitted=" + _omitted
                    + "; limit=32; per=planner_decision; decisionDay=" + _day.ToString("R", CultureInfo.InvariantCulture)
                    + "; omission_is_explanation_sampling_not_accounting_loss");
        }
    }
}
