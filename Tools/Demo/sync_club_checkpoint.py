"""Record a completed provisional combat checkpoint and mirror its exact derivatives."""
from pathlib import Path
import json, shutil
root=Path(__file__).resolve().parents[2]
target=Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
base=root/'Art/Demo/Summons'
run=json.loads((base/'DokkaebiClub/runtime_club_tests.json').read_text(encoding='utf-8-sig'))
assert run['status']=='PASS_RUNTIME_API' and not run['failed']
entry=('**2026-09-15 · 몸 몽둥이 전투 첫 연결:** 기존16,653tris/1재질 원형에21본·3클립을 추가했다. '
       '실측반경1.3m·140도 횡 공격, 적 중심 높이 중복 적용 오류 수정. '
       '규칙45·Unity 참조7·실제Play13검사 통과, 첫24피해·자연 소멸·잔존0. '
       '어깨 변형·플레이어 가림·실제 발 접지·수동 전투·성능은 보완/미검증이다. '
       '곰·놈·솜·몸4종 첫 연결, 옴 및 후반 캠페인은 진행 중. 새 빌드 없음. '
       '[몸 검토](../Art/Demo/Summons/DokkaebiClub/REVIEW.html).\n\n')
status=root/'Docs/PROJECT_STATUS.md'
text=status.read_text(encoding='utf-8-sig')
if entry not in text:status.write_text(entry+text,encoding='utf-8')
spec=root/'Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md'
text=spec.read_text(encoding='utf-8-sig')
text=text.replace('세 외형의 실제 지형·동작과 미술 보완은 남아 있으며 몸·옴 전투는 후속 구현이다.',
    '몸은 기존 도깨비21본·3클립의 횡 공격까지 연결했다. 네 외형의 실제 지형·동작과 미술 보완은 남아 있으며 옴 전투는 후속 구현이다.')
heading='## 몸 몽둥이 횡 공격 — 연결된 TEST'
if heading not in text:
    text+='\n'+heading+'\n\n0.8초 준비·0.3초 횡 공격·0.6초 회복. 실제 ClubTip 반경1.3m·높이0.8505m·좌우70도, 판정 높이 여유0.65m를 사용한다. 여유값은 실측 두께가 아니다. 시전 때 범위 안 대상의 생명주기와 각도별 접촉 시간을 고정하며, 접촉 시 생존·각도8도·높이·거리·차폐를 재검사한다. 각 대상에 시전 위력1.2배를 한 번 적용하며 후발 진입/부활 대상을 추가하지 않는다. 적 루트가 중심인 경우 높이를 다시 더하지 않는다.\n\n기존 원형과 UV를 보존한16,653tris/1재질/21본/3클립이다. 규칙45·Unity7·실제Play13검사는 첫 연결의 근거이며 미술 승인이 아니다. 어깨 장식 압축, 플레이어에 의한 가림, 실제 경사 접지와 정확한 몽둥이 접점, 수동 전투·성능은 후속이다. 상세는 `../../Art/Demo/Summons/DokkaebiClub/REPORT.md`.\n'
spec.write_text(text,encoding='utf-8')
index=base/'REVIEW.html';text=index.read_text(encoding='utf-8-sig')
notice='<p class="notice"><a href="DokkaebiClub/REVIEW.html">최신: 몸 몽둥이 전투 첫 연결</a> — 4종 연결, 최종 미술과 옴·후반 캠페인은 진행 중입니다.</p>'
if notice not in text:index.write_text(text.replace('<main>','<main>'+notice,1),encoding='utf-8')
paths=['Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-DEMO-SUMMON-COMBAT.md','Tools/Demo',
       'Art/Demo/Summons/DokkaebiClub','Art/Demo/Summons/REVIEW.html',
       'Art/Demo/Summons/WaterTurtle/source_inventory.json',
       'Oheangbu/Assets/_Project/Art/Demo/Summons/DokkaebiClub',
       'Oheangbu/Assets/_Project/Art/Demo/Summons/DokkaebiClub.meta',
       'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity',
       'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Campaign.unity.meta']
paths += [str(p.relative_to(root)) for p in (root/'Tools/Blender').glob('*dokkaebi*')]
paths += ['Tools/Blender/inspect_water_turtle_combat.py']
paths += [str(p.relative_to(root)) for p in base.glob('*.json')]
paths += [str(p.relative_to(root)) for p in (root/'Oheangbu/Assets/_Project/Scripts').rglob('*.cs*')
          if p.name.startswith(('DemoSummon','DemoClub','SummonCombatProfile','SummonClubAttackPlan'))]
copied=[]
for relative in paths:
    source=root/relative
    assert source.exists(),source
    for item in source.rglob('*') if source.is_dir() else [source]:
        if not item.is_file() or item.suffix=='.blend1' or '__pycache__' in item.parts:continue
        dest=target/item.relative_to(root);dest.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(item,dest);copied.append(str(item.relative_to(root)))
(base/'club_sync_manifest.json').write_text(json.dumps({'files':copied,'count':len(copied),'gitMutated':False},indent=2),encoding='utf-8')
print('Mirrored',len(copied),'files. No commit/push/build.')
