"""Isolated ten-angle override recipe; retain the frozen24/18-angle originals."""
from pathlib import Path
import hashlib

ROOT=Path(__file__).resolve().parents[3]
recipe=Path(__file__).with_name('build_near_shoulder_lining_lod.py')
source=recipe.read_text()
replacements={
 "SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/ShoulderComplete/DosaV2_ShoulderComplete_Candidate.blend'":
 "SOURCE=ROOT/'Art/PlayerV2/Inspect/ClothBlender/TorsoSupportedPanel/SeamLineCandidate/DosaV2_FreeHemPanel_Candidate.blend'\nassert hashlib.sha256(SOURCE.read_bytes()).hexdigest()=='93f74bc12a70b4d9b0960dcfd05894c7cec9c35bf623b301ef0e52cbf1d0587a'",
 "OUT=SOURCE.parent/'NearOverrides';OUT.mkdir(parents=True,exist_ok=True)":
 "OUT=ROOT/'Art/PlayerV2/Inspect/ClothBlender/NearLiningBudget10';OUT.mkdir(parents=True,exist_ok=True)",
 "points=[];weights=[];samples=[];segments=18":"points=[];weights=[];samples=[];segments=10\n    assert old.shape_keys is None, 'Near lining must remain shape0'",
 "name+'_Near18'":"name+'_Near10'",
 "obj['NearOnlyAngularSamples']=18":"obj['NearOnlyAngularSamples']=10",
 "assert len(me.vertices)==162 and len(me.loop_triangles)==320":"assert len(me.vertices)==90 and len(me.loop_triangles)==176\n    assert max(map(len,weights))<=4, 'Excess skin palette'",
 "'afterVertices':162":"'afterVertices':90",
 "'afterTriangles':320":"'afterTriangles':176",
 "DEST=OUT/'DosaV2_NearArmLining18.blend'":"DEST=OUT/'DosaV2_NearArmLining10.blend'",
 "'netNearTrianglesIncludingTwo96TriangleShoulders':25990-216+192":"'netNearTrianglesIncludingTwo96TriangleShoulders':26270-2*(320-176)",
 "Only near rendering uses these18-angle closed surfaces.":"Only near rendering uses these10-angle closed surfaces. Existing24/18-angle originals are preserved."
}
for old,new in replacements.items():
    assert source.count(old)==1,(old,source.count(old))
    source=source.replace(old,new)
dest=ROOT/'Art/PlayerV2/Inspect/ClothBlender/NearLiningBudget10';dest.mkdir(exist_ok=True)
(dest/'base-recipe.sha256').write_text(hashlib.sha256(recipe.read_bytes()).hexdigest(),encoding='ascii')
# Execute the exact retained algorithm with only the explicit scoped inputs,
# angular count and expected output cardinalities substituted above.
exec(compile(source,str(__file__),'exec'),globals())
