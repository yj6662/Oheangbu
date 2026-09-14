"""Preserve the exact pre-polish map/input sources without resetting unrelated work."""
from pathlib import Path
import hashlib, json, shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/PlaytestPolish'
paths = [
 'Oheangbu/Assets/_Project/Scripts/Drawing/DrawingInputController.cs',
 'Oheangbu/Assets/_Project/Scripts/App/World/WorldMacroCombatWalker.cs',
 'Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapBakedDataSO.cs',
 'Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapPresenter.cs',
 'Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapPolylineGraphic.cs',
 'Oheangbu/Assets/_Project/Scripts/App/World/UI/WorldMapPaperInk.cs',
 'Oheangbu/Assets/_Project/Resources/WorldMap/PaperMapSurface.shader',
 'Oheangbu/Assets/_Project/Resources/WorldMap/WorldMapBakedData.asset',
 'Oheangbu/Assets/_Project/Resources/WorldMap/WorldMapBase.png',
 'Oheangbu/Assets/_Project/Scenes/World/W_WorldMacro_Playtest.unity',
]
manifest = {}
for relative in paths:
 source = ROOT / relative
 target = OUT / 'Baseline' / relative
 target.parent.mkdir(parents=True, exist_ok=True)
 if not target.exists(): shutil.copy2(source, target)
 manifest[relative] = hashlib.sha256(target.read_bytes()).hexdigest()
(OUT/'Baseline/manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
for name, filename in [('A','exec-07801602-c1b3-402c-a9ae-553e2574856b.png'),('B','exec-49d0367e-6828-4d45-b2f5-dd3719aec5d5.png')]:
 source=Path('C:/Users/yj666/.codex/generated_images/01a079b8-3c10-7d33-aa15-2f833e39fbef')/filename
 target=OUT/'Hands'/('Grip_'+name+'.png')
 target.parent.mkdir(parents=True,exist_ok=True)
 if not target.exists(): shutil.copy2(source,target)
print(json.dumps({'preserved':len(manifest),'hands':['Grip_A.png','Grip_B.png']},indent=2))
