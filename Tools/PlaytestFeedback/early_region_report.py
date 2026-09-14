from pathlib import Path
import html
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Playtest/EarlyArt'
views = [
 ('mine_inside','폐광 · 작업과 폭파 흔적','지지틀과 파손 목재, 암편·줄·작업 물품을 추가했다. 기존 조사 문구와 위치를 유지하며 사건의 배후를 설명하지 않는다.'),
 ('mine_exit','폐광 출구 · 산 사면과 숲','높은 사면은 작은 소나무, 낮은 곳은 활엽수 군락. 통행면을 비우고 낮은 풀과 고사리를 길 바깥에 배치했다.'),
 ('forest_approach','숲길 · 능선의 식생','주변 산의 수목 카드는 근거리 원형과 같은 확정 배치를 사용한다. 원거리 카드를 위한 크기 보정과 별도 셰이더를 적용했다.'),
 ('inn_entry','금표 주막 · 생활 흔적','장작과 항아리, 출구의 운반 물자가 광산 작업과 주막 생활을 연결한다. 휴식·대화 서비스는 그대로 유지했다.'),
 ('inn_side','주막 측면 · 통행 공간','마루 외곽의 식생 제외 공간과 주막 진입 구도다. 땔감과 저장 용기는 건물 옆에 배치했다.'),
]
report='''# 초반 지역 환경 미술 1차 보강

2026-09-14 · TEST · W_WorldMacro_Playtest

## 반영

- 폐광: 보유 목재 원형의 지지틀 4세트, 부러진 목재와 암편, 그을린 줄, 작업 상자·항아리. 바위와 바구니의 기존 원형도 유지했다.
- 출구: 운반용 목재 묶음과 바구니. 선택 지선의 소지품 주위에 암편을 보강했다.
- 주막: 통행과 대화 위치를 피해 장작 더미와 항아리를 놓았다.
- 초반 사면·숲길: 느릅나무·소나무·고사리·풀의 보유 원형을 사용한 확정 배치 5,666개. 높은 곳은 더 작은 소나무, 골짜기는 활엽수. 큰 나무는 본선에서 28m 이상 떨어지게 배치하고 다른 가도/제외 영역도 확인했다. 보조 식생에는 새 충돌체를 추가하지 않았다.

기존 Narrative Bible 서막의 폭파 조사와 두 NPC의 대사를 환경으로 보강했다. 대사·서사 정답·새 퀘스트·보상을 추가하지 않았다. 캐릭터 NaturalLocomotion 작업은 이번 수정 대상에서 제외했다.

## 구현과 비용

17개 정적 렌더러, 정적 형상 56,360 tris. 추가 식생은 643개 공간 묶음이며, 전부 원거리 표현을 선택했을 때 11,332 tris다. 이것은 전체 씬 삼각형 수가 아니다. 가까운 식생의 메시 비용과 알파 중첩 비용은 별도로 발생한다.

배치와 행렬·메시·재질 패킷은 편집 시 확정한다. 카메라 회전 시 후보 생성이나 행렬 전체 재구성 없이 시야와 거리만 판정한다. GPU 인스턴싱을 사용하고 추가 식생의 그림자는 끈다. 고사리의 원거리 2,872 tris 원형은 낮은 지면 피복 카드로 대체했다. 나무 카드의 실제 표시 크기가 지나치게 작아지는 현상을 확인해, 카드 정점에 원형의 크기를 반영했다. 원형 공급자와 기존 식생 재질을 수정하지 않고 이번 보강용 재질과 카드 메시를 만들었다.

동일 1080p 정지 촬영에서 새 식생 제출은 폐광 출구 약 0.84ms / 235회, 숲길 약 0.96ms / 272회였다. 이는 CPU 제출 구간 한 번의 측정이며 GPU 시간, 전체 프레임 시간, 이동 중 중앙값·p95, 120fps 달성 근거가 아니다. 원문은 각 *_cost.txt에 있다.

## 검사

| 항목 | 상태 | 근거/한계 |
|---|---|---|
| 기존 콘텐츠 데이터 보존 | 통과 | content_expected.json과 현재 직렬화 비교 일치 |
| 셰이더·인스턴싱·패킷 상한 | 통과 | technical_checks.txt, 패킷당 1,023 이하 |
| 주막 체크포인트 서기 공간 | 통과 | 실제 씬 capsule 검사 |
| 조사·휴식·두 대화·소지품 접근 | 통과 | 5개 지점의 안전 위치·시야 검사 |
| 식생 접지 | 부분 확인 | 현재 Terrain 콜라이더에 정렬, 지면을 찾지 못한 배치 제외. 전체 사면을 육안 완주한 것은 아님 |
| 동일 구도 전후 이미지 | 통과 | 1920×1080, 4개 구도. 아래 로딩 제한 참고 |
| 전통성·자연스러운 군락·서사 전달 | 사용자 검토 | 1차 보강안 |
| 실제 이동 중 CPU/GPU·GC·LOD 전환 | 미검증 | 정지 캡처를 대체 근거로 사용하지 않음 |
| 전체 보행·신규 장식 충돌·AI 전투 | 미검증 | 장식 충돌체 추가 없음. 지지목에 접근하면 통과할 가능성 있음 |

## 촬영과 잔여 문제

기존 256m 식생 스트리밍은 준비 시간을 늘려도 대기가 상당히 남았다. 처음 40ms 준비만으로 찍던 도구는 로딩 도중 화면을 비교하게 되어 있었다. 이번 전후 자료는 매번 캐시를 초기화한 뒤 최대 8초를 준비하고 제출 패킷을 촬영에 한해 준비한다. **완전 로딩된 화면이 아니며**, 기존 식생의 수가 준비 시간에 영향을 받을 수 있다. *_loading.txt에 대기 수·상주 수를 기록했다. 게임의 기존 1ms 생성·0.75ms 패킷 제한은 변경하지 않았다. 새 보강 식생은 별도로 확정 저장되므로 이 생성 대기를 거치지 않는다.

폐광의 스캔 암반 반복과 바닥 이음, 길의 다소 곧은 외관, 장식 지지목의 충돌, 근거리↔카드 전환 시 외관 차이는 추가 정리가 필요하다. 기존 첫 방문 식생 생성 지연도 별도 성능 과제로 남겼다. 초반 지역의 최종 아트 완성이나 성능 합격으로 보고하지 않는다.

원본 씬은 BeforeEarlyArt.unity에 보존했다. Unity의 Playtest_EarlyArt 루트만 별도로 비활성화할 수 있다. 생성 원형은 asset_ledger.tsv, 최초 배치 후보는 foliage_placements.tsv, 최종 배치 좌표는 foliage_final.tsv에 기록했다. 실제 원장에는 별도 PlaceSource로 배치한 KoreanTraditionalFestival의 SM_Rope·SM_Basket도 VisualCorridor 데이터에 기록된다.

적용 재현: WorldMacroEarlyArt.Execute("apply") — 원본 씬에서만 실행. 기존 적용 씬에는 다시 실행하지 않는다. validate 및 capture:after:<view>는 별도 검토 명령이다. 시야·입력·게임 플레이를 직접 연결해 판정하는 테스트는 추가하지 않았다. 빌드·영상·자동 보행 완주는 만들지 않았다.
'''
report=report.replace('5,666','5,659').replace('11,332','11,318').replace('4개 구도','5개 구도')
report+='\n화면의 캡슐은 기존 적·NPC의 편집 상태 임시 외형이다. 승인된 플레이어 모델·모션을 캡슐로 되돌린 것이 아니다.\n'
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
blocks=[]
for key,title,desc in views:
    blocks.append(f'''<section id="{key}"><h2>{title}</h2><p>{desc}</p><div class="pair"><figure><figcaption>수정 전</figcaption><a href="before_{key}.png"><img loading="lazy" src="before_{key}.png"></a></figure><figure><figcaption>수정 후</figcaption><a href="after_{key}.png"><img loading="lazy" src="after_{key}.png"></a></figure></div></section>''')
