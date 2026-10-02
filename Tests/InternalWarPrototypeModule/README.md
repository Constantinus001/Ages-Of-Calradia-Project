# TEST ONLY - Private Clan Wars v0.6.0

## Automatic test logging

No extra test switch is needed. While war records exist, diagnostic snapshots are written on state changes and every 60 application-tick seconds, including when the state appears unchanged. Each build is identified separately. The reports check every controller's active clan/kingdom contract, exact operation identity, camp attachment, current capture owner, terminal camp residue, closed-war hostility, payment receipt shape, battle leaders and raid record identity. Cross-controller party/settlement reservation collisions and recovery blockers are flagged. Save/load payload callbacks are logged as `AUDIT PAYLOAD` events; these are not proof of native save completion.

Send the module's entire `Logs` folder after playing a disposable campaign. You do not need to inspect these values manually. Optional summary command from the installed test module directory:

```powershell
.\Summarize-InternalWarDiagnostics.ps1 -LogDirectory .\Logs | Format-Table Build, Check, Status, ObservedSamples, ReviewSamples -AutoSize
```

`OBSERVED_OK` means a specific sampled condition was consistent, not that an entire feature passed. `REVIEW` preserves suspicious observations (including transient native transitions or legitimate later ownership changes). `NOT_EXERCISED` means evidence is absent. The summary retains earlier warnings, separates module builds and flags incomplete reports. Sampling can miss brief events; the ordinary event log remains important.

Your reduced manual job is to trigger a siege/capture, a raid, multiple wars, peace/monarch controls, and a save/reload, then send the logs. Still tell us about crashes, unclickable settlements, broken movement or mission/UI problems. Logs cannot manufacture coverage for scenarios you never run, prove every reinforcement is correct, validate quest/UI visuals, or certify a native save round-trip without comparison.

1.0-readiness work: strategic AI now chooses the nearest viable target rather than abandoning all targets when the nearest is too strong. It preserves the six-hour planning delay across reloads, rejects malformed schedules, uses overflow-safe defending strength, and revalidates original operation authority after native siege orders. Interrupted native orders block recovery without modifying replacement records. Native acceptance remains outstanding; this development build is not promoted to 1.0 solely because managed checks pass.

Kingdom integration: `[TEST] Kingdom private-war overview` lists your kingdom's open conflicts and policy. The current ruler can use `[TEST] Monarch: kingdom private-war controls` to forbid/repermit new declarations or queue peace in every active local war. The ban affects both player and NPC declarations, not existing operations. Policy is saved per kingdom and survives ruler succession; only the current ruler may change it. Kingdom-wide peace retains captures, waits for battle-safe cleanup, does not change the selected controller, and does not silently forbid later declarations. Stale campaign/ruler confirmations are rejected. No new Harmony patches or existing save-schema replacements are introduced; missing policy data defaults to permitted declarations.

See `COMPLETION_CHECKLIST.md` for the subsystem inventory and native acceptance matrix. This remains a test package, not a claim that native gameplay or visual UI verification has finished.

Compensation peace records success only after verifying both the player's debit and the rival leader's credit, stable participants/realm, and the queued peace state. Invalid recipients and recipient balance overflow are rejected before payment. Uncertain callback outcomes retain the durable payment marker and stop recovery without attempting a second transfer or speculative refund. Save schemas are unchanged. The managed compensation verifier executes the actual diplomacy code with injected native gold/peace adapters; native campaign payment testing is still required.

Automatic diagnostic snapshots now back off for 60 map-tick seconds after a failed or throttled write. State-transition logging continues during the delay, and the next attempt captures the latest state. Identical consecutive failure messages are logged once; manual report requests remain available. This diagnostics-only change does not modify combat, peace, or saved campaign records.

This is not release code. It is an isolated diagnostic module for the inspected Bannerlord v1.4.8 campaign assembly. Build and packaging remain under `Tests/InternalWarPrototypeModule`.

No production `Modules` file is changed. No clan leaves its kingdom and no temporary kingdom is created.

## Implemented behavior

