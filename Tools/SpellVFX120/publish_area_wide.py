from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
source=(ROOT/'Tools/SpellVFX120/publish_area_five.py').read_text(encoding='utf-8')
source=source.replace("AreaFive","AreaWide").replace('AREA_FIVE_REVIEW.html','AREA_WIDE_REVIEW.html').replace('area_five_review.html','area_wide_review.html')
page=(ROOT/'Tools/SpellVFX120/area_five_review.html').read_text(encoding='utf-8').replace('AreaFive/','AreaWide/')
page=page.replace('고·노·소·모·오 — 광역 술식','고·노·소·모·오 — 광역 강화')
page=page.replace('발생원의 전통 문양과 새로운 본체, 작은 명중 문양과 속성 파편을 비교합니다.','문양 가시성과 지속 시간, 넓어진 송곳 산개·모래 회오리·종 모양 물결을 비교합니다.')
page=page.replace('17개 대나무 가시의 산발적 돌출','선명하고 오래 보이는 지면 문양과 가시').replace('근접 문양에서 짧은 화염 방사','선명한 발사구 문양과 길어진 화염 방사').replace('금속 송곳 9발 속사','금속 송곳 24발·좌우45° 산개').replace('기존 발수·간격·대상 배분 유지','24개 경로에 걸린 대상에게만 피해').replace('낮고 넓은 모래 전선','폭12m·높은 모래 회오리').replace('PolyOne 물 셰이더와 굽이치는 마루·포말','폭12m·중앙이 높은 종 모양 파도').replace('기존 거리별 전선 도달','폭12m·기본 위력5·기존 전진 속도')
page=page.replace('‘고’의 기존 표현도 새 순차 피해 계획 위에서 재생했습니다. 이전 동시 피해 규칙까지 재현한 과거 게임 빌드 영상과는 구분됩니다.','기존판은 저장한 이전 프로필·SpellBook, 수정판은 새 설정을 사용합니다. 좌우로 퍼진 표적9개와 범위 밖 표적1개를 같은 위치에 배치했습니다.')
page=page.replace('고 — 지면 문양 → 불규칙한 순서로 솟는 대나무 → 섬유 소멸','고 — 지면 문양의 불투명도·표시 시간 강화').replace('노 — 근접 화 문양 → 짧은 부채꼴 화염 → 작은 문양과 재','노 — 발사구 문양 강화 / 화염 방사 약0.3→0.77초').replace('소 — 얇게 유지되는 금 문양 → 은빛 송곳 속사 → 금속 파편','소 — 9→24발 / 좌우45°에 산발적으로 퍼지는 송곳 / 경로 밖 명중 없음').replace('모 — 토의 지면 문양 → 모래·돌 전선 → 빠르게 옅어지는 먼지','모 — 폭2.4→12m / 높이약3.8m의 모래 회오리 / 기본 위력9→5').replace('오 — 수의 지면 문양 → 굽이치는 물결과 포말 → 물보라','오 — 폭3→12m / 중앙약2.1m에서 양옆으로 낮아지는 종 모양 물결 / 기본 위력9→5')
page=page.replace('href="BASIC_SIX_REVIEW.html">이전 기본 술식 검토','href="AREA_FIVE_REVIEW.html">이전 광역 술식 검토')
(ROOT/'Tools/SpellVFX120/area_wide_review.html').write_text(page,encoding='utf-8')
exec(compile(source,'publish_area_wide_generated','exec'))
