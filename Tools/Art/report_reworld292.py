"""Publish measured candidate status, keeping art/gameplay claims separate."""
from pathlib import Path
import hashlib, html, json, re
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Reworld292'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/Reworld292'

def run():
    checks=json.loads((OUT/'static-checks.json').read_text(encoding='utf8'))
    failed=[r['check'] for r in checks['checks'] if not r['pass_']]
    metadata=json.loads((OUT/'surface.json').read_text(encoding='utf8'))
    metadata['sourceBounds']=dict(south=35.08,north=35.20,west=127.28,east=127.38)
    (OUT/'surface.json').write_text(json.dumps(metadata,ensure_ascii=False,indent=2),encoding='utf8')
    sources=[
        dict(id='korean-relief',source='https://registry.opendata.aws/terrain-tiles/',attribution='https://github.com/tilezen/joerd/blob/master/docs/attribution.md',local=metadata['source'],sha256=metadata['sourceSHA256'],change='N35E127 crop 35.08–35.20 N / 127.28–127.38 E, world scaling, authored ridges/basin/channels/path grading. Not an exact reconstruction of a named mountain.'),
        dict(id='honhwagak',local='Oheangbu/Assets/JejumokGwana/Prefabs/Buildings/Honhwagak.prefab',status='Previously owned project asset. Original distribution terms retained; no new redistribution license inferred.',change='Private materials, compound-bounds placement, Blender distant LOD. Buddhist architectural conversion pending.'),
        dict(id='korean-bell',local='Korea_TreasureProps/SM_028_Buddhist_Temple_Bell.prefab',status='Previously owned asset; individual historical dimensions and source-object comparison pending.',change='Scaled to 1.25m candidate height; not a measured historical dimension.'),
    ]
    for s in json.loads((OUT.parent/'Mountain285/sources.json').read_text(encoding='utf8')):
        sources.append(dict(s,change='Existing local CC0 source reused; candidate material adaptation, original file retained.'))
    (OUT/'sources.json').write_text(json.dumps(sources,ensure_ascii=False,indent=2),encoding='utf8')
    remaining=[
        '산계 골격은 생성했으나 고산의 절리·노출 암반·너덜과 완만한 숲 사면의 시각적 구분이 부족하다. 5강토 모두 미술 완료 아님.',
        '하천은 얕은 채널과 수면 조회 메시를 연결한 단계다. 상류→하류 고도·합류·홍수원·교량/여울 통행은 재설계·검증 필요.',
        '차량 경로 4개와 산길 3개가 현재 경사 기준 실패. 적로/철옹 정상 경로는 목표 800–1,200m에도 못 미친다. 반복 우회로 길이를 채우지 않는다.',
        'Honhwagak 원형을 배치하고 전용 재질·LOD를 만들었으나 법당 전용 부재 변형 및 5곳의 서로 다른 공간 구성은 미완료. 기단·사면 접합과 실제 유물 비례도 검토 필요.',
        '새 지형과 동굴 입구/내부의 접합, 개별 거점의 기초, 교량/잔도 독립 통행면을 실제 충돌로 다시 맞춰야 한다.',
        '국 하단/상단·복귀문·호송 정차점·NPC/상호작용 높이·하늘/탐험 경계를 최종 원장으로 다시 연결해야 한다. ID 보존은 플레이 연결 성공을 의미하지 않는다.',
        '청림 절의 기존 상호작용을 재배치했지만 나머지 4절은 배치 단계이며 장치·완료·세계 변화 연결이 끝나지 않았다.',
        '기존 NavMesh는 새 지형에 유효하지 않다. 최종 경로/충돌 확정 뒤 재생성하고 실제 플레이어·차량·호송·소환수·적을 검사해야 한다.',
        '원경 식생 카드의 계단 현상·군락 경계와 표면 담채/먹면을 더 다듬어야 한다. LOD 접합, 그림자, 성능은 캡처만으로 판정하지 않는다.',
        '저장 실패·중복 보상·사망·휴식·재실행, CPU/GPU 프레임 시간과 120fps, 한국성·수묵담채 사용자 판정은 전부 새 후보 기준 미검증이다.',
    ]
    timestamp=datetime.now(timezone.utc).isoformat()
    receipt=dict(revision=292,generatedUTC=timestamp,status='PARTIAL_IMPLEMENTATION_NOT_PLAY_READY',scene='Assets/_Project/Art/World/Reworld292/W_Demo_Compact_Reworld.unity',saveSlot='world-compact-reworld-292',saveVersion=8,staticPassed=sum(r['pass_'] for r in checks['checks']),staticTotal=len(checks['checks']),staticFailures=failed,manualPlay='NOT RUN',performance='NOT MEASURED',userArt='NOT APPROVED',remaining=remaining,files={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [ASSET/'Surface/height.bytes',ASSET/'Surface/layout.json',ASSET/'Surface/routes.json',ASSET/'W_Demo_Compact_Reworld.unity']})
    receipt['captures']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(OUT.glob('view-*.png'))}
    (OUT/'implementation-status.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf8')
    status=f'''## #292 실제 작업 상태 — 2026-09-26

**전면 재설계의 첫 환경 후보를 생성했으며 전체 계획은 미완료다. 정본 승격·플레이 합격·미술 완료가 아니다.**

- 후보: `Assets/_Project/Art/World/Reworld292/W_Demo_Compact_Reworld.unity`; 별도 슬롯 `world-compact-reworld-292`, 저장 v8 유지.
- 약 4×6km 단일 높이 자료(1001×1501, 4m 출력), 96개 분할 지형·TerrainData·충돌면과 지도/등고선 연결. 실제 남한 DEM 원본은 약30m이며 4m 측량자료가 아니다. 설악/북한/지리산 특정 봉우리를 복원한 것으로 표현하지 않는다.
- 12개 연결 능선과 3개 수계, 52개 경로 데이터. 환경 원형 및 기존 기능 ID를 복구본과 함께 보존했다.
- 한국 보유 건축 Honhwagak 5동과 부속 초가/범종 배치, 전용 재질, Blender 원경 LOD(754,382→91,660삼각형). 법당 전용 개조·5절 상세 구분은 남았다.
- 후보 전용 지면/건축 셰이더·Renderer, 강토별 보유 수종 연결, 식생 접지. GPU 인스턴싱/LOD 사용은 성능 목표 통과 증거가 아니다.
- 새 정적 검사 {receipt['staticPassed']}/{receipt['staticTotal']}: 실패 {len(failed)}건. 별도 Unity 검사에서 ID/누락 참조/사용 셰이더를 확인했다. 기존 compact 검사 9/9는 기존 배치 정적 검사이며 #292 통과 증거로 재사용하지 않는다.
- 기존 `W_Demo_Compact`와 빌드 목록은 작업 전 해시와 동일하다. 새 후보를 로비 기본 씬으로 연결하지 않았다.
- 실제 플레이·CPU/GPU·사용자 미술 판단 미검증. 기록과 실제 Unity 캡처: `Art/World/Compact/Rebuild/Reworld292/REVIEW.html`.

### 남은 작업과 다음 순서

'''+ '\n'.join(f'{i+1}. {s}' for i,s in enumerate(remaining))+ '\n'
    (OUT/'STATUS.md').write_text(status,encoding='utf8')
    spec=ROOT/'Docs/Specs/SPEC-COMPACT-REWORLD-292.md'
    text=spec.read_text(encoding='utf8').split('## 현재 상태')[0]
    spec.write_text(text+'## 현재 상태\n\n'+status,encoding='utf8')
    note=f'''<!-- reworld292:start -->
**#292 한국 산지·산사 전면 재설계 — 첫 환경 후보 / 미완료 (2026-09-26).** 사용자 승인 계약은 `Docs/Specs/SPEC-COMPACT-REWORLD-292.md`. #291 부분 수정 및 초가 본당 고정은 대체한다. 실제 한국 자연·건축을 바탕으로 수묵담채화하며 한국성은 필수 미술 기준이다. 새 `W_Demo_Compact_Reworld`·슬롯 `world-compact-reworld-292`에서 4×6km 지형/지도/재질·식생·산사 원형을 연결했다. 원본 정본과 저장 v8 유지. 정적 검사 {receipt['staticPassed']}/{receipt['staticTotal']}, 경로 경사 {len(failed)}건 실패. 사찰 전용 변형, 강토 상세화, 동굴/호송/국/복귀문/새 NavMesh와 실제 플레이·성능 검증이 남아 **완료·정본 승격 금지**. 원경 산세·식생·수묵담채도 아직 미술 기준 미달이다. 최신 상세 상태: `Art/World/Compact/Rebuild/Reworld292/STATUS.md`; 실제 캡처: 같은 폴더의 `REVIEW.html`. 과거 검사·미술 승인 승계 없음.
<!-- reworld292:end -->

'''
    for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/DECISIONS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Specs/SPEC-COMPACT-MOUNTAINS-290.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/오행부_ArtAudio_Bible_v0_1.md','Docs/오행부_Production_Bible_v0_1.md']:
        p=ROOT/rel;t=p.read_text(encoding='utf-8-sig')
        t=re.sub(r'<!-- reworld292:start -->.*?<!-- reworld292:end -->\s*','',t,flags=re.S)
        p.write_text(note+t,encoding='utf8')
    esc=html.escape
    realms=['청림','적로','철옹','현강','황경']
    scenes=''
    for i,realm in enumerate(realms):
        scenes+=f'<section><h2>{realm}</h2><p>원경 → 산사 원형 → 접근로. 최종 미술 승인 전의 실제 Unity 에디터 캡처입니다.</p>'
        for n,label in [(i+1,'원경'),(i+6,'산사 원형'),(i+11,'접근로')]:
            scenes+=f'<figure><a href="view-{n}.png"><img loading="lazy" src="view-{n}.png" alt="{realm} {label}"></a><figcaption>{realm} · {label}</figcaption></figure>'
        scenes+='</section>'
    page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>한국 산지·산사 — #292 작업 중</title>
