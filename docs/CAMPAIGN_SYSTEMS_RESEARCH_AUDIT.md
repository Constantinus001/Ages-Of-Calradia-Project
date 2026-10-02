# Campaign Systems: research, audit and implementation decision

Date: 2026-09-20. Status: research decision; NOT release approval.

Implementation follow-up: see `Modules/AgesOfCalradiaCampaignSystems/README.md`.
The original findings below describe the pre-integration draft. Constructor
bypass, cross-field travel bound, missing-file semantics, conserved transfers,
immutable plans, reentrancy and persisted order timing have since been addressed
and tested. Core source registration and an optional read-only Logistics ABI now
exist. Release and live-acceptance gates remain outstanding; this record is not
retroactively changed into a claim that every finding or acceptance gate is closed.

## Verdict

The user's idea is 9/10: one discoverable, editable umbrella with independent
subsystems and explicit ownership. One giant patch or one multiplier governing
every rate and duration would be substantially worse. The current new framework
draft is 3/10 for production readiness, distinct from the previously tested
procurement candidate. It compiles, but is not wired into any game module.

Do not describe repeated reviews as proving perfection. A 10/10 acceptance score
means every agreed gate below passed for a named build, not that bugs cannot exist.

## Three audit passes

### 1. Ownership and compatibility

- Verified the immutable approved DLL contains QuestDeadlineBalancePatch:
  StartQuest postfix, BalanceQuestDeadlines guard, Never sentinel guard,
  existing due-minus-now multiplied by CalendarAnnualBalance.DurationFactor.
  Another multiplier at the same boundary would apply scaling twice.
- Existing CalendarSettingsState remains the calendar settings authority.
  Existing native quest deadlines remain the save authority.
- New CampaignSystem.Owners is only a string dictionary. It neither discovers
  active patches nor rejects duplicate owners. It cannot enforce authority yet.
- No procurement/calendar-fixes project references CampaignSystems; this is a
  standalone library draft, not an installed or integrated umbrella.
- Native absolute days, displayed calendar dates, real seconds and annual rate
  factors must be distinct concepts. A shared clock must read CampaignTime;
  never invent another elapsed-time accumulator or assume 365 days for all profiles.

### 2. Configuration and modding

Reproduced against the new Release assembly:

1. `new ProcurementSettings()` bypasses Parse and creates all-zero settings,
   including zero batches and zero freight distance per day. CampaignSystem
   accepts the object. P1: make construction private and validate at entry points.
2. A valid XML combination MaximumDistance=10000 and
   FreightDistancePerDay=0.01 permits 1,000,001 campaign days in transit.
   P1: add cross-field and derived-duration limits, not just individual ranges.
3. File.Exists false currently means defaults. Invalid/inaccessible paths can
   also return false. P1 before runtime integration: only confirmed missing files
   may use defaults; unreadable configuration must produce an explicit failure.
4. Pending orders do not yet persist configurable lifetime/return-delay values.
   P1: capture policy revision plus immutable obligation fields on order creation.
5. No settings file, documented load path, preview command, per-recipe overrides,
   external extension interface or runtime consumer exists yet. These are required
   implementation work, not completed features.

XML parsing already rejects DTDs, unknown attributes, non-finite values and
unsupported schema versions. Preserve these protections. Default numeric parity
must be tested against the current procurement candidate before balancing changes.

### 3. Accounting, save lifecycle and evidence

- Reproduced using the exact EconomicTransfer source: a single wallet credit
  changes 100 to 110 with no debit. This is a generic mutation journal, NOT a
  conservation-enforcing economic authority. P1 before exposing it to mods:
  typed cash-transfer commands require matched debit/credit legs; goods transfers
  require inventory plus cargo entries; production transformations declare recipe
  inputs/outputs; legitimate grants/expenses need explicit source/sink reason codes.
- Public mutable callback legs allow callers to alter the plan during execution.
  P1: snapshot an immutable plan; use stable account identity and validate aliasing.
- Compensation restores balances but cannot undo arbitrary native event effects.
  P1: define commit/event ordering, reentrancy exclusion, persisted operation IDs,
  partial-failure quarantine and idempotent receipts. Never retry unknown commits.
- Preserve save keys, namespaces, existing absolute deadlines and private cargo.
  Added DataMembers deserialize as zero/null when missing; migrations need an
  explicit legacy policy rather than relying on field initializers.
- Existing procurement Encode/Decode size asymmetry remains a separate tracked
  blocker. Define admission bounds before accepting a new obligation; do not
  truncate or silently discard existing cargo to fit a save.
- Existing tests certify the existing procurement path, not this new unused library.

## Recommended architecture

One versioned Campaign Systems composition root, with these boundaries:

| Subsystem | Authority and integration |
| --- | --- |
| Time/calendar | Adapter to protected Core and CampaignTime; explicit units |
| Configuration | Schema registry, immutable effective snapshots, provenance |
| Economy policies | Named production, consumption, wage, revenue and supply policies; no universal multiplier |
| Transfers | Typed conserved cash/goods commands and explicit sources/sinks |
| Procurement | Existing domain/save ledger; consumes shared settings/transactions |
| Quest timing | Read actual saved deadlines; current Core owns scaling; no second patch |
| Diagnostics | Read-only receipts and coverage; never modifies economic state |
| Extensions | Versioned capability registration, stable IDs, dependency checks |

