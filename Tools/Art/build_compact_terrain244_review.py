"""Review of the stronger mountain relief, using this revision's Unity evidence."""
from pathlib import Path
from html import escape
import json

OUT = Path(__file__).resolve().parents[2] / 'Art/World/Compact/Rebuild'
E = OUT / 'Terrain244'
def image(path, caption):
    return f'<figure><a href="{path}"><img loading="lazy" src="{path}" alt="{escape(caption)}"></a><figcaption>{escape(caption)}</figcaption></figure>'

walk = json.loads((E/'walk.json').read_text())
assert walk['status'] == 'PASS_CONTROLLER_TRAVERSAL'
body = '''<h1>능선의 높낮이를 더 뚜렷하게</h1><p>이전 산세의 한국 DEM 형태를 유지하면서 봉우리와 골짜기의 고저차를 키웠습니다. 산의 높이가 한결같이 눌려 보이던 부분을 풀고, 가지 능선과 안부가 더 선명하게 보이도록 조정했습니다.</p><p>동굴·입구·주막과 현재 단서/통행 구간은 보호하고, 자연물을 새 지면에 맞췄습니다. 먹 표현·표면 재질·안개·조명은 동일합니다.</p><p><b>테스트</b> · Oheangbu → Compact Rebuild → Open latest test scene → W_Demo_Compact_MigrationCheck에서 Play.</p>'''
for i,name in enumerate(['주막 뒤 산세','폐광 밖 전망','겹산과 계곡','주막 동쪽 능선']):
    body += f'<h2>{name}</h2><div class="compare">'
    body += image(f'Terrain244/before_{i}.png','이전 · #243')
    body += image(f'Terrain244/after_{i}.png','수정 후 · #244')+'</div>'
body += '<h2>전체 기복</h2>'+image('Terrain244/relief_plan.png','같은 4×6km 범위. 왼쪽은 이전, 오른쪽은 수정 후 높이입니다.')
body += image('Terrain244/region_preview.png','수정된 지형의 최종 높이에서 다시 그린 지역 지도. 도로와 발견 정보는 게임에서 합성합니다.')
body += f'''<h2>확인 범위</h2><p>96개 전용 지형 타일의 표시/충돌과 이음새, 자연물 접지, 상호작용 지점을 검사했습니다. 새 NavMesh로 동굴 단서부터 주막까지 자동 CharacterController 보행 {walk['distance']:.2f}m / 시뮬레이션 {walk['simulatedSeconds']:.2f}초를 통과했습니다.</p><p>아래 Play 검사는 위치 배치와 상호작용 API를 사용하는 fixture입니다. 수동 길찾기·차량 통행·CPU/GPU 실측 및 사용자 미술 판정과 구분합니다. 기존 진행 데이터와 저장 슬롯은 유지했습니다.</p><p><a href="Terrain244/audit.txt">지형/접지</a> · <a href="Terrain244/navigation.txt">NavMesh</a> · <a href="Terrain244/walk.json">자동 보행</a> · <a href="Terrain244/runtime.txt">Play 연결</a> · <a href="Terrain244/rendering.txt">렌더링</a> · <a href="Terrain244/repeat.json">반복 제작</a> · <a href="Terrain244/delivery.json">전달 기록</a></p><p>원자료는 <a href="https://registry.opendata.aws/terrain-tiles/">Mapzen / USGS의 한국 표고 자료</a>입니다. 높이와 동선은 게임에 맞게 변형했으며, 약 30m 원자료를 5m 격자로 보간했습니다. <a href="Terrain243/ATTRIBUTION.md">원자료 출처</a>의 수직 계수 0.48은 이전 #243 값이고 이번은 0.86입니다. 특정 실제 산을 그대로 복제한 지도는 아닙니다.</p>'''
body += '<p>최종 Play의 시작/조사/지도 연결 14건과 렌더링 8건을 통과했습니다. 초기 순간이동식 주막 검사에서는 상호작용이 거절됐고, 연속 보행 검사에서는 문 접근과 휴식 저장이 성공했습니다. 연출 마지막은 게임뷰 포커스 분실로 정지해 이번 전체 휴식 검증은 미완료입니다. <a href="Terrain244/initial_runtime.txt">초기 실패</a> · <a href="Terrain244/rest_unfocused.txt">포커스 타임아웃</a> · <a href="Terrain244/rest_partial.txt">연속 보행/휴식의 확인 범위</a>.</p>'
style='body{max-width:1400px;margin:44px auto;padding:0 24px;background:#e7e1d4;color:#242723;font:17px/1.7 system-ui,sans-serif}h1{font-size:32px}h2{font-size:23px;margin-top:44px}img{width:100%;display:block}figure{margin:20px 0}figcaption{font-size:15px;margin-top:8px;color:#55584f}a{color:#405e52}p{max-width:1100px}.compare{display:grid;grid-template-columns:1fr 1fr;gap:18px}@media(max-width:800px){.compare{grid-template-columns:1fr}}'
(OUT/'MOUNTAIN_REVIEW.html').write_text(f'<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>산세 기복 · #244</title><style>{style}</style>{body}</html>',encoding='utf-8')
print('MOUNTAIN_REVIEW.html written')
