import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/AreaWide'
scope=json.loads((OUT/'scope.json').read_text(encoding='utf-8'))
audits=[json.loads((OUT/n).read_text(encoding='utf-8')) for n in ['audit.json','contact_audit.json','ktp_contact_play_audit.json']]
if any(a['status']!='PASS' for a in audits):raise RuntimeError('Audit not passed')
rows=[json.loads((OUT/g/'report.json').read_text(encoding='utf-8')) for g in '고노소모오']
table='\n'.join(f"| {r['glyph']} | {r['clips'][0]['damageEvents']} | {r['clips'][1]['damageEvents']} | {r['clips'][1]['uniqueContacts']} | {r['clips'][1]['outsideHits']} |" for r in rows)
text=f'''# 광역 술식 강화판 — 2026-09-10

사용자 피드백 적용. 기존 AreaFive의 프로필·영상·검토 페이지는 보존했다. 미술 판단은 사용자 검토 대기다.

## 변경값

| 술식 | 기존 | 수정 |
|---|---|---|
| 고 | 바닥 문양0.32초·최대알파0.35 | 1.25초·최대알파0.85, 지면 오프셋0.025→0.08m. 가시·피해 예약 유지 |
| 노 | 발사구 문양0.32초, 방사약0.3초 | 문양1.25초·알파0.85·배율0.264→0.32, 방사약0.77초. 피해는 기존 시점1회 |
| 소 | 9발·간격0.1초·반각25도·대상 순환 배분 | 24발·간격0.045초·반각45도. 구간별 방향을 섞어 넓게 산개. 기본 총위력 상한8 유지, 발당8/24 |
| 모 | 폭2.4m·위력9·낮은 모래 전선 | 실제 폭12m·위력5, 높이약3.8m의 세 회오리 기둥과 모래·돌. 속도7m/s 유지 |
| 오 | 폭3m·위력9·비슷한 마루 높이 | 실제 폭12m·위력5, 중앙약2.1m에서 양옆으로 낮아지는 종 모양. 속도6m/s 유지 |

소는 시전 시24개의 분산 방향·종점·발사/착탄 시간을 확정한다. 수평 경로와 적의 중심이0.65m 안으로 가까워지는 첫 적에게만 피해를 예약한다. 빈 경로는 사거리 끝까지 날고 명중 효과를 만들지 않는다. 대상 하나에 모든 발을 유도하지 않으므로 단일 적 대상의 실제 합산 피해는 총위력 상한8보다 작을 수 있다. 이동한 적을 다시 탐색하지 않으며 기존 예약 피해 방식을 유지한다.

모는 입자 위치를 세 개의 상승 나선으로 제어한다. 연기/모래240개, 돌100개가 상한이며 파츠 크기를 과도하게 늘리는 방식 대신 위치·회전으로 회오리를 만든다. 오는 가로축에 정규분포 형태의 곡선을 적용했다. 중앙높이는1.95~2.25m, 양쪽 가장자리는 지면에 고정한다. 정규분포의 모양을 실루엣에 사용하는 것이며 확률 밀도값으로 피해를 배분하지 않는다. 물 셰이더·수면·포말은 같은 술식 경과 시간을 사용한다.

## 동일 표적 전후 비교

좌우3열·전후3열 표적9개와 범위 밖 표적1개를 같은 위치에 배치했다. 기존판은 저장한 V1 프로필·SpellBook, 수정판은 새 설정을 사용한다.

| 술식 | 기존 피해 횟수 | 수정 피해 횟수 | 수정 고유 접촉 | 범위 밖 피해 |
|---|---:|---:|---:|---:|
{table}

1080p·24fps·6초 영상20개, 정지 이미지40개. 모든 MP4 디코딩 통과. 최대 촬영 시작 커밋 사용률 {scope['maxCommitRatio']:.1%},85% 중단 장치 유지. 클립별 촬영 후 자원을 해제했다.

## 기술 검사

- **통과:** 광역 {len(audits[0]['checks'])}개, 기존 기본10종 접촉 {len(audits[1]['checks'])}개, 일반 피격·받아치기 {len(audits[2]['checks'])}개, 합계 {sum(len(a['checks']) for a in audits)}개.
- **통과:**24개 확정 종점·넓은 산개·빈 경로 제외·총위력 상한, 모/오 폭12m·위력5의 실제 SpellBook 연결, 나머지12개 SpellBook 항목 불변.
- **통과:**30/60/120fps 및0.2배 감속 상당의 효과 시간 샘플링, 화염 연장 재생, 모래 높이, 파도 중앙>중간>가장자리·좌우 대칭, 지면 경계, 효과 종료/중복 검사.
- **통과:** 실제 광역 Resolve·예약 피해·공통 접촉 경로의 캡처. 다른115프로필과 공급자 원본 해시 유지. 기존10종 보존.
- **미검증:** 손글씨 입력부터 적 AI까지 전투 전체, 각 프레임레이트의 실제 성능, 다수 동시 시전과 모든 지형/투명 정렬 조합.
- **사용자 검토 대기:** 문양 가시성, 회오리 느낌, 정규분포 형태의 해석, 불과 물의 최종 외관. 영상은 시험 표적 전투이며 적 AI 영상으로 표시하지 않는다.

## 전달물

- AREA_WIDE_REVIEW.html: 전후·플레이/외부 비교와 정지 이미지.
- AreaWide/글자/: MP4, 프레임 JPG, report.json, settings.json.
- AreaWide/audit.json, contact_audit.json, ktp_contact_play_audit.json, scope.json: 검사 결과.
- Unity Assets/_Project/Art/SpellVFX120/AreaWide: 이전 프로필/SpellBook, 강화 문양, 모래 회오리 프리팹.
'''
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
print('Wide report written',sum(len(a['checks']) for a in audits),'checks')
