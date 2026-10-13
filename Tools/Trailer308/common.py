"""Trailer308 공통 — 경로 · 상수 · ffmpeg 탐침 · 실행 (cut.py · audio.py · title.py · sheet.py · check.py가 같이 쓴다).

ffmpeg 하나로만 일한다(ffprobe 없음): probe()는 `ffmpeg -i`의 stderr를 읽고, count_frames()는
`-map 0:v -c copy -f null -`의 마지막 `frame=`을 읽는다. 모든 JSON은 UTF-8(BOM 없음).
"""
import json
import os
import re
import shlex
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]                                   # Tools/Trailer308 → 리포 루트
FILMS = ROOT / 'Art/Presentation297/Films'              # 촬영 도구(encode.py)가 놓는 <take>.mp4
OUT = ROOT / 'Art/Pitch308/Trailer'
WORK = OUT / '_work'
MUSIC = OUT / 'music'
SFX = ROOT / 'Oheangbu/Assets/_Project/Audio/Compact255'
DECK_PPTX = ROOT / 'Art/Pitch308/오행부_피칭_v20_제출용.pptx'
FONT_TITLE = Path(r'C:\Windows\Fonts\H2GSRB.TTF')       # HY궁서 B
FONT_SEAL = Path(r'C:\Windows\Fonts\batang.ttc')

W, H, FPS = 1920, 1080, 30
BAR = 138                                                # 2.39:1 띠 — 1080 − 804 = 276 → 위아래 138
VIEW_H = H - 2 * BAR                                     # 804
OUTPUT = {30: 'trailer308_30_v4.mp4', 90: 'trailer308_90_v4.mp4'}
DECK_OUT = {30: 'trailer308_deck_v4.mp4', 90: 'trailer308_deck90_v4.mp4'}
PLAY_MIN = {30: 18.0, 90: 50.0}                           # AC-4
DECK_MAX_BYTES = 25 * 1024 * 1024                        # AC-7

for _s in (sys.stdout, sys.stderr):                       # Git Bash · 콘솔 코드페이지와 무관하게 한글 출력
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass


def ffmpeg_exe():
    env = os.environ.get('TRAILER308_FFMPEG')
    if env and Path(env).exists():
        return env
    try:
        import imageio_ffmpeg
        return imageio_ffmpeg.get_ffmpeg_exe()
    except Exception:
        pass
    fallback = Path(r'C:\Users\yj666\AppData\Roaming\Python\Python314\site-packages\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v7.1.exe')
    if fallback.exists():
        return str(fallback)
    return 'ffmpeg'


FFMPEG = ffmpeg_exe()


def ensure_dirs():
    for d in (OUT, WORK, MUSIC):
        d.mkdir(parents=True, exist_ok=True)


def fmt_cmd(cmd):
    return ' '.join(shlex.quote(str(c)) for c in cmd)


def run(cmd, log=None, check=True, timeout=None):
    """subprocess.run 래퍼: (CompletedProcess, 소요 초). log가 있으면 stderr를 그 파일에 덧붙인다."""
    cmd = [str(c) for c in cmd]
    t0 = time.perf_counter()
    p = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
    dt = time.perf_counter() - t0
    if log:
        with open(log, 'a', encoding='utf-8') as f:
            f.write(f'$ {fmt_cmd(cmd)}\n[{dt:.2f}s rc={p.returncode}]\n{p.stderr}\n')
    if check and p.returncode != 0:
        tail = '\n'.join(p.stderr.strip().splitlines()[-12:])
        raise RuntimeError(f'command failed rc={p.returncode}: {fmt_cmd(cmd)}\n{tail}')
    return p, dt


_DUR = re.compile(r'Duration: (\d+):(\d+):(\d+(?:\.\d+)?)')
# `Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709/unknown/unknown, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 30 fps, …`
#  group1 코덱 · group2 pix_fmt 토큰 · group4 괄호 안 태그(색 범위 · 색 공간 · 필드 순서, 없을 수 있음) · group5/6 해상도.
#  괄호 안의 쉼표 때문에 `[^,]+` 로는 태그가 붙은 스트림을 잡지 못한다(검토 지적 — encode.py 산출물은 태그가 없지만 -color_range/-colorspace 를 준 파일은 있다).
_VID = re.compile(r'Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?: Video: ([^,]+), ([a-z0-9_]+)(\(([^)]*)\))?[^,]*, (\d+)x(\d+)')
_FPS = re.compile(r'(\d+(?:\.\d+)?) fps')
_AUD = re.compile(r'Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?: Audio: ([^,]+), (\d+) Hz, ([^,]+)')


