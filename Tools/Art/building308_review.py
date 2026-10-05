"""#308 building audit review page (SPEC-WORLD-BUILDING-AUDIT-308 §C, review297.py style, Korean).

    python Tools/Art/building308_review.py [--before before] [--after after] [--out <html>]

Reads only what the tools wrote under Art/World/Compact/Rebuild/BuildingAudit308/ (newest scan per scene, Fix/ plans, decisions,
sheet plan and ledger, verify results, Shots/<run>/shots-log.jsonl, Sheets/<run>_NN.jpg) and writes
Art/World/Compact/Rebuild/BUILDING_AUDIT_308_REVIEW.html with relative image links (nothing is copied or embedded).
Missing inputs show as "아직 없음". Reporting words follow the Spec: IMPLEMENTED / VALIDATED / PASS (PASS only after the user).
"""
import argparse, html, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REBUILD = ROOT / 'Art/World/Compact/Rebuild'
AUDIT = REBUILD / 'BuildingAudit308'
OUT = REBUILD / 'BUILDING_AUDIT_308_REVIEW.html'
ALIASES = [('architecture296', '#296 후보'), ('folklore298', '#298 후보'), ('main', 'W_Demo_Main')]
CHECKS = [('C0', '지문'), ('C1', '참조'), ('C2', '착지'), ('C3', '침범'), ('C4', '중복'), ('C5', '구멍'), ('C6', 'LOD'), ('C7', '충돌'), ('C8', '상태'), ('C9', '발광'), ('C10', '셰이더')]
E = html.escape


def load(p):
    try: return json.loads(Path(p).read_text(encoding='utf-8-sig'))
    except Exception: return None


def newest_scan(alias):
    d = AUDIT / alias
    if not d.exists(): return None, None
    runs = sorted([x for x in d.iterdir() if (x / 'audit.json').exists()], key=lambda x: x.name)
    return (runs[-1], load(runs[-1] / 'audit.json')) if runs else (None, None)


def rel(p):
    return Path(p).relative_to(REBUILD).as_posix()


def table(head, rows):
    if not rows: return '<p class="none">아직 없음</p>'
    return '<table><tr>' + ''.join('<th>%s</th>' % E(h) for h in head) + '</tr>' + ''.join('<tr>' + ''.join('<td>%s</td>' % c for c in r) + '</tr>' for r in rows) + '</table>'


def pos(v):
    try: return '(%.1f, %.1f, %.1f)' % (v['x'], v['y'], v['z'])
    except Exception: return ''


def section_scans():
    rows = []; fails = []
    for alias, label in ALIASES:
        d, r = newest_scan(alias)
        if not r: rows.append([E(label), '아직 없음'] + [''] * (len(CHECKS) + 1)); continue
        counts = {c['check']: c for c in r.get('counts', [])}
        cells = []
        for cid, _ in CHECKS:
            c = counts.get(cid)
            cells.append('—' if not c else '<b class="%s">%d</b> / %d / %d' % ('bad' if c['fail'] else 'ok', c['fail'], c['warn'], c['skipped']))
        absent = [x['name'] for x in r.get('roots', []) if not x.get('present')]
        rows.append(['%s<br><small>%s · %s · 품질 %s</small>' % (E(label), E(r.get('utc', '')), E(r.get('sceneSha', '')[:12]), E(r.get('quality', '')))] + cells
                    + ['%d%s' % (r.get('emissionCount', 0), ('<br><small>absent: %s</small>' % E(', '.join(absent))) if absent else '')])
        for f in r.get('findings', []):
            if f.get('level') == 'FAIL': fails.append((alias, f))
    head = ['씬'] + ['%s %s' % c for c in CHECKS] + ['C9 발광 수']
    out = '<p>칸 = FAIL / WARN / SKIPPED. SKIPPED은 통과가 아니다(읽지 못한 메시 등).</p>' + table(head, rows)
    main = [f for a, f in fails if a == 'main'] or [f for a, f in fails]
    out += '<h3>FAIL 항목 (main 우선, 최대 120)</h3>' + table(['검사', '경로', '위치', '값', '내용'], [[E(f['check']), '<code>%s</code>' % E(f.get('path', '')), pos(f.get('position')), '%.2f' % f.get('value', 0), E(f.get('detail', ''))] for f in main[:120]])
    return out


