import json, os, sys
for k in ('male','female'):
    R=json.load(open(f'Art/World/PackLite308/people/_work/probe_{k}.json',encoding='utf-8'))
    print('==',k)
    for o in R['objects']: print(' obj',o['name'],o['type'],o['parent'],[round(v,3) for v in o['rot']],[round(v,3) for v in o['scale']])
    a=R['arm']; print(' bones',len(a['bones']), 'roots',[b['n'] for b in a['bones'] if not b['p']])
    print('  ',' '.join(b['n']+('' if b['deform'] else '!') for b in a['bones']))
    for b in a['bones']:
        if b['n'] in('b_Head','b_L_Foot','b_Pelvis','b_Root','b_L_Hand'): print('  ',b['n'],b['head'])
    for m in R['meshes']:
        print(' mesh',m['name'],m['verts'],m['tris'],[round(v,3) for v in m['min']],[round(v,3) for v in m['max']],m['uv'],'groups',m['groups'],'used',len(m['used_groups']),'sk',m['shapekeys'])
        for s in m['mats']: print('    ',s['i'],s['name'],s['tris'],[os.path.basename(t.replace(chr(92),'/')) for t in s['tex']])
    print(' images',[(i[0],i[2],i[3]) for i in R['images']][:40])