def parse_video_line(err):
    """`ffmpeg -i` stderr → {vcodec, pix_fmt, range(tv|pc|None), color_tags(괄호 안 원문), width, height, fps}. 비디오 스트림이 없으면 None."""
    m = _VID.search(err)
    if not m:
        return None
    tags = (m.group(4) or '').strip()
    tokens = [t.strip() for t in tags.split(',')] if tags else []
    rng = next((t for t in tokens if t in ('tv', 'pc')), None)
    line = err[m.start():err.find('\n', m.start()) if err.find('\n', m.start()) >= 0 else len(err)]
    f = _FPS.search(line)
    return {'vcodec': m.group(1).split(' ')[0], 'pix_fmt': m.group(2), 'range': rng, 'color_tags': tags or None,
            'width': int(m.group(5)), 'height': int(m.group(6)), 'fps': float(f.group(1)) if f else None}


def probe(path):
    """`ffmpeg -i`로 길이 · 해상도 · fps · pix_fmt · 색 범위(tv|pc) · 색 태그 · 오디오를 읽는다. 없는 항목은 None."""
    path = Path(path)
    if not path.exists():
        raise FileNotFoundError(path)
    p, _ = run([FFMPEG, '-hide_banner', '-i', path], check=False)
    err = p.stderr
    info = {'path': str(path), 'bytes': path.stat().st_size, 'duration': None, 'vcodec': None, 'pix_fmt': None,
            'range': None, 'color_tags': None, 'width': None, 'height': None, 'fps': None, 'acodec': None, 'sample_rate': None, 'channels': None}
    m = _DUR.search(err)
    if m:
        info['duration'] = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))
    v = parse_video_line(err)
    if v:
        info.update(v)
    m = _AUD.search(err)
    if m:
        info['acodec'] = m.group(1).split(' ')[0]
        info['sample_rate'] = int(m.group(2))
        ch = m.group(3).strip()
        info['channels'] = 2 if ch.startswith('stereo') else 1 if ch.startswith('mono') else ch
    return info


_FRAME = re.compile(r'frame=\s*(\d+)')


def count_frames(path):
    """정확한 비디오 프레임 수(디코드 없이 패킷 수): `-map 0:v -c copy -f null -`의 마지막 frame=."""
    p, _ = run([FFMPEG, '-hide_banner', '-i', path, '-map', '0:v', '-c', 'copy', '-f', 'null', '-'], check=False)
    found = _FRAME.findall(p.stderr)
    return int(found[-1]) if found else None


def write_json(path, obj):
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)
        f.write('\n')


def read_json(path, default=None):
    try:
        with open(path, encoding='utf-8') as f:
            return json.load(f)
    except FileNotFoundError:
        return default


def frames_of(seconds):
    return int(round(seconds * FPS))


def on_grid(seconds, eps=1e-6):
    """1/30 배수인가."""
    return abs(seconds * FPS - round(seconds * FPS)) < eps


def scan_films(folder=None):
    """<take>.mp4 → probe dict. 폴더가 없으면 빈 dict."""
    folder = Path(folder or FILMS)
    out = {}
    if not folder.exists():
        return out
    for f in sorted(folder.glob('*.mp4')):
        try:
            out[f.stem] = probe(f)
        except Exception as e:                           # noqa: BLE001 — 깨진 파일은 표에 이유만 남긴다
            out[f.stem] = {'path': str(f), 'error': str(e), 'duration': None}
    return out


