"""Assemble factual still-only playtest review from saved Unity reports."""
from pathlib import Path
import json, html, hashlib, datetime, sys, os

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Playtest/VisualCorridor'
PLAY = OUT.parent

def read(path, default=None):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else (default or {})

def esc(value):
    return html.escape(str(value), quote=True)

views = [
    ('mine_exit', '폐광 출구', '기존 갱도와 지형을 보존하고 실제 첫 구간 경로에 길 표면과 식생을 보완했다.'),
    ('mountain_path', '산길', '길 주변 바위·고사리·수목을 보유 에셋으로 배치하고 통행 폭을 비웠다.'),
    ('forest_approach', '청림 숲 진입', '대나무와 활엽수 군락을 기존 지리에 맞춰 보완했다.'),
    ('capital_approach', '황경 남쪽 접근', '원경 도시와 남문. 전역 지형과 하늘 설정은 유지했다.'),
    ('gate_street', '성저에서 도성 방향', '실제 한옥·정자·생활 소품을 사용한 외부 경관이다. 도시 퀘스트가 연결된 구간은 아니다.'),
    ('palace_front', '궁궐 진입', '기존 궁궐을 보존하고 석재 진입부·주변 건물을 보완했다.'),
    ('capital_aerial', '황경 조감', '예약 건물의 흰 대체 형상을 보유 에셋으로 교체했다. 지역 전체의 최종 미술 승인을 뜻하지 않는다.'),
    ('gate_close', '남문 세부', '길이 되돌아 나오는 광장 남쪽으로 문을 옮기고 기둥·처마 비례를 조정했다.'),
]
g = read(OUT/'validation.json')
lod = read(OUT/'HouseLOD/house_lod_report.json')
palace = read(OUT/'palace_access.json')
preserve = read(OUT/'preservation.json')
player = read(PLAY/'PlayerAppearance/validation.json')
summons = read(PLAY/'SummonCast/runtime_review.json')
perf = read(PLAY/'performance_on.json')
builds = sorted((ROOT/'Builds/Playtest-20260915').glob('*/release_build_report.json'))
build = read(builds[-1]) if builds else {}
package = read(builds[-1].parent/'package_report.json') if builds else {}
smokes = sorted(builds[-1].parent.glob('smoke_*.json')) if builds else []
smoke = [(p, read(p)) for p in smokes]
manifest = read(OUT/'asset_manifest.json')
now = datetime.datetime.now(datetime.timezone.utc).isoformat()

