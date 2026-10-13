"""#308 swing3: export_m2.py 를 휘두르기 세 클립만 돌리면 export-report.json 에 그 셋만 남는다 → 백업본(_backup_swing3)의 나머지 항목과 합친다. grip.json 은 다른 클립 값이 그대로인지 견준다."""
import json, os
M = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2')); NEW = ('SwingSweep', 'SwingArc', 'SwingSlam')
old = json.load(open(M + '/_backup_swing3/export-report.json', encoding='utf-8')); new = json.load(open(M + '/export-report.json', encoding='utf-8'))
out = dict(old); out['_reference'] = new['_reference']
for n in NEW: out[n] = new[n]
json.dump(out, open(M + '/export-report.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('MERGE report keys', list(out), 'reference_same', old['_reference'] == new['_reference'])
g0 = json.load(open(M + '/_backup_swing3/grip.json', encoding='utf-8')); g1 = json.load(open(M + '/grip.json', encoding='utf-8'))
print('MERGE grip right_hand_to_brush_root same', g0['right_hand_to_brush_root'] == g1['right_hand_to_brush_root'], '| brush same', g0['brush'] == g1['brush'], '| carry_grip', json.dumps({k: v for k, v in g1['carry_grip'].items()}, ensure_ascii=False))
print('MERGE grip other clips same', all(g0['clips'][n] == g1['clips'][n] for n in g1['clips'] if n not in NEW), '| changed keys in three', {n: [k for k in g1['clips'][n] if g1['clips'][n][k] != g0['clips'][n].get(k)] for n in NEW})
for n in NEW: print('MERGE', n, {k: g1['clips'][n][k] for k in ('length_s', 'frames', 'loop', 'apex_s')}, 'old', {k: g0['clips'][n][k] for k in ('length_s', 'frames', 'loop', 'apex_s')}, 'reimport', {k: out[n]['reimport'][k] for k in ('frame_count_equal', 'names_and_parents_equal', 'max_joint_pos_err_mm', 'take_names')})
