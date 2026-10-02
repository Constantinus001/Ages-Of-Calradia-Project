import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('causal', Path(__file__).with_name('Analyze-CausalEvidence.py'))
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)


class CausalEvidenceTests(unittest.TestCase):
    def run_rows(self, records, close=True, tail=b''):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'capture.tsv'
            lines = ['session\tsequence\tday\tkind\towner\tmetric\tbefore\tafter\tdetail']
            rows = [('SESSION_START', 'campaign', 'supply_v7', 0, 0, '')] + records
            if close: rows.append(('SESSION_END', 'capture', 'complete', 1, 2, ''))
            for i, row in enumerate(rows, 1):
                lines.append('\t'.join(map(str, ('s', i, 1, *row))))
            path.write_bytes(('\n'.join(lines) + '\n').encode() + tail)
            return m.analyze(path, 's')

    def test_positive_negative_zero_town_model_results(self):
        for delta in (10, -10, 0):
            with self.subTest(delta=delta):
                result = self.run_rows([
                    ('TOWN_CASH_BEGIN', 'town', 'UpdateTownGold', 100, 100, 'townCashOperation=1'),
                    ('TOWN_CASH_END', 'town', 'UpdateTownGold', 100, 100 + delta,
                     f'townCashOperation=1; originalRan=True; modelCalls=1; modelResult={delta}; error=none')])
                self.assertEqual(result['findings'][0]['finding'], 'reconciled')

    def test_skipped_exception_missing_begin_not_native_cause(self):
        for detail in ('originalRan=False; modelCalls=1; modelResult=10; error=none',
                       'originalRan=True; modelCalls=1; modelResult=10; error=Exception',
                       'originalRan=True; modelCalls=0; modelResult=unavailable; error=none'):
            result = self.run_rows([('TOWN_CASH_END', 'town', 'x', 0, 10, 'townCashOperation=1; ' + detail)])
            self.assertEqual(result['findings'][0]['attribution'], 'unknown')

    def test_model_difference_not_blanket_inflation(self):
        result = self.run_rows([
            ('TOWN_CASH_BEGIN', 'town', 'x', 0, 0, 'townCashOperation=1'),
            ('TOWN_CASH_END', 'town', 'x', 0, 12, 'townCashOperation=1; originalRan=True; modelCalls=1; modelResult=10; error=none')])
        self.assertEqual(result['findings'][0]['issue'], 'town_cash_application_difference')
        self.assertTrue(result['findings'][0]['missing_evidence'])

    def test_old_log_and_zero_stock_not_fake_shortage(self):
        result = self.run_rows([('WORKSHOP_STATE', 'shop', 'stock', 0, 0, '')])
        self.assertEqual(result['findings'], [])
        self.assertEqual(result['coverage']['workshop_inputs']['evidence'], 'not_exercised_or_unsupported')

    def test_nested_links_not_summed_as_money(self):
        result = self.run_rows([
            ('WALLET_CHANGE', 'wallet', 'Gold', 100, 70, 'market=1; parentMarket=2; workshopCycle=3'),
            ('WALLET_CHANGE', 'wallet', 'Gold', 70, 60, 'market=2; parentMarket=none')])
        self.assertEqual(result['causal_context_links']['market'], 2)
        self.assertEqual(result['findings'], [])

    def test_partial_tail_and_open_scope_pending(self):
        result = self.run_rows([('TOWN_CASH_BEGIN', 'town', 'x', 0, 0, 'townCashOperation=1')], False, b's\t4\t')
        self.assertTrue(result['pending_last_line'])
        self.assertEqual(result['status'], 'open_or_interrupted')
        self.assertEqual(result['integrity'], [])

    def test_closed_open_scope_is_incomplete(self):
        result = self.run_rows([('TOWN_CASH_BEGIN', 'town', 'x', 0, 0, 'townCashOperation=1')])
        self.assertEqual(result['status'], 'integrity_review_required')

    def test_repeated_candidates_bounded_examples_exact_counts(self):
        result = self.run_rows([('PROCUREMENT_CANDIDATE', 'shop', 'supplier_reserve', 0, 0, 'categoryStock=100')] * 20)
        issue = result['findings'][0]
        self.assertEqual(issue['occurrences'], 20)
        self.assertEqual(len(issue['evidence']), 3)
        self.assertIn('Other candidates can succeed.', issue['false_alarm_checks'])

    def test_omitted_witnesses_not_dropped_accounting(self):
        result = self.run_rows([('PROCUREMENT_CANDIDATE', 'shop', 'witness_budget', 0, 0, 'omitted=50')])
        self.assertEqual(result['findings'], [])

    def test_writer_drops_exposed(self):
        result = self.run_rows([('DIAGNOSTIC_COST', 'capture', 'cost', 0, 1, 'discardedRows=10')])
        self.assertEqual(result['findings'][0]['issue'], 'discarded_records')

    def test_resolved_residual_not_presented_as_persistent(self):
        result = self.run_rows([('WALLET_CHECK', 'w', 'Gold', 10, 20, ''), ('WALLET_CHECK', 'w', 'Gold', 20, 20, '')])
        self.assertIn('not_present_at_latest', result['findings'][0]['false_positive_status'])
        self.assertEqual(result['findings'][0]['occurrences'], 1)
        self.assertIn('Historical discrepancy groups absent at the latest snapshot: 1.', m.markdown(result))
        self.assertIn('Other groups requiring review: 0.', m.markdown(result))

    def test_compact_index_retains_separate_full_details(self):
        result = self.run_rows([])
        result['independent_audits'] = {'example': {'status': 'analysis_completed_not_a_pass_verdict',
                                                 'evidence': {'problems': ['a'] * 20}}}
        with tempfile.TemporaryDirectory() as root:
            base = Path(root) / 'report'
            m.write_report(result, base)
            import json
            index = json.loads(Path(str(base) + '.json').read_text())
            audit = index['independent_audits']['example']
            self.assertEqual(audit['summary']['problems']['entries'], 20)
            self.assertEqual(audit['summary']['problems']['examples_omitted'], 8)
            self.assertEqual(len(json.loads(Path(audit['detail_path']).read_text())['problems']), 20)
            with self.assertRaises(FileExistsError): m.write_report(result, base)

    def test_wrong_session_refused(self):
        with tempfile.TemporaryDirectory() as root:
            p = Path(root) / 'a.tsv'
            p.write_text('session\tsequence\tday\tkind\towner\tmetric\tbefore\tafter\tdetail\nother\t1\t1\tSESSION_START\tc\tv7\t0\t0\t\n')
            with self.assertRaises(ValueError): m.analyze(p, 's')

    def test_nonfinite_rejected(self):
        with self.assertRaises(ValueError): self.run_rows([('WALLET_CHECK', 'w', 'Gold', 'NaN', 1, '')])

    def test_report_contains_evidence_and_alternatives(self):
        result = self.run_rows([('WALLET_CHECK', 'w', 'Gold', 100, 120, '')])
        text = m.markdown(result)
        for term in ('Evidence lines:', 'False-alarm checks:', 'Missing proof:', 'Coverage gaps'):
            self.assertIn(term, text)

    def test_bundle_keeps_analyzer_failure_and_continues(self):
        import EvidenceAuditBundle as bundle
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'source'; path.write_text('unchanged')
            def loader(name):
                def run(path):
                    if name == 'Analyze-NavalAcceptance': raise ValueError('missing witness')
                    return {'problems': ['example discrepancy'], 'coverage_gaps': ['example gap']}
                return run
            result = bundle.audit(path, loader)
            self.assertEqual(len(result), len(bundle.AUDITS))
            self.assertEqual(result['naval_rewards_and_cleanup']['status'], 'analysis_failed')
            self.assertEqual(result['quest_and_framework_state']['evidence']['coverage_gaps'], ['example gap'])

    def test_bundle_rejects_changed_input(self):
        import EvidenceAuditBundle as bundle
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'source'; path.write_text('initial')
            def loader(name):
                def run(source):
                    with Path(source).open('a') as stream: stream.write('changed')
                    return {}
                return run
            with self.assertRaises(ValueError): bundle.audit(path, loader)


if __name__ == '__main__': unittest.main()
