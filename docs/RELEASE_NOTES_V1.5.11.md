# Ages of Calradia v1.5.11

This release preserves the user-approved political-map renderer exactly while
shipping calendar, economy, campaign pacing, and UI fixes in an isolated
compatibility sidecar.

## Fixes

- Tournaments resume spawning and ending on the extended calendar.
- Workshop production respects the adopted calendar cadence without breaking
  perk, policy, or building modifiers.
- Siege preparation and siege-engine work use the same configurable campaign
  time scale as the visible clock.
- Troop wages and clan-finance UI show effective Gregorian daily values without
  double-scaling the amount charged.
- War proposal cadence uses the corrected 87-day post-peace cooldown.
- Marriage, allegiance, kingdom, and similar scene notifications use valid
  Gregorian dates.
- World Events UI strategic-map town names remain unchanged. Campaign-map town
  nameplates are hidden when the political overview begins at altitude 580;
  below that threshold all settlement nameplates use Bannerlord's native
  dimensions and visibility rules. The campaign camera's maximum zoom-out
  height is doubled for full-map border work.
- Political drafting mode retains native faction colours, inland ownership
  borders, and faction-name labels. World-space ocean and lake shorelines use
  land-elevated coastal ribbons while IslandExclusion, ordinary
  banners, and World Events UI remain unchanged.

## Builds

- `AgesOfCalradia-v1.5.11.zip` is the clean player release.
- `AgesOfCalradia-v1.5.11-Test.zip` is a prerelease test build with World Events
  alignment diagnostics and matching sidecar symbols.

Both archives require Bannerlord Native v1.4.8 and contain the approved main
DLL SHA-256 `560F1B5181F8CC2EFE51564D8675FD3089E722606FA55B0B166D36ECD9868D8E`.

Remove an older `AgesOfCalradia` module folder before extracting the selected
archive into Bannerlord's `Modules` directory. Do not install both archives at
the same time.
