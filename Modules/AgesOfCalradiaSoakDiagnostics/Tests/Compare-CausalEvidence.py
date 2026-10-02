"""Compare two exact captures using explicitly reviewed, hash-bound contexts.

Context JSON keys: capture_sha256, campaign_id, settings_sha256, build_sha256,
window_start_day, window_end_day, conditions_id. Conditions identify a reviewed
matched scenario, not a free pass for different campaign histories. Never infer
causal improvement from a simple before/after association.
"""
import argparse
import importlib.util
import json
import math
from pathlib import Path


def compare(before, after, before_context, after_context):
    blockers = []
    keys = ('campaign_id', 'settings_sha256', 'window_start_day', 'window_end_day', 'conditions_id')
    for label, report, context in (('before', before, before_context), ('after', after, after_context)):
        if report.get('status') != 'closed_observed' or report.get('pending_last_line') or report.get('excluded_other_session_rows'):
            blockers.append(label + ': intact closed single-session capture required')
        for key in (*keys, 'build_sha256', 'capture_sha256'):
            if context.get(key) in (None, ''):
                blockers.append(label + ': missing ' + key)
        for key in ('settings_sha256', 'build_sha256', 'capture_sha256'):
            value = context.get(key, '')
            if not isinstance(value, str) or len(value) != 64 or any(c not in '0123456789abcdefABCDEF' for c in value):
                blockers.append(label + ': invalid ' + key)
        if context.get('capture_sha256') != report.get('source', {}).get('prefix_sha256'):
            blockers.append(label + ': context does not match captured bytes')
        span = report.get('campaign_window', {})
        for key, field in (('window_start_day', 'first_day'), ('window_end_day', 'last_day')):
            value = context.get(key)
            if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value != span.get(field):
                blockers.append(label + ': invalid or unmatched ' + key)
        if span.get('first_day') is None or span.get('last_day', 0) <= span.get('first_day', 0):
            blockers.append(label + ': positive campaign observation window required')
        if 'review_metrics' not in report:
            blockers.append(label + ': review metrics unavailable')
        cost = report.get('diagnostic_health', {}).get('watchdog', {}).get('cost', {})
        if cost.get('discarded_rows') not in (None, 0) or cost.get('pending_rows') not in (None, 0):
            blockers.append(label + ': discarded or pending records')
        if any(f['issue'] == 'discarded_records' for f in report.get('findings', [])):
            blockers.append(label + ': capture reports discarded records')
    for key in keys:
        if before_context.get(key) != after_context.get(key):
            blockers.append('Different ' + key)
    if before.get('coverage') != after.get('coverage'):
        blockers.append('Different event coverage')
    left, right = before.get('review_metrics', {}), after.get('review_metrics', {})
    if set(left.get('wallets', {})) != set(right.get('wallets', {})):
        blockers.append('Different observed wallet cohorts')
    result = dict(schema='aoc_comparison_v1', status='blocked' if blockers else 'descriptive_comparison_only',
        blockers=blockers, before_source=before.get('source'), after_source=after.get('source'),
        before_context=before_context, after_context=after_context,
        limits=['Reviewed contexts are operator declarations, not automatically proven campaign equivalence.',
                'A difference is not proof the changed build caused it.',
                'Equal missing coverage is still missing coverage.'], metrics={})
    if blockers:
        return result
    wallets = {}
    for wallet, old in left['wallets'].items():
        new = right['wallets'][wallet]
        wallets[wallet] = dict(before=old, after=new,
            peak_absolute_residual_change=(new['peak_absolute_residual'] - old['peak_absolute_residual']
                if old['checks'] == new['checks'] else None),
            comparability='same_wallet_snapshot_counts_differ' if old['checks'] != new['checks'] else 'matched_counts_not_matched_events')
    a, b = left['unsuccessful_attempt_fraction'], right['unsuccessful_attempt_fraction']
    result['metrics'] = dict(wallets=wallets, unsuccessful_attempt_fraction=dict(before=a, after=b,
        change=b-a if a is not None and b is not None else None,
        status='observed_attempt_rates_not_shortage_counts' if a is not None and b is not None else 'not_comparable'),
        profitability=dict(status='not_comparable', reason='Requires reconciled operating revenue and cost evidence; cash alone is insufficient.'))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('before', 'after', 'before-session', 'after-session', 'before-context', 'after-context', 'output'):
        parser.add_argument('--' + name, required=True)
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('causal', Path(__file__).with_name('Analyze-CausalEvidence.py'))
    analyzer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(analyzer)
    result = compare(analyzer.analyze(args.before, args.before_session), analyzer.analyze(args.after, args.after_session),
        json.loads(Path(args.before_context).read_text(encoding='utf-8-sig')),
        json.loads(Path(args.after_context).read_text(encoding='utf-8-sig')))
    with Path(args.output).open('x', encoding='utf-8') as output:
        json.dump(result, output, indent=2, allow_nan=False)
    print(result['status'])
    return 2 if result['blockers'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
