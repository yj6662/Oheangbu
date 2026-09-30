"""Assemble the first combat-deer review from measured artifacts; never generate a player build."""
from pathlib import Path
import hashlib
import html
import json
import shutil

root = Path(__file__).resolve().parents[2]
out = root / 'Art/Demo/Summons'
def read(name):
    return json.loads((out / name).read_text(encoding='utf-8-sig'))

runtime = read('runtime_tests.json')
scene = read('scene_audit.json')
compile_result = read('compile.json')
chapter = json.loads((root / 'Art/Demo/Chapter2/runtime_tests.json').read_text(encoding='utf-8-sig'))
assert runtime['status'] == 'PASS_RUNTIME_API'
assert chapter['suffixRestored'] and not chapter['active'] and not chapter['holdingPlay']
assert not chapter['failures']
assert all(a['exitCode'] == 0 for a in compile_result['results'])

for camera in ('player', 'external'):
    stem = f'gom_horn_{camera}_v1'
    for tail in ('_1920x1080.png', '_capture.json'):
        shutil.copy2(root / 'Art/Demo/Foundation' / (stem + tail), out / (stem + tail))

preserved = read('legacy_preservation.json')
assert all(item['unchanged'] for item in preserved)
rig = read('WoodDeer/rig_report.json')
assert hashlib.sha256(Path(rig['source']).read_bytes()).hexdigest() == rig['sourceSha256Before']
shutil.copy2(root / 'Art/Demo/Chapter2/runtime_tests.json', out / 'save_isolation_and_restore.json')

checks = [('clock_tests.json', '설정·수명·공격 시계'), ('damage_tests.json', '공통 피해·출처·차폐'),
          ('manager_tests.json', '배치·비용·교체·길찾기'), ('scene_audit.json', 'Unity 리그·메시·참조'),
          ('runtime_tests.json', '실제 Play API 접근·공격·소멸')]
rows = []
for file, label in checks:
    data = read(file)
    rows.append((file, label, len(data['passed']), len(data['failed'])))

report = f'''# 곰 전투 연결 — 첫 구현 체크포인트

기존 목 사슴에 실제 리그와 이동·뿔 공격을 추가해 **전용 데모 씬**에 연결했다. 전체 데모나 곰의 전체 전투 역할을 완료한 상태는 아니다. 뿌리 공격, 경사면 발 접지와 나머지 네 소환수 전투는 후속 작업이다.

## 구현

- 기존 몸·뿌리·잎 외형을 보존한 별도 Blender/FBX 파생본. 21본(변형 18본), 재질 3개, Root Motion OFF.
- Idle·Walk·HornAttack을 공통 소환 시계로 재생한다. 발 이동 거리로 걷기 위상을 조절하고 공격 후 복귀 동작까지 재생한다. RootCast 클립은 제작됐으나 아직 전투에 연결하지 않았다.
- 등장 1.2초 → 활동 20초 → 소멸 0.8초. 비용은 기본 공격 2회분이며 준비·배치 실패는 기존 소환과 먹을 보존한다.
- 락온 우선 선택, 접근·공격·추종·복귀, 월드 지면과 NavMesh 경로를 사용한다. 소환 본체에는 통행·피격을 막는 충돌체가 없다.
- 시전 위력과 속성 강화값을 생성 시 한 번 저장한다. 피해는 공통 접촉 경로를 사용하며, 소환 공격은 패링 그로기·플레이어 5속성 완주를 쌓지 않는다.
- 기존 4.6초 정적 소환과 이중 생성되지 않도록 소유권을 분리했다. 다른 네 소환수는 기존 정적 표현을 유지한다.

## 실제 발견·수정한 문제

첫 실제 지형 검사에서는 피해 0회·경로 실패 56회를 기록했다. 적 루트가 지면보다 0.875m 위에 있는데 이를 NavMesh 종점으로 사용해 경로를 얻지 못했다. 대상 루트를 해당 씬 지면에 투영한 뒤 경로를 구하도록 수정했다. 실패 원장은 `runtime_before_ground_target_fix.json`에 보존한다.

회전 시 최종 방향만 공간 검사하던 부분도 수정했다. 실제 적용할 회전량과 그 중간 회전에서 몸 전체의 공간을 검사한다. 양 끝 자세는 통과하지만 중간 자세가 기둥과 겹치는 경우를 별도 검사했다.

## 검사 결과

| 항목 | 통과 | 실패 | 실행 범위 |
|---|---:|---:|---|
'''
for file, label, passed, failed in rows:
    report += f'| [{label}]({file}) | {passed} | {failed} | ' + ('실제 데모 Play, 진단 시전·고정 적' if file == 'runtime_tests.json' else '분리된 기술 검사') + ' |\n'
