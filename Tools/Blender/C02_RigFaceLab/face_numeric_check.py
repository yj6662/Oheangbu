"""Recheck C3 against the connected facial skin only, excluding foreground hair."""
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu')
source=(ROOT/'Tools/Blender/C02_RigFaceLab/face_validate_c3.py').read_text(encoding='utf-8').split('for value,label in ')[0]
source=source.replace("[t.vertices[:] for t in body.data.loop_triangles],all_triangles=True)",
    "[body.data.loop_triangles[i].vertices[:] for i in json.loads((OUT/'face_surface_triangle_ids.json').read_text(encoding='utf-8'))['triangles']],all_triangles=True)")
exec(compile(source,'face_numeric_check_reused.py','exec'),globals())
result={'candidate':'C3','preservation':preservation,'checks':checks,
 'method':'Original 448-triangle facial skin component selected from measured eye landmarks. Foreground hair is excluded from depth and pupil-occlusion comparisons.',
 'scope':'Vertex offset and nine pupil-landmark ray occlusion samples, all five channel strengths plus zero return. These are NOT a full anatomical eyelid/eyeball collision test.',
 'all_finite':all(c['finite'] for c in checks),
 'closed_pupil_coverage':{c['mesh']:c['pupil_covered_samples_of_9'] for c in checks if c['value']==1},
 'zero_pupil_coverage':{c['mesh']:c['pupil_covered_samples_of_9'] for c in checks if c['value']==0},
 'source_face_penetration_samples_over_0_1mm':sum(c['vertices_behind_source_over_0_1mm'] for c in checks),
 'minimum_skin_offset_m':min(c['minimum_vertex_offset_in_front_of_source_m'] for c in checks)}
(FOLDER/'skin_component_validation.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print('FACE_SKIN_COMPONENT_CHECK',json.dumps(result))
