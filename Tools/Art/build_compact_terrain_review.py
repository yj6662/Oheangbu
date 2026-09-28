"""Assemble revision 243's review from saved Unity evidence; no scene mutation."""
from pathlib import Path
from html import escape
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'
EVIDENCE = OUT / 'Terrain243'

def figure(path, caption):
    return f'<figure><a href="{path}"><img loading="lazy" src="{path}" alt="{escape(caption)}"></a><figcaption>{escape(caption)}</figcaption></figure>'

def passes(name):
    return sum(line.startswith('PASS ') for line in (EVIDENCE / name).read_text(encoding='utf-8-sig').splitlines())

walk = json.loads((EVIDENCE / 'walk.json').read_text())
assert walk['status'] == 'PASS_CONTROLLER_TRAVERSAL'
body = '''<h1>한국 산지의 능선과 골짜기</h1>
<p>둥근 둔덕이 반복되던 지형을 실제 한국 표고 자료의 가지 능선과 계곡 형태로 바꿨습니다. 먹 표현·표면 질감·안개는 유지하고, 산세에 맞춰 기존 나무와 바위를 다시 접지했습니다.</p>
<p><b>테스트</b> · Oheangbu → Compact Rebuild → Open latest test scene → W_Demo_Compact_MigrationCheck에서 Play.</p>
<p>약 4×6km 지형에 적용했습니다. 동굴 내부와 입구, 주막 마당, 현재 단서와 상호작용 위치는 유지합니다. 새 지형은 한국의 특정 산을 그대로 복제한 것이 아니라 게임 동선에 맞춘 변형입니다.</p>'''
names = ['주막에서 북쪽 산세', '폐광 밖 전망', '높은 시점에서 겹산', '주막 동쪽 가지 능선']
for i, name in enumerate(names):
    body += f'<h2>{name}</h2><p>같은 카메라·조명·후처리, 1920×1080.</p><div class="compare">'
    body += figure(f'Terrain243/before_{i}.png', '수정 전')
    body += figure(f'Terrain243/after_{i}.png', '수정 후') + '</div>'
body += '<h2>전체 산세와 지도</h2>' + figure('Terrain243/relief_plan.png', '왼쪽: 이전 높이 / 오른쪽: 한국 DEM에서 변형한 새 높이. 수계와 제작된 통행 구간 주변은 부드럽게 연결했습니다.')
body += figure('Cartography/region_preview.png', '최종 장면의 높이를 다시 읽어 그린 청림 지역 지도. 도로·발견·아이콘은 게임에서 별도로 합성합니다.')
if (EVIDENCE / 'player_inn.png').exists():
    body += '<h2>실제 Play</h2>' + figure('Terrain243/player_inn.png', '후보 씬의 실제 플레이어 카메라. 검토 위치까지 테스트용 이동을 사용했습니다.')
body += f'''<h2>이번 변경에서 확인한 것</h2>
<p>지형/접지 검사 {passes('audit.txt')}건, NavMesh 검사 {passes('navigation.txt')}건 통과. 96개 지형 타일의 표시 메시와 충돌 메시가 같고 타일 경계 높이 차이는 0입니다. 기존 배치 중 1,549개의 높이를 조정했으며, 검사한 자연물의 접지 누락과 대응 충돌체 위치 불일치는 없었습니다.</p>
<p>동굴 단서를 거쳐 주막까지 자동 CharacterController 보행 {walk['distance']:.2f}m / 시뮬레이션 {walk['simulatedSeconds']:.2f}초를 통과했습니다. 수동 키 입력·무표식 길찾기·차량 운행 결과와는 구분합니다.</p>
<p>새 지형에서 실제 Play 연결 14건, 주막 문 휴식 20건, 동일 진단 저장 재실행 1건, 렌더링 연결 8건을 다시 통과했습니다. 주막 앞 자동 접근은 21.149m였습니다. 원본 제작 도구의 공용 출력과 별도로 이번 근거를 Terrain243에 보존했습니다.</p>
<p>지상 지도 3종을 최종 높이로 갱신했습니다. 원본 W_Demo_Compact와 후보 진행 데이터/저장 슬롯을 보존했습니다. 반복 제작 결과 및 Play 연결 검사는 아래 기록을 따릅니다. 이동 LOD, CPU/GPU 실측, 전체 지역 통행, NPC 리그와 첫 벌목 마을 이주, 사용자 미술 판정은 아직 남아 있습니다.</p>
<p><a href="Terrain243/audit.txt">지형/접지</a> · <a href="Terrain243/navigation.txt">NavMesh</a> · <a href="Terrain243/walk.json">자동 보행</a> · <a href="Terrain243/runtime.txt">Play 연결</a> · <a href="Terrain243/rest.txt">주막 휴식</a> · <a href="Terrain243/reload.txt">재실행</a> · <a href="Terrain243/rendering.txt">렌더링</a> · <a href="Terrain243/repeat.json">반복 제작</a> · <a href="Terrain243/preservation.json">보존 확인</a> · <a href="Terrain243/delivery.json">전달 기록</a></p>
<h2>표고 자료</h2><p><a href="https://registry.opendata.aws/terrain-tiles/">Mapzen Terrain Tiles / USGS SRTM</a>의 한국 N35E127 타일에서 남서쪽 35.08°N, 127.34°E를 기준으로 약 4×6km를 사용했습니다. 원자료 간격은 약 30m이며 5m 보간이 새로운 실측 정밀도를 뜻하지 않습니다. 수직 비율 0.48과 골짜기 높이 보정, 제작된 통행면 보호를 적용했습니다. <a href="Terrain243/ATTRIBUTION.md">출처와 변형 기록</a>.</p>'''
style = '''body{max-width:1400px;margin:44px auto;padding:0 24px;background:#e7e1d4;color:#242723;font:17px/1.7 system-ui,sans-serif}h1{font-size:32px}h2{font-size:23px;margin-top:44px}img{width:100%;display:block}figure{margin:20px 0}figcaption{font-size:15px;margin-top:8px;color:#55584f}a{color:#405e52}p{max-width:1100px}.compare{display:grid;grid-template-columns:1fr 1fr;gap:18px}@media(max-width:800px){.compare{grid-template-columns:1fr}}'''
(OUT / 'TERRAIN_REVIEW.html').write_text(f'<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>산세 조정 · #243</title><style>{style}</style>{body}</html>', encoding='utf-8')
print('TERRAIN_REVIEW.html assembled from revision 243 evidence')