def section_plans():
    out = ''
    for alias, label in ALIASES:
        p = load(AUDIT / 'Fix' / ('plan-%s.json' % alias))
        if not p: out += '<h3>%s</h3><p class="none">계획 아직 없음 (BuildingFix308 plan:%s)</p>' % (E(label), alias); continue
        rows = [[E(s['step']), E(s['status']), E(s.get('detail', '')), '%d / %d' % (sum(1 for c in s['changes'] if c['state'] == 'apply'), sum(1 for c in s['changes'] if c['state'] == 'already')),
                 '<br>'.join(E(n) for n in s.get('notes', [])[:8])] for s in p.get('steps', [])]
        out += '<h3>%s · %s %s</h3>' % (E(label), E(p.get('utc', '')), '<b class="bad">차단</b>' if p.get('blocked') else '') + table(['단계', '상태', '내용', '적용 / 이미', '메모'], rows)
        held = [c for s in p.get('steps', []) for c in s['changes'] if c['state'] == 'blocked']
        if held: out += '<p>보류(캡처 검토):</p>' + table(['단계', '대상', '사유'], [[E(c['step']), '<code>%s</code>' % E(c['key']), E(c['after'])] for c in held])
    d = load(AUDIT / 'Fix/decisions-F1.json')
    if d:
        out += '<h3>F1 새 집터 (세 씬 공통 결정)</h3>'
        for h in d.get('houses', []):
            out += '<p><code>%s</code> → (%.1f, %.1f) · %s</p>' % (E(h['path']), h['target'][0], h['target'][2], E(h.get('chosenBy', '')))
            out += table(['후보 x', 'z', '높낮이', '거리', '통로 여유', '건물 간격', '막힘'], [['%.1f' % c['x'], '%.1f' % c['z'], '%.2f' % c['range'], '%.1f' % c['dist'], '%.1f' % c['corridorClearance'], '%.1f' % c['buildingGap'], E(c.get('blocker', '')) or '통과'] for c in h.get('candidates', [])])
        out += '<p>다른 후보를 고르면 config.json <code>fix.f1.choices</code>에 넣고 <code>Fix/decisions-F1.json</code>의 그 집 항목을 지운 뒤 plan을 다시 돈다.</p>'
    return out


def section_sheet():
    p = load(AUDIT / 'Fix/sheet-plan.json'); l = load(AUDIT / 'Fix/sheet-removals.json')
    out = ''
    if p:
        out += '<p>시트 <code>%s</code> SHA %s</p>' % (E(p['sheet']), E(p['sheetSha'][:16]))
        out += '<h3>지울 것 (결정한 11+1)</h3>' + table(['종류', 'Id', '#', '프로토타입', '건물', '위치', '면 높이'], [[E(r['kind']), E(r['id']), str(r['index']), E(r['proto']), E(r['root'] + '/' + r['building']), pos(r['position']), '+%.2f m' % r['hitHeight']] for r in p.get('remove', [])])
        out += '<h3>검토 대기 (11+1 밖 — 지우지 않음)</h3>' + table(['종류', 'Id', '건물', '위치'], [[E(r['kind']), E(r['id']), E(r['root'] + '/' + r['building']), pos(r['position'])] for r in p.get('review', [])])
        out += ''.join('<p class="note">%s</p>' % E(n) for n in p.get('notes', []))
    else: out += '<p class="none">sheet-plan 아직 없음</p>'
    if l: out += '<h3>시트 원장</h3>' + ''.join('<p><code>%s</code></p>' % E(e) for e in l.get('events', []))
    return out


def runs():
    d = AUDIT / 'Shots'
    return sorted([x.name for x in d.iterdir() if x.is_dir() and (x / 'shots-log.jsonl').exists()]) if d.exists() else []


def log_rows(run):
    rows = []
    p = AUDIT / 'Shots' / run / 'shots-log.jsonl'
    if p.exists():
        for line in p.read_text(encoding='utf-8').splitlines():
            try: rows.append(json.loads(line))
            except ValueError: pass
    return rows


