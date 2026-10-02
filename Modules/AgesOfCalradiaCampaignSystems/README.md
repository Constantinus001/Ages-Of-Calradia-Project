# AOC Core Campaign Systems — integration candidate

This is **Core infrastructure**, not a separate optional launcher module. The
source Core SubModule.xml registers CoreSystemsSubModule. Build output is isolated
under this project's bin directory; neither protected Core DLL nor WorldCalendar
prefab is rebuilt or replaced. Nothing has been deployed.

## Implemented

- One per-campaign composition root with validated campaign clock access,
  read-only quest remaining-time service, immutable supply settings and optional
  Logistics connection. It resets at game end/unload.
- Procurement references this assembly, uses its settings, and routes native
  cash/stock/cargo transfers through its conserved transfer service. Transfer
  plans are immutable snapshots, balanced per resource, reject nested execution,
  compensate failures and report when compensation cannot restore balances.
- The caller still owns its save ledger, operation identity and failure quarantine.
  This is not a transaction database or automatic guarantee against arbitrary
  third-party event side effects. Production transformations, grants and sinks
  are not accepted as ordinary conserved transfers.
- Existing calendar configuration, economic cadence patches and quest deadline
  scaling remain with their established Core owners. The only new Harmony patch
  is an optional Bannerlord 1.4.8 NavalDLC guard on
  `DistributePartyShipsAndRecoverGold(MobileParty)`: after a successful native
  distribution for one party, it rejects a second immediate distribution of that
  same still-live party. It neither prices ships nor changes the first transfer;
  missing/changed NavalDLC targets leave native behavior untouched and are traced.
- Source Core manifest registration is included. Release packaging still must
  include the new companion exactly once; the old two-sidecar release allowlist
  does not publish this candidate. Never deploy by bypassing that gate.

## Editable settings

Copy `CampaignSystems.example.xml` to:

`Documents/Mount and Blade II Bannerlord/Configs/AgesOfCalradia/CampaignSystems.xml`

The file is optional and is read at campaign start. Missing file/directory uses
defaults. Invalid/unreadable configuration is reported; procurement blocks new
orders but can drain valid saved obligations. Existing saved ordering preference
is not overwritten. There is no live reload or in-game configuration UI yet.

Unknown names, duplicate sections, future schemas, non-finite values, invalid
ranges, fractional integer fields and XML DTDs are rejected. Settings cannot be
constructed without validation. Complete travel must not exceed 365 campaign days.

| Setting | Default | Accepted range / unit |
| --- | ---: | --- |
| BatchesPerOrder | 3 | 1–100 batches; cargo still capped at 1000 units |
| CapitalReserveDays | 7 | 0–365 days of workshop expenses |
| SupplierReserveBatches | 7 | 0–365 local recipe batches |
| IronSupplierReserveDays | 0 | 0 disables; 1–365 campaign days of authoritative local iron recipe demand; offline candidate uses 14 |
| MaximumAdaptiveBatches | 0 | 0 retains fixed lots; otherwise BatchesPerOrder–100, caps lead-time-sized lots with a small-lot fallback |
| DeliveryDelayCostWeight | 0 | 0 retains cheapest landed cost; 0–10 weights estimated uncovered delivery days by daily expense for ranking only |
| MinimumSupplierStock | 10 | 0–100000 units |
| FoodCategoryFloor | 100 | 1–100000 units |
| GrapeCategoryFloor | 10 | 1–100000 units; grape only; local recipe and aggregate food guards still apply |
| ReorderBufferDays | 1 | 0–30 campaign days added to route lead time |
| FoodStoresFloor | 100 | 1–100000 aggregate food stores |
| FoodBufferDays | 30 | 0–3650 campaign days |
| MaximumDistance | 300 | 1–10000 native land-route distance |
| FreightDistancePerDay | 100 | 0.01–10000 distance per campaign day |
| HandlingDays | 1 | 0.01–365 campaign days |
| FreightBase | 2 | 1–100000 denars |
| FreightUnitDistance | 100 | 1–100000 divisor for units × distance charge |
| BlockedReturnDays | 14 | 1–3650 campaign days |
| StockLifetimeDays | 30 | 1–3650 campaign days after actual arrival |

Changes affect new orders. Each order persists its return delay, stock lifetime
and policy fingerprint alongside its already-persisted quote and ETA. Older
ledgers without new fields retain the original 14/30-day policy. Calendar, food
cadence and quest multipliers are **not** duplicated in this file.

## Logistics plug-in boundary

The Core companion discovers `AgesOfCalradiaLogistics.CampaignSystemsApi` on demand
from the loaded `AgesOfCalradiaLogistics` assembly. No hard assembly dependency is
added to Logistics. Standalone Logistics and Systems L & R preserve assembly/save
identities. ABI v1 is public static `int ApiVersion` and
`int ReadReserve(string partyId)`. `-1` is unknown/untracked/no campaign; `0` means
known empty. Reads never initialize parties or grant supply. Missing, unsupported
and failed providers are explicit statuses. Core does not use stale `Active` data.

