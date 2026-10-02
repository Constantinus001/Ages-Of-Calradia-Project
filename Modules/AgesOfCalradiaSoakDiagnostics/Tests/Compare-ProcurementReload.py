"""Read-only comparison of two explicitly selected capture segments."""
import base64
import csv
import hashlib
import json
import math
import sys
import uuid


def details(row):
    return dict(piece.strip().split('=', 1) for piece in row['detail'].split(';') if '=' in piece)


def snapshot(rows, stage, last=False):
    sessions = {r['session'] for r in rows}
    if len(sessions) != 1 or not next(iter(sessions), ''):
        raise ValueError('Mixed/missing capture identity')
    if not rows or rows[0]['kind'] != 'SESSION_START' or rows[-1]['kind'] != 'SESSION_END':
        raise ValueError('Capture segment is not closed')
    if sum(r['kind'] == 'SESSION_START' for r in rows) != 1 or sum(r['kind'] == 'SESSION_END' for r in rows) != 1:
        raise ValueError('Duplicate session boundaries')
    observed = [r for r in rows if r['kind'] == 'PROCUREMENT_LEDGER' and r['metric'] == stage]
    if not observed:
        raise ValueError('Missing ' + stage + ' evidence')
    row = observed[-1 if last else 0]
    d = details(row)
    uuid.UUID(d['procurementMvid'])
    if not row['owner']:
        raise ValueError('Missing campaign identity')
    day = float(row['day'])
    if not math.isfinite(day) or day < 0:
        raise ValueError('Invalid campaign day')
    if len(d['payloadBase64']) > 22000000:
        raise ValueError('Ledger evidence exceeds bound')
    payload = base64.b64decode(d['payloadBase64'], validate=True)
    if hashlib.sha256(payload).hexdigest() != d['sha256'].lower():
        raise ValueError('Ledger hash mismatch')
    ledger = json.loads(payload.decode('utf-8'), parse_constant=lambda value: (_ for _ in ()).throw(ValueError(value)))
    if ledger.get('Schema') != 1 or not isinstance(ledger.get('Orders'), list):
        raise ValueError('Unsupported ledger')
    core = [details(r) for r in rows if r['kind'] == 'CORE_SYSTEMS' and r['metric'] == 'valid']
    if not core or any(r['kind'] == 'CORE_SYSTEMS' and r['metric'] != 'valid' for r in rows):
        raise ValueError('Missing/invalid Core evidence')
    identities = {(r['coreMvid'], r['policyRevision']) for r in core}
    if len(identities) != 1:
        raise ValueError('Core identity changed within capture')
    for build, policy in identities:
        uuid.UUID(build)
        if len(policy) != 64 or any(c not in '0123456789abcdefABCDEF' for c in policy):
            raise ValueError('Malformed policy identity')
    return row['session'], row['owner'], day, d['procurementMvid'], payload, ledger, identities


def compare(before, after):
    a = snapshot(before, 'serialized_for_save', last=True)
    b = snapshot(after, 'loaded_payload')
    if a[0] == b[0]:
        raise ValueError('Reload requires distinct capture segments')
    if a[1] != b[1] or a[3] != b[3] or a[6] != b[6]:
        raise ValueError('Campaign/build/policy differs across reload')
    if abs(a[2] - b[2]) > 1e-8:
        raise ValueError('Campaign time changed across reload boundary')
    if a[4] != b[4]:
        raise ValueError('Saved and loaded obligation payloads differ')
    count = len(b[5]['Orders'])
    return {'status': 'MATCH' if count else 'NOT_EXERCISED_EMPTY_LEDGER', 'orders': count,
            'campaign': a[1], 'before_session': a[0], 'after_session': b[0],
            'limit': 'Payload continuity only; delivery, disk-save completion and balance require separate evidence.'}


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit('Usage: Compare-ProcurementReload.py BEFORE.tsv AFTER.tsv')
    csv.field_size_limit(24000000)
    segments = []
    for path in sys.argv[1:]:
        with open(path, encoding='utf-8-sig', newline='') as stream:
            # Captures can reach 5 GiB: retain only boundary observations.
            rows, sessions = [], set()
            for row in csv.DictReader(stream, delimiter='\t'):
                sessions.add(row['session'])
                if len(sessions) > 1:
                    raise ValueError('Mixed capture sessions')
                if row['kind'] in ('SESSION_START', 'SESSION_END', 'CORE_SYSTEMS', 'PROCUREMENT_LEDGER'):
                    rows.append(row)
            segments.append(rows)
    print(json.dumps(compare(*segments), indent=2, allow_nan=False))
