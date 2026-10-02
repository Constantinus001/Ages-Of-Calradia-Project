import base64
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('reload_check', Path(__file__).with_name('Compare-ProcurementReload.py'))
check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(check)


def segment(session, stage, orders):
    payload = json.dumps({'Schema': 1, 'Orders': orders}).encode()
    common = dict(session=session, owner='campaign', day='20', metric='', detail='')
    def row(kind, **values):
        return dict(common, kind=kind, **values)
    rows = [row('SESSION_START'), row('CORE_SYSTEMS'), row('PROCUREMENT_LEDGER'), row('SESSION_END')]
    rows[1].update(metric='valid', detail='coreMvid=22222222-2222-2222-2222-222222222222; policyRevision=' + 'a'*64)
    rows[2].update(metric=stage, detail='procurementMvid=11111111-1111-1111-1111-111111111111; sha256='
                   + hashlib.sha256(payload).hexdigest() + '; payloadBase64=' + base64.b64encode(payload).decode())
    return rows


class ReloadTests(unittest.TestCase):
    def setUp(self):
        self.a = segment('a', 'serialized_for_save', [{'Town': 'town', 'Quantity': 3}])
        self.b = segment('b', 'loaded_payload', [{'Town': 'town', 'Quantity': 3}])

    def test_match(self):
        self.assertEqual(check.compare(self.a, self.b)['status'], 'MATCH')

    def test_empty_is_gap(self):
        self.assertEqual(check.compare(segment('a', 'serialized_for_save', []), segment('b', 'loaded_payload', []))['status'], 'NOT_EXERCISED_EMPTY_LEDGER')

    def test_changed_payload(self):
        with self.assertRaises(ValueError):
            check.compare(self.a, segment('b', 'loaded_payload', [{'Quantity': 2}]))

    def test_invalid_boundaries(self):
        for edit in ('day', 'owner', 'session', 'metric', 'detail'):
            b = copy.deepcopy(self.b)
            b[2][edit] = {'day': 'NaN', 'owner': 'other', 'session': 'mixed', 'metric': 'unavailable', 'detail': ''}[edit]
            with self.assertRaises((ValueError, KeyError)):
                check.compare(self.a, b)

    def test_open_capture(self):
        with self.assertRaises(ValueError):
            check.compare(self.a, self.b[:-1])

    def test_wrong_hash(self):
        self.b[2]['detail'] = self.b[2]['detail'].replace('sha256=', 'sha256=0')
        with self.assertRaises(ValueError):
            check.compare(self.a, self.b)
