# Supply-chain observer

## v7 deployed — September 20; live validation pending

Scanned release: `C:\Users\fpicc\AocRelease\1e995262a92a\verification.json`.
The four-priority review is `Tests/Analyze-EconomyAcceptance.py`.
Earlier candidate/deployment notes below are historical, not current status.

Follow-up: added native ClanVariablesCampaignBehavior.DailyTickClan(Clan) and
DailyTickHero(Hero) boundaries to catch leader/notable settlement mutations.
GiveGoldAction receipts retain caller chain, requested transfer, transaction label,
hero endpoints and an independent transfer ID. GOLD_TRANSFER_ENDPOINT reports
gross endpoint changes even when nested WALLET_CHANGE records already explain
them; it is context, never additional ledger income. These are diagnostic-only
hooks with validated non-ref signatures, void finalizers, no model reevaluation
and fail-stop capture behavior. SupplyCash verification covers nested receipt
serialization. Full causal closure still needs live evidence; old missing events
cannot be recovered from these new hooks.

Completed v6 capture `0e583ab135b74721865d53cc78019c32` supplies the evidence;
do not rerun or replace it. v7 canonicalizes lord-party cash to leader hero gold,
logs alias changes, and excludes dormant party backing wallets from active
reconciliation. Broader workshop input/output, Hero.ChangeHeroGold and
GiveGoldAction.ApplyInternal observations subtract already-observed nested deltas.
They do not re-evaluate finance models, prices, RNG or native mutations.
The native market-factor hook now supplies buy-price evidence, avoiding the tiny
Town wrapper which produced no observations in v6. Repeated price calls are not
independent purchases. Exact native signatures are required; failures stop capture.

`Tests/Verify-SupplyCash.ps1` tests installed boundaries and serialized nested
accounting. Both analyzers retain v6 support with explicit legacy-alias semantics;
v7 adds WALLET_ALIAS/WALLET_DORMANT. Old traces cannot be deduplicated retroactively.
Bounds remain 15 campaign days / 60 wall minutes / 5 GiB. No claim that all finance
sources or live inlining/leader transitions are covered; residual checks remain.
No deployment, marker rearm, game action or save change occurred for this candidate.

## v6 deployed and armed — September 19, 12:45 local

Game and launcher absent; exclusive installed-DLL access passed. Installed
SHA-256 equals the tested candidate:
`3BDF05AF6A9BB2266EA09C505A637DBABD12CD8AE21FDED608CD5E5A927A5212`.
Protected baseline passed before and after deployment. Native supply/out-argument
checks and 12 analyzer fixtures passed. Only the diagnostics DLL was replaced.
The previous v5 DLL is retained for forensics in Documents diagnostics folder
`unsafe-v5-forensics-before-v6-20260919-124549`; do not restore its unsafe observer.

One-shot AocSupplyCapture.enabled armed for the next user-loaded campaign.
Use an untouched pre-diagnostics save for a clean baseline. Existing
AocSoakRun.enabled was left untouched; supply-only entry takes precedence and
bypasses the long-soak controller for this campaign session. No game was started,
no save changed, no speed changed and no checkpoint created. v6 runtime behavior,
coverage and overhead remain unverified until that load/run.

## v6 broad candidate — September 19 (not deployed or armed)

See [BROAD_CAPTURE_PLAN.md](BROAD_CAPTURE_PLAN.md) for the coverage matrix,
preplanned analysis branches, safeguards and explicit limits. Adds native
purchase and route decisions, all-category/all-recipe coverage and wallet
attribution/reconciliation. Bounds: 15 days / 60 minutes / 5 GiB.

IMPORTANT: a second unsafe out-argument observer was found in v5's workshop
input gate. Its object-array binding reproduces caller cost 37 becoming 0.
The typed by-value correction and native-exception regression pass in v6.
The earlier captures must not be certified as an unaltered economic baseline.
Historical deployment notes below describe past states, not safe rollback advice.

## Corrected v5 deployment — September 17, 15:38 local

