from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
note='''<!-- reworld292:start -->
**#292 한국 산지·산사 기반 전체 맵 재설계 — 구현 중 (2026-09-26):** 사용자 승인에 따라 약4×6km 전체 지형·수계·길·거점·식생·표면을 재설계한다. #291 부분 보강 및 초가 본당 고정은 대체한다. 실제 한국 지형과 재료를 기반으로 수묵담채화하고, 사찰은 부석사/봉정사 실물 비례를 기준으로 보유 한국 건축 부재를 변형한다. Honhwagak 관아 그대로의 법당은 채택하지 않는다. 무료 에셋 보완 허용. 안정적 콘텐츠ID/진행/저장v8은 유지하고 새 후보/슬롯에서 검증한다. 복구본 `Art/World/Compact/Rebuild/Reworld292/Recovery.zip`과 해시 원장을 확보했다. 새 미술·수동 플레이·성능은 아직 미검증이다. 제작 계약: `Docs/Specs/SPEC-COMPACT-REWORLD-292.md`. 다음 순서: 전체 지형/단일 표면 → 거점·경로·한국 사찰/식생 → 실제 화면·통행·회귀·성능 검증. 과거 자동검사나 미술 승인을 새 결과로 승계하지 않는다.
<!-- reworld292:end -->

'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/DECISIONS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Specs/SPEC-COMPACT-MOUNTAINS-290.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md']:
    p=ROOT/rel;s=p.read_text(encoding='utf-8-sig')
    if '<!-- reworld292:start -->' not in s:p.write_text(note+s,encoding='utf8')
print('Current contract inserted; historical decisions retained.')
