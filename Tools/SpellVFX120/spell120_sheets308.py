#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""spell120_sheets308.py - contact sheets, index.html and a ranked defect-list template for the #308 spell VFX sweep.

Input : folders written by the editor tool Spell120Sweep308 under Art/SpellVFX120/Spell120_308/Sweep/<folder>/
        (sweep.json + <id>_<beat>_<backdrop>.png), plus static308.json and coverage.json of the same notes folder.
Output: <folder>/sheets/sheet_<element>_<backdrop>.jpg  one sheet per element: 6 finals (columns) x 4 vowels (rows),
                                                         three beats stacked in every cell
        Sweep/index.html                                 all sweep folders, sheets and per-glyph images
        Sweep/DEFECTS308_TEMPLATE.md                     ranked candidate list in the shape of
                                                         Docs/Art/SpellVFX120/FINAL_ART_REVIEW_CRITERIA.md

  python Tools/SpellVFX120/spell120_sheets308.py                      # every sweep folder found
  python Tools/SpellVFX120/spell120_sheets308.py --sweep catalog_game_review
  python Tools/SpellVFX120/spell120_sheets308.py --template-only      # before any capture exists

Read-only for the project: it writes only under the Sweep folder (or --root). Automatic signals are facts about files
and pixels; every judgement column of the template is left for the reviewer.

