"""Publish the first combat tiger checkpoint with explicit visual limitations."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parents[2]
base = root / 'Art/Demo/Summons'
out = base / 'MetalTiger'
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

run = read(base / 'runtime_tiger_tests.json')
checks = read(base / 'tiger_tests.json')
audit = read(base / 'tiger_scene_audit.json')
restore = read(root / 'Art/Demo/Chapter2/runtime_tests.json')
assert run['status'] == 'PASS_RUNTIME_API' and not run['failed']
assert checks['status'] == 'PASS' and not checks['failed']
assert audit['status'] == 'PASS' and not audit['failed']
assert restore['suffixRestored'] and not restore['holdingPlay'] and not restore['active']
for name in ('runtime_tiger_tests.json', 'tiger_tests.json', 'tiger_scene_audit.json',
             'root_tests.json', 'flame_tests.json', 'manager_tests.json', 'compile.json', 'compile.log'):
    source = base / name
    assert source.is_file(), source
    shutil.copy2(source, out / name)
shutil.copy2(root / 'Art/Demo/Chapter2/runtime_tests.json', out / 'save_restore.json')
shots = [('som_leap_external_v1', '도약 중 · 외부 시점'),
         ('som_claws_external_v1', '좌우 발톱 피해 후 · 외부 시점'),
         ('som_claws_player_v1', '같은 순간 · 플레이 카메라')]
for name, _ in shots:
    for suffix in ('_1920x1080.png', '_capture.json'):
        shutil.copy2(root / 'Art/Demo/Foundation' / (name + suffix), out / (name + suffix))
    cap = read(out / (name + '_capture.json'))
    assert cap['commitRatio'] < .85 and (cap['width'], cap['height']) == (1920, 1080)

report = f'''# 솜 — 금속 호랑이 전투 첫 연결

승인된 기존 호랑이의 별도 사본을 리깅했다. 원본 모델·재질·UV를 보존하고 신규 유료 생성은 하지 않았다. 정적 등장 시안에서 접근·도약·좌우 발톱·자연 소멸까지 연결한 첫 구현이다. 최종 모델·동작·접지의 미술 승인을 의미하지 않는다.

## 구현

- Unity 실제 {audit['tris']:,}삼각형, {audit['materials']}재질, {audit['bones']}본(변형20본). 최대4웨이트, 미할당0, 정상화 최대오차 {audit['weightError']:.3g}.
- 대기2초·걷기0.6초·달리기0.4초·도약1.2초·좌/우 발톱 각0.8초. 60Hz 베이크, Root Motion 비활성. 접근 이동은 관리자가 담당한다.
- 기본 추종2m/s, 공격 접근4m/s. 3~5m 대상은 고정 지면 경로로 도약하고 약2m 앞에 착지한다. 준비0.2초, 착지0.8초, 도약 클립 종료1.2초. 공중 골반 상승은 최대0.38m이며 별도의 피해는 없다.
- 이후 왼발과 오른발 공격을 순서대로 재생하며 각 클립0.3초에 시전 스냅샷의50% 피해를 한 번씩 준다. 명중마다 다른 공격 ID로 기존 공통 피해 경로를 사용한다.
- 경로·대상 생명주기·공격 시각은 시작에 고정한다. 지면 단절·벽·낮은 천장·대상 사망·이탈·소환 교체에서는 남은 공격을 취소한다. 갑작스러운 취소 때 자세가 떨어지는 표현은 후속 보완 대상이다.
- 등장1.2초/활동20초/소멸0.8초와 비용 기본 공격2회분, 동시1체 규칙을 유지한다. 소환 공격은 직접 공격의 그로기·5속성 완주를 대신하지 않는다.

## 검사 결과

| 항목 | 결과 | 근거와 한계 |
|---|---|---|
| 규칙·경로·시간·취소 | 통과 | {len(checks['passed'])}검사. 격리 물리/NavMesh와 대체 외형 검사 포함. 30·60·120fps/감속은 공급한 시간 간격이며 렌더 성능 측정이 아님 |
| Unity 리그·클립·참조 | 통과 | {len(audit['passed'])}검사. 실제 FBX/프리팹/프로필 |
| 실제 데모 Play | 통과 | {len(run['passed'])}검사. 진단 시전, 실제 Update, 정지시킨 기존 적. 수동 작도·적 AI 전투 검증 아님 |
| 실제 피해·비용 | 통과 | 먹1.00→0.70, 좌우 각각10피해, 첫 연속 공격 총20. 도약 중 피해0. 이후 자연 수명 종료와 잔존0 확인 |
| 회귀 | 통과 | 곰 뿌리41·놈 화염35·공통 관리자22검사 |
| 소스 컴파일 | 통과 | 9어셈블리654소스, 오류0. 새 플레이어 빌드가 아님 |
| 저장·검사 종료 | 통과 | 격리 슬롯, 이전 진단 접미사·Play·캡처 크기 복원 |
| 외형 최종 완성 | 실패(미완료) | 팔꿈치/손목의 각진 변형과 어깨 압축, 달리기의 고양잇과 질주감 부족. 기존 비대칭 원형에서 파생 |
| 실제 경사 접지·발톱 접점 | 미검증 | Blender 평면 검사와 실제 지형 접촉을 구분. 화면은 피해 후0.18초 지점으로 정확한 발톱 접촉 프레임의 증거가 아님 |
| 플레이 시점 가시성 | 실패(보완 필요) | 플레이어가 소환수를 가리는 구도 확인. 외부 시점과 함께 공개 |
| 수동 전투·성능·전체 데모 | 미검증 | 직접 입력, 이동 적, CPU/GPU 8.33ms, 보스/후반 콘텐츠는 후속 |

## 전달물과 잔여 작업

`MetalTiger_Combat.blend`, 파생 `SM_MetalTiger_Combat.fbx`, `PF_MetalTiger_Combat.prefab`, `Combat_Som.asset`, 실제1080p 스크린샷3장, Blender 원장과 검사 JSON을 남겼다. 영상·자동 보행·추가 빌드는 만들지 않았다. 캡처3장을 실제로 열어 비검정 화면과 위 가림 문제를 확인했다.

전투 첫 연결은 곰·놈·솜3종이다. 몸·옴, 청룡 이후 콘텐츠, 실제 전투와 성능 검증은 계속 구현한다. 원형/리그 검사 상세는 `rig_report.json`, `native_motion_verification.json`, `fbx_verification.json`을 참조한다. Blender 보고서의 단계별 상태 문자열은 당시 검사 시점을 보존하며 최종 Unity 상태는 `tiger_scene_audit.json`과 `runtime_tiger_tests.json`이다.
'''
(out / 'REPORT.md').write_text(report, encoding='utf-8')
figures = ''.join(f'<figure><img src="{name}_1920x1080.png" alt="{caption}"><figcaption>{caption}</figcaption></figure>' for name, caption in shots)
html = f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>솜 — 도약과 좌우 발톱</title>
<style>body{{background:#191d1c;color:#e7e2d5;font:17px/1.75 system-ui,sans-serif;margin:0}}main{{max-width:1250px;padding:36px 24px;margin:auto}}a{{color:#b6d8d3}}img{{width:100%;display:block}}figure{{margin:32px 0}}figcaption{{color:#bbbbae}}.notice{{padding:22px;background:#30332c;border-left:4px solid #c5ae75}}li{{margin:8px 0}}</style><main>
<p>오행부 · 데모 전투 소환 · 솜</p><h1>고정 경로 도약 → 왼발 → 오른발</h1>
<p class="notice">진단 시전·실제 Play API 검증입니다. 기존 적의 이동을 정지하고 촬영했습니다. 수동 전투와 최종 외형 승인은 미검증이며, 경사 접지·각진 관절·플레이어에 의한 가림은 보완이 필요합니다.</p>
<p>기존 호랑이 12,414tris·1재질·25본. 도약 중 피해 없이 착지한 뒤 좌우 발톱이 각각 피해를 줍니다. 실제 먹 소비·대상 생명주기·활동20초·소멸 정리를 연결했습니다.</p>
{figures}<h2>검사와 한계</h2><ul><li>전투 규칙38·Unity 참조8·실제 Play15검사 통과.</li><li>첫 좌우 공격 각각10피해. 명중2회·총20. 경로 실패0, 수명 종료 후 잔존0.</li><li>Blender 평면 접지 수치와 실제 경사 지형 접지를 동일하게 취급하지 않았습니다.</li><li>발톱 그림은 명중 후 자세입니다. 정확한 피부 접점, 달리기의 자연스러움, 화면 가림은 추가 수정 대상입니다.</li><li>캡처는1080p 정지3장만 순차 진행했습니다. 새 빌드 없음.</li></ul>
<p><a href="REPORT.md">기술 보고서</a> · <a href="tiger_tests.json">규칙 검사</a> · <a href="runtime_tiger_tests.json">실제 Play</a> · <a href="tiger_scene_audit.json">Unity 통계</a> · <a href="MetalTiger_Combat.blend">Blender 원본</a></p>
<p><a href="../FireHaetae/Boundary/REVIEW.html">놈 화염 범위 보정</a> · <a href="../RootAttack/REVIEW.html">곰 뿌리 공격</a> · <a href="../../Chapter2/REVIEW.html">현재 데모 콘텐츠</a></p></main></html>'''
(out / 'REVIEW.html').write_text(html, encoding='utf-8')
print(f'Published {out}; actual Play {len(run["passed"])} checks, provisional art.')
