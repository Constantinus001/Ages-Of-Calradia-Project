# Published border loading-screen dismissal

The reviewed F1B border file and its 42-region fill repair remain unchanged.
This optimizer change enforces readiness at the normal map loading-screen
dismissal call, replacing the earlier temporary SceneView-hiding approach.

## Native boundary

The approved renderer (560F1B...) queues preparation from both
OnSessionLaunched(CampaignGameStarter) and OnGameLoadFinished postfixes.
The existing MapScreen HandleIfBlockerStatesDisabled prefix performs the one
bounded preparation attempt after scene readiness and identity checks.
It always returns true; native map updates and readiness counters continue.

A transpiler on that same SandBox.View method (assembly C7360E...) replaces
exactly its one static LoadingWindow.DisableGlobalLoadingWindow call with
ldarg.0 followed by a static guarded wrapper. The surrounding reviewed IL must
load _sceneReadyFrameCounter, compare against 3, branch past dismissal, then
reset that same counter to zero. Missing/duplicate calls, an altered pattern or
exception boundary at the call reject installation; existing activation cleanup
removes the optimizer patches. No global LoadingWindow method is patched.
A postfix cannot enforce this contract because native dismissal already occurred;
skipping the whole original method would block native readiness progression.

## State contract

Pending publication retains the loading screen. The first dismissal binds an
as-yet-unbound pending load so missing early scene binding cannot bypass the hold.
Otherwise only the owning map is held; unrelated maps remain unaffected.
Ready permits normal dismissal only for the same completed behavior, after the
sidecar verified both borders and required fill. A stale completion cannot unlock
a new campaign. The wrapper performs no rebuilding or polling, and invokes the
native dismissal at most once per permitted native call.

The OnFinalize prefix clears the owning map's state. Duplicate startup events do
not restart an attempt; a new behavior resets it. Native readiness flags,
SceneView enable state, saves and economy settings are not changed.

## Explicit fault fallback

A failed bounded build, verification exception, or 120 seconds of blocked
dismissal becomes Failed. To avoid the prior loading deadlock, failure releases
native loading and displays an AOC warning that native fallback is active.
This is not reported as successful preload and does not guarantee published
geometry on failure. The ordinary success path requires verified borders AND fill.

## Validation

The Release optimizer build, normal optimizer verifier and actual Harmony
composition suite cover the new hooks and IL pattern. Executable generated-IL
tests run the patched map loop: pending blocks dismissal while the counter and
rest of the loop still run; readiness permits one native dismissal. Tests cover
stale/new behaviors, an unbound pending load, cancellation, unrelated maps,
failure/timeout, and zero/two/changed native-call patterns.

The guarded call was installed against the actual reviewed SandBox.View assembly.
Protected Core and published asset checks are still required for deployment.
Live save-load confirmation remains pending. Logs distinguish held dismissal,
verified completion and explicit failure. This does not shorten the roughly
19-second synchronous build or prove loading-screen animation remains smooth.