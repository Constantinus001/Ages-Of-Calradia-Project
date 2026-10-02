# Sundial and clock synchronization

The native strip is 176 x 50 in a 78 x 50 widget, with second-layer offset 15.
Scale these uniformly by 1.68: strip 295.68 x 84, widget 131.04 x 84, offset
25.2. The custom radius-42 aperture is fully covered. Production scales the
entire center UI by a further 0.8. Neither texture axis is stretched alone.

The earlier 72.413793-high strip left a gap in a 105-high widget. Increasing
only its height to 105 filled the gap but distorted the sun/moon. Both versions
are superseded. Native clip radius 29 is not the sprite scaling reference;
the custom aperture crops more tightly than vanilla.

The existing Tick postfix refreshes native VM Time from campaign hours each
UI tick. DayTime stays bound to Time, and clock minutes now derive directly
from that same value. There is no independent minute catch-up clock. Pause
and speed changes follow campaign time without changing simulation speed.
Protected Core/calendar artifacts stay intact.

Verification covers proportional geometry, aperture coverage, native preview,
and dial-to-minute conversion including midnight/day wrap. A still preview
does not prove live animation smoothness or unmodified-game A/B equivalence.

Season hover: the moving arrow, season arch, and complete central dial display the current
season name and calendar days remaining (including the current day). The arch
accepts hover events; the sundial retains its camera-reset click command.
All hovered descendants count as being inside the dial. The old TimeOfDayHint
is removed from this assembly so it cannot compete with the season tooltip.
The hint is refreshed across day/season changes and ends when the cursor leaves
all targets. Tests cover arch hit-test configuration, all season names,
singular/plural wording, and approved-calendar day/season boundaries.