report += f'''
오프라인 소스 컴파일 {len(compile_result['results'])}개 어셈블리/{sum(a['sources'] for a in compile_result['results'])}파일 오류 0. 플레이어 빌드를 생성한 결과가 아니다.

실제 Play에서 먹 {runtime['inkBefore']:.2f}→{runtime['inkAfter']:.2f}, 피해 스냅샷 {runtime['damageSnapshot']:.1f}, 양수 명중 {runtime['hits']}회, 누적 실제 피해 {runtime['damageApplied']:.2f}를 확인했다. 검사 적의 HP는 60이며 마지막 피해는 남은 HP로 제한된다. 경로 계산 {runtime['pathQueries']}회/실패 {runtime['pathFailures']}회. 활동 종료 후 소환 외형 잔존 0을 확인했다. 보고된 마지막 actorAge {runtime['actorAge']:.6f}초는 객체 삭제 직전 관측값이며 정확한 수명 경계 자체는 시계 검사에서 확인했다.

일반 Play Update와 실제 피해 경로를 실행했지만, 시전은 진단 도구가 ResolveSummon을 호출했고 대상 AI는 고정했다. 손글씨 입력·움직이는 적과의 수동 전투·완주를 검증한 것으로 해석하지 않는다.

## 실제 메시 수

| 파츠 | Blender/FBX 왕복 tris | Unity 임포트 tris |
|---|---:|---:|
| 몸 | 12,543 | 12,543 |
| 뿌리 | 3,864 | 3,860 |
| 잎 | 184 | 184 |
| 합계 | 16,591 | {scene['tris']:,} |

18,000 tris 목표 상한 이내. 요청/목표 수치와 실제 임포트 수치를 구분한다. 뿌리의 4삼각형 차이는 관측값이며 원인은 미확정이다. Unity 정점당 최대 {scene['maxInfluences']}웨이트, 무웨이트 정점 {scene['unweightedVertices']}, 최대 정규화 오차 {scene['maxWeightSumError']:.9g}. Blender의 평면 발 접촉 결과는 Unity 경사면 접지 통과로 취급하지 않는다.

## 화면과 보존

- 플레이·외부 시점 PNG 각 1장, 1920×1080. 실제 데모 Game View의 첫 공격 직후 정지 화면이다. 영상 없음.
- 기존 WoodDeer 파일 {len(preserved)}개 비교 및 원본 Blender 해시 일치. 새 재질·셰이더와 모델은 파생 경로에 저장했다.
- 독립 UUID 검사 슬롯을 사용했다. 검사 종료 후 Play·기존 저장 suffix·백그라운드 실행 설정·Game View 선택을 복원했다. 저장 보존 원장은 `save_isolation_and_restore.json`에 있다.
- 신규 유료 생성·플레이어 빌드·커밋·PR 생성 없음.

## 남은 작업과 미검증

**미완료:** 곰의 짧은 직선 뿌리 공격, 지형별 발 접지 보정, 나머지 네 소환수의 전투. 후속은 곰 역할을 마무리한 뒤 놈→솜→몸→옴 순서다.

**미검증:** 손글씨 시전·실제 락온 조작, 움직이는 적 AI와의 전투, 지형 경사에서 발 미끄러짐·관통, 차량 탑승/사망 이벤트의 실제 플레이 통합, 최종 외형·모션 품질, 이동 중 CPU/GPU 및 120fps. 정지 화면에서 발이 지면과 떨어져 보이는 구간은 지지 발/공격 자세를 수치로 구분해 후속 검사한다.

전체 데모의 보스·국/仁·호송·검문·남문 완료와 수동 플레이 시간 측정도 아직 남아 있다.
'''
(out / 'REPORT.md').write_text(report, encoding='utf-8')

