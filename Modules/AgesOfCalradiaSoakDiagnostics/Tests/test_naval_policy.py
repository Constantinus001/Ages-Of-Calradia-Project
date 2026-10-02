import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('naval_policy', Path(__file__).with_name('Analyze-NavalPolicy.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class NavalPolicyTests(unittest.TestCase):
    def analyze(self, rows):
        stream = io.StringIO()
        writer = csv.writer(stream, delimiter='\t')
        writer.writerow(['kind', 'metric', 'detail'])
        writer.writerows(rows)
        stream.seek(0)
        with patch('builtins.open', return_value=stream):
            return module.analyze('fixture')

    def quote(self, **changes):
        fields = dict(revision='r1', route='sale', nativeBase='10500', effectiveBase='600', hull='10000',
                      hullBasis='.01', returnedQuote='257.5', alreadyDiscounted='False', basisValid='True')
        fields.update(changes)
        return ['NAVAL_POLICY_DECISION', 'quote', 'policy_v1; ' + '; '.join(k+'='+v for k, v in fields.items())]

    def test_arithmetic(self):
        self.assertFalse(self.analyze([self.quote()])['problems'])

    def test_wrong_normalization(self):
        self.assertTrue(self.analyze([self.quote(effectiveBase='105')])['problems'])

    def test_nonfinite(self):
        self.assertTrue(self.analyze([self.quote(returnedQuote='nan')])['problems'])

    def test_legacy_not_pass(self):
        self.assertIn('policy_unavailable_in_legacy_capture', self.analyze([])['coverage_gaps'])

    def test_revision_change(self):
        self.assertTrue(self.analyze([self.quote(), self.quote(revision='r2')])['problems'])

    def test_unknown_schema(self):
        self.assertTrue(self.analyze([['NAVAL_POLICY_DECISION', 'quote', 'policy_v2;']])['problems'])

    def test_allocation(self):
        row = ['NAVAL_POLICY_DECISION', 'allocation', 'policy_v1; revision=r1; nativeTotal=5350; effectiveTotal=400; '
               'applied=True; priorGold=100; contribution=1; contributionSum=3; effectivePool=901; poolValid=True']
        self.assertFalse(self.analyze([row])['problems'])
        row[2] = row[2].replace('effectiveTotal=400', 'effectiveTotal=401')
        self.assertTrue(self.analyze([row])['problems'])

    def test_no_payment_certification(self):
        self.assertIn('payment_lifecycle_and_player_isolation_require_companion_evidence', self.analyze([self.quote()])['coverage_gaps'])

    def test_incomplete_active_tail_not_confirmed_failure(self):
        result = self.analyze([['NAVAL_POLICY_DECISION', 'quote']])
        self.assertFalse(result['problems'])
        self.assertIn('unfinished_policy_tail_while_writer_may_be_active', result['coverage_gaps'])

    def test_nonterminal_incomplete_row_is_failure(self):
        self.assertTrue(self.analyze([['NAVAL_POLICY_DECISION', 'quote'], self.quote()])['problems'])

    def test_unterminated_detail_tail_is_not_failure(self):
        stream = io.StringIO('kind\tmetric\tdetail\nNAVAL_POLICY_DECISION\tquote\tpolicy_v1; revision=r1; nativeBase=')
        with patch('builtins.open', return_value=stream):
            result = module.analyze('fixture')
        self.assertFalse(result['problems'])
        self.assertIn('unfinished_policy_tail_while_writer_may_be_active', result['coverage_gaps'])
