# Bannerlord economy research applied to AOC — 28 September 2026

## Decision

Keep the accounting and recipe-cadence corrections. Do not tune the whole economy
to make four businesses profitable. Develop a bounded procurement-policy correction
only where a reproducible case demonstrates a viable purchase is wrongly excluded.
Native scarcity, unsuitable workshop locations, war and weak demand remain valid
outcomes. Three idle workshops identify cases to explain, not three proven code bugs.

This research changes the correction plan; it does not change production settings,
deploy binaries, edit saves or certify unobserved gameplay. It covers the connected
systems relevant to this investigation, not every perk, quest or third-party mod.

## Evidence and version

The installed Native/SubModule.xml identifies **v1.4.8**. Assembly file version
1.0.0.0 is generic and cannot identify the game version. Rechecked these installed
hashes against the September 12 native-research manifest; all match:

| Input | SHA-256 |
| --- | --- |
| TaleWorlds.CampaignSystem.dll | 1F8E33E2ED73E6EC653D7629180AFB70649DDC6E5BD1657A802A264EFDA1C3AE |
| TaleWorlds.Core.dll | 3E5CECE8CA42FAF3FDDE27632519ED2A6F86D1EC6D1A742867729F00001689BF |
| SandBox/ModuleData/spworkshops.xml | B5AF100D431419E6873D42A4524DC524FC8C2E0FC868E65149BCBDC7E34873D4 |

This permits reuse of the private local native decompilations in
`output/economy-deep-audit-20260912/native` and `native-finance` as version-matched
evidence. They are not redistributable implementation source. Historical AOC
sidecar decompilations do NOT establish current sidecar behavior: the sidecars
have changed. Current source and the closed capture must be considered separately.

Current campaign findings come from `ECONOMY_FULL_CAPTURE_REVIEW_20260928.md`,
session `7f1cb08aba0c49518d8ed0605ef61516`. This research did not run another soak.

## Public-source findings and how they apply

