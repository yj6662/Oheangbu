"""Offline contract tests. All transports are in-memory fakes; no provider calls."""
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import folklore_sfx298 as sfx


class Response:
    def __init__(self, usage='1', raw=None):
        self.headers = {'Content-Type': 'audio/mpeg', 'character-cost': usage, 'request-id': 'offline-test'}
        self.raw = raw if raw is not None else b'ID3' + bytes(1000)
    def __enter__(self): return self
    def __exit__(self, *args): pass
    def read(self, limit): return self.raw[:limit]


class Contracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.out = Path(self.temp.name) / 'Audio'
        self.data = sfx.manifest()
        self.ids = [self.data['clips'][0]['id']]
        self.env = Path(self.temp.name) / '.env'
        self.env.write_text('ELEVENLABS_API_KEY=offline-fake-secret\n', encoding='utf-8')
        self.sent = []
    def fake(self, request, timeout):
        self.sent.append(json.loads(request.data))
        self.assertEqual(request.get_method(), 'POST')
        return Response()
    def run_generate(self, **overrides):
        values = dict(data=self.data, ids=self.ids, out=self.out, env_file=self.env,
            max_requests=40, max_seconds=47.2, max_credits=100, credits_per_second=1, transport=self.fake)
        values.update(overrides)
        with patch.dict(os.environ, {}, clear=True):
            return sfx.generate(**values)
    def entry(self):
        return json.loads((self.out / 'Requests' / (self.ids[0] + '.json')).read_text())
    def test_manifest_explicit_and_complete(self):
        result = sfx.summary(self.data)
        self.assertEqual((result['clips'], result['seconds'], result['loops']), (40, 47.2, 0))
        self.assertEqual(len({c['actor'] for c in self.data['clips']}), 8)
    def test_dry_run_does_not_read_key_or_send(self):
        with patch.object(sfx, 'key_from_env', side_effect=AssertionError('key read')), \
             patch.object(sfx.urllib.request, 'urlopen', side_effect=AssertionError('network')), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(sfx.main(['dry-run', '--manifest', str(self.out / 'missing.json')]), 0)
        self.assertFalse(self.out.exists())
    def test_success_and_no_duplicate(self):
        self.assertEqual(self.run_generate()[0]['status'], 'SUCCEEDED')
        self.assertEqual(self.run_generate()[0]['status'], 'ALREADY_SUCCEEDED_NO_REQUEST')
        self.assertEqual(len(self.sent), 1)
        self.assertEqual(self.entry()['pcm_validation'], 'PENDING')
    def test_loop_forwarded(self):
        self.data['clips'][0]['loop'] = True
        self.run_generate()
        self.assertIs(self.sent[0]['loop'], True)
        self.assertEqual(self.sent[0]['duration_seconds'], 1.5)
    def test_validate_import_hash_contract_without_network(self):
        self.run_generate(); sfx.save(self.out/'manifest.json',self.data)
        with patch.object(sfx,'key_from_env',side_effect=AssertionError('key read')), patch.object(sfx.urllib.request,'build_opener',side_effect=AssertionError('network')):
            result=sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
        self.assertEqual(result['status'],'VERIFIED_GENERATED_INTAKE_PCM_PENDING')
        self.assertEqual(result['clips'][0]['sha256'],self.entry()['sha256'])
        self.assertEqual(result['pcm_validation'],'PENDING')
    def test_validate_import_refuses_tamper_or_unresolved(self):
        self.run_generate(); sfx.save(self.out/'manifest.json',self.data)
        original=self.out/'Originals'/(self.ids[0]+'.mp3'); original.write_bytes(b'ID3'+bytes(1001))
        with self.assertRaises(ValueError): sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
        original.write_bytes(Response().raw)
        entry=self.entry();entry['status']='UNKNOWN_RESULT_NO_AUTO_RETRY';sfx.save(self.out/'Requests'/(self.ids[0]+'.json'),entry)
        with self.assertRaises(ValueError): sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
    def test_validate_import_preserves_missing_provider_id_without_invention(self):
        self.run_generate(); sfx.save(self.out/'manifest.json',self.data)
        entry=self.entry();entry.pop('request_id');sfx.save(self.out/'Requests'/(self.ids[0]+'.json'),entry)
        result=sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')['clips'][0]
        self.assertTrue(result['provider_request_id_absent'])
        self.assertEqual(result['request_id'],'')
        self.assertEqual(json.loads(result['request_body_json']),entry['request'])
        self.assertEqual(result['request_body_sha256'],sfx.digest(entry['request']))
        data=dict(self.data,provider='Synthetic')
        with self.assertRaises(ValueError): sfx.validate_import(data,self.ids,self.out,self.out/'manifest.json')
    def test_validate_import_rejects_malformed_id_and_body(self):
        self.run_generate(); sfx.save(self.out/'manifest.json',self.data)
        entry=self.entry();entry['request_id']='\ninvalid';sfx.save(self.out/'Requests'/(self.ids[0]+'.json'),entry)
        with self.assertRaises(ValueError):sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
        entry.pop('request_id');entry['request']['text']='different';sfx.save(self.out/'Requests'/(self.ids[0]+'.json'),entry)
        with self.assertRaises(ValueError):sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
    def test_validate_import_requires_explicit_ids_unchanged_manifest_and_no_lock(self):
        self.run_generate(); sfx.save(self.out/'manifest.json',self.data)
        for ids in ([],self.ids*2):
            with self.assertRaises(ValueError):sfx.validate_import(self.data,ids,self.out,self.out/'manifest.json')
        self.data['clips'][0]['text']+=' edit'
        with self.assertRaises(ValueError):sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
        (self.out/'.generation.lock').touch()
        with self.assertRaises(ValueError):sfx.validate_import(self.data,self.ids,self.out,self.out/'manifest.json')
    def test_missing_explicit_caps(self):
        for missing in ('max_requests', 'max_seconds', 'max_credits', 'credits_per_second'):
            with self.assertRaises(ValueError): self.run_generate(**{missing: None})
        self.assertEqual(self.sent, [])
    def test_batch_preflight_caps(self):
        for overrides in ({'max_requests': 1, 'ids': [c['id'] for c in self.data['clips'][:2]]},
                          {'max_seconds': 1}, {'max_credits': 1}, {'max_credits': float('nan')}):
            with self.assertRaises(ValueError): self.run_generate(**overrides)
        self.assertEqual(self.sent, [])
    def test_changed_attempt_cannot_regenerate(self):
        self.run_generate()
        self.data['clips'][0]['text'] += ' changed'
        with self.assertRaises(ValueError): self.run_generate()
        self.assertEqual(len(self.sent), 1)
    def test_http_body_never_logged_and_no_retry(self):
        def fail(*args, **kwargs):
            raise urllib.error.HTTPError('https://invalid', 401, 'sensitive', {}, io.BytesIO(b'private-account-email-and-secret'))
        self.assertEqual(self.run_generate(transport=fail)[0]['status'], 'HTTP_ERROR_NO_AUTO_RETRY')
        raw = (self.out / 'Requests' / (self.ids[0] + '.json')).read_text()
        self.assertNotIn('sensitive', raw)
        self.assertNotIn('private-account', raw)
        self.assertNotIn('offline-fake-secret', raw)
        with self.assertRaises(ValueError): self.run_generate()
        self.assertEqual(self.sent, [])
    def test_unknown_result_stops_batch_and_is_reserved(self):
        def fail(*args, **kwargs): raise TimeoutError('secret')
        results = self.run_generate(ids=[c['id'] for c in self.data['clips'][:2]], transport=fail)
        self.assertEqual(len(results), 1)
        self.assertEqual(results[0]['status'], 'UNKNOWN_RESULT_NO_AUTO_RETRY')
        self.assertEqual(self.entry()['reserved_credits'], 1.5)
        with self.assertRaises(ValueError): self.run_generate()
    def test_actual_usage_stops_remaining(self):
        def expensive(*args, **kwargs): return Response(usage='10')
        with self.assertRaises(ValueError):
            self.run_generate(ids=[c['id'] for c in self.data['clips'][:2]], max_credits=3, transport=expensive)
        self.assertEqual(len(list((self.out / 'Requests').glob('*.json'))), 1)
    def test_invalid_audio_is_not_saved(self):
        self.run_generate(transport=lambda *a, **kw: Response(raw=b'{"key":"secret"}'))
        self.assertEqual(self.entry()['status'], 'UNKNOWN_RESULT_NO_AUTO_RETRY')
        self.assertFalse(list((self.out / 'Originals').glob('*')))
    def test_lock_prevents_parallel_run(self):
        self.out.mkdir()
        (self.out / '.generation.lock').write_text('other process')
        with self.assertRaises(ValueError): self.run_generate()
        self.assertEqual(self.sent, [])
    def test_missing_key_sends_nothing(self):
        self.env.unlink()
        with self.assertRaises(ValueError): self.run_generate()
        self.assertEqual(self.sent, [])
    def test_runtime_env_file_precedes_stale_process_key(self):
        with patch.dict(os.environ, {'ELEVENLABS_API_KEY': 'stale-fake'}, clear=True):
            self.assertEqual(sfx.key_from_env(self.env), 'offline-fake-secret')
        self.assertIsNone(sfx.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://other.invalid'))
    def test_bad_ids_durations_and_boolean_loop(self):
        for field, value in (('id', '../outside'), ('duration_seconds', .1), ('duration_seconds', float('nan')), ('loop', 'true')):
            data = sfx.manifest()
            data['clips'][0][field] = value
            with self.assertRaises(ValueError): sfx.validate(data)
    def test_missing_original_is_not_regenerated(self):
        self.run_generate()
        (self.out / self.entry()['file']).unlink()
        with self.assertRaises(ValueError): self.run_generate()
        self.assertEqual(len(self.sent), 1)


if __name__ == '__main__':
    unittest.main()
