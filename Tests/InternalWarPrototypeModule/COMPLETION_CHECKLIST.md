# Private Clan Wars: completion and acceptance checklist

## Scope and honest status

This inventory covers the isolated, same-kingdom, bilateral **land clan-war subsystem**. No temporary/rebel kingdom is used. Existing implementation is not the same as native acceptance. No row below is marked in-game verified for v0.6.0; the older Vostrum test does not validate this build.

The v0.6.0 integration work adds kingdom-wide oversight and ruler policy to the existing combat/operation controllers. It does not rebuild the protected kingdom renderer or replace the separate succession subsystem. Mixed-clan armies, coalitions, naval wars, occupation economics, contested-title adjudication and automatic crown transfer remain unsupported, not secretly completed or replaced with temporary kingdoms.

## Implementation inventory

| Capability | Implementation authority | Automated coverage / remaining acceptance |
| --- | --- | --- |
| Player declarations and exact bilateral hostility | TestDiplomacy, ConflictRegistry, CombatService | Registry/eligibility/combat checks; live player declaration and neutral third-clan test pending |
| NPC declarations and political peace | PoliticalAi | Actual AI checks, including royal ban and existing-war peace; live weekly tick pending |
| Concurrent wars, operation ownership and selected war | Controllers, MultipleConflicts, ScopedDataStore | Controller/save/eligibility checks; simultaneous native operations pending |
| Field combat and restricted reinforcement | CombatService, CombatPatches | Combat checks and native patch audits; player/NPC field battles pending |
| Player/NPC raids and village defense | TestRaids, NpcRaids, RaidArrivalPatch, RaidJoinPolicy | Raid lifecycle/arrival/join checks; native raid and village defense pending |
| Siege selection, travel, preparation and assault | TestBehavior, TestService, TestAiCoordinator | Actual strategic-AI target selection, schedule reload and native callback guards; eligibility, record, native contracts and compiled transforms; native camp/mission/AI execution pending |
| Capture assigned to victorious clan | TestPatches, TestBehavior | Capture contract/transform checks; live owner assignment and claimant suppression pending |
| Sally and relief without accidental conquest | SallyService, ReliefService, NonCapturingQuestPatch | Actual service tests and native audits; both victory directions and aftermath pending |
| Negotiated peace, concession, compensation | TestDiplomacy | Actual payment callback/reload checks and record rules; native gold transfer and battle-safe peace pending |
| Individual monarch peace and emergency cleanup | TestBehavior, SiegeCleanup, TestRecovery | Eligibility/record/native contract checks; cleanup postconditions in game pending |
| Kingdom overview and royal controls | RealmMenu, RealmGovernance | Actual callback tests; visible layout/click verification pending |
| Saved royal ban obeyed by players and NPCs | RealmRules, RealmGovernance, TestDiplomacy, PoliticalAi | Actual policy persistence, ruler authority, AI and source wiring checks; native save/ruler transition pending |
| Kingdom-wide peace | RealmGovernance dispatches each existing controller | Actual batch/foreign isolation/reentry/authority-loss tests; multi-war native cleanup pending |
| Succession context | SuccessionBridge | Read-only inspected API boundary; optional sidecar present/absent/real pretender native tests pending; no crown transfer |
| Save migration, malformed state and payment uncertainty | Records, Registry, Controllers, RecoveryState | Managed migration/identity/payment/recovery checks; native save/reload matrix pending |
| Diagnostics and safe failure | DiagnosticMonitor, DiagnosticsReport, PatchSafety | Formatter/monitor/recovery/native binding tests; hotkey and generated native reports pending |

Names in the authority column omit the `InternalWar` prefix for readability. No new Harmony target was introduced by the kingdom governance layer.

## Native acceptance sequence

Use a copy of `aocnewtest` or another disposable campaign, never the original soak seed. Close the game before installing the separate test module. Keep an untouched pre-module backup; uninstalling this module from a save with active native operations is not an accepted recovery procedure.

Record pass/fail and attach a `Ctrl+Shift+F10` snapshot at each checkpoint. A queued peace message alone is **not** a pass.

1. Startup and compatibility: module enables on the inspected v1.4.8 build; protected Core/calendar remain unchanged. Verify controls render in town, castle, village and siege menus.
2. Declarations: sworn player clan declares against a different regular clan in the same kingdom; identities remain in the same kingdom. Mercenary, dead/eliminated, foreign and stale-dialog targets are rejected. Neutral third clans remain neutral.
3. Player siege: select a rival fortification, travel, begin, prepare and assault. Test victory, defeat and retreat. On victory, the intended clan owns the fief, no ordinary claimant vote takes it, and settlement clicks work afterward.
4. Counterattack and raids: test defender-clan counter-siege, both player raid directions, NPC raid arrival, player village defense, neutral visitors and raid cleanup.
5. Sally/relief: win and lose each battle type; neither awards a fief nor completes a conquest quest. Verify siege resume/lift and safe aftermath from both encounter directions.
6. Concurrent operations: A-B and A-C wars coexist without B-C hostility. Select another war during an operation; capture and cleanup stay with the original controller. End A-B and ensure A-C remains operational.
7. Peace: test status quo, concession, compensation, individual royal peace and emergency cleanup before battle, during battle and after capture. Gold transfers once; captures are retained; completed cleanup removes only owned native residue.
8. Kingdom policy: as current ruler, forbid declarations. Both player and weekly NPC declarations stop; existing operations continue. Save/reload, verify the ban, repeal it, and verify declarations resume. Ruler/kingdom changes invalidate old confirmations.
9. Kingdom peace: with two local wars and a foreign record, issue one order. Both local wars enter pending peace and complete independently after battles. Foreign records and selected operation ownership remain unchanged. Repeat the order safely. A ban must be issued separately to prevent future wars.
10. Persistence: save/reload idle, marching, preparing, raiding, after capture, during pending cleanup and with concurrent wars. Verify claim/compensation/history identities and no duplicated operation. Inspect malformed/uncertain-state behavior only in disposable fixtures; it must not automatically retry native payment or unsafe repair.
11. NPC/succession: observe NPC siege/raid selection and weekly politics. Test succession sidecar absent and present with a real living pretender; context may motivate a war but must not grant a crown or settlement title.
    For strategic AI, place a strongly defended target nearer than a weaker eligible target and confirm the viable target is selected. Save/reload during the six-hour planning delay and verify it remains deferred. Compare logged defending strength with native garrison/militia/resident-party totals: whether native settlement totals already include resident parties remains an explicit balance/accuracy check, not an assumed result.
12. Soak: advance the disposable campaign through several political ticks and operation cycles. Inspect reports for stuck cleanup, duplicate reservations, incorrect reinforcements and log growth. Balance values are test defaults until this passes.

## Release decision

- Automated gate: `Verify-InternalWarSystem.ps1` builds only this module, runs managed/native-contract checks, verifies protected artifacts and stages its package.
- Native acceptance: pending. UI callback tests are not screenshots or real Bannerlord click tests.
- Repository release: separately blocked by the existing calendar schema and strategic-map coverage checks and dirty tree. Do not run a protected Core rebuild to work around these failures.
- Completion criterion: the above native sequence must pass with evidence and all failures must be corrected before calling the subsystem production-ready. The unsupported expansions listed above require explicit design/implementation work, not a status-label change.
