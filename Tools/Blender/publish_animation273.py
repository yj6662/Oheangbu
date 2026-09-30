"""Publish measured animation evidence and update the current project records."""
import json,hashlib,re,html
from pathlib import Path
from datetime import datetime,timezone
R=Path('C:/Users/yj666/Oheangbu');O=R/'Art/Characters/Animation273'
reports={}
for name in ['unity-checks','sinmok-combat-checks']:
 lines=(O/(name+'.txt')).read_text(encoding='utf-8-sig').splitlines();reports[name]={'pass':sum(l.startswith('PASS ')for l in lines),'fail':sum(l.startswith('FAIL ')for l in lines)}
assert all(v['fail']==0 for v in reports.values()),reports
rig=json.loads((R/'Art/Characters/Sinmok272/rig/rig-report.json').read_text());triangles=sum(p['triangles']for p in rig['parts'])
summary=f'''# 신목과 전투 동작 #273

2026-09-25. 승인된 `concept-idle.png`를 Meshy 7.1로 생성하고 Blender 5.0에서 직접 리깅했다. 현재 후보 `W_Demo_Compact_MigrationCheck`에 적용했다. Unity Play의 진입점은 기존 `W_Compact_Lobby`다.

## 구현

- **신목:** 생성 표면의 UV/PBR를 유지한 몸통·오른팔·왼팔·수관 4개 스킨 메시, 22개 본, {triangles:,}삼각형. 기본 자세와 왼팔 내려치기·휩쓸기·뿌리 반응·가지 털기, 지지 팔을 드는 동작 총 6개 클립. 큰 방향 전환의 예고 구간에만 지지 팔을 들어 다시 짚고, 고정 방향 공격 중에는 접지를 유지한다. 실제 지형의 낮은 뿌리 지점 33곳을 샘플링해 모델만 1.2565m 내렸다.
- **소환수 5종:** 기존 걷기와 고유 공격/IK를 유지하고 생성·해제 동작을 추가했다. 후보 전용 프로필에서 기존 생성/해제 시계로 재생한다. 새 피격·그로기·사망·일반 적용 공격 클립도 각 리그의 편집 파일에 포함한다.
- **현재 일반 적 5개체:** 호랑이·해태 외형을 사용하는 폐광 3개체와 마을 길목 2개체. 공격 예고·타격·회복 시점에 맞춘 클립, 피격·그로기·사망을 연결했다. 판정과 재화/저장은 기존 전투가 소유한다. 사망 즉시 충돌/공격을 끄고 동작 종료 후 표시를 숨긴다.
- **표시 복귀:** 월드 컬링과 휴식 복귀가 교체 전 신목 표면을 다시 켜지 않도록 최초 표시 상태를 보존한다. 원본 정본·기존 소환수 프로필·원본 Blender 모델은 변경하지 않았다.
- 기존 청룡과 남문 장수의 전용 전투 표현은 유지했다. 학습용 나무는 정적인 훈련 대상이다. 남문 장수의 전용 양손 장병기 동작/손 접촉 정리는 별도 기존 잔여로 유지한다.

## 검증과 증거

- 현재 후보 복제본의 새 검사: 구성·클립 바인딩·월드 좌표 변형 범위·소환/해제/일시정지·실제 피해에 따른 피격·그로기·사망/표시 복귀 **{reports['unity-checks']['pass']} PASS, 0 FAIL**.
- 신목 네 공격 × 두 방향의 전투 시계·잠금 방향·실제 피해 1회 적용·다음 공격·사망 취소 **{reports['sinmok-combat-checks']['pass']} PASS, 0 FAIL**. 과거 #271 결과를 재사용하지 않았다.
- Blender와 Unity의 고정 방향 공격에서 오른팔 손목 드리프트 0m. 방향 전환 시 재접지는 별도 동작이며 지형 변화에 대응하는 실시간 손 IK는 아니다.
- `candidate-scene.png`: 실제 저장된 후보 씬의 1080p 오프스크린 화면. `UnityCaptures/`: Unity 클립과 본/바인드 포즈로 계산한 스킨 표면의 1080p 진단 24장. GPU 스킨의 실제 연속 게임 화면 녹화와 구분한다.
- `sinmok-motion.mp4`: Blender 동작 검토 영상. 생성 모델의 몸통/팔 변형을 검토하기 위한 영상이며 실제 전투 녹화가 아니다.
- 읽기 전용 지형/배치 정합성 점검 9 PASS, 기존 알려진 간극 6개 유지.
- 구현 도중 발견한 가느다란 가지 늘어남은 연결 표면 거리 기반 가중치로 수정했다. 기존 FBX와 다른 단위로 발생한 소환수의 과대 변형도 수정한 뒤 새 검사를 수행했다.

## 남은 판단

사용자 입력을 사용하는 Play, 지형 위 다양한 방향의 연속 공격 가독성/손 접지, 시각적 팔 접촉과 기존 범위 판정의 정밀 일치, CPU/GPU 시간은 아직 실측하지 않았다. 다른 작업을 방해하지 않도록 Computer Use를 사용하지 않았다. 승인된 것은 입력 시안이며, 생성 3D 모델과 동작의 미술 승인은 아직 받지 않았다.

낙엽 띠를 따라 솟는 뿌리, 임시 뿌리 장벽과 탈출구, 몸 전체의 이동/약점 노출은 `PLAN-SINMOK-PATTERNS-272.md`의 후속 전투 제작이다. 이번 가지·뿌리 클립을 그 신규 패턴 전체 구현으로 기록하지 않는다.

## 복구와 출처

`Recovery/Candidate.unity`는 소환수/일반 적 연결 전 후보, `../Sinmok272/rig/Recovery/Candidate.unity`는 신목 교체 전 후보다. 기존 진행 저장에 대한 쓰기, 슬롯 변경, 지형/NavMesh 재조립은 하지 않았다. 미커밋 원본과 생성 전 자료는 보존했다.

생성 입력/작업 ID/35크레딧 사용은 `../Sinmok272/approval.json`, `../Sinmok272/meshy-ledger.json`에 기록했다. [Meshy 이미지→3D API](https://docs.meshy.ai/en/api/image-to-3d), [가격](https://docs.meshy.ai/en/api/pricing). Unity 진단은 렌더러 스케일의 이중 적용을 피하려고 본의 월드 행렬과 바인드 포즈를 사용한다. [Unity BakeMesh 문서](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/skinnedmeshrenderer/bakemesh).
'''
(O/'REPORT.md').write_text(summary,encoding='utf-8')
cards=''.join(f'<figure><img loading="lazy" src="{f}/A273_Form_50.png"><figcaption>{label} · 생성 자세</figcaption></figure>'for f,label in [('WoodDeer','목 사슴'),('FireHaetae','화 해태'),('MetalTiger','금 호랑이'),('DokkaebiClub','토 도깨비'),('WaterTurtle','수 거북')])
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>신목 · 전투 동작</title><style>body{{margin:0;background:#e7e3d9;color:#2d302c;font:16px/1.8 sans-serif}}main{{max-width:1200px;margin:50px auto;padding:0 24px}}h1{{font:38px serif;margin-bottom:8px}}h2{{font:25px serif;margin-top:48px}}img,video{{width:100%;display:block;background:#252825}}.grid{{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:18px}}figure{{margin:0}}figcaption{{padding:8px 0}}a{{color:inherit}}small{{color:#555}}p{{max-width:900px}}</style><main><h1>신목 · 전투 동작</h1><p>승인 시안 → Meshy 7.1 → Blender 리깅 → 축소맵 후보 적용.</p><video controls loop preload="metadata" poster="../Sinmok272/rig/Motion/Sinmok_Idle_0.png" src="sinmok-motion.mp4"></video><small>Blender 동작 영상 · 실제 플레이 녹화와 구분</small><h2>후보 씬</h2><img src="candidate-scene.png"><p>오른팔로 지지하고 왼팔로 공격한다. 기본 자세·내려치기·휩쓸기·뿌리 반응·가지 털기·재접지 6개 클립. 교체 전 모델은 복구 가능하게 숨겼다.</p><h2>소환수와 일반 적</h2><div class="grid">{cards}</div><p>소환수 5종의 생성·해제 동작, 일반 적 5개체의 공격 시점·피격·그로기·사망 동작을 연결했다. 기존 고유 공격은 유지한다.</p><h2>검증 상태</h2><p>격리 애니메이션 검사 {reports['unity-checks']['pass']}개와 신목 전투 검사 {reports['sinmok-combat-checks']['pass']}개 통과. 실제 입력 플레이·성능·미술 판정은 별도다. 신규 낙엽/장벽/이동 공격 패턴 전체 구현은 후속으로 남아 있다.</p><p><a href="REPORT.md">구현과 검증 보고서</a> · <a href="unity-checks.txt">애니메이션 검사</a> · <a href="sinmok-combat-checks.txt">전투 검사</a> · <a href="../Sinmok272/rig/Sinmok273.blend">Blender 원본</a></p></main></html>'''
(O/'REVIEW.html').write_text(page,encoding='utf-8')
block=f'''<!-- animation-273:start -->
**현재 신목/전투 동작 #273 (2026-09-25):** 승인된 대기 자세 시안으로 Meshy 7.1 생성 완료 후 Blender 전용 리깅을 적용했다. 신목 4개 스킨 표면·22본·기본/4공격/재접지 6클립, 소환수 5종 생성/해제 동작, 현재 일반 적 5개체 공격/피격/그로기/사망/복귀를 후보에 연결했다. 지형 접지와 컬링/휴식 시 구형 표면 재등장도 정리했다. #271은 반려된 과거 모델이며 현재 표면이 아니다.

**검증:** 새 애니메이션 격리 검사 {reports['unity-checks']['pass']} PASS, 신목 전투 복제 검사 {reports['sinmok-combat-checks']['pass']} PASS. 실제 후보 화면/Blender 동작 영상과 Unity 스킨 진단을 구분했다. Play/Computer Use/실제 저장 쓰기 없음. 실제 플레이·CPU/GPU·사용자 미술 판단은 미완료다. 기존 청룡/장수 동작과 선택 보스 진행을 보존하며, 신규 낙엽 띠·장벽·몸 이동 패턴은 후속이다. 다음 순서는 실제 방향 전환과 접지/공격 범위 가독성 확인 → 낙엽/장벽 패턴 제작이며 시각 승인 없이 전체 보스 완료로 기록하지 않는다. [보고서](C:/Users/yj666/Oheangbu/Art/Characters/Animation273/REPORT.md) · [검토](C:/Users/yj666/Oheangbu/Art/Characters/Animation273/REVIEW.html).
<!-- animation-273:end -->

'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md']:
 p=R/rel;t=p.read_text(encoding='utf-8-sig');t=re.sub(r'<!-- animation-273:start -->.*?<!-- animation-273:end -->\s*','',t,flags=re.S)
 t=re.sub(r'<!-- sinmok-generated-272:start -->.*?<!-- sinmok-generated-272:end -->', '<!-- sinmok-generated-272:start -->\n**과거 방향 결정 #272:** #271 절차 모델을 반려하고 성균관 고목을 참고한 생성 시안을 검토했다. 이후 대기 자세가 승인되어 #273에서 Meshy/Blender/후보 적용까지 진행했다. [원 시안과 출처](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-SINMOK-GENERATED-272.md).\n<!-- sinmok-generated-272:end -->',t,flags=re.S)
 t=t.replace('**현재 모델 #271 —','**과거 반려 모델 #271 —');p.write_text(block+t,encoding='utf-8')
p=R/'Docs/DECISIONS.md';t=p.read_text(encoding='utf-8-sig');decision='## D273 — 승인 신목 생성 및 Blender 전투 동작\n\n사용자가 승인한 대기 시안을 Meshy 7.1로 생성하고 Blender에서 분리·리깅·공격 동작을 제작한다. 소환수와 현재 일반 적의 누락 동작을 함께 연결한다. 원본 자료와 진행 저장은 보존하고, 새 모델의 사용자 미술 판정은 별도로 받는다. [구현 범위와 검증](../Art/Characters/Animation273/REPORT.md).\n\n';p.write_text(decision+t if '## D273 ' not in t else t,encoding='utf-8')
p=R/'Docs/Specs/SPEC-SINMOK-GENERATED-272.md';t=p.read_text(encoding='utf-8-sig');t=re.sub(r'<!-- animation-273:start -->.*?<!-- animation-273:end -->\s*','',t,flags=re.S);t=t.replace('이하 #272 시안 단계 기록은 과거 경과다. 현재 상태는 위 #273을 따른다.\n\n','');p.write_text(block+'이하 #272 시안 단계 기록은 과거 경과다. 현재 상태는 위 #273을 따른다.\n\n'+t,encoding='utf-8')
p=R/'Docs/Plans/PLAN-SINMOK-PATTERNS-272.md';t=p.read_text(encoding='utf-8-sig');t=re.sub(r'^\*\*#273 진행:\*\*[^\n]*\n\n','',t);p.write_text('**#273 진행:** 승인 시안의 생성 모델/Blender 리깅/기존 네 공격 시계 연결/방향 전환 재접지를 구현했다. 아래 낙엽 띠·장벽·실제 몸 이동/약점 패턴은 후속 전투 구현이다. 애니메이션 클립 제작을 신규 판정 전체 구현으로 보지 않는다.\n\n'+t,encoding='utf-8')
p=R/'Art/Characters/Sinmok272/generation.json';d=json.loads(p.read_text());d.update(status='approved_image_generated_3d_and_blender_rig_applied_model_art_review_pending',meshy_3d_submitted=True,scene_modified=True,approval_basis='User approved concept-idle.png and explicitly requested Blender rigging, attack animation and scene work.',current_model_review='../Animation273/REVIEW.html');p.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
manifest={'created_at':datetime.now(timezone.utc).isoformat(),'reports':reports,'fixture_scope':'Current candidate clones; not native-input play or performance measurement','sources':{}}
paths=list((R/'Oheangbu/Assets/_Project/Art/Characters/Sinmok273').glob('*'))+list((R/'Oheangbu/Assets/_Project/Art/Characters/Animation273').glob('*.fbx'))
paths+=[R/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity']
paths+=[R/('Oheangbu/Assets/_Project/Scripts/'+s) for s in ['App/Demo/SinmokRigAnimation.cs','App/Demo/DemoSummonPresentation.cs','App/Demo/SummonCombatProfile.cs','App/Prologue/PrologueEncounter.cs','App/World/WorldMacroPlaytestSession.cs','App/World/EnemyRigMotion273.cs','Editor/WorldMacro/CompactAnimation273.Checks.cs','Editor/WorldMacro/CompactSinmok273.cs']]
for p in paths:
 if p.is_file():manifest['sources'][str(p.relative_to(R))]=hashlib.sha256(p.read_bytes()).hexdigest()
(O/'verification-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print(json.dumps(reports))
