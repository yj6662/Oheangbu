"""Offline regional sky review from actual Unity capture metadata and audit results."""
from __future__ import annotations

import html
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/RegionalSky'
NAMES = {'Hwanggyeong': '황경', 'Cheongrim': '청림', 'Jeokro': '적로', 'Cheolong': '철옹', 'Hyeongang': '현강'}
SHOTS = ['transition_wood', 'transition_mid', 'transition_earth', *NAMES]
TITLES = {'transition_wood': '청림 쪽', 'transition_mid': '두 강토 사이', 'transition_earth': '황경 쪽', **NAMES}
CHECKS = {
    'normalized region weights including unassigned gaps': '지역 가중치 정규화 · 미지정 틈 포함',
    'maximum parameter delta over 1m at 100m-grid origins': '100m 격자 지점에서1m 이동 시 변화량',
    'actual authored routes maximum <=4m parameter delta': '실제 저작 경로에서4m 이내 변화량',
    'height does not change regional sky': '높이에 따른 지역 하늘 변화 없음',
    'one transient material reused': '임시 하늘 재질 하나 재사용',
    'authored sky material unchanged': '원본 하늘 재질 보존',
    'shared cloud scale': '공통 구름 크기 유지',
    'shared cloud speed': '공통 구름 속도 유지',
    'horizon matches original': '기존 지평선색 유지',
    'actual driver 2s material interpolation': '실제 드라이버의2초 재질 보간 호출',
}


def read(path, default=None):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else default


def esc(value):
    return html.escape(str(value), quote=True)


