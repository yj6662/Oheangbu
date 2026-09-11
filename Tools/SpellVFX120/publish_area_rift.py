from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'Tools/SpellVFX120/publish_area_five.py').read_text(encoding='utf-8').replace('AreaFive','AreaRift').replace('AREA_FIVE_REVIEW.html','AREA_RIFT_REVIEW.html').replace('area_five_review.html','area_rift_review.html')
source=source.replace("for glyph in '고노소모오':","for glyph in '고노소모':").replace("'013_ACE0','037_B178','061_BAA8','085_C18C','109_C624'","'013_ACE0','037_B178','061_BAA8','085_C18C'").replace('clips=20,stills=40','clips=16,stills=32').replace('profilesOutsideFiveUnchanged=True','profilesOutsideFourUnchanged=True,spellBookUnchanged=True')
page=(ROOT/'Tools/SpellVFX120/area_flow_review.html').read_text(encoding='utf-8').replace('AreaFlow/','AreaRift/').replace('고·노·소·오 — 지속·발사 방향·물결 수정','고·노·소·모 — 가시성·지맥 분출')
page=page.replace('가시 유지 시간, 우측 하단 화염방사, 상단에서 내려꽂는 송곳, 낮은 양 끝과 포말을 비교합니다.','3초 가시, 낮아진 화염 발사구, 밝은 은빛 송곳, 지면을 따라 전진하는 균열과 돌가루를 비교합니다.')
page=page.replace("['고','노','소','오']","['고','노','소','모']")
start=page.index('<details open><summary>변경값과 검증 범위</summary>');end=page.index('<p>1080p',start)
page=page[:start]+'''<details open><summary>변경값과 검증 범위</summary><table><tr><th>술식</th><th>이번 수정</th></tr><tr><td>고</td><td>효과 수명3초·문양2.65초</td></tr><tr><td>노</td><td>발사구 화면 높이32→12%, 불꽃 중심을 모으고 밝기 강화·연기 감소</td></tr><tr><td>소</td><td>송곳 두께2.8배·길이1.15배, 은빛 발광과 두꺼워진 잔상</td></tr><tr><td>모</td><td>일곱 갈래 지면 균열을 따라 낮은 돌가루·자갈이 순차 분출</td></tr></table><p>피해·범위·발수·속도는 유지했습니다. 모의 균열은 현재12m 폭의 경로 안에서 부채꼴로 퍼지며 진행합니다.</p>
'''+page[end:]
page=page.replace('직전 AreaWide 설정','직전 AreaFlow 설정').replace('href="AREA_WIDE_REVIEW.html"','href="AREA_FLOW_REVIEW.html"')
page=page.replace('고 — 가시4.6초·문양4.1초 / 피해 횟수 유지','고 — 수명3초·문양2.65초').replace('노 — 우측 하단의 작은 발사구 / 약1.8초간 전진하는 화염','노 — 발사구를 더 아래로 / 밝고 모인 화염 줄기 / 연기 감소').replace('소 — 화면 위에서 대각선 아래로 / 기존24발 산개 유지','소 — 굵고 밝은 은빛 송곳과 잔상 / 기존24발 하향 산개')
start=page.index('<section><h2>모 —');end=page.index('<script>',start);page=page[:start]+page[end:]
page=page.replace("'모':'모 — 폭2.4→12m / 높이약3.8m의 모래 회오리 / 기본 위력9→5'","'모':'모 — 일곱 갈래 균열·낮은 돌가루 분출 / 실제 폭12m·위력5 유지'")
(ROOT/'Tools/SpellVFX120/area_rift_review.html').write_text(page,encoding='utf-8')
exec(compile(source,'publish_area_rift_generated','exec'))
