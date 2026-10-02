"""Policy decisions are not payments. Legacy receipts cannot certify this policy."""
import collections
import csv
import json
import math
import struct
import sys

csv.field_size_limit(24000000)  # Same bound as the framework's ledger readers.


def f32(value):
    return struct.unpack('f', struct.pack('f', value))[0]


def analyze(path):
    counts = collections.Counter()
    problems, gaps, revisions = [], set(), set()
    status = None
    incomplete = False
    closed = False
    with open(path, encoding='utf-8-sig', newline='') as stream:
        line_complete = True
        def lines():
            nonlocal line_complete
            for line in stream:
                line_complete = line.endswith(('\n', '\r'))
                yield line
        for row in csv.DictReader(lines(), delimiter='\t'):
            kind = row.get('kind') or ''
            if kind == 'SESSION_END':
                closed = True
            if incomplete:
                problems.append('Nonterminal incomplete policy row')
                incomplete = False
            if not kind.startswith('NAVAL_POLICY_'):
                continue
            if not line_complete or any(value is None for value in row.values()):
                incomplete = True
                continue
            detail = row.get('detail') or ''
            if 'policy_v1;' not in detail:
                problems.append('Unsupported naval policy schema')
                continue
            fields = dict(part.strip().split('=', 1) for part in detail.split(';') if '=' in part)
            if kind == 'NAVAL_POLICY_STATUS':
                status = detail
                continue
            if kind != 'NAVAL_POLICY_DECISION':
                problems.append('Unknown naval policy record: ' + kind)
                continue
            metric = row.get('metric')
            revisions.add(fields.get('revision'))
            try:
                if metric == 'quote':
                    route = fields['route']
                    if route not in ('sale', 'recovery', 'battle'):
                        raise ValueError('unknown route')
                    counts[route] += 1
                    native, effective, hull, factor, returned = (float(fields[k]) for k in
                        ('nativeBase', 'effectiveBase', 'hull', 'hullBasis', 'returnedQuote'))
                    if not all(math.isfinite(v) for v in (native, effective, hull, factor, returned)):
                        raise ValueError('nonfinite value')
                    expected = native if fields['alreadyDiscounted'] == 'True' else f32(f32(native - hull) + f32(hull * factor))
                    if fields['basisValid'] != 'True' or not .01 - 1e-8 <= factor <= 1 or min(native, effective, hull, returned) < 0:
                        raise ValueError('invalid policy basis')
                    if not math.isclose(expected, effective, rel_tol=1e-6, abs_tol=.001):
                        raise ValueError('hull normalization mismatch')
                elif metric == 'allocation':
                    counts['allocations'] += 1
                    native, effective = int(fields['nativeTotal']), int(fields['effectiveTotal'])
                    if fields['applied'] == 'True':
                        prior, contribution, total, pool = (int(fields[k]) for k in
                            ('priorGold', 'contribution', 'contributionSum', 'effectivePool'))
                        if total <= 0 or contribution < 0 or contribution > total or not 0 <= pool <= 2147483647:
                            raise ValueError('invalid allocation operands')
                        expected = prior + math.floor(f32(f32(f32(contribution) / f32(total)) * f32(pool)))
                        if fields['poolValid'] != 'True' or effective != expected:
                            raise ValueError('allocation mismatch')
                        counts['applied_allocations'] += 1
                    elif fields['applied'] != 'False' or effective != native:
                        raise ValueError('native allocation changed without policy')
                elif metric == 'rejected':
                    gaps.add('policy_rejected_quote')
                else:
                    raise ValueError('unknown policy decision')
            except (ValueError, KeyError, TypeError, OverflowError) as error:
                if len(problems) < 50:
                    problems.append(str(error))
    if status is None:
        gaps.add('policy_unavailable_in_legacy_capture')
    elif 'enabled;' not in status:
        gaps.add('policy_not_enabled')
    if incomplete:
        if closed:
            problems.append('Incomplete policy row after session end')
        else:
            gaps.add('unfinished_policy_tail_while_writer_may_be_active')
    if None in revisions or len(revisions) > 1:
        problems.append('Missing or changing campaign policy revision')
    for route in ('battle', 'sale', 'recovery'):
        if not counts[route]:
            gaps.add(route + '_policy_not_exercised')
    # This standalone arithmetic check cannot prove wallet or cleanup joins.
    gaps.add('payment_lifecycle_and_player_isolation_require_companion_evidence')
    return dict(status=status, counts=dict(counts), revisions=sorted(r for r in revisions if r),
                problems=problems, coverage_gaps=sorted(gaps),
                limit='Checks policy arithmetic only; quote counts and allocations are not additive income.')


if __name__ == '__main__':
    print(json.dumps(analyze(sys.argv[1]), indent=2))
