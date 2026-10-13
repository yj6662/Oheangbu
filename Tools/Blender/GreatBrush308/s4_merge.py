"""#308 s4: export_m2.py 를 몇 클립만 돌리면 export-report.json 에 그 클립만 남는다 → 누적본(export-report.all.json, 처음엔 _backup_swing3 것)에 합쳐 다시 쓴다."""
import json, os, sys
M = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'Art', 'Characters308', 'GreatBrush308', 'motion2')); ACC = M + '/export-report.all.json'
acc = json.load(open(ACC if os.path.exists(ACC) else M + '/_backup_swing3/export-report.json', encoding='utf-8')); new = json.load(open(M + '/export-report.json', encoding='utf-8'))
acc.update(new); json.dump(acc, open(ACC, 'w', encoding='utf-8'), ensure_ascii=False, indent=1); json.dump(acc, open(M + '/export-report.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
g = json.load(open(M + '/grip.json', encoding='utf-8'))
print('MERGE report clips', [k for k in acc if k != '_reference'], '| new', [k for k in new if k != '_reference'])
print('MERGE grip', {n: (c['length_s'], c['apex_s'], c['frames'], c['loop']) for n, c in g['clips'].items()})
print('MERGE two-hand spread', g['right_hand_to_brush_root']['spread_over_two_hand_clips'], '| carry same', g['carry_grip']['same_as_two_hand'], g['carry_grip']['spread_vs_two_hand'])
