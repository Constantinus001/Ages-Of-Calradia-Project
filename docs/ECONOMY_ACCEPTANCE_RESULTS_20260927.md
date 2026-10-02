# Five-day economy acceptance results

## Evidence

Session `daeceb2d3b09444da48052c917489b33`, campaign `gHBJvLViLE7O`.
Source: `C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics\AocFramework-current.tsv`.
SHA-256: `C07D71E385E01709A22203F88A689990D2F4BB78AAA9ADABA17C930B4CCCF4B1`.
Analyzer: `Modules/AgesOfCalradiaSoakDiagnostics/Tests/Analyze-FrameworkPeriods.py`.

The closed, stable capture spans 5.00003831944 campaign days, with 1,018,406
records and 393,711,423 bytes. Closure reason is
`acceptance_window_complete_coverage_not_certified`. No discarded records,
segment integrity problems, or reconciliation errors were reported. Coverage
gaps remain; this is not a full economic-balance or release certification.
No game state, speed, saves, installed files or economic rates were changed by
this analysis.

## Findings

### Accounting

- All 228 workshop capital accounts reconcile over the capture, including
  expense and owner-payout observations. All 1,789 matched capital-affecting
  production batches have zero discrepancies.
- All 39 procurement transactions reconcile. There are 66 accrual receipts
  and five checked inventory-cost intervals, without reported gaps/errors.
  Twelve arrivals and 27 consumption events occurred. An outstanding order
  is not lost cargo merely because its arrival falls after capture closure.
- The detail report covers 2,169 hero/clan/kingdom wallet entries with zero
  maximum observed residuals. Eleven wallets start after the capture opening;
  nine of those lack hero-to-clan identity. Their observed windows reconcile,
  but they are not certified full-period clan accounts.
- Stable identified noble-clan hero wallets cover 97 clans: observed inflows
  891,439, outflows 628,290, net +263,149 gold. This is this tracked population,
  not total world money creation or an explanation of the old 28.19-million
  increase. Transfers within this population cancel in the net.
- Largest observed incoming caller paths include battle gold commitment
  (421,500), horse-sale settlement transactions (253,211), DailyTickClan
  transfers (103,074), and casualty-loot handling (74,563). Caller context
  identifies investigation targets, not a demonstrated exploit or grant.

### Supply and profitability

There were 3,677 workshop cycles: 2,628 succeeded and 1,049 failed.
Input-gate counts are failed attempts, not independent shortages or quantities.
Market stock checks have no reported residuals or unaligned windows.

- Livestock recipes consumed 159 cows, 460 hogs and 162 sheep, producing
  1,718 meat and 318 hides. Of 1,525 attempts, 781 succeeded and 744 failed:
  626 input-check rejections, 116 native margin-gate candidates and two town
  cash-gate candidates. This demonstrates working production, not sufficient
  supply in every town.
- Wool recipes consumed 58 wool, producing 110 felt and six garments. There
  were 58 successful and 142 failed attempts: 140 input-check rejections and
  two margin-gate candidates. Investigate local wool supply, competing uses,
  arrival timing and successful later consumption before changing rates.

| Type | Workshops | Losing workshops | Combined operating result | Owner withdrawals, separately |
| --- | ---: | ---: | ---: | ---: |
| Wine press | 10 | 4 | +877 | 173 |
| Smithy | 14 | 2 | +37,630 | 4,591 |
| Linen weavery | 22 | 1 | +36,409 | 4,320 |

Results cover five days, include recognized procurement costs and recorded
operating expenses, and exclude owner withdrawals as expenses. They do not
establish long-run balance. Wine had 19 input and two margin-gate rejections;
smithies had 38 input and eight margin-gate rejections; linen had six input
rejections. A rejected margin check is not proof of weak market demand.

### Naval and other coverage

- 432 native naval reward scopes executed, 716 ownership changes and 296
  party-removal scopes were observed. Fifteen removal scopes contained ships.
  The lifecycle analyzer counted 293 destruction observations; no removal
  integrity problems or retained-ship coverage warnings were reported.
- There were no valued leftover ships, evaluated selling-penalty receipts,
  or player naval scopes. Player selling penalties remain NOT_EXERCISED.
- Procurement returns, liquidation, rollback and failure branches were not
  exercised. Finite quest deadlines were not exercised. Recipe no-attempt
  samples are retained, not reclassified as failures.

### Timing and diagnostics

- Total recording wall time was 390.217 seconds, including the initial pause.
  Three subsequent measured day intervals were 40.119, 40.038 and 40.073
  seconds/day, consistent with about 40.08 seconds/day at the chosen speed.
- Cumulative measured writer/flush cost was 4.491 seconds, about 1.15% of the
  whole capture wall time. This excludes observer snapshot/stack/status work
  and is not a total overhead or FPS measurement.
- Sampled growth was 61.5–71.0 MB/day. Including initial/terminal records,
  the whole-capture average projects about 2.36 GB per 30 campaign days.
  This is an extrapolation, not a guaranteed upper bound.
- The readiness command labels a deliberately closed checkpoint as a
  campaign/checkpoint mismatch because its recording gate requires `open`.
  The final analyzer verified the closed checkpoint and length successfully.
  Improve that status wording separately; do not reopen the capture.

## Next work, using existing evidence first

1. Trace wool input failures and the four losing wine presses by town and
   attempt, joining local inventory, procurement lead time, price thresholds
   and later consumption. Do not blanket-buff production or all resources.
2. Review the large noble inflow caller paths and counterparties, especially
   battle rewards and horse sales, before proposing an income reduction.
3. Address misleading closed-capture readiness wording and late-created hero
   identity coverage in diagnostics. Measure full observer overhead separately
   before claiming lightweight logging.
4. Keep unexercised naval/player/rare procurement/quest branches explicitly
   unverified. Do not automatically request another soak or artificial events.

The five-day run supplies useful reconciled evidence. No additional run is
required to begin the targeted offline investigations above.
