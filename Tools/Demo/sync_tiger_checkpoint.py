"""Mirror bounded demo derivatives to the existing review worktree, without git mutation."""
from pathlib import Path
import shutil
import json

root = Path(__file__).resolve().parents[2]
target = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
assert (target / '.git').is_file()
base = root / 'Art/Demo/Summons'
run = json.loads((base / 'MetalTiger/runtime_tiger_tests.json').read_text(encoding='utf-8-sig'))
assert run['status'] == 'PASS_RUNTIME_API' and not run['failed']
entry = ('**2026-09-15 · 놈 화염 경계 보정 / 솜 전투 첫 연결:** '
         '놈의 실제 화염 외곽6.114m를 GPU 진단에서 재현하고 KTP 사본의 최종 출력 마스크로4.492m까지 제한했다. '
         '수평 검사8·재질/공격35·실제Play13검사 통과. 솜은 기존12,414tris/1재질 원형에25본·6클립을 추가해 '
         '고정 도약 뒤 좌우 발톱을 연결했다. 규칙38·리그8·실제Play15검사, 첫 연속 피해10+10·자연 소멸·잔존0. '
         '9어셈블리654소스 오류0. 경사 접지·각진 관절·발톱 실제 접점·플레이어 가림·수동 전투·성능은 남아 있다. '
         '곰·놈·솜3종 첫 전투 연결, 몸·옴과 후반 데모는 계속 제작. 저장/검사 상태 복원, 새 빌드 없음. '
         '[솜 검토](../Art/Demo/Summons/MetalTiger/REVIEW.html) · '
         '[놈 범위 보정](../Art/Demo/Summons/FireHaetae/Boundary/REVIEW.html).\n\n')
status = root / 'Docs/PROJECT_STATUS.md'
text = status.read_text(encoding='utf-8-sig')
if entry not in text:
    status.write_text(entry + text, encoding='utf-8')
index = base / 'REVIEW.html'
text = index.read_text(encoding='utf-8-sig')
notice = '<p class="notice"><a href="MetalTiger/REVIEW.html">최신: 솜 도약·좌우 발톱 전투</a> · <a href="FireHaetae/Boundary/REVIEW.html">놈 화염 경계 보정</a> — 아래 이전 체크포인트의 미완료 목록은 당시 상태입니다.</p>'
if notice not in text:
    index.write_text(text.replace('<main>', '<main>' + notice, 1), encoding='utf-8')

paths = [
    'Docs/PROJECT_STATUS.md', 'Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md',
    'Tools/Demo', 'Tools/Blender/build_metal_tiger_combat.py',
    'Art/Demo/Summons/MetalTiger', 'Art/Demo/Summons/FireHaetae/Boundary',
    'Art/Demo/Summons/FireHaetae/REVIEW.html', 'Art/Demo/Summons/REVIEW.html',
    'Oheangbu/Assets/_Project/Art/Demo/Summons/MetalTiger',
    'Oheangbu/Assets/_Project/Art/Demo/Summons/MetalTiger.meta',
    'Oheangbu/Assets/_Project/Art/Demo/Summons/FireHaetae/Combat_Nom.asset',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity.meta',
]
paths += [str(p.relative_to(root)) for p in (root / 'Oheangbu/Assets/_Project/Shaders').glob('DemoFlame*')]
paths += [str(p.relative_to(root)) for p in base.glob('*.json')]
paths += [str(p.relative_to(root)) for p in (root / 'Oheangbu/Assets/_Project/Scripts').rglob('*.cs*')
          if p.name.startswith(('DemoSummon', 'DemoFlame', 'DemoTiger', 'DemoHaetae', 'SummonCombatProfile', 'SummonTigerAttackPlan'))]
copied = []
for relative in paths:
    source = root / relative
    assert source.exists(), source
    for item in (source.rglob('*') if source.is_dir() else [source]):
        if not item.is_file() or item.suffix == '.blend1' or '__pycache__' in item.parts:
            continue
        destination = target / item.relative_to(root)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, destination)
        copied.append(str(item.relative_to(root)))
# The old class was renamed preserving its meta GUID. Never leave both classes in the review tree.
old = list((target / 'Oheangbu/Assets/_Project/Scripts').rglob('DemoHaetaeEmberPresentation.cs*'))
assert not old, f'Removed renamed files unexpectedly remain: {old}'
(base / 'tiger_sync_manifest.json').write_text(json.dumps({'files': copied, 'count': len(copied), 'gitMutated': False}, indent=2), encoding='utf-8')
print(f'Mirrored {len(copied)} checkpoint files. No stage/commit/push/build.')