report = f'''# 9월 15일 플레이테스트 시각 통합 — TEST

작성 UTC: {now}

## 이번 적용

- 별도 `W_WorldMacro_Playtest` 씬에서 폐광→주막 산길, 숲과 상경 가도의 길 표면·주변 식생을 보완했다. 전역 지형 재생성은 하지 않았다.
- 황경 남문·성곽·한옥·정자·생활 소품을 보유 에셋으로 구성했다. 건물 48개의 흰 대체 외형을 교체했다. 궁궐 원본의 전각·테라스는 보존했다.
- C02 A2/B2/C3 기존 모델을 숄더뷰 플레이어로 연결했다. 높이 1.75m, 충돌 캡슐 지름 0.56m, {player.get('triangles','미검증'):,}삼각형. 실제 이동 속도로 대기·보행·달리기를 구동하며 Root Motion은 껐다.
- 곰·놈·몸·솜·옴을 Playtest 전용 SpellBook에서 실제 시전 승인·먹 소비·고정 위치 표시·자연 만료에 연결했다. 소환 이동·공격·피해·방어·어그로는 추가하지 않았다. 4.6초는 TEST 표현 수명이다.
- 예약 콘텐츠 표시를 외부 플레이에서 숨기고 실제 조사·대화 대상과 콘텐츠 ID는 유지했다.

## 검사 결과

| 항목 | 결과 | 근거와 범위 |
|---|---|---|
| 원본/공용 파일 보존 | {'통과' if not preserve.get('changed',['unknown']) else '실패'} | {preserve.get('checked',0):,}개 SHA-256 비교, 변경 {len(preserve.get('changed',[]))}개. `preservation.json` |
| 숲·상경 연결로 | {'통과' if g.get('passed') else '실패'} | {g.get('capsuleStations',0):,}개 정적 캡슐 표본, 막힘 {g.get('capsuleBlockers','?')}, 지지 없음 {g.get('routeSupportFailures','?')}. 자동 보행이 아니다. |
| 궁궐 접근 | {'통과' if palace.get('passed') else '실패/확인 중'} | {palace.get('stations',0)}개 표본, 단차 위반 {palace.get('stepViolations','?')}, 캡슐 막힘 {palace.get('capsuleBlockedStations','?')}. `palace_access.json` |
| 플레이어 구조 | {player.get('status','미검증')} | Humanoid·프로필 로딩·원본 텍스처·신장·렌더러 소유권. `../PlayerAppearance/validation.json` |
| 플레이어 정지 런타임 진단 | 통과 | Play 모드 13개 검사, 임시 검토 카메라의 전/후 숄더 구도. 대기 상태만 관찰했고 이동 중 도포/발 미끄러짐과 손 파지는 미검증. |
| 소환수 5종 | {summons.get('status','미검증')} | 각 1회 승인·먹 소비, 적 HP/방어 상태 불변, 종료 등록 객체 {summons.get('finalRegistryCount','?')}개. 인식 완료 이벤트를 주입한 실제 서비스 검사다. 직접 손글씨 인식 검사는 아니다. |
| 한옥 LOD | {lod.get('status','미검증')} | {lod.get('houses',0)}채. LOD0 {lod.get('sourceTriangles',0):,}, LOD1 {lod.get('lod1Triangles',0):,}, LOD2 {lod.get('lod2Triangles',0):,} tris/채. 공유 메시, 원본 LOD0 보존. |
| 렌더 자산 누락 | {'통과' if g.get('missingMeshes',1)+g.get('missingMaterials',1)==0 else '실패'} | 메시 {g.get('missingMeshes','?')}, 재질/셰이더 {g.get('missingMaterials','?')}개 누락. |
| 빌드 | {build.get('status','미생성')} | {build.get('buildResult','미검증')}, 오류 {build.get('totalErrors','?')}, 경고 {build.get('totalWarnings','?')} |
| 직접 보행·차량 완주·최종 미술 | 미검증 | 사용자가 판단할 항목. 영상이나 자동 완주는 만들지 않았다. |

## 성능과 보존

주막 Editor Play 1920×1080, 120표본: 프레임 중앙값 {perf.get('medianFrameMs',0):.3f}ms, P95 {perf.get('p95FrameMs',0):.3f}ms, CPU 중앙값 {perf.get('medianCpuMs',0):.3f}ms, GPU 중앙값 {perf.get('medianGpuMs',0):.3f}ms. 이 값은 마지막 한옥 LOD 적용 전이며, 최종 빌드 전체 구간 120fps 달성을 증명하지 않는다. 정적 시작점 빌드 측정은 아래 별도로 기록한다.

새 배치 {g.get('placements',0)}개, LODGroup {g.get('lodGroups',0)}개. `allLodTriangles`는 동시에 그려지는 삼각형 수가 아니라 모든 LOD 자산을 더한 수치다. 캐시의 소스 해시 불일치 검사와 재질의 GUID+local file ID 식별을 추가했다. 일반 닫힌 건물은 뒷면을 그리지 않으며, 지상 시점에서 안쪽이 보이는 남문 기와 시트만 양면 예외로 두었다.

씬 소유 식생 제외 데이터를 사용해 실제 길·건물·출입 공간을 비웠다. 원본 팔레트, C2, 공용 PlayerRig, 원래 SpellBook, 공급자 모델/텍스처를 보존했다. 신규 Meshy 요청과 추가 유료 생성은 0건이다.

## 범위와 남은 항목

- 첫 폐광→금표 주막의 조사·전투·휴식·저장 체계는 유지한다. 이후 황경까지는 외부 경관과 연결로 검토 범위이며, 본편 퀘스트/도시 내부 콘텐츠 완료가 아니다.
- C02에는 손가락 본이 없다. 이번 결과는 자연스러운 붓 파지·전신 작도 IK·천 물리 완성을 주장하지 않는다. 측면 이동은 기존 전진 모션을 재사용한다.
- 소환수는 기존 승인 외형의 정적 등장·소멸이다. 적/NPC는 기존 임시 외형이다.
- 지형의 넓은 빈 공간, 마을 밀도, 산의 근거리 표현은 스크린샷으로 최종 판단할 사항이다. 건물 외관 교체를 최종 미술 승인으로 취급하지 않는다.
- 정적 경로 표본 통과는 사용자의 전 구간 완주나 차량 통과를 대신하지 않는다.
- 실행물은 BUILD CANDIDATE이며 다음 사용자 플레이 결과를 반영할 기준 빌드다.

## 근거 파일

`validation.json`, `asset_manifest.json`, `preservation.json`, `material_optimization.json`, `palace_access.json`, `HouseLOD/house_lod_report.json`, `../PlayerAppearance/RuntimeReview/runtime_review.json`, `../SummonCast/runtime_review.json`. 계획은 `Docs/Plans/PLAYTEST-VISUAL-CORRIDOR-20260915.md`.
'''
if build:
    report += '\n실행 파일: `' + build.get('executable','') + '`\n'
