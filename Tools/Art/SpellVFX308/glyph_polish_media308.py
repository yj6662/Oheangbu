#!/usr/bin/env python3
"""2026-10-27 per-glyph polish (showcase120_plan.md step 3): the gap sheets (concept | engine frames) and the before | after media of the polished glyphs.

  python Tools/Art/SpellVFX308/glyph_polish_media308.py sheet <round dir> [--only ma,mo] [--out <dir>] [--ages 0.4,0.6,...]
      one jpg per glyph: the chosen concept (left) and six engine frames of that round at the given ages (right) - for the gap list
  python Tools/Art/SpellVFX308/glyph_polish_media308.py media <before round dir> <after round dir> --only ma,mo [--still ma=0.8,mo=1.0]
      Art/SpellVFX308/Vol2Vfx/free_showcase/glyph_polish/
        <key>_before_after.mp4     before | after side by side (2 x 1280 x 720): the whole capture at 1x, then its first CUT seconds again at 0.5x
        <key>_concept_after.jpg    the chosen concept | the after frame at the given age
        glyph_polish_all.mp4       every polished glyph in one file (60 fps, crf 22)
        glyph_polish_all_small.mp4 the same at 1280 px wide, 30 fps, libx264 crf 30 (< 30 MB)
round dir = a Vol2VfxCapture out= folder (frames/<job>/f_*.jpg + index.csv). Frames are streamed to ffmpeg (imageio_ffmpeg's binary), never held in memory."""
import csv, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import imageio_ffmpeg

REPO = Path(__file__).resolve().parents[3]
FS = REPO / 'Art/SpellVFX308/Vol2Vfx/free_showcase'
OUT = FS / 'glyph_polish'
FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
FONT = 'C:/Windows/Fonts/malgunbd.ttf'
BODY = {'목': 'Wood', '화': 'Fire', '토': 'Earth', '금': 'Metal', '수': 'Water'}
# key, letter, template, body, concept (folder/file), cut (s replayed at 0.5x), label
G = []
def _add(rows, tpl, conc_dir, cut, verb):
    for key, letter, body, concept in rows:
        G.append((key, letter, tpl, BODY[body], conc_dir + '/' + concept, cut, '%s · %s · %s %s' % (letter, body, tpl, verb)))
