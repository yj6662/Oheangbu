from pathlib import Path
import json,hashlib,re,html
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
O=ROOT/'Art/World/Compact/Rebuild/Highlands293';A=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292'
def run():
    d=json.loads((A/'Surface/layout.json').read_text(encoding='utf8'));mods=json.loads((O/'modules.json').read_text(encoding='utf8'))['Modules'];stats=json.loads((O/'static-checks.json').read_text())
    baseline=np.fromfile(O/'baseline-height.bytes',dtype='<f4');current=np.fromfile(A/'Surface/height.bytes',dtype='<f4')
    stats['actualChangedSamples']=int(np.count_nonzero(current!=baseline));stats['heightSHA256']=hashlib.sha256((A/'Surface/height.bytes').read_bytes()).hexdigest()
    # Voronoi ownership supplies complete, disjoint region interiors.
    def inside(p,poly):
        n=False
        for a,b in zip(poly,poly[1:]+poly[:1]):
            if (a['y']>p[1])!=(b['y']>p[1]) and p[0]<(b['x']-a['x'])*(p[1]-a['y'])/(b['y']-a['y'])+a['x']:n=not n
        return n
    counts=[sum(inside((x+.37,z+.73),r['Polygon']) for r in d['Realms']) for x in range(0,4000,80) for z in range(0,6000,80)]
    stats['regionInteriorSamples']=len(counts);stats['uniqueRegionCoverage']=all(n==1 for n in counts)
    stats['moduleOwners']=[dict(mountain=m['Id'],realm=next((r['Id'] for r in d['Realms'] if inside((m['Centre']['x'],m['Centre']['z']),r['Polygon'])),'NONE')) for m in mods]
    stats['rendering']='Unity editor captures, not gameplay';stats['performance']='NOT MEASURED'
    (O/'static-checks.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf8')
    svg=['<svg xmlns="http://www.w3.org/2000/svg" viewBox="-130 -170 4260 6410"><rect x="-130" y="-170" width="4260" height="6410" fill="#ede7da"/>']
    for r in d['Realms']:
        colour='#'+''.join(f'{int(r["Tint"][k]*255):02x}' for k in ['r','g','b']);p=' '.join(f'{v["x"]},{6000-v["y"]}' for v in r['Polygon']);c=r['Centre']
        svg.append(f'<polygon points="{p}" fill="{colour}" fill-opacity=".35" stroke="#68614e" stroke-width="10"/><text x="{c["x"]}" y="{6000-c["y"]}" text-anchor="middle" font-size="130" fill="#272b27" font-family="Malgun Gothic,sans-serif">{r["Label"]}</text>')
    for m in d['Mountains']:
        p=' '.join(f'{v["x"]},{6000-v["z"]}' for v in m['MainPath']);svg.append(f'<polyline points="{p}" fill="none" stroke="#55452d" stroke-width="12"/>')
    for m in mods:
        p=' '.join(f'{v["x"]},{6000-v["z"]}' for v in m['Path']);svg.append(f'<polyline points="{p}" fill="none" stroke="#cfba82" stroke-width="23"/>')
    svg.append('</svg>');(O/'REALMS.svg').write_text(''.join(svg),encoding='utf8')
    labels=['청림','적로','철옹','현강','황경'];traits=['소나무·활엽수, 화강암, 벌목 흔적과 낡은 목재','마른 수종, 적은 숲 밀도, 국소적인 탄 목재','성긴 수목, 회백색 암반과 잔도 철물 보강','습윤 수종, 차가운 담채, 빛바랜 젖은 목재','중앙 분지와 인왕산, 절제된 식생, 석로·보수 흔적']
    pages=''
    for k,(label,trait) in enumerate(zip(labels,traits)):
        pages+=f'<section><h2>{label}</h2><p>{trait}</p>'
        for kind in ['trail','overview']:
            if (O/f'{kind}-{k}.png').exists():pages+=f'<a href="{kind}-{k}.png"><img loading="lazy" src="{kind}-{k}.png" alt="{label} 실제 Unity {kind}"></a>'
        pages+='</section>'
    surface=(O/'surface-checks.txt').read_text(encoding='utf8') if (O/'surface-checks.txt').exists() else 'Surface obstruction audit pending'
    page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>고산 산길과 다섯 강토 · 293</title><style>body{margin:0;background:#eee9de;color:#2d302b;font:17px/1.8 system-ui}main{max-width:1300px;margin:auto;padding:45px 28px}h1{font-size:34px}h2{margin-top:50px}img{display:block;width:100%;height:auto;margin:20px 0}a{color:#605338}.map{max-width:520px}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#e1dcce;padding:18px;font-size:14px}p{max-width:1050px}</style><main><p>오행부 · 293</p><h1>완만한 산을 남기고, 고산에 산길을 잇다</h1><p>대표 고산 다섯 곳에 이전 #285–289의 불규칙 계단·절벽·하부 사면·바위 턱·목재 잔도를 변형해 적용했습니다. 각 전체 산행 중 약110m 구간입니다. 산 전체를 같은 계단으로 바꾸지 않았습니다.</p>'''
    page+=f'<p>지형 변경 창: 전체 높이 샘플의 {stats["changedPercent"]:.2f}%. 창 밖 {stats["unchangedSamples"]:,}개 높이는 작업 전과 동일합니다. 낮고 완만한 산의 높이를 다시 생성하지 않았습니다.</p>'
    page+='<h2>강토 배치</h2><p>중앙 황경 / 동 청림 / 남 적로 / 서 철옹 / 북 현강. 같은 폴리곤을 지도·도착명·하늘 위치 조회·식생 배치에 연결했습니다. 아래 선은 제작 검토용이며 게임 목적지 마커가 아닙니다.</p><img class="map" src="REALMS.svg" alt="강토 원장과 고산 산길 위치">'+pages
    page+='<h2>검증과 한계</h2><pre>'+html.escape(surface)+'</pre><p>산길 접지 레이는 자동 fixture입니다. 실제 보행·회피·낙하·AI와 CPU/GPU 성능은 미검증입니다. 기존 #292의 차량 경사·동굴·호송·국·복귀문·NavMesh 잔여와 사찰 상세 변형은 이번 산길 이식과 별도입니다. 고산 산체 접합과 강토별 전체 생활 공간도 추가 미술 검토가 필요합니다.</p><p><a href="static-checks.json">새 정적 검사</a> · <a href="unity-checks.txt">메시/충돌 이식 기록</a> · <a href="surface-checks.txt">통행면 차폐 검사</a></p><p>후보 씬: Assets/_Project/Art/World/Reworld292/W_Demo_Compact_Reworld.unity<br>기존 정본·저장 슬롯은 변경하지 않았습니다.</p></main></html>'
    (O/'REVIEW.html').write_text(page,encoding='utf8')
    note=f'''<!-- highlands293:start -->
**#293 고산 산길·강토 구분 (2026-09-26):** 대표 고산 5곳의 중상단 약110m씩에 #285–289의 실제 절벽/불규칙 계단/잔도 메시를 변형 이식했다. 변경 창은 전체 높이 샘플의 {stats['changedPercent']:.2f}%; 바깥 높이 보존. `CompactWorldLayoutSO.Realms`에 중앙 황경·동 청림·남 적로·서 철옹·북 현강의 5영역을 명시하고 지도/도착명/하늘 조회/식생을 연결한다. 강토별 수종·밀도·재료와 일부 산길 생활 흔적 보강. 근거: `Art/World/Compact/Rebuild/Highlands293/REVIEW.html`, 제작 계약 `Docs/Specs/SPEC-COMPACT-HIGHLANDS-293.md`. 기존 #292의 게임 연결·통행·성능·미술 잔여는 여전히 남으며 정본 승격 아님. 과거 #292 캡처/검사는 #293의 통과 근거로 사용하지 않는다.
<!-- highlands293:end -->

'''
    for rel in ['BIBLE_INDEX.md','PROJECT_STATUS.md','DECISIONS.md','Specs/SPEC-COMPACT-REWORLD-292.md','Plans/PLAN-COMPACT-REBUILD-NEXT.md','Handoff/HANDOFF-COMPACT-REBUILD.md','오행부_ArtAudio_Bible_v0_1.md','오행부_Production_Bible_v0_1.md']:
        p=ROOT/'Docs'/rel;s=p.read_text(encoding='utf-8-sig');s=re.sub(r'<!-- highlands293:start -->.*?<!-- highlands293:end -->\s*','',s,flags=re.S);p.write_text(note+s,encoding='utf8')
    print(json.dumps(stats,ensure_ascii=False))
if __name__=='__main__':run()
