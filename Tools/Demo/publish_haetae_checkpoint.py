"""Publish measured first fire summon checkpoint; never claim art or manual combat completion."""
from pathlib import Path
import json,hashlib,shutil

root=Path(__file__).resolve().parents[2]
base=root/'Art/Demo/Summons';out=base/'FireHaetae';out.mkdir(exist_ok=True)
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
run=read(base/'runtime_flame_tests.json');tests=read(base/'flame_tests.json');audit=read(base/'haetae_scene_audit.json')
assert run['status']=='PASS_RUNTIME_API' and not run['failed']
assert tests['status']==audit['status']=='PASS'
save=read(root/'Art/Demo/Chapter2/runtime_tests.json')
assert save['suffixRestored'] and not save['holdingPlay'] and not save['active'] and not save['failures']
shutil.copy2(root/'Art/Demo/Chapter2/runtime_tests.json',out/'save_isolation_and_restore.json')
rig=read(out/'rig_report.json')
assert hashlib.sha256(Path(rig['source']).read_bytes()).hexdigest()==rig['sourceSha256Before']==rig['sourceSha256After']
assert rig['uvHashBefore']==rig['uvHashAfter']
captures=[]
for view in ('external','player'):
    name='nom_flame_'+view+'_v1'
    for suffix in ('_1920x1080.png','_capture.json'):
        source=root/'Art/Demo/Foundation'/(name+suffix);shutil.copy2(source,out/source.name)
    meta=read(out/(name+'_capture.json'));assert meta['commitRatio']<.85 and (meta['width'],meta['height'])==(1920,1080)
    captures.append(meta['commitRatio'])
