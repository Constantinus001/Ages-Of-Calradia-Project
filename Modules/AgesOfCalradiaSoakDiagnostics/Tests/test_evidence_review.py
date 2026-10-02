"""Hand-derived regressions and fail-closed comparison tests; no live game."""
import copy
import importlib.util
import json
from pathlib import Path
import unittest
import tempfile
import subprocess
import sys
import test_causal_evidence as base

spec = importlib.util.spec_from_file_location('comparison', Path(__file__).with_name('Compare-CausalEvidence.py'))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class ReviewTests(unittest.TestCase):
    def rows(self, records):
        return base.CausalEvidenceTests.run_rows(self, records)

    def pair(self):
        before = self.rows([('WALLET_CHECK', 'w', 'Gold', 100, 110, ''),
                            ('WORKSHOP_CYCLE', 'shop', 'failed_see_gates', 0, 0, '')])
        after = self.rows([('WALLET_CHECK', 'w', 'Gold', 100, 105, ''),
                           ('WORKSHOP_CYCLE', 'shop', 'succeeded', 0, 1, '')])
        # Comparison unit fixture gives explicit matched windows. File parsing
        # is exercised separately; these are not purported real captures.
        for report in (before, after):
            report['campaign_window'] = dict(first_day=1, last_day=16)
        contexts = [dict(capture_sha256=r['source']['prefix_sha256'], campaign_id='fixture',
            settings_sha256='a'*64, build_sha256=build*64,
            window_start_day=1, window_end_day=16, conditions_id='matched-fixture')
            for r, build in ((before, 'b'), (after, 'c'))]
        return before, after, *contexts

    def test_known_incident_reproductions(self):
        fixture = json.loads((Path(__file__).parent / 'fixtures/review-incidents.json').read_text())
        for case in fixture['cases']:
            with self.subTest(case=case['name']):
                report = self.rows(case['rows'])
                metrics, expected = report['review_metrics'], case['expected']
                if 'wallet' in expected:
                    wallet = metrics['wallets'][expected['wallet']]
                    self.assertEqual(wallet['peak_absolute_residual'], expected['peak'])
                    self.assertEqual(wallet['latest_residual'], expected['latest'])
                    self.assertEqual(wallet['discrepant_checks'], expected['discrepant_checks'])
                    self.assertEqual(report['findings'][0]['confidence']['causal_attribution'], expected['cause'])
                else:
                    for key in ('successful', 'unsuccessful'):
                        self.assertEqual(metrics['workshop_cycles'][key], expected[key])
                    self.assertAlmostEqual(metrics['unsuccessful_attempt_fraction'], expected['fraction'])

    def test_large_impact_does_not_invent_cause(self):
        finding = self.rows([('WALLET_CHECK', 'w', 'Gold', 0, 28190000, '')])['findings'][0]
        self.assertEqual(finding['impact']['magnitude_by_metric']['Gold']['peak_absolute_residual'], 28190000)
        self.assertEqual(finding['confidence']['causal_attribution'], 'unknown')
        self.assertEqual(finding['confidence']['severity'], 'not_inferred_from_magnitude')

    def test_unknown_cycle_cannot_produce_healthy_rate(self):
        report = self.rows([('WORKSHOP_CYCLE', 's', 'future_metric', 0, 0, '')])
        self.assertIsNone(report['review_metrics']['unsuccessful_attempt_fraction'])

    def test_separate_wallet_views_and_metrics(self):
        report = self.rows([('WALLET_CHECK', owner, metric, 0, 10, '')
                            for owner, metric in (('hero', 'Gold'), ('clan', 'Gold'), ('clan', 'Debt'))])
        self.assertEqual(len(report['review_metrics']['wallets']), 3)

    def test_compatible_comparison_is_descriptive_not_causal(self):
        result = comparison.compare(*self.pair())
        self.assertEqual(result['status'], 'descriptive_comparison_only')
        self.assertEqual(result['metrics']['wallets']['w|Gold']['peak_absolute_residual_change'], -5)
        self.assertEqual(result['metrics']['unsuccessful_attempt_fraction']['change'], -1)
        self.assertEqual(result['metrics']['profitability']['status'], 'not_comparable')

    def test_missing_provenance_blocks(self):
        args = list(self.pair()); args[3] = {}
        result = comparison.compare(*args)
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual(result['metrics'], {})

    def test_different_settings_windows_conditions_and_campaign_block(self):
        for key, value in (('settings_sha256', 'd'*64), ('window_end_day', 17),
                           ('conditions_id', 'another'), ('campaign_id', 'another')):
            args = list(self.pair()); args[3][key] = value
            with self.subTest(key=key):
                self.assertEqual(comparison.compare(*args)['status'], 'blocked')

    def test_changed_bytes_block(self):
        args = list(self.pair()); args[3]['capture_sha256'] = '0'*64
        self.assertEqual(comparison.compare(*args)['status'], 'blocked')

    def test_active_integrity_mixed_and_tail_block(self):
        for key, value in (('status', 'open_or_interrupted'), ('status', 'integrity_review_required'),
                           ('excluded_other_session_rows', 1), ('pending_last_line', True)):
            args = list(self.pair()); args[1][key] = value
            self.assertEqual(comparison.compare(*args)['status'], 'blocked')

    def test_coverage_or_cohort_difference_blocks(self):
        args = list(self.pair()); args[1]['coverage'] = {}
        self.assertEqual(comparison.compare(*args)['status'], 'blocked')
        args = list(self.pair()); args[1]['review_metrics']['wallets'] = {}
        self.assertEqual(comparison.compare(*args)['status'], 'blocked')

    def test_comparison_does_not_modify_input(self):
        args = self.pair(); original = copy.deepcopy(args)
        comparison.compare(*args)
        self.assertEqual(args, original)

    def test_lost_records_block(self):
        args = list(self.pair())
        args[1]['diagnostic_health']['watchdog']['cost']['discarded_rows'] = 1
        self.assertEqual(comparison.compare(*args)['status'], 'blocked')

    def test_unequal_sampling_does_not_claim_peak_improvement(self):
        args = list(self.pair()); args[1]['review_metrics']['wallets']['w|Gold']['checks'] = 3
        result = comparison.compare(*args)
        self.assertIsNone(result['metrics']['wallets']['w|Gold']['peak_absolute_residual_change'])

    def test_cli_preserves_existing_output_and_blocks_missing_context(self):
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            capture = root / 'capture.tsv'
            capture.write_text('session\tsequence\tday\tkind\towner\tmetric\tbefore\tafter\tdetail\n'
                's\t1\t1\tSESSION_START\tc\tsupply_v7\t0\t0\t\n'
                's\t2\t16\tSESSION_END\tc\tcomplete\t1\t16\t\n')
            context = root / 'context.json'; context.write_text('{}')
            output = root / 'comparison.json'
            command = [sys.executable, str(Path(__file__).with_name('Compare-CausalEvidence.py')),
                '--before', str(capture), '--after', str(capture), '--before-session', 's', '--after-session', 's',
                '--before-context', str(context), '--after-context', str(context), '--output', str(output)]
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 2, result.stderr)
            original = output.read_bytes()
            self.assertEqual(json.loads(original)['status'], 'blocked')
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(output.read_bytes(), original)

    def test_impacts_show_full_span_beyond_bounded_examples(self):
        evidence = base.m.Evidence('s')
        for i in range(1, 6):
            evidence.observe(dict(session='s', sequence=str(i), line=i, day=str(i),
                kind='WALLET_CHECK', owner='w', metric='Gold', before='0', after=str(i), detail=''))
        finding = evidence.report()['findings'][0]
        self.assertEqual(len(finding['evidence']), 3)
        self.assertEqual(finding['impact']['observed_span_days'], 4)
        self.assertEqual(finding['impact']['magnitude_by_metric']['Gold']['peak_absolute_residual'], 5)
