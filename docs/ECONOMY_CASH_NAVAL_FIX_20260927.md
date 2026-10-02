# Cash completeness and naval skip correction

## Scope

Implements the focused fixes from `ECONOMY_CASH_NAVAL_INVESTIGATION_20260927.md`.
Only CampaignSystems and SoakDiagnostics sidecars, their tests, and analyzer/reporting
code change. No balance constants, calendar rates, protected Core/prefabs, saves,
settings, or capture markers change. This is a private test update, not a public
release or a declaration that the entire economy is balanced.

## Cash observation

Native Bannerlord 1.4.8 targets added to SupplyCashObserver:

- WorkshopsCampaignBehavior.DailyTickTown(Town): all town workshop capital.
- HandleDailyExpense / HandleNotableWorkshopExpense / HandlePlayerWorkshopExpense(Workshop).
- Workshop.InitializeWorkshop(Hero, WorkshopType) and ChangeOwnerOfWorkshop(Hero, WorkshopType, int).
- DefaultClanFinanceModel.CalculateHeroIncomeFromWorkshops(Hero, ref ExplainedNumber, bool).
- Existing DailyTickHero/Clan boundaries now also snapshot owned workshops as an
  explicitly unclassified fallback, rather than only the hero wallet.

These are read-only prefix/finalizer observations. They never re-run finance or
modify native arguments/results. The withdrawal prefix binds only Hero and the
apply-withdrawals flag, deliberately omitting the native ref accumulator and
`__args`. Preview calculations do not emit withdrawal receipts. Wider changes
subtract already observed nested deltas. Zero-value boundary receipts establish
execution without inventing an expense or payout. Skipped originals have explicit
execution flags, and their patch-side cash remains unclassified.

Missing targets fail diagnostics setup; observation exceptions close capture safely
without suppressing native exceptions. These patches add diagnostic overhead and
can interact with inlining/other patches. Offline fixtures verify target binding,
ref-result preservation, preview exclusion and nested/bypassed cash changes. Live
completeness and overhead remain acceptance requirements.

## Profitability

Analyze-WorkshopPayments carries per-workshop wallet baseline/end dates, final and
maximum sampled residuals, and executed expense/withdrawal receipts. Idle shops are
included. Existing batch reconciliation stays separate.

WorkshopProfitability returns `INCOMPLETE_CASH_EVIDENCE` with a null reconciled
operating result when cash evidence is missing, windows are unaligned, residuals
are nonzero, or supporting payment/accrual checks fail. The partial observed margin
remains available under its explicit existing name. Owner distributions are not
operating costs. A reconciled result is still not economic-balance certification.

Old capture schemas remain readable, but absent new evidence is reported as missing,
not retrospectively fabricated. No historical capture is rewritten.

## Naval behavior and diagnostics

NavalDuplicateRewardGuard finalizer now releases only the current call's newly
entered gate when another prefix skipped the original. An earlier completed
distribution remains protected. Native exceptions still propagate and allow retry.
The target remains NavalDLC.DistributePartyShipsAndRecoverGold(MobileParty), with
no price, payout-formula, save, or first-successful-call changes.

The reward observer records `originalRan` and native recovery eligibility at entry.
Skipped calls have an explicit skipped record or a scoped end marked false.
Runtime counters use completed, executed naval scopes instead of prefix entries.
The analyzer separates skipped originals, legacy unknown execution, ineligible-at-
entry returns, and missing valuation evidence. Ship-destruction observations also
require the original to have run.

Ship destruction/cleanup coverage and player-only selling penalties are not newly
certified by this change. No unsupported cleanup or inventory mutation is added.

## Verification

- Both changed sidecars built in Release with warnings treated as errors.
- Verify-WorkshopCashBoundaries: actual observer methods on an isolated synthetic
  target, unchanged ref result, no preview withdrawal, -23 bypassed expense and
  -25 nested withdrawal reconcile to -48 exactly.
- Verify-NavalPatchCoexistence: actual built patch methods, both prefix orders,
  exactly one executed reward and one skipped call, competing-skip release,
  exception propagation, retry, and restored observer context.
- Existing cash, reward observation, naval guard, and runtime summary checks.
- Analyzer suite, including historical missing evidence, offsetting residuals,
  mismatched windows, skipped originals, and native ineligibility cases.
- Protected baseline, Protected560 calendar contract, and strategic-map checks.

The candidate verification path now runs the new fixtures using its exact candidate
assemblies. The scoped economy release gate also includes the new cash fixture.
No security scan/public-release gate is claimed by a private deployment.

Historical replay of the preserved 17.024-day capture still matches 6,609 batches
without batch discrepancies. Its workshop residual sum remains -407,923; all 228
workshops now report incomplete cash evidence and no reconciled operating result.
Missing expense/withdrawal receipts are not retroactively reconstructed. All 109
Python analyzer tests passed.

## Private deployment completed 2026-09-27

Only the two verified DLLs were copied, after the process-idle guard and installed
hash checks passed. Installed hashes match the Release candidates:

- CampaignSystems: `A90DEE5D23F9C5AF53A4F137AC710182BA87D84CFF997E8C5AE479559D840756`
- SoakDiagnostics: `3BB3823AEE4E958B831B52116214BD22253616C92A3E21ED64851F0FD97AF33C`

Previous DLLs are backed up in
`C:\Users\fpicc\AocRelease\cash-naval-fix-97746954074f4896a3a346da1c7a1362`.
Protected baseline verification passed before and after copying. No game process,
save, setting, capture marker, protected Core DLL, or protected prefab was changed.

## Live acceptance remains pending

Do not start another broad soak. After explicitly preparing a new capture without
discarding the preserved September 25 evidence, use a bounded window that observes
daily workshop expenses and withdrawals. Require aligned wallet checks, actual
boundary receipts and no unexplained residuals before calling profitability complete.
The known rollback rejection must be addressed by explicit archival/new-session
preparation, not silently erasing logs or changing the user's save.