Verified game/launcher closed. Out-struct regression, native-hook checks and
protected baseline passed again. Installed corrected DLL SHA-256:
`387F50B06668CCABBC043A7C857E720EBD674FBA7A30FF86B1D2DA1C921B3D65`.
Installed hash equals local tested candidate. Faulty previous DLL retained only
for forensics in Documents diagnostics `faulty-v5-replaced-20260917-153831`;
do not restore that build for gameplay. Protected baseline passed after copy.
One-shot supply marker armed. Use an untouched pre-v5 save. First live values
must be checked; no in-game validation or economy-fix claim yet.

## Critical v5 correction — local fix verified, deployment blocked

The initial v5 DLL C300FB023B3EE6C340E61AA5A2ACD13E29B54DB3619C6BD7D909EC21E79E52A2
is faulty. Capture a3de149766394485905bf36dae6e3b27 is invalid for economy
comparison. Harmony 2.4.2's object[] __args postfix binding for the native
GetCategoryPriceData out-struct writes the default boxed argument back to the
caller. Reproduced in an isolated private-struct fixture: native 1.25 becomes 0.
The binding can corrupt results even when the observer returns early. Ending
capture or reloading a save in the same patched process is not a reliable remedy.

Corrected Lookup<T> uses an exact closed private-struct type passed BY VALUE,
without __args or ref/out parameters. Regression fixture proves preserved
output, return, call count and native exceptions. Installed native-hook test
checks the observer's exact value-type binding. Analyzer now rejects nonpositive
price indices and negative proposed quantities; historical faulty runs cannot
pass just because their scopes close.

Release build, eight analyzer fixtures, wool out-parameter regression, supply
native-hook checks, economy/soak checks, CalendarMath, StrategicMapCoverage and
protected baseline passed. No live verification claimed. Deployment deferred:
TaleWorlds.MountAndBlade.Launcher.exe PID 17444 remains running. No installed DLL,
game process or save changed in this correction turn. Use an untouched pre-v5
save after the corrected DLL has been deployed and the process restarted by user.

## Supply v5 — wool selling decisions (deployed and armed September 17)

Installed at 12:16 local, SHA-256
`C300FB023B3EE6C340E61AA5A2ACD13E29B54DB3619C6BD7D909EC21E79E52A2`.
Rollback DLL verified in Documents diagnostics `wool-decision-deploy-20260917-121618`.
Game/launcher absent; exclusive DLL access passed. Only diagnostics DLL replaced.
Protected baseline passed before/after. One-shot supply marker armed.

SupplyWoolSaleObserver observes native 1.4.8 SellGoodsInternal (non-horse pass),
including no-cargo and retained-cargo visits. Records session-local party
identity, opening/closing wool cargo, town cash, native selling-limit arguments,
GetCategoryPriceData result (average/minimum indices and cached category value),
native price factor and actual price results. A preflighted DUP/void observer
after the single native RoundRandomized call records quantity before caps, with
no extra RNG evaluation. Native results/branches/exceptions remain unchanged.
Weight-loss mode is explicitly recorded; do not infer ordinary-rule caps there.
Repeated item evaluations/price calls are not independent transactions.

Analyzer supports v5 decision scope closure, outcome counts and required evidence
categories. It does not automatically diagnose a refusal reason from a missing
row. Unknown schema fails closed. Seven synthetic tests pass. Release build,
native hook/IL checks, economy/soak checks, CalendarMath, StrategicMapCoverage and
protected-baseline checks passed. Live evaluation coverage and volume remain
unverified until the next capture. No gameplay changes or game/save control.

## Supply v4 — market attribution (deployed, not armed)

September 17 at 11:38 local: diagnostics DLL deployed after successful Release
build, supply native-hook checks, economy/soak regression checks, six analyzer
fixtures, CalendarMath, StrategicMapCoverage and protected-baseline checks.
Game/launcher absent; exclusive DLL access passed. Installed SHA-256:
`74B8E003F5665BD260E0068106B890118686AA84155D4D69FDB7E5822699B372`.
Previous DLL hash `3BA8D844A7D736710B87EFE7C514E5A78ED5DA0B9C8961C6E2FED52416473764`
verified in Documents diagnostics backup `market-observer-deploy-20260917-113837`.
Only diagnostics DLL copied. Protected baseline passed after deployment.
Supply marker absent: no capture armed or game/save operation performed.
Runtime coverage and overhead remain unverified.

