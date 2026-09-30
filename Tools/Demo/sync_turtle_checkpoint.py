"""Record and mirror verified turtle derivatives; preserve unrelated work and git state."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parents[2]
target = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
base = root / 'Art/Demo/Summons'
run = json.loads((base / 'WaterTurtle/runtime_water_tests.json').read_text(encoding='utf-8-sig'))
assert run['status'] == 'PASS_RUNTIME_API' and not run['failed']
entry = ('**2026-09-15 · 옴 물줄기 전투 첫 연결:** 원본16,648tris/1재질을 보존한18본·4클립, '
         '느린 추종·후진·입에서 전진하는 물줄기를 연결했다. 판정45·시각범위18·Unity8·실제Play15검사 통과. '
         '곰·놈·솜·몸·옴5종 첫 연결이며 최종 완성이 아니다. 관 같은 물 표현, 시야 가림, 닫힌 입·보행, '
         '수동 전투·성능은 보완/미검증. 다음은 심부·청룡·국·仁와 후반 캠페인이다. 새 빌드 없음. '
         '[옴 검토](../Art/Demo/Summons/WaterTurtle/REVIEW.html).\n\n')
status = root / 'Docs/PROJECT_STATUS.md'
text = status.read_text(encoding='utf-8-sig')
if entry not in text:
    status.write_text(entry + text, encoding='utf-8')
spec = root / 'Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md'
text = spec.read_text(encoding='utf-8-sig')
heading = '## 옴 물줄기 — 연결된 TEST'
if heading not in text:
    text += ('\n' + heading + '\n\n기존 승인 거북18본·4클립. 추종0.6m/s, 선호4m/최소2.5m. '
             '0.6초 준비·0.7초 발사·0.5초 회복, 범위6m/반경0.22m/전선12m/s. '
             '방향·대상 생명주기·접촉 시각을 공격 시작 때 고정하고 실제 전선 도착 때 이동·생존·차폐를 다시 확인한다. '
             '벽 제한은 물줄기의 전체 반경으로 검사하고 도중 벽이 사라져도 사거리를 다시 늘리지 않는다. '
             '판정과 메시·포말은 같은 경과 시간을 사용하며 발사 종료 후 꼬리가 전진한다. '
             '시전 위력1.0배, 공격당 대상별1회, 소환 그로기/속성완주 대행 없음.\n\n'
             '16,648tris·1재질·18본·4클립, 실제 발사 입 위치(0,0.954323,1.808349)m. '
             '별도 물줄기1152tris/최대64입자. 판정45·시각18·Unity8·Play15검사는 첫 연결의 근거다. '
             '관 같은 물·등껍질 시야 가림·닫힌 입·실제 경사 발 접지·수동 전투·성능은 미완료다. '
             '상세는 `../../Art/Demo/Summons/WaterTurtle/REPORT.md`.\n')
spec.write_text(text, encoding='utf-8')
index = base / 'REVIEW.html'
text = index.read_text(encoding='utf-8-sig')
notice = '<p class="notice"><a href="WaterTurtle/REVIEW.html">최신: 옴 물줄기 전투 첫 연결</a> — 5종 첫 연결, 미술·수동 전투·성능과 후반 캠페인은 진행 중입니다.</p>'
if notice not in text:
    index.write_text(text.replace('<main>', '<main>' + notice, 1), encoding='utf-8')
paths = ['Docs/PROJECT_STATUS.md', 'Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md', 'Tools/Demo',
         'Art/Demo/Summons/WaterTurtle', 'Art/Demo/Summons/REVIEW.html',
         'Oheangbu/Assets/_Project/Art/Demo/Summons/WaterTurtle',
         'Oheangbu/Assets/_Project/Art/Demo/Summons/WaterTurtle.meta',
         'Oheangbu/Assets/_Project/Shaders/DemoWaterJet.shader',
         'Oheangbu/Assets/_Project/Shaders/DemoWaterJet.shader.meta',
         'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity',
         'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity.meta']
paths += [str(p.relative_to(root)) for p in (root / 'Tools/Blender').glob('*water_turtle*')]
paths += [str(p.relative_to(root)) for p in base.glob('*.json')]
paths += [str(p.relative_to(root)) for p in (root / 'Oheangbu/Assets/_Project/Scripts').rglob('*.cs*')
          if p.name.startswith(('DemoSummon', 'DemoTurtle', 'SummonCombatProfile', 'SummonWaterAttackPlan'))]
copied = []
for relative in paths:
    source = root / relative
    assert source.exists(), source
    for item in source.rglob('*') if source.is_dir() else [source]:
        if not item.is_file() or item.suffix == '.blend1' or '__pycache__' in item.parts:
            continue
        destination = target / item.relative_to(root)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, destination)
        copied.append(str(item.relative_to(root)))
(base / 'turtle_sync_manifest.json').write_text(json.dumps({'files': copied, 'count': len(copied), 'gitMutated': False}, indent=2), encoding='utf-8')
print('Mirrored', len(copied), 'files. No commit/push/build.')
