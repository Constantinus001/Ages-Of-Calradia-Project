# Season indicator diagnostics

Create `Documents/AOC Diagnostics/season-indicator.enabled` to opt in. Remove
that marker to stop logging. Normal releases do not enable it automatically.

`season-indicator.log` records first display and season-day changes, including
rewinds. Fields include season, one-based day within season, season length,
days remaining (including today), widget x/y, inward rotation in degrees,
radius, and campaign versus preview source. A changing position across two
campaign-day records confirms calculated movement, not on-screen rendering.
Hovering over the indicator still displays the remaining-day hint.

Logs rotate at approximately 1 MiB to `season-indicator.previous.log`. File
errors disable logging for that widget without affecting the campaign. Missing
calendar APIs hide the indicator and produce an error entry when logging is enabled.
