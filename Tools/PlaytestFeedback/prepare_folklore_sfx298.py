"""Offline original-preserving SFX preparation; no credentials, network or generation.

Explicit stereo arithmetic mean avoids the default equal-power mono boost. A single
attenuation factor leaves quiet clips untouched, limits sample/4x estimated peaks to
-1 dBFS and RMS to -18 dBFS. No compression, trimming, fades or regeneration.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import wave
import numpy as np
import folklore_sfx298 as sfx

ALGORITHM = 'stereo-mean-48k-s16-attenuate-only-v1'
PEAK_LIMIT = 10 ** (-1 / 20)
RMS_LIMIT = 10 ** (-18 / 20)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def stats(samples, rate=48000):
    if not len(samples) or not np.isfinite(samples).all():
        raise ValueError('Empty or nonfinite PCM')
    return dict(samples=len(samples), seconds=len(samples) / rate,
                peak=float(np.max(np.abs(samples))),
                rms=float(np.sqrt(np.mean(samples.astype(np.float64) ** 2))),
                dc=float(np.mean(samples.astype(np.float64))),
                nonzero=int(np.count_nonzero(np.abs(samples) > .00001)))


def acceptable(value, requested):
    return (abs(value['seconds'] - requested) <= max(.12, requested * .1)
            and .0001 <= value['peak'] <= .995 and value['rms'] >= .00001
            and value['nonzero'] >= 48 and abs(value['dc']) <= .05)


def attenuation(samples, peak4):
    value = stats(samples)
    return min(1., PEAK_LIMIT / max(value['peak'], peak4), RMS_LIMIT / value['rms'])


def execute(ffmpeg, args, data=None):
    done = subprocess.run([str(ffmpeg), '-v', 'error', '-nostdin', *args], input=data,
                          capture_output=True, check=False)
    if done.returncode:
        raise ValueError('Offline audio decode/resample failed')
    return done.stdout


def peak4(ffmpeg, samples):
    raw = execute(ffmpeg, ['-f', 'f32le', '-ar', '48000', '-ac', '1', '-i', 'pipe:0',
                         '-ar', '192000', '-f', 'f32le', 'pipe:1'], samples.astype('<f4').tobytes())
    return stats(np.frombuffer(raw, dtype='<f4'), 192000)['peak']


def wav_bytes(samples):
    # Input is attenuated in float before this conversion; never repair clipped s16.
    pcm = np.rint(samples.astype(np.float64) * 32767).astype('<i2')
    stream = io.BytesIO()
    with wave.open(stream, 'wb') as handle:
        handle.setnchannels(1); handle.setsampwidth(2); handle.setframerate(48000)
        handle.writeframes(pcm.tobytes())
    return stream.getvalue(), pcm.astype(np.float32) / 32768


def prepare(ids, ffmpeg, out=sfx.OUT):
    path = out / 'manifest.json'
    data = json.loads(path.read_text(encoding='utf-8'))
    intake = sfx.validate_import(data, ids, out, path)
    decoder_sha = sha(ffmpeg.read_bytes())
    rows, analyses = [], []
    for item in intake['clips']:
        original = out / item['file']
        # Eleven's requested mp3_44100_128 is stereo. Decode both float channels;
        # arithmetic mean is explicit and avoids equal-power (+3dB) downmix.
        raw = execute(ffmpeg, ['-i', str(original), '-ac', '2', '-ar', '48000', '-f', 'f32le', 'pipe:1'])
        stereo = np.frombuffer(raw, dtype='<f4').reshape(-1, 2)
        mono = np.mean(stereo.astype(np.float64), axis=1).astype(np.float32)
        before = stats(mono); before['peak4_estimate'] = peak4(ffmpeg, mono)
        if abs(before['seconds'] - item['duration_seconds']) > max(.12, item['duration_seconds'] * .1):
            raise ValueError('Original decoded duration differs from manifest: ' + item['id'])
        if before['peak'] < .0001 or before['rms'] < .00001 or before['nonzero'] < 48:
            raise ValueError('Original has insufficient signal: ' + item['id'])
        gain = attenuation(mono, before['peak4_estimate'])
        encoded, decoded = wav_bytes(mono * gain)
        after = stats(decoded); after['peak4_estimate'] = peak4(ffmpeg, decoded)
        if not acceptable(after, item['duration_seconds']) or after['peak4_estimate'] > PEAK_LIMIT + .0001:
            raise ValueError('Prepared PCM violates technical contract: ' + item['id'])
        prepared = 'Prepared/' + item['id'] + '.wav'
        receipt_file = 'Processing/' + item['id'] + '.json'
        receipt = dict(schema=1, id=item['id'], algorithm=ALGORITHM, decoder_sha256=decoder_sha,
                       original_file=item['file'], original_sha256=item['sha256'],
                       request_sha256=item['request_sha256'], gain=gain, sample_rate=48000,
                       channels=1, bits=16, peak_limit=PEAK_LIMIT, rms_limit=RMS_LIMIT,
                       before=before, after=after, file=prepared, sha256=sha(encoded), bytes=len(encoded),
                       scope='Technical PCM preparation only. Fourfold resample peak is an estimate; no listening or Unity DSP claim.',
                       listening_review='NOT_PERFORMED_NO_LISTENING_TOOL', paid_requests=0)
        target = out / prepared; target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or target.read_bytes() != encoded:
            target.write_bytes(encoded)
        # Deterministic processing records make reruns byte-identical.
        sfx.save(out / receipt_file, receipt)
        row = dict(item, original_file=item['file'], original_sha256=item['sha256'], original_bytes=item['bytes'])
        row.update(file=prepared, sha256=sha(encoded), bytes=len(encoded), processing_file=receipt_file,
                   processing_sha256=sha((out / receipt_file).read_bytes()), gain=gain)
        rows.append(row); analyses.append(receipt)
    # Reject source mutation while decoding before exporting a usable intake.
    repeat = sfx.validate_import(data, ids, out, path)
    if repeat['clips'] != intake['clips']:
        raise ValueError('Generation inputs changed during preparation')
    intake.update(schema=2, status='VERIFIED_PREPARED_INTAKE_NATIVE_PCM_PENDING', clips=rows,
                  scope='Successful local ElevenLabs POST receipt/body/original SHA and derived WAV chain. Provider IDs absent when not supplied. Not independent provider attestation or listening approval.',
                  pcm_validation='OFFLINE_48K_MONO_S16_PASS_NATIVE_UNITY_PENDING', listening_review='NOT_PERFORMED_NO_LISTENING_TOOL')
    sfx.save(out / 'validated-intake.json', intake)
    requests = [json.loads((out / row['request_file']).read_text()) for row in rows]
    summary = dict(utc=sfx.now(), status='OFFLINE_PCM_PASS_UNITY_AND_LISTENING_PENDING', count=len(rows),
                   requested_seconds=sum(row['duration_seconds'] for row in rows),
                   decoded_seconds=sum(row['after']['seconds'] for row in analyses),
                   actual_credits=sum(r['actual_credits'] for r in requests),
                   reserved_credits=sum(r['reserved_credits'] for r in requests),
                   provider_request_ids=sum(not row['provider_request_id_absent'] for row in rows),
                   attenuated=sum(row['gain'] < 1 for row in rows),
                   peak_max=max(row['after']['peak'] for row in analyses),
                   peak4_estimate_max=max(row['after']['peak4_estimate'] for row in analyses),
                   original_files_unchanged=True, paid_requests_this_command=0, listening_verified=False,
                   native_unity_pcm_verified=False, clips=analyses)
    sfx.save(out.parent / 'Analysis/audio-preparation.json', summary)
    return {key: value for key, value in summary.items() if key != 'clips'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ids', required=True, help='Explicit comma-separated successful IDs')
    parser.add_argument('--ffmpeg', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(prepare(args.ids.split(','), args.ffmpeg), ensure_ascii=False))
