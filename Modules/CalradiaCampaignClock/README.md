# Calradia Campaign Clock

Calradia Campaign Clock is a standalone Bannerlord v1.4.8 module that adds the
numeric campaign time beside the native map-bar sundial. It contains the clock
display and its synchronization fixes only.

## Included behavior

- Derives the display from the same native `CampaignTime.Now.ToHours % 24`
  expression used by Bannerlord's map-time ViewModel.
- Writes that canonical hour to the inherited sundial and the numeric clock.
- Refreshes after native `MapTimeControlVM.Tick()`, including accelerated time,
  and publishes a UI change only when the displayed minute changes.
- Floors partial minutes so the label never displays a future minute.
- Correctly handles noon, midnight, negative modulo normalization, and
  12-hour AM/PM transitions.
- Supports a compact 24-hour clock or a 12-hour clock with AM/PM on a second
  line.

The module does **not** change campaign pacing, fast-forward speed, the length
of a day or year, seasons, dates, saves, weather, lighting, economy, population,
or political rendering.

## Requirements

- Mount & Blade II: Bannerlord v1.4.8 single-player
- Bannerlord.Harmony v2.4.2 or compatible
- Bannerlord.UIExtenderEx v2.13.3 or compatible

UIExtenderEx is required because the clock is inserted as one additive widget.
The module deliberately does not package a replacement `GUI/Prefabs/Map/MapBar.xml`,
which reduces conflicts with map-bar and HUD overhauls.

## Settings

Edit `CalradiaCampaignClock.settings.xml` before launching the game:

```xml
<CampaignClockSettings
  Use24HourClock="false"
  ShowMeridiemOnSecondLine="true" />
```

`Use24HourClock="true"` displays `00:00` through `23:59` and does not append a
redundant meridiem. With the 12-hour clock, `ShowMeridiemOnSecondLine="false"`
uses a single-line value such as `9:26 PM`.

## Compatibility and failure behavior

The UI extension targets the `Children` collection of the native MapBar
`MapCurrentTimeVisualWidget` whose ID is `CenterPanel`. If a game update or UI
overhaul removes that target, UIExtenderEx cannot insert the label, while the
native sundial and time-control buttons remain intact.

When `AgesOfCalradia.dll` already exposes
`TwelveMonthCalendar.CalendarMapTimeControlVM`, this module disables itself.
That avoids a duplicate label and two refresh owners while the protected Ages
of Calradia build still contains its original clock. The standalone module is
intended to run independently; a future Ages of Calradia bridge can retire the
legacy owner without rebuilding the protected DLL.

Failures are logged to
`%LOCALAPPDATA%\Mount and Blade II Bannerlord\Logs\CalradiaCampaignClock.log`.

## Verification

From the repository root:

```powershell
dotnet msbuild .\Modules\CalradiaCampaignClock\CalradiaCampaignClock.csproj /t:Rebuild /p:Configuration=Release
& .\Modules\CalradiaCampaignClock\Tests\Verify-CampaignClock.ps1
& .\Tests\Verify-ProtectedPoliticalBaseline.ps1
```

Runtime acceptance still requires an in-game pass at pause, normal speed,
fast-forward, 11:59 to 12:00, and 23:59 to 00:00.

## Player release package

Create the Nexus/GitHub player archive with the guarded packaging script:

```powershell
& .\Modules\CalradiaCampaignClock\New-CampaignClockRelease.ps1
```

The generated `Modules\CalradiaCampaignClock-v1.0.0.zip` contains only
`SubModule.xml`, the settings XML, this README, and the runtime DLL. Do not
upload a ZIP made directly from the source directory because that would include
test executables, build caches, PDBs, and local build metadata.
