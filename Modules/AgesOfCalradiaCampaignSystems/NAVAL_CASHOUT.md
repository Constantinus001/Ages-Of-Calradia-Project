# Naval cash-out candidate

Implemented 2026-09-27 in the unprotected framework companion. This is an
opt-in implementation candidate, not a deployed or live-certified balance fix.
Protected Core and WorldCalendar assets are not build inputs or outputs.

## Boundary and formula

`NavalCashoutPolicy` owns pure arithmetic. `NavalCashoutSettings` owns an
immutable campaign-start settings snapshot. `NavalCashoutRuntime` adapts native
quotes and allocation scopes. `NavalCashoutPatches` owns native contracts and
installation. Optional diagnostics subscribe without owning gameplay.

Correct the missing AI **hull** subsidy on cash-out, not the entire price:

```
effectiveBase = (nativeBase - hullValue) + hullValue * HullBasis
```

Already-discounted native bases are untouched. Native upgrade-slot maxima,
per-piece AI discounts, rounding, and owner perks/policies are retained. This
deliberately does not reprice upgrades using an invented counterfactual owner
in mixed battles. That differs from a blanket full-price multiplier and avoids
discounting already discounted upgrade values twice. The chosen policy fixes
the demonstrated hull asymmetry; it is not historical acquisition-cost tracking.

Native trade code applies its remaining policy modifiers, sale multiplier and
repair subtraction once. Negative eligible resale quotes clamp to zero, avoiding
a reversed payment on damaged ships. Purchase quotes are never normalized.

Battle liquidation retains the native quote/pool for native eligibility and
player-clan allocation. A shadow pool adds each corrected quote after native-style
integer truncation. Only eligible AI recipients use that pool, with the original
float contribution fraction and floor ordering. Existing non-ship plundered gold
is preserved. No payment is reversed or reissued. Recovery changes quotes only
inside the native remaining-ship recovery scope; physical distribution stays native.

## Settings and activation

The separate file is `Configs/AgesOfCalradia/NavalEconomy.xml` beneath the
Bannerlord Documents directory. Do not add it to `CampaignSystems.xml`.
`NavalEconomy.candidate.xml` is an example, not automatically activated.

- Schema 1: required `Enabled` boolean and finite `HullBasis` in [0.01, 1].
- Missing file or disabled flag: native behavior.
- Malformed settings: naval policy rejected; procurement remains independent.
- Candidate 0.01 matches the observed native AI hull purchase basis, not a
  proven optimal economy-wide tuning value. Factor 1 retains full hull value;
  enabled resale still has the explicit nonnegative clamp.
- One snapshot per campaign start, with canonical revision. No hot reload,
  acquisition history, save schema changes or retrospective wealth removal.
- `aoc.systems_status` includes the naval installation state/rejection reason.

## Native contracts and risks

Pinned to the inspected CampaignSystem and NavalDLC v1.4.8 MVIDs and six method
IL hashes. A game update requires a fresh native audit, not relaxing the gate.

| Target | Patch and purpose |
| --- | --- |
| NavalDLCShipCostModel.GetShipTradeValue | Scope prefix/finalizer, result postfix, one-call transpiler replacing its static GetShipBaseValue call with a delegate wrapper; preserve native modifiers and repair execution |
| MapEvent.LootDefeatedPartyShips | Scope prefix/finalizer; replace its one GetShipTradeValue call and one PlunderedGold setter; load hash-bound local 11 (native contribution sum) for AI allocation |
| NavalShipDistributionCampaignBehavior.RecoverGoldFromRemainingShipsAfterDistribution | Prefix/finalizer mark only remaining-ship recovery quotes, restore nested state and preserve original exceptions |

Both transforms are preflighted before installation. Settings publish only
after all three patches succeed. Startup failures remove only this policy's
touched patches, not diagnostics or the duplicate-reward guard. Unknown patch
owners on audited targets and foreign transpilers reject activation. The two
existing AOC diagnostic observer owners are permitted for non-transpiler taps.
Idle application ticks now recheck the six audited methods, each installed
policy binding and the active model type at most once per wall second. A late
conflict latches this policy off until the next campaign and emits a rejection
receipt, leaving other owners intact. Open policy scopes defer teardown.
Polling is not an atomic lock against repatching during an operation, so this
is not a universal compatibility claim. An overridden cost-model type is not
silently priced as native. Unsupported battle quotes invalidate the shadow pool
and preserve native allocation.

Scopes restore on exceptions and original skips. Existing duplicate-reward guard
semantics are unchanged: native partial payment followed by an exception is not
transactionally rolled back and is not certified exactly-once. This correction
does not add another retry, settlement payment, or post-payment debit.

## Diagnostics and verification