A folder is read as the union of its sweep.json and its per-glyph records (<id>.json): a later run over a few glyphs
rewrites sweep.json with only those rows, and an interrupted run leaves it short, but the records of the other glyphs
are still on disk. Cells that come from a record instead of the report are marked, and a record made with other capture
settings than the report is marked as such. A report that cannot be read (the editor is writing it), a missing image
and a damaged image are drawn as what they are; none of them stops the run.
"""
import argparse
import datetime
import html
import json
import os
import re
import sys

BS = chr(92)
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..')).replace(BS, '/')
NOTES = REPO + '/Art/SpellVFX120/Spell120_308'
SWEEP_ROOT = NOTES + '/Sweep'
ELEMENTS = [('목', 'wood', 0), ('화', 'fire', 2), ('토', 'earth', 6), ('금', 'metal', 9), ('수', 'water', 11)]  # initial index
VOWELS = [(0, 'ㅏ'), (4, 'ㅓ'), (8, 'ㅗ'), (13, 'ㅜ')]
FINALS = [(0, '무'), (1, 'ㄱ'), (4, 'ㄴ'), (16, 'ㅁ'), (19, 'ㅅ'), (21, 'ㅇ')]
BEATS = [('1cast', '시전'), ('2impact', '착탄'), ('3end', '소멸 직전')]
BACKDROPS = [('dark', '어두운 배경'), ('light', '밝은 배경')]
FONT_CANDIDATES = ['C:/Windows/Fonts/malgunbd.ttf', 'C:/Windows/Fonts/malgun.ttf', 'C:/Windows/Fonts/gulim.ttc']
# Docs/Art/SpellVFX120/FINAL_ART_REVIEW_CRITERIA.md, "우선 비교할 대상"
PRIORITY_GROUPS = [
    (1, '오온옷옹옥', '같은 물마루 띠인지, 정화·냉각선·가라앉힘이 실제로 다른지'),
    (1, '것넛멋섯엇', '검 다섯이 실제 크기에서 색 말고도 구별되는지'),
    (1, '곰놈솜옴몸', '소환 다섯의 주실루엣이 장식이 아니라 몸에서 갈리는지'),
    (1, '넉넌넘', '시전자 불씨 고리 / 팔 충전 매듭 / 가슴·어깨 봉인 세 점의 위치'),
    (2, '사삭삿상', '단발 / 관통 / 방벽 무시 / 적 탄 동결의 구별'),
    (2, '소속솟송손', '속사·도탄·벼림·요격·가열의 구별'),
    (2, '아악안앙', '유도 / 재타격 / 분열 / 복귀의 종점과 방향'),
    (2, '억엄언먹석선섬', '비축·응축·속도·심안·감쇠가 링 개수 차이로만 끝나는지'),
    (2, '농몽', '오르는 김과 가라앉는 흙탕의 구별'),
    (2, '감남맘삼암간검녹앗', '부착 유지→격발, 전이, 성장→해체, 회복존, 얼림→깨짐의 전후'),
    (2, '눈숫웅국뭄', '소각·절단·정화는 대상 전후 비교가 필요, 지지 구조·다리는 자산으로 먼저 판단'),
    (3, '곡곤공국', '09-09 네 번째 패스에서 확인한 구조가 회귀하지 않았는지'),
]


def rel(path):
    path = path.replace(BS, '/')
    return path[len(REPO) + 1:] if path.startswith(REPO + '/') else path


def decompose(glyph):
    """(initial, medial, final) of one Hangul syllable; (-1, -1, -1) for anything else."""
    if not isinstance(glyph, str) or len(glyph) != 1 or not 0xAC00 <= ord(glyph) <= 0xD7A3:
        return -1, -1, -1
    code = ord(glyph) - 0xAC00
    return code // 588, (code % 588) // 28, code % 28


def load_json(path):
    """Parsed JSON, or None when the file is missing, empty or not (yet) complete JSON."""
    if not os.path.isfile(path):
        return None
    try:
        with open(path, 'r', encoding='utf-8-sig') as fh:
            return json.load(fh)
    except (OSError, ValueError):
        return None


RECORD_NAME = re.compile(r'^[0-9]{3}_[0-9A-F]{4}[.]json$')


def collect_rows(folder, report):
    """Rows of one sweep folder: the report's rows, then per-glyph records the report does not list.

    Every row gets '_source' ('report' or 'record') and '_stale' (record written with another settings key than the
    report). Returns (rows in catalogue order, number of unreadable records).
    """
    rows, seen, unreadable = [], set(), 0
    for row in (report or {}).get('rows') or []:
        if not isinstance(row, dict) or not row.get('glyph'):
            continue
        row = dict(row, _source='report', _stale=False)
        rows.append(row)
        seen.add(row['glyph'])
    key = (report or {}).get('settingsKey')
    try:
        names = sorted(os.listdir(folder))
    except OSError:
        names = []
    for name in names:
        if not RECORD_NAME.match(name):
            continue
        record = load_json(folder + '/' + name)
        if not isinstance(record, dict) or not record.get('glyph') or not isinstance(record.get('beats', []), list):
            unreadable += 1
            continue
        if record['glyph'] in seen:
            continue
        seen.add(record['glyph'])
        rows.append(dict(record, _source='record', _stale=bool(key) and record.get('settingsKey') != key))
    rows.sort(key=lambda r: (r.get('index') or 0, r.get('glyph')))
    return rows, unreadable


def glyph_table():
    """glyph -> merged facts from static308.json and coverage.json (either may be missing)."""
    table = {}
    static = load_json(NOTES + '/static308.json') or {}
    coverage = load_json(NOTES + '/coverage.json') or {}
    for g in coverage.get('glyphs', []):
        table[g['glyph']] = {'glyph': g['glyph'], 'index': g.get('index'), 'category': g.get('category'),
                             'effect': g.get('csvEffect'), 'implKo': (g.get('impl') or {}).get('statusKo'),
                             'reachableKo': (g.get('impl') or {}).get('reachableKo'),
                             'vfxKo': (g.get('vfx') or {}).get('stateKo')}
    lookalike = {}
    for group in static.get('lookAlikeGroups', []):
        replaced = ''.join(group.get('replacedInGame') or [])
        for g in group['glyphs']:
            lookalike[g] = ''.join(group['glyphs']) + ('(게임에서는 %s가 다른 프리팹)' % replaced if replaced else '')
    for g in static.get('glyphs', []):
        row = table.setdefault(g['glyph'], {'glyph': g['glyph']})
        row.update(index=g.get('index'), id=g.get('id'), element=g.get('element'), category=g.get('category'),
                   bodyState=g.get('bodyState'), castResolves=(g.get('live') or {}).get('castResolvesInGame'),
                   livePlays=(g.get('live') or {}).get('plays'), sameAsCatalog=(g.get('live') or {}).get('sameAsCatalog'),
                   catalogPrefabPlayed=(g.get('live') or {}).get('catalogPrefabPlayed'),
                   reachable=(g.get('live') or {}).get('reachableInGame'),
                   effect=row.get('effect') or g.get('csvEffect'), lookalike=lookalike.get(g['glyph']))
        variants = g.get('variants') or {}
        # 'live' = what the game plays (the template ranks by it); 'catalog' = the catalogue prefab. A sheet of the
        # catalogue or original set must show the catalogue prefab's own signals, not the Bolt300 replacement's.
        # the game does not spawn the catalogue prefab for an externally owned summon (or the bridge): what it shows
        # then is the runtime-data variant, or nothing of the catalogue at all
        played = variants.get('live') or variants.get('catalog') or {}
        if (g.get('live') or {}).get('castResolvesInGame') and (g.get('live') or {}).get('catalogPrefabPlayed') is False:
            played = variants.get('runtimeData') or {}
        for name, shown in (('live', played), ('catalog', variants.get('catalog') or {})):
            emission = shown.get('emission') or {}
            row[name] = dict(sustainedEmission=bool(emission.get('sustainedCandidate')),
                             flightEmission=bool(emission.get('additiveFlight') or emission.get('emissiveFlight')),
                             staticTris=(shown.get('triangles') or {}).get('estimate'),
                             overBudget=(shown.get('triangles') or {}).get('overBudget'),
                             lifeSeconds=shown.get('lifeSeconds'), dispatch=shown.get('dispatch'),
                             fogLayer=(shown.get('layers') or {}).get('usesVfxAfterFog'))
        row.update(row['live'])
    return table, bool(static), bool(coverage)


def variant_facts(facts, set_name):
    """Static signals of the prefab a sweep of `set_name` actually drew."""
    return facts.get('catalog' if set_name in ('catalog', 'original') else 'live') or {}


def find_sweeps(only):
    found = []
    if not os.path.isdir(SWEEP_ROOT):
        return found
    for name in sorted(os.listdir(SWEEP_ROOT)):
        folder = SWEEP_ROOT + '/' + name
        if os.path.isfile(folder + '/sweep.json') and (not only or name == only):
            found.append(folder)
    return found


# ----------------------------------------------------------------------------------------------- sheets
def open_thumbnail(path, size, expected):
    """(image or None, note). The note names what is wrong with the file; a wrong file never stops the sheet."""
    from PIL import Image
    if not os.path.isfile(path):
        return None, '이미지 없음'
    try:
        with Image.open(path) as im:
            original = im.size
            im.load()
            thumb = im.convert('RGB').resize(size, Image.LANCZOS)
    except Exception as error:   # truncated, empty or foreign file
        return None, '이미지 손상(%s)' % type(error).__name__
    note = ''
    if expected and tuple(original) != tuple(expected):
        note = '크기 다름 %dx%d' % original
    return thumb, note


def build_sheets(folder, table, thumb, quality, report, all_rows):
    from PIL import Image, ImageDraw, ImageFont
    rows = {r['glyph']: r for r in all_rows}
    set_name = report.get('set')
    expected = (report.get('width', 1280), report.get('height', 720))
    from_records = sum(1 for r in all_rows if r.get('_source') == 'record')
    stale_records = sum(1 for r in all_rows if r.get('_stale'))
    font_path = next((f for f in FONT_CANDIDATES if os.path.isfile(f)), None)

    def font(size):
        return ImageFont.truetype(font_path, size) if font_path else ImageFont.load_default()

    f_title, f_glyph, f_small = font(26), font(24), font(13)
    tw, th = thumb, int(round(thumb * report.get('height', 720) / float(report.get('width', 1280))))
    label_h, gap, left, top = 46, 6, 44, 110   # title, legend, provenance line, column labels
    cell_w, cell_h = tw, label_h + 3 * th
    out_dir = folder + '/sheets'
    os.makedirs(out_dir, exist_ok=True)
    written = []
    for element, slug, initial in ELEMENTS:
        cells = {}
        for glyph, row in rows.items():
            i, m, f = decompose(glyph)
            if i == initial:
                cells[(m, f)] = row
        if not cells:
            continue
        for backdrop, backdrop_ko in BACKDROPS:
            dark = backdrop == 'dark'
            bg = (24, 24, 24) if dark else (236, 236, 236)
            ink = (232, 232, 232) if dark else (24, 24, 24)
            faint = (150, 150, 150) if dark else (110, 110, 110)
            sheet = Image.new('RGB', (left + 6 * (cell_w + gap), top + 4 * (cell_h + gap)), bg)
            draw = ImageDraw.Draw(sheet)
            env = report.get('environment', {})
            draw.text((10, 8), '%s · %s · %s' % (element, os.path.basename(folder), backdrop_ko), font=f_title, fill=ink)
            draw.text((10, 40), '열 = 종성(무 ㄱ ㄴ ㅁ ㅅ ㅇ) · 행 = 중성(ㅏ ㅓ ㅗ ㅜ) · 칸 안 위에서 아래로 시전 / 착탄 / 소멸 직전 · %s · %s · 미술 판정 아님'
                      % (env.get('rendererName', '?'), report.get('status', '?')), font=f_small, fill=faint)
            if from_records:
                draw.text((10, 60), '마지막 보고서(sweep.json)에 없는 %d칸은 글자별 기록에서 가져왔다(◇ 표시)%s'
                          % (from_records, ' · 그중 %d칸은 다른 촬영 설정의 기록(※ 표시)' % stale_records if stale_records else ''),
                          font=f_small, fill=(220, 150, 40))
            for r, (medial, vowel_name) in enumerate(VOWELS):
                y = top + r * (cell_h + gap)
                draw.text((12, y + label_h + th), vowel_name, font=f_glyph, fill=ink)
                for c, (final, final_name) in enumerate(FINALS):
                    x = left + c * (cell_w + gap)
                    row = cells.get((medial, final))
                    if r == 0:
                        draw.text((x + cell_w // 2 - 8, top - 22), '종성 ' + final_name, font=f_small, fill=ink)
                    if row is None:
                        draw.rectangle([x, y, x + cell_w - 1, y + cell_h - 1], outline=faint)
                        draw.text((x + 8, y + 8), '이 폴더에 기록 없음(찍지 않았거나 기록을 읽을 수 없음)', font=f_small, fill=faint)
                        continue
                    facts = table.get(row['glyph'], {})
                    bar = bar_colour(facts, row, dark)
                    draw.rectangle([x, y, x + cell_w - 1, y + label_h - 1], fill=bar)
                    draw.text((x + 6, y + 2), row['glyph'], font=f_glyph, fill=(255, 255, 255))
                    mark = ' ※다른 설정' if row.get('_stale') else ' ◇기록' if row.get('_source') == 'record' else ''
                    line1 = '%s · %s · %s%s' % (row.get('id', ''), facts.get('category') or '', facts.get('implKo') or '', mark)
                    draw.text((x + 40, y + 4), line1, font=f_small, fill=(255, 255, 255))
                    draw.text((x + 40, y + 24), cell_flags(facts, row, set_name), font=f_small, fill=(255, 255, 255))
                    for b, (beat_key, beat_ko) in enumerate(BEATS):
                        ty = y + label_h + b * th
                        beat = next((bt for bt in row.get('beats') or [] if bt.get('name') == beat_key), None)
                        metrics = (beat or {}).get(backdrop) or {}
                        # only a file name inside this folder: a record is data, not a path to follow
                        image_name = os.path.basename(str(metrics.get('image') or '%s_%s_%s.png' % (row.get('id'), beat_key, backdrop)))
                        picture, note = open_thumbnail(folder + '/' + image_name, (tw, th), expected)
                        if picture is not None:
                            sheet.paste(picture, (x, ty))
                        else:
                            draw.rectangle([x, ty, x + tw - 1, ty + th - 1], outline=(220, 60, 50), width=2)
                            draw.text((x + 8, ty + 24), note, font=f_small, fill=(220, 60, 50))
                        tag = '%s %.2fs' % (beat_ko, (beat or {}).get('seconds') or 0)
                        if beat is None:
                            tag = '%s · 기록 없음' % beat_ko
                        elif metrics.get('changedPixels', 1) == 0:
                            tag += ' · 화소 없음'
                            draw.rectangle([x, ty, x + tw - 1, ty + th - 1], outline=(220, 60, 50), width=3)
                        if picture is not None and note:
                            tag += ' · ' + note
                        draw.text((x + 5, ty + 3), tag, font=f_small, fill=(255, 255, 255) if dark else (0, 0, 0))
            name = 'sheet_%s_%s.jpg' % (slug, backdrop)
            sheet.save(out_dir + '/' + name, 'JPEG', quality=quality)
            written.append(name)
    return written


def bar_colour(facts, row, dark):
    if row.get('status') != 'CAPTURED':
        return (150, 40, 40)
    if row.get('_stale'):
        return (150, 100, 20)
    if facts.get('category') == '공백':
        return (95, 95, 95)
    if facts.get('castResolves'):
        return (30, 92, 70)
    return (70, 78, 104)


def cell_flags(facts, row, set_name=None):
    flags = []
    if facts.get('castResolves'):
        flags.append('게임 재생')
    if facts.get('bodyState') == 'shared':
        flags.append('공용 몸체')
    if variant_facts(facts, set_name).get('sustainedEmission'):
        flags.append('지속 가산·발광 후보')
    if row.get('maximumTriangles'):
        flags.append('%s tris' % format(row['maximumTriangles'], ','))
    if row.get('status') != 'CAPTURED':
        flags.append(row.get('status', ''))
    return ' · '.join(flags) if flags else (facts.get('vfxKo') or '')


# ----------------------------------------------------------------------------------------------- html
def build_index(sweeps, table):
    parts = ['<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>술식 120 VFX 스윕 #308</title>',
             '<style>body{font-family:"Malgun Gothic",sans-serif;margin:20px;background:#1c1c1c;color:#e6e6e6}'
             'a{color:#9cc8ff}h2{margin-top:36px}table{border-collapse:collapse;font-size:13px}'
             'td,th{border:1px solid #444;padding:4px 7px;vertical-align:top}th{background:#2a2a2a}'
             '.sheets img{width:300px;margin:4px;border:1px solid #444}.beats img{width:150px;margin:1px}'
             '.none{color:#ff8a80}.note{color:#aaa;max-width:1100px}</style></head><body>',
             '<h1>술식 120 VFX 스윕 #308</h1>',
             '<p class="note">자동 촬영은 「찍혔다」만 보여 준다. 실루엣·가독·미술 판정은 사용자가 한다. '
             '검수용 표적과 예시 이벤트는 연출용이며 게임 규칙 연결을 증명하지 않는다. 생성 %s.</p>'
             % datetime.datetime.now().strftime('%Y-%m-%d %H:%M'),
             '<p><a href="DEFECTS308_TEMPLATE.md">결함 목록 틀(DEFECTS308_TEMPLATE.md)</a> · '
             '<a href="../STATIC308.md">정적 조사(STATIC308.md)</a> · <a href="../COVERAGE.md">구현 현황(COVERAGE.md)</a></p>']
    if not sweeps:
        parts.append('<p class="none">아직 스윕 폴더가 없다. 편집기에서 Sweep308Trial → Sweep308을 돌린 뒤 다시 실행한다.</p>')
    for folder, sheets, report, rows in sweeps:
        name = os.path.basename(folder)
        parts.append('<h2 id="%s">%s</h2>' % (html.escape(name), html.escape(name)))
        if report is None:
            parts.append('<p class="none">sweep.json을 읽을 수 없다(편집기가 쓰는 중이거나 잘린 파일). 글자별 기록 %d칸만 아래에 보인다. '
                         '스윕이 끝난 뒤 이 도구를 다시 돌린다.</p>' % len(rows))
            report = {}
        env = report.get('environment') or {}
        from_records = sum(1 for r in rows if r.get('_source') == 'record')
        stale = sum(1 for r in rows if r.get('_stale'))
        if from_records:
            parts.append('<p class="none">마지막 보고서(sweep.json)에 없는 %d칸을 글자별 기록에서 가져왔다%s. '
                         '보고서 줄의 수치는 마지막 실행만의 값이다.</p>'
                         % (from_records, ', 그중 %d칸은 다른 촬영 설정의 기록' % stale if stale else ''))
        parts.append('<p class="note">상태 %s · 요청 %s · 촬영 %s · 재사용 %s · 예외 %s · 화소 없는 박자 %s · 한 박자도 안 보인 글자 %s<br>'
                     '카메라 %s/%s · 렌더러 %s(%s) · 품질 %s · 렌더 스케일 %s · 조명 %s<br>물리 바닥 %s<br>격리하지 못한 것: %s</p>'
                     % tuple(html.escape(str(x)) for x in (
                         report.get('status'), report.get('requested'), report.get('captured'), report.get('reused'),
                         report.get('exceptions'), report.get('beatsWithoutPixels'), report.get('glyphsWithoutAnyPixels'),
                         env.get('cameraMode'), env.get('view'), env.get('rendererName'), env.get('rendererFeatures') or '기능 없음',
                         env.get('qualityLevel'), env.get('pipelineRenderScale'), env.get('lightingOverride'),
                         env.get('physicsFloor'), env.get('notIsolated'))))
        parts.append('<div class="sheets">' + ''.join(
            '<a href="%s/sheets/%s"><img loading="lazy" src="%s/sheets/%s" alt="%s"></a>' % (name, s, name, s, s) for s in sheets) + '</div>')
        parts.append('<details><summary>글자별 박자 이미지 (%d자)</summary><table><tr><th>글자</th><th>분류·구현</th><th>수명</th>'
                     '<th>최대 tris·입자</th><th>어두운 배경: 시전 / 착탄 / 소멸 직전</th><th>밝은 배경</th><th>메모</th></tr>' % len(rows))
        for row in rows:
            facts = table.get(row['glyph'], {})
            cells = []
            for backdrop, _ in BACKDROPS:
                imgs = []
                for beat in row.get('beats') or []:
                    metrics = beat.get(backdrop) or {}
                    if metrics.get('image'):
                        image_name = html.escape(os.path.basename(str(metrics['image'])), quote=True)
                        imgs.append('<a href="%s/%s"><img loading="lazy" src="%s/%s" title="%s %.2fs · 바뀐 화소 %s"></a>'
                                    % (html.escape(name, quote=True), image_name, html.escape(name, quote=True), image_name,
                                       html.escape(str(beat.get('name'))), beat.get('seconds') or 0,
                                       html.escape(str(metrics.get('changedPixels')))))
                cells.append('<td class="beats">%s</td>' % ''.join(imgs))
            notes = []
            if row.get('status') != 'CAPTURED':
                notes.append('<span class="none">%s</span>' % html.escape(str(row.get('status', ''))))
            if row.get('_stale'):
                notes.append('<span class="none">다른 촬영 설정의 기록</span>')
            elif row.get('_source') == 'record':
                notes.append('글자별 기록에서 가져옴')
            if row.get('deploy308Active'):
                notes.append('전개 레이어(Deploy308) 그려짐')
            if row.get('unattributedHiddenRoots'):
                notes.append('<span class="none">열린 씬에 새 숨김 객체: %s</span>' % html.escape(str(row['unattributedHiddenRoots'])))
            for beat in row.get('beats') or []:
                if beat.get('pixelStatus') == 'NOT_VISIBLE_AT_THIS_BEAT':
                    notes.append('<span class="none">%s 화소 없음</span>' % html.escape(beat.get('name', '')))
            if row.get('leakedRootsAdopted'):
                notes.append('루트 누출 %d(회수)' % row['leakedRootsAdopted'])
            if row.get('openSceneCameraTouched'):
                notes.append('열린 씬 카메라 건드림(복구 %s)' % row.get('openSceneCameraRestored'))
            parts.append('<tr><td><b>%s</b> %s</td><td>%s<br>%s · %s</td><td>%.2fs</td><td>%s · %s</td>%s<td>%s</td></tr>' % (
                html.escape(row['glyph']), html.escape(str(row.get('id', ''))), html.escape(facts.get('category') or ''),
                html.escape(facts.get('implKo') or ''), html.escape(str(row.get('dispatch') or '')), row.get('life') or 0,
                format(row.get('maximumTriangles') or 0, ','), format(row.get('maximumParticles') or 0, ','), ''.join(cells),
                '<br>'.join(notes)))
        parts.append('</table></details>')
    parts.append('</body></html>')
    return '\n'.join(parts)


# ----------------------------------------------------------------------------------------------- defect template
def build_template(sweeps, table, has_static, has_coverage):
    best = {}   # glyph -> (folder name, row): prefer a sweep of what the game plays, then the catalogue
    order = {'live': 0, 'bolt300': 1, 'catalog': 2, 'original': 3}
    for folder, _, report, rows in sorted(sweeps, key=lambda s: order.get((s[2] or {}).get('set'), 9)):
        if os.path.basename(folder).startswith('trial_') or report is None:
            continue
        for row in rows:
            if row.get('_stale'):
                continue   # captured with other settings than this folder's report: not evidence for it
            best.setdefault(row['glyph'], (os.path.basename(folder), row, report))
    rows = []
    for glyph, facts in table.items():
        if facts.get('category') == '공백':
            continue
        score, signals = 0, []
        if facts.get('castResolves'):
            score += 100
            if facts.get('catalogPrefabPlayed') is False:
                signals.append('시전은 풀리지만 카탈로그 프리팹은 재생되지 않음(' + ('소환 전투 본체' if 'summon' in str(facts.get('livePlays')) else '자체 메시') + ')')
            else:
                signals.append('게임에서 재생됨')
            if facts.get('reachable') == 'yes_after_cheongryong_unlock':
                signals.append('청룡 처치 뒤에 도달')
            elif facts.get('reachable') and not str(facts.get('reachable')).startswith('yes'):
                signals.append('메인에서 도달 불가')
        swept = best.get(glyph)
        where = '스윕 전'
        if swept:
            folder, row, report = swept
            where = '%s/%s_*' % (folder, row.get('id', ''))
            if row.get('status') != 'CAPTURED':
                score += 50
                signals.append('촬영 실패(%s)' % row.get('status'))
            beats = row.get('beats') or []
            dead = [str(b.get('name')) for b in beats if b.get('pixelStatus') == 'NOT_VISIBLE_AT_THIS_BEAT']
            if dead:
                score += 40 * len(dead)
                signals.append('화소 없는 박자: ' + ', '.join(dead))
            pixels = max(1, (report.get('width') or 1280) * (report.get('height') or 720))
            for b in beats:
                d = b.get('dark') or {}
                if (d.get('saturatedPixels') or 0) > pixels * .005:
                    score += 15
                    signals.append('%s 어두운 배경 백색 포화 %.1f%%' % (b.get('name'), 100.0 * d['saturatedPixels'] / pixels))
                if b.get('name') == '2impact' and (d.get('frameEdgePixels') or 0) > 0:
                    score += 10
                    signals.append('착탄 박자가 화면 가장자리에 닿음')
                if (b.get('lights') or 0) > 0:
                    score += 30
                    signals.append('%s 활성 Light %d' % (b.get('name'), b['lights']))
            if (row.get('maximumTriangles') or 0) > 17000:
                score += 10
                signals.append('그려진 삼각형 %s' % format(row['maximumTriangles'], ','))
            # a record without its files is not evidence: name the files that are missing or empty
            absent = []
            for b in beats:
                for backdrop in ('light', 'dark'):
                    image_name = os.path.basename(str((b.get(backdrop) or {}).get('image') or ''))
                    path = SWEEP_ROOT + '/' + folder + '/' + image_name
                    if not image_name or not os.path.isfile(path) or os.path.getsize(path) == 0:
                        absent.append('%s_%s' % (b.get('name'), backdrop))
            if row.get('status') == 'CAPTURED' and (absent or len(beats) != 3):
                score += 50
                signals.append('이미지 파일 없음·빈 파일: ' + (', '.join(absent) if absent else '박자 기록 %d개' % len(beats)))
            if row.get('deploy308Active'):
                signals.append('전개 레이어(Deploy308)가 그려진 촬영')
        if facts.get('sustainedEmission'):
            score += 30
            signals.append('지속 역할 가산·발광 재질(정적)')
        if facts.get('flightEmission'):
            score += 10
            signals.append('비행체 가산 재질(정적)')
        if facts.get('bodyState') == 'shared':
            score += 25
            signals.append('공용 몸체')
        if facts.get('lookalike'):
            score += 20
            signals.append('계열·동작·배치·메시가 같은 묶음: ' + facts['lookalike'])
        if facts.get('overBudget'):
            score += 10
            signals.append('정적 추정 %s tris' % format(facts.get('staticTris') or 0, ','))
        rows.append((score, facts.get('index') or 0, glyph, facts, where, signals))
    rows.sort(key=lambda r: (-r[0], r[1]))
    out = []
    w = out.append
    w('# 술식 120 VFX 결함 목록 틀 (DEFECTS308)')
    w('')
    w('생성 %s · 도구 `Tools/SpellVFX120/spell120_sheets308.py` · 기준 `Docs/Art/SpellVFX120/FINAL_ART_REVIEW_CRITERIA.md`'
      % datetime.datetime.now().strftime('%Y-%m-%d %H:%M'))
    w('')
    w('이 문서는 **채워 넣을 틀**이다. 「자동 신호」 칸만 도구가 적었고(파일·화소·정적 조사의 사실), 나머지 칸은 접촉 시트와 영상을 본 사람이 적는다. '
      '기준 문서의 네 판정 단위(실루엣·재질 / 단계와 운동 / 동사의 상황 표현 / 실전 연결)는 따로 적고, 값은 기준 문서의 세 가지 '
      '(통과 / 실패 / 미검증)만 쓴다. 자동 촬영은 「찍혔다」만 증명하므로 도구는 모든 판정 칸을 미검증으로 둔다. '
      '실전 연결은 Play 없이는 채울 수 없다.')
    w('')
    w('- 이번 전수 검사의 성격: 합격 판정이 아니라 예고된 VFX 전면 교체(SPEC-SPELL-DEPLOY-308)의 대상 선별이다(기본안, 사용자 확인 대기 — 질문 9).')
    w('- 발광 판정 기준: ArtAudio Bible 39행(술식은 순간 발광만, 지속 금지). 기본안, 사용자 확인 대기.')
    w('- 순위는 조사 우선순위다: 게임에서 재생되는 글자가 먼저, 그 안에서 자동 신호가 많은 순.')
    if not has_static:
        w('- **`static308.json`이 없어 정적 신호가 비어 있다.** `spell120_static308.py`를 먼저 돌린다.')
    if not has_coverage:
        w('- **`coverage.json`이 없어 구현 상태가 비어 있다.**')
    if not sweeps:
        w('- **아직 스윕 이미지가 없다.** 화소 신호는 스윕 뒤 이 도구를 다시 돌리면 채워진다.')
    w('')
    w('## 1. 기록 방식 (기준 문서 「최종 검수 기록 방식」)')
    w('')
    w('잔여 문제 하나에 한 줄: `글자 / 파일·시각 / 실제로 보이는 문제 / 필요한 한 가지 수정 / 판정 범위`. '
      '같은 화면을 이름·recipe가 다르다는 이유로 다른 완성 연출로 세지 않는다. 별도 동사가 없는 공통 규칙에는 억지 차이를 만들지 않는다. '
      '밝은·어두운 배경, 전투 배경, 영상, 비용·잔존 객체 검사를 하지 않은 칸은 미검증으로 남긴다.')
    w('')
    w('## 2. 순위표')
    w('')
    w('| # | 글자 | 분류 · 구현 | 파일·시각 | 자동 신호(사실) | 실제로 보이는 문제 | 필요한 한 가지 수정 | 실루엣·재질 | 단계와 운동 | 동사의 상황 표현 | 실전 연결 | 판정 범위 |')
    w('|---|---|---|---|---|---|---|---|---|---|---|---|')
    for i, (score, index, glyph, facts, where, signals) in enumerate(rows):
        w('| %d | %s | %s · %s | `%s` | %s |  |  | 미검증 | 미검증 | 미검증 | 미검증(Play 필요) |  |' % (
            i + 1, glyph, facts.get('category') or '?', facts.get('implKo') or '?', where, '; '.join(signals) or '없음'))
    w('')
    w('## 3. 우선 비교 묶음 (기준 문서 「우선 비교할 대상」)')
    w('')
    w('묶음 안의 글자를 같은 박자·같은 배경으로 나란히 본다. 공유 계열만으로 실패를 확정하지 않는다.')
    w('')
    w('| 우선 | 묶음 | 눈으로 구별돼야 하는 것 | 시트 위치(원소 · 중성행 · 종성열) | 구별됨? | 메모 |')
    w('|---|---|---|---|---|---|')
    for priority, glyphs, question in PRIORITY_GROUPS:
        places = []
        for g in glyphs:
            i, m, f = decompose(g)
            element = next((e for e, _, ini in ELEMENTS if ini == i), '?')
            places.append('%s(%s·%s·%s)' % (g, element, dict(VOWELS).get(m, '?'), dict(FINALS).get(f, '?')))
        w('| %d | %s | %s | %s | 미검증 |  |' % (priority, glyphs, question, ' '.join(places)))
    w('')
    w('## 4. 최소 미술 계약 점검표 (원소별로 한 번)')
    w('')
    w('| 항목 | 목 | 화 | 토 | 금 | 수 | 큰 실패의 예 |')
    w('|---|---|---|---|---|---|---|')
    for item, failure in (
            ('글자의 동사', '다른 동사인데 같은 회전 링·직선 탄·파도'),
            ('오행 차이', '주형상이 같고 색 말고 차이가 없음'),
            ('한국적 조형', '흰 사각형, 무작위 문자·종교 도상, 완제품 VFX 통째 복사'),
            ('색·재질', '단색 불투명 판, 지속 네온, 화면 백색 포화'),
            ('위치·가독', '얼굴 높이 지면 오라, 발바닥을 가로지르는 검, 지속 지면 관통'),
            ('동작 검증', '타이머 애니메이션만 보고 명중·회복·요격 완료로 주장')):
        w('| %s | 미검증 | 미검증 | 미검증 | 미검증 | 미검증 | %s |' % (item, failure))
    w('')
    w('## 5. 이 틀로 판정할 수 없는 것')
    w('')
    w('- 실제 `Update` 수명과 파괴, 연속 입자, 동시 인스턴스 누수.')
    w('- EA 런타임 신호(각·낙·삭·악, 버프, 방벽), 실제 명중, 패링 접촉. 스윕의 `Sample`은 이 분기를 지나지 않는다.')
    w('- 메인 씬 1인칭 화면의 가독성: 안개, 먹 씻김 후처리, 적 예고를 가리는지.')
    w('- 성능(프레임 시간, VRAM).')
    w('')
    return '\n'.join(out) + '\n'


def main():
    global SWEEP_ROOT
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--sweep', default='', help='one folder name under Sweep/ (default: every folder with a sweep.json)')
    ap.add_argument('--thumb', type=int, default=320, help='thumbnail width in a cell')
    ap.add_argument('--quality', type=int, default=88)
    ap.add_argument('--template-only', action='store_true', help='write only DEFECTS308_TEMPLATE.md and index.html')
    ap.add_argument('--root', default=SWEEP_ROOT, help='sweep root (for tests)')
    args = ap.parse_args()
    SWEEP_ROOT = args.root.replace(BS, '/')
    os.makedirs(SWEEP_ROOT, exist_ok=True)
    table, has_static, has_coverage = glyph_table()
    sweeps = []
    for folder in find_sweeps(args.sweep):
        report = load_json(folder + '/sweep.json')
        if not isinstance(report, dict):
            report = None
        rows, unreadable = collect_rows(folder, report)
        sheets = []
        if not args.template_only and rows:
            sheets = build_sheets(folder, table, args.thumb, args.quality, report or {}, rows)
        sweeps.append((folder, sheets, report, rows))
        print('%s: %d sheets (%s, %d rows in the report, %d more from per-glyph records, %d of them with other settings%s)' % (
            os.path.basename(folder), len(sheets), (report or {}).get('status') if report is not None else 'sweep.json UNREADABLE',
            sum(1 for r in rows if r.get('_source') == 'report'), sum(1 for r in rows if r.get('_source') == 'record'),
            sum(1 for r in rows if r.get('_stale')), ', %d unreadable records' % unreadable if unreadable else ''))
    with open(SWEEP_ROOT + '/index.html', 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(build_index(sweeps, table))
    with open(SWEEP_ROOT + '/DEFECTS308_TEMPLATE.md', 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(build_template(sweeps, table, has_static, has_coverage))
    print('wrote', rel(SWEEP_ROOT + '/index.html'))
    print('wrote', rel(SWEEP_ROOT + '/DEFECTS308_TEMPLATE.md'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
