"""#308 PeopleLite: _work/<Body>/{stats,verify,layout,textures}.json -> people/manifest.json + manifest.md"""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CFG = json.load(open(os.path.join(HERE, 'peoplelite308.json'), encoding='utf-8')); OUT = os.path.join(ROOT, CFG['out'])


def load(n, f): return json.load(open(os.path.join(OUT, '_work', n, f), encoding='utf-8'))


bodies = []
for b in CFG['bodies']:
    n = b['name']; s = load(n, 'stats.json'); v = load(n, 'verify.json'); lay = load(n, 'layout.json'); t = load(n, 'textures.json'); src = CFG['sources'][b['src']]
    bodies.append(dict(name=n, label=b['label'], source=b['src'], fbx=s['fbx'], fbx_bytes=s['fbx_bytes'], root=s['root'], lod_meshes=['%s_LOD%d' % (n, i) for i in range(3)],
                       lod_triangles=s['lod_triangles'], lod_vertices=s['lod_vertices'], lod_max_influences=CFG['lodInfluences'], skinned_bones=s['skinned_bones'][0], transforms=s['transforms'],
                       bones_deleted=len(s['bones_deleted']), bones_kept_unskinned=s['bones_unskinned'], materials=1, alpha_clip=lay['alpha'], atlas=lay['size'],
                       textures={k: p for k, p in t.items() if k != 'orig'}, height_m=s['height_m'], head_bone_height_m=s['head_bone_z'], source_triangles=s['source']['triangles'], source_bones=s['source']['bones'],
                       source_material_slots=s['source']['slots'], dropped_materials=b['drop'], hidden_removed=s['hidden_removed'], parts=s['parts'], static_bake_max_mm=s['static_bake_max_mm'],
                       fbx_settings_equal_to_source=s.get('fbx_settings', {}).get('equal'), skeleton=v['skeleton'], rest_fit_mm=v['rest_fit_mm'], fit_excluded_vertices=v['fit_excluded_vertices'],
                       pose_deviation=v['pose'], walk=v['walk'], clips={k: dict(c, pack_path='Assets/CustomPackage/' + src[k]) for k, c in v['clips'].items()}, sheets=v['sheets']))
M = dict(id=CFG['id'], version=CFG['version'], status=CFG['status'], pack=CFG['pack'], variants=[dict(key=v['key'], label=v['label'], weight=v['weight']) for v in CFG['variants']],
         clip_note='팩 클립은 FBX 가 아니라 Unity .anim(Generic, 경로 곡선)이다. 가벼운 몸은 같은 뼈 이름 · 같은 계층 · 같은 바인드 행렬이라 그대로 물린다. 지운 뼈의 곡선은 Unity 가 건너뛴다. 전부 제자리 클립(뿌리 이동 없음).',
         bodies=bodies)
json.dump(M, open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
L = ['# PeopleLite308 — 군중용 가벼운 평민 몸 (' + CFG['version'] + ')', '', '> ' + CFG['status'] + ' · 원본: ' + CFG['pack'], '> 다시 만들기: `python Tools/Blender/PeopleLite308/run.py`', '',
     '| 몸 | 원본 tris / 뼈 / 재질 칸 | LOD0 / 1 / 2 tris | 스킨 뼈 (트랜스폼) | 재질 | 키 m | 걷기 보폭 m/바퀴 (m/s) | 걷기 편차 mm 평균 / p95 / p99 / 최대 |', '|---|---|---|---|---|---|---|---|']
for b in bodies:
    w = b['pose_deviation']['walk']
    L.append('| %s — %s | %d / %d / %d | %s | %d (%d) | 1 (알파 클립 %s) | %.2f | %.3f (%.3f) | %.2f / %.2f / %.2f / %.1f |' % (
        b['name'], b['label'], b['source_triangles'], b['source_bones'], b['source_material_slots'], ' / '.join(map(str, b['lod_triangles'])), b['skinned_bones'], b['transforms'], '예' if b['alpha_clip'] else '아니오',
        b['height_m'], b['walk']['stride_metres_per_cycle'], b['walk']['stance_foot_speed_mps'], w['mean_mm'], w['p95_mm'], w['p99_mm'], w['max_mm']))
L += ['', '색 변형(몸마다 1024 아틀라스 한 장씩, 메시는 같다): ' + ' · '.join('%s %s %d %%' % (v['key'], v['label'], round(v['weight'] * 100)) for v in CFG['variants']), '',
      '클립(팩의 .anim, Generic): ' + ' · '.join('%s %.2f s' % (c['name'], c['length_s']) for b in bodies[:1] + bodies[-1:] for c in b['clips'].values()) + ' — 전부 제자리.', '', M['clip_note'], '',
      '한계: 손가락 뼈 30개를 손으로 합쳤다(걷기 클립의 느슨한 손 모양으로 구움 — 서기 · 뛰기 클립의 손 모양과는 최대 수 cm 다르다). 아낙은 겉 두른치마와 머리카락 가닥 판을 뺐고 치마 끝 뼈(004)를 003 으로 합쳤다.',
      '편차는 가벼운 LOD0 정점과 원본 표면 대응점 사이 거리의 변화다(정지 자세에서 3 mm 안에 붙은 정점만; 뺀 수는 manifest.json fit_excluded_vertices).', '',
      '시트: ' + ' · '.join(s for b in bodies for s in b['sheets'])]
open(os.path.join(OUT, 'manifest.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n'); print('MANIFEST', len(bodies), 'bodies ->', os.path.join(OUT, 'manifest.md'))
