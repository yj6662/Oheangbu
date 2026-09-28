from pathlib import Path
import hashlib,json
from datetime import datetime,timezone
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Continuation258'
lines=(OUT/'giyeok-followup260.txt').read_text(encoding='utf-8-sig').splitlines()
passed=sum(s.startswith('PASS ') for s in lines);failed=sum(s.startswith('FAIL ') for s in lines)
if passed!=35 or failed:raise RuntimeError(f'Expected 35 current contracts, got {passed}/{failed}')
targets=['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md',
         'Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md','Docs/Plans/PLAN-CONTINUATION-258.md',
         'Docs/Specs/SPEC-EA-GIYEOK-FOLLOWUP-260.md','Tools/Unity/continuation_report_258.py']
for rel in targets:
 p=ROOT/rel;s=p.read_text(encoding='utf-8-sig')
 s=s.replace('PreviewScene27','PreviewScene35').replace('PreviewScene 계약27','PreviewScene 계약35')
 s=s.replace('별도 PreviewScene27','별도 PreviewScene35')
 s=s.replace('중앙 작도/먹 소비·라이브 전투·시각 품질은 미검증이다.', '중앙 해석·먹 1회 소비·먹 부족·단일 이벤트도 같은 PreviewScene에서 검사했다. 실제 손 작도·현장 전투·시각 품질은 미검증이다.')
 s=s.replace('실제 중앙 작도/먹 소비/후보 전투와 표현 검수는 미완료다.', '중앙 해석/먹/이벤트 계약도 PreviewScene에서 검사했다. 실제 손 작도/후보 전투와 표현 검수는 미완료다.')
 s=s.replace('중앙 작도/먹 소비·현장 전투·표현 검증과 ㄱ 공격6종은 남아 있다.', '중앙 해석/먹/이벤트는 PreviewScene에서 추가 검사했다. 손 작도·현장 전투·표현 검증과 ㄱ 공격6종은 남아 있다.')
 s=s.replace('새 술식의 실제 중앙 작도·먹 소비·현장 전투와 미술 검증은 남아 있다.', '중앙 해석·먹·이벤트 계약은 검사했고, 실제 손 작도·현장 전투와 미술 검증은 남아 있다.')
 p.write_text(s,encoding='utf-8')
sources=[ROOT/'Oheangbu/Assets/_Project/Scripts/App/EAGiyeokRuntime.cs',ROOT/'Oheangbu/Assets/_Project/Scripts/App/EAGiyeokRuntime.Traces.cs',
         ROOT/'Oheangbu/Assets/_Project/Scripts/App/CombatLoopWiring.cs',ROOT/'Oheangbu/Assets/_Project/Scripts/Spellcraft/SpellResolver.cs',
         ROOT/'Oheangbu/Assets/_Project/Scripts/Combat/EAGiyeokProfileSO.cs',ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro/CompactGiyeokFollowup260.cs',
         ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/Continuation258/EAGiyeok_TEST.asset']
report={'utc':datetime.now(timezone.utc).isoformat(),'status':'CANDIDATE_CONNECTED','passed':passed,'failed':failed,
        'scope':'Edit PreviewScene with production resolver, ink, accepted events, damage and scene-owned physics. No user input or Play entry.',
        'human_drawing':'NOT_TESTED','candidate_live_combat':'NOT_TESTED','art':'NOT_APPROVED','standalone_gpu':'NOT_MEASURED',
        'source_hashes':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}}
(OUT/'followup260.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'passed':passed,'failed':failed,'status':report['status']}))