if package:
    report += '\n배포 ZIP: `' + package.get('archive','') + '`\n\n압축 CRC 검사: ' + str(package.get('crcVerified',False)) + '\n'
for path, s in smoke:
    if not s.get('performance',{}).get('renderedSampleValid',False):
        report += '\n숨김 창 실행: 렌더링 여부가 확인되지 않아 아래 update-loop 속도와 CPU/GPU 0값을 게임 FPS/렌더 비용으로 사용하지 않는다.\n'
    report += '\n### 실행 파일 검사 — '+path.name+'\n\n상태: `'+s.get('status','미검증')+'`\n\n```json\n'+json.dumps(s.get('performance',{}),ensure_ascii=False,indent=2)+'\n```\n'
(OUT/'REPORT.md').write_text(report,encoding='utf-8')

style = '''body{margin:0;background:#1a1d1a;color:#e5e3d8;font:16px/1.65 system-ui,"Malgun Gothic",sans-serif}header,main{max-width:1440px;margin:auto;padding:28px}h1{font-size:32px;line-height:1.25}h2{margin-top:45px}a{color:#b9d3b3}nav{display:flex;flex-wrap:wrap;gap:14px}.note{padding:18px;background:#31362e;border-left:3px solid #b2bd90}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}img{width:100%;height:auto;aspect-ratio:16/9;background:#111;object-fit:contain}figcaption{font-size:14px;color:#bec6b9}section{scroll-margin-top:20px}details{margin:18px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere;font-size:13px}footer{padding:25px;color:#b6bcae}@media(max-width:760px){.pair{grid-template-columns:1fr}}'''
parts = [f'<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>폐광·청림·황경 / 플레이어·소환수 검토</title><style>{style}</style><header><p>오행부 · 2026-09-15 PLAYTEST / TEST</p><h1>폐광·청림·황경<br>플레이어와 소환수 통합</h1><p class="note">보유 에셋을 이용한 경관 보완과 C02 플레이어, 정적 소환수 5종의 시전 연결입니다. 미술 최종 판단과 직접 보행 검토는 아직입니다. 아래 환경 이미지는 고정 시점 Editor 렌더, 배우 이미지는 실제 Play 모드 진단입니다. 영상·자동 완주는 제작하지 않았습니다.</p><nav>']
parts += [f'<a href="#{id}">{esc(title)}</a>' for id,title,_ in views]
parts += ['<a href="#player">플레이어</a><a href="#summons">소환수 5종</a><a href="#checks">검사·빌드</a></nav><p><a href="REPORT.md">상세 보고서</a> · <a href="asset_manifest.json">자산 원장</a> · <a href="preservation.json">원본 보존 검사</a></p></header><main>']
image_manifest=[]
def figure(rel, caption):
    path=OUT/rel
    if not path.exists(): return '<p>이미지 미확보</p>'
    image_manifest.append(dict(file=rel,sha256=hashlib.sha256(path.read_bytes()).hexdigest(),caption=caption))
    return f'<figure><a href="{esc(rel)}" target="_blank"><img src="{esc(rel)}" alt="{esc(caption)}" loading="lazy"></a><figcaption>{esc(caption)}</figcaption></figure>'
