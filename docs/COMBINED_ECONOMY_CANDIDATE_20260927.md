# Combined economy candidate and single acceptance plan

This replaces the proposed iron-only handoff. Status: privately deployed on
2026-09-27 after combined offline and isolated package verification; not
Nexus-certified or live-accepted. No per-fix game run or checkpoint is requested.

Deployment: `C:\Users\fpicc\AocRelease\framework-a99ae8a4d8ce\verification.json`.
Binary rollback copies and receipt: that run's
`private-test-deployment-f4ea5f4a1664` directory. Protected Core/UI verified before
and after. The previously absent Documents campaign configuration was created
with the four reviewed overrides; rollback instructions are in CONFIGURATION-NOTE.md
beside the binary receipt. Existing launcher selection, saves and speed unchanged.
Prior current capture was hash-archived by Prepare-AcceptanceCapture. The next
combined capture is prepared for 16 campaign days, READY_TO_LOAD_NOT_RECORDING.
Actual loaded build, policy, hooks and recording must be verified after save load.

## Implemented together

1. Correct battle-allocation analysis: missing legacy identity/upstream evidence
   cannot manufacture either a mismatch or a verified join. Real mismatches
   remain failures. Current capture retains 689 verified commits.
2. Opt-in iron reserves based on current local recipe demand, including hidden
   artisans and the authoritative calendar/native model rate. Candidate: 14 days,
   stock floor retained. Unknown demand rejects new offers, not existing cargo.
3. Capped lead-time-sized procurement lots across eligible recipes. Candidate:
   maximum 12 batches, versus the existing baseline three. Requested size is
   max(baseline, ceil(daily rate * (route travel days + reorder buffer))), capped
   by the setting. Every quantity, quote, freight amount, reserve, profitability,
   cash and cargo-bound check uses the actual lot. If the larger lot fails,
   the original small lot is still considered. No extra simultaneous order.
4. Delivery-aware supplier selection. Candidate weight 1 compares landed cost
   plus estimated daily expense during uncovered travel, divided by lot size.
   Covered days use current available complete input bundles / recipe rate.
   This is a selection estimate, never a payment, realized loss or profit claim.
   Both candidates must still pass all native-margin/capital/route safety checks.
5. Combined diagnostics retain source, recipe, lot size, lead time, cadence,
   conservative output quote, actual landed cost, daily expense, selection score,
   policy settings/revision, rejection classes and planner compute milliseconds.
   Compute timing excludes logging. Existing writer timing remains separate;
   neither is an FPS or complete observer-overhead measurement.
6. Opt-in wine-only operating margin. Candidate WineExpenseCoverage=1.25;
   default 0 preserves native behavior. Single-recipe, non-hidden AI workshops
   producing wine use min(native hurdle, ceil(daily expense * coverage / model-
   derived potential daily rate)). No production-rate or price change. Native
   capital, input and town-cash gates and strict profit comparison survive.
   Player, initialization, ownerless, other-output and multi-recipe cases retain
   native margins. Missing cadence falls back to native. Planning uses the same
   function and includes actual landed costs. Coverage is not realized profit.
   Gate diagnostics observe the scoped evaluated margin without invoking the
   rate model again; unknown observation is not silently replaced by native.

Architectural boundary: validated Campaign Systems configuration, procurement
planning integration, native wine-margin integration and offline diagnostics.
One new Harmony patch shares the existing CanNotableWorkshopProduceThisCycle
target: exact 200 / ConversionSpeed IL expression only. Pattern mismatch rejects
procurement installation before patching. No protected Core edit, calendar change,
production multiplier, income cut or save migration.
All new settings default off; legacy XML remains valid and preserves behavior.
The combined XML selects all four policies together. When deployment is later
authorized, merge these attributes into the user's settings, preserving unrelated
overrides; reject conflicts rather than silently reducing BatchesPerOrder.

The public schema-1 ledger already supports these bounded lot sizes. Original
quantity, cash basis, partial-batch quarantine, ownership, delayed arrival,
liquidation/refunds and duplicate-order checks remain intact. Old orders retain
their snapshots. Reverting configuration stops the new planning policy without
discarding existing cargo; old binaries cannot parse new XML attributes, so a
binary rollback must also restore compatible configuration.

## Reviewed areas with no unsupported balance mutation

| Area | Evidence and disposition |
| --- | --- |
| Livestock → meat | Input failures exist, but this capture does not prove a rate defect. Preserve native supply exemptions, food protection and hidden-workshop rules. Do not claim the procurement candidate directly supplies hidden artisans. |
| Wool → felt | Native felt semantics and wool exemption are intentional. A uniform day-reserve formula worsened sampled wool stock eligibility; retain wool reserves. Eligible wool procurement can use bounded lots and delivery ranking. |
| Wine/smithy/linen | Supply/timing changes apply to eligible recipes. The two losing wine presses had input-accepted margin failures; the opt-in wine-only operating-margin policy addresses that gate, not proven demand weakness. Smithy and linen retain native margins. Realized profitability remains unverified. |
| Noble wealth/naval rewards | Recorded payouts/cleanup reconcile; native ship monetization is a large observed source in the recent capture. Duplicate-reward guard verified, not a new proven duplicate bug. Historical 28.19-million growth now reconciles exactly to the same 97 observed leaders' setter flows; the dominant generic character-transfer bucket still lacks complete purpose attribution. No blanket income nerf. See the historical audit below. |
| Continuous replenishment | Larger lots and earlier triggers reduce repeated transit exposure, but a one-order ledger cannot eliminate all replenishment gaps. Two simultaneous orders require a separately designed persistence/accounting change; this candidate does not secretly weaken duplicate protection. |

