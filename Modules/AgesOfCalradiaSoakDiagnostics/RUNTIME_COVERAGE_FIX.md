# Runtime evidence correction, 2026-09-12

Boundary: diagnostics only. No calendar, balance, protected Core, UI, save or checkpoint changes.

The first live run exposed a real instrumentation defect: prefix `__state` was created by EconomyTransactionDiagnostics but result postfixes were declared on EconomyRecipeDiagnostics. Harmony keys state by declaring patch type. The helper received null and returned without recording bool, ExplainedNumber or float results. This also made recipe attempt counts zero and derived cadence values unreliable. Do not use the earlier run's RECIPE_CADENCE as a valid production-rate estimate.

Correction: all Harmony state entry points are now declared beside the prefix and delegate to the focused helper. Regression tests require emitted gate, model and demand rows as well as unchanged native results and exactly one evaluation. The actual native-install test checks the production prefix/postfix declaring-type contract for every installed target.

Party accounts now use session-local weak-reference object identities in addition to native StringId. Native party creation allocates the next currently unique string ID; a lifetime ledger must not assume IDs can never be reused. This prevents distinct objects sharing a balance account, but does not retrospectively establish that every earlier unexplained delta was an ID collision. Remaining deltas continue to be reported, not suppressed.

Observed transfer residuals included negative settlement payout requests crediting a hero while SettlementComponent.ChangeGold floors the settlement balance at zero. The observer retains the original residual, captures starting endpoint balances, and emits a separate NATIVE_SETTLEMENT_CLAMP classification only when both observed deltas exactly match that native pattern. This record overlaps transfer evidence; never add it as another cash flow. No native payout logic was changed. Further nested-event effects remain reviewable.

Release build, transaction behavior checks, bounded summary/native installation, raw coverage gate and protected-baseline checks are required before deployment. Runtime success still requires new evidence from the corrected DLL; static patch installation alone was insufficient in the previous build. Existing logs and run control are preserved. No automatic launch or intermediate checkpoint is introduced.
