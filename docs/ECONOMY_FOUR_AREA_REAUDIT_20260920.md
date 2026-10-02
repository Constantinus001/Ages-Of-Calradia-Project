# Four-area economy re-audit — 20 September 2026

Status: existing-capture analysis and an offline analyzer correction. No gameplay
rates, installed files, settings, saves or running game were changed. No scan.

## Evidence

Re-read the closed supply_v7 capture
`AocSupply-f50288772c15414ba92b923b3227d01a.tsv` in the Bannerlord Documents
diagnostics directory: 15.0000456146 campaign days. Used Analyze-SupplyCapture.py,
Analyze-WorkshopPayments.py and Analyze-FourEconomyCases.py from the diagnostics
Tests directory. This capture predates the new committed procurement movement
receipts and integrated-Core candidate. It is not live acceptance of that build.

## 1. Livestock and wool

Current FoodAwareVillageProductionFix already exempts cow, sheep, hog and wool
from industrial annual scaling. WorkshopRecipeCadenceFix selects cadence per
recipe, not per entire workshop. Do not apply these corrections twice.

Recorded village production events: 638 cows, 1,378 sheep, 2,040 hogs and 2,340
wool (approximately 42.53, 91.87, 136 and 156 units per campaign day across
observed producers). These are not guaranteed deliveries to needy towns.
Workshop consumption events: 488 cows, 635 sheep, 1,529 hogs, 371 wool.
Meat production events total 5,645, exactly 488*4 + 635 + 1529*2.
Felt production events total 698. Do not assume all 371 wool went to felt:
installed SandBox spworkshops.xml also has a wool-to-garment recipe.

First/last daily market samples (not exact synchronized world inventories):

| Category | Units first → last | Zero-stock towns first → last, of 57 |
| --- | ---: | ---: |
| Cow | 218 → 79 | 22 → 39 |
| Sheep | 471 → 806 | 29 → 8 |
| Hog | 534 → 410 | 18 → 34 |
| Wool | 695 → 550 | 26 → 18 |
| Meat | 3,095 → 2,242 | 0 → 3 |
| Felt | 48 → 255 | 44 → 24 |

Sheep recipe gates: 635 acceptances, 450 profit rejections and 4 cash rejections
across 1,089 evaluations. Twenty-four of 55 observed shops had no accepted sheep
gate. Repeated evaluations are not independent shortages; zero sampled stock
does not establish continuous absence. Existing production exceeds workshop
consumption in aggregate for these inputs, but party demand, transfers and stocks
outside towns prevent deriving a supply surplus from that comparison alone.

Conclusion: mixed local availability, not one demonstrated global broken rate.
Prioritize cattle/hog destination availability and competing consumption; do not
boost sheep/wool indiscriminately or bypass profit gates. Distribution and
complete endpoint attribution remain necessary before tuning.

## 2. Previously weak workshops

5,462 capital-affecting successful batches reconcile; no batch discrepancies or
payment-analyzer problems. The following are mapped-shop recorded operating
results, not certified all-shop net profit:

| Type | Successful cycles / attempts | Production margin | Recorded operating expenses | Result before owner payouts |
| --- | ---: | ---: | ---: | ---: |
| Wine press | 46 / 103 | 8,593 | 3,450 | 5,143 |
| Smithy | 149 / 231 | 41,416 | 3,450 | 37,966 |
| Linen weavery | 100 / 105 | 47,021 | 4,830 | 42,191 |

Recognized prepaid inputs are deducted from production margin. Nine unmapped
nonproduction workshop wallets each contain 345 of expenses, 3,105 total.
Procurement dispatch cash also includes obligations for future consumption;
subtracting all dispatch cash again would double-count recognized prepaid costs.
Other capital changes and incomplete remaining-cargo valuation prohibit calling
these totals fully accrued net profit. Owner payouts are distributions, not
operating losses. No subsidy or profit multiplier is supported by these totals.
Next: map expense-only/nonproducing shops and attribute freight/remaining cargo
before individual-shop viability certification.

## 3. Noble wealth

The historical 28,193,389 increase is a specific 97-leader cohort in
`AocEconomyDaily-a254bc8dd38048b8818a2f8d2f325828.tsv`, documented in
output/soak-findings-audit-20260912/AUDIT.md. Its summary lacks full transaction
ancestry. The separate 9.56M → 41.66M endpoints in the older deep audit use a
different interval; they must not replace the 28.19M cohort calculation.

