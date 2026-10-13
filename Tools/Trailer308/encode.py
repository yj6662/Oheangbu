"""Trailer 4: one take's PNG sequence -> mp4, then the PNGs are deleted (the 2026-10-09 shoot left 32 GB of frames behind).

  python Tools/Trailer308/encode.py <take> [--keep] [--crf 12] [--preset slow] [--fps 30] [--films DIR]

  Art/Presentation297/Films/<take>/f%05d.png  ->  Art/Presentation297/Films/<take>.mp4
  libx264 -preset slow -crf 12 -pix_fmt yuv420p -movflags +faststart, 30 fps.
  The mp4 is decoded once (-f null) and its frame count must match the PNG count within 1; only then are the PNGs removed
  (never with --keep). Exit 0 = encoded (and cleaned) | 1 = frame count mismatch or ffmpeg failed (PNGs kept) | 2 = no PNG folder / 0 frames.
"""
import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FILMS = ROOT / 'Art/Presentation297/Films'
FFMPEG_FALLBACK = Path(os.environ.get('APPDATA', '')) / 'Python/Python314/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'


def ffmpeg_exe():
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        pass
    if FFMPEG_FALLBACK.exists(): return str(FFMPEG_FALLBACK)
    return 'ffmpeg'


def count_frames(ffmpeg, mp4):
    """frames in the file: the last 'frame=N' of a null decode (no ffprobe on this PC)"""
    r = subprocess.run([ffmpeg, '-v', 'info', '-nostats', '-i', str(mp4), '-map', '0:v:0', '-f', 'null', '-'], capture_output=True, text=True, errors='replace')
    found = re.findall(r'frame=\s*(\d+)', r.stderr + r.stdout)
    return int(found[-1]) if found else -1


def encode(take, keep=False, crf=12, preset='slow', fps=30, films=FILMS):
    folder = Path(films) / take
    out = Path(films) / (take + '.mp4')
    pngs = sorted(folder.glob('f*.png')) if folder.is_dir() else []
    if not pngs:
        print('ENCODE %s: no PNG frames in %s' % (take, folder)); return 2
    numbers = [int(p.stem[1:]) for p in pngs if p.stem[1:].isdigit()]
    start = min(numbers) if numbers else 0
    if numbers and max(numbers) - start + 1 != len(numbers):
        print('ENCODE %s: PNG numbering has gaps (%d files, f%05d..f%05d) - encoding what is there' % (take, len(numbers), start, max(numbers)))
    ffmpeg = ffmpeg_exe()
    cmd = [ffmpeg, '-y', '-v', 'error', '-nostats', '-framerate', str(fps), '-start_number', str(start), '-i', str(folder / 'f%05d.png'),
           '-c:v', 'libx264', '-preset', preset, '-crf', str(crf), '-pix_fmt', 'yuv420p', '-movflags', '+faststart', '-r', str(fps), str(out)]
    r = subprocess.run(cmd, capture_output=True, text=True, errors='replace')
    if r.returncode != 0 or not out.exists():
        print('ENCODE %s: ffmpeg failed (%d): %s' % (take, r.returncode, (r.stderr or '').strip()[-400:])); return 1
    n = count_frames(ffmpeg, out)
    if abs(n - len(pngs)) > 1:
        print('ENCODE %s: mp4 has %d frames, %d PNGs - PNGs kept' % (take, n, len(pngs))); return 1
    size_mb = out.stat().st_size / 2 ** 20
    removed = 0
    if not keep:
        for p in pngs:
            try: p.unlink(); removed += 1
            except OSError as e: print('  could not delete %s: %s' % (p.name, e))
        try:
            if not any(folder.iterdir()): folder.rmdir()
        except OSError: pass
    print('ENCODED %s %d frames (%d PNG) -> %s %.1f MB%s' % (take, n, len(pngs), out, size_mb, ' (PNGs kept)' if keep else ', %d PNG deleted' % removed))
    return 0


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('take'); ap.add_argument('--keep', action='store_true')
    ap.add_argument('--crf', type=int, default=12); ap.add_argument('--preset', default='slow'); ap.add_argument('--fps', type=int, default=30)
    ap.add_argument('--films', default=str(FILMS))
    a = ap.parse_args()
    return encode(a.take, a.keep, a.crf, a.preset, a.fps, Path(a.films))


if __name__ == '__main__':
    sys.exit(main())
