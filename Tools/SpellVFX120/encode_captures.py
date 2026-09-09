"""Encode and fully decode-check captured VFX PNG sequences, without deleting PNGs.

    python Tools/SpellVFX120/encode_captures.py --limit 1
    python Tools/SpellVFX120/encode_captures.py

Input: ClipFrames/001_AC00/0000.png and ClipFrames/001_AC00_capture.json.
Only technical encoding/timing is verified. Art, gameplay and source-version
agreement are not inferred from a valid video. Requires local FFmpeg, no network.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from fractions import Fraction
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import subprocess
import sys
import uuid

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'Art/SpellVFX120'
DEFAULT_FFMPEG = Path.home() / ('.cache/codex-runtimes/codex-primary-runtime/dependencies/'
    'python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe')
SETTINGS = {'codec': 'libx264', 'fps': 24, 'crf': 19, 'preset': 'medium',
            'pixelFormat': 'yuv420p', 'faststart': True}
SCHEMA = 1


class IncompleteCapture(ValueError):
    pass


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat()


def sha256(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_json(path: Path, value: dict) -> None:
    temporary = path.with_name(path.name + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(path)


def run(command: list[str], timeout: float) -> subprocess.CompletedProcess:
    return subprocess.run(command, capture_output=True, text=True, encoding='utf-8',
                          errors='replace', timeout=timeout, check=False)


def inspect_input(folder: Path, ident: str, glyph: str) -> dict:
    """Hash original bytes and require a contiguous, complete 24fps capture."""
    metadata = folder / f'{ident}_capture.json'
    sequence = folder / ident
    if not metadata.is_file() or not sequence.is_dir():
        raise IncompleteCapture('Missing capture metadata or PNG sequence directory')
    metadata_bytes = metadata.read_bytes()
    evidence = json.loads(metadata_bytes.decode('utf-8-sig'))
    if not isinstance(evidence, dict):
        raise ValueError('Capture metadata must be a JSON object')
    if evidence.get('glyph') != glyph:
        raise ValueError('Capture glyph does not match catalog ID')
    times, life = evidence.get('sampledSeconds'), evidence.get('life')
    if not isinstance(times, list) or not times:
        raise ValueError('sampledSeconds must contain every expected frame time')
    if isinstance(life, bool) or not isinstance(life, (int, float)) or not math.isfinite(life) or life <= 0:
        raise ValueError('Capture life must be finite and positive')
    for index, time in enumerate(times):
        if isinstance(time, bool) or not isinstance(time, (int, float)) or not math.isfinite(time):
            raise ValueError('Invalid sampledSeconds value')
        # Unity stores these timestamps as float; this permits its representation error only.
        if abs(time - index / SETTINGS['fps']) > 0.00001:
            raise ValueError('Capture is not a zero-based uniform 24fps sequence')
    if times[-1] + 0.00001 < life:
        raise IncompleteCapture('Captured samples do not reach the complete authored lifetime')
    expected = [f'{i:04d}.png' for i in range(len(times))]
    found = {p.name for p in sequence.iterdir() if p.is_file() and p.suffix.lower() == '.png'}
    missing = sorted(set(expected) - found)
    extra = sorted(found - set(expected))
    if missing:
        raise IncompleteCapture(f'Missing {len(missing)} PNG frames; first: {missing[0]}')
    if extra:
        raise ValueError(f'Unexpected/stale PNG frames; first: {extra[0]}')
    digest = hashlib.sha256()
    digest.update(metadata_bytes)
    dimensions = set()
    total_bytes = 0
    for name in expected:
        path = sequence / name
        content = path.read_bytes()
        if len(content) < 64 or content[:8] != b'\x89PNG\r\n\x1a\n' or content[12:16] != b'IHDR':
            raise ValueError(f'Invalid PNG header: {name}')
        width, height = struct.unpack('>II', content[16:24])
        if width < 2 or height < 2 or width % 2 or height % 2:
            raise ValueError(f'PNG dimensions must be positive and even for yuv420p: {name}')
        dimensions.add((width, height))
        total_bytes += len(content)
        digest.update(name.encode('ascii') + b'\0')
        digest.update(hashlib.sha256(content).digest())
    if len(dimensions) != 1:
        raise ValueError('PNG dimensions change within the sequence')
    width, height = dimensions.pop()
    return {'inputSha256': digest.hexdigest(), 'metadataSha256': hashlib.sha256(metadata_bytes).hexdigest(),
            'metadataPath': str(metadata), 'sequencePath': str(sequence), 'frames': len(times),
            'width': width, 'height': height, 'fps': SETTINGS['fps'], 'pngBytes': total_bytes,
            'life': life, 'firstSampleSeconds': times[0], 'lastSampleSeconds': times[-1],
            'expectedDurationSeconds': len(times) / SETTINGS['fps'],
            'fullLifetimeSampled': True, 'captureStatus': evidence.get('status'),
            'demonstrationCues': evidence.get('demonstrationCues'),
            'captureSourceVersionMatch': 'UNVERIFIED'}


def verify_video(path: Path, source: dict, ffmpeg: Path, timeout: float) -> dict:
    """Decode all frames and check exact rational timestamps, dimensions and format."""
    command = [str(ffmpeg), '-nostdin', '-hide_banner', '-loglevel', 'info', '-xerror',
               '-err_detect', 'explode', '-i', str(path), '-map', '0:v:0', '-an', '-sn', '-dn',
               '-vf', 'showinfo', '-fps_mode', 'passthrough', '-f', 'null', '-']
    decoded = run(command, timeout)
    if decoded.returncode != 0:
        raise ValueError('Full video decode failed: ' + decoded.stderr[-1800:])
    configs = re.findall(r'config in time_base:\s*(\d+/\d+), frame_rate:\s*(\d+/\d+)', decoded.stderr)
    if not configs or len(set(configs)) != 1:
        raise ValueError('Missing or changing decoded time base/frame rate')
    time_base, fps = map(Fraction, configs[0])
    pattern = (r'\bn:\s*(\d+)\s+pts:\s*(-?\d+)\s+pts_time:\S+'
               r'\s+duration:\s*(\d+)\s+duration_time:\S+\s+fmt:(\S+).*?\bs:(\d+)x(\d+)')
    frames = [(int(n), int(pts), int(duration), fmt, int(w), int(h))
              for n, pts, duration, fmt, w, h in re.findall(pattern, decoded.stderr)]
    count = source['frames']
    step = Fraction(1, SETTINGS['fps'])
    checks = {
        'fullDecodeWithoutError': True,
        'h264Input': bool(re.search(r'Stream #0:\d+[^\r\n]*Video: h264\b', decoded.stderr)),
        'frameCountMatches': len(frames) == count,
        'frameIndicesContiguous': [f[0] for f in frames] == list(range(count)),
        'fps24': fps == SETTINGS['fps'],
        'allDimensionsMatchInput': bool(frames) and all((f[4], f[5]) == (source['width'], source['height']) for f in frames),
        'allPixelFormatsYuv420p': bool(frames) and all(f[3] == SETTINGS['pixelFormat'] for f in frames),
        'everyTimestampMatches24fps': bool(frames) and all(f[1] * time_base == i * step for i, f in enumerate(frames)),
        'everyFrameDurationMatches24fps': bool(frames) and all(f[2] * time_base == step for f in frames),
    }
    if not all(checks.values()):
        raise ValueError('Decoded video verification failed: ' + ', '.join(k for k, passed in checks.items() if not passed))
    return {'status': 'VERIFIED_ENCODING_AND_TIMING_ONLY', 'checks': checks,
            'decodedFrames': len(frames), 'fpsRational': str(fps),
            'timeBaseRational': str(time_base), 'dimensions': [source['width'], source['height']],
            'durationSeconds': float(count * step),
            'decodeLogSha256': hashlib.sha256(decoded.stderr.encode('utf-8')).hexdigest()}


def encode_one(folder: Path, output: Path, ident: str, glyph: str,
               ffmpeg: Path, version: str, validator_hash: str, timeout: float) -> dict:
    row = {'id': ident, 'glyph': glyph, 'status': 'NOT_VERIFIED'}
    temporary = None
    try:
        source = inspect_input(folder, ident, glyph)
        row['input'] = source
        destination = output / f'{ident}.mp4'
        sidecar = output / f'{ident}_encoding.json'
        previous = {}
        if sidecar.is_file():
            try:
                previous = json.loads(sidecar.read_text(encoding='utf-8-sig'))
            except (OSError, ValueError):
                pass
        matching = (isinstance(previous, dict) and previous.get('schema') == SCHEMA
                    and previous.get('status') == 'VERIFIED_ENCODING_AND_TIMING_ONLY'
                    and previous.get('inputSha256') == source['inputSha256']
                    and previous.get('settings') == SETTINGS and previous.get('ffmpegVersion') == version
                    and previous.get('validatorSha256') == validator_hash
                    and destination.is_file() and previous.get('outputSha256') == sha256(destination))
        if matching:
            try:
                verification = verify_video(destination, source, ffmpeg, timeout)
            except (OSError, ValueError, subprocess.TimeoutExpired):
                matching = False
        if not matching:
            temporary = output / f'.{ident}.{uuid.uuid4().hex}.encoding.mp4'
            command = [str(ffmpeg), '-nostdin', '-hide_banner', '-loglevel', 'error', '-xerror',
                       '-err_detect', 'explode', '-framerate', str(SETTINGS['fps']), '-start_number', '0',
                       '-i', str(folder / ident / '%04d.png'), '-frames:v', str(source['frames']),
                       '-an', '-c:v', SETTINGS['codec'], '-preset', SETTINGS['preset'],
                       '-crf', str(SETTINGS['crf']), '-pix_fmt', SETTINGS['pixelFormat'],
                       '-fps_mode', 'passthrough', '-movflags', '+faststart', str(temporary)]
            encoded = run(command, timeout)
            if encoded.returncode != 0:
                raise ValueError('Encoding failed: ' + encoded.stderr[-1800:])
            verification = verify_video(temporary, source, ffmpeg, timeout)
        # Unity may still be writing this folder. Never publish a result from changing inputs.
        if inspect_input(folder, ident, glyph)['inputSha256'] != source['inputSha256']:
            raise IncompleteCapture('Input changed during encoding/verification; retry after capture finishes')
        if not matching:
            temporary.replace(destination)
            temporary = None
        proof = {'schema': SCHEMA, 'status': 'VERIFIED_ENCODING_AND_TIMING_ONLY',
                 'id': ident, 'glyph': glyph, 'verifiedAtUtc': utc_now(),
                 'inputSha256': source['inputSha256'], 'metadataSha256': source['metadataSha256'],
                 'outputSha256': sha256(destination), 'outputBytes': destination.stat().st_size,
                 'settings': SETTINGS, 'ffmpegVersion': version, 'validatorSha256': validator_hash,
                 'input': source, 'verification': verification,
                 'artGameplayPerformance': 'UNVERIFIED'}
        write_json(sidecar, proof)
        row.update({'status': 'VERIFIED_ENCODING_AND_TIMING_ONLY',
                    'action': 'REUSED_MATCHING_INPUT_AND_REDECODED' if matching else 'ENCODED_AND_DECODED',
                    'output': str(destination), 'sidecar': str(sidecar), 'outputSha256': proof['outputSha256'],
                    'verification': verification})
    except IncompleteCapture as error:
        row.update(status='INCOMPLETE_CAPTURE', error=str(error))
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        row.update(status='FAILED_ENCODING_OR_VALIDATION', error=str(error))
    finally:
        # This is only our newly created temporary MP4, never a PNG or previous verified MP4.
        if temporary is not None and temporary.is_file():
            temporary.unlink()
    return row


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--input', type=Path, default=ART / 'ClipFrames')
    parser.add_argument('--output', type=Path, default=ART / 'Clips')
    parser.add_argument('--manifest', type=Path, default=ART / 'build_manifest.json')
    parser.add_argument('--ffmpeg', type=Path, default=DEFAULT_FFMPEG)
    parser.add_argument('--limit', type=int, help='Inspect/encode only the first N catalog entries; never claims 120 coverage.')
    parser.add_argument('--timeout', type=float, default=180, help='Seconds per encoding or decoding command.')
    args = parser.parse_args()
    if args.limit is not None and not 1 <= args.limit <= 120:
        parser.error('--limit must be between 1 and 120')
    if not math.isfinite(args.timeout) or args.timeout <= 0:
        parser.error('--timeout must be positive')
    folder, output, ffmpeg = args.input.resolve(), args.output.resolve(), args.ffmpeg.resolve()
    if output == folder:
        parser.error('--input and --output must be different directories')
    definitions = json.loads(args.manifest.read_text(encoding='utf-8-sig'))['spells']
    if len(definitions) != 120 or len({s['glyph'] for s in definitions}) != 120:
        parser.error('Manifest must contain exactly 120 unique glyphs in catalog order')
    if any(not isinstance(s['glyph'], str) or len(s['glyph']) != 1 for s in definitions):
        parser.error('Manifest glyphs must be single Unicode characters')
    catalog = [(f'{i:03d}_{ord(s["glyph"]):04X}', s['glyph']) for i, s in enumerate(definitions, 1)]
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / 'encoding_report.json'
    report = {'schema': SCHEMA, 'status': 'RUNNING', 'startedAtUtc': utc_now(),
              'input': str(folder), 'output': str(output), 'manifest': str(args.manifest.resolve()),
              'manifestSha256': sha256(args.manifest), 'expectedCoverage': 120,
              'selectedCount': args.limit or 120, 'verifiedCoverage': 0, 'coverage120': False,
              'settings': SETTINGS, 'artGameplayPerformance': 'UNVERIFIED',
              'scope': 'Complete PNG sequences encoded to H264 and fully decoded. File/timing verification only. PNGs are never deleted.',
              'results': [{'id': ident, 'glyph': glyph, 'status': 'NOT_SELECTED' if i >= (args.limit or 120) else 'NOT_INSPECTED'}
                          for i, (ident, glyph) in enumerate(catalog)]}
    write_json(report_path, report)
    try:
        if not ffmpeg.is_file():
            raise FileNotFoundError(f'Local FFmpeg not found: {ffmpeg}')
        version_result = run([str(ffmpeg), '-version'], 10)
        if version_result.returncode != 0 or not version_result.stdout:
            raise ValueError('Could not read local FFmpeg version')
        version = version_result.stdout.splitlines()[0]
        validator_hash = sha256(Path(__file__))
        report.update(ffmpeg=str(ffmpeg), ffmpegVersion=version, validatorSha256=validator_hash)
        for i, (ident, glyph) in enumerate(catalog[:args.limit or 120]):
            row = encode_one(folder, output, ident, glyph, ffmpeg, version, validator_hash, args.timeout)
            report['results'][i] = row
            report['verifiedCoverage'] = sum(r['status'] == 'VERIFIED_ENCODING_AND_TIMING_ONLY' for r in report['results'])
            write_json(report_path, report)
            print(json.dumps({'id': ident, 'status': row['status'], 'action': row.get('action'),
                              'error': row.get('error')}, ensure_ascii=False), flush=True)
        report['coverage120'] = report['verifiedCoverage'] == 120
        if report['coverage120']:
            report['status'] = 'VERIFIED_120_ENCODING_AND_TIMING_ONLY'
        elif report['verifiedCoverage'] == report['selectedCount']:
            report['status'] = 'VERIFIED_SELECTED_ONLY_COVERAGE_INCOMPLETE'
        else:
            report['status'] = 'INCOMPLETE_OR_FAILED_NO_COVERAGE_PASS'
    except (OSError, ValueError, subprocess.TimeoutExpired, KeyboardInterrupt) as error:
        report.update(status='INTERRUPTED_OR_FAILED', error=str(error) or type(error).__name__)
    report['finishedAtUtc'] = utc_now()
    write_json(report_path, report)
    print(json.dumps({k: v for k, v in report.items() if k not in ('results', 'scope')}, ensure_ascii=False))
    return 0 if report['status'].startswith('VERIFIED_') else 1


if __name__ == '__main__':
    sys.exit(main())
