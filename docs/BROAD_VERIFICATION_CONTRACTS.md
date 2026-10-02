# Broad verification contracts — September 13, 2026

The two previously unresolved checks had verification-boundary problems, not
evidence requiring changes to the protected Core DLL or World Events prefab.
No game assets, gameplay settings, profiles, saves or deployed files were edited.

## Calendar

Verify-CalendarMath used schema-6 development-source expectations while loading
the immutable SHA-256 560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E
artifact. Direct inspection of CalendarCampaignProfile and CalendarSettingsState
in that binary confirms schema 5, base automatic scale 0.15 and fast-forward 4;
development source uses schema 6, scale 0.803 and fast-forward 2. These are Core
contract defaults, NOT measurements or assertions about active sidecar pacing.

The verifier now exposes Auto, Protected560 and Development contracts. Auto
selects Protected560 only for the exact approved hash; every other artifact must
satisfy the Development contract. Protected560 explicitly rejects any other hash.
Expected values are fixed constants, never copied from the result under test.
Schema-5 automatic profiles remain unchanged under Protected560; the original
schema-5-to-6 automatic migration requirements remain under Development.
Calendar math, age preservation, legacy migrations, manual settings, fingerprints,
four-cycle multi-profile reloads and native target audits are retained.
Single-precision scales use a 0.000001 absolute tolerance rather than comparing
float values against double literals for exact binary equality.

Default tests do not certify undeployed schema-6 development code. Test that
explicitly with -CalendarAssemblyPath <development DLL> -Contract Development,
only when separately authorized to build that development artifact.

## Strategic Map

The old single-composer check counted every TextureProviderName in WorldCalendar,
including unrelated treasury pages, banners, backgrounds and tabs. It now requires
exactly one CalendarStrategicCampaignAtlasTextureProvider. Existing checks still
require both engine legend icons and reject the unsafe direct marker providers.

The long-running pixel checks were exhaustive PowerShell GetPixel loops. Their
identical ARGB/alpha/id predicates now execute in a test-only compiled helper.
Every pixel and atlas offset is still examined. No sampling, asset rewriting,
image conversion, relaxed coverage threshold or omitted index checks were added.
The 97.5% mask coverage, 133 settlement mappings, index border preservation and
generated split-index authoring checks remain intact. The source comment now
correctly distinguishes that authoring index from the active original-province
composer, matching the preexisting source assertions.

## Verification

Verify-CalendarMath passes against Protected560. Verify-StrategicMapCoverage
completes in seconds and passes. Verify-BroadCheckContracts exercises positive
and deliberately corrupt pixel/index fixtures, missing/duplicate map composers,
unrelated provider acceptance, schema-6 mismatch rejection and protected-hash
rejection. This resolves the previous broad-check blockers, not live shipment
acceptance or the complete release gate from a clean committed tree.
