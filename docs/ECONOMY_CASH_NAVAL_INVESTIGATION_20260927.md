# Workshop cash and naval investigation — September 27, 2026

## Verdict

The existing capture demonstrates diagnostic blind spots and an offline-reproduced
naval compatibility defect. It does **not** demonstrate that 407,923 gold was
incorrectly destroyed, or justify changing noble income or workshop output rates.
No gameplay code, installed files, settings, saves, or capture controls were changed
in this investigation. Additional broad soak testing is not the next step.

## Evidence identity

- Capture: `C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics\AocFramework-current.tsv`
- Session: `a8245018b64f4fe498e7c5ffd59a7b8c` (one session).
- SHA-256: `10E2151846D6DB687B4F5464211DC954AAE2EA28222B088284970B8600EB41C8`.
- Size: 1,092,478,814 bytes; final sequence 3,014,151.
- Campaign window: 396013.39322577778–396030.41760251042 (17.02437673264 days).
- End: `requested_close_snapshots_taken_coverage_not_certified`; discarded uncommitted rows: 0.
- Native CampaignSystem MVID: `886629fe-6e60-40d7-9a57-8d46017179d9`, confirmed against installed IL and capture provenance.
- NavalDLC capture MVID: `b60485da-883c-4a91-b58c-040702fe3ce9`.
- Installed and repository CampaignSystems DLL hashes match: `86664B170B87A44AA15DEC95C332646A2AE3119A20CCE5BDFBCE1C670A8E35CC`.
- Installed and repository diagnostics DLL hashes match: `A1B9232F92A10A3137827FE24F4252C4432E15F85F012E55D775673B210C4788`.
  The capture predates the counter-only correction; replay uses its preserved raw records.

## 1. Workshop capital is incompletely observed

`SupplyCashObserver` patches the small `Workshop.ChangeGold` method and wider
production purchase/sale calls. Its wider boundaries do not enclose workshop daily
expenses, workshop-owner income withdrawals, or ownership-driven capital resets.
Wrapping daily hero/clan updates currently captures the hero wallet, not all owned
workshop wallets.

The actual workshop cash records contain exactly these source groups:

| Source | Records | Net gold |
| --- | ---: | ---: |
| ConsumeInputFromTownMarket | 6,372 | -168,443 |
| ProduceAnOutputToTown | 15,261 | +1,214,138 |
| Procurement EconomicTransfer.ExecutePlan | 104 | -2,898 |

There are no observed workshop expense or owner-payout cash entries in this run.
Final checks have nonzero residuals in 227 of 228 workshop wallets, totaling
**-407,923 gold** (actual minus baseline-plus-observed changes).

This is an unexplained ledger difference, not an operating loss or proof of money
destruction. Other wallet groups also have residuals: 42 hero wallets total
-214,069, and nine kingdom wallets total +45,728. Do not combine these into a
world-money-loss claim. Active direct-party, clan, town, village and hideout
wallet groups have zero final residual in this audit.

Installed native IL confirms:

- `DailyTickTown` runs production, then `HandleDailyExpense`.
- Non-hidden notable workshops call `ChangeGold(-expense)` when sufficiently funded.
- `CalculateHeroIncomeFromWorkshops` calls `ChangeGold(-ownerIncome)` when withdrawals
  are enabled and income is positive.
- `ChangeOwnerOfWorkshop` can set capital directly, bypassing `ChangeGold`.

Missing wider boundaries are confirmed. Inlining is a strong candidate mechanism
for missed small-method hooks, not proven per individual historical mutation.
Harmony documents this limitation and recommends observing a higher caller:
https://harmony.pardeike.net/v2/articles/patching-edgecases.html

Example: `town_EN1/workshop:40`, wool weavery, ends at capital 25,653 versus the
observed ledger expectation of 32,363 (difference -6,710). Its first four daily
residual increments are -23 each; later increments grow. That pattern is consistent
with expenses followed by withdrawals, but it is not an independently matched
expense/payout receipt. Do not retroactively label every residual as wages.

### Profitability reporting defect

`Tests/WorkshopProfitability.py:25-40` computes operating results from recorded
flows and checks unclassified **recorded** changes. It does not receive or check
whole-wallet residuals. Replaying the actual run yields 228 workshop results and
`coverage_gaps=[]` despite the -407,923 residual. Missing expenses default to zero.
The existing textual limit says these are observed results, but an empty gap list
still fails to expose this known missing coverage.

The 6,609 successfully matched capital-affecting batches still have no batch
discrepancies. Procurement transaction and accrual analyzers return no problems or
coverage gaps. Those narrower passes remain valid; they do not certify total profit.