- Multiple concurrent bilateral declarations with an exact clan-pair registry. Ending one war does not end other wars.
- Native field encounters and clan-specific battle joining; neutral third clans remain outside the conflict.
- Player village raids, including counter-raids when the player is the political defender. NPC parties may order village raids when they lack a viable siege target; village resistance and damage remain native.
- Player and NPC siege operations, native preparation and assault, capture assignment to the victorious clan, and suppression of the ordinary claimant vote for authorized captures.
- Land sally-outs use reversed battle sides: owner forces attack the besieger. Owner victory lifts the operation without ending the war; besieger victory resumes the siege. Neither result transfers ownership. Automatic sallies use only owner-clan forces and require more than twice the besiegers' healthy troop count; the ordinary kingdom-wide sally planner is bypassed for registered sieges.
- Land siege-relief battles normalize the besieger to the native attacker side before player encounter setup. Relief victory lifts the siege without transferring the settlement; besieger victory resumes it. The native conquest-quest listener is guarded against treating either sally-outs or relief as a conquest.
- One settlement claim per declaring clan's war. Counterattacks do not replace or satisfy the attacker's claim.
- Status-quo peace after fourteen days or achievement of the claim; immediate concession; optional 5,000 gold payment to the rival leader. All terms retain current fief ownership. Concession and payment are alternative early exits, not territorial negotiations.
- Player monarch force-peace, plus emergency diagnostic cleanup.
- Kingdom overview, persisted royal declaration policy shared by player/NPC paths, and kingdom-wide peace orders with per-controller cleanup, reentry protection and live ruler revalidation.
- NPC political decisions every seven campaign days within the player's kingdom. Severe leader hostility (relation at or below -40) or an existing pretender dispute can cause a declaration. NPCs stop adding wars at four open conflicts. The same pair waits ninety days from its last declaration.
- NPC-only wars may agree peace after fourteen days. An uninvolved NPC monarch can order peace in a thirty-day-old vassal war at the next political tick.
- Optional succession integration reads the loaded sidecar's living pretender and recognition. A crown dispute can motivate a feud with the ruling clan; it does not establish a fief title or transfer the crown.
- Saved registry, claim outcomes, compensation receipts, previous-war history, and preserved malformed payloads. An interrupted payment has a durable marker preventing automatic repayment after reload.
- Automatic and manual diagnostic snapshots, including `Ctrl+Shift+F10`.
- A campaign-wide recovery safety stop survives save/reload, even when uncertain operation payloads are preserved unchanged. The first failure remains the recorded cause. Blocked saves do not automatically become safe after reloading; restore a pre-failure backup for further testing. Older saves without this new key load normally, but failures from earlier builds cannot be reconstructed retrospectively.

## Current limits

Multiple wars have independent native controllers: **one siege or raid per war** can run concurrently with operations in other wars. A party or settlement cannot be reserved by two wars. Menu selection does not transfer operation ownership. Malformed saves with competing reservations block new actions.

Mixed-clan armies, coalitions, naval operations, occupation economics, contested-title adjudication, and automatic crown transfers are not implemented. The political thresholds and peace prices above are initial test defaults. NPC raids wait when a player outside the defending clan occupies the village; neutral parties cannot join a private raid. NPC-only sallies do not draft a player visiting the target. Mods that directly construct map events while bypassing native encounter initialization are not covered by the relief normalization contract.

This package has not passed the full in-game matrix. Earlier Vostrum capture testing used an older single-war build. Save and native API checks do not prove campaign behavior.

## Test flow

1. Close Bannerlord, back up the chosen campaign, and install `package/AgesOfCalradiaInternalWarsTest` as a separate module. Enable Harmony and this module.
2. In a settlement menu, declare a war. Use `[TEST] Select active private war` to choose which conflict siege, raid and peace commands operate on.
3. Test simultaneous A-B and A-C wars: both pairs fight, B-C remain neutral, and ending A-B leaves A-C active.
4. Choose an internal siege target, travel to it, enter its menu, and select `[TEST] Begin siege of this settlement`. Native menus own preparation and assault.
5. Test player raids and counter-raids, NPC raids and player village defense, siege victory, defeat, player/AI sally-outs, relief from both encounter directions, and emergency cleanup. For sally-outs/relief verify no fief is awarded, no conquest quest completes, and the private war remains open. Verify settlement clicking works afterward.
6. Test negotiated peace, monarch peace and compensation. Compensation transfers real campaign gold; use the disposable pre-payment save for repeated experiments.
7. Save/reload after declaring, while two wars are marching/preparing, during raids, after capture and between operations. Select a different war during a siege and verify the original clan receives its capture. End only one war and check the other's orders remain intact.
8. Toggle global NPC declarations and each war's siege/raid AI independently. Observe NPC wars and inspect succession diagnostics when the optional sidecar has a real pretender.
9. Reports are under `Logs/Diagnostics`; the event log is `Logs/AgesOfCalradiaInternalWarsTest.log`. Pending cleanup is not successful completion. Keep the original backup.
10. As ruler, forbid new wars: player declarations and subsequent NPC political ticks must reject new wars while existing operations remain usable. Save/reload, verify the policy, then repeal it. With two wars active, issue kingdom-wide peace and confirm both finish cleanup, no foreign war changes, and captured ownership remains. Open a royal confirmation before a ruler/kingdom change and confirm afterward: the stale order must be rejected.

## Architecture and verification

The registry owns political identities; each war controller owns its native siege/raid context. Only the root behavior registers campaign events and forwards them to controllers. Menus select a controller, while combat, capture and cleanup use actual operation identity. Callbacks are revalidated; cleanup checks native event participants. The inspected CampaignSystem MVID remains the compatibility gate; failed patch registration removes all module patches.

The v3 conflict payload reads v1/v2 with zero compensation; old single-war saves migrate into the registry. Root keys remain compatible and other controllers use escaped war-ID namespaces. Operation schemas are preserved. Completed operation IDs may remain without a current record; an unfinished operation ID requires its record. An orphan child siege/raid cannot silently acquire a registry identity.

Run `Verify-InternalWarSystem.ps1` for the isolated Release module and all managed verifiers, native audits, compiled patch-transform checks, diagnostic formatter, protected baseline and package contracts. These tests use managed code and injected boundaries; native gameplay remains a separate test phase. This is not the protected Core release gate.