_add([('na', '나', '화', 'na_b.png')], 'P0', 'concepts', 1.6, '경로')
_add([('go', '고', '목', 'go_b.png')], 'F0', 'concepts', 1.6, '면')
_add([('gak', '각', '목', 'gak_a.png')], 'P1', 'concepts', 1.8, '경로 · 남긴다')
_add([('ga', '가', '목', 'ga_a.png'), ('ma', '마', '토', 'ma_a.png'), ('sa', '사', '금', 'sa_b.png'), ('a', '아', '수', 'a_b.png')], 'P0', 'batch1/concepts', 1.6, '경로')
_add([('no', '노', '화', 'no_a.png'), ('mo', '모', '토', 'mo_b.png'), ('so', '소', '금', 'so_a.png'), ('o', '오', '수', 'o_a.png')], 'F0', 'batch1/concepts', 1.6, '면')
_add([('geo', '거', '목', 'geo_b.png'), ('neo', '너', '화', 'neo_a.png'), ('meo', '머', '토', 'meo_a.png'), ('seo', '서', '금', 'seo_a.png'), ('eo', '어', '수', 'eo_a.png')], 'A0', 'batch2/concepts', 1.4, '부착')
_add([('gu', '구', '목', 'gu_a.png'), ('nu', '누', '화', 'nu_a.png'), ('mu', '무', '토', 'mu_a.png'), ('su', '수', '금', 'su_a.png'), ('u', '우', '수', 'u_b.png')], 'R0', 'batch2/concepts', 1.6, '둘레')
_add([('nak', '낙', '화', 'nak_a.png'), ('mak', '막', '토', 'mak_a.png'), ('sak', '삭', '금', 'sak_a.png'), ('ak', '악', '수', 'ak_a.png')], 'P1', 'batch3/concepts', 1.8, '경로 · 남긴다')
_add([('geok', '걱', '목', 'geok_a.png'), ('neok', '넉', '화', 'neok_a.png'), ('meok', '먹', '토', 'meok_a.png'), ('seok', '석', '금', 'seok_a.png'), ('eok', '억', '수', 'eok_a.png')], 'A1', 'batch3/concepts', 1.8, '부착 · 남긴다')
_add([('gok', '곡', '목', 'gok_a.png'), ('nok', '녹', '화', 'nok_a.png'), ('mok', '목', '토', 'mok_b.png'), ('sok', '속', '금', 'sok_a.png'), ('ok', '옥', '수', 'ok_b.png')], 'F1', 'batch3/concepts', 1.8, '면 · 남긴다')
_add([('guk', '국', '목', 'guk_a.png'), ('nuk', '눅', '화', 'nuk_a.png'), ('muk', '묵', '토', 'muk_a.png'), ('suk', '숙', '금', 'suk_b.png'), ('uk', '욱', '수', 'uk_b.png')], 'R1', 'batch3/concepts', 1.8, '둘레 · 남긴다')
_add([('gan', '간', '목', 'gan_a.png'), ('nan', '난', '화', 'nan_a.png'), ('man', '만', '토', 'man_a.png'), ('san', '산', '금', 'san_b.png'), ('an', '안', '수', 'an_a.png')], 'P2', 'batch4/concepts', 2.2, '경로 · 번진다')
_add([('geon', '건', '목', 'geon_a.png'), ('neon', '넌', '화', 'neon_a.png'), ('meon', '먼', '토', 'meon_b.png'), ('seon', '선', '금', 'seon_b.png'), ('eon', '언', '수', 'eon_b.png')], 'A2', 'batch4/concepts', 2.2, '부착 · 번진다')
_add([('gon', '곤', '목', 'gon_a.png'), ('non', '논', '화', 'non_a.png'), ('mon', '몬', '토', 'mon_a.png'), ('son', '손', '금', 'son_b.png'), ('on', '온', '수', 'on_a.png')], 'F2', 'batch4/concepts', 2.2, '면 · 번진다')
_add([('gun', '군', '목', 'gun_a.png'), ('nun', '눈', '화', 'nun_a.png'), ('mun', '문', '토', 'mun_a.png'), ('sun', '순', '금', 'sun_a.png'), ('un', '운', '수', 'un_a.png')], 'R2', 'batch4/concepts', 2.2, '둘레 · 번진다')
BY = {g[0]: g for g in G}


def job_dir(rnd, tpl, body):
    hits = sorted(d for d in (rnd / 'frames').glob(tpl + '_*_' + body + '_red') if d.is_dir())
    return hits[-1] if hits else None


def frames(d):
    rows = []
    with open(d / 'index.csv', encoding='utf-8', errors='replace') as f:
        for r in csv.DictReader(f):
            if r.get('video', '1') != '1': continue
            p = d / ('f_%05d.jpg' % int(r['frame']))
            if p.exists(): rows.append((float(r['ageSec']), p, r.get('note', '') or ''))
    return rows


def at(rows, age):
    return min(rows, key=lambda r: abs(r[0] - age))