September 17: adds observation-only private native boundaries for household
MakeConsumption, DeleteOverproducedItems, GetFoodFromMarketInternal and town
SellItemsAction.ApplyInternal. Tracked-category transactions record market and
counterparty inventory deltas, town/caravan cash, trade-tax accrual, native
ChangeGold arguments and evaluated price results. Gold calls are evidence, not
an assertion that every negative call is tax. Non-caravan hero cash is not
recorded and is not claimed reconciled. Nested scope IDs permit root-only totals.

Market state records actual stock value and smoothed supply/demand without new
model/RNG evaluation. Session receipts identify effective model types and patch
owners/priorities for observed operations and selected effective model methods.
Normal time/day completion adds a terminal inventory snapshot. Byte/error limits
can still truncate a scope; analyzer reports incompleteness, not native corruption.

The existing 256 MiB cap remains; changed balances and per-operation state are
recorded, not global per-tick rosters. Live overhead/volume and hook execution
remain unverified. Price layers may both emit: they are quotes, never quantities
to sum. Market endpoint totals do not yet constitute complete all-sink stock or
tax-adjusted cash reconciliation. Unobserved mutations remain possible.

Verification: Release build; native supply/market hook install checks; six
Python fixtures including market scopes and unknown schema; economy and soak
regression checks; protected baseline. No deployment, arming, game control or
balance changes. This candidate must demonstrate actual consumption and caravan
rows in-game before the run can be considered useful for causal attribution.

## Supply v3 - workshop follow-through

Deployed/armed September 15 at 05:52 local. Installed hash:
`3BA8D844A7D736710B87EFE7C514E5A78ED5DA0B9C8961C6E2FED52416473764`.
Backup: `workshop-observer-deploy-20260915-055220` in the Documents diagnostics
directory. Game/launcher absent and DLL exclusive-access check passed before
copying; candidate/installed hashes match. Protected baseline passed before and
after. CalendarMath, StrategicMapCoverage, existing economy/soak checks passed.
Only the diagnostics DLL was deployed. No game or save operation was performed.

Adds SupplyWorkshopObserver only in the opt-in supply session. Exact installed
1.4.8 workshop targets: RunTownWorkshop, both TickOneProductionCycleFor*Workshop,
both Can*WorkshopProduceThisCycle, DetermineItemRosterHasSufficientInputs, and
dispatcher OnItemProduced/OnItemConsumed. Prefix/postfix/finalizer observations
do not change arguments, native results, recipes, money, speed or save state.
All hooks share the supply owner and rollback/unload cleanup. Missing targets
fail the capture; original exceptions are not suppressed. Private
GetWarehouseRoster was audited as a lookup-only getter; no model is re-queried.

Selected recipes touch cow/sheep/hog/wool/meat/felt. Logs include native event
inputs/outputs, market and warehouse before/after deltas, per-category event-net
versus inventory-net checks, cycle successes/failures, input-gate results,
profit/capital/town-cash context, and progress-derived production increment.
Player cash/warehouse gate context is not mislabeled an exact rejection reason.
Daily snapshots include capital, town cash, rebellion and owner. Event counts
must reconcile with inventory changes before being treated as actual flows.
The analyzer flags unclosed cycles, missing workshop coverage and flow residuals.
No guarantee is made that every rejection branch will occur during 15 days.

Verification: Release sidecar build and native hook install checks; deterministic
progress cap/decrement tests; analyzer tests for incomplete cycles and flow
residuals; journey classification precedence and party-instance separation.
Live event coverage and overhead remain acceptance gates, not build guarantees.

Existing 0f35d26d2dca416fbea7c1bad429d1dc capture reclassified using
Tests/Classify-SupplyJourneys.py: 274 unmatched loads split exclusively into
77 destruction callbacks, 119 subsequent loads on the same party instance,
76 loads younger than two days, and 2 older unresolved loads (trips 917/1230).
The two last snapshots contain none of the six tracked categories, are about
0.93 days older than capture end, and cannot prove final cargo or stuck routing.
Reloading a party's cargo does not prove its previous delivery. No lost-unit
estimate, production increase, cash subsidy or routing change follows from this.

