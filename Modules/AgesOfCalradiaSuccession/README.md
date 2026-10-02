# Ages of Calradia Succession

Standalone ownership boundary for the future hereditary succession system.

Current development status and the remaining in-game acceptance cases are in
[`Tests/Runtime-Validation.md`](Tests/Runtime-Validation.md). The sections below
record the module's design and implementation history; historical debug-only
limitations are superseded by the claimant lifecycle section at the end.

## Module responsibility

This module will exclusively own:

- hereditary succession laws and heir ordering;
- dynastic claims, legitimacy, pretenders, and disputed inheritance;
- regencies for underage or incapacitated rulers;
- recognition, coronation, usurpation, and succession crises;
- succession-driven civil wars and claimant settlements.

The design will not select rulers through a kingdom vote. Succession must first
resolve the lawful hereditary heir, then evaluate recognition and opposition to
that heir. Political resistance may create a pretender or civil war, but it
does not turn inheritance into an election.

## Boundary with the religion module

`AgesOfCalradiaReligions` continues to own personal faith, piety, clergy
relations, realm faith, and religious legitimacy. This module reads those
values through `ReligionService`; it never writes religion population cohorts
or clergy state.

## Hereditary core v0.2.0

The module now stores a versioned succession law, ruling dynasty, and last
recognized monarch for every kingdom. The hero-death listener and ruler-selection
Harmony guards queue hereditary resolution after native death processing completes.
The module ranks eligible hereditary claimants, blocks the vote, and transfers
the crown with Bannerlord's supported ruling-clan action.
If the normal claimant list is empty, a deterministic emergency order prefers
the surviving ruling house, adults, higher-tier houses, and then renown. It does
not restore voting. If a kingdom has no living clan leader at all, the empty
vote is cancelled and the incident is logged rather than manufacturing a ruler.

Default laws are culture-based: imperial realms use absolute primogeniture,
Vlandian realms use male-preference primogeniture, Aserai realms use agnatic
primogeniture, Battanian/Sturgian/Nord realms use house seniority, and Khuzait
realms use nomadic house seniority. Religious legitimacy is a secondary
claim-strength input; it cannot defeat a valid continuation of the ruling house.

The public `SuccessionService` exposes the current law and an ordered claimant
ledger for later debug and management UI work without coupling that UI to the
succession engine.

## Debug ruler-death test v0.2.1

Town, castle, and village menus include **[DEBUG] Kill a ruler to test
succession**. It opens a list of living rulers, displays each realm's succession
law, and requires a second confirmation. The action uses Bannerlord's native
old-age death path so it exercises the same ruler-death and succession events as
normal play. The player character is never an eligible target.

## Underage heirs and regencies v0.3.0

Primogeniture now follows the former monarch's child branches before collateral
relatives. If the lawful heir is younger than eighteen, the child is recorded as
heir while a deterministic adult regent governs the kingdom. The regent does not
become the new dynasty. If a regent dies, the same child remains heir and another
adult is appointed without a vote. At adulthood, Bannerlord's supported clan-
leader and ruling-clan actions transfer authority to the heir and close the
regency. Heir and regent identities are stored in the versioned campaign state;
v0.2 saves migrate with empty regency fields.

## Legitimacy, recognition, and claimant wars v0.4.3

Every accession now receives a 0–100 legitimacy score derived from its dynastic
basis, the heir's age, culture, personal faith, religious legitimacy, regency,
and coronation. Each non-mercenary clan deterministically recognizes the ruler,
remains neutral, opposes the accession, or supports the strongest recorded
pretender. These are recognition states, not votes.

Player rulers may hold a coronation from a town or castle menu. AI adult rulers
coronate after seven days. Regents cannot crown themselves in place of a child.
Legitimacy, coronation, accession basis, pretender, and clan recognition persist
in a separate versioned politics payload.

Town, castle, and village menus also provide **[DEBUG] Cause a succession civil
war**. After confirmation it creates a separate claimant kingdom through
Bannerlord's supported kingdom-creation, clan-defection, ruling-clan, and claim-
on-throne war actions. The opening twenty-day peace can immediately suspend the
war, but the claimant split remains. This destructive debug action should only
be used after saving.