def sheet(rnd, only, out, ages):
    font = ImageFont.truetype(FONT, 22)
    out.mkdir(parents=True, exist_ok=True)
    for key, letter, tpl, body, conc, cut, label in G:
        if only and key not in only: continue
        d = job_dir(rnd, tpl, body)
        if d is None: print('NO JOB', key); continue
        rows = frames(d)
        if not rows: print('NO FRAMES', key); continue
        jump = next((i for i, r in enumerate(rows) if 'JUMP' in r[2]), None)
        sel = [at(rows, a) for a in ages]
        if jump is not None:      # the lasting part: just after the jump and near the end
            sel[-2] = rows[min(len(rows) - 1, jump + 20)]; sel[-1] = rows[max(jump, len(rows) - 25)]
        cw, ch = 960, 540
        tw, th = 480, 270
        im = Image.new('RGB', (cw + tw * 2, max(ch, th * 3) + 40), (20, 20, 20))
        cp = FS / conc
        if cp.exists():
            c = Image.open(cp).convert('RGB'); c.thumbnail((cw, ch)); im.paste(c, (0, 40))
        dr = ImageDraw.Draw(im)
        dr.text((8, 6), key + '  ' + label + '   concept ' + conc.split('/')[-1], font=font, fill=(255, 230, 160))
        for i, (age, p, note) in enumerate(sel):
            f = Image.open(p).convert('RGB').resize((tw, th))
            x, y = cw + (i % 2) * tw, 40 + (i // 2) * th
            im.paste(f, (x, y))
            dr.text((x + 6, y + 4), 't=%.2f%s' % (age, ' J' if jump is not None and rows.index((age, p, note)) > jump else ''), font=font, fill=(255, 255, 120))
        im.save(out / ('sheet_%s.jpg' % key), quality=85)
        print('sheet', key, d.name, len(rows), 'frames', 'jump' if jump is not None else '')


def pair(before, after, only, out, ages):
    """concept (left) | before row (top) / after row (bottom) at the same ages - the polish round check."""
    font = ImageFont.truetype(FONT, 22)
    out.mkdir(parents=True, exist_ok=True)
    for key, letter, tpl, body, conc, cut, label in G:
        if only and key not in only: continue
        bd, ad = job_dir(before, tpl, body), job_dir(after, tpl, body)
        if bd is None or ad is None: continue
        fb, fa = frames(bd), frames(ad)
        tw, th = 480, 270
        n = len(ages)
        im = Image.new('RGB', (640 + tw * n, 40 + th * 2), (20, 20, 20))
        cp = FS / conc
        if cp.exists():
            c = Image.open(cp).convert('RGB'); c.thumbnail((640, th * 2)); im.paste(c, (0, 40))
        dr = ImageDraw.Draw(im)
        dr.text((8, 6), key + '  ' + label + '   (top: before, bottom: after)', font=font, fill=(255, 230, 160))
        for i, a in enumerate(ages):
            for row, rows in enumerate((fb, fa)):
                age, p, note = at(rows, a)
                f = Image.open(p).convert('RGB').resize((tw, th))
                im.paste(f, (640 + i * tw, 40 + row * th))
                dr.text((640 + i * tw + 6, 44 + row * th), ('B ' if row == 0 else 'A ') + 't=%.2f' % age, font=font, fill=(255, 255, 120))
        im.save(out / ('pair_%s.jpg' % key), quality=85)
        print('pair', key)


class Enc:
    def __init__(self, path, w, h, fps, crf, scale_w=None):
        vf = ['-vf', 'scale=%d:-2:flags=lanczos' % scale_w] if scale_w else []
        self.p = subprocess.Popen([FFMPEG, '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', '%dx%d' % (w, h), '-r', str(fps), '-i', '-',
                                   *vf, '-c:v', 'libx264', '-preset', 'medium', '-crf', str(crf), '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(path)],
                                  stdin=subprocess.PIPE)
    def put(self, im): self.p.stdin.write(im.tobytes())
    def close(self): self.p.stdin.close(); self.p.wait()


def compose(b, a, label, sub, font, small):
    W, H = b.size
    im = Image.new('RGB', (W * 2, H))
    im.paste(b, (0, 0)); im.paste(a.resize((W, H)), (W, 0))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, W * 2, 44], fill=(0, 0, 0))
    d.text((16, 6), '전 (before)', font=font, fill=(235, 235, 235)); d.text((W + 16, 6), '후 (after · 글자별 폴리싱)', font=font, fill=(255, 220, 150))
    d.text((W - 420, 6), label, font=font, fill=(255, 255, 255))
    if sub: d.text((16, H - 34), sub, font=small, fill=(255, 255, 160))
    d.line([(W, 0), (W, H)], fill=(0, 0, 0), width=4)
    return im


