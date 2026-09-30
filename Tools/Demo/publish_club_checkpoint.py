"""Publish verified club combat derivatives without committing or building the game."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parents[2]
base = root / 'Art/Demo/Summons'
out = base / 'DokkaebiClub'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
run = read(base / 'runtime_club_tests.json')
rules = read(base / 'club_tests.json')
audit = read(base / 'club_scene_audit.json')
restore = read(root / 'Art/Demo/Chapter2/runtime_tests.json')
assert run['status'] == 'PASS_RUNTIME_API' and not run['failed']
assert rules['status'] == 'PASS' and not rules['failed']
assert audit['status'] == 'PASS' and not audit['failed']
assert restore['suffixRestored'] and not restore['active'] and not restore['holdingPlay']
assert read(out / 'fbx_verification.json')['status'] == 'PASS'
for name in ('runtime_club_tests.json', 'club_tests.json', 'club_scene_audit.json', 'compile.json', 'compile.log'):
    shutil.copy2(base / name, out / name)
shots = [('mom_club_external_v1', '외부 시점 — 실제 명중 후 회복 자세'),
         ('mom_club_player_v1', '플레이 시점 — 플레이어에 의한 부분 가림이 남음')]
for name, _ in shots:
    shutil.copy2(root / f'Art/Demo/Foundation/{name}_1920x1080.png', out / f'{name}_1920x1080.png')
report = f'''# 몸 — 몽둥이 전투 첫 연결

최종 미술 승인 전의 전투 파생본이다. 승인된 뿔 없는 도깨비의 원형·UV·텍스처를 보존하고, 같은 메시를 21본·대기/보행/공격 3클립으로 연결했다. 원형과 파생본 모두 16,653tris·1재질이다. 새 Meshy 생성이나 플레이어 빌드는 없다.

## 동작과 판정

0.8초 준비 → 0.3초 동안 왼쪽에서 오른쪽으로 140도 휘두르기 → 0.6초 회복. 몽둥이 끝을 Blender에서 측정한 반경 약1.3m와 평균 높이0.8505m를 사용한다. 높이 허용0.65m는 게임 판정 여유로 설정한 값이며 몽둥이 두께를 측정한 값이 아니다. 발자국 폭·길이에 총0.06m, 몸 높이에0.05m 여유를 포함했다.

공격 시작 시 대상·생명주기·각도별 접촉 시각을 고정한다. 접촉 때 같은 생명인지, 원래 각도에서8도 이내인지, 거리·높이·차폐가 유효한지 재검사한다. 늦게 들어온 적, 죽었다 부활한 적, 벽 뒤 대상에는 예약 피해를 주지 않는다. 실제 적의 중심 좌표에 높이를 다시 더하던 오류를 수정했다. 충돌체가 있으면 실제 중심, 없으면 적의 중심 루트를 사용한다.

## 검사

| 항목 | 결과 | 근거·한계 |
|---|---|---|
| 규칙 검사 | 통과 | {len(rules['passed'])}개. 시간·다수 대상·중복·생명주기·차폐·취소·실측 사거리. 30/60/120은 dt 조건이며 실제 FPS 측정이 아님 |
| Unity 리그·클립 | 통과 | {len(audit['passed'])}개. 1스킨·21본·정점당 최대4웨이트, 정상화 오차2.384e-7 |
| FBX 왕복 | 통과 | 표본 자세에서 최대 표면 오차1.28935e-5m |
| 실제 데모 Update | 통과 | {len(run['passed'])}개. 진단 시전·기존 정지 적, 첫 명중24피해, 먹1.00→0.70, 경로 실패0, 활동20초 뒤 객체 정리 |
| 저장 보호 | 통과 | UUID 검사 저장만 사용, 이전 슬롯·Play·캡처 크기 복원 |
| 어깨·손목 최종 외형 | 실패(미완료) | 큰 어깨 장식의 압축·각진 변형. 시도한 웨이트 변경은 원형을 더 훼손하여 되돌림 |
| 플레이 시점 가시성 | 실패(보완 필요) | 플레이어가 몸 일부를 가린다. 외부 시점과 함께 공개 |
| 실제 지형 발 접지 | 미검증 | 지면과 발 사이 거리·동작 중 미끄러짐을 실측하지 않음. 스크린샷만으로 통과 판정하지 않음 |
| 실제 몽둥이 접촉 프레임 | 미검증 | 스크린샷은 명중 후 회복 자세. 뼈 끝의 공격 궤도와 정확한 피부 접촉은 별도 검사 필요 |
| 수동 전투·성능 | 미검증 | 직접 작도·이동 적 AI·8.33ms·수동 완주는 실행하지 않음 |

1080p 게임 스크린샷2장을 순차 캡처하고 직접 열어 확인했다. Blender 작업 화면5장은 형태 참고이며 Unity 검증을 대체하지 않는다. 원장 상태 문자열은 해당 단계의 기록이고 최종 연결 상태는 이 보고서와 runtime_club_tests.json을 따른다. 다음은 옴이며 후반 캠페인·보스와 전체 통합 검증은 계속 제작한다.
'''
(out / 'REPORT.md').write_text(report, encoding='utf-8')
figures = ''.join(f'<figure><img src="{n}_1920x1080.png" alt="{c}"><figcaption>{c}</figcaption></figure>' for n, c in shots)
(out / 'REVIEW.html').write_text(f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>몸 — 몽둥이 전투</title>
<style>body{{background:#1d201c;color:#e9e3d2;font:17px/1.8 system-ui;margin:0}}main{{max-width:1240px;margin:auto;padding:32px 24px}}img{{width:100%;display:block}}figure{{margin:30px 0}}a{{color:#c4d9ba}}.notice{{padding:22px;background:#35372c}}figcaption{{color:#c6c1b4}}</style><main>
<p>오행부 · 데모 소환수 몸</p><h1>몽둥이를 들고 접근해 횡으로 휘두르는 도깨비</h1>
<p class="notice">진단 시전 · 실제 게임 Update · 정지시킨 기존 적의 결과입니다. 최종 외형·수동 전투·성능 승인은 미검증입니다. 어깨 변형과 플레이어 가림은 보완 항목입니다.</p>
<p>16,653tris · 1재질 · 21본 · 3클립. 첫 명중24피해, 기본 공격2회분 먹 소비, 20초 활동 후 소멸을 확인했습니다.</p>{figures}
<p>규칙 {len(rules['passed'])} · Unity 참조 {len(audit['passed'])} · 실제 Play {len(run['passed'])}검사 통과. 실제 사거리 약1.3m. 검사 영상과 새 빌드는 제작하지 않았습니다.</p>
<p><a href="REPORT.md">기술 보고서·잔여 문제</a> · <a href="DokkaebiClub_Combat.blend">Blender</a> · <a href="club_tests.json">규칙 검사</a> · <a href="runtime_club_tests.json">실제 Play 검사</a> · <a href="../MetalTiger/REVIEW.html">이전 솜</a></p></main></html>''', encoding='utf-8')
print(f'Published {out}: rules {len(rules["passed"])}, runtime {len(run["passed"])}; provisional art.')