This module does not own or modify World Events artwork, the political map,
borders, islands, island exclusions, the calendar, or core UI dimensions.
When a claimant realm forms, Succession gives it the claimant clan's original
banner, applies a lighter or darker variant of the parent kingdom's colours,
and sends a one-shot dirty notification to the existing campaign-map political
border behavior. The protected renderer remains the sole owner of rebuilding
and drawing its territory meshes.

## Death dispatch and election ordering

The succession sidecar listens to `CampaignEvents.HeroKilledEvent` and compares
the victim with persisted monarch/regent identities. Native clan inheritance may
already have changed `kingdom.Leader` before that notification. The listener
queues work; `TickEvent` performs the transfer on the campaign thread after the
death call stack finishes. There are no worker threads or native calls from
background tasks.

The former `KingdomDecisionAdded` listener was too late: installed Bannerlord
1.4.8 raises that event before starting the AI election or enqueueing the player
decision. Removing a decision in that event cannot prevent either action.

Harmony owner: `agesofcalradia.succession.ruler-election`.

| Native target | Patch and purpose | Boundary |
| --- | --- | --- |
| `Kingdom.AddDecision(KingdomDecision, bool)` | Prefix blocks enqueue/start only for `KingSelectionKingdomDecision` and requests deferred resolution. | Other decision types and campaigns without this behavior retain native execution. |
| `KingSelectionKingdomDecision.ApplyChosenOutcome(DecisionOutcome)` | Prefix blocks a stale saved or directly invoked election from overwriting hereditary authority. | Shares the dispatch gate; does not apply a second ruler transfer. |

Pending, completed and failed incumbent tokens use a new optional string save
key, `AOC_Succession_Dispatch_v1`. Existing succession/politics keys are unchanged.
Old saves recover from recorded dead incumbents and pending ruler decisions.
Snapshot and regency mutation paths do not overwrite an in-flight or quarantined
transfer. Duplicate event/decision requests coalesce, recursive ticks cannot
resolve twice, and repeated patch installation does not add duplicate prefixes.
Enforced native abdication still requests hereditary resolution. Ordinary stale
elections against the recorded living ruler are suppressed.

A native transfer exception is logged and quarantined instead of automatically
retrying a potentially partial mutation. Reload a pre-event save after diagnosing
such a failure. Missing Harmony targets abort startup and remove this owner's
partial patches. Other owners on the two targets are logged; this is conflict
detection, not a guarantee against another mod directly changing ruling clans.
The Harmony 2.4.2 assembly supplied by the required Core module is reused.

Run `Tests/Verify-SuccessionDispatch.ps1` with Windows PowerShell. It builds the
sidecar and test programs in a temporary directory with warnings treated as
errors, runs dispatch and persistence behavioral checks, audits the installed
native method order, and installs the actual prefixes on the installed native
methods inside a disposable process. The dispatch harness links production
integration code with native boundary doubles; it does not exercise real crown
transfers or certify mod interoperability.

Before deployment, use a disposable campaign to test AI and player-realm ruler
deaths by old age, battle and execution; adult and child heirs; regent death;
the player's own death/heir-selection flow; save/reload before and after transfer;
abdication; and ruler mods loaded on either side of Succession. Verify exactly
one lawful accession, no ruler ballot, unchanged unrelated decisions, retained
child/dynasty identity, and correct adulthood transfer. No protected Core or UI
artifact is rebuilt or deployed by this verification.

Validation on 2026-09-05: isolated Release build passed with warnings treated as
errors; 26 dispatch assertions, existing persistence verifier, module contract
check, installed native method audit and actual Harmony registration passed.
The installed CampaignSystem MVID was
`886629FE-6E60-40D7-9A57-8D46017179D9`. Both repository protected hashes matched.
Broader existing checks failed outside this change: CalendarMath expected
campaign profile schema 6 while the protected DLL reported 5; StrategicMapCoverage
failed its single-composer assertion against the protected prefab. Neither
protected artifact was changed to satisfy those checks. No deployment or in-game
campaign validation was performed. The Core rebuild step was excluded to honor
the protected-artifact rule; only the succession sidecar was rebuilt.

Follow-up on 2026-09-08: quarantine now blocks every later token for the affected
realm, including a request enqueued reentrantly before a native transfer fails.
A save taken inside a native transfer callback stores that realm as quarantined,
because the saved crown state may be partial; it does not quarantine the live
campaign if the action subsequently succeeds. Loading such a save emits a
diagnostic. Saves made before a transfer starts still resume pending work.
The optional dispatch save key and its three state fields remain compatible.