def mp4_moov_before_mdat(path):
    """faststart 확인: 최상위 박스 순서에서 moov가 mdat보다 앞인가. (None = 판정 불가)"""
    pos, size = 0, Path(path).stat().st_size
    order = []
    with open(path, 'rb') as f:
        while pos + 8 <= size:
            f.seek(pos)
            head = f.read(16)
            if len(head) < 8:
                break
            n = int.from_bytes(head[:4], 'big')
            kind = head[4:8].decode('latin1')
            if n == 1:
                n = int.from_bytes(head[8:16], 'big')
            elif n == 0:
                n = size - pos
            if n < 8:
                break
            order.append(kind)
            pos += n
    if 'moov' in order and 'mdat' in order:
        return order.index('moov') < order.index('mdat')
    return None


# ---------------------------------------------------------------- 자체 시험: python common.py  (probe 정규식 — 태그 있음 · 없음 · 실제 파일)
_SAMPLES = [
    ('tagged', "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709/unknown/unknown, progressive), "
               "1920x1080 [SAR 1:1 DAR 16:9], 4985 kb/s, 30 fps, 30 tbr, 15360 tbn (default)\n",
     {'vcodec': 'h264', 'pix_fmt': 'yuv420p', 'range': 'tv', 'width': 1920, 'height': 1080, 'fps': 30.0}),
    ('untagged', "  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1920x1080 [SAR 1:1 DAR 16:9], "
                 "50 kb/s, 30 fps, 30 tbr, 15360 tbn (default)\n",
     {'vcodec': 'h264', 'pix_fmt': 'yuv420p', 'range': None, 'width': 1920, 'height': 1080, 'fps': 30.0}),
    ('bare', "  Stream #0:0: Video: png, rgb24(pc), 1920x1080, 25 fps, 25 tbr, 25 tbn\n",
     {'vcodec': 'png', 'pix_fmt': 'rgb24', 'range': 'pc', 'width': 1920, 'height': 1080, 'fps': 25.0}),
    ('mjpeg', "  Stream #0:0: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 960x540 [SAR 1:1 DAR 16:9], 25 fps\n",
     {'vcodec': 'mjpeg', 'pix_fmt': 'yuvj420p', 'range': 'pc', 'width': 960, 'height': 540, 'fps': 25.0}),
]


def selftest(tmp_dir=None):
    """정규식 샘플 4종 + ffmpeg로 만든 태그 있는 mp4(-color_range tv -colorspace bt709)를 실제로 probe. 실패하면 AssertionError."""
    fails = []
    for name, line, want in _SAMPLES:
        got = parse_video_line(line)
        bad = {k: (got and got.get(k), v) for k, v in want.items() if not got or got.get(k) != v}
        print(f'{"ok  " if not bad else "FAIL"} sample {name:9s} {got}')
        if bad:
            fails.append((name, bad))
    tmp = Path(tmp_dir or os.environ.get('TRAILER308_TMP') or (WORK / 'probe_test'))
    tmp.mkdir(parents=True, exist_ok=True)
    cases = {'tagged.mp4': ['-color_range', 'tv', '-colorspace', 'bt709', '-color_primaries', 'bt709', '-color_trc', 'bt709'], 'plain.mp4': []}
    for fn, extra in cases.items():
        f = tmp / fn
        run([FFMPEG, '-y', '-loglevel', 'error', '-f', 'lavfi', '-i', f'testsrc2=size={W}x{H}:rate={FPS}', '-t', '0.5', '-c:v', 'libx264',
             '-preset', 'ultrafast', '-pix_fmt', 'yuv420p'] + extra + [f])
        got = probe(f)
        want = {'width': W, 'height': H, 'fps': float(FPS), 'pix_fmt': 'yuv420p', 'range': 'tv' if extra else None}
        bad = {k: (got.get(k), v) for k, v in want.items() if got.get(k) != v}
        print(f'{"ok  " if not bad else "FAIL"} file   {fn:9s} {{{", ".join(f"{k}={got[k]!r}" for k in ("vcodec", "pix_fmt", "range", "color_tags", "width", "height", "fps", "duration"))}}}')
        if bad:
            fails.append((fn, bad))
    if fails:
        raise AssertionError(f'probe selftest FAIL: {fails}')
    print('probe selftest PASS')
    return True


if __name__ == '__main__':
    selftest()
