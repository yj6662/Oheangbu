from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'Tools/SpellVFX120/publish_area_five.py').read_text(encoding='utf-8').replace('AreaFive','AreaFlow').replace('AREA_FIVE_REVIEW.html','AREA_FLOW_REVIEW.html').replace('area_five_review.html','area_flow_review.html')
source=source.replace("for glyph in '고노소모오':","for glyph in '고노소오':").replace("'013_ACE0','037_B178','061_BAA8','085_C18C','109_C624'","'013_ACE0','037_B178','085_C18C','109_C624'").replace('clips=20,stills=40','clips=16,stills=32').replace('profilesOutsideFiveUnchanged=True','profilesOutsideFourUnchanged=True,spellBookUnchanged=True')
page=(ROOT/'Tools/SpellVFX120/area_wide_review.html').read_text(encoding='utf-8').replace('AreaWide/','AreaFlow/').replace('고·노·소·모·오 — 광역 강화','고·노·소·오 — 지속·발사 방향·물결 수정')
page=page.replace('문양 가시성과 지속 시간, 넓어진 송곳 산개·모래 회오리·종 모양 물결을 비교합니다.','가시 유지 시간, 우측 하단 화염방사, 상단에서 내려꽂는 송곳, 낮은 양 끝과 포말을 비교합니다.')
page=page.replace("['고','노','소','모','오']","['고','노','소','오']")
start=page.index('<details open><summary>변경값과 검증 범위</summary>');end=page.index('<p>1080p',start)
page=page[:start]+'''<details open><summary>변경값과 검증 범위</summary><table><tr><th>술식</th><th>이번 수정</th></tr><tr><td>고</td><td>가시 전체 수명4.6초·문양4.1초, 추가 피해 없음</td></tr><tr><td>노</td><td>화면(0.65,0.32) 발사구에서 약1.8초간 화염 방사, 근접 입자는 작게</td></tr><tr><td>소</td><td>화면 위쪽(0.5,1.2)·깊이3.2m에서 확정된24개 산개 종점으로 하향 발사</td></tr><tr><td>오</td><td>기존0.75~1.15m 물결 복원·폭12m 유지·양 끝을 완만하게 낮추고 경계 포말 추가</td></tr></table>
<p>‘노’의 연장은 시각적 지속 시간입니다. 반복 피해를 새로 추가하지 않았습니다. ‘소’도 기존24발의 종점·명중 예약을 유지합니다.</p>
'''+page[end:]
page=page.replace('기존판은 저장한 이전 프로필·SpellBook, 수정판은 새 설정을 사용합니다.','기존판은 직전 AreaWide 설정, 수정판은 이번 설정입니다. SpellBook·피해·범위·속도는 이번에 변경하지 않았습니다.')
page=page.replace('href="AREA_FIVE_REVIEW.html"','href="AREA_WIDE_REVIEW.html"')
page=page.replace('고 — 지면 문양의 불투명도·표시 시간 강화','고 — 가시4.6초·문양4.1초 / 피해 횟수 유지').replace('노 — 발사구 문양 강화 / 화염 방사 약0.3→0.77초','노 — 우측 하단의 작은 발사구 / 약1.8초간 전진하는 화염').replace('소 — 9→24발 / 좌우45°에 산발적으로 퍼지는 송곳 / 경로 밖 명중 없음','소 — 화면 위에서 대각선 아래로 / 기존24발 산개 유지').replace('오 — 폭3→12m / 중앙약2.1m에서 양옆으로 낮아지는 종 모양 물결 / 기본 위력9→5','오 — 기존 낮은 물결 복원 / 폭12m / 완만한 양 끝과 접촉선 포말')
page=page.replace('<script>','''<section><h2>모 — 회오리를 대체할 방향</h2><p>이번에는 제안만 정리했으며 기존 모 모델·프로필은 변경하지 않았습니다.</p><table><tr><th>후보</th><th>표현</th><th>읽히는 특징</th></tr><tr><td>사토쇄도 — 추천</td><td>모래·자갈이 낮고 넓게 지면을 쓸며 전진. 앞의 작은 돌이 튀고 뒤의 먼지는 빠르게 옅어짐.</td><td>시야를 덜 가리면서 다수 적을 넓게 공격하는 성격</td></tr><tr><td>지맥 분출</td><td>부채꼴로 뻗는 균열을 따라 짧은 돌가루 분출이 순차 전진.</td><td>토 문양에서 땅으로 힘이 퍼지는 표현</td></tr><tr><td>흙너울</td><td>낮은 흙 능선 여러 개가 부서지며 밀려오고 모래가 양옆으로 흘러내림.</td><td>질량감이 강한 흙의 이동, 수 속성 파도와는 거친 입자·조각으로 구분</td></tr></table></section><script>''')
(ROOT/'Tools/SpellVFX120/area_flow_review.html').write_text(page,encoding='utf-8')
exec(compile(source,'publish_area_flow_generated','exec'))
