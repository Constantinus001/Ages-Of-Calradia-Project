using System;

namespace AgesOfCalradia.PoliticalBorderOptimizer
{
    internal enum PublishedLoadingState { Idle, Pending, Ready, Failed }

    // Only the owning map's dismissal is held. The native map update continues.
    // Failure is terminal and explicitly reported; it must not deadlock loading.
    internal sealed class PublishedLoadingGate
    {
        internal object Behavior {get;private set;}
        internal object Screen {get;private set;}
        internal PublishedLoadingState State {get;private set;}
        internal string Failure {get;private set;}
        private double _blockedAt=-1;
        internal void Begin(object behavior)
        {
            if(ReferenceEquals(Behavior,behavior))return;
            Behavior=behavior;Screen=null;State=PublishedLoadingState.Pending;Failure=null;_blockedAt=-1;
        }
        internal void Bind(object screen,object behavior)
        {if(ReferenceEquals(Behavior,behavior)&&Screen==null)Screen=screen;}
        internal void Complete(object behavior)
        {if(ReferenceEquals(Behavior,behavior)&&State==PublishedLoadingState.Pending)State=PublishedLoadingState.Ready;}
        internal void Fail(object behavior,string reason)
        {if(ReferenceEquals(Behavior,behavior)){State=PublishedLoadingState.Failed;Failure=reason;}}
        internal void Cancel(object screen)
        {if(ReferenceEquals(Screen,screen)){Behavior=null;Screen=null;State=PublishedLoadingState.Idle;Failure=null;_blockedAt=-1;}}
        internal bool TryDismiss(object screen,object completedBehavior,double now,Action dismiss)
        {
            // The native dismissal can arrive before scene matching succeeds.
            // It must not bypass a queued load just because no owner was bound.
            if(Screen==null&&State==PublishedLoadingState.Pending)Screen=screen;
            if(ReferenceEquals(Screen,screen)&&State!=PublishedLoadingState.Idle&&State!=PublishedLoadingState.Failed)
            {
                if(State!=PublishedLoadingState.Ready||!ReferenceEquals(Behavior,completedBehavior))
                {
                    if(_blockedAt<0)_blockedAt=now;
                    if(now-_blockedAt<120)return false;
                    Fail(Behavior,"Published loading readiness timed out after 120 seconds.");
                }
            }
            dismiss();return true;
        }
    }
}
