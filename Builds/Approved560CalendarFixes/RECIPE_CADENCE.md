# Recipe cadence correction — September 13, 2026

Status: implemented, verified locally and deployed September 13, 2026 at 02:59
local time. Both installed DLL hashes match the candidate hashes below; the
protected baseline passed before and after deployment. Not yet validated in a
live campaign. Deployment did not launch the game or arm a raw capture.

Previous installed sidecars are backed up in
`C:\Users\fpicc\Documents\Mount and Blade II Bannerlord\AgesOfCalradiaSoakDiagnostics\recipe-cadence-deploy-backup-20260913-025909`.

## Behavior and boundary

The calendar integration sidecar now classifies the current recipe, rather than
the entire workshop. With annual balancing enabled, a recipe having any output
with the native BonusToFoodStores property retains its native base speed. All
other recipes receive the existing calendar factor once. Annual balancing off
preserves every recipe's native base speed. Mixed-output batches remain whole.
This retains the installed felt category semantics; it does not relabel items.

The patch replaces one ConversionSpeed getter at each native model call in
WorkshopsCampaignBehavior.RunTownWorkshop, GetInputDailyChange and
GetOutputDailyChange (the latter two are explicit warehouse interface methods).
It retains the original virtual model call, so native bonuses still apply.
The original model-wide WorkshopProductionFix prefix has been removed.

Production is a struct. The replacement takes it by reference, matching the
original getter's stack signature. The transpiler preserves instruction count,
labels and exception blocks. Native caps, progress storage, success/failure
loops, prices, expenses and output ratios are not rewritten.

Compatibility: audited against installed Bannerlord 1.4.8. All three original
patterns are checked before module patching and legacy patch removal. A pattern
must contain exactly one getter followed by false and the expected model call.
Mismatch throws to the existing logged module-load rollback boundary. Unknown
mods that independently scale model inputs/results still require load-order
testing. No shared current-recipe state or save-schema changes are introduced.

## Verification

Both modified sidecars passed Release rebuilds without warnings/errors.
Verify-WorkshopRecipeCadence.ps1 loads the approved Core from the repository and
patches actual native methods in an isolated process. It passes classification
of industrial, food and mixed-output recipes, disabled annual balancing,
interleaved recipes, unchanged recipe definitions, all three targets, preservation
of all other native instructions/labels/exception blocks, and rejection of empty
or already-replaced call patterns. The prior compatibility suite passes 24 fixes,
including native perks, wages, tournaments, food and save-age contracts.

Transaction tests cover the new raw-only root caller evidence, including a known
caller frame and omission of filesystem paths. Daily summaries, coverage-gate
fixtures and soak controller checks pass. Protected political baseline passes.

The broader CalendarMath check still fails against the protected artifact's
schema 5 where the test expects 6. This unrelated mismatch is not repaired by
rebuilding the immutable Core. The broader strategic-map coverage verifier was
started but stopped during its lengthy asset scan without a result; it is not
claimed as passing. No general release approval is claimed.

Local candidate SHA-256 values:

- Calendar sidecar: `71E99C06DF87713C66296302409DE9754342063879009A01497BD7687F92D6D3`
- Diagnostics sidecar: `FB75565ED7C12420A9ABD8421B15D6CA349F198EBB6BE09BFD2F9657A389A4F2`

## Next runtime evidence

Use the same copied starting save and settings for control and candidate, with
matching campaign-day intervals at 2x. First use a brief raw capture, checking
growth against its 1 GiB limit; do not assume a 15–30 minute full raw trace fits.
The existing AocEconomyTransactions.enabled marker selects raw mode at session
load. Building this candidate does not create it or start/restart the game.

Check iron/hardwood artisan recipe progress and realized cycles against ordinary
shops. Track livestock-to-meat and wool-to-felt village supply, consumption,
market-stock samples and rejected-input gates. Those chains have demonstrated
shortages, but the candidate's effect and any additional narrow supply correction
must be measured before choosing a multiplier.

For wealth, inspect raw rootCallerStack and transaction ancestry; reconcile actual
leader-wallet changes for an identical cohort against grants, trades and finance
side effects. No blanket income or workshop-capital adjustment is included.
Reassess wine, smithy and linen viability after production/input effects are known.
Actual bankruptcy, warehouse operation and save/reload acceptance remain live tests.
