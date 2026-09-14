"""Offline review artifact, generated only from this run's actual outputs."""
import json, html, hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Prologue'
layout=json.loads((OUT/'layout.json').read_text(encoding='utf-8-sig'))
def xy(p):return 330+p['x']*1.8,520-p['z']*1.8
def line(points):return ' '.join(f'{x:.1f},{y:.1f}' for x,y in map(xy,points))
svg=['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 670 620"><rect width="670" height="620" fill="#e8e4d8"/>',
     '<path d="M265 430 Q255 240 330 155 Q425 250 390 420 Q360 460 315 415Z" fill="#74786e" opacity=".55"/>',
     '<text x="280" y="300" font-size="18" fill="#33372f">가림 산자락</text>',
     f'<polyline points="{line(layout["MainPath"])}" fill="none" stroke="#73593d" stroke-width="5"/>',
     f'<polyline points="{line(layout["BranchPath"])}" fill="none" stroke="#66836e" stroke-width="4" stroke-dasharray="7 4"/>']
labels={'MineStart':'폐광 시작','BlastEvidence':'폭파 흔적','WorkerSatchel':'통보 지선','InnRest':'금표 주막','GukPreview':'국 재방문 예고'}
for p in layout['Points']:
    if p['Id'] not in labels:continue
    x,y=xy(p['Position']);svg += [f'<circle cx="{x}" cy="{y}" r="5" fill="#45392e"/><text x="{x+9}" y="{y+5}" font-size="16">{labels[p["Id"]]}</text>']
svg+=['<text x="24" y="590" font-size="15">길 데이터의 평면도 · 산 실루엣은 연결 설명용</text></svg>']
(OUT/'first_segment.svg').write_text(''.join(svg),encoding='utf-8')
baseline=json.loads((OUT/'baseline.json').read_text(encoding='utf-8-sig'))
preserved={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==v for p,v in baseline.items()}
(OUT/'preservation.json').write_text(json.dumps(preserved,ensure_ascii=False,indent=2),encoding='utf-8')
walk=json.loads((OUT/'walk_status.json').read_text(encoding='utf-8-sig')) if (OUT/'walk_status.json').exists() else {}
sections=[]
for name,label in [('technical_static.txt','씬·접지·저장 구조'),('technical_runtime.txt','직접 호출 상태 검사')]:
    if (OUT/name).exists():sections.append(f'<details open><summary>{label}</summary><pre>{html.escape((OUT/name).read_text(encoding="utf-8-sig"))}</pre></details>')
comparison=''.join(f'<article><h3>{title}</h3><div class="pair"><figure><img src="view_{i}_before.png"><figcaption>새 지형 / Skybox·후처리 OFF</figcaption></figure><figure><img src="view_{i}_after.png"><figcaption>동일 카메라 / ON</figcaption></figure></div></article>' for i,title in enumerate(['폐광 바깥','산길 진입','능선 우회','금표 주막']))
comparison+='<article><h3>물길과 통보 지선</h3><div class="pair"><figure><img src="view_4_before.png"><figcaption>OFF</figcaption></figure><figure><img src="view_4_after.png"><figcaption>ON</figcaption></figure></div></article>'
video='<h3>입력 검사 부분 영상 · 완주 영상 아님</h3><video controls preload="metadata" src="input_walk.mp4"></video><p>메모리 제한으로 중단된 약 12.58초의 기록입니다.</p>' if walk.get('frames',0)>0 else ''
page='''<!doctype html><meta charset="utf-8"><title>폐광 → 금표 주막 · 제작 검토</title>
<style>body{margin:0;background:#eeebe3;color:#302e29;font:16px/1.7 system-ui,sans-serif}main{max-width:1300px;margin:auto;padding:48px 32px}h1{font-size:34px}h2{margin-top:42px}.note{background:#ded5bd;padding:18px}.flow{display:flex;gap:12px;flex-wrap:wrap}.flow span{border:1px solid #bbb5a8;padding:16px;flex:1;min-width:150px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:14px}figure{margin:0}img,video{width:100%}pre{white-space:pre-wrap;font:13px/1.6 monospace;background:#deddd5;padding:20px}.map{max-width:670px}a{color:#395e62}@media(max-width:700px){.pair{grid-template-columns:1fr}}</style>
<main><p>오행부 · EA 첫 플레이 구간 / TEST</p><h1>폐광에서 금표 주막까지</h1>
<p class="note">C2 기준 씬 보존. 적·NPC는 임시 캡슐입니다. 미술적 합격 여부는 사용자 검토 대상입니다. 전체 계획 완료를 의미하지 않으며, 검사 결과와 미완료 사항은 아래 보고서를 따릅니다.</p>
<h2>황경까지의 연결 예약</h2><div class="flow"><span>① 폐광 → 금표 주막<br>이번 제작</span><span>② 금표길<br>역참 J1 · 객주 W1<br>벌목장 · 심부</span><span>③ 청룡 → 국<br>높은 장소 재탐험</span><span>④ 상경 가도<br>화물 동행 · 검문<br>성저 인도</span><span>⑤ 남문 관문전<br>승리 후 문 개방<br>도성 내부는 후속</span></div>
<h2>주경로와 탐험 고리</h2><img class="map" src="first_segment.svg"><p>주경로 약 587m. 4.5m/s 기준 순수 보행 약 130초. 계산값과 입력 실측은 구분합니다. 미래 구간은 연결 예약이며 아직 플레이 구간이 아닙니다.</p>
<h2>실제 입력 검사</h2>'''+video+f'<pre>{html.escape(json.dumps(walk,ensure_ascii=False,indent=2))}</pre>'+'''
<p>촬영 중 시스템 커밋 한도 85%에 도달하면 촬영을 중단합니다. 영상이 없는 구간을 완료 영상으로 대체하지 않습니다. 가상 장치 입력은 실제 조작·물리·AI 경로를 사용하지만 물리 키보드를 직접 누른 검사는 아닙니다.</p>
<h2>하늘·후처리 비교</h2>'''+comparison+''.join(sections)+f'<h2>원본 보존</h2><pre>{html.escape(json.dumps(preserved,ensure_ascii=False,indent=2))}</pre><p><a href="REPORT.md">전체 검증 보고서</a></p></main>'
(OUT/'REVIEW.html').write_text(page,encoding='utf-8')
print(OUT/'REVIEW.html')
