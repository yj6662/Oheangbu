"""Compare axis conversion with the same approved FBX whose Unity pose is serialized."""
import json,sys
from pathlib import Path
import bpy,math
from mathutils import Vector,Euler
from io_scene_fbx import parse_fbx
ROOT=Path('C:/Users/yj666/Oheangbu');OUT=ROOT/'Art/Demo/Summons/WoodDeer'
def clean(value):
    if isinstance(value,bytes):return value.decode('utf-8','replace').replace('\x00','|')
    return value
def props(element):
    p=next((c for c in element.elems if c.id==b'Properties70'),None)
    return {clean(c.props[0]):[clean(v) for v in c.props[4:]] for c in p.elems} if p else {}
def inspect(path):
    data,version=parse_fbx.parse(str(path))
    settings=next(c for c in data.elems if c.id==b'GlobalSettings')
    objects=next(c for c in data.elems if c.id==b'Objects')
    return {'version':version,'globalSettings':props(settings),'models':[{'name':clean(m.props[1]),'type':clean(m.props[2]),'props':props(m)} for m in objects.elems if m.id==b'Model'],
      'animationStacks':[clean(m.props[1]) for m in objects.elems if m.id==b'AnimationStack']}
old=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/WoodDeer/SM_WoodDeer.fbx'
new=ROOT/'Oheangbu/Assets/_Project/Art/Demo/Summons/WoodDeer/SM_WoodDeer_Combat.fbx'
forward=Euler((-math.pi/2,0,0)).to_matrix()@Vector((0,-1,0))
assert (forward-Vector((0,0,1))).length<.000001
result={'approvedStatic':inspect(old),'combatDerivative':inspect(new),
  'nativeForward':[0,-1,0],'fbxNodeConvertedForward':list(forward),'expectedUnityForward':'+Z',
  'unitNote':'Combat FBX uses meters via UnitScaleFactor100 and node scale1. Keep ModelImporter globalScale1/useFileUnits. No extra 100x or -90deg wrapper correction.',
  'verificationScope':'Binary FBX axis/transform comparison and mathematical direction check against approved static FBX. No Unity API invoked.'}
(OUT/'fbx_axis_comparison.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
for label in ('approvedStatic','combatDerivative'):
    row=result[label]
    print(label,json.dumps({'axes':{k:v for k,v in row['globalSettings'].items() if 'Axis' in k or 'Scale' in k},
        'models':[m for m in row['models'] if m['type']!='LimbNode']},ensure_ascii=False),flush=True)
