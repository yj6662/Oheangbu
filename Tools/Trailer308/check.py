"""검사 — SPEC-TRAILER-308 §4차 AC를 실측해 PASS/FAIL 표 + _work/check_<N>.json. FAIL이 하나라도 있으면 exit 1.

  AC-1  프레임 수(= `-map 0:v -c copy -f null -`의 frame=) 900/2,700 · 1920×1080 · 30 fps · aac 48 kHz 스테레오
  AC-4  플레이 초 ≥ 18 / 50 — report_<N>.json의 실제 컷(ALT · drop 반영)
  AC-5  채도: 출력 / 입력 테이크 0.95–1.05 (제목 · 암전 제외, 138–942행, HSV S 평균)
  AC-7  --deck: 덱 판 ≤ 25 MB · moov가 mdat 앞(faststart) · 프레임 수 · aac
  inputs  Films/*.mp4 표(길이 · 해상도 · fps · pix_fmt · 색 범위 · 어느 타임라인이 쓰는가 · ALT/DROP 예상)

usage: python check.py 30|90 [--out MP4] [--deck] [--preview] [--fake] [--no-sat]   |   python check.py inputs [--fake]
"""
import argparse
import subprocess
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
from common import (BAR, DECK_MAX_BYTES, DECK_OUT, FFMPEG, FILMS, FPS, H, OUT, OUTPUT, PLAY_MIN, W, WORK, count_frames,  # noqa: E402
                    ensure_dirs, mp4_moov_before_mdat, probe, read_json, scan_films, write_json)
from timeline import ALT, AbortTimeline, Cut, REQUIRED, play_seconds, resolve, starts, timeline  # noqa: E402

SAT_LO, SAT_HI = 0.95, 1.05
SAMPLE_W, SAMPLE_H = 480, 270


def frame_rgb(path, t):
    cmd = [FFMPEG, '-loglevel', 'error', '-ss', f'{t:.4f}', '-i', str(path), '-frames:v', '1', '-vf', f'scale={SAMPLE_W}:{SAMPLE_H}:flags=bicubic',
           '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-']
    raw = subprocess.run(cmd, capture_output=True).stdout
    if len(raw) < SAMPLE_W * SAMPLE_H * 3:
        return None
    return np.frombuffer(raw[:SAMPLE_W * SAMPLE_H * 3], np.uint8).reshape(SAMPLE_H, SAMPLE_W, 3)


def mean_saturation(rgb):
    """HSV S의 평균, 138–942행만(샘플 해상도로 축척)."""
    y0, y1 = int(round(BAR * SAMPLE_H / H)), int(round((H - BAR) * SAMPLE_H / H))
    band = rgb[y0:y1].astype(np.float32)
    mx, mn = band.max(2), band.min(2)
    s = np.where(mx > 8, (mx - mn) / np.maximum(mx, 1), 0.0)
    return float(s.mean())


def saturation_ratio(out_path, cuts, t0s, films_dir):
    pairs = []
    for c, t0 in zip(cuts, t0s):
        if c.kind != 'shot':
            continue
        take = Path(films_dir) / f'{c.take}.mp4'
        if not take.exists():
            continue
        times = [t0 + 0.5 + j for j in range(int(c.dur))] or [t0 + c.dur / 2]
        for t in times:
            if t >= t0 + c.dur:
                continue
            s = c.src + (t - t0) * c.speed
            fo, fi = frame_rgb(out_path, t), frame_rgb(take, s)
            if fo is None or fi is None:
                continue
            pairs.append((c.name, round(t, 2), mean_saturation(fo), mean_saturation(fi)))
    if not pairs:
        return None, []
    so = np.mean([p[2] for p in pairs])
    si = np.mean([p[3] for p in pairs])
    return (so / si if si > 1e-6 else None), pairs


def row(cid, name, value, expect, ok):
    return {'id': cid, 'name': name, 'value': value, 'expect': expect, 'pass': bool(ok)}


def check_file(path, N, preview=False, label='final'):
    rows = []
    info = probe(path)
    frames = count_frames(path)
    rows.append(row('AC-1', f'{label} 프레임 수', frames, N * FPS, frames == N * FPS))
    want = (960, 540) if preview else (W, H)
    rows.append(row('AC-1', f'{label} 해상도', f"{info['width']}x{info['height']}", f'{want[0]}x{want[1]}', (info['width'], info['height']) == want))
    rows.append(row('AC-1', f'{label} fps', info['fps'], FPS, info['fps'] is not None and abs(info['fps'] - FPS) < 1e-3))
    rows.append(row('AC-1', f'{label} 오디오', f"{info['acodec']} {info['sample_rate']} ch{info['channels']}", 'aac 48000 ch2',
                    info['acodec'] == 'aac' and info['sample_rate'] == 48000 and info['channels'] == 2))
    return rows, info