Current integration is **read-only party reserves**, not workshop freight. Supply
crates, party reserves and workshop cargo remain distinct; no second production,
consumption, delivery or speed rule is introduced. Both systems use CampaignTime
already. Merely detecting Logistics must never disable timed workshop delivery.

Future transport ABI acceptance contract:

1. Explicit `workshop-transport.v1` capability, not inferred from reserve access.
2. Reserve/accept a stable shipment ID with complete cargo, cost and policy data.
3. Persist its single transport owner before dispatch; one handler can deliver.
4. Idempotent status/receipt acknowledgement, save/reload and cancellation tests.
5. Missing provider after dispatch holds the existing shipment and reports why;
   never switch an in-flight shipment to a fallback that can deliver it twice.
6. Reconcile costs, inventory and cargo in one combined diagnostics run.

The present Logistics module has no workshop transport service. This hand-off is
not implemented or claimed plug-and-play for freight.

## Commands (after future approved installation)

- `aoc.systems_status`: configuration path/revision and declared ownership map.
- `aoc.quest_times`: read current saved quest deadlines; Never is not arithmetic.
- `aoc.logistics_status <party-id>`: optional provider status and known reserve.

The ownership map is a declared architecture contract, not a runtime audit of
every third-party Harmony patch. Full policy registration/conflict enforcement,
per-recipe overrides and economic-wide mod extension commands remain future work.

## Verification

Run Windows PowerShell:

`Tests/Verify-Candidate.ps1`

This builds the Core companion/procurement/diagnostics and an isolated Logistics
candidate, then runs framework, procurement/native, actual Logistics ABI, supply
math, Python analyzer and protected/calendar/map regressions. It verifies the
Core manifest entry. It is not the release/security gate or live acceptance.

2026-09-20 results: zero-warning builds, 40 framework assertions, 84 procurement
assertions, seven native planner scenarios (including editable policy), native
delivery/return/liquidation and 16 production cases, actual optional Logistics
discovery/read-only checks, 11 native Core lifecycle assertions, logistics
math/regressions and 43 analyzer tests pass. The economy coverage rejection fixture
now targets the freshly built isolated diagnostics DLL explicitly, retaining its
stale-build rejection check; synthetic fixtures never enter game logs.

Procurement encode and decode share a 4,000,000-character payload limit. New
orders reserve fault/lifecycle growth space before any debit or stock mutation.
Existing readable ledgers without that space are held and preserved byte-for-byte
as their original save string, not silently truncated or replayed. Fault text is
capped in the save while the full exception remains in diagnostics.

The combined capture now includes Core build/policy identity and saved quest
deadline observations. Native adapter and missing/invalid/empty-quest fixtures
pass. The offline delta package checks loadable entry points, command discovery,
single framework identity and preservation of every installed Core registration.
See `docs/CAMPAIGN_SYSTEMS_PRETEST_RECEIPT.md` for package/security status and
offline ledger timing; neither packaging nor those timings prove live behavior.

The private candidate completed its Defender scan/hold. The release entry point
now exposes `Tests/Verify-Release.ps1 -CampaignSystemsCandidateOnly` without
deployment or bypass options. This is not public-release certification.
Remaining: reproducible public packaging, in-game startup/load order,
unsupported-module coexistence, live performance and combined live acceptance.
See `docs/CAMPAIGN_SYSTEMS_NEXUS_RELEASE.md` for the public-release contract.
No game, save, installed module, campaign speed or user settings were changed.

## Wine operating-margin policy (local candidate)

`Procurement WineExpenseCoverage="0"` preserves native behavior (default).
Values 1..3 enable a wine-only AI policy; the combined candidate selects 1.25.
Only a non-hidden, single-recipe workshop producing the wine category qualifies.
Its required contribution per batch is the lesser of the native hurdle and
`ceil(daily expense * coverage / authoritative daily recipe rate)`. Input costs
and procurement freight remain additional costs. Coverage is an estimate at
potential throughput, not a guaranteed realized operating profit.

Player workshops, initialization, ownerless/multi-recipe workshops and missing
rate evidence retain native rules. Procurement and the native notable gate use
one policy implementation in the procurement sidecar. Nothing changes production
speed, item prices, town cash, taxes or saved deadlines. Invalid configuration
disables the opt-in behavior. New settings require matching companion binaries;
restore the previous XML when rolling binaries back.

# Naval cash-out implementation candidate

The opt-in, three-route naval hull valuation correction is implemented in this
companion. It is not enabled or deployed automatically. See
[NAVAL_CASHOUT.md](NAVAL_CASHOUT.md) for configuration, native compatibility
contracts, verified checks and remaining live-acceptance limits.