checks=html.escape((OUT/'technical_checks.txt').read_text(encoding='utf-8-sig'))
page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>초반 지역 · 폐광에서 주막으로</title><style>
*{box-sizing:border-box}body{margin:0;background:#171d19;color:#ddd9ca;font:16px/1.7 system-ui,sans-serif}header,main,footer{max-width:1600px;margin:auto;padding:32px}header{padding-top:56px}h1{font-size:38px;letter-spacing:-1px}h2{color:#e7dfc8;font-size:25px}p{max-width:1050px}.tag{color:#aabfa0;letter-spacing:3px;font-size:12px}nav{display:flex;gap:20px;flex-wrap:wrap}a{color:#c6d5b0}section{margin:40px 0 72px;scroll-margin:20px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}figcaption{color:#adb6a7;font-size:13px;margin-bottom:8px}img{width:100%;aspect-ratio:16/9;object-fit:contain;background:#101411;border:1px solid #41463b}.note{padding:20px;border-left:3px solid #a99365;background:#272b23}.stats{display:flex;gap:40px;flex-wrap:wrap}.stats b{font-size:30px;font-weight:500;color:#d6dfc8}pre{font-size:13px;white-space:pre-wrap;background:#101612;padding:20px}@media(max-width:850px){.pair{grid-template-columns:1fr}header,main,footer{padding:20px}h1{font-size:28px}}
</style><header><div class="tag">오행부 / 환경 미술 / 1차 보강</div><h1>폐광에서 숲과 주막으로</h1><p>광산 작업과 폭파의 흔적, 숲길의 시야, 주막의 생활을 잇는 환경 보강안입니다. 캐릭터와 기존 조사·대화·보상은 유지했습니다.</p><nav>'''+''.join(f'<a href="#{k}">{t.split(" · ")[0]}</a>' for k,t,_ in views)+'''<a href="REPORT.md">검사 보고서</a></nav><div class="stats"><p><b>5,666</b><br>추가 식생 배치</p><p><b>11,332 tris</b><br>추가 식생 전체의 원거리 카드</p><p><b>17</b><br>정적 환경 렌더러</p></div><div class="note">Unity Edit 모드의 동일 구도 1080p 정지 비교입니다. 기존 스트리밍 식생은 각 촬영에서 8초 준비 후에도 대기가 남아 있습니다. 전체 보행·이동 중 FPS·최종 비주얼은 미검증이며, 새 빌드는 만들지 않았습니다.</div></header><main>'''+''.join(blocks)+f'''<section><h2>검사와 남은 작업</h2><pre>{checks}</pre><p>폐광 암면·바닥 이음의 세부 마감, 지지목의 충돌, 카드 전환 시 외관 차이와 실제 이동 중 성능을 추가로 확인해야 합니다. 기존 첫 방문 식생 생성 지연은 이번 보강과 별도 과제로 남깁니다.</p><a href="REPORT.md">보고서 전체</a> · <a href="validation.txt">수치</a> · <a href="foliage_final.tsv">최종 배치 좌표</a></section></main><footer>2026-09-14 · TEST · 사용자 비주얼 검토용</footer></html>'''
page=page.replace('5,666','5,659').replace('11,332','11,318')
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print(OUT/'REVIEW.html')
