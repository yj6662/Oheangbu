"""Report an isolated ink material/form study without changing or fabricating captures."""
from pathlib import Path
import json, hashlib, re
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy'
VIEWS=[('inn','주막 뒤 산'),('mountain_path','산길 조망')]
checks=[]
for key,title in VIEWS:
    before=json.loads((OUT/f'before_{key}_submissions.json').read_text())
    after=json.loads((OUT/f'after_{key}_submissions.json').read_text())
    receipt=json.loads((OUT/f'after_{key}_checks.json').read_text())
    for label in ('before','after'):
        checks.append({'item':f'{title} {label} 1920×1080','pass':Image.open(OUT/f'{label}_{key}.png').size==(1920,1080)})
    checks.append({'item':f'{title} scene/material restoration','pass':receipt['bindingsRestored'] and receipt['sceneFileUnchanged'] and receipt['sourceMaterialsUnchanged'] and not receipt['sceneDirtyAfter']})
    checks.append({'item':f'{title} vegetation submissions unchanged','pass':all(before[k]==after[k] for k in ('drawCalls','submittedInstances','visibleTriangles','billboardInstances')),'values':{k:after[k] for k in ('drawCalls','submittedInstances','visibleTriangles','billboardInstances')}})
    checks.append({'item':f'{title} shaders compile','pass':all(s.startswith('PASS') for s in receipt['shaderChecks'])})
