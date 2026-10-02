# Four-input supply candidate

September 13, 2026 at 03:28 local: deployed after checking the installed DLL was
unlocked. Installed SHA-256 matches tested candidate
`EDA70BD78EE477C8788732655762DDAAC0479D62A424473EBB005963D0B11C41`.
The previous sidecar was backed up under the Documents diagnostics directory,
`input-supply-deploy-20260913-032811`. Protected baseline passed before/after.
One-shot comparison capture is armed; live supply effects remain unverified.

The existing DefaultVillageProductionCalculatorModel.CalculateDailyProductionAmount
postfix now preserves native final output for category IDs cow, sheep, hog and
wool, alongside all existing BonusToFoodStores categories. Other inputs retain
annual conversion; annual balancing disabled preserves native results everywhere.
No new Harmony target, shared state, settings or save contract was added.

This acts after native village eligibility, hearth, perks and building modifiers.
It does not create output when the native result is zero. Native inventory
rounding, dispatch, trade, market demand and recipe batch ratios are unchanged.
Native output is about 4.35 times the previous adjusted rate for these four
inputs; oversupply and price effects require a short runtime comparison.

Release rebuild passed without warnings/errors. Verify-VillageInputSupply.ps1
passed four-category inclusion, ordinary horses/materials and unknown-category
exclusion, food-property preservation, annual-off, zero production and native
bonus/explanation preservation. The same invocation passed the full sidecar
compatibility suite and recipe/warehouse transpiler tests on installed native
methods. Protected political baseline passed after building. Existing broader
calendar schema 6-vs-5 and unfinished strategic-map checks remain outside this
change; no full release approval or in-game supply improvement is claimed.

Deployment requires the game and launcher to release the installed sidecar.
After deployment, arm the existing one-shot capture before loading the same
starting save, use 2x, and compare raw supply and stock evidence against session
b453d67f42f54f658de0e89247397c72. No further income or workshop-capital changes
are included.
