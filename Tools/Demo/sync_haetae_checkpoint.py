"""Copy this checkpoint's derivatives to the existing review worktree; never stage or commit."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parents[2]
worktree = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
assert (worktree / '.git').exists()
base = root / 'Art/Demo/Summons'
compile_report = json.loads((base / 'compile.json').read_text(encoding='utf-8-sig'))
assert len(compile_report['results']) == 9
assert all(row['exitCode'] == 0 for row in compile_report['results'])
for name in ('compile.json', 'compile.log'):
    shutil.copy2(base / name, base / 'FireHaetae' / name)

status = root / 'Docs/PROJECT_STATUS.md'
entry = ('**2026-09-15 · 놈 해태 전투 첫 연결:** 기존 원형의 별도 파생본에 21본 리그·전진·후퇴·화염 공격을 연결했다. '
         'Unity 실제12,532tris/1재질, 최대4웨이트. 거리 유지·공통 피해·곰↔놈 교체 등34검사, 씬/리그8검사, 실제 Play API13검사 통과. '
         '먹1.00→0.70, 적3회/누적60피해, 활동20초 뒤 소멸/잔존0. 9어셈블리648소스 컴파일 통과. '
         '화염 밝기·입자 외곽/피해 범위 일치·경사 접지·수동 전투·성능은 미완료/미검증이다. '
         '곰·놈 2종의 첫 전투 연결이며 솜·몸·옴과 후반 데모는 계속 제작한다. 사용자 저장/검사 상태 복원, 새 빌드 없음. '
         '[검토](../Art/Demo/Summons/FireHaetae/REVIEW.html) · [보고서](../Art/Demo/Summons/FireHaetae/REPORT.md).\n\n')
text = status.read_text(encoding='utf-8-sig')
if not text.startswith(entry):
    status.write_text(entry + text, encoding='utf-8')

paths = [
    'Docs/PROJECT_STATUS.md', 'Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md',
    'Docs/Plans/DEMO-NOM-IMPLEMENTATION-NOTES.md',
    'Tools/Demo', 'Tools/Blender/build_fire_haetae_combat.py',
    'Art/Demo/Summons/FireHaetae', 'Art/Demo/Summons/REVIEW.html',
    'Art/Demo/Summons/flame_tests.json', 'Art/Demo/Summons/haetae_scene_audit.json',
    'Art/Demo/Summons/runtime_flame_tests.json',
    'Oheangbu/Assets/_Project/Art/Demo/Summons/FireHaetae',
    'Oheangbu/Assets/_Project/Art/Demo/Summons/FireHaetae.meta',
    'Oheangbu/Assets/_Project/Shaders/DemoFireHaetae.shader',
    'Oheangbu/Assets/_Project/Shaders/DemoFireHaetae.shader.meta',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity',
    'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity.meta',
]
count = 0
for relative in paths:
    source = root / relative
    assert source.exists(), source
    files = source.rglob('*') if source.is_dir() else [source]
    for item in files:
        if not item.is_file() or item.suffix == '.blend1' or '__pycache__' in item.parts:
            continue
        destination = worktree / item.relative_to(root)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(item, destination)
        count += 1
print(f'Mirrored {count} checkpoint files. No staging, commit, push or build.')
