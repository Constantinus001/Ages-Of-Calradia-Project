# Naval valuation follow-up — 2026-09-27

Status: read-only investigation. No gameplay policy, settings, binaries, saves,
or deployment changed. This is not balance certification.

## Evidence boundary

- Capture: `AocFramework-current.tsv`, session
  `2d6e13b1666248b9953ead00fb6b515d`, campaign `gHBJvLViLE7O`.
- Campaign interval: 396013.39322577778 to 396029.39346122456,
  approximately 16 days. Other sessions excluded.
- Existing reconciled report:
  `output/diagnostics/combined-2d6e13b1/report.json`.
- Inspected installed native IL using Windows PowerShell and Harmony 2.4.2.
  CampaignSystem MVID: `886629fe-6e60-40d7-9a57-8d46017179d9`.
  NavalDLC MVID: `b60485da-883c-4a91-b58c-040702fe3ce9`.
- Original method IL establishes native rules, not universal compatibility
  with arbitrary third-party patches. Observed final values and wallet changes
  independently establish the measured session behavior.

## Findings

### 1. Pirate ships dominate observed battle-ship monetization

All 102 distinct evaluated ships were first observed in battle snapshots with
32 northern/southern pirate parties: 23 northern ships and 79 southern ships.
Their evaluated values total 1,908,750 gold. The allocation report records
1,908,738 gold distributed, with native per-winner flooring present in IL.
The 12-gold difference is consistent with rounding; it is not an additional
payment and has not been individually recomputed here.

For 61 ships, a `none -> pirate party` ownership event precedes valuation
within this capture. Their values total 1,159,500. The other 41 have no such
observed assignment. Absence is not proof of their creation history.
None of the 102 has an observed settlement-to-party ownership transfer in
this session. Thus an AI purchase/resale cycle is NOT the demonstrated origin
of these specific battle payouts.

Previously verified native destruction completion and cleared ownership cover
all 102 valued ships. This does not indicate repeated sale of the same ship.

### 2. Native party-template creation does not purchase its ships

`MobileParty.InitializeMobilePartyWithPartyTemplate` calls
`PartySizeLimitModel.FindAppropriateInitialShipsForMobileParty` and assigns the
returned ships via `ChangeShipOwnerAction.ApplyByMobilePartyCreation`.
The NavalDLC model delegates this method to its base model.
`DefaultPartySizeLimitModel` constructs ships from template hull stacks and
the initial party-size ratio. The creation owner action uses detail 3;
`ChangeShipOwnerAction.ApplyInternal` executes its monetary trade block only
for detail 0. Therefore this native creation/assignment path has no ship
purchase debit. A none-to-party receipt alone does not prove which caller
created each specific ship.

### 3. AI purchase and liquidation use different hull price bases

`NavalDLCShipCostModel.GetShipTradeValue` enables its AI discount when the buyer
is a non-player mobile party and the seller is a settlement. In
`GetShipBaseValue`, that flag multiplies the hull component by 0.01.
Upgrade valuation has separate player/AI, perk and policy rules and must not
be treated as an identical whole-price multiplier.

Buyerless liquidation calls use buyer=null, so the hull discount is false.
The base value is multiplied by 1.5. The normal mobile-to-settlement sale
branch instead multiplies trade value by 0.3 and deducts repair cost.
`GetShipSellingPenalty` returns 0.3, but battle and recovery callers apply it
only to player-clan rewards.

For the hull component alone, ignoring upgrades/policies/repair/rounding:

| Path | Hull contribution for hull value H |
| --- | ---: |
| AI purchase from settlement | 0.015 H |
| Buyerless liquidation before player penalty | 1.5 H |
| Ordinary settlement resale before repair deduction | 0.45 H |

These are formula comparisons, not an assertion that every ship realizes a
100x or 30x round-trip profit. A player-equivalent 30% liquidation factor still
does not reconcile the AI purchase and liquidation hull bases.

### 4. The capture contains separate swap-sale proceeds and perk grants

Reused canonical wallet sources for the stable noble-clan cohort (97 clan
groups, including the player/minor clans, 687 hero wallets). Do not add gross
transfer endpoint records or nested reward scopes to these net observations.

