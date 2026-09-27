from pathlib import Path
import json,re
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Detail261'
checks=(OUT/'audit.txt').read_text()
assert 'FAIL ' not in checks
walk=json.loads((OUT/'collision-walk.json').read_text());reverse=json.loads((OUT/'collision-reverse.json').read_text())
assert walk['status']==reverse['status']=='PASS'
block=f'''<!-- detail-261:start -->
**현재 주변 미술 #261 — 약초길 갈림·물증·심부:** 사면에 묻힌 암반·너덜, 소나무/활엽수 군락, 고사리·낮은 풀, 목재 작업 흔적과 지면에 고정한 낙엽·잔가지·자갈을 후보에 적용했다. 기존 산 지형 높이와 통행 좌표는 유지한 주변 미술 보강이며 전체 강토 제작 완료가 아니다. 물증 주변의 원형 풀 배치는 제거했다. 지도 식생 표현과 NavMesh를 새 배치에 맞춰 갱신했다.

최신 배치/재질/접지 자동 검사 {checks.count('PASS ')}개 통과. 새 충돌을 포함한 양방향 Editor 캡슐 이동 {walk['distance']:.2f}m/{reverse['distance']:.2f}m 통과. 동일 시점 1080p 전후 8장 제공. **실제 입력 플레이, 사람의 길찾기, 이동 중 LOD·식생 안정성, CPU/GPU 시간, 사용자 미술 판정은 별도 미검증**이다. 원본 축소맵·일반 저장·기존 지형 및 경로 보호 해시 일치. Computer Use·창 활성화·Play·키/커서 조작 없이 수행했다.

다음은 사용자 화면 검토에 따른 밀도/시야 조정, 사람의 지선 길찾기 검증, 뿌리 굴·신목 등 남은 지선 순서다. #260 전투 검사 결과는 이 미술 변경의 플레이 통과 증거로 사용하지 않는다. [전후 화면](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/DETAIL_REVIEW.html) · [제작 범위](C:/Users/yj666/Oheangbu/Docs/Plans/PLAN-CHEONGRIM-DETAIL-261.md).
<!-- detail-261:end -->
'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md']:
 p=ROOT/rel;text=p.read_text(encoding='utf-8-sig');text=re.sub(r'<!-- detail-261:start -->.*?<!-- detail-261:end -->\s*','',text,flags=re.S);p.write_text(block+'\n'+text,encoding='utf-8')
p=ROOT/'Docs/DECISIONS.md';text=p.read_text(encoding='utf-8-sig')
if '## D261 ' not in text:
 text+='\n\n## D261 — 청림 산허리의 주변 미술 보강 (2026-09-24)\n\n약초길~심부의 산세를 재교체하지 않고 지면에 묻힌 암반과 너덜, 불규칙한 숲과 미세 지면 프롭으로 층을 보강한다. 후보 전용 배치/재질·정적 충돌·NavMesh·지도 식생을 함께 갱신한다. 원본 씬과 일반 저장은 보존한다. 오프스크린 비교와 Editor 충돌 검사는 실제 입력·성능·미술 승인을 대체하지 않는다. Computer Use 없이 수행한다.\n'
 p.write_text(text,encoding='utf-8')
print('Updated six current records and decision log')
