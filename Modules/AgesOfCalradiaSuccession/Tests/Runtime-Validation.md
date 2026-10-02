# Succession runtime acceptance

Status: **not executed in a live campaign**. The automated harness passes 164
behavioral assertions using production code and native boundary doubles. It does
not certify these cases. Bannerlord was not running during the implementation
pass and native game UI control was unavailable in the agent session.

Use a separate disposable campaign with a saved copy before each destructive
case. `Verify-SuccessionDispatch.ps1` builds the normal module under
`%TEMP%\aoc-succession-verification\module` and an explicit diagnostics variant
under `diagnostics`; it never installs either. Only the diagnostics variant
registers the ruler-death and claimant-war test menus. Never copy or rebuild the
protected Core DLL or approved calendar prefab to enable these tests.

| Case | Required observable result | Live result |
| --- | --- | --- |
| AI ruler: old age, battle, execution | One hereditary accession; no ruler vote; unrelated decisions still work | Not run |
| Player realm ruler dies | Same hereditary result; no stale ruler-election screen | Not run |
| Main player dies and chooses an heir | Native player succession completes; kingdom and clan identities remain coherent | Not run |
| Adult and child hereditary branches, all five laws | Correct branch/house priority; religion cannot override valid hereditary continuation | Not run |
| Child heir, successive regent deaths, child death | Dynasty and lawful child preserved or replaced correctly | Not run |
| Child comes of age | One clan/kingdom authority transfer; regency cleared | Not run |
| Adult abdication and regent abdication | Outgoing leader excluded; regent abdication preserves child | Not run |
| Candidate-free throne gains a candidate later | Next daily audit queues one accession | Not run |
| Save before tick, after accession, during active war | Queue and crisis resume correctly; completed actions do not repeat | Not run |
| Automatic rebellion | Grace period, support/land thresholds and player exclusion hold | Not run |
| Claimant victory | Crown transfers; current rebel clans/fiefs reunify; empty shell eliminated | Not run |
| Claimant defeat, claimant death and negotiated peace | Incumbent retained; same reunification; no repeated uprising during cooldown | Not run |
| Captured claimant | Capture alone does not dissolve war; crown waits for eligible claimant | Not run |
| Third-party conquest/defection | No seized third-party lands or forcibly returned third-party clans | Not run |
| Native kingdom destruction during war | No resurrection or duplicate destruction | Not run |
| Opening peace integration | Peace respected; no continuous redeclaration loop | Not run |
| Ruler mods before and after Succession | One lawful accession, unrelated elections intact; inspect logged Harmony owners | Not run |
| Partial native failure / save inside callback | Quarantined state after load; no daily retry or coronation | Not run |
| Normal vs diagnostics module | Destructive options absent in normal build; present only in diagnostics | Not run |
| Coronation menu across campaign reload/switch | Current campaign state only; unresolved transfers disabled | Not run |

For each run record the game version, module order, source build hash, save name,
expected ruler/child/regent IDs and succession log excerpt. A failure should
produce a reproducible scenario and a corresponding regression, not merely a
changed expected assertion. Do not promote any row to Passed from the offline
harness results.

Existing release blockers outside Succession were reproduced: CalendarMath
expects schema 6 while the immutable approved Core reports 5; StrategicMapCoverage
fails its single-map-composer assertion. The succession sidecar cannot resolve
these by rebuilding protected artifacts. The full release gate has not passed.
