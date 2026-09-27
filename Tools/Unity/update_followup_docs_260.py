from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
files=['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md',
       'Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md']
line='''
**#260 후보 연결:** 삭의 직선 관통과 악의 동일 대상 재타격을 분리 구현했다. 별도 PreviewScene27개 계약 검사를 통과하고 후보 프로필에 기존 표현 프리팹을 연결했다. 벽·다중 Collider·피하기·수명·시전 시점 능력치·사망 취소를 검사했다. 중앙 작도/먹 소비·라이브 전투·시각 품질은 미검증이다. 해석 경로 결손은65자이고 남은 ㄱ 공격은막·곡·녹·목·속·옥이다. [계약](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-EA-GIYEOK-FOLLOWUP-260.md).
'''
for rel in files:
 p=ROOT/rel;s=p.read_text(encoding='utf-8-sig')
 if '**#260 후보 연결:**' not in s:s=s.replace('<!-- branches-259:end -->',line+'<!-- branches-259:end -->',1)
 if rel.endswith('PLAN-BIBLE-IMPLEMENTATION-TRACKER.md'):
  s=s.replace('각/낙2자 경로. 나머지67자는 실제 해석 경로 없음.','각/낙/삭/악4자 경로. 나머지65자는 실제 해석 경로 없음.')
  s=s.replace('나머지 ㄱ 공격8과 후속 타격을 구현하고','삭/악27개 PreviewScene 검사 뒤 남은 ㄱ 공격6과 후속 타격을 구현하고')
 p.write_text(s,encoding='utf-8')
p=ROOT/'Docs/Specs/SPEC-EA-GIYEOK-FOLLOWUP-260.md';s=p.read_text(encoding='utf-8-sig').replace('상태: 설계/구현 준비. 후보 적용 및 판정 검증 전이다.','상태: 후보 프로필/표현 프리팹 연결, PreviewScene 계약27 PASS. 현재 Editor는 최신 후보의 clean Edit 상태다. 실제 중앙 작도/먹 소비/후보 전투와 표현 검수는 미완료다. 최초 컴파일의 지역 변수 이름 충돌은 수정 후 재컴파일했다. 원시 결과: `Art/World/Compact/Rebuild/Continuation258/giyeok-followup260.txt`.')
p.write_text(s,encoding='utf-8')
p=ROOT/'Docs/Plans/PLAN-CONTINUATION-258.md';s=p.read_text(encoding='utf-8-sig');s+='\n- #260 삭/악을 후보에 연결하고 PreviewScene27개 계약 검사 PASS. 정적 해석 결손65자. 중앙 작도/먹 소비·현장 전투·표현 검증과 ㄱ 공격6종은 남아 있다. 사용자 다른 작업 중 Computer Use/창 활성화/입력 조작 없이 진행한다.\n';p.write_text(s,encoding='utf-8')
print('Updated current documents without overwriting earlier version evidence')