table = ''.join(f'<tr><td><a href="{f}">{html.escape(label)}</a></td><td>{passed}</td><td>{failed}</td></tr>' for f,label,passed,failed in rows)
page = f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>곰 — 전투 연결 첫 체크포인트</title><style>body{{margin:0;background:#171a17;color:#e5e2d6;font:17px/1.7 system-ui,sans-serif}}main{{max-width:1250px;margin:auto;padding:36px 24px}}h1{{font-size:32px}}h2{{margin-top:44px}}a{{color:#b4d4bd}}img{{width:100%;display:block;border-radius:8px}}figure{{margin:28px 0}}figcaption,.sub{{color:#b7bbae}}table{{width:100%;border-collapse:collapse}}td,th{{text-align:left;padding:10px;border-bottom:1px solid #455044}}.notice{{padding:20px;border-left:4px solid #cbb887;background:#282d25}}</style>
<main><p class="sub">오행부 · 데모 구현 · 곰 / 목 사슴</p><h1>정적 소환에서 접근·뿔 공격으로</h1>
<p class="notice">전투 동작 첫 체크포인트. 뿌리 공격과 경사면 발 접지는 미완료입니다. 실제 데모 Play에서 진단 시전과 고정 적으로 검사했으며 수동 전투·최종 미술 승인 자료와 구분합니다.</p>
<p>기존 목질 사슴·가지뿔·잎 목걸이에 21본 리그를 추가했습니다. 등장 1.2초, 활동 20초, 소멸 0.8초. 실제 먹 소비·피해·중복 방지·소멸 정리를 연결했습니다.</p>
<figure><img src="gom_horn_external_v1_1920x1080.png" alt="곰 뿔 공격 직후 외부 시점"><figcaption>외부 시점 · 첫 뿔 명중 직후 · 실제 벌목장 지형, 정지시킨 기존 적. 발 접지와 공격 자세의 시각 품질은 후속 보완 대상입니다.</figcaption></figure>
<figure><img src="gom_horn_player_v1_1920x1080.png" alt="곰 뿔 공격 직후 플레이 시점"><figcaption>플레이 시점 · 같은 공격 · 플레이어와 소환수의 가림 관계를 확인할 수 있습니다.</figcaption></figure>
<h2>기술 검사</h2><table><tr><th>검사</th><th>통과</th><th>실패</th></tr>{table}</table>
<p>실제 Play: 명중 {runtime['hits']}회, 누적 피해 {runtime['damageApplied']:.2f}, 경로 실패 0, 수명 종료 뒤 외형 잔존 0. 비용은 기본 공격 2회분. 정지 화면을 FPS 달성 근거로 사용하지 않습니다.</p>
<p>Blender/FBX 16,591 tris · Unity {scene['tris']:,} tris · 3재질. 뿌리 임포트 4삼각형 차이를 원장에 분리 기록했습니다.</p>
<h2>다음 작업</h2><p>곰의 짧은 직선 뿌리 공격과 지형 접지를 마무리한 뒤 놈·솜·몸·옴을 순서대로 연결합니다. 수동 입력·움직이는 적·사망/탑승 통합·성능은 미검증입니다.</p>
<p><a href="REPORT.md">전체 보고서</a> · <a href="WoodDeer/WoodDeer_Combat.blend">편집용 Blender</a> · <a href="../../../Oheangbu/Assets/_Project/Art/Demo/Summons/WoodDeer/SM_WoodDeer_Combat.fbx">파생 FBX</a> · <a href="save_isolation_and_restore.json">저장 보존·검사 상태 복원</a> · <a href="../Chapter2/REVIEW.html">역참·객주·벌목장 구현</a></p></main></html>'''
(out / 'REVIEW.html').write_text(page, encoding='utf-8')
print('Published', out / 'REVIEW.html')