| Ship-trade caller group | Incoming | Outgoing |
| --- | ---: | ---: |
| TryPurchasingShipFromTown ancestry | 94,425 | 19,090 |
| OnShipOwnerChanged ancestry | 9,000 | 0 |
| OnShipRepaired ancestry | 960 | 0 |
| Total | 104,385 | 19,090 |

`TryPurchasingShipFromTown` can trade an existing ship to the town before
buying its replacement. Its positive wallet entries therefore must not all
be labelled purchase refunds or unexplained grants. This aggregate is not
a same-hull, same-condition, same-party round-trip profitability measurement.

`OnShipOwnerChanged` separately pays a town governor using the MerchantPrince
perk through `GiveGoldAction.ApplyBetweenCharacters(null, governor, amount)`.
The repair caller is separately identified above; its complete perk formula
was not audited in this follow-up. These smaller sources must not be confused
with the 1.91-million battle-ship valuation total.

## Revised recommendation, not implemented

1. Prioritize AI monetization of defeated pirate ships. Preserve native ship
   allocation, contribution shares and destruction. Choose an explicit reward
   budget or salvage policy, rather than cutting all clan income.
2. Separately reconcile AI ship valuation across purchase, swap/resale and
   buyerless recovery. A blanket `GetShipTradeValue` multiplier risks changing
   purchases, player interactions and genuine trade together. Preserve the
   distinction between hull, upgrades, repair deductions and perk grants.
3. Keep player rewards and actual transfers unchanged unless separately
   authorized. Do not reduce pirate fleet size merely to hide a gold issue.
4. Before implementation, replay measured reward distributions and test the
   proposed formula offline across player/AI, mixed winners, damaged ships,
   upgrades, normal sales and recovery. Define handling of negative/invalid
   values and exact rounding. New sidecar targets need compatibility guards;
   protected Core stays immutable.

A flat 30% factor would reduce the 1,908,750 evaluated amount to 572,625,
a reduction of 1,336,125 before allocation rounding, if the same events occur.
This is arithmetic sensitivity, not a campaign simulation or a prediction of
cohort net wealth. It neither establishes the best factor nor retrospectively
explains the historical 28.19-million increase.

## Remaining limitations

- Exact long-term target wealth and the resulting reward budget are not yet
  established. No optimal multiplier is claimed.
- No ship purchase cost basis exists for the 102 battle ships in this window.
- Template-spawn cadence has not yet been audited against the extended year.
- Same-ship purchase/resale histories, retained captured ships, fleet replacement
  behavior and future demand feedback require separate reasoning; they cannot
  be inferred from aggregate credits minus debits alone.
- No fresh user-run test was necessary for these findings. Future changed
  gameplay would still require focused acceptance, not a claim of perfection
  from static IL inspection.

## Second deep audit: replenishment, calendar exposure and correction safety

Same installed native assemblies and completed session. This follow-up makes
no runtime changes. The protected Core hash was rechecked and remains
`560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`.

### Pirate replenishment is a deficit controller, not an annual timer

Inspected `PiratesCampaignBehavior.DailyTickClan`, `TrySpawnPirateParties`,
`GetPirateData`, `SpawnPirateParty`, `GetRandomSuitableZone`,
`CanSpawnPiratePartyInZone`, `DailyTickParty`, and `TryRemoveWeakPirate`.

- DailyTickClan checks IsPirateClan, then attempts replenishment.
- Current count is the clan's WarPartyComponents.Count, NOT total troop count,
  despite the local parameter name pirateMemberCount.
- For naval clans, NavalDLCBanditDensityModel obtains the maximum from the
  count of map-scene spawn points tagged with that clan's StringId. Story-mode
  player-interaction restrictions can return zero. The actual loaded map's
  spawn-point count was not measured here.
- The native attempt count is floor(pow(maximumCount - currentCount, 0.66)).
  Positive attempts still need a suitable patrol zone. This formula is quoted
  for nonnegative deficits; behavior for an over-cap negative input is not
  established as a desired policy.
- SpawnPirateParty uses the clan's DefaultPartyTemplate through
  BanditPartyComponent.CreateLooterParty, then initializes trade, food and
  patrol behavior. This connects pirate spawning to the previously inspected
  native template fleet creation path.
