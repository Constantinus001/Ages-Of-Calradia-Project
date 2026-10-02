# Wool, wine and noble wealth: targeted follow-up

## Scope and evidence

Read-only investigation of session `daeceb2d3b09444da48052c917489b33`.
The five-day TSV still matches SHA-256
`C07D71E385E01709A22203F88A689990D2F4BB78AAA9ADABA17C930B4CCCF4B1`.
See `ECONOMY_ACCEPTANCE_RESULTS_20260927.md` for its location and capture bounds.
Procurement text-log corroboration is restricted to 2026-09-27
17:51:37–17:58:07 UTC, the same loaded capture window; it is not mixed with
earlier sessions. Native IL was inspected from the installed CampaignSystem
assembly, MVID `886629fe-6e60-40d7-9a57-8d46017179d9`.

Evidence and exploratory read-only scripts are retained under
`output/diagnostics/causal-daeceb2d-20260927`. These are analysis artifacts,
not runtime patches or shipping analyzers. No gameplay code, settings, saves,
speed, installation or protected artifact changed. No build/deploy was needed.

## 1. The four losing wine presses

Names below are the installed SandBox settlement definitions. Stable IDs are
included because a mod/localization may alter display names.

| Town / workshop tag | Five-day operating result | Observed explanation |
| --- | ---: | --- |
| Vostrum (`town_ES2/workshop_3`) | -115 | Four input failures, no production, 115 expenses, no owner withdrawals. Four opening grapes were exported by other parties; subsequent daily snapshots had zero. |
| Ocs Hall (`town_V2/workshop_1`) | -115 | Three input failures, no production, 115 expenses. Its 67 opening grapes reconcile to 41 caravan exports, 24 other-party exports and two town consumption. |
| Jalmarys (`town_EW3/workshop_1`) | -115 | Three input failures, no production, 115 expenses. Its 23 opening grapes reconcile to 21 caravan exports and two town consumption. |
| Galend (`town_V5/workshop_3`) | -11 | One margin rejection, one input rejection, one successful batch. Output paid 118 less input cost 14 gives 104, minus 115 expenses gives -11. |

Galend's rejected quote offered 110 output income against a 119 native hurdle
(39 input cost plus 80 margin requirement). Its later successful quote was
118 against a 94 hurdle (14 input plus 80). This is a concrete quote constraint,
not proof that wine has no demand. Galend received 97 grapes through villager
sale scopes and six through caravan imports over the capture. Closing stock
was 93 after the recorded workshop/consumption/export flows.

The same-window procurement log repeatedly reports
`shortage_no_viable_supplier` for these towns. Capitals remained around
9,900–10,000, so their recorded failures were not capital-reserve rejections.
That generic supplier reason still does not distinguish distance, diplomacy,
food protection, available stock and other pre-offer filters.

### Strong policy constraint, with explicit limit

The installed framework's default policy revision exactly matches the runtime
receipt: `F16127D1321D19CAB3374E7F222E75B1DBFC2F9FBF79C9C9E7A599B175D93582`.
It uses three batches/order, a 100-unit food-category reserve, seven supplier
reserve batches, and a minimum supplier stock of ten.
Grapes are a food category in the native category definition. Procurement's
`HasSurplus` requires stock minus the order to meet the reserve, hence at
least 103 grapes for this three-grape order, potentially more for local demand.
The maximum town grape snapshot after each subsequent day was 52, 53, 43,
42 and 93. No sampled town qualified at those snapshots. Opening maximum
was 180; brief inter-snapshot replenishments are not ruled out.

Conclusion: export/consumption depleted local grapes; the conservative food
reserve severely constrains replacement procurement. Do not claim that the
100-unit floor was the sole failed filter at every decision without per-source
decision evidence. Do not solve this by gifting capital or boosting wine output.

## 2. Wool: working deliveries with reactive-order delay

Direct movement records contain **20 dispatches, eight arrivals and 24
consumption events** for wool. The terminal ledger retains 12 orders carrying
36 wool, all not yet due: arrival was 0.592–3.038 days beyond capture closure.
Their route times were 1.680–3.917 days. None was marked route-blocked in that
terminal ledger. These orders are not lost cargo.

Examples joining input failures and public stock/flows:

- Ortongard (`town_K4`): 12 input failures and six successful cycles. Public
  wool snapshots were 126, 0, 30, 29, 0, 0. Top-level caravan exports totaled
  184 units over the period; this exceeds opening stock because replenishment
  also occurred. Two pending orders remained at closure.
