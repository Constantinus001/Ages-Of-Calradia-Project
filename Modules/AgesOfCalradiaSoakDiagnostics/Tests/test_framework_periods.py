import csv
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('periods', Path(__file__).with_name('Analyze-FrameworkPeriods.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PeriodTests(unittest.TestCase):
    def test_combined_findings_surface_nested_failures_and_coverage(self):
        evidence={'procurement_accrual':{'problems':['duplicate'], 'coverage_gaps':['cash']},
                  '2_workshop_payments':{'batch_evidence':{'batch_discrepancies':[{'paid':1}]}},
                  '3_input_shortages':{'stock_residuals':{'town/wool':-3}}}
        result=module.evidence_summary(evidence)
        self.assertEqual(result['status'],'FINDINGS_REQUIRE_REVIEW')
        self.assertEqual(len(result['errors']),2)
        self.assertEqual(len(result['evidence_gaps']),2)
        self.assertEqual(module.evidence_summary({})['status'],'MANUAL_REVIEW_REQUIRED')

    def test_empty_capture_not_certified(self):
        self.assertEqual(self.run_rows([])['status'],'INVALID_OR_INCOMPLETE')

    def test_real_analyzers_retain_missing_coverage(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'capture.tsv'
            fields = ['utc','session','sequence','day','kind','scope','parent','owner','metric','before','after','detail']
            with path.open('w', newline='') as stream:
                writer = csv.DictWriter(stream, fieldnames=fields, delimiter='\t')
                writer.writeheader()
                for number, kind in enumerate(('SESSION_START','SESSION_END'), 1):
                    row = dict.fromkeys(fields, '0')
                    row.update(utc='2026-09-20T00:00:00Z', session='test', sequence=str(number), day=str(number),
                               kind=kind, owner='campaign', metric='supply_v7' if number==1 else 'fixture_completed', detail='')
                    writer.writerow(row)
            result = module.analyze(path)
        self.assertFalse(result['problems'])
        evidence = result['segments'][0]['evidence']
        self.assertTrue(evidence['core_framework_and_quests']['coverage_gaps'])
        self.assertEqual(evidence['procurement_accrual']['snapshots'], [])
        self.assertTrue(evidence['combined_findings']['evidence_gaps'])
        self.assertEqual(evidence['compact_verdict']['status'], 'EVIDENCE_GAPS_REMAIN')
        self.assertIn('naval', evidence['compact_verdict'])

    def test_compact_verdict_does_not_hide_a_reconciliation_failure(self):
        evidence = {
            'combined_findings': {'errors': [{'path': 'workshop'}], 'evidence_gaps': []},
            '2_workshop_payments': {'batch_evidence': {
                'closed': True, 'problems': [], 'batch_discrepancies': [],
                'matched_capital_affecting_successful_batches': 4}},
            'procurement_transactions': {'problems': [], 'coverage_gaps': []},
            'reward_accounting': {'naval_coverage': {'player_penalty_status': 'NOT_EXERCISED'}},
        }
        verdict = module.compact_verdict(evidence)
        self.assertEqual(verdict['status'], 'FINDINGS_REQUIRE_REVIEW')
        self.assertEqual(verdict['workshop_accounting']['status'], 'RECONCILED_FOR_CAPTURED_BATCHES')
        self.assertEqual(verdict['reported_errors'], 1)

    def run_rows(self, rows):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'capture.tsv'
            with path.open('w', newline='') as stream:
                writer = csv.writer(stream, delimiter='\t')
                writer.writerow(['session','sequence','kind','day','owner'])
                writer.writerows(rows)
            def evaluate(segment):
                with open(segment) as stream:
                    records = list(csv.DictReader(stream, delimiter='\t'))
                self.assertEqual(len({r['session'] for r in records}), 1)
                return {'rows': len(records)}
            return module.analyze(path, evaluate)

    def test_reload_keeps_segments_and_gaps_separate(self):
        result = self.run_rows([['a',1,'SESSION_START',10,'campaign'],['a',2,'SESSION_END',12,'capture'],
                                ['b',1,'SESSION_START',13,'campaign'],['b',2,'SESSION_END',16,'capture']])
        self.assertFalse(result['problems'])
        self.assertEqual(result['observed_days'], 5)
        self.assertEqual(result['unobserved_intervals'][0]['days'], 1)
        self.assertEqual(len(result['segments']), 2)
        self.assertEqual(result['reload_continuity'][0]['status'], 'UNOBSERVED_INTERVAL')

    def test_exact_reload_without_ledger_is_a_named_coverage_gap(self):
        result = self.run_rows([['a',1,'SESSION_START',10,'campaign'],['a',2,'SESSION_END',12,'capture'],
                                ['b',1,'SESSION_START',12,'campaign'],['b',2,'SESSION_END',16,'capture']])
        self.assertFalse(result['problems'])
        self.assertEqual(result['reload_continuity'][0]['status'], 'EVIDENCE_GAP')
        self.assertIn('serialized_for_save', result['reload_continuity'][0]['limit'])

    def test_overlap_mixed_campaign_and_repeated_session_rejected(self):
        for session, day, campaign in [('b',11,'campaign'),('a',12,'campaign'),('b',12,'other')]:
            result = self.run_rows([['a',1,'SESSION_START',10,'campaign'],['a',2,'SESSION_END',12,'capture'],
                                    [session,1,'SESSION_START',day,campaign]])
            self.assertTrue(result['problems'])

    def test_unclosed_or_sequence_gap_not_certified(self):
        for rows in [[['a',1,'SESSION_START',10,'campaign']],
                     [['a',1,'SESSION_START',10,'campaign'],['a',3,'SESSION_END',12,'capture']]]:
            self.assertTrue(self.run_rows(rows)['problems'])
