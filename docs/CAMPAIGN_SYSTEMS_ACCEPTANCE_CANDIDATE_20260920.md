# Revised framework acceptance candidate

Candidate: `C:\Users\fpicc\AocRelease\framework-08947cd140d0`.
Source snapshot: `31f9f7457da6687dfbf9742c32d141a72437364a`.
Security status: `offline_candidate_pass`, with
`no_detection_during_scan_and_hold`. All four archives (combined and the three
separate downloads) were scanned; the ten-minute hold, input/archive hash checks
and final protected-baseline check completed. Defender and real-time protection
were still enabled at handoff. This is a bounded scan result, not a permanent
security guarantee. Exact evidence is in this candidate's `verification.json`.
Not deployed and not approved for public Nexus upload.

## Included correction

The previous bc12fcdaa8bd candidate passed offline checks but lacked successful
pending-order persistence observations. It is superseded for this acceptance.
The revised procurement assembly emits the exact string passed to IDataStore
after SyncData returns while saving. This is serialization evidence, not a disk
commit guarantee. Listeners are optional and failures cannot propagate into saving.

The diagnostics assembly subscribes only during the bounded capture, records
campaign ID, campaign day, procurement MVID, SHA-256 and base64 payload, and
detaches on close/failure. Capture start reads the recovered saved string without
changing orders. The native Campaign.UniqueGameId is a saved property (80), so
normal campaign reloads preserve the comparison identity.

## Verified against the exact package

- Release builds: zero warnings/errors; protected Core/calendar/map checks pass.
- 84 procurement assertions, 40 framework assertions and 11 lifecycle assertions.
- Native save fixture verifies observed payload equals IDataStore payload and
  reload recovers it exactly across the existing native production scenarios.
- Real optional-ABI fixture verifies payload preservation, replacement of an
  existing subscription (no duplicate save rows) and detachment on close.
- Separate Player, Logistics and Diagnostics archives: exact file lists/hashes,
  no duplicate runtime payload, no protected artifacts and no player hard
  dependency on either optional module.
- Negative package fixtures reject removed border registration, altered Core
  identity, duplicate framework copy and missing consumer dependency.
- Native quest finite/Never/expired observations remain unchanged.

## Supplemental acceptance tooling

Use `acceptance-tools` within this candidate directory, commit
`5a3b1dfe15be9a826b2d52c3763ba4d2e27483b4`, for analysis. These supplemental sources
are separate from the unchanged packaged DLL source snapshot. They fix the
131072-character CSV reader default for large ledger observations, using a
bounded 24-million-character field limit. Seven reader entry points pass a
300000-character row fixture. All 50 Python tests pass in this committed snapshot.
The readers are local acceptance tools, not a new gameplay archive.

Compare-ProcurementReload.py takes explicitly selected BEFORE and AFTER capture
paths. It verifies closed/distinct segments, campaign/day/build/policy identity,
base64 integrity and exact saved/loaded obligation payload equality. An empty
ledger is NOT_EXERCISED. Matching payloads do not prove deliveries, balance or
disk persistence independently. Analyze each segment's normal integrity first.

## Live handoff

Follow CAMPAIGN_SYSTEMS_LIVE_ACCEPTANCE.md: one coordinated test containing one
deliberate save/reload and two explicitly armed capture segments. A short reload
segment is not a second long soak. No automatic checkpoints, speed changes,
campaign starts or game exits are introduced by these changes.

The download remains a private installed-baseline delta. Public distribution
still requires a verified published baseline or a tested additive update kit;
such an installer has not been implemented. Do not upload the private manifest.
