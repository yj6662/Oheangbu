"""Verify rendered MP4 files by full local decode and MP4 sample-table inspection.

Standalone Python CLI; no bpy, MCP, network, media rewriting or paid calls.

    python verify_videos.py <render_folder> [<second_render_folder> ...]
    python verify_videos.py <render_folder>/progress.json --ffmpeg <ffmpeg.exe>

Writes video_validation.json beside each progress.json. TECHNICAL_PASS describes
encoding/timing only. Character quality, deformation and Unity remain UNVERIFIED.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import datetime
from fractions import Fraction
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess


DEFAULT_FFMPEG = (
    'C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/'
    'python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
)


def mp4_atoms(data, start, end):
    cursor = start
    while cursor + 8 <= end:
        size, kind = struct.unpack_from('>I4s', data, cursor)
        header = 8
        if size == 1:
            size = struct.unpack_from('>Q', data, cursor + 8)[0]
            header = 16
        elif size == 0:
            size = end - cursor
        if size < header or cursor + size > end:
            raise ValueError('Invalid MP4 atom bounds')
        yield kind, cursor + header, cursor + size
        cursor += size


def mp4_child(data, parent, kind):
    matches = [atom for atom in mp4_atoms(data, parent[1], parent[2]) if atom[0] == kind]
    if len(matches) != 1:
        raise ValueError('Expected exactly one MP4 atom ' + repr(kind))
    return matches[0]


def video_sample_table(data):
    """Read the actual video mdhd duration and stts sample-count/duration runs."""
    moov = mp4_child(data, (b'root', 0, len(data)), b'moov')
    videos = []
    for track in mp4_atoms(data, moov[1], moov[2]):
        if track[0] != b'trak':
            continue
        mdia = mp4_child(data, track, b'mdia')
        hdlr = mp4_child(data, mdia, b'hdlr')
        if data[hdlr[1] + 8:hdlr[1] + 12] != b'vide':
            continue
        mdhd = mp4_child(data, mdia, b'mdhd')
        begin = mdhd[1]
        if data[begin] == 0:
            timescale, duration = struct.unpack_from('>II', data, begin + 12)
        elif data[begin] == 1:
            timescale = struct.unpack_from('>I', data, begin + 20)[0]
            duration = struct.unpack_from('>Q', data, begin + 24)[0]
        else:
            raise ValueError('Unsupported mdhd version')
        if timescale <= 0:
            raise ValueError('Video timescale must be positive')
        stbl = mp4_child(data, mp4_child(data, mdia, b'minf'), b'stbl')
        stts = mp4_child(data, stbl, b'stts')
        entries = struct.unpack_from('>I', data, stts[1] + 4)[0]
        if stts[1] + 8 + entries * 8 > stts[2]:
            raise ValueError('Invalid stts entry count')
        runs = [struct.unpack_from('>II', data, stts[1] + 8 + i * 8) for i in range(entries)]
        if not runs or any(count <= 0 or delta <= 0 for count, delta in runs):
            raise ValueError('Expected positive, nonempty video sample table')
        constant = len({delta for _, delta in runs}) == 1
        videos.append({
            'timescale': timescale,
            'mdhd_duration_ticks': duration,
            'duration_seconds': float(Fraction(duration, timescale)),
            'sample_count': sum(count for count, _ in runs),
            'stts_duration_ticks': sum(count * delta for count, delta in runs),
            'stts_runs': [{'sample_count': count, 'sample_delta': delta} for count, delta in runs],
            'constant_sample_rate': constant,
            'sample_fps': str(Fraction(timescale, runs[0][1])) if constant else None,
        })
    if len(videos) != 1:
        raise ValueError('Expected exactly one video stream; found ' + str(len(videos)))
    return videos[0]


def parse_decode(stderr):
    configs = re.findall(
        r'config in time_base:\s*(\d+/\d+), frame_rate:\s*(\d+/\d+)', stderr)
    if not configs or len(set(configs)) != 1:
        raise ValueError('Missing or changing decoder time base/frame rate')
    time_base, fps = map(Fraction, configs[0])
    if time_base <= 0:
        raise ValueError('Invalid decoded stream time base')
    # Parse exact integer timestamps/durations, not rounded pts_time console text.
    pattern = (
        r'\bn:\s*(\d+)\s+pts:\s*(-?\d+)\s+pts_time:\S+'
        r'\s+duration:\s*(\d+)\s+duration_time:\S+\s+fmt:(\S+)'
        r'.*?\bs:(\d+)x(\d+)'
    )
    frames = [(int(n), int(pts), int(duration), fmt, int(width), int(height))
              for n, pts, duration, fmt, width, height in re.findall(pattern, stderr)]
    if not frames:
        raise ValueError('No decoded video frames')
    pts = [row[1] for row in frames]
    durations = [row[2] for row in frames]
    duration = (pts[-1] + durations[-1] - pts[0]) * time_base
    dimensions = sorted({(row[4], row[5]) for row in frames})
    intervals = sorted({(b - a) * time_base for a, b in zip(pts, pts[1:])})
    frame_durations = sorted({ticks * time_base for ticks in durations})
    public = {
        'decoded_frames': len(frames), 'decoder_fps_rational': str(fps), 'fps': float(fps),
        'time_base_rational': str(time_base), 'dimensions_seen': [list(v) for v in dimensions],
        'pixel_formats': sorted({row[3] for row in frames}),
        'first_pts_seconds': float(pts[0] * time_base),
        'last_pts_seconds': float(pts[-1] * time_base),
        'presentation_duration_seconds': float(duration),
        'presentation_duration_rational': str(duration),
        'frame_intervals_rational': [str(v) for v in intervals],
        'frame_durations_rational': [str(v) for v in frame_durations],
    }
    internal = {'frames': frames, 'pts': pts, 'fps': fps, 'duration': duration,
                'dimensions': dimensions, 'intervals': intervals, 'frame_durations': frame_durations}
    return public, internal


def validate_job(job, folder, ffmpeg, timeout=90):
    expected_count = int(job['frame_count'])
    raw_path = Path(job['mp4_path'])
    path = (folder / raw_path).resolve() if not raw_path.is_absolute() else raw_path.resolve()
    row = {
        'label': job['label'], 'action': job.get('action'), 'view': job.get('view'), 'path': str(path),
        'expected': {'fps': 30, 'width': 1280, 'height': 720, 'frames': expected_count,
                     'duration_seconds': expected_count / 30,
                     'source_key_interval_seconds': job.get('source_key_interval_seconds'),
                     'last_sample_hold_seconds': 1 / 30},
        'character_acceptance': 'UNVERIFIED',
    }
    try:
        if not path.is_relative_to(folder):
            raise ValueError('MP4 path points outside the requested render folder')
        if expected_count < 1:
            raise ValueError('Expected frame count must be positive')
        if job.get('fps') != 30:
            raise ValueError('Manifest job FPS is not 30')
        data = path.read_bytes()
        row['bytes'], row['sha256'] = len(data), hashlib.sha256(data).hexdigest()
        row['container'] = video_sample_table(data)
        command = [str(ffmpeg), '-nostdin', '-hide_banner', '-loglevel', 'info', '-xerror',
                   '-err_detect', 'explode', '-i', str(path), '-map', '0:v:0', '-vf', 'showinfo',
                   '-fps_mode', 'passthrough', '-f', 'null', '-']
        completed = subprocess.run(command, capture_output=True, text=True, check=False, timeout=timeout)
        row['decode_returncode'] = completed.returncode
        row['decoder_log_sha256'] = hashlib.sha256(completed.stderr.encode('utf-8')).hexdigest()
        row['actual'], decoded = parse_decode(completed.stderr)
        container = row['container']
        frames = decoded['frames']
        checks = {
            'full_decode_without_error': completed.returncode == 0,
            'frame_count_matches': len(frames) == expected_count,
            'decoded_indices_contiguous': [f[0] for f in frames] == list(range(len(frames))),
            'resolution_1280x720': decoded['dimensions'] == [(1280, 720)],
            'decoder_fps_30': decoded['fps'] == 30,
            'constant_pts_spacing_1_over_30': all(v == Fraction(1, 30) for v in decoded['intervals']),
            'frame_duration_1_over_30': decoded['frame_durations'] == [Fraction(1, 30)],
            'first_pts_zero': decoded['pts'][0] == 0,
            'duration_includes_final_sample': decoded['duration'] == Fraction(expected_count, 30),
            'container_frame_count_matches': container['sample_count'] == expected_count,
            'container_constant_fps_30': container['sample_fps'] == '30',
            'container_duration_matches': Fraction(container['mdhd_duration_ticks'], container['timescale'])
                                          == Fraction(expected_count, 30),
            'container_sample_duration_consistent': container['mdhd_duration_ticks']
                                                    == container['stts_duration_ticks'],
        }
        row['checks'] = checks
        row['status'] = 'PASS' if all(checks.values()) else 'FAIL'
        if completed.returncode:
            row['decoder_error_tail'] = completed.stderr[-3000:]
    except Exception as exc:
        row['status'], row['error'] = 'FAIL', repr(exc)
    return row


def validate_folder(folder_or_manifest, ffmpeg_executable=DEFAULT_FFMPEG, workers=2, timeout=90):
    supplied = Path(folder_or_manifest).resolve()
    manifest_path = supplied if supplied.is_file() else supplied / 'progress.json'
    folder = manifest_path.parent
    ffmpeg = Path(ffmpeg_executable).resolve()
    if not ffmpeg.is_file():
        raise FileNotFoundError('ffmpeg executable not found: ' + str(ffmpeg))
    manifest_bytes = manifest_path.read_bytes()
    manifest = json.loads(manifest_bytes)
    jobs = manifest['jobs']
    if not jobs:
        raise ValueError('Manifest has no video jobs')
    version = subprocess.run([str(ffmpeg), '-version'], capture_output=True, text=True,
                             check=True, timeout=10).stdout.splitlines()[0]
    with ThreadPoolExecutor(max_workers=max(1, min(int(workers), 4))) as pool:
        rows = list(pool.map(lambda job: validate_job(job, folder, ffmpeg, timeout), jobs))
    manifest_checks = {
        'render_manifest_complete': manifest.get('status') == 'COMPLETE',
        'manifest_fps_30': manifest.get('fps') == 30,
        'manifest_resolution_1280x720': manifest.get('resolution') == [1280, 720],
        'distinct_mp4_paths': len({row['path'] for row in rows}) == len(rows),
    }
    technical_ok = all(row['status'] == 'PASS' for row in rows) and all(manifest_checks.values())
    report = {
        'status': 'TECHNICAL_PASS' if technical_ok else 'TECHNICAL_FAIL',
        'character_acceptance': 'UNVERIFIED', 'visual_deformation': 'UNVERIFIED', 'unity_runtime': 'UNVERIFIED',
        'validated_at_utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
        'scope': 'MP4 encoding and timing only; no visual, anatomical, rig, gameplay or Unity acceptance implied.',
        'method': 'Independent ffmpeg full decode with showinfo and strict error detection; exact integer PTS '
                  'and frame durations; direct MP4 mdhd/stts sample-table cross-check.',
        'ffmpeg_executable': str(ffmpeg), 'ffmpeg_version': version,
        'validator': str(Path(__file__).resolve()), 'validator_sha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        'manifest': str(manifest_path), 'manifest_sha256': hashlib.sha256(manifest_bytes).hexdigest(),
        'manifest_status': manifest.get('status'), 'manifest_checks': manifest_checks,
        'clip_count': len(rows), 'expected_total_frames': sum(int(job['frame_count']) for job in jobs),
        'actual_total_decoded_frames': sum(row.get('actual', {}).get('decoded_frames', 0) for row in rows),
        'jobs': rows,
    }
    destination = folder / 'video_validation.json'
    temporary = destination.with_suffix('.json.tmp')
    temporary.write_text(json.dumps(report, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    temporary.replace(destination)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('folders', nargs='+', help='Render output folder or progress.json path')
    parser.add_argument('--ffmpeg', default=DEFAULT_FFMPEG, help='Local ffmpeg executable path')
    parser.add_argument('--workers', type=int, default=2, help='Parallel clip decodes (1..4; default 2)')
    parser.add_argument('--timeout', type=float, default=90, help='Timeout per decode in seconds')
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error('--timeout must be positive')
    summaries = []
    for folder in args.folders:
        try:
            report = validate_folder(folder, args.ffmpeg, args.workers, args.timeout)
            summaries.append({
                'folder': str(Path(report['manifest']).parent), 'status': report['status'],
                'character_acceptance': 'UNVERIFIED', 'clips': report['clip_count'],
                'decoded_frames': report['actual_total_decoded_frames'],
                'report': str(Path(report['manifest']).with_name('video_validation.json')),
                'failures': [{'label': row['label'], 'error': row.get('error'),
                              'checks': [key for key, okay in row.get('checks', {}).items() if not okay]}
                             for row in report['jobs'] if row['status'] != 'PASS'],
            })
        except Exception as exc:
            summaries.append({'folder': folder, 'status': 'TECHNICAL_FAIL',
                              'character_acceptance': 'UNVERIFIED', 'error': repr(exc)})
    print(json.dumps(summaries, ensure_ascii=False, indent=2))
    return 0 if all(row['status'] == 'TECHNICAL_PASS' for row in summaries) else 1


if __name__ == '__main__':
    raise SystemExit(main())
