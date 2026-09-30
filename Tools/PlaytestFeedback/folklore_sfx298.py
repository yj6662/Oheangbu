"""Bounded ElevenLabs SFX intake for #298. Import/dry-run never access credentials/network.

Generate requires explicit IDs, cumulative request/seconds/estimated-credit caps, and
an operator-supplied credit rate. Estimates are not provider billing guarantees or
spending authorization. Every attempted POST is reserved first and never retried.
"""
import argparse
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path
import re
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Characters/Folklore298/Audio'
MODEL = 'eleven_text_to_sound_v2'
ENDPOINT = 'https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128'
SPECIES = {
    'dokkaebi': 'A burly Korean dokkaebi goblin, woody chest resonance and rough amused breath',
    'agwi': 'A gaunt Korean hungry ghost, hollow dry throat and strained rasping breath',
    'changgui': 'A Korean tiger-bound changgui ghost, thin spectral breath and restrained feline rasp',
    'bulgasari': 'A heavy Korean iron-eating bulgasari beast, deep animal breath with iron and stone texture',
    'fox_spirit': 'A Korean fox spirit, agile short fox yip, fine breath and soft fur movement',
    'imugi': 'A large Korean imugi serpent, low reptilian throat resonance and scale friction',
    'cheongryong': 'A great Korean azure dragon, powerful low wooden chest resonance and restrained wind breath',
    'jangsu': 'An armored Korean warrior general, low nonverbal exertion with cloth and restrained metal contact',
}
ROLES = {
    'alert': (1.5, 'one brief alert call on noticing danger'),
    'windup': (1.0, 'one short gathering breath and effort before attacking, no impact'),
    'hit_a': (.65, 'one short involuntary pain grunt from a light hit, variation A'),
    'hit_b': (.75, 'one distinct clipped pain grunt from a hit, variation B'),
    'death': (2.0, 'one final exhale and body settling, ending cleanly in silence'),
}


def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.writing')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temp.replace(path)


def manifest():
    clips = []
    for actor, character in SPECIES.items():
        for role, (seconds, action) in ROLES.items():
            clips.append(dict(id=f'{actor}_{role}', actor=actor, role=role,
                duration_seconds=seconds, loop=False, prompt_influence=.6,
                text=f'{character}: {action}. Isolated dry game sound effect, close and clear, '
                     'one gesture only. No words, speech, music, long ambience, cinematic bass drop or clipping.'))
    return dict(schema=1, provider='ElevenLabs', model_id=MODEL, output_format='mp3_44100_128',
        scope='Original SFX only; Unity import, listening and decoded PCM validation remain required.', clips=clips)


def validate(data):
    if data.get('schema') != 1 or data.get('model_id') != MODEL or data.get('output_format') != 'mp3_44100_128':
        raise ValueError('Unsupported manifest schema/model/output format')
    seen = set()
    for clip in data['clips']:
        identifier = clip['id']
        if not isinstance(identifier, str) or not re.fullmatch(r'[a-z][a-z0-9_]{1,95}', identifier) or identifier in seen:
            raise ValueError('Invalid or duplicate clip ID')
        seen.add(identifier)
        if clip['actor'] not in SPECIES or clip['role'] not in ROLES:
            raise ValueError('Unknown actor or role')
        if type(clip['duration_seconds']) not in (float, int) or not .5 <= clip['duration_seconds'] <= 30:
            raise ValueError('Explicit finite duration must be 0.5 to 30 seconds')
        if type(clip['loop']) is not bool or type(clip['prompt_influence']) not in (int, float) or not 0 <= clip['prompt_influence'] <= 1:
            raise ValueError('Invalid loop or prompt influence')
        if not isinstance(clip['text'], str) or not 1 <= len(clip['text'].strip()) <= 450:
            raise ValueError('Prompt must contain 1 to 450 characters')
    if not seen:
        raise ValueError('Empty manifest')


def summary(data):
    validate(data)
    return dict(status='DRY_RUN_NO_NETWORK', clips=len(data['clips']),
        seconds=round(sum(c['duration_seconds'] for c in data['clips']), 4),
        loops=sum(c['loop'] for c in data['clips']), manifest_sha256=digest(data),
        ids=[c['id'] for c in data['clips']], credentials_read=False, paid_requests=0)