def check_label(name):
    if name.startswith('2s transition frame rate '):
        return f'{name.rsplit(" ", 1)[-1]}fps 수동 시간 간격의2초 보간'
    return CHECKS.get(name, name)


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    audit = read(OUT / 'audit.json', {})
    sheet = read(OUT.parent / 'sheet.json', {})
    regions = sheet.get('Regions', [])
    labels = [r.get('Label', r.get('Id', str(i))) for i, r in enumerate(regions)]
    checks = audit.get('checks', [])
    passed = sum(c.get('pass') is True for c in checks)
    failed = sum(c.get('pass') is False for c in checks)
    live = read(OUT / 'live.json', {})
    capture_log = read(OUT / 'captures.json', {})
    commits = [float(value) for item in capture_log.get('captures', []) for value in re.findall(r'commit=([0-9.]+)', item.get('result', ''))]
    memory_note = f'순차 촬영 응답의 최고 커밋은{max(commits) * 100:.2f}%이며 신규 촬영 중단 기준은85%입니다.' if commits else '촬영 메모리 기록 미확보.'
    if live:
        live_state = '통과' if live.get('pass') is True else '실패' if live.get('pass') is False else '미검증'
        live_note = f'실제 Play Mode에서 카메라 위치를 바꾼 뒤 정상 LateUpdate를 통해 {live.get("elapsed", 0):.2f}초 시점의 목표값 도달을 확인했습니다: {live_state}, 재질 오차 {live.get("error", 0):.7g}. 이 검사는2초 동안의 실시간 시각 추종이나 중간 프레임을 검수한 결과가 아닙니다.'
        live_html = '<p>' + esc(live_note) + '</p><p class="caption"><a href="live.json">Play Mode 최종 도달 기록 ↗</a></p>'
    else:
        live_note = '실제 Play Mode에서 지역 이동 후 목표 하늘값 도달은 미검증입니다.'
        live_html = '<p class="pending">' + esc(live_note) + '</p>'
    figures = {}
    available = []
    for shot in SHOTS:
        info = read(OUT / f'{shot}.json')
        if not info or not (OUT / f'{shot}.png').exists():
            figures[shot] = f'<p class="pending">{esc(TITLES[shot])} 촬영 미확보</p>'
            continue
        available.append(shot)
        position = info.get('position', {})
        weights = info.get('weights', [])
        pairs = [(labels[i] if i < len(labels) else f'지역{i}', value) for i, value in enumerate(weights)]
        weight_text = ' · '.join(f'{esc(name)} {weight * 100:.1f}%' for name, weight in sorted(pairs, key=lambda p: -p[1]))
        location = ' / '.join(f'{axis.upper()} {position.get(axis, 0):.0f}m' for axis in ('x', 'y', 'z'))
        figures[shot] = f'<figure><a href="{shot}.png"><img src="{shot}.png" alt="{esc(TITLES[shot])} 하늘 검토" loading="lazy"></a><figcaption><strong>{esc(TITLES[shot])}</strong><span>{esc(location)}</span><p>{weight_text}</p></figcaption></figure>'
    rows = ''.join(f'<tr><td>{esc(check_label(c.get("name", "")))}</td><td>{"통과" if c.get("pass") is True else "실패" if c.get("pass") is False else "미검증"}</td><td>{esc(c.get("measured", "미측정"))}</td><td>{esc(c.get("limit", "미지정"))}</td></tr>' for c in checks)
    settings = {}
    profile = ROOT / 'Oheangbu/Assets/_Project/Art/World/WorldMacro/RegionalSkyProfile.asset'
    if profile.exists():
        text = profile.read_text(encoding='utf-8-sig')
        for key in ('BlendDistance', 'ResponseSeconds'):
            match = re.search(r'^\s*' + key + r':\s*([^\r\n]+)', text, re.M)
            if match:
                settings[key] = float(match.group(1))
    distance = settings.get('BlendDistance', '미확인')
    response = settings.get('ResponseSeconds', '미확인')
    transition = ''.join(figures[s] for s in SHOTS[:3])
    realms = ''.join(figures[s] for s in SHOTS[3:])
    summary = f'통과{passed} · 실패{failed}' if checks else '검사 결과 미확보'
    doc = '''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>강토별 수묵 하늘 · 오행부</title><style>
:root{--paper:#edece3;--ink:#323b37;--quiet:#6c756d;--line:#cbd0c4}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.7 system-ui,"Malgun Gothic",sans-serif}main{max-width:1540px;margin:auto;padding:50px 40px}a{color:#526d5d;text-underline-offset:4px}.eyebrow{font-size:11px;letter-spacing:3px;color:var(--quiet)}h1{font-weight:500;font-size:clamp(30px,4vw,50px);letter-spacing:-1.5px;margin:8px 0 16px}h2{font-size:24px;font-weight:500;margin:45px 0 10px}p{margin:8px 0}header{border-bottom:1px solid var(--line);padding-bottom:25px}.intro{max-width:890px}.caption{color:var(--quiet);font-size:12px}.notice{background:#e0e2d6;border-left:3px solid #899681;padding:15px 18px;margin:25px 0}.transition{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}.realms{display:grid;grid-template-columns:1fr 1fr;gap:30px 22px}figure{margin:0}img{display:block;width:100%;aspect-ratio:16/9;object-fit:contain;background:#c8cdc0}figcaption{padding-top:10px}figcaption strong{font-weight:500}figcaption span{display:block;color:var(--quiet);font-size:11px}figcaption p{font-size:11px;color:var(--quiet)}details{margin:20px 0;padding:15px 18px;border:1px solid var(--line)}summary{cursor:pointer}.table-wrap{overflow:auto}table{border-collapse:collapse;width:100%;font-size:12px}th,td{text-align:left;border-bottom:1px solid var(--line);padding:10px 9px}th{font-weight:400;color:var(--quiet)}td{font-variant-numeric:tabular-nums}.pending{padding:20px;background:#dedfd2}footer{margin-top:40px;padding-top:20px;border-top:1px solid var(--line);display:flex;gap:24px;flex-wrap:wrap;font-size:12px}@media(max-width:850px){.transition,.realms{grid-template-columns:1fr}main{padding:28px 20px}}
</style></head><body><main><header><p class="eyebrow">OHEANGBU / REGIONAL INK SKY</p><h1>길을 따라 달라지는 하늘</h1><p class="intro">지역의 경계에서 하늘을 바꾸는 대신 주변 강토의 색과 구름 농도를 섞습니다. 기존 지평선과 구름의 흐름은 유지하며, 상공에만 옅은 차이를 더합니다.</p></header>
<div class="notice">정적 위치별 하늘 검토 · 이미지 안에는 설명문을 넣지 않았습니다. 아래8장은 같은 방향의 시선으로 서로 다른 지점에서 촬영한 화면입니다. 실제 이동 영상이나 시간 순서의 연속 프레임이 아닙니다.</div>
<h2>청림에서 황경으로</h2><p class="caption">실제 귀환길에서 선택한3지점입니다. 청림 비중이 줄어드는 공간 변화를 비교하며, 각 위치의 목표 혼합값을 즉시 적용해 촬영했습니다. 건물·산세가 다른 것은 카메라 위치가 다르기 때문입니다.</p><div class="transition">__TRANSITION__</div>
<h2>다섯 강토의 하늘</h2><p class="caption">거점 이름과 화면 아래 가중치를 함께 보세요. 경계와 가까운 거점은 다른 강토의 색도 섞이므로 해당 지역색100%의 견본이 아닙니다. 이미지를 누르면 원본1920×1080으로 열립니다.</p><div class="realms">__REALMS__</div>
<h2>전환과 확인 범위</h2><p>공간 혼합 설정 __DISTANCE__m · 시간 응답 __RESPONSE__초. 응답값은 지수 보간의 기준이며 그 시간에 전환이 끝난다는 뜻은 아닙니다. 실행 중에는 임시 하늘 재질 사본 하나를 갱신합니다.</p><p class="caption">__SUMMARY__. 수치 검사는 공간 이동과 고정 목표의 시간 보간을 따로 확인합니다. Editor의 실제 드라이버·재질 호출은 포함하지만, 사용자 입력으로 지역 경계를 건너는 Play Mode 완주나30·60·120fps 렌더 검증은 수행하지 않았습니다. 하늘의 자연스러움과 최종 색은 사용자 판단입니다.</p>__LIVE__<p class="caption">__MEMORY__</p>
<details><summary>검사별 수치와 한계</summary><div class="table-wrap"><table><thead><tr><th>검사</th><th>결과</th><th>측정값</th><th>허용 최대</th></tr></thead><tbody>__CHECKS__</tbody></table></div><p class="caption">차이값은 색·농도 등 파라미터의 수치 오차이며 화면 밝기의 물리 측정값이 아닙니다.</p></details>
<footer><a href="REPORT.md">짧은 보고서 ↗</a><a href="audit.json">검사 원문 ↗</a><a href="../REVIEW.html">V4 지면색·이동 흐름 검토 ↗</a><span>지형·경로 재생성 없음 · 영상 없음</span></footer></main></body></html>'''
    for key, value in {'__TRANSITION__': transition, '__REALMS__': realms, '__DISTANCE__': esc(distance), '__RESPONSE__': esc(response), '__SUMMARY__': esc(summary), '__CHECKS__': rows, '__LIVE__': live_html, '__MEMORY__': esc(memory_note)}.items():
        doc = doc.replace(key, value)
    (OUT / 'REVIEW.html').write_text(doc, encoding='utf-8')
    details = '\n'.join(f'| {check_label(c.get("name", ""))} | {"통과" if c.get("pass") is True else "실패" if c.get("pass") is False else "미검증"} | {c.get("measured", "미측정")} | {c.get("limit", "미지정")} |' for c in checks)
    report = f'''# 강토별 수묵 하늘 전이

2026-09-11 · TEST · 결정 #208 · {summary} · 촬영{len(available)}/8장

매크로 씬에만 지역별 하늘을 연결했다. 지역 폴리곤의 부호 있는 거리로 연속 가중치를 만들며 BlendDistance는{distance}m, 지수 보간의 ResponseSeconds는{response}초다. 기존 오행 팔레트를 상공색16%·구름색18%로 섞고 구름 농도는0.12–0.20 범위로 저작했다. 기존 지평선색과 구름 크기·이동 속도는 유지하며 임시 재질 사본 하나를 갱신한다. 지형·경로·V4 지면색과 C2·프롤로그의 기존 설정을 바꾸는 작업은 아니다.

5강토 거점과 청림→황경 귀환길3지점을1920×1080 정지 이미지로 제공한다. 가중치는 각 PNG와 같은 이름의JSON에 기록했다. **서로 다른 위치의 목표 혼합값을 즉시 적용한 이미지이며, 실제 이동 중2초 응답을 촬영한 연속 프레임이 아니다.** {memory_note} 영상은 제작하지 않았다. [검토 페이지](REVIEW.html).

| 검사 | 결과 | 측정값 | 허용 최대 |
|---|---|---:|---:|
{details}

{live_note} [실제 실행 기록](live.json).

공간 연속성과30·60·120fps의 고정 목표 시간 보간은 수치 검사다. 사용자 입력 Play Mode 지역 간 완주·프레임별 렌더·최종 미술 합격은 미검증이다. [검사 원문](audit.json). 원래 V4 지면색17장은 [기존 검토 페이지](../REVIEW.html)에 보존하며 새 하늘의 예시로 취급하지 않는다.
'''
    (OUT / 'REPORT.md').write_text(report, encoding='utf-8')
    print(json.dumps({'review': str(OUT / 'REVIEW.html'), 'captures': len(available), 'checksPassed': passed, 'checksFailed': failed}, ensure_ascii=False))


if __name__ == '__main__':
    build()
