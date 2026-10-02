# Ages of Calradia War Score Recovery

This Bannerlord 1.4.8 compatibility sidecar preserves the approved AOC Core DLL.
It patches the two protected ledger event handlers only after they run. If a
scored battle or fortification capture left the score unchanged, it restores the
Core's intended award: 3–10 for a battle, 15 for a castle, and 25 for a town.
Fortification recovery is restricted to the native `BySiege` ownership event;
later fief-grant/king-decision ownership changes can never create another award.

It does not use calendar-day arithmetic, rescale campaign time, or change fief
ownership itself. A score that already changed is never awarded again; a score
at the Core's +/-100 clamp is also left unchanged.

When peace is proposed before Core reaches +/-100, a side with greater occupied
fief value wins the settlement instead: towns are worth 25 and castles 15,
matching Core's capture values. Equal value remains white peace. A genuine
Core +/-100 result always wins over this fallback. This applies to automated
peace and the player ledger, so an early AI treaty no longer automatically
erases a clear territorial victory.

Compatibility is deliberately hash-bound to approved Core `560F1B...9868D8E`.
The sidecar verifies the private handler signatures before patching. Discovery,
reflection invocation, or installation failures are logged once and leave Core
behavior untouched; a partial Harmony installation is rolled back.
Its prefixes and postfixes use Harmony's last priority and observe
`__runOriginal`: if an earlier mod has suppressed a Core handler, recovery makes
no award or peace-score mutation and the trace records that incompatibility.
The fief handler also requires the approved binary's `newOwner` and `oldOwner`
parameter identities, avoiding ambiguous inference from transient settlement state.

After loading a campaign, run `aoc.war_score_recovery` in the developer console.
It reports whether this sidecar bound to the approved Core target. This is an
installation diagnostic only; the acceptance evidence remains a non-zero score
after a real battle or fief capture.

Run `aoc.war_score_audit` to read the saved `WarScoreV1` records without
modifying them. Capture its output immediately after a battle/capture and again
after the next campaign day if a score appears to reset.

For trace-only live acceptance, run `aoc.war_score_trace on`, reproduce one
scored battle or siege capture, advance exactly one campaign day, and run
`aoc.war_score_trace report`. The bounded in-memory report records score before
and after each event, its next-day score, relevant fortification ownership, and
the score/occupation outcome at peace. Run `aoc.war_score_trace off` to stop it.
It has no save writes and is off by default. Test this procedure with every
enabled diplomacy mod before deployment; the offline fixture cannot establish
Harmony ordering in a real modded campaign.

The exact evidence matrix is in [Tests/Runtime-Acceptance.md](Tests/Runtime-Acceptance.md).

Install only after the normal candidate/release gate approves it. A live save
must demonstrate a non-zero score after a battle or capture before this is
considered runtime-validated.