<style>body{margin:0;background:#eee9de;color:#302d26;font:17px/1.8 system-ui,sans-serif}main{max-width:1300px;margin:auto;padding:50px 30px}h1{font-size:34px;line-height:1.4}h2{margin-top:55px;font-size:25px}p,li{max-width:1000px}a{color:#5a5744}figure{margin:25px 0}img{display:block;width:100%;height:auto}figcaption{font-size:14px;padding:8px 0;color:#605b50}table{border-collapse:collapse;width:100%}td,th{padding:14px;border-bottom:1px solid #bdb5a3;text-align:left;vertical-align:top}.state{border-left:4px solid #8b543f;padding:12px 20px;background:#e6ddcf}code{overflow-wrap:anywhere}</style><main>
<p>오행부 / 축소맵 전면 재설계 / 292</p><h1>한국 산지·산사의 첫 환경 후보</h1>
<p class="state">전체 계획은 아직 미완료입니다. 기존 정본을 교체하지 않았으며, 이 후보는 환경 제작용입니다. 산세·표면·식생·법당 변형과 게임 연결에 남은 문제가 있어 완성본으로 제시하지 않습니다.</p>
<p><a href="STATUS.md">상세 작업 상태</a> · <a href="checks.txt">Unity 검사</a> · <a href="static-checks.json">지형/경로 검사</a> · <a href="sources.json">에셋 출처</a></p>
<h2>실물 참고 → 보유 자산 → 현재 적용</h2><table><tr><th>한국 실물 참고</th><th>사용 자료·에셋</th><th>현재 적용과 한계</th></tr>
<tr><td><a href="https://www.kgeography.or.kr/media/11/fixture/data/bbs/publishing/journal/51/01/01.pdf">설악산 지형 연구</a><br>연결된 암릉·풍화토·계곡 관계</td><td>남한 N35E127 DEM, 기존 CC0 rock_face_03 / roots 텍스처</td><td>단일 지형·재료 마스크. 사용 DEM 범위는 북위35.08–35.20/동경127.28–127.38이며 설악산이나 지리산을 복원한 지형이 아닙니다. 참고 사진별 지질 조형 적용은 남았습니다.</td></tr>
<tr><td><a href="https://whc.unesco.org/en/list/1562/">한국의 산사 · 부석사/봉정사/선암사/대흥사</a><br>산지 접근·기단·마당·생활 공간</td><td>JejumokGwana / Honhwagak, 기존 부속 초가와 한국 범종</td><td>원형 건축과 전용 재질·LOD 배치. 관아 원형을 법당으로 최종 채택한 상태가 아닙니다. 5곳의 전용 공간 구성과 실물 비례 검토가 남았습니다.</td></tr>
<tr><td>한국 소나무·활엽수·계곡 식생</td><td>보유 SeyeonjeongPavilion·YongmeoriCoast·기존 Meshy 소나무</td><td>강토별 수종·군락 배치와 접지. 원경 카드 품질·숲 가장자리·성능 실측 미완료.</td></tr></table>
<h2>전체 산계 점검</h2><p>안개와 포스트를 줄인 항공 화면입니다. 이 높이에서는 식생 LOD가 사라지므로 숲의 밀도 판정용 화면은 아닙니다.</p><figure><a href="view-0.png"><img src="view-0.png" alt="후처리를 줄인 전체 지형"></a></figure>
'''+scenes+'<section><h2>남은 문제</h2><ol>'+''.join('<li>'+esc(s)+'</li>' for s in remaining)+'</ol><h2>검증 구분</h2><p>'+f'새 정적 검사 {receipt["staticPassed"]}/{receipt["staticTotal"]}. 실패 목록: '+esc(', '.join(failed))+'''</p><p>실제 플레이: 미실시. CPU/GPU: 미측정. 120fps: 판정 안 함. 사용자 미술 승인: 받지 않음.</p><p>후보 <code>Assets/_Project/Art/World/Reworld292/W_Demo_Compact_Reworld.unity</code><br>메뉴: Oheangbu → Compact Rebuild → 한국 전체맵 292 후보 열기. 기존 로비의 시작 씬은 변경하지 않았습니다.</p></section></main></html>'''
    (OUT/'REVIEW.html').write_text(page,encoding='utf8')
    print(json.dumps(dict(status=receipt['status'],staticFailed=len(failed),captureCount=len(receipt['captures'])),ensure_ascii=False))

if __name__=='__main__':run()
