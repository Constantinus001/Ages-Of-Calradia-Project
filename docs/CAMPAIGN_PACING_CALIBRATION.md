# Campaign pacing calibration

Current user-approved target: 80 seconds per day at 1x, 40 at 2x, 20 at 4x.
The native conversion factor is now 80/80. Verifiers and calibration expected
values use this target. The four-hour-year calculation below records the
previous calibration diagnosis, not the current requested speed.

The approved sidecar alone owns the TickMapTime prefix; protected Core remains
unchanged. Native 1.4.8 advances 4320 * 0.25 * input delta * speed game seconds.
With 86400 game seconds per day, the unscaled duration is 80 seconds/day.
The four-hour/365.2425-day target requires a factor of 2.029125, not 0.60225006.

The independent verifier computes all three day durations from the native formula
and the actual sidecar speed selectors: 39.425861, 19.712930, 9.856465 seconds.
This proves conversion arithmetic, not actual wall-clock performance. Runtime
calibration must additionally measure engine input delta versus elapsed wall time.
Do not compensate for stalls, load screens, background throttling or missing ticks
by applying an unexplained global multiplier. Better Time is unsupported.