def section_shots(before, after):
    out = ''
    for run in runs():
        rows = log_rows(run); ok = [r for r in rows if r.get('ok')]
        quality = sorted({r.get('qualityRender', '') for r in ok})
        out += '<h3>%s · 스틸 %d (실패 %d) · 실내 표시 %d</h3><p><small>렌더 품질: %s</small></p>' % (E(run), len(ok), len(rows) - len(ok), sum(1 for r in ok if r.get('interior')), E('; '.join(quality)))
        sheets = sorted((AUDIT / 'Sheets').glob(run + '_*.jpg')) if (AUDIT / 'Sheets').exists() else []
        out += ''.join('<a href="%s"><img class="sheet" src="%s" alt="%s" loading="lazy"></a>' % (rel(s), rel(s), E(s.name)) for s in sheets) or '<p class="none">접촉 시트 없음</p>'
    if not runs(): out += '<p class="none">스윕 아직 없음 (Tools/Unity/buildingaudit308_sweep.py --run before)</p>'
    pairs = []
    if before and after:
        a = {r['name']: r for r in log_rows(after) if r.get('ok')}
        for r in log_rows(before):
            if r.get('ok') and r['name'] in a: pairs.append(r['name'])
    out += '<h2>수정 전후</h2>'
    if not pairs: out += '<p class="none">전후 쌍 없음 (같은 shots json으로 before·after 두 번 돌린다)</p>'
    else:
        views = [[n, 'BuildingAudit308/Shots/%s/%s.png' % (before, n), 'BuildingAudit308/Shots/%s/%s.png' % (after, n)] for n in pairs]
        out += '<div class="controls" id="pairs"></div><h3 id="pairname"></h3><div class="pair"><figure><p>수정 전 (%s)</p><a id="bl"><img id="bi" alt="수정 전"></a></figure><figure><p>수정 후 (%s)</p><a id="al"><img id="ai" alt="수정 후"></a></figure></div>' % (E(before), E(after))
        out += '<script>const P=%s;const box=document.getElementById("pairs");const show=i=>{const [n,b,a]=P[i];document.getElementById("pairname").textContent=n;for(const [img,link,src] of [["bi","bl",b],["ai","al",a]]){document.getElementById(img).src=src;document.getElementById(link).href=src;}box.querySelectorAll("button").forEach((x,k)=>x.classList.toggle("on",k===i));};P.forEach((v,i)=>{const b=document.createElement("button");b.textContent=v[0].replace(/^b308_/,"");b.onclick=()=>show(i);box.append(b);});show(0);</script>' % json.dumps(views, ensure_ascii=False)
    return out


def section_pending():
    _, r = newest_scan('main')
    if not r: return '<p class="none">main 스캔 아직 없음</p>'
    f = r.get('findings', [])
    def pick(pred, n=40): return [x for x in f if pred(x)][:n]
    groups = [
        ('처마 의심 (C3 WARN, 철옹·황경)', pick(lambda x: x['check'] == 'C3' and x['level'] == 'WARN' and x.get('root', '').startswith(('Finish297_Cheolong', 'Finish297_Hwanggyeong')))),
        ('River294 바위 (C3, Banks 시트)', pick(lambda x: x['check'] == 'C3' and 'Banks' in x.get('extra', ''))),
        ('11+1 밖 C3 FAIL', pick(lambda x: x['check'] == 'C3' and x['level'] == 'FAIL' and not x.get('root', '').startswith(('Finish297_Cheolong', 'Finish297_Hwanggyeong')))),
        ('청림 산사 요사채 (Reworld292_Sansa, 보호 트리: 고치면 사본만)', pick(lambda x: x.get('root') == 'Reworld292_Sansa' and x['level'] in ('FAIL', 'WARN') and x['check'] in ('C2', 'C7'))),
        ('Guesthouse252·InspectionDesk252', pick(lambda x: 'Guesthouse252' in x.get('path', '') + x.get('unit', '') or 'InspectionDesk252' in x.get('path', '') + x.get('unit', ''))),
        ('Highlands293·CliffPath·Boundary240', pick(lambda x: x.get('root') in ('Highlands293', 'Finish297_CliffPath', 'Boundary240_Dressing') and x['level'] == 'FAIL')),
        ('성벽 LOD 이음 (C5 seam)', pick(lambda x: x['check'] == 'C5' and 'seam' in x.get('detail', ''))),
        ('겹친 면 (C4 동일평면 WARN)', pick(lambda x: x['check'] == 'C4' and x['level'] == 'WARN')),
    ]
    out = ''
    for title, rows in groups:
        out += '<h3>%s · %d</h3>' % (E(title), len(rows)) + table(['검사', '등급', '경로', '위치', '내용'], [[E(x['check']), E(x['level']), '<code>%s</code>' % E(x.get('path', '')), pos(x.get('position')), E(x.get('detail', ''))] for x in rows])
    return out