Next test: same starting AOC save, same 2x setting, same 15-day window; limits
remain 60 wall minutes/256 MiB, with no automatic checkpoint/save/speed/quit.
Confirm WORKSHOP_PROGRESS/CYCLE/GATE/CONSUMED/PRODUCED early. Compare like-for-like
windows; the prior capture has no workshop consumption baseline and cannot prove
a causal workshop improvement. This is observation, not a balancing trial.

## Previous supply v2 deployment - September 15, 2026

Corrected diagnostics DLL deployed and supply marker armed at 05:27 local.
SHA-256: `358CC94861F53FCE9C2C537D4B80A972FB821380053F4978E13585B23C47994A`.
Previous DLL retained under the Documents diagnostics directory in
`supply-observer-deploy-20260915-052739`; backup and installed hashes verified.
Release build, SupplyCapture, EconomyTransactions, SoakDiagnostics, both Python
analysis fixtures, CalendarMath and StrategicMapCoverage passed. Protected
baseline passed before and after deployment. No game process was running;
no process or save was started, stopped or modified. Next campaign load still
must demonstrate live sale/quote/cash coverage; arming is not runtime acceptance.

Initial implementation September 13, 2026; diagnostics module only. Historically deployed and armed at
04:08 local on September 13 after the user requested deployment. Installed DLL
SHA-256: A898EF1AABE6C817727F736AFCE20ED0D7E9B07EA03C0B1C7F14F514558EE4DC.
Previous DLL backed up in the Documents diagnostics directory under
`supply-observer-deploy-20260913-040831`; backup and deployed hashes verified.
Protected baseline passed before and after. That marker was consumed by the
September 13 capture; this historical receipt does not describe current arming.
The four-input production candidate is unchanged.
Protected AgesOfCalradia.dll and WorldCalendar.xml are not rebuilt or copied.

## Boundaries and evidence

SupplyCapture owns a separate bounded TSV writer and one-shot lifecycle.
SupplyChainObserver owns installed-native Harmony integration and session-local
scope/party/journey identity. SupplyValueTaps contains only validated observer IL.
No settings/profile/save schema, production, storage, dispatch, trade, AI, speed,
checkpoint, automatic save or quit behavior is changed. The completed long-soak
controller is bypassed for the entire supply-only session, including after stop.

Native targets: VillagerCampaignBehavior.ThinkAboutSendingItemToTown,
MoveItemsToVillagerParty, SendVillagerPartyToTradeBoundTown,
OnSettlementEntered, OnMobilePartyDestroyed;
VillageGoodProductionCampaignBehavior.TickProductions and TickGoodProduction;
SellGoodsForTradeAction.ApplyInternal(Settlement, MobileParty, private detail enum); Village.GetWarehouseCapacity;
Town.GetItemPrice(EquipmentElement, MobileParty, bool);
CampaignEventDispatcher.OnItemProduced(ItemObject, Settlement, int).

Exact native bodies/signatures are preflighted before patching. Dispatch and
loading transpilers add DUP plus a void observer after existing RNG/capacity/
weight getters; they do not call getters again, alter results, remove branches,
or advance RNG separately. Dispatch requires the audited single RandomFloat
getter followed by 0.15f. Duplicate/missing calls or incompatible signatures
reject installation. Original native exceptions propagate; observer errors close
the evidence stream and log failure without interrupting gameplay. Live mod
owners/MVIDs are recorded. Third-party changes can still invalidate attribution.

The log records:

- Actual village item-production events for cow, sheep, hog, wool, meat and felt.
- Native dispatch roll, evaluated warehouse threshold, initial stock, party/
  battle/raft/route state and whether loading was called. A prethreshold gate is
  reported as grouped party/battle evidence, not an invented exact branch.
- Production stock/ceiling observations; native loading capacity and carried
  weight values; before/after village and party inventories.
