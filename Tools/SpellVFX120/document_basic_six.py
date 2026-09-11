import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/BasicSix'
scope=json.loads((OUT/'scope.json').read_text(encoding='utf-8'))
audits=[json.loads((OUT/n).read_text(encoding='utf-8')) for n in ['contact_audit.json','ktp_contact_play_audit.json']]
assert scope['status']=='PASS' and all(a['status']=='PASS' for a in audits)
text='''# 기본 술식 사·마·아·서·머·어 — KTP 적용 결과

2026-09-10. 사용자 요청에 따라 기존 사각 문양 방패와 빠른 발동 방식을 여섯 기본 술식에 적용했다. 최종 비주얼 판단은 사용자 검토 대기다.

## 구현

| 술식 | 공격·방어 표현 | 접촉 표현 |
|---|---|---|
| 사 | 기존 빠른 은빛 송곳, 우측 하단의 짧은 금 문양에서 출발 | 확대된 금 KTP 명중 효과 |
| 마 | 기존 포물선 바위, 우측 하단의 짧은 토 문양에서 출발 | 확대된 토 KTP 명중 효과 |
| 아 | 기존 느린 유도탄 표현, 우측 하단의 짧은 수 문양에서 출발 | 확대된 수 KTP 명중 효과 |
| 서 | 중앙 금 Bottom 문양을 가진 반투명 사각 방패 | 작은 문양1개 + 금속 조각18 + 짧은 불꽃20 |
| 머 | 중앙 토 Bottom 문양을 가진 반투명 사각 방패 | 작은 문양1개 + 돌조각20 + 돌가루14 |
| 어 | 중앙 수 Bottom 문양을 가진 반투명 사각 방패 | 작은 문양1개 + 물방울28 + 물보라16 |

공격은 화면 좌표(0.60,0.40), 깊이1.6m에서 발사한다. 발동은0.32초, 입자 알파 최고0.35, NativeScale을 포함한 문양 배율0.264다. 명중 크기는 공통 접촉의3.5배(최종0.77), 밝기3.5, 표적 앞 배치 보정0.4m다. 실제 피해·표적·착탄 시계는 변경하지 않았다.

방패는 폭1.8m×높이2.2m, 중앙 문양 약1.05m다. 별도 발동 효과와 이전 방어 본체는 새 프로필에서 대체한다. 접촉 문양 수명0.3초, 전체 접촉 배율0.44를 사용하고 반성공은 기존 절반 크기 규칙을 따른다. 금속·돌·물의 파편은 단발 Particle System이며 지속 화염을 추가하지 않았다.

## 검증

- 통과: 선택10종(기존4종 포함) 명중·피해0·사망 표적·성공·반성공·방어 교체·실패·해제 검사78개.
- 통과: 기존 받아치기·피격 회귀 검사107개.
- 통과: C2 런타임 시험24클립. 클립마다 접촉1회, 공격 피해1회, 중복 명중 문양 없음, 종료 후 잔존 없음. 빠른 발동 원점·종료와 공격 화면 발사 좌표 확인.
- 통과: 여섯 프로필의 본체 메시·재질·비행 시간·Lift·Count·Size·PartScale·Duration·색상 총54항목 보존.
- 통과: KTP 공급자 원본과 선택 여섯 외114개 프로필의 해시 동일. 기존 가·나·거·너도 변경하지 않았다.
- 미검증: 사용자 최종 미술 판단, 손글씨 입력부터 실제 적 AI까지의 전체 전투, 움직이는 표적에 대한 유도 성능, 다중 시전 성능·벽 근접 상황. 이번 작업은 해당 기존 게임 로직을 변경하지 않았다.

영상은 고정한 C2 카메라와 임시 캡슐 표적·시간 지정 입사 신호로 촬영했다. 실제 CombatLoopWiring의 예약 피해와 ParryJudge를 사용하지만 전체 적 AI 플레이 영상은 아니다. 공격 시험 시계는 기존 프로필 Flight(사0.16초, 마0.95초, 아1.2초)를 before/after 양쪽에 동일하게 전달했다. 실제 게임의 거리·속도 기반 예약 시계와 구분한다. 방어 접촉은0.25초에 시험했다.

## 결과 파일

- [비교 페이지](../BASIC_SIX_REVIEW.html): 6종×기존/수정×플레이/외부 =1080p·24fps·6초 영상24개, 정지 이미지48개. 모든 MP4 인코딩 후 전체 디코딩 확인.
- 각 글자 폴더: report.json, settings.json, play_before/after 및 external_before/after 영상·프레임.
- contact_audit.json, ktp_contact_play_audit.json, scope.json: 수치 검사 증거.
- Unity 원본 백업: Assets/_Project/Art/SpellVFX120/KtpEmphasis/BaselineBasicSix_글자.asset.
- Unity 신규 에셋: QuickCast/TransparentShield/RectShield/SmallPatternContact/ElementDebrisContact의 가족 번호2(토)·3(금)·4(수).
- 런타임 접촉 프로필은 기존4개에6개를 더해10개를 참조한다. 일반 피격 및 나머지 술식 공통 경로는 유지한다.

촬영은 한 클립씩 순차 실행하고 렌더 텍스처·읽기용 텍스처·임시 오브젝트를 해제했다. 유휴 임포트 작업자를 Unity API로 정리하고 기존 DesiredWorkerCount=3은 복원했다. 새 촬영 시작의 시스템 커밋85% 중단 기준을 유지했다.
'''
text+=f"\n기록된 클립 시작 커밋 최고: {scope['maxCommitRatio']*100:.2f}%. 런타임 MVID: {', '.join(scope['mvids'])}.\n"
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
entry='**2026-09-10 · 금·토·수 기본6종 KTP 적용:** 사·마·아는 화면(0.60,0.40)의 빠른 발동 문양과 확대 명중, 서·머·어는 중앙 문양 사각 반투명 방패와 금속/돌/물 접촉 파편으로 변경했다. C2 비교24영상, 선택10종78개·기존107개 검사 통과. 공급자 원본·다른114개 프로필 보존. 사용자 비주얼 판단 및 전체 적 AI 전투는 미검증. [비교](http://127.0.0.1:8771/BASIC_SIX_REVIEW.html) · [보고서](../Art/SpellVFX120/BasicSix/REPORT.md).\n\n'
p=ROOT/'Docs/PROJECT_STATUS.md';s=p.read_text(encoding='utf-8');p.write_text(entry+s,encoding='utf-8')
p=ROOT/'Docs/Specs/SPEC-SPELL-VFX120.md';s=p.read_text(encoding='utf-8');head,tail=s.split('\n',1)
entry='\n2026-09-10 기본6종 후속: 사용자 요청에 따라 사·마·아의 기존 본체·이동은 유지하고 화면(0.60,0.40)·깊이1.6m의 빠른 KTP 발동 및 확대 접촉을 적용한다. 서·머·어는 중앙 Bottom 문양의 사각 반투명 방패와 작은 접촉 문양+금속/돌/물 파편을 사용한다. 기존4종과 공급자 원본은 보존한다. [6종 검토 기록](../../Art/SpellVFX120/BasicSix/REPORT.md)을 따르며 미술 판단은 사용자 검토 대상이다.\n'
p.write_text(head+'\n'+entry+tail,encoding='utf-8')
print('Report and project checkpoint written.')