def check(N, out=None, deck=False, preview=False, fake=False, no_sat=False):
    ensure_dirs()
    out = Path(out) if out else (WORK / f'preview_{N}.mp4' if preview else OUT / OUTPUT[N])
    rows = []
    if not out.exists():
        rows.append(row('AC-1', '출력 존재', str(out), 'exists', False))
        return rows
    rows, _ = check_file(out, N, preview)
    rep = read_json(WORK / f'report_{N}{"_preview" if preview else ""}.json')   # cut.py --preview 는 report_<N>_preview.json 에 쓴다
    films_dir = Path(rep['films_dir']) if rep and rep.get('films_dir') else (WORK / 'test_takes' if fake else FILMS)
    if rep:
        cuts = [Cut.from_dict(x) for x in rep['cuts']]
        src = 'report'
    else:
        films = scan_films(films_dir)
        try:
            cuts, _ = resolve(timeline(N), {k: v.get('duration') for k, v in films.items()})
        except AbortTimeline:
            cuts = timeline(N)
        src = 'timeline'
    ps = play_seconds(cuts)
    rows.append(row('AC-4', f'플레이 초({src})', ps, f'≥ {PLAY_MIN[N]}', ps >= PLAY_MIN[N] - 1e-6))
    if not no_sat:
        ratio, pairs = saturation_ratio(out, cuts, starts(cuts), films_dir)
        if ratio is None:
            rows.append(row('AC-5', '채도 출력/입력', None, f'{SAT_LO}–{SAT_HI}', False))
        else:
            rows.append(row('AC-5', f'채도 출력/입력({len(pairs)} 샘플)', round(ratio, 4), f'{SAT_LO}–{SAT_HI}', SAT_LO <= ratio <= SAT_HI))
    if deck:
        d = OUT / DECK_OUT[N]
        if not d.exists():
            rows.append(row('AC-7', '덱 판 존재', str(d), 'exists', False))
        else:
            drows, dinfo = check_file(d, N, False, 'deck')
            rows += drows
            rows.append(row('AC-7', '덱 판 크기', f'{dinfo["bytes"] / 1e6:.2f} MB', '≤ 25 MB', dinfo['bytes'] <= DECK_MAX_BYTES))
            fast = mp4_moov_before_mdat(d)
            rows.append(row('AC-7', '덱 판 moov 앞', fast, True, fast is True))
            pp = (rep or {}).get('deck', {}).get('pptx') if rep else None
            if pp:
                rows.append(row('AC-7', 'pptx 사본 삽입', pp.get('pptx_copy') or pp.get('error') or pp.get('skipped'), '사본에 add_movie · 원본 불변',
                                bool(pp.get('pptx_copy')) and pp.get('original_unchanged', False)))
    return rows


def print_rows(rows):
    for r in rows:
        print(f"{'PASS' if r['pass'] else 'FAIL'}  {r['id']:5s} {r['name']:28s} {str(r['value']):>24s}   기대 {r['expect']}")


def inputs(fake=False):
    films_dir = WORK / 'test_takes' if fake else FILMS
    films = scan_films(films_dir)
    need = {}
    for n in (30, 90):
        for c in timeline(n):
            if c.kind == 'shot':
                need.setdefault(c.take, set()).add(str(n))
    for k, v in ALT.items():
        for alt in v:
            need.setdefault(alt, set()).add('ALT')
    rows, fail = [], False
    print(f'{"take":14s} {"status":8s} {"sec":>7s} {"size":>10s} {"fps":>5s} {"pix_fmt":9s} {"range/tags":24s} {"MB":>6s}  used-by')
    for take in sorted(set(need) | set(films)):
        f = films.get(take)
        used = ','.join(sorted(need.get(take, {'-'})))
        if f and not f.get('error'):
            ok = (f['width'], f['height']) == (W, H) and f['fps'] is not None and abs(f['fps'] - FPS) < 1e-3
            status = 'ok' if ok else 'BAD'
            if not ok:
                fail = True
            tags = (f.get('color_tags') or '').replace('unknown', '?')
            rng = f"{f['range'] or '-'}" + (f" {tags}" if tags else '')      # tv|pc + 괄호 안 태그(색 공간 · 필드 순서)
            print(f"{take:14s} {status:8s} {f['duration'] or 0:7.2f} {f['width']}x{f['height']:<5} {f['fps'] or 0:5.1f} {str(f['pix_fmt']):9s} {rng[:24]:24s} {f['bytes'] / 1e6:6.1f}  {used}")
        else:
            status = 'MISSING' if take in REQUIRED else 'missing'
            if take in REQUIRED:
                fail = True
            print(f"{take:14s} {status:8s} {'':7s} {'':10s} {'':5s} {'':9s} {'':24s} {'':6s}  {used}")
        rows.append({'take': take, 'status': status, 'used_by': used, 'probe': f})
    for n in (30, 90):
        try:
            _, res = resolve(timeline(n), {k: v.get('duration') for k, v in films.items()})
            print(f"TL{n}: ALT {[(x['name'], x['alt']) for x in res['alt']]}  DROP {[x['name'] for x in res['dropped']]}  "
                  f"FREEZE {[(x['name'], x['frames']) for x in res['freeze']]}  play {res['play_seconds']}s")
        except AbortTimeline as e:
            print(f'TL{n}: ABORT {e}')
            fail = True
    write_json(WORK / 'check_inputs.json', {'films_dir': str(films_dir), 'rows': rows, 'fail': fail})
    return not fail


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('what', help='30 | 90 | inputs')
    ap.add_argument('--out', default=None)
    ap.add_argument('--deck', action='store_true')
    ap.add_argument('--preview', action='store_true', help='해상도 기준 960×540, 기본 출력 _work/preview_<N>.mp4')
    ap.add_argument('--fake', action='store_true')
    ap.add_argument('--no-sat', action='store_true', help='AC-5 채도 측정 생략')
    a = ap.parse_args(argv)
    if a.what == 'inputs':
        sys.exit(0 if inputs(a.fake) else 1)
    N = int(a.what)
    rows = check(N, a.out, a.deck, a.preview, a.fake, a.no_sat)
    print_rows(rows)
    ok = all(r['pass'] for r in rows)
    write_json(WORK / f'check_{N}.json', {'length': N, 'out': a.out, 'preview': a.preview, 'rows': rows, 'pass': ok})
    print('RESULT', 'PASS' if ok else 'FAIL', WORK / f'check_{N}.json')
    sys.exit(0 if ok else 1)


if __name__ == '__main__':
    main()