1. **Connected local markets, not guaranteed industrial profit.** TaleWorlds
   describes village production, villager transport, workshop processing and
   caravan redistribution. Interruption is intended to hurt local economies.
   Preserve that causal chain rather than creating inputs when it breaks.
   [Passage of Time](https://www.taleworlds.com/en/Games/Bannerlord/Blog/14).
2. **Demand and prosperity matter as much as inputs.** The official economic
   design ties consumption budgets and prices to category demand and local
   availability. Supplying more inputs is not sufficient when the output market
   cannot support production. This is design context, not a source for current
   numerical constants.
   [Economy development blog](https://www.taleworlds.com/en/Games/Bannerlord/Blog/107).
3. **The baseline changed substantially.** v1.3.4 altered taxes, caravan trading,
   livestock outputs, wool/felt and workshop balance. It also retained expenses
   while rebellious towns halt production. Do not calibrate 1.4.8 against old
   guides promising a particular workshop income or obsolete livestock yields.
   [v1.3.4 release](https://www.taleworlds.com/en/News/587).
4. **Modern trade agreements are an income source.** v1.4.5 documents trade-partner
   caravan/convoy visits generating kingdom income that is distributed to vassals.
   Include this source in wealth attribution; it is not necessarily workshop or
   battle profit.
   [v1.4.5 release](https://www.taleworlds.com/en/News/602).
5. **Player warehouses are a separate workflow.** Native warehouses support
   player-selected inputs and stored outputs. AOC's AI procurement must not
   silently take control of them.
   [Warehouse update](https://www.taleworlds.com/en/News/536).
6. **Quests interact with the economy.** Issues can originate from trade
   disruption, and resolving them can improve local conditions. Quest deadline
   policy is therefore a separate integration concern, not a workshop multiplier.
   [Quest design](https://www.taleworlds.com/en/Games/Bannerlord/Blog/78).

Some direct page opens returned HTTP errors; indexed official-page text supplied
the public research above. No community guide is used to establish native formulas.

## Economy map and ownership boundaries

| System | Verified behavior or evidence | Application to AOC |
| --- | --- | --- |
| Village supply | Native production depends on village state, hearth level, output definition, perks and buildings | Distinguish no production from goods that never reached town. Do not diagnose every empty market as a production-rate bug. |
| Transport | Villagers/caravans move goods through the world; disruption matters | Current procurement is virtual paid transport, not a physical caravan or sea logistics simulation. Keep that distinction explicit. |
| Market demand | Native base demand depends on prosperity; luxury demand additionally uses prosperity above 3,000 | Evaluate category demand and prices, not only workshop inputs. More production can depress prices. |
| Town liquidity | Native gold change is round(0.25 * (10000 + 12 * prosperity - town gold)) | Vanilla has a monetary source/sink. Reconciliation must attribute it, not demand a closed-money world. AOC's final rate may be patched. |
| Workshops | Fractional per-recipe progress, input checks, margin/capital/town-cash gates and daily expenses | Potential cadence is not realized production. Failed cycles do not justify unlimited catch-up output. |
| Consumption | Native category budget derives from demand and a price-index term | Do not scale both demand and derived budget independently. Current sidecar source explicitly retires that double scaling. |
| Food | Market items and aggregate food stores are different quantities | Export policy needs both commodity availability and town-food protection; do not treat them as the same stock. |
| Finance | Native finance combines taxes, tariffs, parties, workshops, treaties, tribute, wages, debts and kingdom support | Attribute actual wallet changes once, preserving transfers and distinguishing grants. |
| Kingdom support | Native DailyTickClan can top up the kingdom budget outside the finance calculation | A finance-wrapper audit alone cannot certify all money creation or its annual scaling. |
| War and recruitment | Money supports parties; raids and sieges disrupt supply | Smaller early armies and slower wars are separate balancing goals, not reasons to secretly cut workshop income. |
| Naval rewards | Distinct reward/transfer/cleanup paths | Keep observed naval accounting separate from still-unexercised player selling penalties. |
| Quests | Timers and rewards can affect economy and local conditions | Keep timer ownership separate; an economy soak with no quests does not validate deadlines. |

Native sources inspected: DefaultVillageProductionCalculatorModel,
DefaultSettlementEconomyModel, DefaultWorkshopModel, WorkshopsCampaignBehavior,
DefaultClanFinanceModel, ClanVariablesCampaignBehavior and DefaultItemCategories.

## Calendar conclusions: retain completed fixes

Current `WorkshopRecipeCadenceFix` scales industrial recipes individually and
shares their base speed with warehouse estimates. Food-output recipes remain at
native cadence. Current `FoodAwareVillageProductionFix` explicitly preserves
cow, sheep, hog and wool supply in addition to food-tagged inputs. These are
already present in source; recommending their implementation again is circular.

The local native category data classifies felt as BonusToFoodStores, wool as
ordinary material, wine as BonusToLoyalty and silver as BonusToTax. Names alone
are not valid category tests. Wool supply supports both a native-speed felt
recipe and an annualized garment recipe. Blanket food/non-food rules are not
enough to understand every edge of a chain.

Separate four quantities in every policy: calendar days, real seconds, batches
per campaign day and gold per transaction. For an illustrative 365.25-day year,
the annual rate factor is 84 / 365.25 = approximately 0.22998. This is NOT an
instruction to multiply freight payments, inventory balances, every timer or
all physical travel by that number. A transaction must conserve its actual
payment; annual flows and deadlines have different semantics.

## New procurement findings

### 1. A generalized iron-reserve fix could make wool worse

`ProcurementPolicy.HasSurplus` normally protects seven summed recipe-input
batches, with a ten-unit minimum. The optional iron policy instead protects
forecast daily use for a configured number of days. These are different units.

Using native XML recipes, the current calendar classification, no perks and
the illustrative factor above:

| Supplier example | Existing reserve | Hypothetical 14-day reserve |
| --- | ---: | ---: |
| One wool weavery, garment + felt | max(10, 2 * 7) = 14 | ceil(14 * (2 + 0.22998)) = 32 |
| One silver/jewelry workshop | max(10, 1 * 7) = 10 | max(10, ceil(14 * 0.75 * 0.22998)) = 10 |

These are deterministic examples, NOT reconstructions of the actual supplier
rosters. They disprove the assumption that extending the iron policy necessarily
unblocks wool or silver. At multiple suppliers the result could differ.

### 2. Olives and grapes face different policy floors

Olives are food-tagged and retain the generic 100-unit food-category floor;
grapes have a ten-unit exception. Aggregate food-store and deficit-buffer checks
still apply. This is a concrete policy distinction worth assessing for Lageta,
not proof that a 100-unit olive reserve is wrong. An olive exception must not
export essential food from a starving town or assume that production implies
immediate replenishment.

### 3. Route rejection cannot currently identify its cause

`ProcurementPlanner.Find` combines invalid/nonpositive distance and distance
above MaximumDistance under `routeRejected`. Its land-route contract must not be
replaced by straight-line distance, unlimited reach or invented sea transport.
Record distinct invalid/unreachable and over-limit reasons, with a bounded
sample of supplier IDs, distance, stock and the binding reserve calculation.
Preserve the old aggregate field for analyzer compatibility.

The completed log does not contain every rejected candidate's complete state.
It cannot be used to replay every route/reserve decision exactly. Static checks
and synthetic fixtures can validate mechanisms without loading the game; they
cannot establish a missing historical supplier inventory.

### 4. Reordering has an intentional pipeline limit

`DailyTown` refuses a new order while any order for that workshop remains,
including arrived private inventory. Lead-aware/adaptive sizing and delivery
delay cost already exist. A proposed new reorder feature must not be presented
as if none exists. Pipelining could reduce gaps, but creates concurrent cargo,
cost-basis and save-compatibility risks. No evidence here warrants replacing the
single-order invariant before addressing actual supplier feasibility.

### 5. Eligibility and supply feasibility are separate

Both endpoints currently require positive food stores even for non-food goods.
That conservative policy is not a universal native trade law. Conversely,
procurement eligibility does not check the native rebellious-town production
halt. Neither observation proves the three captured losses were caused by it.
The classifier should identify production-halted towns and avoid diagnosing
their missing output as a supplier failure. Do not bypass native halts.

## Ranked correction package

1. **P0 — preserve known-good accounting.** Keep the 109 reconciled transactions,
   batch payments, private cargo cost basis, money bounds and save lifecycle as
   regression anchors. Do not weaken tests to force a profitable result.
2. **P1 — explain and correct false procurement exclusions.** Separate route,
   export reserve, food safety, recipient production halt, lot size and quote
   failures. Use controlled supplier cases to prove a legitimate offer passes
   while disconnected/hostile/unsafe/insolvent cases remain blocked. Category
   reserve overrides require demonstrated binding thresholds, not global easing.
3. **P2 — wine throughput.** Preserve current expense-aware wine-margin support.
   Galend's -23 gold is fully explained and includes town-cash and margin gates,
   not just grape availability. Judge total operating profit and exporter impact,
   not owner payouts. Do not add another wine buff before identifying the limiter.
4. **P2 — wealth source coverage, not an income cut.** Current aligned noble cash
   fell 0.97%. Historical excess wealth still needs its own attribution. Include
   kingdom grants, treaty income, trade, rewards and expenses; reconciliation is
   necessary but not proof each source has the intended annual cadence.
5. **P3 — physical logistics integration.** One owner of stock, cash and delivery
   state per shipment. A future physical carrier must replace the virtual
   fulfillment path, not run beside it. No duplicate deliveries or calendar scaling.
6. **P3 — production diagnostics and release claims.** Routine summaries plus
   bounded failure samples; retain detailed transactions for unresolved cases.
   Player naval/quest tests are targeted gaps, not reasons to repeat a generic soak.

## Acceptance without repeating the same run

Before another requested live check, the correction package must pass offline
fixtures for viable supply, over-distance/unreachable routes, minimum/reserve
boundaries, mixed wool recipes, olives under food stress, rebellion, price/cash
failure, existing cargo, ownership change, save/reload and accounting rollback.
Tests for already-supported cases should be reused, not replaced by new systems.

Any eventual live check must have an exact question, e.g. whether a previously
rejected but now demonstrably valid supplier is selected without harming its
food/production reserve. If that supplier state is unavailable in the recorded
capture, say so. Do not promise no live validation will ever be necessary.
Do not request another unchanged 16-day run to rediscover aggregate failures.

Success is correct decisions and conserved assets, with supported improvement
where supply exists. It is not 228 profitable workshops, zero unsuccessful
cycles, zero native money creation, or every recipe executing in 16 days.

## Verification performed in this research turn

- Verified installed version and three native/XML hashes above.
- Read native formulas, current procurement settings/planner/lifecycle and
  calendar recipe/input/demand code; compared with the completed capture review.
- Calculated the reserve counterexamples above without changing settings.
- Ran Windows PowerShell `Verify-Procurement.ps1`: **117 assertions passed**
  across state, multi-input, money/stock, freight, reserves, lifecycle and rollback.
- No runtime-code edit, deployment, save change, game restart or new capture.
  No Release build was needed for this research-only document. Passing the
  existing verifier does not validate the proposed policy changes or live balance.