The updated suite passes 31 assertions. The normal sidecar Release output was
rebuilt with zero warnings/errors and a subsequent Build skipped CoreCompile as
up to date. The native Harmony audit also passed against that exact local DLL
using `-NativeAuditAssemblyPath`; the R&S integration and persistence checks
passed. This is a local build, not an installed-game deployment. In-game
campaign and real mod-combination validation remain outstanding.
The broader checks were rerun on this date and reproduced the calendar schema
and strategic-map single-composer failures recorded above. Repository protected
hashes still matched; no unrelated source or protected artifact was changed.

## Heir and regency bug fixes (2026-09-08)

The production resolver now skips a sister's branch in agnatic collateral
succession, while retaining that branch under absolute succession. Candidates
outside the inheriting kingdom no longer hide the next eligible dynastic heir.
A resident descendant of a foreign candidate retains branch priority. Resolution
uses the inheriting kingdom explicitly even if the recorded dynasty clan left it.
Foreign-clan transfers remain outside this module's implemented contract.

Emergency resolution excludes minor-faction and mercenary clans. Ruling house,
adulthood, tier and renown are ordered as separate priorities, so extreme renown
cannot defeat the ruling-house priority or overflow an additive score. Emergency
claim scores represent the resulting order. An emergency child heir enters a
regency, including a vacant regency when no adult regent exists.

The campaign behavior itself rejects coronation by a regent, dead ruler or child;
this no longer relies solely on the menu condition. A recorded heir who matures
while a regent-death request is pending retains the crown instead of being ranked
again under a potentially changed law. Adult fallback succession clears obsolete
minor-heir state. Undefined saved law/recognition values use their documented
defaults; non-finite legitimacy defaults to 50 and finite values are bounded to
0–100. Existing save keys and payload formats are unchanged.

Verification now includes the production campaign behavior in
`SuccessionEngineVerifier` as well as the production resolver in the dispatch
harness. The boundary doubles replace native game actions and event dispatch;
these are behavioral tests, not a live Bannerlord simulation. Together they pass
57 assertions (44 dispatch/resolver and 13 engine), plus persistence and actual
native Harmony installation checks. The local Release DLL was rebuilt with
warnings treated as errors. Real campaign, player-death and competing-mod runs
remain required before claiming runtime reliability.

## Regency event and reload fixes (2026-09-08)

The death listener now includes the recorded minor heir. A child's death resolves
the next heir on the next campaign tick without waiting for the regent to die or
for the daily audit. Death notifications use the victim's identity as their
dispatch token; startup recovery also detects a saved dead child.

Adulthood events and daily regency audits enqueue work instead of invoking native
leader changes directly. All these transfers now share dispatch quarantine and
save/reload protection. Vacant or temporarily ineligible regencies may be audited
once per campaign day, but a failed native transfer remains quarantined across
days and reloads. If no adult regent is available, the dead regent identity is
cleared while the child's lawful claim is retained.

The production behavior tests now exercise actual `SyncData` calls through a
string-store double, covering replacement heirs, a saved dead child, a save
between maturity notification and transfer, completed-transfer deduplication,
and a native clan-leader action that throws after mutation. The test suite passes
68 assertions (44 dispatch/resolver and 24 engine), plus persistence, native
Harmony and R&S contract checks. The local sidecar Release build passes with
warnings treated as errors. These tests still use native boundary doubles rather
than a live campaign; no installed-game deployment has been made.

## Campaign lifetime and claimant validity (2026-09-08)

`SuccessionService` now resolves its behavior from `Campaign.Current` for each
query rather than retaining the last campaign in a static field. Exiting a
campaign or entering one without Succession returns default service values;
reused kingdom IDs do not expose the previous campaign's saved laws.

Cached pretenders must remain living, active adult clan leaders in the relevant
kingdom. Lost leadership, eliminated/minor/mercenary clans, and incompatible
agnatic eligibility invalidate the cached result. Claimant-war supporters and
the war-creation entry point recheck current clan leadership and membership.
A stale clan cannot qualify by referencing another clan's leader. These guards
do not implement new claimant-war victory or settlement mechanics.

