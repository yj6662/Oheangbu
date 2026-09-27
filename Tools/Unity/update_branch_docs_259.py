from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
files=['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md',
       'Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md']
block='''<!-- branches-259:start -->
**현재 후속 #259 — 약초꾼 지선 후보 연결:** 본선 분기→약초꾼 길→심부의 약810m 우회로와 바구니 물증을 추가했다. 수주 전 발견이 영구 사실로 남고 주막 약초꾼의 늦은 대사에 반영된다. 추가 재화나 본선 필수 조건은 없다. 지도/원장/식생/시각 참조12개, 진행 조건·실제 원자 파일 저장9개를 Editor에서 검사했다. 양방향 캡슐 충돌 이동808.11m/807.97m는 실제 입력이나 수동 길찾기 검증이 아니다.

후보 Campaign은 선택 물증을 포함해21단계이며 단계 수는 게임 완료율이 아니다. 원본 축소맵·일반 저장은 보존한다. **사용자 요청으로 Computer Use·창 활성화·키/커서 조작 없이 진행한다.** 라이브 휴식/사망 재검사·수동 탐험·주변 숲 미술·성능 실측은 미완료다. 뿌리 굴/신목과 ㄱ 공격 후속을 이어간다. [지선 기록](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/BRANCH_REVIEW.html), [제작 계획](C:/Users/yj666/Oheangbu/Docs/Plans/PLAN-CHEONGRIM-BRANCHES-259.md).
<!-- branches-259:end -->

'''
for rel in files:
    p=ROOT/rel;s=p.read_text(encoding='utf-8-sig')
    if '<!-- branches-259:start -->' in s:
        start=s.index('<!-- branches-259:start -->');end=s.index('<!-- branches-259:end -->',start)+len('<!-- branches-259:end -->')
        s=s[:start]+block.rstrip()+s[end:]
    else:s=block+s
    if rel.endswith('PLAN-BIBLE-IMPLEMENTATION-TRACKER.md'):
        s=s.replace('**다음 공통 제작은 성저 객주 통행 보정 → 남문 장수/개문이다.**','**현재 다음 제작은 #259 탐험 지선과 #260 ㄱ 공격 후속이다. 아래 #252 요약은 과거 이력이다.**')
        s=s.replace('뿌리 굴·약초꾼 길·신목 위험 지선·현장 단서·숲 외형·지름길은 잔여.', '약초길은 #259 후보 연결/왕복 캡슐·저장 계약 검사. 뿌리 굴·신목·숲 외형·추가 지름길은 잔여.')
    p.write_text(s,encoding='utf-8')
p=ROOT/'Docs/Plans/PLAN-CONTINUATION-258.md';s=p.read_text(encoding='utf-8-sig').replace('**대기 — 탐험 지선:**','**진행 중 #259 — 탐험 지선:**').replace('높이표 경로 후보만 작성했으며 실제 지선/단서/동굴/식생은 아직 미구현이다.','약초길과 바구니 물증은 후보에 연결하고 Edit 참조/저장/왕복 충돌 검사를 통과했다. 실제 입력 보행·라이브 휴식/사망·미술과 뿌리 굴/신목은 잔여다.')
p.write_text(s,encoding='utf-8')
p=ROOT/'Docs/Plans/PLAN-CHEONGRIM-BRANCHES-259.md';s=p.read_text(encoding='utf-8-sig');s=s.replace('— 제작 준비','— 약초길 후보 연결')
s+='''
## 적용 기록

- 약초길 두 구간은 실제 NavMesh 경로287.63m/521.91m로 연결했다. 양방향 실제 크기 Capsule 충돌 검사808.11m/807.97m PASS. 창 포커스나 입력 장치를 사용하지 않은 Edit 검사다.
- 참조12 PASS, 발견 순서/배열 순서/반복/원자 파일 실패·재시도·재로드9 PASS. 새로운 `herb_trace259` 선택 단계와 `evidence:herb_black_root259` 사실을 기존 전체 저장에 넣는다. 원본/일반 저장은 변경하지 않는다.
- 검은 길 띠는 동굴 바닥 재질을 공유한 것이 원인이었다. 새 지선에 전용 가장자리 혼합 재질을 적용했다. 식생은 바구니 주변의 작은 풀 군락까지 추가했으며 숲의 밀도/공간 구성은 미술 잔여다.
- 라이브 NPC 상호작용/휴식/사망/재실행과 실제 입력 보행은 별도 검사 대상으로 남긴다. 뿌리 굴/신목은 아직 지형 표본상의 경로 제안이다.
'''
p.write_text(s,encoding='utf-8')
print('Updated six current-state documents and two execution plans')
