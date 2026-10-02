"""Read-only framework/quest evidence; never certifies balance or quest scaling."""
import csv
csv.field_size_limit(24000000)  # Bounded base64 ledger observation; native payload <= 4M chars.
import math
import re


def analyze(path):
    problems, revisions, builds, deadlines = [], set(), set(), {}
    snapshots = finite = never = summaries = 0
    changes = []
    sessions = set()
    started = ended = False
    with open(path, encoding='utf-8-sig', newline='') as source:
        for row in csv.DictReader(source, delimiter='\t'):
            sessions.add(row['session'])
            kind = row['kind']
            if ended:
                problems.append('Rows after SESSION_END')
            if kind == 'SESSION_START':
                if started:
                    problems.append('Duplicate SESSION_START')
                started = True
            if kind == 'SESSION_END':
                ended = True
            if kind not in ('CORE_SYSTEMS', 'QUEST_DEADLINE', 'QUEST_COVERAGE'):
                continue
            values = [float(row[k]) for k in ('day', 'before', 'after')]
            if not all(math.isfinite(v) and v >= 0 for v in values):
                raise ValueError('Invalid framework observation numeric field')
            day, before, after = values
            detail = dict(part.strip().split('=', 1) for part in row['detail'].split(';') if '=' in part)
            if kind == 'CORE_SYSTEMS':
                snapshots += 1
                if row['metric'] != 'valid':
                    problems.append('Core status: ' + row['metric'])
                revision = detail.get('policyRevision', '')
                build = detail.get('coreMvid', '')
                if not re.fullmatch(r'[0-9a-fA-F]{64}', revision):
                    problems.append('Missing/invalid effective policy revision')
                else:
                    revisions.add(revision.lower())
                if not re.fullmatch(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}', build):
                    problems.append('Missing/invalid Core build identity')
                else:
                    builds.add(build.lower())
            elif kind == 'QUEST_COVERAGE':
                summaries += 1
                if before != after or after != int(after) or row['metric'] != ('none_active' if after == 0 else 'observed'):
                    problems.append('Inconsistent quest coverage summary')
            elif row['metric'] == 'finite':
                finite += 1
                remaining = float(detail.get('remainingDays', 'nan'))
                if before != day or not math.isfinite(remaining) or abs(remaining - max(0, after - day)) > 1e-7:
                    problems.append('Quest remaining time mismatch: ' + row['owner'])
                previous = deadlines.get(row['owner'])
                if previous is not None and previous != after:
                    changes.append({'quest': row['owner'], 'previous_due': previous, 'due': after, 'day': day})
                deadlines[row['owner']] = after
            elif row['metric'] == 'never':
                never += 1
                if before != day or after != day:
                    problems.append('Never sentinel encoded as a deadline')
            else:
                problems.append('Unknown quest deadline kind')
    if not started or not ended or len(sessions) != 1:
        problems.append('One closed session required')
    if len(revisions) > 1 or len(builds) > 1:
        problems.append('Framework build/policy changed within capture')
    gaps = []
    if not snapshots:
        gaps.append('framework_identity_not_observed')
    if not summaries:
        gaps.append('quest_summary_not_observed')
    if not finite:
        gaps.append('finite_quest_deadlines_not_exercised')
    return {'problems': sorted(set(problems)), 'coverage_gaps': gaps,
            'core_snapshots': snapshots, 'policy_revisions': sorted(revisions), 'core_builds': sorted(builds),
            'finite_quest_observations': finite, 'never_quest_observations': never,
            'deadline_changes_require_context_not_automatic_bugs': changes,
            'scope': 'Saved deadline observations only; does not prove deadline creation scaling or completion behavior.'}
