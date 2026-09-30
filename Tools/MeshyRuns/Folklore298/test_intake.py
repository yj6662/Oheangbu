"""Offline Meshy intake contracts. Every API and asset request is mocked."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error

SOURCE = Path(__file__).resolve().parents[1] / 'folklore298.py'
spec = importlib.util.spec_from_file_location('folklore298_intake_under_test', SOURCE)
intake = importlib.util.module_from_spec(spec)
spec.loader.exec_module(intake)


class IntakeContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.out = self.root / 'output'
        self.out.mkdir()
        self.ledger = self.out / 'meshy-ledger.json'
        self.reference = self.root / 'reviewed.png'
        self.reference.write_bytes(b'\x89PNG\r\n\x1a\nOFFLINE_FIXTURE')
        self.calls = []
        self.balance = {'credit_balance': 500}
        self.results = {}
        self.patches = [patch.object(intake, 'ROOT', self.root), patch.object(intake, 'OUT', self.out),
            patch.object(intake, 'LEDGER', self.ledger), patch.object(intake, 'call', self.fake_call),
            patch.object(intake.urllib.request, 'urlopen', side_effect=AssertionError('Real network forbidden'))]
        for p in self.patches:
            p.start()
            self.addCleanup(p.stop)
        self.write_ledger([])
    def write_ledger(self, tasks, cap=280):
        intake.save(self.ledger, dict(schema=1, operating_cap=cap, tasks=tasks, balance_start=self.balance))
    def data(self): return json.loads(self.ledger.read_text())
    def args(self, **kwargs):
        result = dict(name='dokkaebi_model', kind='model', reference=str(self.reference), polygons=12000,
            humanoid=True, parent=None, height=2.0, actions=None, dry_run=False)
        result.update(kwargs)
        return SimpleNamespace(**result)
    def fake_call(self, method, endpoint, payload=None):
        self.calls.append((method, endpoint, payload))
        if endpoint == '/openapi/v1/balance': return self.balance
        if method == 'POST':
            self.assertEqual(self.data()['tasks'][-1]['status'], 'POST_RESERVED')
            return {'result': 'offline-task-id-' + str(len(self.data()['tasks']))}
        return self.results[endpoint]
    def posts(self): return [c for c in self.calls if c[0] == 'POST']
    def prior(self, name='existing', status='SUCCEEDED', reserved=15, actual=None, kind='model'):
        return dict(name=name, kind=kind, id='offline-parent', endpoint='/openapi/v1/image-to-3d',
            status=status, reserved_credits=reserved, consumed_credits=actual)
    def test_reserve_before_post_and_idempotent_name(self):
        intake.submit(self.args())
        before=len(self.calls)
        intake.submit(self.args())
        self.assertEqual(len(self.posts()),1)
        self.assertEqual(len(self.calls),before)
        self.assertEqual(self.data()['tasks'][0]['status'],'SUBMITTED')
        self.assertNotIn('image_url',self.ledger.read_text())
    def test_dry_run_has_no_post_or_ledger_task(self):
        result=intake.submit(self.args(dry_run=True))
        self.assertTrue(result['dry_run'])
        self.assertEqual(self.posts(),[])
        self.assertEqual(self.data()['tasks'],[])
    def test_uncertain_post_is_persisted_and_never_retried(self):
        def uncertain(method,endpoint,payload=None):
            if method=='POST':
                self.calls.append((method,endpoint,payload))
                self.assertEqual(self.data()['tasks'][-1]['status'],'POST_RESERVED')
                raise RuntimeError('Transport failure')
            return self.balance
        with patch.object(intake,'call',uncertain):
            with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.data()['tasks'][0]['status'],'POST_UNCERTAIN')
        intake.submit(self.args())
        with self.assertRaises(RuntimeError): intake.submit(self.args(name='next'))
        self.assertEqual(len(self.posts()),1)
    def test_unexpected_post_response_stays_uncertain(self):
        with patch.object(intake,'call',lambda method,*args: {'credit_balance':500} if method=='GET' else {'result':None}):
            with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.data()['tasks'][0]['status'],'POST_UNCERTAIN')
    def test_reserved_previous_attempt_blocks_new_post(self):
        self.write_ledger([self.prior(status='POST_RESERVED')])
        with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.calls,[])
    def test_cumulative_cap_blocks_before_balance_or_post(self):
        self.write_ledger([self.prior(reserved=270)])
        with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.calls,[])
    def test_actual_credits_drive_cap(self):
        self.write_ledger([self.prior(reserved=15,actual=275)])
        with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.posts(),[])
    def test_ledger_cannot_raise_hard_cap(self):
        self.write_ledger([self.prior(reserved=270)],cap=10000)
        with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.calls,[])
    def test_invalid_reserved_usage_is_rejected(self):
        for value in (float('nan'),float('inf'),-1,'15',True):
            with self.subTest(value=value):
                with self.assertRaises((RuntimeError,ValueError)): intake.cost(self.prior(reserved=value))
    def test_valid_balance_schema_variants(self):
        for value in ({'credit_balance':15},{'balance':15},{'balance':{'credits':15}}):
            with self.subTest(value=value):
                self.balance=value
                result=intake.submit(self.args(dry_run=True))
                self.assertTrue(result['dry_run'])
        self.assertEqual(self.posts(),[])
    def test_insufficient_or_unknown_balance_blocks(self):
        for value in ({'credit_balance':14},{'unknown':500},{'credit_balance':'500'},{'credit_balance':False}):
            with self.subTest(value=value):
                self.balance=value
                with self.assertRaises(RuntimeError): intake.submit(self.args())
        self.assertEqual(self.posts(),[])
    def test_nonfinite_balance_blocks(self):
        for value in (float('nan'),float('inf')):
            with self.subTest(value=value):
                self.write_ledger([])
                self.balance={'credit_balance':value}
                with self.assertRaises((RuntimeError,ValueError)): intake.submit(self.args())
        self.assertEqual(self.posts(),[])
    def test_invalid_actual_usage_is_rejected(self):
        for value in (float('nan'),float('inf'),-1,'15',True):
            with self.subTest(value=value):
                with self.assertRaises((RuntimeError,ValueError)): intake.cost(self.prior(actual=value))
    def test_invalid_reference_or_task_name_sends_nothing(self):
        for override in ({'reference':str(self.root/'absent.png')},{'name':'../outside'}):
            with self.assertRaises(RuntimeError): intake.submit(self.args(**override))
        self.assertEqual(self.calls,[])
    def test_rig_requires_completed_parent(self):
        self.write_ledger([self.prior(status='IN_PROGRESS')])
        with self.assertRaises(RuntimeError): intake.submit(self.args(kind='rig',parent='existing'))
        self.assertEqual(self.posts(),[])
    def test_animation_unique_actions_and_cost(self):
        self.write_ledger([self.prior(kind='rig')])
        with self.assertRaises(RuntimeError): intake.submit(self.args(kind='animation',parent='existing',actions='1,1'))
        result=intake.submit(self.args(kind='animation',parent='existing',actions='1,2,3',dry_run=True))
        self.assertEqual(result['entry']['reserved_credits'],9)
        self.assertEqual(result['entry']['config']['action_ids'],[1,2,3])
        self.assertEqual(self.posts(),[])
    def test_download_url_extraction_nested_and_signed(self):
        result=dict(model_urls={'glb':'https://assets.invalid/model.glb?signature=offline','fbx':'https://assets.invalid/model.fbx','obj':'https://assets.invalid/model.obj'},
            thumbnail_url='https://assets.invalid/preview.png',thumbnail_urls={'front left':'https://assets.invalid/front.png'},
            texture_urls=[{'base_color':'https://assets.invalid/color.png'}],
            result={'rigged':{'fbx_url':'https://assets.invalid/rig.fbx?sig=offline'},
                'animations':[{'glb':'https://assets.invalid/a.glb','video':'https://assets.invalid/a.mp4'}]})
        urls=dict(intake.asset_urls(result))
        self.assertEqual(len(urls),8)
        self.assertIn('source.glb',urls)
        self.assertIn('result_animations_0_video.mp4',urls)
        self.assertNotIn('source.obj',urls)
        self.assertIn('preview_front_left.png',urls)
    def test_download_resume_does_not_repeat_asset_or_post(self):
        entry=self.prior();self.write_ledger([entry])
        self.results[entry['endpoint']+'/'+entry['id']]=dict(status='SUCCEEDED',consumed_credits=15,
            model_urls={'glb':'https://assets.invalid/model.glb'})
        def download(url,timeout): return contextlib.closing(io.BytesIO(b'offline-model-bytes'))
        with patch.object(intake.urllib.request,'urlopen',side_effect=download) as request:
            intake.poll(True);intake.poll(True)
            self.assertEqual(request.call_count,1)
        self.assertEqual(self.posts(),[])
        files=json.loads((self.out/'Source/existing/downloads.json').read_text())
        self.assertEqual(files[0]['bytes'],19)
    def test_cli_lock_prevents_call(self):
        (self.out/'.intake-lock').write_text('other process')
        with patch.object(sys,'argv',['folklore298.py','status']):
            with self.assertRaises(RuntimeError): intake.main()
        self.assertEqual(self.calls,[])


if __name__=='__main__':
    unittest.main()
