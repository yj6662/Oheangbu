"""Publish current, passing candidate evidence to the project status documents."""
from pathlib import Path
import re
import json
import hashlib

ROOT=Path(__file__).resolve().parents[2]
DATA=ROOT/'Art/World/Compact/Rebuild/Sinmok263'

def main():
    reports={}
    for key in ['checks','combat-checks']:
        lines=(DATA/(key+'.txt')).read_text(encoding='utf-8-sig').splitlines()
        assert not any(x.startswith('FAIL ') for x in lines), key
        reports[key]=sum(x.startswith('PASS ') for x in lines)
    assert reports['checks']>=33 and reports['combat-checks']==40, reports
    protected=json.loads((DATA/'protected-before.json').read_text(encoding='utf-8-sig'))
    assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest()==h for p,h in protected.items())
    block=f'''<!-- sinmok-263:start -->
**현재 후보 #263 — 뿌리 굴·선택 신목·강토 표현 (2026-09-25):** 벌목장→뿌리 굴→심부 복귀와 심부→신목 접근로를 연결했다. 신목은 사용자 답변대로 청룡 전에 만나는 선택 보스이며 본선 필수 조건이 아니다. 45개 부위 스킨 메시/45개 뼈와 대기·네 공격 클립을 조립하고, 실제 공격 시계·상체 방향·피해·사망 취소를 연결했다. HP520/처치260통보/물증60통보는 TEST다. 청룡의 종성·국·仁 보상과 기존 저장 슬롯/v8은 유지한다.

도착/지도 이름은 `청림 · 뿌리 굴`처럼 강토와 현장을 병기한다. 24개 카탈로그 영역, 걸어간 갱도 공개, 지도 오방색 8.5%, 카메라 전용 하늘색 최대5.5%와 구름, 높이145~300m의 이동·생성·소산 산안개를 후보에 적용했다. 지면/프롭의 강토별 색은 후속이다.

**검증:** 최신 구성·저장·셰이더·NavMesh {reports['checks']}개와 실제 신목 복제본의 전투/PlayableGraph {reports['combat-checks']}개 통과. 별도로 공유 전투 엔진138개, 도착/영구 발견24개 회귀 검사 통과. 후보 NavMesh를 다시 굽고 네 새 경로 연결을 확인했다. 원본 씬/실제 저장/백업 보호 해시3개 일치. 중단된 초기 fixture와 잘못된 동일 씬 NavMesh 검사 결과는 폐기 증거로 별도 보관했다. 실제 입력 플레이·난이도·시각 판정·CPU/GPU 시간은 미검증이며 미술 완료나 EA 전체 완료가 아니다.

테스트는 `Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity`. 원본 `W_Demo_Compact`는 보존했다. Computer Use·Play 진입 없이 후보를 저장된 Edit 상태로 남겼다. [화면/검사](C:/Users/yj666/Oheangbu/Art/World/Compact/Rebuild/SINMOK_REVIEW.html) · [사양](C:/Users/yj666/Oheangbu/Docs/Specs/SPEC-SINMOK-REALMS-263.md).
<!-- sinmok-263:end -->'''
    for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md',
                'Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md',
                'Docs/Plans/PLAN-BIBLE-IMPLEMENTATION-TRACKER.md']:
        path=ROOT/rel;text=path.read_text(encoding='utf-8-sig')
        assert '<!-- sinmok-263:start -->' in text, rel
        text=re.sub(r'<!-- sinmok-263:start -->.*?<!-- sinmok-263:end -->',lambda _:block,text,count=1,flags=re.S)
        path.write_text(text,encoding='utf-8')
    path=ROOT/'Docs/Specs/SPEC-SINMOK-REALMS-263.md'
    text=path.read_text(encoding='utf-8-sig').split('## 적용 기록')[0].rstrip()
    text+='''

## 적용 기록

- 후보에 부위별 절차 모델링 메시 45개와 45개 뼈, 스킨 웨이트, 대기+4공격 PlayableGraph를 적용했다. 뿌리는 고정하고 상체와 공격 가지가 잠긴 공격 방향으로 돌아간다.
- 새 NavMesh 경로는 벌목장 접근 약261m, 갱도 약100m, 심부 복귀 약369m, 신목 접근 약489m이다. 이는 길찾기 길이이며 수동 보행 시간이 아니다.
- 신규 구성/저장/셰이더/내비게이션33개, 신목 실제 복제본 전투40개, 공유 전투 엔진138개가 통과했다. 통합 입력 플레이·보스 난이도·공격 시각 판정·GPU/CPU 실측·사용자 미술 승인은 남아 있다.
- 같은 씬을 다시 Additive로 열 때 원래 지형까지 비활성화하던 NavMesh 제작 도구를 수정하고 다시 구웠다. 첫 전투 fixture의 반복 처리도 유한 반복과 독립 시계로 수정했다. 실패/중단 결과는 새 실행의 통과 증거로 사용하지 않는다.

## 다음 확인 순서

1. 사람이 마커 없이 벌목장→뿌리 굴→심부→신목을 찾아가며 출입구·카메라·휴식 위치를 확인한다.
2. 실제 입력으로 내려치기·휩쓸기·뿌리·씨앗 공격의 예고와 판정, 회피·금 패링·죽음/휴식/재실행을 검증한다. 신목을 지나쳐 청룡에 가는 경우도 확인한다.
3. 신목 실루엣과 동굴 암반 접합부를 사용자 미술 검토로 다듬고, 이동 중 식생·하늘 경계 전환·산안개와 CPU/GPU 시간을 측정한다.
4. 지면/프롭의 강토색과 나머지 강토 본 제작, 남은 시스템/EA 항목은 기존 추적표에서 이어간다.
'''
    path.write_text(text,encoding='utf-8')
    (DATA/'build.txt').write_text('Candidate authored and saved. Current evidence: checks.txt, combat-checks.txt, ../migration_navigation.txt. Manual play, visual approval and CPU/GPU timing remain unverified.\n',encoding='utf-8')
    print(json.dumps(reports))

if __name__=='__main__': main()