- Session-local trip IDs, route requests, settlement entry, destruction callback
  and daily in-transit cargo snapshots. Loading is not falsely labeled departure
  or delivery. Preexisting trips remain explicitly uncorrelated.
- Actual sale-boundary inventory and cash changes, already-evaluated sale prices,
  town cash and cargo at quote. Endpoint conservation residuals are explicit.
- Daily town/village stocks and measured wall seconds per campaign day, including
  pauses and instrumentation overhead (not a configured-speed claim).

Other item categories remain in total inventory counts, but detailed supply
conservation is limited to the six categories above. Native events and completed
scope deltas are different evidence: do not add nested scope deltas or transfers
together as newly created production. A positive integrity check does not certify
the complete economy, steady-state balance, or causal improvement.

## Use after authorized deployment

Run Tests/Arm-SupplyCapture.ps1 with the game closed. It checks the installed
diagnostics DLL against this build and rejects conflicting capture markers.
It does not deploy, stop/start the game, select a save or alter the old soak.
Load the agreed AOC save yourself. No separate vanilla campaign is required.

The next load consumes AocSupplyCapture.enabled and writes
Documents/Mount and Blade II Bannerlord/AgesOfCalradiaSoakDiagnostics/
AocSupply-<session>.tsv. Start/stop/failure receipts go to AocSoakEvents.tsv.
The capture stops at the first of 15 campaign days, 60 wall minutes or 256 MiB.
Wall/file limits explicitly mean incomplete, not success. Reaching 15 days does
not certify sufficient delivery cycles. It leaves the game running without
resuming the long-soak controller. Reload/end closes as incomplete; it does not
silently append a different campaign or restart capture. No saveable state.

Analyze with Python and Tests/Analyze-SupplyCapture.py <AocSupply-file.tsv>.
It streams the file, checks closure/sequence/scope lineage/transfer residuals,
reports missing categories and correlates observed loads with town sale entry.
Its totals are descriptive; matched starting saves and observation windows are
required for a causal comparison. No indefinite or year-long trace is armed.

## Verification

September 15 correction: observe the private sale transaction body instead of
the tiny public wrapper, which can be inlined before session-time patching.
The absence of earlier sale rows does not prove this was the sole runtime cause.
Preflight validates the enum and verification inspects actual price/cash/roster
calls in that body. Value callees are patched before callers are rebuilt.
Only villager-trade calls are observed; no arguments/results are changed.
Changed-only inventory rows and compact random-gate scopes remove redundant
zero balances/context, while all dispatch decisions and conservation checks stay.
Missing delta rows mean unchanged values within an observed completed scope,
not missing snapshots. Root scopes are buffered atomically (at most 1 MiB);
the byte limit discards an uncommitted scope with an explicit incomplete receipt,
rather than emitting an unmatched BEGIN. Tests cover nested closure and cap hits.
Actual sale/price/cash callback coverage still requires the next live capture.

Release diagnostics build: zero warnings/errors.
Tests/Verify-SupplyCapture.ps1: installed target patching, dispatch/load preflight,
getter-count/result/exception preservation using executable fixture, decision
classifier, 15-day/wall/byte limits and durable/idempotent closure passed.
Tests/test_supply_analysis.py: transfer double-count protection, trip timing,
missing evidence, residual and open-scope rejection passed.
Existing Verify-EconomyTransactions and Verify-SoakDiagnostics passed.
Protected political baseline passed. The formerly unresolved broad checks were
subsequently corrected and both pass: CalendarMath now uses the hash-bound
Protected560 schema-5 contract, and StrategicMapCoverage retains exhaustive pixel
checks in compiled test code with a properly scoped composer assertion. See
docs/BROAD_VERIFICATION_CONTRACTS.md at the project root for the diagnosis and
negative-test coverage. No full release approval or live shipment/overhead
acceptance is claimed.

Remaining runtime acceptance: exercise real production, loading, affordable and
cash-limited sales, return trips, blocked routes and a reload; verify adequate
coverage and observer overhead on the actual mod stack. Only then decide whether
the supply candidate needs another gameplay change.