The suite now passes 90 behavioral assertions (44 dispatch/resolver and 46 engine).
Additional scenarios cover three consecutive regent deaths with the dynasty
preserved at adulthood, campaign switching, stale pretenders, and native clan or
kingdom leadership changing before the ruler-death notification. Persistence,
actual native Harmony installation, R&S contracts and protected repository hashes
also pass. The local Release build has zero warnings/errors. Live campaign and
real mod-combination validation remain outstanding.

## Political updates during unresolved transfers (2026-09-08)

Manual coronation, startup political initialization and daily political updates
now skip realms owned by the succession dispatch queue (pending, active or
quarantined). A temporary native leader or partially applied failed transfer
cannot gain coronation or overwrite saved legitimacy through these entry points.
Successful accession still evaluates its own political state during dispatch;
normal coronation resumes after completion. Save keys and Harmony targets are
unchanged.

The regression failed on manual coronation before the fix. Eight new assertions
cover pending and failed transfers, daily updates, production save/reload and
successful resumption. All 98 behavioral assertions (44 dispatch/resolver and 54
engine), persistence, native Harmony installation, module/R&S contracts and
protected artifact checks pass. The sidecar Release build passes with warnings
treated as errors. Live campaign validation remains outstanding; this is not a
deployment or a claim that the system is bug-free. Previously recorded unrelated
calendar schema and strategic-map coverage failures remain release blockers.

## Claimant lifecycle and authority recovery (2026-09-08)

Claimant wars now have a complete code path from formation through settlement.
`SuccessionCrisisController` owns crisis timing, persistence and lifecycle;
`SuccessionCivilWar` owns native formation/reunification actions. The campaign
behavior retains hereditary authority and political-state commits.

Automatic NPC opposition may rise fourteen days after accession when legitimacy
is below 45, there are at least two supporting clans comprising at least one
third of eligible clan leaders, and both coalitions hold fiefs. The player clan
is never automatically recruited or chosen as claimant. These are initial balance
rules, not empirically validated difficulty settings. The audit runs once per
campaign day on the campaign tick, after succession dispatch.

An eligible claimant wins when the original realm has no fiefs while the claimant
realm still has land. Loss of claimant land, death/loss of the claimant's clan
leadership, or peace after formation ends the claim in the incumbent's favor.
Captivity alone does not extinguish a claim; accession waits until eligibility
returns. Native elimination of either realm closes tracking without resurrecting
a kingdom. There is no artificial timer forcing an ongoing landed war to end.

Settlement returns current claimant-realm clans to the original realm, preserves
their current fiefs, installs the victorious claimant when appropriate, and
destroys the empty claimant kingdom. Clans that joined third-party kingdoms and
territory conquered by third parties are left alone. After settlement, automatic
uprisings have a ninety-day cooldown. The existing opening peace can end a newly
formed claim; the controller respects peace instead of redeclaring war against it.

The optional `AOC_Succession_Crises_v1` string payload stores original realm,
claimant realm, claimant hero, lifecycle stage and campaign day. Older saves with
no payload start without tracked crises. Preexisting debug-created kingdoms are
not guessed into this ledger from names or sanitized IDs. A save taken within a
native formation/settlement callback loads as quarantined. Failed or malformed
crises block further authority/political changes in both realms and are never
automatically retried. A successful live operation remains unaffected by a
mid-operation save. Inspect diagnostics and restore a pre-crisis save after a
partial native failure.

Abdications now exclude the outgoing ruler; a saved regent abdication preserves
the child and selects another regent. Vacant thrones are audited daily so newly
available candidates can inherit after an earlier empty candidate set. Native
ruler postconditions are checked before committing political state; a competing
mod that silently reverses a native transfer triggers quarantine.

Normal Release builds exclude destructive test-menu registration. Build with
`/p:EnableSuccessionDiagnostics=true` in an isolated output directory to enable
the ruler-death and claimant-war tools for disposable campaign testing. The
verification script builds and audits both variants. Coronation and debug menu
callbacks resolve the current campaign rather than retaining an old behavior;
coronation is disabled while authority is unresolved.

Validation: 164 behavioral assertions (44 dispatch/resolver and 120 production
engine/crisis), persistence, native settlement-target audit, actual Harmony
installation, and production/diagnostics separation pass. Native boundaries in
behavioral tests remain doubles. Live battles, player death/heir selection,
save compatibility inside a real campaign, and real competing-mod load orders
remain unverified. No installed module or protected artifact was deployed.
