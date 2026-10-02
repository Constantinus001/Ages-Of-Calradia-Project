import csv
import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import patch


class LargeLedgerReaderTests(unittest.TestCase):
    def test_all_stream_readers_accept_large_observation(self):
        columns = ['utc', 'session', 'sequence', 'day', 'kind', 'scope', 'parent', 'owner', 'metric', 'before', 'after', 'detail']
        stream = io.StringIO()
        writer = csv.DictWriter(stream, fieldnames=columns, delimiter='\t')
        writer.writeheader()
        for i, kind in enumerate(['SESSION_START', 'PROCUREMENT_LEDGER', 'SESSION_END'], 1):
            writer.writerow(dict(utc='2026-09-20T00:00:00Z', session='fixture', sequence=i, day=1,
                                 kind=kind, scope=0, parent=0, owner='campaign', metric='fixture',
                                 before=1, after=1, detail='x'*300000 if i == 2 else ''))
        content = stream.getvalue()
        for name in ['Analyze-PravendSupply', 'Analyze-CampaignSystems', 'Analyze-BroadSupplyCapture',
                     'Analyze-SupplyCapture', 'Analyze-FourEconomyCases', 'Analyze-WorkshopPayments',
                     'Classify-SupplyJourneys']:
            with self.subTest(reader=name):
                csv.field_size_limit(131072)
                spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name+'.py'))
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                self.assertEqual(csv.field_size_limit(), 24000000)
                with patch('builtins.open', return_value=io.StringIO(content)):
                    getattr(module, 'analyze', getattr(module, 'classify', None))('fixture.tsv')
