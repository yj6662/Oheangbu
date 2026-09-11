from pathlib import Path
import json,hashlib
R=Path(__file__).resolve().parents[2];O=R/'Art/SpellVFX120/StoneDokkaebi';O.mkdir(exist_ok=True)
A=R/'Oheangbu/Assets/_Project/Art/SpellVFX120';files=list((A/'Profiles').glob('*.asset'))
for folder in ['WoodDeer','FireHaetae','MetalTiger','MeshySummons']:files+=list((A/folder).rglob('*'))
if not (O/'scope_before.json').exists():(O/'scope_before.json').write_text(json.dumps({p.relative_to(R).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.is_file()},indent=2))
prompt='A Korean folk dokkaebi made entirely of natural weathered rock. Squat broad pear-shaped boulder torso, very wide shoulders, oversized chunky stone fists, short bowed legs and two broad bare rock feet. Huge expressive carved eyes, thick curved eyebrows, bulbous flat nose, lopsided mischievous toothy grin with two short blunt tusks, two tiny worn stone horns. Amusing but powerful village guardian, not evil. Bare stone anatomy flows continuously, rounded eroded granite masses with broad shallow cracks. Neutral standing pose, fists hanging beside hips, clear gaps under arms and between feet. Complete freestanding creature. No armor, belt, boots, clothing, weapons, pedestal, lettering, jewels or glowing lava. No Minecraft cubes, knight or robot.'
texture='Warm grey granite and dusty muted ochre clay in broad natural cracks. Rough matte weathered stone with fine mineral grain. Dark carved recesses emphasize expressive eyes, eyebrows and smiling mouth. Teeth and tiny horns are the same stone. Sparse moss stains in sheltered creases only. No metal, lava, bright glow, jewelry, paint, lettering, armor or clothing.'
assert len(prompt)<=800 and len(texture)<=800
s=(R/'Tools/MeshyRuns/SpellVFX120/run_retry02.py').read_text()
s=s.replace("OUT = ROOT / 'Art/SpellVFX120/MeshySummons/Retry02'", "OUT = ROOT / 'Art/SpellVFX120/StoneDokkaebi/Meshy'")
s=s.replace("ORIGINAL_LEDGER = OUT.parent / 'ledger.json'", "ORIGINAL_LEDGER = ROOT / 'Art/SpellVFX120/MeshySummons/ledger.json'")
s=s.replace('CAP = 70','CAP = 35').replace('ledger02.json','ledger.json')
start=s.index('SPECS = ');end=s.index('\n\n',start)
s=s[:start]+'SPECS = '+repr({'064_BAB8':('몸','StoneDokkaebi',prompt,texture)})+s[end:]
s=s.replace("'prior_actual_credits': 155, 'combined_self_imposed_cap': 225,", "'prior_summon_batches_actual_credits': 205,")
s=s.replace('Self-imposed Retry02 cap 70: two previews at 25 and accepted-only two refinements at 10. Prior batch actual 155 remains separate; combined cap 225. Not a user-set credit limit.','Self-imposed cap 35: one Meshy 7 Ultra preview at 25 and one refine at 10. No automatic paid retries. Separate from earlier batches; not a user-set limit.')
(R/'Tools/MeshyRuns/SpellVFX120/run_stone_dokkaebi.py').write_text(s)
print('STONE_DOKKAEBI_READY; text length',len(prompt))