baseline=json.loads((OUT/'baseline.json').read_text())
import base64
scene=ROOT/'Oheangbu'/baseline['scene']
checks.append({'item':'current compact scene file unchanged','pass':base64.b64encode(hashlib.sha256(scene.read_bytes()).digest()).decode()==baseline['sceneHash']})
summary={'status':'TECHNICAL_STUDY_CHECKS_PASS' if all(c['pass'] for c in checks) else 'FAIL','artistic_status':'REFERENCE_MATCH_NOT_ACHIEVED; NOT_APPLIED_TO_GAME_SCENE','checks':checks,'performance':'New shader CPU/GPU timing NOT MEASURED. Previous BroadBrush timing is not evidence for this study.','unverified':['Continuous camera motion and temporal aliasing','All-region visual transitions','Actual Play CPU/GPU cost','Artistic approval'],'excluded':['draft1_inn.png: new shader renderer registration was not complete; terrain was absent. It is not a valid visual comparison.']}
(OUT/'study_checks.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
form_exists=(OUT/'FormStudy.png').exists()
form='''<section id="form"><h2>별도 겹산 형태 실험</h2><p class="notice">기존 월드의 변경 결과가 아닙니다. 같은 산 재질과 새로 만든 둥근 지형을 분리된 Unity 미리보기 씬에서 렌더했습니다. 밝은 골짜기 바탕도 이 실험에서만 조정했습니다.</p><img class="wide" src="FormStudy.png" alt="별도 PreviewScene에서 렌더한 둥근 겹산 형태 실험"><p>이 실험은 실제 맵의 길·충돌·건물·콘텐츠에 적용되지 않았습니다. 둥근 형태에서도 획의 반복과 지나치게 매끈한 면이 남아 있습니다.</p><details><summary>같은 형태에서 기존 대기를 사용한 비교</summary><img class="wide" src="FormStudy_FullWash.png" alt="같은 겹산에 기존 대기를 적용한 형태 실험"></details></section>''' if form_exists else ''
sections=[]
for key,title in VIEWS:
    sections.append(f'''<section id="{key}"><h2>{title} · 동일 위치의 실제 Unity 렌더</h2><div class="controls"><button data-view="{key}" data-side="before">현재 맵</button><button class="selected" data-view="{key}" data-side="after">시험용 산 재질</button></div><figure><img id="image-{key}" class="wide" src="after_{key}.png" alt="{title} 시험용 산 재질"><figcaption id="caption-{key}">시험용 산 재질 · 1920×1080 · 본 씬 적용 전</figcaption></figure></section>''')
html='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>먹 면과 비백 · 분리 시안</title><style>
:root{color-scheme:dark;background:#1b1c19;color:#ece7dc;font:16px/1.75 system-ui,"Malgun Gothic",sans-serif}*{box-sizing:border-box}body{max-width:1400px;margin:auto;padding:32px 28px 90px}h1{font-size:clamp(28px,4vw,48px);line-height:1.2;font-weight:600}h2{font-size:23px;font-weight:550;margin-top:0}p{max-width:1000px}.eyebrow{color:#c2b39a;font-size:13px;letter-spacing:.12em}.notice{padding:14px 18px;background:#333128;border-left:3px solid #bba775}.wide{display:block;width:100%;height:auto;border:1px solid #494a43}section{margin:42px 0 0;padding-top:24px;border-top:1px solid #494a43}figure{margin:12px 0}figcaption{color:#bbb8ab;font-size:14px;padding-top:8px}button{font:inherit;border:1px solid #6f7165;padding:7px 17px;background:#252722;color:#ccc;cursor:pointer}.selected{background:#c6b899;color:#201f1b}.controls{display:flex;gap:8px;flex-wrap:wrap}a{color:#d5c6a7}nav{display:flex;gap:22px;flex-wrap:wrap}.reference{max-height:550px;width:auto;max-width:100%;display:block}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #494a43;padding:10px;text-align:left;vertical-align:top}th{width:25%}.small{font-size:14px;color:#bbb8ab}@media(max-width:650px){body{padding:20px 14px 50px}h2{font-size:20px}th{width:32%}}
</style><body><div class="eyebrow">2026.09.17 · INK PAINTING STUDY · 미술 승인 전</div><h1>검은 먹 면과 밝게 남긴 붓털</h1><p>기존의 부드러운 회색 띠를 대신해, 검은 먹 덩어리 위에 실제 붓자국의 갈라진 결과 밝은 틈을 넣은 재질 시안입니다.</p><p class="notice"><strong>참조의 느낌을 완성한 결과는 아닙니다.</strong> 현재 산의 긴 직선 능선·삼각 면과 수목 표현은 여전히 그림과 다릅니다. 전체 맵 적용은 하지 않았으며, 비교 촬영 후 기존 씬 재질을 복원했습니다.</p><nav><a href="#reference">사용자 참조</a><a href="#inn">주막 비교</a><a href="#mountain_path">산길 비교</a>FORM_LINK<a href="#verification">검사 범위</a></nav>
<section id="reference"><h2>이번 판단 기준</h2><img class="reference" src="reference.png" alt="사용자가 제공한 수묵 산수화 참조"><p>검은 산몸통과 밝게 비운 능선, 먹 안에서 길게 갈라지는 붓털, 골짜기의 여백이 함께 있어야 합니다. 참조 자체는 게임 텍스처로 사용하지 않았습니다.</p></section>
SECTIONS
FORM
<section><h2>이번에 바꾼 것과 남은 차이</h2><table><tr><th>먹과 종이</th><td>산만 독립적인 짙은 먹색과 밝은 여백을 사용합니다. 평지·길의 기존 명암은 유지했습니다.</td></tr><tr><th>붓자국</th><td>생성한 4종 붓 마스크를 실제 3D 산 표면의 고정 좌표에 투영했습니다. 화면 위에 산 그림을 합성한 결과가 아닙니다.</td></tr><tr><th>겹먹·비백</th><td>연결된 먹 바탕 위에 굵은 획을 놓고, 먹 안의 가는 밝은 틈을 복원했습니다. 획이 나열된 무늬로 읽히는 구간은 추가 조정이 필요합니다.</td></tr><tr><th>먼 먹의 유지</th><td>이 시안은 짙은 산 먹 면에만 대기 혼합 강도를18%로 줄였습니다. 대기 거리·평지·식생은 기존대로여서 수목과의 통합은 추가 검토가 필요합니다.</td></tr><tr><th>남은 산 형태</th><td>길게 솟은 경사벽과 삼각 면이 여전히 보입니다. 실제 배경 능선의 조형과 산세에 맞춘 획 방향 저작이 다음 단계입니다.</td></tr><tr><th>풍경 전체</th><td>나무의 잎 묘사·수관 덩어리와 골짜기 여백도 함께 맞춰야 참조의 통일감에 가까워집니다. 이번 시안에서는 식생을 바꾸지 않았습니다.</td></tr></table><details><summary>셰이더에 사용한 붓자국 원본</summary><img src="brush_atlas.png" class="reference" alt="생성한 4종 붓 마스크"><p class="small">imagegen으로 생성한 별도 텍스처입니다. 실제 게임 캡처가 아닙니다.</p></details></section>
<section id="verification"><h2>확인한 범위</h2><p>두 구도 1080p 전후 이미지, 셰이더 컴파일, 175개 렌더러의 원본 재질 복원, 씬 파일·원본 6개 재질 보존을 검사했습니다. 두 구도의 식생 제출 수는 전후 일치합니다.</p><p>새 셰이더의 실제 Play CPU/GPU 비용과 이동 중 떨림은 아직 측정하지 않았습니다. 이전 버전의 성능 수치를 이번 결과에 사용하지 않습니다. 영상·새 빌드·전역 지형 변경은 없습니다.</p><p><a href="REPORT.md">검토 보고서</a> · <a href="study_checks.json">기술 검사</a> · <a href="asset_provenance.json">텍스처 출처</a></p></section>
<script>for(const button of document.querySelectorAll('button[data-side]'))button.addEventListener('click',()=>{const view=button.dataset.view,side=button.dataset.side;const img=document.getElementById('image-'+view);img.src=side+'_'+view+'.png';img.alt=(side==='before'?'현재 맵':'시험용 산 재질')+' 실제 Unity 렌더';document.getElementById('caption-'+view).textContent=(side==='before'?'현재 맵 · 원본 보존':'시험용 산 재질 · 본 씬 적용 전')+' · 1920×1080';for(const other of document.querySelectorAll('button[data-view="'+view+'"]'))other.classList.toggle('selected',other===button)});</script></body></html>'''.replace('FORM_LINK','<a href="#form">별도 형태 실험</a>' if form_exists else '').replace('SECTIONS',''.join(sections)).replace('FORM',form)
(OUT/'REVIEW.html').write_text(html,encoding='utf-8')
(OUT/'REPORT.md').write_text('''# 먹 면·비백의 분리 시안 — 2026-09-17

**참조 스타일 미달. 전역 적용하지 않은 기술·시각 검토 시안이다.**

- 사용자 참조의 검은 산몸통, 밝은 능선 여백, 갈라진 붓털을 목표로 했다.
- 기존 BroadBrush의 회색 바탕+부드러운 감산 획만으로 이 대비가 나지 않아 산 전용 파생 셰이더 2개를 분리했다. 원본 공통 셰이더는 바꾸지 않았다.
- 생성한 흑백 붓 마스크 4종(실제1254²), 선형 샘플·밉맵·Trilinear·Aniso4를 사용한다. 생성 이미지는 붓 텍스처이며 비교 이미지는 실제 Unity 렌더다.
- 산의 검정은 linear(.008,.007,.006), 여백은 linear.20~.40·warm tint. 획 크기150~310m/440~780m, 큰 쪽으로 분산하고 연결된 먹 바탕 위에 합성한다. 어두운 획 안의 밝은 비백은 텍스처의 국부 평균과 실제 밝은 결의 차이로 복원한다. 평지/길 수식과 기본 대기 거리는 보존한다. 분리 시안에서는 짙은 산 먹 면에 대기 혼합 강도의18%만 남겨 먼 먹빛을 유지한다. 나무와의 대기 일치는 후속 조정 대상이다.
- 검은 면·갈라진 결은 실제 두 구도에 나타났으나, 단순 투영에 따른 획 반복·방향과 직선 산 형태가 참조에 미달한다. 배경산의 둥근 중첩 형태와 산세에 맞춘 획 방향, 수관 군집 표현을 함께 저작해야 한다.
- 현재 지도·충돌체·콘텐츠는 변경하지 않았다. 비교 시 재질을 잠깐 바꾸고 복원한다. 60초 타임아웃·도메인 리로드·에디터 종료·캡처 실패 때도 복원한다.
- 첫 draft1은 새 셰이더 등록 전 같은 Editor update에서 촬영하여 지형이 표시되지 않았다. 유효 비교에서 제외했으며 재질 등록과 캡처를 서로 다른 Editor update로 분리했다. 기술 검사 PASS는 미술 완성을 의미하지 않는다.
- 확인: 2구도 전후1080p, 셰이더오류0, 씬디스크동일·원본6재질동일·175렌더러복원. 식생 제출 일치: 주막583draw/54,120instances, 산길679draw/63,413instances.
- 미검증: 현재 시안의 실제Play CPU/GPU비용·이동중떨림·전지역전환·사용자미술승인. 정지RenderRequest소요시간은게임프레임타임이아니다.
- 영상·빌드없음. 촬영은순차,시스템커밋85%이상중단.

'''+('## 별도 형태 실험\n\nFormStudy.png는 기존 맵이 아닌 임시 PreviewScene에서 새 둥근 겹산과 같은 재질을 렌더했다. 골짜기 지면 명암은 이 실험에서만 밝게 했다. 원본 플레이 씬에는 적용하지 않았으며, 참조 완성본으로 간주하지 않는다.\n' if form_exists else ''),encoding='utf-8')
print(json.dumps({'status':summary['status'],'checks':len(checks),'form':form_exists,'review':str(OUT/'REVIEW.html')},ensure_ascii=False))
