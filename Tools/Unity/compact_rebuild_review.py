"""Render the Unity-authored Compact layout; does not edit Unity assets."""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'

def render():
    data = json.loads((OUT / 'layout.json').read_text(encoding='utf-8-sig'))
    places = {p['Id']: p for p in data['Places']}
    def xy(p):
        return (50 + p['x'] / 4000 * 680, 1040 - p['y'] / 6000 * 980)
    def point(p):
        x, y = xy(p)
        return f'{x:.1f},{y:.1f}'
    svg = ['<svg viewBox="0 0 820 1100" role="img" aria-label="축소맵 새 연결 구조">',
           '<rect x="50" y="60" width="680" height="980" fill="#e5e3d8"/>']
    for ridge in data['Ridges']:
        path = ' '.join(point(p) for p in ridge['Spine'])
        svg.append(f'<polyline points="{path}" fill="none" stroke="#b8bab0" stroke-width="45" stroke-linecap="round"/>')
    river = ' '.join(point(p) for p in data['River'])
    svg.append(f'<polyline points="{river}" fill="none" stroke="#7c999d" stroke-width="5"/>')
    colors = ['#303a32', '#8b7856', '#777775', '#954b39']
    for route in data['Routes']:
        path = ' '.join(point(p) for p in [places[route['From']]['XZ'], *route['Bends'], places[route['To']]['XZ']])
        color = colors[route['Role']]
        dash = '' if route['Role'] == 0 else 'stroke-dasharray="5 5"'
        svg.append(f'<polyline points="{path}" fill="none" stroke="{color}" stroke-width="2" {dash}><title>{html.escape(route["Id"])}</title></polyline>')
    for p in places.values():
        x, y = xy(p['XZ'])
        svg.append(f'<circle cx="{x}" cy="{y}" r="4" fill="#222d25"/><text x="{x+7}" y="{y-7}" font-size="12">{html.escape(p["Label"])}</text>')
    svg.append('<text x="50" y="34" font-size="19">북 · 현강</text><text x="50" y="1080" font-size="14">4 × 6 km · 개념 연결도 / 길 모양과 높이·통행은 미검증</text></svg>')
    rows = ''.join(f'<tr><td>{html.escape(p["Label"])}</td><td>{html.escape(p["Purpose"])}</td></tr>' for p in places.values())
    page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><title>축소맵 재설계 — 연결 구조</title>
<style>body{margin:32px;background:#f4f2eb;color:#242d28;font:16px/1.65 system-ui,sans-serif}main{display:grid;grid-template-columns:minmax(600px,1.3fr) minmax(360px,1fr);gap:40px;max-width:1500px;margin:auto}svg{width:100%;font-family:system-ui}table{border-collapse:collapse;width:100%;font-size:14px}td{padding:9px 5px;border-bottom:1px solid #ccc}td:first-child{white-space:nowrap}h1{font-size:28px}h2{font-size:20px}p{max-width:70ch}@media(max-width:1000px){main{display:block}}</style>
<h1>축소맵 정본 복귀 · 새 연결 구조</h1><p>TEST — 전체 연결 계획. 폐광–주막 후보의 실제 기능과 화면은 <a href="RUNTIME_REVIEW.html">실행 검토</a>에서 확인한다. 정본 지형 교체·전체 통행·미술 승인은 아직 아니다.</p><main><section>'''+''.join(svg)+'''<p>실선: 본선 · 황토 점선: 탐험 · 회색 점선: 복귀 · 붉은 점선: 국 관문. 능선 위 흔적→주막은 하강 복귀로 설계하며 역방향 능력 우회는 허용하지 않는다.</p></section><section>
<h2>발견과 보고를 분리한다</h2><p>폐광 조사는 수주한 상태에서 시작한다. 벌목장 물증·청룡 처치는 NPC 만남과 독립적으로 가능하다. 정담을 만난 사실이 계약을 열고, 계약과 청룡 해금이 함께 호송을 연다.</p>
<p>정담 접촉 → 왕소 계약<br>계약 + 청룡 처치 → 실제 동행 → 검문 1 → 검문 2 → 인도 → 장수 처치 → 개문</p>
<h2>시야를 이어 주는 지점</h2><p>폐광의 어두운 입구 → 계곡 너머 주막 처마 → 갈림에서 보이는 벌목 흔적과 능선 → 숲 사이 신목 → 성역. 상경 고개에서는 황경 지붕과 하천이 다음 방향을 알려 준다. 실제 가시성은 새 높이 데이터 생성 후 확인한다.</p>
<h2>거점의 역할</h2><table>'''+rows+'''</table></section></main></html>'''
    (OUT / 'LAYOUT_REVIEW.html').write_text(page, encoding='utf-8')
    print(OUT / 'LAYOUT_REVIEW.html')

if __name__ == '__main__':
    render()
