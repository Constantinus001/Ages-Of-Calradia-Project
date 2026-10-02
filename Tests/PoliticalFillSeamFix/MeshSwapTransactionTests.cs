using System;
using System.Collections.Generic;
using AgesOfCalradia.PoliticalFillSeamFix;

internal static class MeshSwapTransactionTests
{
    private sealed class FakeRow : IMeshSwapRow
    {
        public int Id { get; set; }
        internal bool Original = true, Candidate;
        internal string Fault;
        internal bool AfterEffect, FaultUsed, RefuseRollback;
        public bool HasOriginal { get { return Original; } }
        public bool HasCandidate { get { return Candidate; } }
        private void Before(string operation)
        { if (Fault == operation && !AfterEffect && !FaultUsed) { FaultUsed = true; throw new InvalidOperationException("before " + operation); } }
        private void After(string operation)
        { if (Fault == operation && AfterEffect && !FaultUsed) { FaultUsed = true; throw new InvalidOperationException("after " + operation); } }
        public void AddOriginal() { if (RefuseRollback) throw new InvalidOperationException("rollback refused"); if (Original) throw new InvalidOperationException("duplicate original"); Original = true; }
        public void AddCandidate() { Before("add"); if (Candidate) throw new InvalidOperationException("duplicate candidate"); Candidate = true; After("add"); }
        public bool RemoveOriginal() { Before("remove"); bool existed = Original; Original = false; After("remove"); return existed; }
        public bool RemoveCandidate() { bool existed = Candidate; Candidate = false; return existed; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static int Main()
    {
        int tests = 0;
        foreach (string operation in new[] { "add", "remove" }) foreach (bool after in new[] { false, true })
        {
            var first = new FakeRow { Id = 1 }; var second = new FakeRow { Id = 2, Fault = operation, AfterEffect = after };
            bool threw = false;
            try { MeshSwapTransaction.Commit(new IMeshSwapRow[] { first, second }, () => { }, s => { }); }
            catch (InvalidOperationException ex) { threw = true; Assert(ex.Message.Contains("original meshes restored"), "safe rollback classification"); }
            Assert(threw && first.Original && second.Original && !first.Candidate && !second.Candidate, "before/after failure must restore both rows without duplicates"); tests++;
        }
        {
            var row = new FakeRow { Id = 1 }; int refreshes = 0; bool threw = false;
            try { MeshSwapTransaction.Commit(new IMeshSwapRow[] { row }, () => { if (++refreshes == 1) throw new InvalidOperationException("refresh"); }, s => { }); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw && row.Original && !row.Candidate && refreshes == 2, "alpha refresh failure must roll back meshes and retry old alpha"); tests++;
        }
        {
            var row = new FakeRow { Id = 1, RefuseRollback = true }; bool diagnosed = false;
            try { MeshSwapTransaction.Commit(new IMeshSwapRow[] { row }, () => { throw new InvalidOperationException("refresh"); }, s => { if (s.Contains("ROLLBACK INCOMPLETE")) diagnosed = true; }); }
            catch (InvalidOperationException ex) { Assert(ex.Message.Contains("rollback incomplete"), "must not falsely report rollback success"); }
            Assert(diagnosed, "persistent native recovery failure must be diagnosed"); tests++;
        }
        {
            var row = new FakeRow { Id = 1 }; int refreshes = 0;
            MeshSwapTransaction.Commit(new IMeshSwapRow[] { row }, () => refreshes++, s => { });
            Assert(!row.Original && row.Candidate && refreshes == 1, "successful swap retains candidate and reapplies alpha"); tests++;
        }
        Console.WriteLine("PASS: " + tests + " mesh transaction failure/rollback tests."); return 0;
    }
}
