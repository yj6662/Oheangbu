from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts')
p=r/'App/SpellVFX120/Vfx120Effect.cs';s=p.read_text(encoding='utf-8').replace('|| IsFireGuard(Profile);','|| IsFireGuard(Profile) || IsFireAura(Profile);').replace('ClearFireGuard();','ClearFireGuard();\n            ClearFireAura();').replace('BuildFireGuard();','BuildFireGuard();\n            BuildFireAura();').replace('SampleFireGuard();','SampleFireGuard();\n            SampleFireAura();');p.write_text(s,encoding='utf-8')
