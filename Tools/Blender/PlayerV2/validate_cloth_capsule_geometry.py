"""Regression check for real collision mesh and analytic zero-length spheres."""
import importlib.util
import json
from pathlib import Path
import bpy
import bmesh
import numpy as np

ROOT = Path(__file__).resolve().parents[3]
spec = importlib.util.spec_from_file_location('cloth_fixture', Path(__file__).with_name('diagnose_cloth_blender.py'))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
collection = bpy.data.collections.new('CapsuleFixture')
bpy.context.scene.collection.children.link(collection)
rows = []
for name,a,b,r in [('sphere',(0,0,1),(0,0,1),.076670587), ('capsule',(.1,.2,.3),(-.1,.6,.8),.05)]:
    proxy = fixture.capsule(collection,name,a,b,r)
    points = np.array([tuple(v.co) for v in proxy['object'].data.vertices])
    axis = np.array(b)-a
    length2 = axis@axis
    t = np.clip(((points-a)@axis)/length2,0,1) if length2>1e-18 else np.zeros(len(points))
    radius_error = float(np.max(np.abs(np.linalg.norm(points-(a+t[:,None]*axis),axis=1)-r)))
    mesh = bmesh.new()
    mesh.from_mesh(proxy['object'].data)
    boundary = sum(e.is_boundary for e in mesh.edges)
    nonmanifold = sum(not e.is_manifold for e in mesh.edges)
    volume = mesh.calc_volume(signed=True)
    mesh.free()
    centre = (np.array(a)+b)*.5
    inside = fixture.penetration_samples(np.array([centre]),[proxy])['body']
    outside = fixture.penetration_samples(np.array([centre+np.array([2,2,2])]),[proxy])['body']
    assert np.isfinite(points).all() and radius_error<1e-7
    assert boundary==0 and nonmanifold==0 and volume>0
    assert inside['count']==1 and abs(inside['depth']-r)<1e-7 and outside['count']==0
    rows.append({'name':name,'maxRadialErrorMeters':radius_error,'boundaryEdges':boundary,'nonManifoldEdges':nonmanifold,'signedVolume':volume,'inside':inside,'outside':outside})
out = ROOT/'Art/PlayerV2/Inspect/ClothBlender/capsule-geometry-regression.json'
out.write_text(json.dumps({'status':'PASS','validatorSha256':fixture.digest(Path(fixture.__file__)),'fixtures':rows},indent=2),encoding='utf-8')
print(out)
