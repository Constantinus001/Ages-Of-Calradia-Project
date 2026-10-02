using System;
using System.Collections.Generic;

namespace AgesOfCalradia.PoliticalFillSeamFix
{
    internal interface IMeshSwapRow
    {
        int Id { get; }
        bool HasOriginal { get; }
        bool HasCandidate { get; }
        void AddOriginal();
        void AddCandidate();
        bool RemoveOriginal();
        bool RemoveCandidate();
    }
    // The same state machine is tested against simulated before/after native
    // failures. Presence is queried after failure, never inferred from flags.
    internal static class MeshSwapTransaction
    {
        internal static void Commit(IList<IMeshSwapRow> rows, Action refreshAlpha, Action<string> log)
        {
            foreach (IMeshSwapRow row in rows)
                if (!row.HasOriginal || row.HasCandidate) throw new InvalidOperationException("Unexpected mesh membership before commit;row=" + row.Id);
            try
            {
                foreach (IMeshSwapRow row in rows)
                {
                    row.AddCandidate();
                    if (!row.HasCandidate) throw new InvalidOperationException("Candidate addition not confirmed;row=" + row.Id);
                    if (!row.RemoveOriginal() || row.HasOriginal) throw new InvalidOperationException("Original removal not confirmed;row=" + row.Id);
                }
                refreshAlpha();
            }
            catch (Exception ex)
            {
                log("mesh commit failed;rollback starting; " + ex);
                bool failed = false;
                foreach (IMeshSwapRow row in rows)
                {
                    try
                    {
                        if (!row.HasOriginal) row.AddOriginal();
                        if (row.HasCandidate && !row.RemoveCandidate()) throw new InvalidOperationException("Candidate rollback removal rejected.");
                        if (!row.HasOriginal || row.HasCandidate) throw new InvalidOperationException("Rollback membership validation failed.");
                    }
                    catch (Exception rollbackError) { failed = true; log("ROLLBACK INCOMPLETE;row=" + row.Id + "; " + rollbackError); }
                }
                try { refreshAlpha(); }
                catch (Exception alphaError) { failed = true; log("ROLLBACK alpha refresh failed; " + alphaError); }
                throw new InvalidOperationException(failed ? "Native rollback incomplete; inspect log." : "Native swap failed; original meshes restored.", ex);
            }
        }
    }
}