- Akkalat (`town_K2`): 12 input failures and six successes. Its 24 opening
  public wool was exported by caravans; two pending orders remained.
- `town_N4`: 13 failures and no successful wool recipe cycles. Public stock
  fell from 37 to zero at the daily snapshots; two pending orders remained.
- Varcheg, Sargot, Jaculan, Epicrotea, Amitatys and several other towns had
  repeated observed input failures with zero public wool at sampled times.
  Samples alone do not prove uninterrupted zero stock between observations.

The source confirms a reactive policy: `ProcurementPlanner.Find` skips recipes
while the public market still contains one complete batch; `DailyTown` also
skips new planning if an existing order is pending. Combined with multi-day
travel, this creates a predictable replenishment gap after stock exhaustion.
Existing arrivals and consumption show that the chain is operating, not that
every town has enough supply. Public stock, warehouse stock and private cargo
must remain separate in the analysis.

## 3. Noble wealth: transfers versus reward allocation

### Horse sales are matched transfers

All 486 selected horse-sale transfers into the tracked noble wallets have two
matching endpoints. Noble credits total 253,211 and town debits total 253,211;
the endpoint net is zero. These receipts explain movement of existing money
from towns to nobles, not creation of money. They do not by themselves certify
that the sale prices or trading behavior are balanced.

### Battle credits are real, but their origin needs an upstream split

The 203 selected battle-commit transfers credit nobles 421,500, each through
the native single-recipient/null-giver contract. Native `CommitGoldChanges`
pays `PlunderedGold`, separately debits `GoldLost`, then resets both allocation
fields. Thus null giver does not establish a grant or duplicate payment.
The three largest recorded credits were 74,661, 73,050 and 64,780; their
observed reward-wallet deltas equal their recorded plunder allocations, with
zero allocation residual in those calls.

Installed native IL identifies two distinct upstream allocation paths:

1. `CalculatePlunderedAndLostGoldAmounts` computes defeated-party loss and
   distributes that pot among winners.
2. `LootDefeatedPartyShips` can destroy ships, allocate their cash value by
   battle contribution, apply a player-clan selling penalty, and **add that
   amount to PlunderedGold** before the later payment.

Our current reward observer opens scopes around battle commitment and the
separate `RecoverGoldFromRemainingShipsAfterDistribution` path. Valuation
logging returns early when there is no current scope, and penalty logging
requires that recovery stage. Therefore the second allocation path is not
fully attributed by the current capture. The earlier “zero valued ships” means
zero within the observed reward scopes, NOT that no native ship-to-gold
conversion happened anywhere. This is a confirmed diagnostics coverage gap,
not proof of a native or mod duplication defect.

The 74,563 casualty-loot-context inflow is another distinct path. Native
`LootCasualtyCharacter` converts eligible AI casualty loot value into party
trade gold. Do not add this context a second time to canonical wallet flows.
This capture cannot reconstruct the historical 28.19-million increase.

## Recommended implementation order

1. **Close battle allocation attribution first, without changing income.**
   Observe one full map-event allocation lifecycle, keyed by event/party/ship:
   defeated-wallet allocation, ship disposal/value, before/after PlunderedGold,
   evaluated player penalty, then the existing commit receipts. Never re-query
   randomized/native valuation or count allocations as additional cash. Prove
   native target signatures and offline fixtures before deployment.
2. **Add bounded, earlier workshop replenishment.** Order when usable inputs
   fall below consumption expected over route lead time plus a small buffer,
   using the authoritative calendar recipe rate. Count owned/in-transit cargo,
   avoid duplicate orders, retain affordability, route and supplier safeguards,
   and permit at most a bounded refill plan. Do not reserve every market item
   globally or ban normal caravan trade.
3. **Make scarce food-input reserves category-aware.** Retain the aggregate
   town food-store and declining-food safety checks. Replace the one-size
   per-category reserve only with a reviewed, configurable consumption/local
   recipe policy. Do not lower every food reserve or remove starvation guards.
   Expose reason counts for each rejected supplier filter so future reports
   identify the controlling constraint rather than `no_viable_supplier` alone.
4. **Reassess prices/output only after those corrections.** The current wine
   losses have specific input/cadence explanations; a blanket workshop buff or
   noble-income cut is not supported by this evidence.

No new soak is needed to implement and unit-test those focused candidates.
New runtime behavior would still require acceptance; this investigation does
not promise an untested fix or silently schedule another run.