def media(before, after, only, stills):
    font = ImageFont.truetype(FONT, 26); small = ImageFont.truetype(FONT, 20)
    OUT.mkdir(parents=True, exist_ok=True)
    allfull = allsmall = None
    for key, letter, tpl, body, conc, cut, label in G:
        if only and key not in only: continue
        bd, ad = job_dir(before, tpl, body), job_dir(after, tpl, body)
        if bd is None or ad is None: print('skip', key, 'before', bd, 'after', ad); continue
        fb, fa = frames(bd), frames(ad)
        n = min(len(fb), len(fa))
        if n < 2: print('skip', key, 'frames', len(fb), len(fa)); continue
        W, H = Image.open(fb[0][1]).size
        if allfull is None:
            allfull = Enc(OUT / 'glyph_polish_all.mp4', W * 2, H, 60, 22)
            allsmall = Enc(OUT / 'glyph_polish_all_small.mp4', W * 2, H, 30, 30, 1280)
        one = Enc(OUT / (key + '_before_after.mp4'), W * 2, H, 60, 21)
        jumped = False; seq = []
        for k in range(n):
            if 'JUMP' in fb[k][2] or 'JUMP' in fa[k][2]: jumped = True
            seq.append((k, '1x' + (' · JUMP 뒤 (실험실이 긴 대기를 건너뜀)' if jumped else '')))
        slow = [(k, '0.5x') for k in range(n) if fb[k][0] <= cut and not ('JUMP' in fb[k][2])]
        for idx, (k, tag) in enumerate(seq + [s for s in slow for _ in (0, 1)]):
            im = compose(Image.open(fb[k][1]).convert('RGB'), Image.open(fa[k][1]).convert('RGB'), label, tag + '  t=%.2fs' % fb[k][0], font, small)
            one.put(im); allfull.put(im)
            if idx % 2 == 0: allsmall.put(im)
        one.close()
        # concept | after still
        age = stills.get(key, 1.0)
        a = Image.open(at(fa, age)[1]).convert('RGB')
        cp = FS / conc
        c = Image.open(cp).convert('RGB') if cp.exists() else Image.new('RGB', a.size)
        c = c.resize((int(c.width * a.height / c.height), a.height))
        im = Image.new('RGB', (c.width + a.width, a.height)); im.paste(c, (0, 0)); im.paste(a, (c.width, 0))
        d = ImageDraw.Draw(im)
        d.rectangle([0, 0, im.width, 44], fill=(0, 0, 0))
        d.text((16, 6), '시안 ' + conc.split('/')[-1], font=font, fill=(235, 235, 235)); d.text((c.width + 16, 6), '엔진 (폴리싱 후) t=%.2fs · %s' % (at(fa, age)[0], label), font=font, fill=(255, 220, 150))
        im.save(OUT / (key + '_concept_after.jpg'), quality=90)
        print('wrote', key, n, 'frames (+', len(slow) * 2, 'slow)')
    if allfull: allfull.close(); allsmall.close()
    for f in ('glyph_polish_all.mp4', 'glyph_polish_all_small.mp4'):
        p = OUT / f
        if p.exists(): print(f, round(p.stat().st_size / 1e6, 1), 'MB')


def main():
    a = sys.argv[1:]
    if not a: print(__doc__); return
    only = a[a.index('--only') + 1].split(',') if '--only' in a else None
    if a[0] == 'sheet':
        out = Path(a[a.index('--out') + 1]) if '--out' in a else OUT / 'sheets'
        ages = [float(x) for x in a[a.index('--ages') + 1].split(',')] if '--ages' in a else [0.35, 0.55, 0.8, 1.1, 1.5, 2.1]
        sheet(Path(a[1]), only, out, ages)
    elif a[0] == 'pair':
        out = Path(a[a.index('--out') + 1]) if '--out' in a else OUT / 'pairs'
        ages = [float(x) for x in a[a.index('--ages') + 1].split(',')] if '--ages' in a else [0.5, 0.8, 1.2]
        pair(Path(a[1]), Path(a[2]), only, out, ages)
    elif a[0] == 'media':
        stills = {}
        if '--still' in a:
            for kv in a[a.index('--still') + 1].split(','):
                k, v = kv.split('='); stills[k] = float(v)
        media(Path(a[1]), Path(a[2]), only, stills)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    main()
