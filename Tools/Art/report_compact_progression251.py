"""Publish revision 251 evidence and documentation; never controls Unity."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import html

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Progression251'
title = '## 현재 개정 #251 · 청림 실조우와 시스템 공통 저장 경계 (2026-09-24)'
summary = '''EA와 시스템에서 겹치는 **청룡 실조우→종성·국·仁 해금→국 재방문**을 먼저 후보에 연결했다. 마을→벌목장→심부→성역 약2km 경로, 금 생장 차단 조우와 성역 휴식 지점을 원장·지도·충돌·NavMesh에 연결했다. 일반 적·보스·남문 장수의 처치 저장 대기를 공통 검사해 휴식·장비 거래·사망 복귀가 미저장 처치 결과를 앞지르지 않도록 했다. NavMesh는 조우 루트 위치와 관계없이 적 몸체를 베이크에서 제외한다.

새 증거는 후보 연결18 PASS, NavMesh/지면24 PASS, 실제 CharacterController 자동 보행1,997.942m/시뮬레이션440.64초, 별도 저장 Unity Play32 PASS, 일반 적 저장 회귀11 PASS다. 보고/학습 전 보스 처치, 파일 잠금 실패/재시도, 실제 국 상승·상단 이동·보상, 늦은 학습, 仁 생존·재충전, 휴식·사망·재실행 보존을 검사했다. 공격과 작도 입력은 API fixture이며 수동 전투 승리나 탐험 실측이 아니다.

원본 W_Demo_Compact와 일반 진행/백업 저장 해시는 유지했다. 테스트는 **Open latest test scene → W_Demo_Compact_MigrationCheck → Play**다. 새 심부 식생·성역/재방문 외형은 기능 배치 단계이며 임시 보스 외형을 사용한다. 수동 길찾기·전투·차량 통행·성능·사용자 미술 판정은 미완료다. 다음 공통 제작은 **실제 호송/검문/인도→남문 장수/개문**이며 호송~ending6단계는 아직 비활성이다.
'''

for name in ['BIBLE_INDEX.md', 'PROJECT_STATUS.md', 'Specs/SPEC-COMPACT-REBUILD.md', 'Plans/PLAN-COMPACT-REBUILD-NEXT.md', 'Handoff/HANDOFF-COMPACT-REBUILD.md']:
    path = ROOT / 'Docs' / name
    text = path.read_text(encoding='utf-8-sig')
    if title in text:
        continue
    if '## 진행 중 #251' in text:
        start = text.index('## 진행 중 #251')
        end = text.index('## 현재 개정 #250', start)
        text = text[:start] + text[end:]
    text = text.replace('## 현재 개정 #250', '## 이전 개정 #250', 1)
    prefix = '../' if '/' in name else ''
    links = f'\n[구현 계약]({prefix}Specs/SPEC-COMPACT-PROGRESSION-251.md), [새 검증/배치 화면]({prefix}../Art/World/Compact/Rebuild/PROGRESSION_REVIEW.html). 아래 과거 기록의 청룡/국 미연결 판단은 #250 당시의 상태다.\n'
    first, rest = text.split('\n', 1)
    path.write_text(first + '\n\n' + title + '\n\n' + summary + links + '\n' + rest.lstrip(), encoding='utf-8')

p = ROOT / 'Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md'
t = p.read_text(encoding='utf-8-sig').replace('결정 #250 · 소유:', '결정 #251 · 소유:', 1)
old = '**지금 이어서 만들 핵심은 청림 심부 → 청룡 → 국 재방문이다.** 첫 마을과 장비는 후보에 연결되어 있지만, 청룡/국 재방문은 데이터의 활성 표시와 실제 씬 연결이 다르다. 이후 상경 호송·검문·남문을 연결한다. 전체 게임 구현률은 산출하지 않는다.'
new = '**다음 공통 제작은 실제 호송 → 검문/인도 → 남문이다.** #251에서 청림 보행 경로·청룡·국 재방문·仁을 후보에 연결하고 저장 경계를 함께 검증했다. 새 구간의 식생/외형·수동 탐험/전투·성능은 미완료다. [현재 근거](../Specs/SPEC-COMPACT-PROGRESSION-251.md). 전체 게임 구현률은 산출하지 않는다.'
t = t.replace(old, new)
t = t.replace('- #250 [후보 실연결 조회]', '- 과거 #250 [후보 실연결 조회]', 1)
if '- #251에서는 청룡' not in t:
    t = t.replace('- 후보 Campaign은 20단계', '- #251에서는 청룡·금 생장 학습 조우와 국 장치·필드 프로필·새 휴식을 실제 연결했다. `Progression251/audit.txt` 18 PASS, `runtime.txt` 32 PASS가 현재 근거다. #250 조회의 보스/국 0개는 현재 상태가 아니다.\n- 후보 Campaign은 20단계', 1)
lines = t.splitlines()
for i, line in enumerate(lines):
    if line.startswith('| B02 ·'):
        lines[i] = '| B02 · LDB-REALMS/GATING/BOSSPLACEMENT, NARR-LENSES | **일부 후보 연결 + 확장 예정**. #251 마을→벌목장→심부→성역 약2km 보행 골격·금 생장 차단·성역 휴식 연결, 자동 충돌 보행 PASS | 뿌리 굴·약초꾼 길·신목 위험 지선·현장 단서·숲 외형·지름길은 잔여. 지도 선과 자동 보행을 수동 길찾기/미술 승인으로 승격하지 않음 |'
    if line.startswith('| B03 ·'):
        lines[i] = '| B03 · COMBAT-BOSS/OSANG, SPELL-FIELD | **후보 연결 + 자동 검증**. #251 실제 청룡, 보고/학습 전 처치, 국 2.4m 승강·상단 이동·보상, 仁 생존/휴식 충전, 저장 실패/재시도·사망·재실행 PASS | 실제 입력 전투·국 우회 등반 전체 경계·전투 난이도·임시 외형 교체·미술/성능 검증은 남음. API 공격 fixture를 사용자 전투 통과로 세지 않음 |'
    if line.startswith('| B12 ·'):
        lines[i] = '| B12 · SPELL-FIELD, LDB-GATING | **국 후보 연결 / 나머지 확장 예정**. #251 실제 승강/탑승/상단 접근/보상/저장 PASS. 눈·숫·웅·뭄의 물리 관문은 미완료 | 같은 저장/사실 경로를 재사용해 나머지 필드 능력을 실제 충돌·지지면·세계 변화와 연결. 국 우회 전체 경계도 별도 검사. RequiredAbility/OneWay 선언만으로 완료 처리하지 않음 |'
    if line.startswith('1. B01/B07/B08'):
        lines[i] = '1. B04의 실제 화물·좌석·검문/인도 지점을 원장에 연결하고 #251 처치 저장 경계와 계약/청룡 선행 조건을 재사용한다.'
    if line.startswith('2. B03을'):
        lines[i] = '2. 남문 장수·문 세계 변화를 연결하고 인도→처치→개문을 저장 실패/재실행까지 검사한다. 비활성 Campaign 단계를 실제 연결보다 먼저 켜지 않는다.'
    if line.startswith('3. B04 호송'):
        lines[i] = '3. B01~B03/B07/B08의 새 청림 숲/단서/외형·수동 탐험/전투·독립 성능 검증을 진행한다. 첫 구간 통과를 EA 전체 완료로 승격하지 않는다.'
p.write_text('\n'.join(lines)+'\n', encoding='utf-8')

decision = ROOT / 'Docs/DECISIONS.md'
t = decision.read_text(encoding='utf-8-sig')
extra = '| 251 | 처치 저장/베이크 공통화 | 일반 적만의 대기 검사 → 일반 적·청룡·남문 통합 경계 | 휴식·장비·사망·일반 저장의 중간 상태 공개를 방지. NavMesh는 모든 조우 몸체를 제외. 청룡과 일반 적을 새 Play에서 검사했으며 남문 실전은 후속. |\n'
if extra not in t:
    decision.write_text(t.rstrip()+'\n'+extra, encoding='utf-8')

checks = {}
for name in ['audit', 'runtime', 'ordinary-runtime', 'navigation', 'walk']:
    data = (OUT/f'{name}.txt').read_text(encoding='utf-8-sig').splitlines()
    checks[name] = dict(passed=sum(x.startswith('PASS') for x in data), failed=sum(x.startswith('FAIL') for x in data))
    assert checks[name]['failed'] == 0, (name, checks[name])
receipt = json.loads((OUT.parent/'migration_slice.json').read_text(encoding='utf-8-sig'))
candidate = ROOT/'Oheangbu'/receipt['scene']
sources = [candidate, candidate.parent/'Content.asset', candidate.parent/'Map.asset', candidate.parent/'Navigation.asset', candidate.parent/'Village245/Campaign.asset']
scripts = ROOT/'Oheangbu/Assets/_Project/Scripts'
sources += list((scripts/'Editor/WorldMacro').glob('CompactRebuildProgression251*.cs'))
sources += list((scripts/'App/World').glob('WorldMacroPlaytestSession*.cs'))
sources += [scripts/'Editor/WorldMacro/CompactRebuildNavigation.cs', scripts/'Editor/WorldMacro/CompactRebuildSliceChecks.cs']
delivery = dict(revision=251, utc=datetime.now(timezone.utc).isoformat(), scene=receipt['scene'], slot='world-demo-compact-cave-v4', diagnostic_suffix='_compact_slice_e8835243247745ad8340fd540769e507', checks=checks,
    walk=json.loads((OUT/'walk.json').read_text(encoding='utf-8-sig')),
    manual_play='not performed', gpu_cpu_measurement='not performed', art_approval='pending; functional prototype visuals',
    sources={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources})
(OUT/'delivery.json').write_text(json.dumps(delivery, ensure_ascii=False, indent=2), encoding='utf-8')

page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><title>청림 공통 진행 연결</title><style>body{max-width:1160px;margin:48px auto;padding:0 24px;background:#e5dfd1;color:#252621;font:17px/1.7 sans-serif}h1{font-size:32px}figure{margin:36px 0}img{width:100%}a{color:#415447}small{color:#655f55}table{border-collapse:collapse}td,th{padding:9px 24px 9px 0;text-align:left;border-bottom:1px solid #b4ae9f}</style><h1>청림 실조우와 재방문</h1><p>마을 → 벌목장 → 심부 → 청룡 → 국 재방문. EA 진행과 필드 술식·영구 사실·저장 처리가 겹치는 구간을 먼저 연결했습니다.</p><p><strong>기능 배치 검토입니다.</strong> 새 숲의 식생, 성역과 재방문 장소의 외형, 임시 보스 모델은 미술 완료 상태가 아닙니다.</p><table><tr><th>검증</th><th>현재 결과</th></tr>'''
for name, result in checks.items():
    page += f'<tr><td><a href="Progression251/{name}.txt">{html.escape(name)}</a></td><td>{result["passed"]} PASS</td></tr>'
page += '''</table><p>자동 충돌 보행: 1,997.942m / 시뮬레이션 440.64초. 보스 공격과 작도 입력은 API fixture이고 국 승강·탑승·상단 이동은 실제 Unity 시간과 충돌 처리를 사용했습니다. 휴식·사망·동일 저장 재실행 보존을 확인했습니다.</p><p>수동 길찾기·전투 난이도·차량 통행·국 우회 등반 전체 경계·CPU/GPU 실측·사용자 미술 판정은 별도로 남아 있습니다. 원본 축소맵과 일반 저장은 보존했습니다.</p>'''
for i, label in enumerate(['청룡 성역 — 기존 임시 외형, 식생 제작 전', '금 생장 차단 조우 — 기능 배치', '국 재방문 — 실제 승강과 상단 보상, 석대 외형 보완 필요']):
    page += f'<figure><img src="Progression251/view_{i}.png"><figcaption>{label}</figcaption></figure>'
page += '<p>테스트: Open latest test scene → W_Demo_Compact_MigrationCheck → Play.</p><p><a href="Progression251/delivery.json">전달 기록</a> · <a href="../../../../Docs/Specs/SPEC-COMPACT-PROGRESSION-251.md">구현 계약</a></p></html>'
(OUT.parent/'PROGRESSION_REVIEW.html').write_text(page, encoding='utf-8')
print(json.dumps(checks, ensure_ascii=False))
