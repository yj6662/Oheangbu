from pathlib import Path
import json,subprocess
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/AreaNatural'
page=(ROOT/'Tools/SpellVFX120/area_branch_review.html').read_text(encoding='utf-8').replace('AreaBranch/','AreaNatural/').replace('모 — 하나의 큰 줄기에서 분기','모 — 불규칙 분기 · 언덕 지면 추종')
page=page.replace('굵은 주 균열 하나 → 두 갈래 → 네 개의 가는 균열로 이어지며 돌가루가 경로를 따라 분출합니다.','시전마다 갈림점·길이·굴곡·두께가 달라집니다. 하나의 큰 줄기에서 작은 줄기로 이어지며, 균열 양쪽 가장자리와 돌가루가 지면을 따릅니다.')
start=page.index('<details open><summary>변경값과 검증 범위</summary>');end=page.index('<p>1080p',start)
page=page[:start]+'''<details open><summary>변경값과 검증 범위</summary><p>고정된 대칭 분기를 절차생성으로 변경했습니다. 첫 갈림점은 전체 거리24~34%, 양쪽 다음 갈림점은 각각50~65%·55~70%에 생깁니다. 끝 가지의 길이·좌우 위치·굴곡·두께도 다릅니다. 시전이 바뀌면 모양이 달라지고, 같은 시전의 재생·시점 변경에서는 같은 모양을 유지합니다.</p><p>언덕에서는 약10cm 간격으로 균열 중심과 양쪽 가장자리를 실제 지면에 맞춥니다. 지면이 없거나 높이가 급격하게 단절된 구간은 허공으로 이어 붙이지 않습니다. 돌가루도 같은 경로를 사용합니다. 피해 폭12m·위력5·속도7m/s는 유지했습니다.</p>
'''+page[end:]
page=page.replace('직전 AreaRift 설정','직전 AreaBranch 설정').replace('href="AREA_RIFT_REVIEW.html"','href="AREA_BRANCH_REVIEW.html"')
page=page.replace("'모':'모 — 큰 줄기 하나 → 두 갈래 → 네 개의 작은 줄기'","'모':'모 — 시전별 비대칭 분기 · 실제 지면에 맞춘 균열'")
section='''<details open><summary>언덕 진단 — 전투 영상과 별도</summary><p>높이 약2.5m의 굴곡진 시험 지형입니다. 아래 세 결과는 서로 다른 시드입니다. 이미지는1080p이며 라벨은 이미지 밖에 표시했습니다. 이 시험은 지면 추종 확인용으로, C2 전투·전체 성능 검증을 대체하지 않습니다.</p><video controls playsinline preload="metadata" src="AreaNatural/hill_motion.mp4" poster="AreaNatural/hill_seed_1.jpg"></video><div class="pair"><section><h2>생성 결과1</h2><img loading="lazy" src="AreaNatural/hill_seed_1.jpg" alt="언덕의 첫 생성 결과"></section><section><h2>생성 결과2</h2><img loading="lazy" src="AreaNatural/hill_seed_2.jpg" alt="언덕의 두 번째 생성 결과"></section><section><h2>생성 결과3</h2><img loading="lazy" src="AreaNatural/hill_seed_4.jpg" alt="언덕의 세 번째 생성 결과"></section></div></details>'''
page=page.replace('<script>const reports=',section+'<script>const reports=')
(ROOT/'Tools/SpellVFX120/area_natural_review.html').write_text(page,encoding='utf-8')
source=(ROOT/'Tools/SpellVFX120/publish_area_five.py').read_text(encoding='utf-8').replace('AreaFive','AreaNatural').replace('AREA_FIVE_REVIEW.html','AREA_NATURAL_REVIEW.html').replace('area_five_review.html','area_natural_review.html')
source=source.replace("for glyph in '고노소모오':","for glyph in '모':").replace("'013_ACE0','037_B178','061_BAA8','085_C18C','109_C624'","'061_BAA8'").replace('clips=20,stills=40','clips=4,stills=8').replace('profilesOutsideFiveUnchanged=True','profilesOutsideMoUnchanged=True,spellBookUnchanged=True')
exec(compile(source,'publish_area_natural_generated','exec'))
subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','6','-i',str(OUT/'hill_motion_%02d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(OUT/'hill_motion.mp4')],check=True)
subprocess.run([str(ff),'-v','error','-threads','2','-i',str(OUT/'hill_motion.mp4'),'-f','null','-'],check=True)
audit=json.loads((OUT/'audit.json').read_text(encoding='utf-8'));assert audit['status']=='PASS'
scope=json.loads((OUT/'scope.json').read_text(encoding='utf-8'))
text=f'''# 모 — 불규칙 절차생성 분기와 지면 추종

2026-09-10. 모만 변경했다. 기존 AreaBranch 프로필과 영상, 다른119프로필·SpellBook·공급자 원본을 보존했다.

## 구현

- 하나→둘→넷의 가지 구조를 유지하면서 갈림점, 좌우 위치, 각 가지의 길이, 굴곡·두께를 시드 기반으로 생성한다. 첫 갈림점24~34%, 왼쪽 다음 갈림점50~65%, 오른쪽55~70%, 끝 가지84~100% 범위. 좌우 복제·반사가 아니다.
- 실제 모 시전마다 CombatLoopWiring이 VisualSeed를 증가시켜 AreaImpactPlan에 전달한다. 생성기는 별도 System.Random을 사용하며 Unity 전역 난수와 피해 계산을 변경하지 않는다. 한 계획을 재생할 때 형상을 다시 뽑지 않는다.
- 카메라를 향하던 선을 지면에 놓인7개 메시 띠로 교체했다. 약10cm 간격으로 중심과 좌우 가장자리를 모두 실제 콜라이더에 투영한다. 가지마다 최대256지점. 지면에서1.2cm 띄워 Z충돌을 줄인다.
- Default/WorldGround/WorldRidge/WorldRibbon 레이어 중 지지 가능한 면을 사용한다. 캐릭터·적·트리거·효과 자체는 제외한다. 높이16m 위에서48m 아래로 조회하며, 지면 누락 또는 인접 중심 높이 차35cm 이상에서는 면을 연결하지 않는다. 가파른 절벽을 내려가는 연출은 이번 목표가 아니다.
- 돌가루는 같은 생성 경로·캐시 높이에서 분출한다. 시전 초기 지면 조회만 수행하고 프레임별 Raycast는 없다. 생성한 메시7개는 효과 정리 시 해제한다. 기존 입자 개수 상한은 유지했다.
- 피해 폭12m·위력5·전진7m/s·피해 예약은 그대로다. 시각적 줄기 사이도 기존 광역 피해 범위에 포함된다.

## 검증

- **통과:** {len(audit['checks'])}개 검사.100시드의 재현성·시드 차이·비대칭·부모/자식 연결·경로 폭, Unity 난수 불변.30/60/120fps 및0.2배 효과 시간 샘플링에서 표시·입자·종료·피해 계획 불변.
- **통과:** 굴곡진 언덕에서 메시 정점과 삼각형 중심을 콜라이더와 대조. 지면1.2cm 오프셋 제외 최대 오차 {audit['maxSurfaceError']*1000:.3f}mm. 마지막 시험 시드 기준 정점{audit['vertices']}개, 초기 조회{audit['groundQueries']}회. 지면이 없을 때 허공에 면을 생성하지 않음.
- **통과:** C2 기존/수정·플레이/외부4클립 모두 실제 예약 피해9회·고유 접촉9회·범위 밖0회·종료 후 정리.1080p24fps각6초, 전체 영상 디코딩 완료.
- **통과:** 다른119프로필·SpellBook 전체·공급자 원본 해시 보존. 순차 촬영 시작 최대 커밋{scope['maxCommitRatio']:.1%};85% 중단 기준 유지.
- **제공:** C2 비교4영상·8정지 이미지. 별도 언덕 시드3이미지와12연속 프레임/6fps 진단 영상. 언덕은 실제 C2 전투가 아닌 지면 추종용 시험이다.
- **미검증:** 손글씨 입력·적 AI 전체 전투, 모든 지형/다층 건물·움직이는 바닥, 전체 게임 성능. 지면 캐시는 시전 시점의 정적 지형 기준이다. 초기 전체 효과 생성 관측 최대{audit['maxBuildMilliseconds']:.1f}ms는 Editor 진단값이며 실제 플레이 CPU 비용 판정으로 사용하지 않는다.
- **비주얼:** 사용자 판단 대기.

## 파일

- `AREA_NATURAL_REVIEW.html`: 통합 비교
- `AreaNatural/모`: C2 클립·원본 프레임·settings.json·report.json
- `AreaNatural/hill_*`: 언덕 진단 결과
- `AreaNatural/audit.json`, `scope.json`: 수치 검사·보존 검사
- Unity `Assets/_Project/Art/SpellVFX120/AreaNatural`: 이전 프로필·SpellBook 사본

이전 비교는 `AREA_BRANCH_REVIEW.html`에 남아 있다.
'''
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
status=ROOT/'Docs/PROJECT_STATUS.md';entry='**2026-09-10 · 모 불규칙 분기:** 시전별 시드로 비대칭 경로를 생성하고 실제 지면에 맞춘 메시·돌가루를 사용한다. 언덕/종료/시간 검사 및 C2 비교 통과, 다른119프로필·피해 설정 보존. 비주얼 검토 대기. [비교](http://127.0.0.1:8771/AREA_NATURAL_REVIEW.html) · [보고서](../Art/SpellVFX120/AreaNatural/REPORT.md).\n\n'
current=status.read_text(encoding='utf-8')
if not current.startswith('**2026-09-10 · 모 불규칙 분기:'):status.write_text(entry+current,encoding='utf-8')
print('Published natural rift review and report')
