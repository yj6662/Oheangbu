"""Offline macro-water review; final presentation is opt-in after Unity verification."""
from __future__ import annotations

import argparse
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/WaterSurface'
NAMES = {'shore': '물가', 'confluence': '합류부', 'bridge': '다리 아래'}
RIVERS = {'HyeonMain': '현강 본류', 'CheongTributary': '청림 지류',
          'WesternTributary': '서쪽 지류', 'SongakCreek': '송악 계류',
          'SouthernCreek': '남쪽 계류', 'EastDrain': '동쪽 물길'}


def read(name, default=None):
    path = OUT / name
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else default


def esc(value):
    return html.escape(str(value), quote=True)


def build(final=False):
    OUT.mkdir(parents=True, exist_ok=True)
    audit = read('audit.json', {})
    geometry = read('geometry.json', {}).get('rivers', [])
    figures, available = {}, []
    shots = [f'{v}_{s}' for s in NAMES for v in ('before', 'after')]
    shots += [f'after_{s}_time1' for s in NAMES if (OUT / f'after_{s}_time1.png').exists()]
    for shot in shots:
        info = read(f'{shot}.json', {})
        if not info or not (OUT / f'{shot}.png').exists():
            figures[shot] = '<div class="pending">촬영 자료 미확보</div>'
            continue
        available.append((shot, info))
        label = '기존 수면' if shot.startswith('before_') else '수정 수면'
        if shot.endswith('_time1'):
            label += f' · {info.get("waterTime", 1.25):g}초'
        position = info.get('position', {})
        where = ' / '.join(f'{k.upper()} {position.get(k, 0):.1f}m' for k in ('x', 'y', 'z'))
        figures[shot] = f'<figure><a href="{shot}.png"><img src="{shot}.png" alt="{esc(label)}" loading="lazy"></a><figcaption>{esc(label)}<span>{esc(where)}</span></figcaption></figure>'
    comparisons = []
    for key, title in NAMES.items():
        before, after = read(f'before_{key}.json', {}), read(f'after_{key}.json', {})
        same = bool(before and after and before.get('position') == after.get('position') and before.get('target') == after.get('target'))
        note = '같은 카메라 위치와 시선으로 촬영했습니다.' if same else '카메라 일치 확인 대기. 위치 기록은 이미지별 JSON을 따릅니다.'
        comparisons.append(f'<section><h2>{title}</h2><p class="caption">{note}</p><div class="pair">{figures[f"before_{key}"]}{figures[f"after_{key}"]}</div></section>')
    time_pairs = []
    for key, title in NAMES.items():
        shot = f'after_{key}_time1'
        if shot in figures:
            time_pairs.append(f'<h3>{title}</h3><div class="pair">{figures[f"after_{key}"]}{figures[shot]}</div>')
    temporal = '<section><h2>흐름의 두 순간</h2><p class="caption">동일 카메라에서 셰이더 경과 시간을 직접 지정한 정지 이미지입니다. 실제 프레임 순서의 영상이나 Play Mode 연속 촬영은 아닙니다.</p>' + ''.join(time_pairs) + '</section>' if time_pairs else '<section><h2>흐름의 두 순간</h2><p class="pending">시간별 정지 이미지 미확보</p></section>'
    checks = [
        ('Shader Graph 컴파일', audit.get('shaderErrors') == 0 if 'shaderErrors' in audit else None, f'오류 {audit.get("shaderErrors", "미측정")}'),
        ('수면 메시', audit.get('surfaceCount') == 6 if 'surfaceCount' in audit else None, f'{audit.get("surfaceCount", "미측정")}개 / {audit.get("totalTriangles", 0):,} tris'),
        ('공유 재질', audit.get('sharedMaterial'), f'{audit.get("materialInstances", "미측정")}개'),
        ('Depth / Opaque Texture', bool(audit.get('depthTexture') and audit.get('opaqueTexture')) if audit else None, '매크로 카메라 설정'),
        ('수면 충돌체 추가 없음', audit.get('noWaterColliders'), '수영·통행 판정 미추가'),
        ('시간 정지 안정성', audit.get('frozenTimeStable'), f'최대 시간 차이 {audit.get("maxClockDifference", "미측정")}'),
        ('원본 재질 시간값 보존', audit.get('sourceTimeUnchanged'), '런타임 사본에서만 갱신'),
    ]
    if audit.get('surfaces'):
        bad = sum(s.get(k, 0) for s in audit['surfaces'] for k in ('missingTangent', 'missingColor', 'downwardTriangles'))
        checks.append(('탄젠트·색 채널·면 방향', bad == 0, f'누락·역방향 {bad}'))
    preservation = read('preservation.json', {})
    if preservation:
        kept = sum(item.get('unchanged') is True for item in preservation.values())
        checks.append(('기존 그래프·재질·지형 보존', kept == len(preservation), f'{kept}/{len(preservation)} 파일 해시 동일'))
    for filename, label in [('live_slow.json', 'Play Mode 감속'), ('live_pause.json', 'Play Mode 일시정지')]:
        live = read(filename, {})
        detail = f'실제 {live.get("elapsedReal", 0):.2f}초 / 게임 {live.get("elapsedGame", 0):.2f}초 / 재질 시간 오차 {live.get("timeError", 0):.5g}' if live else '실행 기록 미확보'
        checks.append((label, live.get('pass'), detail))
    state = lambda p: '통과' if p is True else '실패' if p is False else '미검증'
    checks_html = ''.join(f'<tr><td>{esc(name)}</td><td>{state(passed)}</td><td>{esc(detail)}</td></tr>' for name, passed, detail in checks)
    river_rows = ''.join(f'<tr><td>{esc(RIVERS.get(r.get("riverId"), r.get("riverId")))}</td><td>{r.get("triangles", 0):,}</td><td>{r.get("sourceWidthM", 0):.1f}</td><td>{r.get("minWidthM", 0):.1f}–{r.get("maxWidthM", 0):.1f}</td><td>{r.get("bankSearchCaps", 0)}</td></tr>' for r in geometry)
    commits = [i.get('commitRatio', 0) for _, i in available if isinstance(i.get('commitRatio'), (float, int))]
    memory = f'제공 이미지의 성공 촬영 최고 커밋 {max(commits) * 100:.2f}%. 촬영 중단 기준은 85%입니다.' if commits else '촬영 커밋 기록 미확보.'
    notice = '수면 외형 검토 · 지형과 물길 원천은 유지했습니다. 다음 단계는 캡슐 보행, 주요 건축물, 마법가마 순서입니다.' if final else '작업 중 · 구조 검사는 확보했으나 최종 수면 표시를 진단하고 있습니다. 아래 수정 이미지는 최종 합격 결과가 아닙니다.'
    doc = '''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>매크로 수면 · 오행부</title><style>
:root{--paper:#e6e8e0;--ink:#2e3d3b;--quiet:#68756f;--line:#bcc7bd}*{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:15px/1.75 system-ui,"Malgun Gothic",sans-serif}main{max-width:1540px;margin:auto;padding:50px 40px}a{color:#3d665e;text-underline-offset:4px}header{border-bottom:1px solid var(--line);padding-bottom:24px}.eyebrow{letter-spacing:3px;font-size:11px;color:var(--quiet)}h1{font-size:clamp(30px,4vw,50px);font-weight:500;letter-spacing:-1.5px;margin:8px 0 14px}h2{font-size:25px;font-weight:500;margin:42px 0 8px}h3{font-weight:500;font-size:17px}.intro{max-width:870px}p{margin:8px 0}.notice{padding:15px 18px;border-left:3px solid #789489;background:#d5dfd5;margin:26px 0}.caption{font-size:12px;color:var(--quiet)}.pair{display:grid;grid-template-columns:1fr 1fr;gap:22px}figure{margin:0}img{display:block;width:100%;aspect-ratio:16/9;object-fit:contain;background:#c2cdc3}figcaption{padding-top:8px;font-size:13px}figcaption span{display:block;color:var(--quiet);font-size:11px}.pending{min-height:160px;display:grid;place-items:center;background:#d3dad0;color:var(--quiet)}details{border:1px solid var(--line);padding:14px 18px;margin:20px 0}summary{cursor:pointer}.scroll{overflow:auto}table{border-collapse:collapse;width:100%;font-size:12px}td,th{text-align:left;padding:10px 9px;border-bottom:1px solid var(--line);font-variant-numeric:tabular-nums}th{font-weight:400;color:var(--quiet)}footer{margin-top:40px;padding-top:20px;border-top:1px solid var(--line);display:flex;gap:24px;flex-wrap:wrap;font-size:12px}@media(max-width:850px){main{padding:28px 20px}.pair{grid-template-columns:1fr}}
</style></head><body><main><header><p class="eyebrow">OHEANGBU / MACRO WATER SURFACE</p><h1>물길의 표면</h1><p class="intro">기존 강의 중심선과 높이를 바탕으로 물가까지 수면을 넓히고, 작은 물결과 하류 방향의 흐름을 더합니다. 공급자 물 그래프와 기존 술식 수면은 보존한 별도 매크로 수면입니다.</p></header><div class="notice">__NOTICE__</div>__COMPARISONS____TEMPORAL__
<section><h2>확인 범위</h2><p>여섯 물길의 메시·재질과 시간 갱신을 다룹니다. 물 위 보행·수영·마법가마·실제 플레이 완주는 이번 검사가 아닙니다. 최종 물색·물결 크기·자연스러움은 사용자 검토로 판단합니다.</p><p class="caption">__MEMORY__</p><details><summary>구조·시간 검사</summary><div class="scroll"><table><thead><tr><th>항목</th><th>결과</th><th>범위</th></tr></thead><tbody>__CHECKS__</tbody></table></div></details><details><summary>실제 수면 기하와 남은 한계</summary><div class="scroll"><table><thead><tr><th>물길</th><th>삼각형</th><th>원천 폭 m</th><th>새 메시 폭 m</th><th>둑 탐색 상한 도달</th></tr></thead><tbody>__RIVERS__</tbody></table></div><p class="caption">폭은 기존 지형과 만나는 지점으로 확장했습니다. 탐색 상한 도달은 해당 행·측면이 허용 거리 안에서 땅을 찾지 못했다는 뜻이며, 합류부 포함 모든 둑의 시각 접촉을 통과했다고 해석하지 않습니다. 정점의 구운 지형 깊이는 정적 지면용이며 이동 물체를 표현하지 않습니다.</p></details></section>
<footer><a href="REPORT.md">짧은 보고서 ↗</a><a href="audit.json">검사 원문 ↗</a><a href="geometry.json">메시 통계 ↗</a><a href="../RegionalSky/REVIEW.html">강토별 하늘 ↗</a><a href="../REVIEW.html">이전 V4 지면 검토 ↗</a><span>1920×1080 정지 이미지 · 영상 없음</span></footer></main></body></html>'''
    for key, value in {'__NOTICE__': esc(notice), '__COMPARISONS__': ''.join(comparisons), '__TEMPORAL__': temporal, '__MEMORY__': esc(memory), '__CHECKS__': checks_html, '__RIVERS__': river_rows}.items():
        doc = doc.replace(key, value)
    (OUT / 'REVIEW.html').write_text(doc, encoding='utf-8')
    check_md = '\n'.join(f'| {name} | {state(passed)} | {detail} |' for name, passed, detail in checks)
    river_md = '\n'.join(f'| {RIVERS.get(r.get("riverId"), r.get("riverId"))} | {r.get("triangles", 0):,} | {r.get("minWidthM", 0):.1f}–{r.get("maxWidthM", 0):.1f} | {r.get("bankSearchCaps", 0)} |' for r in geometry)
    status = '검토 자료 제출 · 미술 합격 대기' if final else '작업 중 · 최종 수면 표시 진단 미완료'
    report = f'''# 매크로 수면 Shader Graph

2026-09-12 · TEST · {status}

기존6강의 중심선·지형을 유지하고 수면 메시와 별도 Shader Graph를 연결한다. 실제 지형 콜라이더로 둑을 찾고 세로8m 이하·가로8칸으로 구성했으며, UV는 중심선 기준 횡 좌표와 하류 방향의 실제 미터를 사용한다. 양쪽 둑의 변위와 독립 끝단의 알파를 줄이고, 자식 물길 끝60m에서 부모 수면 높이에 맞춘다. 부모의 실제 확장된 수면 영역으로 자식 삼각형을 잘라 중첩을 줄인다.

구운 지형 깊이는 정점 G에0–8m를 정규화해 저장한다. 정적 지형용이며 이동 물체 깊이와 다르다. 초기 투명 표시 문제는 저장 메시와 GPU 정점 채널의 불일치를 갱신해 수정했다. 실제 GPU 깊이는 양수로 확인했으므로 Depth Texture 실패로 결론내리지 않았다.

[검토 페이지](REVIEW.html). 확보한 비교·시간별 정지 이미지는 {len(available)}장이다. {memory} 영상은 제작하지 않는다. 시간별 이미지는 셰이더 시간을 직접 지정한 결과이며 실제 연속 프레임 촬영이 아니다.

| 검사 | 결과 | 범위 |
|---|---|---|
{check_md}

| 물길 | 실제 tris | 메시 폭 m | 둑 탐색 상한 도달 |
|---|---:|---:|---:|
{river_md}

둑 탐색 상한은 행·측면별 횟수이며 고유한 결함 개수가 아니다. 합류 범위를 포함할 수 있고 모든 강변의 시각 접촉이 검증됐다는 뜻도 아니다. 최종 물색·물결·연속 합류의 미술 합격, 실제 플레이 입력·수영·통행과 성능 비교는 미검증이다.

작업 순서는 **수면 → 실제 크기 캡슐 보행 → 조선식 궁궐·성곽·동굴·사찰 → 마법가마**다. 건축물·가마의 파츠별 ImageGen·Meshy·Blender 제작, 가마의 바퀴·흔들림·가감속·탑승 시점은 아직 진행하지 않았다. 이번에는 수면만 다룬다. [구조 검사](audit.json), [실제 메시 통계](geometry.json), [기존 하늘 검토](../RegionalSky/REVIEW.html), [기존 지면 검토](../REVIEW.html).
'''
    (OUT / 'REPORT.md').write_text(report, encoding='utf-8')
    print(json.dumps({'review': str(OUT / 'REVIEW.html'), 'captures': len(available), 'final': final}, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--final', action='store_true')
    build(parser.parse_args().final)
