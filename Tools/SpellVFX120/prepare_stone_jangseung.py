from pathlib import Path
import json,hashlib,shutil
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/StoneJangseung';O.mkdir(exist_ok=True);M=O/'Meshy';M.mkdir(exist_ok=True)
A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120';files=list((A/'Profiles').glob('*.asset'))
for folder in ['WoodDeer','FireHaetae','MetalTiger','StoneDokkaebi','MeshySummons']:files+=list((A/folder).rglob('*'))
if not (O/'scope_before.json').exists():(O/'scope_before.json').write_text(json.dumps({p.relative_to(R).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()},indent=2))
shutil.copy2(Path('C:/Users/yj666/AppData/Local/Temp/codex-clipboard-22a4bd67-6bb1-4b9b-9acd-a9859ae01d09.png'),O/'Reference_StoneJangseung.png')
(M/'.gitignore').write_text('.private/\n')
prompt='One Korean stone jangseung guardian totem, sophisticated dark fantasy game prop. Single tall chunky tapered granite pillar, broad flat stable base, NO arms or legs. Face carved directly into upper third: huge protruding rounded stone eyes beneath sweeping joined brows, broad triangular blunt nose with curled nostrils, sly asymmetrical smile and few broad square teeth. Crown is a simple broken rock ridge, no hat or horns. Refined strong silhouette, restrained geometric carving and deliberate chisel planes. Lower column bears three shallow abstract flowing earth-seal grooves, NOT writing. Original stylized interpretation of weathered Korean village guardian stones, dignified and slightly mischievous. No humanoid torso, armor, weapons, pedestal, letters, jewelry or modern objects.'
texture='Muted warm grey weathered granite, broad mineral variation, subtle ochre dust in cracks and sparse dark green moss near the base. Carved eye sockets and mouth read clearly with dark recesses, raised eyes and brows remain stone. Matte rough stone and fine pores; no polished marble, metal, gold, lava, bright glowing eyes, lettering, painted colors or white plaster.'
assert len(prompt)<=800
s=(R/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py').read_text().replace('StoneDokkaebi/Meshy','StoneJangseung/Meshy').replace("'prior_summon_batches_actual_credits': 205","'prior_summon_batches_actual_credits': 240")
start=s.index('SPECS = ');end=s.index('\n\n',start);s=s[:start]+'SPECS = '+repr({'064_BAB8':('몸','StoneJangseung',prompt,texture)})+s[end:]
(R/'Tools/MeshyRuns/SpellVFX120/run_stone_jangseung.py').write_text(s)
(O/'REFERENCE_INTERPRETATION.md').write_text('사진의 둥근 돌눈·굵은 코·돌기둥을 해석한 작성 텍스트를 Meshy7에 전달한다. 사진 자체를 API에 업로드하거나 명문을 복제하지 않는다. 낮은 채도·정돈된 조각면·비대칭 미소·추상 지맥 홈으로 게임적 각색한다. 이전 도깨비와 별도 원본/비용 기록을 유지한다. 이번 자체 한도35크레딧, 유료 자동 재시도 없음.',encoding='utf-8')
print('READY',len(prompt))
