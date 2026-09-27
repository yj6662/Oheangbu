"""Publish the local review from actual Village245 evidence; no Unity mutations."""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'
V = OUT / 'Village245'
ledger = json.loads((V / 'ledger.json').read_text(encoding='utf-8-sig'))
performance = json.loads((V / 'performance.json').read_text(encoding='utf-8-sig'))

def figure(name, caption):
    return f'<figure><a href="Village245/{name}"><img loading="lazy" src="Village245/{name}" alt="{html.escape(caption)}"></a><figcaption>{html.escape(caption)}</figcaption></figure>'

def point(x, z):
    return (40 + (x - 2580) * 1.25, 35 + (2540 - z) * 1.25)

svg = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 800 630"><rect width="800" height="630" fill="#e8e2d5"/>']
for key, color, width in [('main', '#71694f', 6), ('branch', '#9d946f', 3), ('forest', '#657761', 3)]:
    pts = ' '.join(f'{x:.1f},{y:.1f}' for x, y in (point(p['x'], p['z']) for p in ledger[key]))
    svg.append(f'<polyline points="{pts}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')
for b in ledger['buildings']:
    x, y = point(b['xz']['x'], b['xz']['y'])
    svg.append(f'<rect x="{x-7:.1f}" y="{y-5:.1f}" width="14" height="10" fill="#686350"/>')
for x, z, label in [(3110, 2250, '금표 주막'), (2840, 2390, '길목 역참'), (2700, 2180, '청림 벌목마을'), (2975, 2400, '공구함 지선'), (2955, 2515, '선택 조우·물증')]:
    px, py = point(x, z)
    svg.append(f'<circle cx="{px:.1f}" cy="{py:.1f}" r="5" fill="#443f30"/><text x="{px+12:.1f}" y="{py-12:.1f}" fill="#292c24" font-family="sans-serif" font-size="16">{label}</text>')
svg.append('<text x="40" y="600" font-family="sans-serif" font-size="15" fill="#55594a">배치 원장의 경로·건물 위치 / 갈색: 본선, 옅은 갈색: 물증 지선, 녹색: 숲 진입로</text></svg>')
(V / 'route.svg').write_text(''.join(svg), encoding='utf-8')

body = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>청림 벌목마을과 장비 · #245</title><style>
body{max-width:1400px;margin:44px auto;padding:0 24px;background:#e7e1d4;color:#242723;font:17px/1.7 system-ui,sans-serif}h1{font-size:32px}h2{font-size:23px;margin-top:44px}img{width:100%;display:block}figure{margin:22px 0}figcaption{font-size:15px;margin-top:8px;color:#55584f}a{color:#405e52}p{max-width:1120px}table{border-collapse:collapse;width:100%}td,th{padding:10px 14px;text-align:left;border-bottom:1px solid #bdb7a7}.compare{display:grid;grid-template-columns:1fr 1fr;gap:18px}@media(max-width:800px){.compare{grid-template-columns:1fr}}</style>
<h1>수레길 끝, 아직 사람이 사는 마을</h1><p>금표 주막에서 길목 역참을 거쳐 청림 벌목마을로 이어집니다. 마을의 일곱 집에는 객주·장비 상점·목공 작업장과 주민 집, 문을 닫은 빈집이 섞여 있습니다. 나무 테마는 판재와 장작, 수레와 작업 흔적으로 표현했습니다.</p>
<p><b>테스트 씬</b> · Unity에서 <b>Oheangbu → Compact Rebuild → Open latest test scene</b>을 선택한 뒤 <b>W_Demo_Compact_MigrationCheck</b>에서 Play합니다. 보존 원본 W_Demo_Compact에는 이번 마을·장비 기능을 켜지 않았습니다.</p>
<p>주막에서 수레길을 따라 역참의 정담을 지나 마을로 내려갑니다. 상인과 장인은 각 작업장 앞에서 F로 이용하고, 휴식은 객주 문에서 F를 누릅니다. I로 소지품을 열어 장비를 선택하거나 부위 칸으로 끌어 놓습니다. 장착 칸을 선택하면 해제할 수 있습니다.</p>
<h2>경로와 생활 공간</h2>'''
body += figure('route.svg', f'본선 {ledger["length"]:.1f}m. 물증 지선과 숲 쪽 두 번째 진입로는 선택 경로입니다.')
body += figure('village_0.png', '마을 진입부 · 실제 Unity 카메라')
body += figure('village_1.png', '중앙 마당 · 기능별 건물과 주민, 목재 작업 흔적')
body += '<div class="compare">' + figure('village_2.png', '마을 배치 조감') + figure('village_3.png', '역참 · 정담의 위치는 유지') + '</div>'
body += '<h2>소지품과 장비</h2><p>붓·머리·몸·손·발·장신구의 여섯 부위입니다. 기본 붓과 의복은 보너스 0으로 시작하며, 판매 장비 여섯 종은 +3까지 강화됩니다. 소유·장착·강화는 전체 진행 저장의 한 상태를 공유합니다. 장비를 바꾸어도 이미 시전한 술식의 피해는 바뀌지 않습니다.</p>'
body += figure('inventory.png', '실제 Play 장비창 · 왼쪽 부위 칸, 오른쪽 소지 장비, 선택한 장비만 상세 표시. 테스트용 자금입니다.')
body += '<div class="compare">' + figure('shop.png', '별도 상인의 장비 구매') + figure('forge.png', '장인의 소유 장비 강화') + '</div>'
body += '<h2>선택 의뢰</h2><p>계곡의 공구함은 장인에게 직접 건네고, 잘린 나무의 흔적은 주민에게 직접 보고합니다. 먼저 발견해도 인정되며 각 보상은 80통보로 한 번만 지급됩니다. 왕소를 먼저 만나도 인사할 수 있고, 정식 화물 계약에는 정담 접촉이 필요합니다. 의뢰 완료로 마을 서비스나 진입을 잠그지 않았습니다.</p>'
body += '<h2>확인 범위</h2><p>아래 자동 검사는 실제 Play 세션·전체 저장·전투 계산 및 UI 이벤트를 호출하는 fixture입니다. 자동 CharacterController 이동은 수동 길찾기와 구분합니다. 주막의 이전 포커스 문제는 이번 게임뷰 포커스가 확보된 실행에서 휴식 완료·입력 복귀·반복 휴식을 다시 확인했습니다.</p>'
body += '<table><tr><th>기록</th><th>확인 내용</th></tr>'
for file, label in [('rules.txt', '6부위 규칙·최대 강화·가격·v7 이행·기존 보강 합산·서사 조건'), ('runtime_final.txt', '최종 배치의 54검사: 거래·선행 발견·보고·UI 드롭·피해 스냅샷·저장 실패·휴식·사망·재실행'), ('health_fresh.txt', '새 게임의 동굴 시작·적·지도·의뢰 연결'), ('walk.txt', '본선·지선·숲 진입·동굴부터 마을까지 연속 물리 이동·문 접근'), ('navigation.txt', '최종 NavMesh·지면·통행 공간'), ('inn_rest_runtime.txt', '금표 주막 휴식 20개 자동 검사'), ('rendering.txt', '셰이더·식생 패스·누락 스크립트·후보 재질 격리'), ('bindings.txt', '카탈로그·배치·지도 연결과 기존 저장/씬 해시 보존'), ('consistency.txt', '문서/원장 정적 정합성'), ('performance.json', '현재 경로의 Editor CPU/GPU 실측'), ('delivery.json', '최종 연결·보존 해시와 한계')]:
    body += f'<tr><td><a href="Village245/{file}">{file}</a></td><td>{label}</td></tr>'
body += '</table>'
body += f'<p>{performance["device"]} / Unity Editor {performance["width"]}×{performance["height"]}, 600프레임 카메라 경로 검사: CPU 중앙값 {performance["cpuTimingMedianMs"]:.2f}ms, GPU 중앙값 {performance["gpuMedianMs"]:.2f}ms, 프레임 P95 {performance["frameP95Ms"]:.2f}ms. 적 전투를 제외한 Editor 측정이며 120fps 목표 달성 판정은 아닙니다.</p>'
body += '<p><b>남은 판정:</b> 마커 없이 길과 서비스를 찾는 수동 탐험, 조우를 포함한 실제 5~8분 체감, 이동 중 LOD·식생 안정성, 독립 실행 전투 부하 및 사용자 미술 판단은 미완료입니다. 의상 메시 교체와 상경 호송 확장은 이번 범위에 포함하지 않았습니다. 변형 문제가 남은 생성 리그는 장면 교체에 사용하지 않았습니다.</p></html>'
(OUT / 'VILLAGE_EQUIPMENT_REVIEW.html').write_text(body, encoding='utf-8')
print('Published VILLAGE_EQUIPMENT_REVIEW.html')
