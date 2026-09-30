"""#297 presentation captures through the editor queue (Presentation297.cs).

usage:
  python Tools/Unity/presentation297.py shot <name> <eye x,y,z> <target x,y,z> [fov=60] [w=1920] [h=1080] [hideplayer]
  python Tools/Unity/presentation297.py film <spec.json>      # Play mode; waits, then encodes <name>.mp4 (H.264)
  python Tools/Unity/presentation297.py encode <name> [fps]
  python Tools/Unity/presentation297.py raw <command>          # any Presentation297 command
Output: Art/Presentation297/{Stills,Films}
"""
import contextlib
import io
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Unity'))
from playtest_polish import call  # noqa: E402

OUT = ROOT / 'Art/Presentation297'


def run(command, timeout=600):
    with contextlib.redirect_stdout(io.StringIO()):
        r = call('Oheangbu.EditorTools.WorldMacro.Presentation297', 'Run', command, timeout)
    if r.get('status') != 'COMPLETE':
        raise RuntimeError(f"{command}: {r.get('status')} {r.get('error') or r.get('result')}")
    return r['result']


def encode(name, fps=30):
    import imageio_ffmpeg
    folder = OUT / 'Films' / name
    target = OUT / 'Films' / f'{name}.mp4'
    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-loglevel', 'error', '-framerate', str(fps), '-i', str(folder / 'f%05d.png'),
           '-c:v', 'libx264', '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(target)]
    subprocess.run(cmd, check=True)
    return target


def film(spec_path):
    spec = json.loads(Path(spec_path).read_text(encoding='utf-8'))
    print(run('film:' + str(Path(spec_path).resolve())))
    while True:
        time.sleep(3)
        state = json.loads(run('film-status', 120))
        if state['status'] != 'running':
            break
        print(f"  {state['frames']}/{state['total']}", flush=True)
    if state['status'] != 'done':
        raise RuntimeError('film ' + state['status'] + ' ' + (state.get('error') or ''))
    return encode(spec['name'], spec.get('fps', 30))


if __name__ == '__main__':
    verb = sys.argv[1]
    if verb == 'shot':
        print(run(':'.join(['shot'] + sys.argv[2:])))
    elif verb == 'film':
        print(film(sys.argv[2]))
    elif verb == 'encode':
        print(encode(sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 30))
    elif verb == 'raw':
        print(run(sys.argv[2]))
    else:
        raise SystemExit(__doc__)
