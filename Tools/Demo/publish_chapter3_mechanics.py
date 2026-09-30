"""Archive verified mechanics without enabling unfinished campaign stages or building a player."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parents[2]
out = root / 'Art/Demo/Chapter3'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
growth = read(out / 'growth_tests.json')
ren = read(out / 'ren_tests.json')
lesson = read(out / 'lesson_tests.json')
for data in (growth, ren, lesson):
    assert data['status'] == 'PASS'
runtime = read(root / 'Art/Demo/Chapter2/runtime_tests.json')
assert runtime['status'] == 'PASS_API_INTEGRATION' and runtime['suffixRestored'] and not runtime['holdingPlay']
for source, name in [
    ('Art/Demo/Chapter2/runtime_tests.json', 'previous_chapter_runtime.json'),
    ('Art/Demo/Chapter2/interaction_tests.json', 'interaction_regression.json'),
    ('Art/Demo/Chapter2/checkpoint_tests.json', 'rest_regression.json'),
    ('Art/Demo/Foundation/combat_tests.json', 'combat_regression.json'),
    ('Art/Demo/Summons/manager_tests.json', 'summon_regression.json'),
    ('Art/Demo/Summons/compile.json', 'compile.json')]:
    shutil.copy2(root / source, out / name)
report = f'''# 심부·청룡·仁 — 전투와 저장 기반 구현

청룡 전체 또는 심부 플레이 구간의 완료 보고가 아니다. 이 단계에서 검증한 것은 청룡의 생장 회복 상태, 실제 금 명중 취소, 仁의 저장을 동반한 치명타 생존, 학습 완료의 저장 경로다. 현장 적·보스·발판을 배치하지 않았고 캠페인 다음 단계는 활성화하지 않았다.

## 구현

- 청룡 생장: 살아 있는 상태에서 HP50% 이하가 되면4초 동안 한 번 준비한다. 준비 중 실제 양수 피해가 확정된 금 공격은 회복을 취소한다. 플레이어와 소환수 금 공격이 모두 해당한다. 준비를 시작시킨 첫 타격까지 소급해 취소하지 않는다. 마감 시각의 금 타격은 늦은 것으로 처리하고, 치명타는 회복보다 우선한다. 실패하면 최대 HP로 회복하고2페이즈로 간다. 생명주기가 바뀌기 전에는 재시도하지 않는다.
- 기존 EnemyVitals.Restore와 별개로 제한된 Heal을 추가했다. 생명주기·그로기·급소창을 초기화하지 않고 죽은 적을 부활시키지 않는다.
- 심부 학습: GrowthInterrupted 이벤트를 추가했다. 실제 중단 상태의 컴포넌트, 현재 세션·장면·등록된 조우의 소유 관계를 검사한다. 일반 상호작용으로 학습을 완료할 수 없다. 진행·통보 보상은 분리된 저장 제안을 통해 한 번만 반영한다. 저장 실패 시 기존 진행은 유지한다.
- 仁: 도감의 실제 보유 상태를 읽는다. 치명타 처리 전에 사용 상태와 HP1을 저장한 뒤 생존을 허용한다. 저장 실패 시 일반 사망 처리를 따른다. HP회복·사망 재시도는 충전하지 않고, 저장에 성공한 휴식만 재충전한다. 낙사 규칙은 유지한다.
- 저장 v5의 renUsed를 추가했고 v4의 기존 위치·체력·먹·통보·드롭·조사·도감·보스 상태를 보존한다. 청룡 보상으로 仁를 주는 경로는 아직 연결하지 않았다.

## 검증

| 항목 | 판정 | 근거·범위 |
|---|---|---|
| 생장 상태·실제 EnemyVitals | 통과 | {len(growth['passed'])}개. 준비시간·반복·중단·죽음·비활성·회복·생명주기, 직접/소환 금 명중 |
| 仁와 실제 파일 저장 | 통과 | {len(ren['checks'])}개. HP1·중복 방지·저장 실패·이전 HP 덮어쓰기 방지·휴식 실패/성공·재로딩·v4이행 |
| 학습 진행 저장 | 통과 | {len(lesson['passed'])}개. 순서·일반 상호작용 차단·보상 제안·실제 저장 실패/재시도·중복·오버플로 |
| 기존 상호작용·휴식 | 통과 |47개·32개 회귀 검사 |
| 기존 전투·소환 관리 | 통과 |56개·22개 회귀 검사 |
| 기존 구간 실제 Play | 통과 | UUID 저장의 관청~벌목장 API 진단 재실행. 기존 저장 접미어·Play 복원. 직접 입력이나 전체 완주가 아님 |
| 컴파일 | 통과 |9어셈블리·673소스, Unity에서 검사 도구 실행 가능. 플레이어 빌드 없음 |
| 청룡 머리·꼬리·뿌리·목 투사체 | 미구현 | 이 컴포넌트는 생장 상태만 담당 |
| 심부 현장 학습 적·연출 | 미구현 | 실제 전투로 학습 완료하는 컴포넌트는 작성됐으나 씬에 배치하지 않음 |
| 보스 보상·국 발판·재탐험 | 미구현 | ㄱ/국/仁 지급·안전 승강·활용 단차는 다음 단계 |
| 실제 조작·난도·성능 | 미검증 | 보스와 실제 입력·이동 중 측정이 필요 |

영상·자동 보행·새 빌드·유료 생성은 실행하지 않았다. 시각 구현이 없는 이 단계에서는 새로운 플레이 스크린샷을 만들지 않았다.
'''
(out / 'MECHANICS_REPORT.md').write_text(report, encoding='utf-8')
(out / 'REVIEW.html').write_text('''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>심부·청룡 전투 기반</title><style>body{background:#202721;color:#ede8d9;font:18px/1.9 system-ui;max-width:950px;margin:auto;padding:40px}a{color:#bad2a8}.notice{background:#363e30;padding:22px}</style><h1>심부·청룡의 전투와 저장 기반</h1><p class="notice">구현 중입니다. 청룡 생장 회복·금 취소, 仁의 HP1 생존과 저장, 심부 학습 완료 경로를 검사했습니다. 보스의 전체 전투와 현장 배치가 완성된 상태는 아닙니다.</p><p>생장33 · 仁26 · 학습12 검사 통과. 기존 구간과 전투·저장 회귀 검사도 통과했습니다.</p><p><a href="MECHANICS_REPORT.md">변경 내용·검증 범위·미완료 작업</a> · <a href="growth_tests.json">생장 검사</a> · <a href="ren_tests.json">仁 검사</a> · <a href="lesson_tests.json">학습 진행 검사</a> · <a href="../Summons/WaterTurtle/REVIEW.html">직전 옴 검토</a></p></html>''', encoding='utf-8')
status = root / 'Docs/PROJECT_STATUS.md'
entry = ('**2026-09-15 · 심부/청룡 기반 구현:** HP50%의 단회4초 생장 회복·금 취소, '
         '仁의 저장 선행 HP1 생존/휴식 충전과v5저장, 실제 금 중단에 연결될 학습 완료 경로를 추가했다. '
         '검사33+26+12 및 기존 저장/전투 회귀 통과. 청룡 전체 공격·현장 배치·보상·국은 미구현, 다음 캠페인 단계 비활성. '
         '[검사 보고서](../Art/Demo/Chapter3/MECHANICS_REPORT.md). 새 빌드 없음.\n\n')
text = status.read_text(encoding='utf-8-sig')
if entry not in text:
    status.write_text(entry + text, encoding='utf-8')
target = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
paths = ['Docs/PROJECT_STATUS.md', 'Art/Demo/Chapter3', 'Tools/Demo/publish_chapter3_mechanics.py']
paths += [str(p.relative_to(root)) for p in (root / 'Oheangbu/Assets/_Project/Scripts').rglob('*')
          if p.is_file() and p.suffix in ('.cs', '.meta', '.asmdef')]
copied = 0
for relative in paths:
    source = root / relative
    for item in source.rglob('*') if source.is_dir() else [source]:
        if not item.is_file(): continue
        destination = target / item.relative_to(root)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, destination); copied += 1
print('Chapter3 mechanics published and mirrored:', copied, 'files. No git mutation/build.')
