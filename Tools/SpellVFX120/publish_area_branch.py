from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'Tools/SpellVFX120/publish_area_five.py').read_text(encoding='utf-8').replace('AreaFive','AreaBranch').replace('AREA_FIVE_REVIEW.html','AREA_BRANCH_REVIEW.html').replace('area_five_review.html','area_branch_review.html')
source=source.replace("for glyph in '고노소모오':","for glyph in '모':").replace("'013_ACE0','037_B178','061_BAA8','085_C18C','109_C624'","'061_BAA8'").replace('clips=20,stills=40','clips=4,stills=8').replace('profilesOutsideFiveUnchanged=True','profilesOutsideMoUnchanged=True,spellBookUnchanged=True')
page=(ROOT/'Tools/SpellVFX120/area_rift_review.html').read_text(encoding='utf-8').replace('AreaRift/','AreaBranch/').replace('고·노·소·모 — 가시성·지맥 분출','모 — 하나의 큰 줄기에서 분기')
page=page.replace('3초 가시, 낮아진 화염 발사구, 밝은 은빛 송곳, 지면을 따라 전진하는 균열과 돌가루를 비교합니다.','굵은 주 균열 하나 → 두 갈래 → 네 개의 가는 균열로 이어지며 돌가루가 경로를 따라 분출합니다.')
page=page.replace("['고','노','소','모']","['모']").replace("||'고'","||'모'").replace("glyph='고'","glyph='모'")
start=page.index('<details open><summary>변경값과 검증 범위</summary>');end=page.index('<p>1080p',start)
page=page[:start]+'''<details open><summary>변경값과 검증 범위</summary><p>기존 일곱 갈래 동시 출발을 큰 줄기 하나에서 단계적으로 분기하는 구조로 바꿨습니다. 주 줄기는 경로30%까지, 두 중간 줄기는62%까지, 네 작은 줄기는 끝까지 전진합니다. 폭은 주 줄기46→28cm, 중간24→13cm, 끝 가지12→5.5cm로 줄어듭니다.</p><p>돌가루는 그 시점의 줄기와 갈림점을 따라 발생합니다. 피해 폭12m·위력5·속도7m/s는 유지했습니다.</p>
'''+page[end:]
page=page.replace('직전 AreaFlow 설정','직전 AreaRift 설정').replace('href="AREA_FLOW_REVIEW.html"','href="AREA_RIFT_REVIEW.html"').replace('모 — 일곱 갈래 균열·낮은 돌가루 분출 / 실제 폭12m·위력5 유지','모 — 큰 줄기 하나 → 두 갈래 → 네 개의 작은 줄기')
page=page.replace('기본 10종 회귀','이전 기본 10종 회귀').replace('href="AreaBranch/contact_audit.json"','href="AreaRift/contact_audit.json"')
(ROOT/'Tools/SpellVFX120/area_branch_review.html').write_text(page,encoding='utf-8')
exec(compile(source,'publish_area_branch_generated','exec'))
OUT=ROOT/'Art/SpellVFX120/AreaBranch';scope=json.loads((OUT/'scope.json').read_text(encoding='utf-8'));audit=json.loads((OUT/'audit.json').read_text(encoding='utf-8'));assert audit['status']=='PASS'
report=json.loads((OUT/'모/report.json').read_text(encoding='utf-8'))
text=f'''# 모 — 하나의 큰 줄기에서 분기

2026-09-10 사용자 요청. 모만 변경했다. 이전 AreaRift 모 프로필·영상과 다른119프로필을 보존했다.

- 주 줄기1개는 경로0~30%, 중간 줄기2개는30~62%, 작은 줄기4개는62~100%에 배치한다.
- 선 너비는 주 줄기46→28cm, 중간24→13cm, 작은 줄기12→5.5cm다. 같은 시각에 겹친7개 선으로 주 줄기를 흉내 내지 않고 실제 세그먼트를 분리했다.
- 부모 끝점과 자식 시작점은 동일한 좌표·지면 높이를 사용한다. 돌가루·자갈의 발생 위치도 경로 단계에 맞는 줄기로 변경했다.
- 기존 경과 시간·전선 속도7m/s·피해 폭12m·위력5는 유지했다. 단일 줄기 구간도 게임 피해 폭은 기존12m이며, 시각적 줄기 폭 자체를 새 판정으로 사용하지 않는다.

## 검증

- 통과: 분기 전용 {len(audit['checks'])}개 검사. 갈림점 연결, 1→3→7개 활성 세그먼트, 단계별 두께 감소, 입자 발생·종료, 공유 피해 계획 불변. 30/60/120fps·0.2배 효과 시간 샘플링 포함.
- 통과: 실제 C2 예약 피해·접촉 경로 촬영. 이전/수정 플레이 각각 피해{report['clips'][0]['damageEvents']}/{report['clips'][1]['damageEvents']}회, 범위 밖0회, 종료 후 잔존 없음.
- 통과: SpellBook 전체·다른119프로필·공급자 원본 해시 유지. 1080p 비교4영상·이미지8개, 전체 디코딩 완료. 촬영 시작 최대 커밋{scope['maxCommitRatio']:.1%}.
- 미검증: 손글씨 입력·적 AI 전투 전체, 전체 게임 성능과 모든 지형/카메라 각도. 기본10종 회귀는 변경 전 AreaRift 기록을 연결하며 이번에 재실행했다고 주장하지 않는다.
- 비주얼 판단: 사용자 검토 대기.

결과는 AREA_BRANCH_REVIEW.html, AreaBranch/모의 영상·프레임·report.json·settings.json, audit.json·scope.json이다. Unity 프로필 사본은 Assets/_Project/Art/SpellVFX120/AreaBranch에 보존했다.
'''
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
status=ROOT/'Docs/PROJECT_STATUS.md';current=status.read_text(encoding='utf-8');entry=f"**2026-09-10 · 모 줄기 분기:** 하나의 굵은 균열→두 갈래→네 작은 갈래로 변경하고 돌가루 경로를 공유했다. 전용 검사{len(audit['checks'])}개·C2 비교4영상 통과. 모 외119프로필·SpellBook 보존. 미술 판단·전체 입력/AI·성능은 미검증. [비교](http://127.0.0.1:8771/AREA_BRANCH_REVIEW.html) · [보고서](../Art/SpellVFX120/AreaBranch/REPORT.md).\n\n"
if not current.startswith('**2026-09-10 · 모 줄기 분기:'):status.write_text(entry+current,encoding='utf-8')