New `NAVAL_POLICY_STATUS` and `NAVAL_POLICY_DECISION` rows use `policy_v1` and
include revision, route, native/effective base, hull basis, returned quote and
allocation operands. The optional bridge uses existing canonical ship and
map-event-party identities. A quote is not a payment; existing reward, wallet
and ship-lifecycle observers remain necessary to verify actual consequences.

`Analyze-NavalPolicy.py`, included in `Analyze-FrameworkPeriods.py`, independently
checks hull normalization and allocation arithmetic, rejects unsupported records
and marks old sessions unavailable. It never labels missing wallet/player/
lifecycle evidence a PASS. No old live capture was reinterpreted as policy proof.

### Combined acceptance and tuning preparation (2026-09-28)

The optional `NavalSaleObserver` targets native
`ChangeShipOwnerAction.ApplyInternal(PartyBase, Ship, ShipOwnerChangeDetail)`
for trade operations only. Its prefix/finalizer assign an operation ID that
joins the quote made inside that call to canonical wallet mutations and owner
change receipts. The exact signature is checked during diagnostic installation;
this observer does not reprice, retry, suppress native exceptions or change
wallets. Observer exceptions fail the capture visibly. Finalizers restore nested
scope even if recording ends. Unsupported party fallbacks are not claimed as
hero-wallet evidence. Game updates require rechecking this native ordering.

`Analyze-NavalAcceptance.py <capture> --markdown` produces one read-only report,
also integrated into `Analyze-FrameworkPeriods.py`. It checks:

- Executed sales: quote, recipient delta, canonical mutations and ship transfer.
- Battle allocations: policy pool, native gold commit, wallet receipts, player
  preservation and disposal of quoted ships.
- Recovery: innermost reward scope, native float/rounding behavior, payment and
  disposal; outer scopes do not count the same payment twice.
- Partial failures, missing evidence, mixed sessions, live unfinished scopes,
  late compatibility rejection, dropped records and writer timing.

Battle reporting requires native payment eligibility and cleared allocations.
Ineligible patrols with verified zero cash effect count separately from payments;
unexpected cash, including offsetting mutations, remains a failure. Missing
eligibility/reset evidence or negative eligible inputs remains an explicit gap.
The September 28 replay reconciled 29 battle payments, one legitimate no-payment
commit, 17 sales/transfers and six recovery/cleanup operations. Mixed-player
coverage remains open. See `docs/NAVAL_ACCEPTANCE_INVESTIGATION_20260928.md`.

Writer timing is a lower bound, not total diagnostic overhead: it excludes
snapshot, stack and status work. Preview quotes never count as realized income.
The report exposes counterfactual base-component totals for reconciled operations
at several hull factors. These are tuning preparation, not predicted wallet
savings or a claim of economy-wide optimal pricing. No tuning values changed.

Verified locally:

- Release builds of framework and diagnostics: zero warnings/errors.
- `Verify-NavalCashout.ps1`: 69 assertions, real native trade pipeline with
  fixture base/repair inputs, real transpiler compilation, pure math/settings,
  mixed recipients, no duplicate getters, damage clamp, skipped originals,
  nested contexts, exceptions, competing patch rejection and owned teardown.
  The expanded fixture executes the patched native battle loop, native gold
  commit and remaining-ship recovery, including mixed player/AI recipients,
  transferred ships and a failure after crediting money. World/event endpoints
  and fixture wallets are stubbed; this is not live campaign acceptance.
- `Verify-Framework.ps1`: 67 assertions; procurement candidate revision unchanged.
- `Verify-NavalDuplicateRewardGuard.ps1`: passed.
- `Verify-NavalPolicyObservation.ps1`: real ABI, idempotent subscription,
  canonical ship join and unsubscribe passed in isolated temporary fixtures.
- `Verify-BattleAllocation.ps1` and `Verify-NavalPatchCoexistence.ps1`: passed.
- Python diagnostic suite: 194 tests passed, including 28 acceptance
  reconciliation tests and the eleven policy arithmetic tests.
- Protected political baseline verification: passed.

The full `Verify-Candidate.ps1` offline suite also passed, covering procurement,
calendar/protected baseline, logistics, lifecycle, capture readiness and observer
coexistence. Intentional malformed/locked fixture configuration errors were
rejected as expected, not runtime failures.

Remaining gates before live certification: release packaging, actual campaign
payments/cleanup and mixed-player gameplay, real mod-load-order compatibility,
total logging overhead and economic tuning. Native fixtures strengthen readiness
but cannot certify those live outcomes. No claim of optimal pricing or 10/10
release readiness is made.

One combined acceptance run should exercise battle liquidation, ordinary resale,
recovery after transfers, and a mixed-player case, with both policy decisions and
existing payment/lifecycle receipts. Days elapsed alone cannot guarantee coverage.
No game restart, save change, capture rearm, deployment or security scan was done
as part of implementing this candidate.