## 2. Naval observation depends on patch ordering

`output/investigation-20260927/Check-NavalCoexistence.ps1` attaches the actual built
guard and observer methods to a synthetic no-inline target accepting native
MobileParty objects. It runs in a separate PowerShell process, not the game.

| Prefix order | Calls attempted | Original executions | Logged reward scopes |
| --- | ---: | ---: | ---: |
| Guard first | 2 | 1 | 1 |
| Observer first | 2 | 1 | 2 |

Both orders restore observer context and produce balanced begin/end records.
However, observer-first records a skipped duplicate as an ordinary completed scope.
The observer does not record whether the original ran. This is a reproduced
reporting ambiguity, not proof of duplicated payout in the saved campaign.

A separate hypothetical competing-prefix case reproduces a guard defect: when
another prefix skips the first original after the guard has entered, the guard
remains latched and blocks a later legitimate original call. The guard currently
releases its entry only for an exception, not for a skipped original. No evidence
establishes that another installed mod caused this condition in the captured run.
The real guard finalizer does preserve a thrown exception and permit a retry.

Harmony confirms that skipping the original does not suppress finalizers:
https://harmony.pardeike.net/v2/articles/patching-prefix.html

### What the naval capture actually establishes

- Reward analyzer: no reported accounting problems.
- 1,330 naval scopes; zero player-clan scopes; 22 ship valuations.
- Recovery scopes: 639 with no ships, 10 with valuations, 16 with ships but no valuations/payment.
- Every valued recovery scope has matching before-ship and valuation sets.
- The 16 zero-valuation cases cannot be classified from current fields. Installed
  native recovery can legitimately return early for no clan, a bandit clan, no
  leader, or an inactive leader. The observer does not record all those conditions.
- No ship-destruction records exist in this capture. Native cleanup code has been
  inspected, but missing callbacks do not prove ships survived or were rewarded twice.
- Player selling-penalty behavior remains NOT_EXERCISED, not failed.

## Recommended fix order

1. **Cash completeness and honest profitability gates together.** Add read-only,
   net-of-nested observation around daily town workshop updates, expense handling,
   owner withdrawals, and ownership/capital reset boundaries. Snapshot owned shops
   at the appropriate finance boundary; preserve native ref arguments and never
   re-evaluate mutating finance methods. Feed window-aligned wallet residuals and
   hook provenance into profitability. Mark missing expense/payout evidence and
   nonzero residuals explicitly; keep owner payouts separate from operating costs.
2. **Naval skip semantics.** Record whether the original ran; distinguish skipped,
   returned-early, executed and failed calls. Release a newly entered guard when
   the original did not execute. Verify both patch orders, duplicate suppression,
   competing skips, native exceptions and retries using actual patch methods.
3. **Eligibility and cleanup evidence.** Record native recovery eligibility inputs
   without calling valuation models again. Add a wider party-removal/ship-list
   observation for cleanup, keeping transfers distinct from destruction. Preserve
   NOT_EXERCISED for player-only branches.
4. **Reassess balance only after those checks.** Existing data cannot provide an
   exact expense-versus-owner-payout split. Do not change noble income or wine,
   smithy, linen, livestock or wool rates to compensate for logging gaps.

These changes belong in sidecars/diagnostics and offline analyzers, never the
immutable Core DLL or WorldCalendar prefab. Historical logs must remain unchanged.
Any corrected historical report must still expose what cannot be recovered.

## Validation and remaining live requirement

Performed: full streaming cash audit; workshop, procurement transaction/accrual and
reward analyzer replays; installed native IL inspection; real-patch synthetic
coexistence and exception/skip tests; existing naval guard check; protected baseline
verification. Synthetic fixtures are separate from campaign evidence.

No broad rerun is needed to establish these defects or test their offline fixes.
After implementing them, one bounded live acceptance window must show the wider
hooks actually execute and residuals reconcile. Do not claim offline tests recover
missing historical receipts. Player-only naval behavior needs a targeted scenario
only if release acceptance requires certification of that branch.

## Separate capture-start warning

`AocSoakEvents.tsv` records at `2026-09-27T16:26:40.6782650Z`:
`SUPPLY_CAPTURE_FAILURE ... Campaign rollback; existing framework log preserved`.
The preserved capture remains the closed September 25 session; it is not a new
September 27 observation run. Do not silently follow another session, erase the
closed log, or change the user's save. Archival/new-session handling must be explicit
before a future acceptance capture. No capture controls were changed here.
