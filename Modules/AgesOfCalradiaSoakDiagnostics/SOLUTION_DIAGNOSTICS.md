# Diagnostics for every proposed solution

Status: implemented in the local diagnostics sidecar; not deployed or observed in a live campaign. This instruments the seven packages in `output/economy-deep-audit-20260912/PRIORITIZED_SOLUTIONS.md`. It does not implement their balance changes.

| Priority / solution | Evidence added or retained | Required interpretation |
| --- | --- | --- |
| P0 trustworthy diagnostics | Actual account deltas, parent scopes, zero-net settlements, independent transfer endpoint residuals, daily reconciliation, session closure, observer timing/memory and explicit capture failures | Run raw coverage separately. Equal and opposite errors cannot disappear in gross-positive/gross-negative summaries. Endpoint observations overlap leaf mutations: never add them together. |
| P1 recipe-aware cadence | Native speed result, per-recipe definition/progress/derived increment, attempts/successes, input/profitability gate results, player/notable caller, town eligibility, warehouse model estimates | Compare food/non-food recipes within mixed artisans as well as ordinary shops. Shared speed does not identify a recipe; recipe progress does. Estimates are not actual inventory. |
| P1 food-input chains | Actual item/modifier roster changes attributed to workshop/caller, village item production, trade destinations/hearth state, all recipe-category market stocks including zero, in-stock price quotes, sampled shortage duration, demand model calls, food-stock changes and model explanation, existing warehouse stock | Specifically inspect livestock/meat and wool/felt, plus grain/fish and wood/iron. Model demand is not consumption. Zero-sample spans do not prove uninterrupted shortage. Missing prices remain unavailable. |
| P2 cash attribution | Active finance-model result, actual daily hero settlement including zero, wage assessments, component deltas, requested versus actual clan credits, internal/external gold transfers, kingdom-budget wallet changes and outside-finance top-up candidates | Wage/tribute components are not proof of cash payment. Native null-endpoint mint/burn is not automatically a bug. Review residuals against the short raw trace and nested callers. |
| P3 weak-workshop balance | Capital/initial-capital surplus, owner/type, direct initialization/ownership capital changes, expense calls, production gates, bankruptcy/ownership/type transitions, rebellion/siege/loyalty and daily native eligibility | ProfitMade is capital surplus, not lifetime earnings. Exercise bankruptcy, rebellion, player warehouse and ordinary notable production separately. No occurrence means NOT_EXERCISED. |
| P4 treaty reversals | War/peace correlation, caller stack, rapid-reversal warning, optional opening-peace initialization/end day/current active state/days left at events and daily snapshots | Keep raw warnings. Module absent, fields unsupported or unreadable means unavailable, not legitimate treaty proof. A treaty snapshot is not a witnessed reversal. |
| Acceptance / controlled comparison | Campaign identity and diagnostic MVID, actual model identities, save completion result, existing reload receipt verification, monotonic wall seconds/day, native-scope inclusive timing, observer callback time/memory/GC, coverage report | Pair the same starting save/settings and campaign-day interval for control/candidate. Session identity alone cannot certify that pairing. Save completion alone is not reload success. Timing includes pauses/stalls; native timing is not isolated observer overhead. |

## Report

Run in Windows PowerShell against the actual collected directory:

```powershell
& .\Modules\AgesOfCalradiaSoakDiagnostics\Tests\Summarize-EconomySolutions.ps1 -DiagnosticsDirectory '<run-directory>' | ConvertTo-Json -Depth 8
```

The report covers all seven packages, records required category counts, and separately checks zero settlements, successful/failed cycles, rejected gates, observed treaty state, save success, rebellion and bankruptcy. It reports `NOT_EXERCISED`, `REVIEW_REQUIRED`, or observed evidence only; it never declares the economy fixed. War/peace event presence is separate from treaty state. Raw reconciliation, reload acceptance and matched A/B acceptance remain explicitly unverified by this summary report.

For disputed cash attribution use `AocEconomyTransactions.enabled` and the short raw-ledger gate (`Verify-EconomyRuntimeCoverage.ps1`). Default daily summaries retain counts and gross flows but only the last context per key. They cannot substitute for transaction ancestry or capture every intermediate branch. Do not start another long soak until the short real-run coverage is inspected.

## Native compatibility and fail-safe behavior

Bannerlord 1.4.8: 57 static native targets, plus the active finance-model override when distinct. Additional boundaries are `GiveGoldAction.ApplyInternal` (endpoint reconciliation), workshop initialization/ownership/production changes (capital/lifecycle), `WorkshopsCampaignBehavior.DailyTickTown` (eligibility), its two warehouse daily-change interface implementations (estimates), and `DefaultSettlementEconomyModel.GetDailyDemandForCategory` (observed demand result). Prefixes/postfixes/finalizers observe; they do not skip originals or change results. Reflection reads the audited existing warehouse backing array without creating storage, and optional religion treaty fields without mutating them.

Missing mandatory targets, unsupported warehouse storage or capture exceptions invalidate diagnostics while leaving gameplay running. Optional treaty absence is explicitly unavailable. Other patch owners/native MVIDs are logged. Overrides, inlining and other postfixes remain live compatibility risks; reconciliation and branch coverage must be checked with the actual load order.

Bounds: 50,000 summary keys/day, 5 GiB summary/session, 1 GiB raw trace, 500,000 ledger keys, 50,000 shortage history keys. Reaching a bound invalidates evidence rather than silently dropping records. Actual CPU/disk overhead and these budgets still require a short campaign run. Health counters measure observer callbacks, not the entire snapshot/serialization overhead.

## Verification on this candidate

Release build passed with no compiler warnings/errors. All 57 native targets resolved and were actually Harmony-patched in an isolated verifier process. Transaction, daily-summary, coverage-gate, seven-solution report, soak, peace and protected-baseline checks passed. Added behavioral checks cover shortage duration/reset/backward-time rejection, aliased endpoint arithmetic and nonzero transfer residuals, missing evidence, incomplete sessions, offsetting residuals and invalid evidence counts.

No game launch, stop, deployment, save change, pacing change, balance change or protected-artifact replacement was performed. Live production/cash/treaty branches, save/reload acceptance, A/B balance effects and runtime overhead remain NOT_EXERCISED by these tests. The existing broader calendar schema mismatch (expected 6 versus protected artifact 5) and unfinished strategic-map coverage check remain outside this diagnostics change; no general release-gate pass is claimed.
