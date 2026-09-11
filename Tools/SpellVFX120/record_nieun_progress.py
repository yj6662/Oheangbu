from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2]
out=ROOT/'Art/SpellVFX120'
profiles=ROOT/'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles'
before=json.loads((out/'na45_before_profiles.json').read_text())
changed=[name for name,sha in before.items() if hashlib.sha256((profiles/name).read_bytes()).hexdigest()!=sha]
assert changed==['025_B098.asset'],changed
(out/'na45_scope_check.json').write_text(json.dumps(dict(status='PASS',changedProfiles=changed,unchangedProfiles=119,giyeokProfilesUnchanged=True,meshGenerationRequests=0),ensure_ascii=False,indent=2),encoding='utf-8')
review=dict(status='USER_REVIEW_COMPLETE',date='2026-09-10',scope='ㄱ VFX20종',userMessage='비주얼 검토 완료했어. 이제 ㄴ 계열 VFX 진행해.',meaning='User review complete and next initial requested; no new automated art rating.')
(out/'GIYEOK_USER_REVIEW.json').write_text(json.dumps(review,ensure_ascii=False,indent=2),encoding='utf-8')
p=out/'GIYEOK_REVIEW.html'
s=p.read_text(encoding='utf-8').replace('비주얼 최종 판정은 사용자 검토 대기입니다.','2026-09-10 사용자 비주얼 검토 완료. 후속 ㄴ 계열 제작으로 진행합니다.').replace('AWAITING_USER_REVIEW','USER_REVIEW_COMPLETE')
p.write_text(s,encoding='utf-8')
p=out/'GIYEOK_REPORT.md';s=p.read_text(encoding='utf-8').replace('비주얼 최종 판정은 **사용자 검토 대기**다.','비주얼은 **2026-09-10 사용자 검토 완료**로 갱신했다.').replace('| 미술 품질 | 사용자 검토 대기 | 에이전트의 미술 PASS 없음 |','| 미술 검토 | 사용자 검토 완료 | 2026-09-10 후속 ㄴ 제작 지시. 자동 미술 PASS 아님 |').replace('- 사용자 비주얼 검토와 피드백 반영.','- 추후 사용자 피드백이 있으면 반영.')
p.write_text(s,encoding='utf-8')
print(json.dumps(dict(status='PASS',changed=changed),ensure_ascii=False))