def section_verify():
    out = ''
    for alias, label in ALIASES:
        files = sorted((AUDIT / 'Fix').glob('verify-%s-*.json' % alias)) if (AUDIT / 'Fix').exists() else []
        if not files: out += '<h3>%s</h3><p class="none">verify 아직 없음</p>' % E(label); continue
        v = load(files[-1])
        out += '<h3>%s · %s · %s</h3>' % (E(label), E(v.get('utc', '')), '<b class="ok">통과</b>' if v.get('fail', 1) == 0 else '<b class="bad">FAIL %d</b>' % v.get('fail', 0))
        out += '<ul>' + ''.join('<li class="%s">%s</li>' % ('bad' if row.startswith('FAIL') else 'ok' if row.startswith('PASS') else '', E(row)) for row in v.get('rows', [])) + '</ul>'
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--before', default='before'); ap.add_argument('--after', default='after'); ap.add_argument('--out', default=str(OUT))
    a = ap.parse_args()
    cfg = load(AUDIT / 'config.json') or {}
    body = f'''<!doctype html>
<html lang="ko">
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>건물 검수 308</title>
<style>
body{{margin:0;background:#eee9de;color:#2d302b;font:16px/1.7 system-ui,"Malgun Gothic",sans-serif}}main{{max-width:1400px;margin:auto;padding:36px 22px 72px}}
h1{{font-size:32px;line-height:1.35}}h2{{margin-top:48px;font-size:24px}}h3{{font-size:18px;margin:22px 0 6px}}
.note{{background:#f8f4ea;border-left:4px solid #8a5a33;padding:10px 14px}}.none{{color:#8a8172}}
table{{border-collapse:collapse;width:100%;font-size:14px}}td,th{{border-bottom:1px solid #cfc6b3;padding:6px 8px;text-align:left;vertical-align:top}}
code{{background:#e4dccb;padding:1px 5px;border-radius:3px;font-size:13px;word-break:break-all}}.bad{{color:#9b2c1f}}.ok{{color:#2f6b3a}}
img.sheet{{width:100%;display:block;margin:10px 0;border-radius:4px}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:14px}}
@media(max-width:900px){{.pair{{grid-template-columns:1fr}}}}figure{{margin:0}}figure p{{margin:0 0 4px;font-size:14px;color:#6b6457}}figure img{{width:100%;display:block;border-radius:4px;background:#ccc}}
.controls{{display:flex;flex-wrap:wrap;gap:6px}}button{{font:inherit;font-size:13px;padding:4px 10px;border:1px solid #9c937f;background:#faf7f0;border-radius:4px;cursor:pointer}}button.on{{background:#2d302b;color:#faf7f0}}
</style>
<main>
<p>오행부 · #308 건물 검수 (D308-1) · 검토 쪽</p>
<h1>전체맵 건물 깨진 곳 검수와 수리</h1>
<p class="note">기준값은 전부 TEST다(config {E(cfg.get("version", "?"))}). 보고는 IMPLEMENTED / VALIDATED / PASS로 나눈다. PASS는 사용자 확인 뒤에만 쓴다. 오프라인 추정치(높이장·메시 JSON)는 측정값이 아니다.</p>
<h2>검사 C0–C10</h2>
{section_scans()}
<h2>수리 계획 (F1 F3 F4 F5)</h2>
{section_plans()}
<h2>F2 지붕 아래 나무·바위 (공유 DryLandscape 시트)</h2>
{section_sheet()}
<h2>캡처 스윕</h2>
{section_shots(a.before, a.after)}
<h2>결정 대기 (사용자 확인)</h2>
{section_pending()}
<h2>수리 뒤 검증</h2>
{section_verify()}
<h2>되돌리기</h2>
<p><code>BuildingFix308 Run "revert:&lt;architecture296|folklore298|main&gt;[:F1|F3|F4|F5]"</code> — 원장의 전 값으로 되돌린다. 마지막 적용 뒤 씬이 바뀌지 않았으면 백업 씬 파일을 그대로 복원한다(SHA 동일).<br>
<code>BuildingFix308 Run "revert:sheet"</code> — DryLandscape 시트를 백업 사본으로 되돌린다. NavMesh는 <code>BuildingAudit308/Backup/Navigation-before308.asset</code>.</p>
</main>
</html>'''
    Path(a.out).write_text(body, encoding='utf-8')
    print(a.out)


if __name__ == '__main__':
    main()
