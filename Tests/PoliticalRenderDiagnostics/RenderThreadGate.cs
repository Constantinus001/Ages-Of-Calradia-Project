using System;
using System.Threading;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    internal enum ThreadObservation { AwaitingApplicationTick, Allowed, UnexpectedThread }

    // Loader threads are deliberately not registered. Only the host application's
    // first tick establishes affinity for passive scene/native observations.
    internal sealed class RenderThreadGate
    {
        private int _applicationThread;
        internal int BoundThreadId { get { return Volatile.Read(ref _applicationThread); } }
        internal ThreadObservation BindApplicationTick(int observedThread)
        {
            if (observedThread <= 0) throw new ArgumentOutOfRangeException("observedThread");
            Interlocked.CompareExchange(ref _applicationThread, observedThread, 0);
            return Observe(observedThread);
        }
        internal ThreadObservation Observe(int observedThread)
        {
            int expected = BoundThreadId;
            return expected == 0 ? ThreadObservation.AwaitingApplicationTick
                : expected == observedThread ? ThreadObservation.Allowed : ThreadObservation.UnexpectedThread;
        }
    }
}