Fixed the four-case analyzer to recognize leading-dot Harmony frames and
NavalDLC callers, skipping generic wallet setters, ChangeHeroGold, GiveGoldAction
and MbEvent dispatch. Unknown chains remain explicitly unattributed. This reports
the nearest observed domain caller, not necessarily the ultimate money source.

Fresh results for this newer 15-day capture, across observed hero wallets:

| Observed caller | Net observed change |
| --- | ---: |
| Battle CommitGoldChanges | +1,990,378 |
| Naval recovery after ship distribution | +424,206 |
| DailyTickClan | +394,895 |
| LootCasualtyCharacter | +196,814 |
| DailyTickHero | +97,016 |
| AddPartyExpense | -551,596 |
| AddIncomeFromParty | -407,058 |

Zero final hero-wallet residuals were reported. These figures are not the old
97-leader cohort and cannot explain its entire historical increase. A residual
of zero means observed changes reconcile, not that rewards are well balanced.
Battle rewards and ship recovery are now concrete investigation priorities:
check debited assets, valuation, recipient shares and duplicate awards before
reducing income. Do not sum mirrored hero/lord-party wallets as distinct money.

## 4. Framework authority: what actually exists

| Concern | Current authority/capability | Not implemented by this framework |
| --- | --- | --- |
| Clock | CampaignTime adapter plus protected calendar | Replacement ticking clock |
| Calendar settings | Existing CalendarSettingsState | New duplicate calendar configuration |
| Procurement | Validated immutable settings and conserved transfers; caller-owned save ledger | Universal economy transaction database |
| Economic rates | Existing calendar-fixes sidecar and Core models | General enforced policy/plugin registry |
| Quest deadlines | Existing protected scaling owner; framework reads saved due dates | Framework ownership of scaling or arbitrary quest durations |
| Logistics | Optional read-only party reserve ABI | Physical workshop shipment hand-off |
| Declared ownership map | Documentation exposed through service | Third-party patch conflict enforcement |

The broader umbrella idea remains reasonable, but the present implementation is
not universal authority over economics and quests. Expanding it requires explicit
capability ownership and migrations; simply adding another multiplier risks
double scaling. No such expansion is implied by this audit.

## Verification and next work

58 analyzer tests pass, including three attribution regressions. Diagnostics
Release build succeeds with zero warnings/errors in isolated output. Re-ran the
corrected attribution analyzer on the existing capture; no game restart needed.

Unresolved: cattle/hog local availability, expense-only workshop identity and
complete freight accrual, battle/ship reward asset-level justification, historical
cohort attribution limits, and wider framework authority. These are not declared
fixed merely because the analyzer/build passes. No new soak requested.
# Competing-demand reanalysis of the existing closed capture

Reprocessed `AocSupply-f50288772c15414ba92b923b3227d01a.tsv` with the
operation-separated broad analyzer. No new game run or log mutation. These are
aggregate observed town-market endpoint changes, not worldwide destruction or
total party consumption. The analyzer reports no broad structural problems but
still reports **31 stock residuals**; the old capture predates new procurement
movement receipts, so these figures do not certify complete conservation.

| Category | Town consumption | Workshop market net | Caravan exports | Caravan imports | Villager sale scope net |
|---|---:|---:|---:|---:|---:|
| Cow | -18 | -488 | -71 | +38 | +477 |
| Sheep | -62 | -635 | -113 | +78 | +1077 |
| Hog | -32 | -1529 | -137 | +62 | +1659 |
| Wool | -196 | -266 | -1678 | +373 | +1742 |
| Meat | -3255 | +5645 | -2138 | +879 | +4 |
| Felt | -125 | +698 | -934 | +565 | 0 |

Other-party net trade is also present: cow -77, sheep -10, hog -147,
meat -1988 and felt +3. Trade moves inventory out of observed town markets;
it is not automatically a supply loss. Wool workshop input events and market
net draw differ because input events are not limited to market stock and output
can share a category. Keep both measurements separate. Next causal checks must
resolve residuals and private/warehouse endpoints before choosing rate changes.