- Daily weak-party cleanup can disband a pirate whose healthy troop count or
  ship count falls below 70% of its corresponding template threshold. It is
  guarded by naval capability, no active map event, and distance from the
  player (outside twice the player's seeing range). These removals can create
  replenishment demand independently of monetized battle losses.

The reconciled report's observed caller deltas contain 54 pirate trade-wallet
initialization observations through DailyTickClan -> TrySpawnPirateParties ->
SpawnPirateParty -> InitializePirateParty -> CreatePartyTrade, totaling
29,255 gold. This is evidence that the spawn path executed, not 54 measured
ship sales or a complete world-population census. It must not be added to
noble wealth as though already transferred to noble wallets.

### Shipyards and AI buying have independent daily opportunities

Inspected ShipProductionCampaignBehavior.DailyTickTown, CreateShip,
GetMaxShipCountForTown, GetIdealShipCountForTown, plus
ShipTradeCampaignBehavior.GetClanShipPurchaseChance and OnShipRepaired.

- Native port production starts with a 0.5 daily probability, modified by the
  StreamlinedOperations perk, and is blocked by siege/map-event/non-port
  conditions. When below the building-derived maximum, a successful roll can
  attempt CreateShip up to ten times, stopping if stock reaches the maximum.
  Failure to select an eligible hull can produce no ship. Ten is an attempt
  ceiling, not ten guaranteed ships every day.
- Ideal inventory is max(maximum - 2, 0); excess-stock cleanup is separate.
- CreateShip constructs a hull, can equip available upgrade pieces, assigns
  it by production and emits OnShipCreated. This method has no direct raw
  material or treasury debit. Its event subscribers were not exhaustively
  audited, so this is not proof of zero indirect production cost everywhere.
- AI purchase consideration uses a native probability of 0.5 per daily clan
  tick; successful purchases still require eligibility, affordability, a
  suitable town and improved fleet composition. A chance roll is not a sale.
- The 960-gold repair-related credits identified earlier have a concrete
  native source: OnShipRepaired applies the MasterShipwright town perk and
  pays the governor through a null-giver GiveGoldAction. The 9,000 trade-perk
  credits remain distinct MerchantPrince payments.

### Calendar verdict and limits

The source calendar contract is 365 common-year days versus 84 native days.
The inspected native pirate/shipyard/purchase methods contain no annual-rate
conversion. Focused source searches and the approved Core IL text found no
named pirate-spawn adjustment. This is not a complete live Harmony registry
audit and must not be presented as one.

There are 365/84 = 4.345238 times as many daily opportunities per common year.
For an always-eligible 0.5 purchase roll, expected successful rolls would be
42 versus 182.5. Actual purchases, pirate populations and reward amounts do
NOT necessarily grow by that factor: stock caps, deficits, losses, routes,
affordability and AI decisions constrain them.

Naively multiplying the integer pirate attempt count by 84/365 and flooring
would turn deficits of 1, 2, 4 and 10 into zero attempts (native attempts are
1, 1, 2 and 4). Such a patch could prevent small deficits from ever refilling.
If annualized replenishment is later desired, it needs an explicit scheduling
or fractional-carry policy, including persistence/reload behavior where
applicable. It must not be smuggled into a money-payout correction.

### Wealth concentration does not justify a uniform income cut

Recomputed from the existing report, using each stable clan group's canonical
hero wallet IDs and net observed source flows:

| Clan ID | Net wallet change | CommitGoldChanges credits/net |
| --- | ---: | ---: |
| clan_empire_south_2 | 162,117 | 146,186 |
| clan_empire_west_1 | 134,871 | 197,306 |
| clan_empire_south_3 | 134,474 | 156,741 |
| clan_empire_south_4 | 132,970 | 135,599 |
| clan_aserai_1 | 112,447 | 148,821 |

All 97 groups total +1,533,544 net, with 2,039,097 from CommitGoldChanges and
162,176 from separate remaining-ship recovery. CommitGoldChanges includes
non-ship battle gold. The all-recipient ship valuation total is not automatically
the identical cohort, so subtracting all 1,908,750 from the clan total would
not be a validated counterfactual. Operating expenses and other income still
matter, and historical 28.19-million causality remains a separate limitation.

### Safer correction boundaries

1. Retain native fleet availability and pirate population rules for the first
   money correction. Calendar-aware spawning is a separate gameplay choice,
   not a necessary prerequisite to closing the demonstrated payout issue.
2. Preserve the defeated ship owner before allocation/destruction. In
   MapEvent.LootDefeatedPartyShips, GetShipTradeValue receives the winning
   leader party as seller and null as buyer. Testing that seller for pirate
   identity would target the wrong party.
3. Apply any AI-only adjustment to the intended recipient's ship component,
   not all battle gold or the shared pool indiscriminately. Preserve existing
   player-clan handling. Native eligible winners exclude MainParty, bandits,
   caravans and nonpositive contributions; other player-clan parties still
   make mixed-recipient behavior important.
4. Keep normal AI purchase/resale normalization separate from pirate salvage
   rewards and from remaining-ship recovery. A null buyer alone does not mean
   a pirate ship. Do not classify all liquidated ships as originally free.
5. Before implementation, cover mixed AI/player recipients, ownership changes,
   original-body skips, exceptions, integer rounding, damaged/zero-value ships,
   upgrades, native perk changes, and repeated disband/destroy callbacks in
   isolated native-compatible tests. Preserve the existing duplicate guard.

Verdict: the evidence supports a focused naval cash-conversion correction.
It does not support nerfing every clan income stream, multiplying every naval
rate by 84/365, or claiming a 30% factor alone provides coherent valuation.
No additional user-run soak was needed for this audit. No implementation,
deployment or new runtime acceptance is claimed.

## Third audit: bypasses, price components and failure safety

### A battle-only adjustment does not cover retained loot

Native battle distribution can transfer a captured ship instead of liquidating
it. The normal trade path can later sell that asset, and native party removal
can transfer it to another clan party or recover its remaining value.
`DistributePartyShipsAndRecoverGold` calls `DistributeShips` BEFORE
`RecoverGoldFromRemainingShipsAfterDistribution`. Only remaining ships belong
in the latter payment, not every ship seen when distribution starts.

`OnPartyDisbanded` excludes bandit clans. `OnMobilePartyDestroyed` requires an
actual clan and a party that is not currently at sea. These predicates differ;
do not collapse them into a single generic destruction hook.

Thus a pirate-specific immediate-battle policy is a partial correction, not
complete lifetime valuation. Two designs are possible:

- A coherent AI cash-out valuation policy across battle, ordinary resale and
  remaining-ship recovery avoids needing a persistent pirate-origin tag, but
  deliberately affects legitimate AI-owned ships too. That scope must be
  explicit, with player and real-transfer paths preserved.
- A pirate-origin-only lifetime policy needs persistent provenance, transfer
  handling and an honest unknown-origin rule for existing saves. An in-memory
  tag alone would lose its meaning after reload. No such persistence exists
  in the proposed immediate-battle correction.

Recommended first implementation boundary: explicitly configured AI cash-out
valuation, with branch-specific adapters and one component-based policy.
Do not advertise it as pirate-only if it changes other AI ships. Leave fleet
spawn cadence and player prices unchanged. Exact tuning is still unresolved.

### Whole-quote multiplication would mishandle repair costs

Inspected the installed `NavalDLCShipCostModel.GetShipRepairCost`. Its damage
fraction is `(MaxHitPoints - HitPoints) / MaxHitPoints`. The base-value call
already enables the AI hull discount when the owner is not in the player
clan. Repair cost is that base times damage fraction times 0.25, with relevant
perk adjustments. Ordinary settlement resale subtracts this repair quote.

Hypothetical hull-only arithmetic, not a recorded transaction or final tuning:

| Input or result | Gold |
| --- | ---: |
| Hull value, 50% damage, no upgrades/perks/policies | 10,000 |
| Native AI hull purchase component | 150 |
| Native AI repair deduction | 12.5 |
| Native settlement sale quote | 4,487.5 |
| Incorrect whole sale quote multiplied by 0.01 | 44.875 |
| Sale using discounted hull basis, retaining repair deduction | 32.5 |

The difference is real algebra: applying 0.01 to the final quote also discounts
the already-discounted repair subtraction. Upgrades have additional native
rules and are another reason not to treat 0.01 as a universal price multiplier.
Validate finiteness, nonnegative permitted values, native integer conversion,
and zero/max-HP boundaries before side effects. Do not silently recompute a
model getter multiple times or turn missing decomposition into a zero payout.

### Duplicate guard: successful-path coverage versus partial failure

Reviewed `NavalDuplicateRewardGuard`. It remembers completed distribution by
party object identity, rejects reentry, releases a newly acquired gate when
another prefix skips the original, and releases the gate on a native exception
so the native flow may retry. This is NOT a transactional rollback of money
or ownership already changed before an exception.

The existing exception fixture throws before a monetary side effect. Therefore
it does not establish exactly-once payment after an exception that follows a
partial credit. No such live failure was established by this audit. This is a
failure-case coverage gap, not evidence of current duplicate payments.

The new valuation policy should compute and validate amounts before native
payment, avoid corrective give/take transactions afterward, and add an explicit
partial-side-effect failure fixture. Do not weaken or replace the existing
guard merely to make a new adapter easier to install. An at-most-once gate
and transaction rollback are different contracts.

### Fresh verification results

Three existing suites were rerun in isolated Windows PowerShell processes:

1. `Verify-NavalPatchCoexistence.ps1`: PASS for both observer/guard priorities,
   competing-prefix skip, exception preservation and retry.
2. `Verify-BattleAllocation.ps1`: PASS for native target binding, nested,
   skipped/error scopes, stable joins and one-time model observations.
3. `Verify-ShipLifecycle.ps1`: PASS for native removal/ownership bindings,
   cleanup call presence, stable identity and callback failure handling.

These runs used installed diagnostics SHA-256
`E90A917324E20AC3AA0438614D400A4B48FAD3C20D47A678C30A3B0708B7BF52`.
The coexistence fixture used repository framework SHA-256
`F1E24E84C6A117EB7240906C68CFB9DA8C6CF34F2BFC132149B1FD356B4E623D`,
matching the previously recorded deployed candidate identity. An initial
coexistence run also passed against the repository diagnostics binary; the
installed-binary runs above are the relevant explicit verification.
Fixture receipts are isolated in temporary `aoc-supply-verifier-*` directories,
not the campaign log. The legacy out-int corruption line is the fixture's
intentional reproduction; subsequent current-observer checks passed.

The existing campaign report still records zero player naval scopes and zero
observed selling penalties in recovery. The three passes do not fill that live
coverage gap and do not exercise a new balancing implementation (none exists).
Both protected Core and WorldCalendar hashes remain unchanged and approved.

### Decision after three audits

Proceed toward one coherent naval cash-out correction, not a blanket clan
income nerf and not a pirate-spawn throttle. Before coding, settle the explicit
AI valuation scope and configuration defaults. Before gameplay acceptance,
verify all three cash-out adapters together, retained-ship transfers, mixed
player/AI winners, damaged/upgraded ships, normal buying, and partial failures.
Existing saves and unknown ship provenance must not be guessed away.

Only this audit document changed. No gameplay code, configuration, binaries,
saved games, or live process state was modified. No Release build was needed
for this documentation-only investigation, and no deployment occurred.

## Audits four through six and public-source research

These are three additional reviews by the same investigator, not three
independent reviewers. They refine the design; they do not constitute an
implemented or certified balance fix.

### Audit 4 — challenge the economic policy

**Question:** should the fix remove AI purchase subsidies, reduce all income,
scale all naval rates, or change conversion of ships into cash?

The installed code and measured flows favor the last option. The AI purchase
discount is a native rule, not an AOC arithmetic error. Raising purchase costs
to player prices would change fleet affordability and is not justified by the
cash-out evidence. Pirate population and port stock are bounded controllers;
annualizing them is not a substitute for coherent pricing.

The preferred candidate is **subsidy-aware AI cash-out valuation**: derive the
AI hull/upgrade basis consistently with the native AI replacement economy,
then apply the appropriate path's resale/recovery rule, repair treatment and
integer conversions exactly once. Preserve AI purchases, player-clan prices,
actual ship transfers, fleet composition and spawn rules initially. Keep
battle, ordinary settlement resale and remaining-ship recovery as distinct
adapters to one pure valuation policy. Do not put a universal multiplier on
GetShipTradeValue or GiveGoldAction.

This is a current-value rule, not historical cost accounting. It deliberately
applies to eligible AI ships regardless of unknown origin, avoiding a new
save-persistent pirate provenance system. It reprices future cash-outs; it
does not confiscate existing gold or rewrite old transactions. Ordinary trades
involving player parties, caravan/villager flows, non-ship battle loot and
treasury transfers need explicit exclusion contracts rather than assumptions.

Remaining economic caveats:

- No specific factor is proven optimal. Applying an AI-consistent hull basis
  can cut cash-outs much more than the earlier suggested 30% of full value.
  Fixed-event arithmetic cannot predict later recruitment, purchases or wars.
- Governor trade/repair perks are separate grants. Coherent asset prices do
  not eliminate every possible net subsidy. Current evidence identifies
  9,000 and 960 respectively, not a need to remove them.
- A captured asset has value even if spawned without a cash purchase. Null
  payer and destroyed asset are not proof of duplication. This is an intentional
  balance-policy correction justified by measured reward dominance.
- Do not subtract the all-recipient valuation total from a different clan
  cohort and call the result a simulated post-fix economy.

### Audit 5 — integration, configuration and rollback

**New concrete finding:** ProcurementSettings.Parse requires exactly one child
section named Procurement in CampaignSystems schema 1. A fresh execution of
the real parser with Procurement plus NavalEconomy rejected it with:
`Expected CampaignSystems schema 1 and one Procurement section`.
CoreSystemsSubModule records the configuration rejection; the current contract
blocks new procurement orders while retaining saved obligations. Simply adding
naval configuration beside Procurement would therefore break another system.

Refinement:

1. Keep existing procurement XML, revision and semantics intact. Recommended
   first boundary is a separate versioned NavalEconomy configuration document,
   still loaded and owned by the framework companion. A future unified schema
   needs an explicit backward-compatible migration; do not sneak one into
   this correction.
2. Missing naval configuration means native behavior. Invalid naval settings
   disable only this optional feature with a precise reason; procurement stays
   governed by its own existing configuration validity. Read an immutable
   policy snapshot at campaign start, not during each payment. Record its
   own revision without changing the procurement policy fingerprint.
3. Preflight all required targets and instruction contracts before enabling
   the three adapters. If installation fails partway, remove only this feature's
   patches, preserve the existing duplicate guard, and report native fallback.
   Do not silently run a battle-only correction while advertising full coverage.
4. Detect overlapping Harmony owners. Known observer patches are not economic
   conflicts; unknown price-changing patches require explicit compatibility
   handling, not automatic unpatching of another mod.
5. Prepare valuation before native side effects. Do not debit an overpayment
   afterward or retry an entire original method following partial execution.
   Restore nested context on original skips and exceptions; preserve exceptions.
6. No new ship-provenance save field is required by this candidate. Reset
   transient context on campaign end/start. Existing saves keep their ships
   and gold. Rollback restores native future pricing but cannot undo payments
   already made under a different policy. Restore any explicitly changed
   naval config along with its binary rollback.

These are proposed contracts, not new classes/files/settings already shipped.
The approved Core and WorldCalendar remain immutable.

### Audit 6 — acceptance coverage and false-positive prevention

Reviewed Analyze-BattleAllocations.py and Analyze-RewardAccounting.py against
their existing fixtures. The battle analyzer checks before/after allocations
and their joins to commit inputs; it does not independently calculate a correct
price from hull, upgrades, damage, rules and contribution shares. Reward
accounting checks requests against cash movement. A consistently wrong price
could pass both. These checks are useful but insufficient alone.

Refined verification gates:

| Gate | Required proof |
| --- | --- |
| Pure policy | Independently expected hull/upgrade/repair decomposition, finite values, zero/full/partial damage, range limits, rounding boundaries and no second repair discount |
| Native adapters | All three cash-out routes, retained/transferred ships, original skips, nested calls, native/competing exceptions, and no repeated model getter evaluation |
| Player isolation | MainParty exclusion, other player-clan parties, mixed winners, and unchanged player-native reward/penalty ordering |
| Configuration | Old procurement XML unchanged, missing/invalid naval settings, snapshot revision, reload/end reset and partial-install rollback |
| Accounting | Original valuation versus effective policy decision, recipient allocation, requested payment, actual wallet delta, transfer/destruction disposition; nested amounts counted once |
| Failure cases | Failure before payment and after a synthetic partial payment, with no invented transaction rollback or claim of exactly-once behavior |

New policy evidence should have its own documented schema and analyzer support.
Adding an unknown `BATTLE_ALLOCATION_*` event today would be rejected by the
existing analyzer. Conversely, letting new events be silently ignored must not
produce a policy-verification PASS. Legacy sessions should remain readable and
state policy coverage unavailable, not be retroactively certified.

After offline readiness, use one planned acceptance run with explicit coverage
targets for the changed routes. Do not promise that elapsed days alone will
exercise mixed-player battles or all recovery cases. A missing branch remains
a coverage gap; do not invent a requirement to restart a completed campaign
just to repeat already-established accounting. New live validation is still
necessary before claiming a new implementation works in game.

Fresh checks in this turn:

- FrameworkVerifier: 67 assertions passed, combined candidate XML revision
  unchanged (`45D53196034FF51BEB0CEF73A7EC69612123F7F6C6385FE489CAB2E9690B55A3`).
- Current parser's extra-section rejection reproduced using the actual type.
- Analyzer unit tests: 14 battle allocation + 15 reward accounting + 4 lifecycle
  coverage tests passed (33 total).
- Verify-RewardObservation.ps1 passed against installed diagnostics
  `E90A917324E20AC3AA0438614D400A4B48FAD3C20D47A678C30A3B0708B7BF52`.
- Verify-NavalDuplicateRewardGuard.ps1 passed against the existing repository
  framework assembly. Both callback targets and first-only/failure-retry state
  were checked. These do not fill the partial-payment failure gap.

No new implementation exists for the proposed valuation policy, so none of
these results is a passing test of that future policy.

### Public research and alternative selection

- [TaleWorlds War Sails Q&A #3](https://www.taleworlds.com/en/News/579): the
  official search excerpt describes fleet composition choices that avoid
  materially burdening AI clan treasuries. Full-page fetching returned 403.
  This supports preserving affordability as a design goal; it does not prove
  that the exact 1% number or every payout asymmetry was intentionally balanced.
- [Naval DLC Balance Fix author documentation](https://www.nexusmods.com/mountandblade2bannerlord/mods/9632?tab=docs):
  the author describes configurable buying/selling/repair/upgrade multipliers
  and explicitly says AI factions are unaffected. Therefore it is not a direct
  solution to this AI-clan issue. The source code was not verified, no mod was
  installed, and its compatibility claims are not adopted as evidence.
- [Harmony prefix documentation](https://harmony.pardeike.net/v2/articles/patching-prefix.html):
  an original-body skip does not skip postfixes/finalizers, and narrow changes
  are preferred to replacing an entire original implementation.
- [Harmony finalizer documentation](https://harmony.pardeike.net/v2/articles/patching-finalizer.html):
  finalizers wrap originals and patches, support cleanup, and can preserve or
  suppress exceptions. They are not automatic rollbacks of game state.

Installed v1.4.8 IL and this campaign's receipts remain the authority for the
version-specific findings. Public material does not establish an optimal
multiplier or retrospectively attribute the earlier 28.19-million increase.

**Best current path:** retain the AI's inexpensive fleet replacement; correct
the inconsistent cash-out basis across the three identified routes; keep the
policy configurable and optional behind one compatibility gate; validate the
formula separately from payment reconciliation. Reject global income cuts,
player-only price mods and universal calendar scaling as solutions to this
specific demonstrated problem. Exact tuning and gameplay acceptance remain
open; six audits do not make either disappear.

## Research decision: source correction versus economy-wide controls

Additional primary-author research on 2026-09-27 does not establish a better
direct replacement for the three-route correction above:

- [Living Economy](https://www.nexusmods.com/mountandblade2bannerlord/mods/10796)
  describes soft AI wealth limits, partial removal of large windfalls, gradual
  reduction of old hoards, and editable XML. Its compatibility documentation
  also describes yielding ownership of overlapping economic systems. These are
  author claims, not source-verified behavior or compatibility certification
  for AOC. Explicit domain ownership and editable policy are useful design
  lessons; removing accumulated wealth is not the first fix for AOC's measured
  ship valuation asymmetry. It would obscure whether the source was corrected.
- [TAOM economy diagnostics](https://raw.githubusercontent.com/haterade22/TAOM/bannerlord-1.4.5/docs/features/economy-diagnostics.md)
  documents flow-specific town ledgers and silent caravan failure paths. The
  useful lesson is to measure actual drains, receipts and failed decisions,
  rather than treating a larger cash target as a cure. Its branch-specific
  findings are not evidence that AOC has the same caravan problem.
- [Banner Kings Redux shipping documentation](https://raw.githubusercontent.com/GIO443/bannerlord-banner-kings-redux/main/docs/wiki/Shipping-and-Trade.md)
  reports both a disabled shipping graph under investigation and explicit
  boundaries around native naval convoy ownership. This supports keeping a
  valuation correction separate from routing and spawning; it is not a reason
  to import a shipping overhaul into this fix.

### Chosen path and boundaries

1. Implement a pure, configurable AI cash-out policy in the unprotected
   framework companion, with separate battle, sale and recovery adapters.
   Preserve native purchase affordability, player-clan outcomes and physical
   transfers. Do not add a global gold multiplier or confiscate existing gold.
2. Determine hull, upgrade and repair components separately. The native 1%
   hull purchase basis is an observed reference point, not a proven optimal
   balance default. A blanket 30% payout or whole-quote 1% multiplier is not an
   equivalent correction. Retain native rounding order where applicable.
3. Before choosing production defaults, run offline counterfactual calculations
   on captured transactions whose component data is sufficient. Report missing
   components explicitly. Do not estimate a new campaign net balance simply
   by subtracting a scaled gross ship reward: future behavior and recipient
   cohorts can differ. Existing gold-only receipts cannot validate every price
   component or historical acquisition cost.
4. Require all three adapter contracts, mixed-player isolation, configuration
   compatibility, failure handling and accounting checks before deployment.
   Record native value, effective value, reason, recipient, actual wallet delta
   and ship disposition with the policy revision. Keep unresolved attribution
   separate from successful payment reconciliation.
5. Then perform one planned acceptance run for this combined implementation.
   Offline readiness is not live acceptance; elapsed campaign days alone do
   not guarantee sale, recovery or mixed-player branch coverage. Do not ask
   for another unchanged soak merely to restate evidence already collected.

This is the strongest currently supported solution to the identified naval
cash-out problem, not proof that all noble wealth growth is explained. The
historical 28.19-million increase still lacks sufficient source attribution.
No external mod was installed, no gameplay implementation was changed, and no
new runtime validation was performed for this research addendum.

## Implementation follow-up (2026-09-27)

The subsequent implementation request produced an opt-in framework companion
candidate. See [native contracts, configuration and verification](../Modules/AgesOfCalradiaCampaignSystems/NAVAL_CASHOUT.md).
All three routes share hull-basis normalization. Native upgrade pricing is
retained, including owner-context rules in mixed battles, rather than applying
a second whole-quote discount. Player-clan allocations retain the native pool.
Separate policy diagnostics and arithmetic analysis were added; settings and
gameplay files were not deployed or activated. The research sections above
describe evidence available before implementation, not a live validation of
this candidate.

## Five-part hardening follow-up (2026-09-28)

Implemented operation-linked payment/lifecycle reconciliation, idle late-patch
compatibility checks, stronger native battle/payment/recovery fixtures, read-only
tuning comparisons and a combined acceptance report. Details and remaining live
gates are in the [candidate contract](../Modules/AgesOfCalradiaCampaignSystems/NAVAL_CASHOUT.md).
The full offline candidate suite passed. No settings were tuned, no game files
were deployed, and these checks are not evidence of a new live campaign run.
