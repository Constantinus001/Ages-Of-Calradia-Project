# Calradia Campaign Clock v1.0.0

Initial standalone release of the numeric Bannerlord campaign-map clock and
its synchronization fixes.

## Included

- Native campaign-hour numeric display beside the map-bar sundial.
- Synchronized clock and sundial updates during normal and accelerated time.
- Correct minute flooring, midnight/noon rollover, and AM/PM formatting.
- Configurable 12-hour, two-line AM/PM, and 24-hour display modes.
- Additive UIExtenderEx integration without a full `MapBar.xml` replacement.
- Duplicate-owner guard when the protected Ages of Calradia clock is active.

## Requirements

- Mount & Blade II: Bannerlord v1.4.8 single-player
- Bannerlord.Harmony v2.4.2 or compatible
- Bannerlord.UIExtenderEx v2.13.3 or compatible

The player ZIP contains only the module descriptor, settings, README, and the
runtime DLL. It contains no installer, executable utility, source, build cache,
PDB, nested archive, or bundled third-party dependency.
