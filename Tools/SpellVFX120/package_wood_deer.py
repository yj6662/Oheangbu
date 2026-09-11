"""Package the current, verified wood deer appearance for review."""
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[2]
out = root / 'Art/SpellVFX120/WoodDeer'
assets = root / 'Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer'
with zipfile.ZipFile(out / 'WoodDeer_Delivery.zip', 'w', zipfile.ZIP_DEFLATED, compresslevel=2) as archive:
    for path in assets.rglob('*'):
        if path.is_file():
            archive.write(path, 'Unity/Assets/_Project/Art/SpellVFX120/WoodDeer/' + path.relative_to(assets).as_posix())
    for name in ['WoodDeer_Working.blend', 'REPORT.md', 'model_report.json', 'fbx_roundtrip.json',
                 'body_preservation.json', 'unity_model.json', 'audit.json', 'model_before.png', 'model_after.png']:
        archive.write(out / name, name)
    archive.write(out / 'BeforeNecklace/model_after.png', 'BeforeNecklace/model_after.png')
with zipfile.ZipFile(out / 'WoodDeer_Delivery.zip') as archive:
    assert archive.testzip() is None
print('PACKAGE_PASS')