report=f'''# 놈 — 거리 유지와 짧은 화염 공격 첫 연결

기존 해태 외형에 실제 리그와 대기·전진·후퇴·화염 시전을 만들고 전용 데모의 소환 경로에 연결했다. 신규 유료 생성과 빌드는 없다. 최종 모델과 모션 미술, 전체 데모 완료를 뜻하지 않는다.

## 구현

- 적이2.5m 안으로 접근하면 바라보는 방향을 유지하며3.5m까지 후퇴한다. 후퇴 경로의 지면·NavMesh·회전 중간 공간을 확인한다. 벽에 막히면 강제로 이동하지 않고 현재 위치에서 유효한 공격을 시도한다.
- 고정 입 위치를 기준으로 사거리4.5m·반각30도, 준비0.45초·분사0.65초·회복0.5초다. 준비 시작에 대상 목록과 생명주기를 고정하고 발사 때 실제 위치·고도·벽·생존을 다시 확인한다.
- 대상별 피해1회. 시전 필세·화속성 강화 스냅샷, 출처·공통 접촉 경로를 사용하며 그로기/5속성 완주를 대신 쌓지 않는다.
- 곰↔놈은 동일 비용·유효 배치 거래로 교체한다. 새 소환 실패는 기존 소환과 공격을 지우지 않는다. 활동20초 뒤 소멸하며 기존 나머지 정적 소환은 보존한다.
- 기존 KTP FlameCone의10개 PS를 재사용한다. 원본은 수정하지 않고 인스턴스의 배치·크기·방출 시간을 공유 공격 시계로 제어한다. 기존 숯빛/불씨 셰이더는 별도 전투 사본에서 표시 시간과 불씨 운동 시간을 분리했다.
- 기존 FormationSparks·ManeEmbers·DissolveAsh/Sparks를 실제 등장·활동·소멸 단계로 분리 재생한다. 메뉴 정지 시 별도 자동 PS 시계가 계속 흐르지 않는다.

## 리깅과 실제 수치

Unity {audit['tris']:,}tris, {audit['skins']}스킨 메시, {audit['materials']}재질, {audit['bones']}본. 최대{audit['maximumInfluences']}가중치, 무가중{audit['unweighted']}, 정규화 최대오차{audit['weightError']:.3g}. 원본UV와 승인 원형 바인드 표면은 보존했다.

| 클립 | 길이 | 적용 |
|---|---:|---|
| FH_Idle | 2초 | 네 발 중립 대기 |
| FH_WalkForward | 0.6초 | 전진/접근 |
| FH_WalkBackward | 0.6초 | 적을 보며 후퇴 |
| FH_FlameAttack | 1.6초 | 준비→분사→회복 |

명목 이동속도0.9230769m/s 기준으로 실제 이동거리에서 재생 시간을 구한다. Root Motion은 사용하지 않는다. 기존 비대칭 앞발을 원본 메시 삭제·변경 없이 본 자세로 내렸다. 원래 발바닥 일부는 지면과 가까웠으므로 단순한 공중 발 문제로 단정하지 않았다.

Blender/FBX 왕복 최대 표면오차1.16µm. 평면 위 중립/화염 실제 발바닥 약4mm, 반 프레임 보행 지지 발3.27~10.01mm. 이는 Unity의 모든 경사면 검사 결과가 아니다. 관절 주변 각진 스키닝과 실제 지형 접지 보정은 남아 있다.

## 검사 결과

| 검사 | 상태 | 범위 |
|---|---|---|
| 분리 전투·시간·PS·교체 | 통과 {len(tests['passed'])}개 | 실제 manager와 공통 피해 경로, 소유 PhysicsScene/NavMesh |
| Unity 리그/프로필 | 통과 {len(audit['passed'])}개 | 실제 FBX 임포트와 씬 참조 |
| 실제 데모 Play 진단 | 통과 {len(run['passed'])}개 | 고정한 기존 벌목장 적에게 진단 시전 |
| 실제 손글씨/이동 적 전투 | 미검증 | API 진단과 구분 |
| 최종 외형·경사 발 접지 | 미완료/미검증 | 수치 통과로 미술 승인을 대체하지 않음 |
| 실제120fps/출력 장치 SFX | 미검증 | 이번 수치로 성능·음향 합격을 주장하지 않음 |

실제 Play에서 먹{run['inkBefore']:.2f}→{run['inkAfter']:.2f}, 위력 스냅샷{run['flameDamageSnapshot']:.2f}, 양수피해{run['hits']}회/누적{run['damageApplied']:.0f}, 경로실패{run['pathFailures']}회, 활동20초 후 외형 잔존0을 확인했다. 30/60/120fps와0.25배속은 해당dt를 공급한 분리 검사이며 실제 렌더 FPS가 아니다.

기존 곰 뿌리41검사와 공통관리22검사도 통과했다. 기존 사용자 저장·시험 suffix·GameView 해상도를 복원했다. 순차1080p 캡처 최고 커밋{max(captures)*100:.2f}%.

## 남은 문제와 다음 순서

1. 화염의 밝은 덩어리감이 강하고, 플레이 시점에서는 소환수와 플레이어가 겹친다. 최종 미술은 승인하지 않았다.
2. 원본 FlameCone 속도·수명과 전체 확대 조합에서 일부 입자 중심이 약4.99m까지 갈 수 있다.4.5m 피해 경계 밖의 빌보드/입자 가시 범위는 미검증이며 실제 경계 계측 후 보완해야 한다. 현재 검사34개는 입자 외곽과 벽 뒤 시각 표현을 증명하지 않는다.
3. 해태 실제 지형 발 접지, 이동 적에 대한 거리 유지 체감, 곰의 접지감/뿌리 가시성을 보완한다. 이후 솜→몸→옴 전투로 진행한다.
4. 전체 데모의 심부/청룡·국/仁·호송·검문·남문·최종 통합 완주는 별도 미완료다. 소환2종의 첫 연결을 전체 완성으로 세지 않는다.

산출물: FireHaetae_Combat.blend, SM_FireHaetae_Combat.fbx, Unity전용프리팹/프로필/재질/셰이더, 원본텍스처참조, 제작·왕복JSON, 외부/플레이1920×1080정지화면. Blender 회색 진단 이미지는 별도이며 실제 Unity 화면을 대체하지 않는다.
'''
(out/'REPORT.md').write_text(report,encoding='utf-8')
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><title>놈 — 전투 첫 연결</title><meta name="viewport" content="width=device-width,initial-scale=1">
<style>body{{background:#201d19;color:#e9dfcf;font:17px/1.75 system-ui;margin:0}}main{{max-width:1280px;margin:auto;padding:32px 24px}}a{{color:#e0bc8b}}img{{width:100%}}figure{{margin:30px 0}}.note{{padding:20px;background:#393027;border-left:4px solid #c19461}}figcaption{{color:#bfb09f}}</style><main>
<p>오행부 · 데모 · 놈 / 화 해태</p><h1>거리를 유지하며 짧은 화염 공격</h1>
<p class="note">리깅과 실제 전투 첫 연결입니다. 고정 적에 대한 Play 진단이며 수동 전투·최종 미술 승인·성능 검증과 구분합니다.</p>
<p>기존12,532삼각형·1재질에21본과 전진·후퇴·분사 모션을 추가했습니다. 가까워진 적을 보며 물러나고, 발사 순간 고정된 부채꼴 안의 적에게 한 번씩 피해를 줍니다.</p>
<figure><img src="nom_flame_external_v1_1920x1080.png" alt="해태 화염 외부 시점"><figcaption>실제 벌목장 · 명중 직후 외부 시점. 화염의 덩어리감·시각 범위는 보완 대상입니다.</figcaption></figure>
<figure><img src="nom_flame_player_v1_1920x1080.png" alt="해태 화염 플레이 시점"><figcaption>같은 순간의 플레이 카메라. 플레이어와 소환수의 겹침이 남아 있습니다.</figcaption></figure>
<p>기술{len(tests['passed'])}개·리그{len(audit['passed'])}개·실제Play진단{len(run['passed'])}개 통과. 시전 먹 소비·피해{run['hits']}회·자연 소멸을 확인했습니다.</p>
<p class="note">미완료: 화염4.5m 경계와 빌보드 실제 외곽, 실제 경사 접지·관절 미술·수동 입력/다수전·120fps. 다음 세 소환수와 후반 데모는 계속 제작합니다.</p>
<p><a href="REPORT.md">상세 보고서</a> · <a href="../flame_tests.json">기술 검사</a> · <a href="../runtime_flame_tests.json">실제Play원장</a> · <a href="rig_report.json">리그 원장</a> · <a href="fbx_verification.json">FBX왕복</a> · <a href="../RootAttack/REVIEW.html">곰 결과</a></p></main></html>'''
(out/'REVIEW.html').write_text(page,encoding='utf-8')
print(out/'REVIEW.html')