The candidate is therefore broader than iron-only, but is not an honest basis
for saying every economic complaint is solved. A coordinated package is not a
reason to introduce unmeasured multipliers or conceal unresolved design limits.

## One offline gate

Run `Tests/Verify-CombinedEconomyCandidate.ps1` from the repository. It builds
the sidecars and runs procurement/native/coexistence, protected baseline/calendar/
strategic-map, framework, naval guard, logistics, capture readiness, readiness
policy, Python analyzers and native quest observation checks. It writes a unique
receipt and logs under output/combined-economy-check-* with artifact hashes.
It does not deploy, arm diagnostics, start the game, alter saves, scan Defender
or certify a public release. Synthetic logs stay in isolated fixture directories.

Current checks: zero-warning/error Release builds, 117 procurement assertions,
67 framework assertions, native combined-policy and save-serialization fixtures,
155 Python tests, and the other named gates. A Windows PowerShell wrapper issue
initially mistook unittest's normal stderr output for a failure; the wrapper now
records native exit status and preserves actual failures. It does not suppress
test failures. Evidence of a passed gate is the final receipt, not this checklist.

Latest receipt: `output/combined-economy-check-20260927-215257-76ea3ba2/receipt.json`.
All eight grouped checks passed. Combined default-based policy revision:
`45D53196034FF51BEB0CEF73A7EC69612123F7F6C6385FE489CAB2E9690B55A3`.
Preserved user overrides can legitimately produce a different policy revision;
verify the actual merged configuration rather than assuming this fingerprint.

## Historical wealth audit, verified offline

`Analyze-HistoricalWealth.py` joins only HERO_GOLD setter summaries to stable
observed clan-leader identities. Eight regression tests cover cohort selection,
changed leaders, incomplete windows, duplicate identities, outside/multiple/open
sessions, invalid aggregates, real residuals and mirrored-wallet exclusion.
It does not reconstruct missing transaction ancestry or certify intraday identity
stability from daily last-value summaries.

Replay source: `AocEconomyDaily-a254bc8dd38048b8818a2f8d2f325828.tsv` in the user's
Bannerlord AgesOfCalradiaSoakDiagnostics directory. All 9,918,796 rows processed;
day 396026 to 396378; 97 eligible leaders, no excluded clans. Endpoint wealth
10,875,689 -> 39,069,078: increase 28,193,389. Observed setter net 28,193,389;
endpoint-minus-summary residual zero. Edge-day gross timing ambiguity: 299,433.
That bound remains relevant despite the exact aggregate match.

Recorded caller buckets (not fully attributed economic purposes):

| Caller | Net gold |
| --- | ---: |
| Generic ApplyBetweenCharacters | +29,605,042 |
| ApplyBetweenCharacters with DailyTickClan | -1,978,803 |
| PartyTradeGold setter | +1,401,577 |
| Character-to-settlement item sales | -1,277,368 |
| Settlement-to-character item sales | +392,019 |
| Party-to-character | +54,514 |
| Party-to-settlement via PartyTradeGold | -2,981 |
| Recorded clan-finance expense path | -611 |

Decision: the arithmetic is no longer unexplained, but the dominant generic
transfer purpose still is. This does not establish money creation, grants,
duplicate battle rewards, or excessive tax income. Do not apply a blanket
income reduction based on this legacy file. Recent raw attribution must be
evaluated independently; do not claim it retroactively identifies old transfers.

The Time Lord-inspired calendar-policy integration is explicitly deferred until
the coordinated economy fixes are complete. No code for it was changed.

## One planned campaign acceptance, only after deployment approval

No run is requested now. Before accepting future gameplay evidence, verify:

- Exact deployed hashes/MVIDs and loaded combined policy revision, not merely
  the local source build; same save/campaign identity and preserved prior logs.
- Recording proof, required hooks, writable log, adequate space, no stale
  closure marker, dropped records or confusing mixed/rollback session windows.
- Supply and workshop, canonical wallet, naval lifecycle and planner evidence
  are enabled together. Keep routine summaries light and focused raw detail.

During that single run evaluate all of the following together:

- Orders use intended lot/reserve/selection policy; supplier stock, buyer
  capital/wage protection, arrival timing, consumption and cost basis reconcile.
- Livestock/meat and wool/felt input gates, production, actual deliveries,
  consumption and demand observations remain distinct; zero stock is not proof.
- Wine, smithy and linen operating results exclude owner withdrawals. Report
  margin failures separately from input failures, delayed goods and town cash.
- Noble/hero/kingdom wallets reconcile with attribution completeness stated;
  do not sum allocations, transfers and the corresponding cash a second time.
- Naval ship valuation/payment/cleanup joins and optional player penalties are
  evaluated if exercised; absence is a coverage gap, not grounds to force events.
- Planner compute/writer cost, log growth, drops and measured wall seconds per
  campaign day are reported, including pauses in the timing interpretation.

The window must include route arrival plus a subsequent recipe opportunity for
each targeted observed chain. Do not promise a fixed short duration before seeing
those schedules, or close the capture just before a known delivery. Use the
existing comparison baseline; do not demand another unchanged control replay.
No manual checkpoints. Unexercised rare events remain named limitations instead
of automatically triggering another run. A failed acceptance may still require
a focused correction; no honest workflow can guarantee zero follow-up forever.
