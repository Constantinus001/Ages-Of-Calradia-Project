# War Score Recovery live acceptance

This is a trace-only campaign test. Do not deploy or overwrite the protected
Core DLL. The sidecar must report installed through `aoc.war_score_recovery`.

For each enabled diplomacy-changing mod and once with no such mod enabled:

1. Load a copied save with two kingdoms at war and run `aoc.war_score_trace on`.
2. Resolve one scored field battle. Run `aoc.war_score_trace report`; the trace
   must show a non-zero `score=before->after` transition.
3. Capture one fortification by siege. The trace must identify the fief and its
   owner, and the score must advance by the Core value (15 castle/25 town) or
   already have advanced by Core before recovery runs.
4. Advance exactly one campaign day. The `next-day` entry must retain the same
   non-zero score and report the tracked kingdoms' fortifications.
5. Cause or wait for peace while the score is below 100 but one side has greater
   occupied-fief value. The `peace` entry must show the promoted score (+100 or
   -100) and a matching non-zero `occupation` result. Verify in the campaign
   that the winning kingdom retains those fiefs after the peace notification.
6. Repeat with equal occupation value. The peace entry must show
   `occupation=0`, and Core white-peace reversion is expected.

Record the enabled module list, trace report, game version, save origin, and
whether each Harmony patch owner is present. A mod which bypasses or replaces
Core's handlers is not compatible until this exact matrix passes; do not infer
compatibility from an offline Harmony fixture.