This is a modular in-process system, not network services, a new database or a
replacement save engine. Domain owners retain their state. A unified settings
surface delegates to existing owners; it does not create competing sources of truth.

## Editable values contract

- Ship documented defaults and an optional user override file; stable IDs,
  descriptions, units, ranges and change scope for every supported setting.
- First version precedence: shipped defaults < user overrides. Do not introduce
  arbitrary load-order-based mod override merging. Add explicit dependencies and
  deterministic conflict rejection before third-party policy packs.
- Distinguish **editable balance** from **noneditable safety invariants**. Modders
  may change costs/reserves/durations within supported bounds, not disable
  conservation, save validation, finite-number checks or protected-artifact checks.
- Classify changes: display-only; future operations; campaign restart; migration.
  Existing freight quotes, committed ETAs, stock lifetimes and quest deadlines
  are not retroactively recalculated from a newly edited file.
- Validate the entire candidate configuration; activate one immutable snapshot
  only at a main-thread safe boundary. Invalid reload keeps the previous valid
  snapshot and reports why. Initial invalid config disables affected new actions
  while preserving existing obligations. No per-tick file reads or watcher-thread
  Bannerlord calls.
- Include revision/hash and provenance in diagnostics, without private file data.
- Quest-setting UI first exposes only already-supported Core controls. Independent
  per-quest multipliers need a separately audited integration/handover, not another
  StartQuest postfix layered blindly over the protected one.

## Implementation order and acceptance gates (10 points)

Each gate earns one point only when evidenced; partial implementation is not a pass.

1. Ownership: complete map of native/Core/sidecar policies; exact installed targets
   and active Harmony ownership checked; duplicate authority rejected safely.
2. Settings: immutable construction, safe parsing, unknown/schema rejection,
   cross-field validation, bounds, documented defaults and deterministic precedence.
3. Integration: real procurement consumes the framework; no dead parallel
   implementation; one framework assembly identity/version in the eventual package.
4. Accounting: balanced cash/goods, explicit sources/sinks, replay/reentrancy
   rejection, overflow limits, callback failures and failed rollback covered.
5. Time/quests: native and calendar units separated; Never, expired/future,
   pause/speed changes and save reload tested; no duplicate duration scaling.
6. Persistence: old/missing/unknown/corrupt payloads, size boundaries, policy
   revisions, in-transit/delivered/partial orders and unchanged quest deadlines.
7. Modding: documented public capability API; overrides/default parity tested;
   missing dependencies and unsupported versions fail with actionable diagnostics.
8. Diagnostics: one run manifest/build/settings identity; receipts reconcile stock,
   cash, production, expenses, payouts, quests and coverage without flooding logs.
9. Release: zero-warning sidecar builds, full targeted regressions, immutable
   artifact hashes, clean committed package, security scan and rollback artifact.
10. Live acceptance: one preplanned combined capture exercises actual lifecycle,
    save/reload, representative supply chains and settings-change behavior; measured
    overhead and observed economic outcomes meet thresholds defined before the run.

Do not request another campaign run until diagnostics coverage preflight passes.
Separate deterministic event fixtures from live economic acceptance: calendar
elapsed days alone do not guarantee a liquidation, war, shortage or quest boundary
occurred. Report missing events as coverage gaps, not clean behavior.

## Evidence and limitations

- New framework Release build: zero warnings/errors. Not integrated or deployed.
- Constructor bypass and extreme settings reproduced against the compiled draft.
- Unbalanced-credit probe reproduced against exact draft source in an isolated
  .NET process. Initial assembly-reference probe failed due to the host runtime;
  it was not treated as test success.
- Protected DLL and WorldCalendar XML SHA-256 still match AGENTS.md.
- No game/saves/settings changed. No runtime fixes made after the request switched
  to research/audit. Existing test results do not certify new framework behavior.

## Primary research

- TaleWorlds Bannerlord 1.4.8 API entry: https://apidoc.bannerlord.com/v/1.4.8/
  Actual quest implementation verified in the locally installed/protected binaries;
  older online API pages are not treated as version-specific proof.
- Microsoft options validation/snapshot concepts:
  https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options
  Use the pattern, not an ASP.NET hosting stack in this .NET Framework game module.
- Harmony 2 priorities: https://harmony.pardeike.net/v2/articles/priorities.html
  Launcher order alone does not establish patch authority.
- .NET data-contract versioning:
  https://learn.microsoft.com/en-us/dotnet/framework/wcf/feature-details/data-contract-versioning
- File.Exists failure semantics:
  https://learn.microsoft.com/en-us/dotNet/API/system.io.file.exists?view=net-6.0
- Compensation limits and idempotency:
  https://learn.microsoft.com/en-us/azure/architecture/patterns/compensating-transaction
  Apply failure principles, not the article's cloud/distributed infrastructure.