for id,title,description in views:
    parts += [f'<section id="{id}"><h2>{esc(title)}</h2><p>{esc(description)}</p><div class="pair">']
    if (OUT/f'before_{id}.png').exists(): parts += [figure(f'before_{id}.png','작업 전')]
    parts += [figure(f'{id}.png','현재 작업본'),'</div>']
    a,b=read(OUT/f'before_{id}.json'),read(OUT/f'{id}.json')
    matching=a and all(a.get(k)==b.get(k) for k in ('position','euler','fieldOfView'))
    parts += [f'<p>1920×1080 · {"동일 카메라" if matching else "개별 구도 — 정밀 픽셀 비교용 아님"} · <a href="{id}.json">촬영 위치·조건</a></p></section>']
parts += ['<section id="player"><h2>C02 플레이어</h2><p>실제 Play 씬의 정지 플레이어를 임시 숄더 검토 카메라로 촬영했습니다. 이동 속도에 따른 모션 혼합은 연결했으며, 이 진단에서는 대기 상태만 관찰했습니다.</p><p><a href="../PlayerAppearance/RuntimeReview/runtime_review.json">플레이어 런타임 검사</a></p><div class="pair">',figure('../PlayerAppearance/RuntimeReview/player_shoulder_front.png','전면 확인(정지)'),figure('../PlayerAppearance/RuntimeReview/player_shoulder_behind.png','후면 숄더 구도(정지)'),'</div></section><section id="summons"><h2>정적 소환수 5종</h2><p>인식이 완료된 글자 이벤트를 주입해 실제 시전 승인→먹 소비→VFX 수명 경로로 확인했습니다. 직접 필기 인식·이동·공격은 이 이미지의 검증 범위에 포함되지 않습니다.</p><p><a href="../SummonCast/runtime_review.json">소환수 런타임 검사</a></p>']
for file,title in [('01_gom','곰 · 목질 사슴'),('02_nom','놈 · 해태'),('03_mom','몸 · 도깨비'),('04_som','솜 · 호랑이'),('05_om','옴 · 거북')]:
    parts += [f'<h3>{title}</h3>',figure(f'../SummonCast/summon_{file}.png',title+' / 실제 Play 서비스 진단')]
parts += ['</section><section id="checks"><h2>기술 검사와 빌드</h2>']
parts += ['<p><a href="../performance_on.json">Editor 고정 지점 성능 측정</a></p>']
if package:
    archive_rel = Path(os.path.relpath(package['archive'],OUT)).as_posix()
    parts += [f'<p><a href="{esc(archive_rel)}">플레이테스트 ZIP 받기</a> · 압축을 모두 푼 뒤 Play_1080p.bat 실행</p>']
for title,data in [('경로·자산',g),('궁궐 진입', {k:v for k,v in palace.items() if k not in ('samples','decks')}),('한옥 LOD',lod),('빌드',{k:build.get(k) for k in ('status','buildResult','executable','totalErrors','totalWarnings','totalSizeBytes')})]:
    parts += [f'<details><summary>{esc(title)}</summary><pre>{esc(json.dumps(data,ensure_ascii=False,indent=2))}</pre></details>']
for path,s in smoke:
    smoke_rel=Path(os.path.relpath(path,OUT)).as_posix()
    parts += [f'<details><summary>실행 파일: {esc(s.get("status","미검증"))} / {path.name}</summary><p><a href="{esc(smoke_rel)}">원본 JSON</a></p><p>숨김 창 검사에서 renderedSampleValid=false이면 루프 속도를 게임 FPS로 해석하지 않습니다.</p><pre>{esc(json.dumps(s,ensure_ascii=False,indent=2))}</pre></details>']
parts += ['<p class="note">120fps 달성은 확인되지 않았습니다. 에디터 측정과 정적 시작점 빌드 측정은 전체 구간의 보행·전투 성능을 대신하지 않습니다.</p></section></main><footer>원본 공급자 에셋·기준 C2·공용 PlayerRig 보존. 추가 유료 생성 0건.</footer></html>']
(OUT/'REVIEW.html').write_text('\n'.join(parts),encoding='utf-8')
(OUT/'image_manifest.json').write_text(json.dumps(image_manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(review=str(OUT/'REVIEW.html'),images=len(image_manifest),build=build.get('status','not-built')),ensure_ascii=False))