def validate_import(data, ids, out, manifest_path):
    """Verify recorded intake only. PCM/listening require Unity/a human; no key/network.

    The exported receipt binds exact source/ledger/manifest file hashes for Unity.
    It records provider provenance; it is not an independent provider attestation.
    """
    validate(data)
    if data.get('provider') != 'ElevenLabs' or not ids or len(ids) != len(set(ids)):
        raise ValueError('Explicit unique generated clip IDs and ElevenLabs provenance required')
    if not manifest_path.is_file() or manifest_path.resolve() != (out / 'manifest.json').resolve():
        raise ValueError('Import uses the recorded Audio/manifest.json only')
    if json.loads(manifest_path.read_text(encoding='utf-8')) != data:
        raise ValueError('Manifest file changed during verification')
    if (out / '.generation.lock').exists():
        raise ValueError('Generation is still active or unresolved')
    clips = {c['id']: c for c in data['clips']}
    rows = []
    for identifier in ids:
        clip = clips[identifier]
        request_path = out / 'Requests' / (identifier + '.json')
        request = json.loads(request_path.read_text(encoding='utf-8'))
        original = out / 'Originals' / (identifier + '.mp3')
        if request['id'] != identifier or request['status'] != 'SUCCEEDED' or request['clip_sha256'] != digest(clip) or request['manifest_sha256'] != digest(data):
            raise ValueError('Only successful receipts matching the unchanged manifest may import')
        expected = {k: clip[k] for k in ('text', 'duration_seconds', 'prompt_influence', 'loop')}
        expected['model_id'] = MODEL
        request_id = request.get('request_id')
        # This endpoint can omit both request-id headers. Preserve absence explicitly;
        # the successful local POST receipt/body/original chain is not provider attestation.
        if request['request'] != expected or request.get('file') != 'Originals/' + original.name or (request_id is not None and (not isinstance(request_id, str) or not re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', request_id))):
            raise ValueError('Provider request provenance is incomplete')
        body_json = json.dumps(expected, sort_keys=True, separators=(',', ':'))
        raw = original.read_bytes()
        raw_sha = hashlib.sha256(raw).hexdigest()
        if len(raw) != request['bytes'] or not 128 <= len(raw) <= 16 * 1024 * 1024 or raw_sha != request['sha256'] or not (raw[:3] == b'ID3' or raw[0] == 255 and raw[1] & 224 == 224):
            raise ValueError('Original audio bytes fail the generation receipt')
        rows.append(dict(id=identifier, actor=clip['actor'], role=clip['role'], duration_seconds=clip['duration_seconds'], loop=clip['loop'],
            file='Originals/' + original.name, sha256=raw_sha, bytes=len(raw), request_file='Requests/' + request_path.name,
            request_sha256=hashlib.sha256(request_path.read_bytes()).hexdigest(), request_id=request_id or '',
            provider_request_id_absent=request_id is None, request_body_json=body_json,
            request_body_sha256=hashlib.sha256(body_json.encode()).hexdigest(), clip_sha256=digest(clip)))
    return dict(schema=1, utc=now(), status='VERIFIED_GENERATED_INTAKE_PCM_PENDING', provider='ElevenLabs', model_id=MODEL,
        manifest_file='manifest.json', manifest_file_sha256=hashlib.sha256(manifest_path.read_bytes()).hexdigest(),
        manifest_canonical_sha256=digest(data), scope='Recorded successful provider requests and original hashes only; not PCM, listening, DSP or rights certification.',
        credentials_read=False, paid_requests=0, pcm_validation='PENDING', listening_review='PENDING', clips=rows)


def key_from_env(path):
    # Read only this named key at execution time. Do not log file contents or key fragments.
    key = ''
    if path.exists():
        for line in path.read_text(encoding='utf-8-sig').splitlines():
            match = re.fullmatch(r'\s*(?:export\s+)?ELEVENLABS_API_KEY\s*=\s*(.*?)\s*', line)
            if match:
                key = match[1].strip().strip('"\'')
                break
    if not key:
        key = os.environ.get('ELEVENLABS_API_KEY', '').strip()
    if not key or '\n' in key or '\r' in key:
        raise ValueError('ELEVENLABS_API_KEY is not configured')
    return key


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, response, code, message, headers, new_url):
        # Never forward xi-api-key or turn a POST into a second request after a redirect.
        return None


def generate(data, ids, out, env_file, max_requests, max_seconds, max_credits, credits_per_second, transport=None):
    validate(data)
    if not ids or len(ids) != len(set(ids)):
        raise ValueError('Explicit unique --ids are required')
    clips = {c['id']: c for c in data['clips']}
    if any(i not in clips for i in ids):
        raise ValueError('Requested ID is absent from manifest')
    if type(max_requests) is not int or max_requests < 1 or any(
            type(x) not in (float, int) or not math.isfinite(x) or x <= 0
            for x in (max_seconds, max_credits, credits_per_second)):
        raise ValueError('Positive explicit request/seconds/estimated-credit caps and credit rate are required')
    key = key_from_env(env_file)
    out.mkdir(parents=True, exist_ok=True)
    lock = out / '.generation.lock'
    try:
        with lock.open('x', encoding='utf-8') as handle:
            handle.write('One process only. A stale lock requires manual ledger reconciliation.\n')
    except FileExistsError:
        raise ValueError('Generation lock exists; reconcile interrupted work first') from None
    results = []
    try:
        ledger_dir = out / 'Requests'
        originals = out / 'Originals'
        ledger_dir.mkdir(exist_ok=True)
        originals.mkdir(exist_ok=True)
        prior = [json.loads(p.read_text(encoding='utf-8')) for p in ledger_dir.glob('*.json')]
        known = {p['id']: p for p in prior}
        pending = []
        for identifier in ids:
            clip = clips[identifier]
            if identifier in known:
                entry = known[identifier]
                if entry['clip_sha256'] != digest(clip):
                    raise ValueError('An attempted ID has a different manifest; do not overwrite its history')
                if entry['status'] != 'SUCCEEDED':
                    raise ValueError('An attempted request is unresolved or failed; no automatic retry')
                original = originals / (identifier + '.mp3')
                if not original.exists() or hashlib.sha256(original.read_bytes()).hexdigest() != entry['sha256']:
                    raise ValueError('Successful original is missing or changed; no automatic regeneration')
                results.append(dict(id=identifier, status='ALREADY_SUCCEEDED_NO_REQUEST'))
            else:
                if (originals / (identifier + '.mp3')).exists():
                    raise ValueError('Untracked original exists; refusing overwrite')
                pending.append(clip)
        # All attempted requests, including ambiguous failures, consume the conservative local cap.
        used_seconds = sum(p['request']['duration_seconds'] for p in prior)
        used_credits = sum(max(p['reserved_credits'], p.get('actual_credits') or 0) for p in prior)
        planned_seconds = sum(c['duration_seconds'] for c in pending)
        if len(prior) + len(pending) > max_requests or used_seconds + planned_seconds > max_seconds + 1e-8 or used_credits + planned_seconds * credits_per_second > max_credits + 1e-8:
            raise ValueError('Cumulative generation cap would be exceeded; no requests sent')
        send = transport or urllib.request.build_opener(NoRedirect()).open
        for clip in pending:
            # Recheck after each response if actual usage was larger than the reservation.
            reserved = clip['duration_seconds'] * credits_per_second
            if used_credits + reserved > max_credits + 1e-8:
                raise ValueError('Provider usage exhausted the local cap; remaining requests stopped')
            body = {k: clip[k] for k in ('text', 'duration_seconds', 'prompt_influence', 'loop')}
            body['model_id'] = MODEL
            entry = dict(id=clip['id'], status='REQUEST_STARTED', utc=now(), clip_sha256=digest(clip),
                manifest_sha256=digest(data), request=body, reserved_credits=reserved,
                credit_rate_assumed=credits_per_second, actual_credits=None,
                cap=dict(requests=max_requests, seconds=max_seconds, estimated_credits=max_credits))
            path = ledger_dir / (clip['id'] + '.json')
            # Lock serializes cap evaluation; exclusive create also prevents overwriting an attempt.
            with path.open('x', encoding='utf-8') as handle:
                json.dump(entry, handle, ensure_ascii=False, indent=2)
                handle.flush()
                os.fsync(handle.fileno())
            try:
                request = urllib.request.Request(ENDPOINT, data=json.dumps(body).encode(), method='POST',
                    headers={'xi-api-key': key, 'Content-Type': 'application/json'})
                with send(request, timeout=180) as response:
                    content_type = response.headers.get('Content-Type', '').split(';')[0].lower()
                    raw = response.read(16 * 1024 * 1024 + 1)
                    if content_type not in ('audio/mpeg', 'audio/mp3') or not 128 <= len(raw) <= 16 * 1024 * 1024 or not (raw[:3] == b'ID3' or raw[0] == 255 and raw[1] & 224 == 224):
                        raise ValueError('Invalid audio response')
                    usage = response.headers.get('character-cost') or response.headers.get('x-character-cost')
                    if usage is not None:
                        usage = float(usage)
                        if not math.isfinite(usage) or usage < 0:
                            raise ValueError('Invalid usage metadata')
                        entry['actual_credits'] = usage
                    request_id = response.headers.get('request-id') or response.headers.get('x-request-id')
                    if request_id and re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', request_id):
                        entry['request_id'] = request_id
                original = originals / (clip['id'] + '.mp3')
                with original.open('xb') as handle:
                    handle.write(raw)
                entry.update(status='SUCCEEDED', file='Originals/' + original.name, bytes=len(raw),
                    sha256=hashlib.sha256(raw).hexdigest(), pcm_validation='PENDING', listening_review='PENDING')
            except urllib.error.HTTPError as error:
                entry.update(status='HTTP_ERROR_NO_AUTO_RETRY', http_status=error.code)
                # Only a fixed error category is retained. Account data, provider
                # messages, headers and credential values never enter the ledger.
                try:
                    detail = json.loads(error.read().decode('utf-8')).get('detail', {})
                    category = detail.get('status') if isinstance(detail, dict) else None
                    allowed = {'invalid_api_key', 'missing_permissions', 'quota_exceeded',
                               'unauthorized', 'authentication_error', 'rate_limit_exceeded'}
                    entry['error_category'] = category if category in allowed else 'UNCLASSIFIED'
                except (ValueError, UnicodeError, AttributeError):
                    entry['error_category'] = 'UNCLASSIFIED'
                error.close()
            except Exception:
                entry.update(status='UNKNOWN_RESULT_NO_AUTO_RETRY')
            entry['finished_at'] = now()
            save(path, entry)
            results.append({k: entry[k] for k in ('id', 'status')})
            if entry['status'] != 'SUCCEEDED':
                break
            used_credits += max(reserved, entry['actual_credits'] or 0)
        return results
    finally:
        lock.unlink()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['manifest', 'dry-run', 'generate', 'validate-import'])
    parser.add_argument('--manifest', type=Path, default=OUT / 'manifest.json')
    parser.add_argument('--out', type=Path, default=OUT)
    parser.add_argument('--env-file', type=Path, default=ROOT / '.env')
    parser.add_argument('--ids', help='Comma-separated explicit clip IDs; no implicit paid batch')
    parser.add_argument('--max-requests', type=int)
    parser.add_argument('--max-seconds', type=float)
    parser.add_argument('--max-credits', type=float, help='Cumulative estimated credits; not a provider billing guarantee')
    parser.add_argument('--credits-per-second', type=float, help='Operator-verified rate; no obsolete/default price assumed')
    args = parser.parse_args(argv)
    try:
        if args.command == 'manifest':
            data = manifest()
            validate(data)
            if args.manifest.exists() and json.loads(args.manifest.read_text(encoding='utf-8')) != data:
                raise ValueError('Existing edited manifest preserved; choose a new path')
            save(args.manifest, data)
            result = summary(data)
        else:
            data = json.loads(args.manifest.read_text(encoding='utf-8')) if args.manifest.exists() else manifest()
            if args.command == 'dry-run':
                result = summary(data)
            elif args.command == 'validate-import':
                result = validate_import(data, args.ids.split(',') if args.ids else [], args.out, args.manifest)
                save(args.out / 'validated-intake.json', result)
            else:
                result = generate(data, args.ids.split(',') if args.ids else [], args.out, args.env_file,
                    args.max_requests, args.max_seconds, args.max_credits, args.credits_per_second)
        print(json.dumps(result, ensure_ascii=False))
        return 1 if isinstance(result, list) and any(r['status'] not in ('SUCCEEDED', 'ALREADY_SUCCEEDED_NO_REQUEST') for r in result) else 0
    except (ValueError, KeyError, TypeError, OSError, json.JSONDecodeError) as error:
        # CLI error text is deliberately fixed: unexpected local data may include credentials.
        print(json.dumps(dict(status='REFUSED', error_type=type(error).__name__,
            action='Check manifest, explicit caps, key presence and existing ledgers; do not retry unresolved attempts.')))
        return 2


if __name__ == '__main__':
    raise SystemExit(main())
