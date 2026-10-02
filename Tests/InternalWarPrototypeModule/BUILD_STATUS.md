# Private Clan Wars v0.6.0 - build handoff

Verified 2026-09-08. Isolated test module, not a production release.

1.0-readiness audit: development build updated without claiming a 1.0 release. Council review confirmed native campaign/UI acceptance remains the next blocking gate. No Bannerlord process was observed during the read-only process check; installation and native testing still need user coordination under the earlier no-disturb constraint.

## Implemented

- Automatic test logging: build-tagged state and periodic health snapshots check all controllers, not only the selected war. Checks cover active clan/kingdom contracts, operation identities, camp attachment, current capture ownership, terminal camp residue, closed-war hostility, payment receipt shape, battle leader identities, raid records and concurrent reservations. Save/load payload callbacks leave explicit markers. The packaged read-only summary keeps review evidence across later healthy snapshots, separates builds, flags incomplete reads and never marks absent/manual-only evidence as passed. No game state or save schema changed by this diagnostics layer; actual gameplay/visual acceptance remains pending.
- Strategic AI chooses the nearest viable fortification/village, retains six-hour backoff across reloads, and rejects invalid saved schedules. Defending strength accumulation cannot overflow Int32. Native siege orders immediately revalidate original record/war/participant ownership after callbacks; uncertain outcomes stop recovery without rewriting replacement records. An additive strategic-schedule key preserves legacy saves. Native strength aggregation still needs the acceptance check documented in the checklist.
- Kingdom-wide integration: current-realm overview, saved royal declaration ban/repeal shared by player and NPC paths, and batch royal peace through independently owned controllers. Ruler/campaign identity is rechecked on confirmation and between orders; reentry cannot dispatch duplicate work. Policy defaults preserve older saves, malformed policy is retained and fails closed, and foreign kingdoms are excluded. New UI uses existing native inquiry/menu APIs, not new Harmony targets.
- `COMPLETION_CHECKLIST.md` inventories all subsystem capabilities and the remaining native acceptance sequence; it is shipped and hash-checked with the test package. Native/UI acceptance is not claimed by managed tests.
- Compensation verifies the exact payer debit and recipient credit, participant/realm identity and pending peace before recording a receipt. Recipient overflow and invalid participants are rejected before transfer. Uncertain native results retain their payment marker and block retries, including after save/reload; no speculative refunds occur. This is a diplomacy/native-gold boundary change with no schema or Harmony-target changes. Compatible callbacks must preserve the agreed exact transfer; otherwise the system fails closed for inspection.
- Automatic diagnostic reports defer failed/throttled writes for 60 map-tick seconds, retain transition logging during backoff, capture the latest state on retry, and deduplicate repeated failure messages. Manual requests remain available. Actual monitor tests inject input/time/report boundaries; native gameplay is not exercised.
- Same-kingdom bilateral clan wars without temporary kingdoms, including concurrent wars and separately owned siege/raid controllers.
- Clan-specific field encounters, sieges, captures, player/NPC raids and reinforcement restrictions.
- Land sally-outs and relief battles with native-side normalization, noncapturing outcomes and a scoped conquest-quest guard.
- Settlement claims, negotiated status quo/concession/compensation, monarch peace, NPC political decisions and immutable closed-war history.
- Optional read-only succession-dispute context, scoped saves, migration/identity validation, uncertain-payment protection and diagnostics across controllers.
- Durable campaign recovery stops preserve the first failure across save/reload, including when operation payloads are preserved for inspection. Empty or missing keys cannot clear an existing stop; clean legacy saves remain compatible. Recovery requires a pre-failure backup rather than automatic unblocking. Diagnostics also report blocked campaigns without usable operation records.

One siege or raid is permitted per war; different wars may operate concurrently. Parties and settlements cannot be double-reserved. The menu selection does not determine capture or cleanup ownership.

## Verification completed

`Verify-InternalWarSystem.ps1` passed including packaging. Release builds produced no warnings.

| Verification | Result |
| --- | --- |
| Record/history/registry policy | 23 suites, 301 assertions |
| Actual eligibility service | 206 scenarios, 523 assertions |
| Combat policy | 127 assertions |
| Raid lifecycle | 41 scenarios, 165 assertions |
| Controller persistence | 15 assertions |
| Raid arrival | 30 assertions |
| Political AI | 18 assertions |
| Actual strategic AI | 24 assertions: viable target selection, claim/reservation guards, save/backoff, native callback mutation/failure |
| Actual realm governance/menu | 41 assertions plus 4 integration wiring contracts |
| Actual compensation diplomacy | 54 assertions, including callback failure, reentry, and payment-marker reload |
| Sally service | 22 assertions |
| Relief service and normalization | 31 assertions |
| Noncapturing quest guard | 25 assertions |
| Scoped datastore | 16 assertions |
| Raid join policy | 28 assertions |
| Durable recovery state | 21 assertions and 4 owner-wiring contracts |
| Diagnostic monitor retry/recovery and periodic health | 11 assertions |
| Actual automatic acceptance checks | 18 assertions |
| Diagnostic summary aggregation | 7 assertions |
| Compiled Harmony bindings | 26 patch methods, 174 assertions |
| Compiled instruction transformations | 30 scenarios, 87 assertions |
| Diagnostic formatter | 16 checks |
| Native contract audits | Passed; exact v1.4.8 MVID and 19 expanded signatures |
| Protected renderer/calendar baseline | Passed |
| Package/source contracts | Passed |

These checks execute managed policies and explicit test boundaries. They do not prove native gameplay, mission behavior, balance, or compatibility with another mod's patches.

Staged DLL SHA-256:
`581F044BFF83120D5410C25556D22F944579D4068645C462212BC95229529864`

Package: `package/AgesOfCalradiaInternalWarsTest`, with `SHA256SUMS.txt`. The generated test package was regenerated; no installed game files, campaign saves, protected Core DLL or protected calendar prefab were modified.

## Still required before calling this finished

Run the README's disposable-campaign matrix, especially simultaneous operations, save/reload, abort after capture, player/NPC raids, both sally outcomes, relief from either encounter direction, monarch peace and compensation. Earlier Vostrum testing is not evidence for this build.

Mixed-clan armies, coalitions, naval operations, occupation economics, contested-title adjudication and automatic crown transfer are outside this implemented test scope. External mods creating map events directly can bypass encounter normalization. AI thresholds are unbalanced test defaults, and shared-kingdom native strength estimates remain a compatibility/balance risk.

Repository-wide release checks remain separately blocked by existing failures observed during this work:

- `Tests/Verify-CalendarMath.ps1`: schema expected 6, actual 5.
- `Tests/Verify-StrategicMapCoverage.ps1`: expected one map composer while engine widgets draw both legend icons.

Those unrelated systems were not altered to make this test module pass. The dirty repository and unverified native campaign matrix prevent treating this as a production release.
