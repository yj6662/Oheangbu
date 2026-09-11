from pathlib import Path
import json,shutil,subprocess,hashlib
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/WoodDeer';PAGE=OUT.parent/'WOOD_DEER_REVIEW.html'
model=json.loads((OUT/'model_report.json').read_text());audit=json.loads((OUT/'audit.json').read_text());capture=json.loads((OUT/'capture.json').read_text());shape=json.loads((OUT/'body_preservation.json').read_text())
total=model['totalTris'];leaf_count=model.get('necklaceLeaves',0)
unity_tris=json.loads((OUT/'unity_model.json').read_text())['triangles']
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
clips=[]
for clip in capture['clips']:
 if clip['frames']<25:continue
 frames=Path(clip['folder']);video=OUT/(clip['view']+'.mp4')
 subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-frames:v',str(clip['frames']),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(video)],check=True)
 subprocess.run([str(ff),'-v','error','-threads','2','-i',str(video),'-f','null','-'],check=True)
 clips.append(clip['view'])
complete=capture['status']=='PASS_DIAGNOSTIC_VISUAL_PENDING' and len(clips)==2
intro='C2 플레이·외부 영상 촬영 완료. 외형과 연출은 사용자 검토 대기입니다.' if complete else '외형·파일·수치 검사 완료. C2 영상은 메모리85% 제한으로 중단되어 미검증입니다.'
parts='\n'.join(f'| {r["name"]} | {r["tris"]:,} | {r["vertices"]:,} |' for r in model['parts'])
report=f'''# 곰 — 목질 사슴 외형·등장 연출

2026-09-11. {intro}

## 실제 변경과 전달

기존 Meshy 사슴을 Blender5.0.1 백그라운드에서 별도 작업본으로 수정했다. Blender MCP는 연결되지 않아 호출에 성공했다고 보고하지 않는다. 추가 Meshy 요청과 비용은0이다. 원본 .blend의 작업 전후 SHA256은 {model['sourceHashBefore']}로 동일하다.

어깨 높이를1.5m로 균일 정규화했다. 전체 뿔 높이는약2.59m다. 중복 위치 정점만 병합하고 부드러운 노멀을 다시 계산했다. 원본 몸의 정규화 후 위치 최대 오차는{shape['maxBodyPositionErrorM']}m다. 얼굴·몸·가지뿔의 형상을 새로 조각하지 않았다.

프로젝트 HwaseongHaenggung/Textures/Additions/T_Bark_BC.png의 수피 영역을 몸의 기존UV에2048×2048 BaseColor/Normal로 베이크했다. 회갈색 틴트와 높은 거칠기를 사용한다. 가슴·발목의 국부 뿌리14가닥과 실제 뿔 표면에 붙인 새잎20장을 추가했다. 바닥에서 자라는 짧은12가닥은 별도 VFX 메시1320tris이며 모델 예산과 분리한다. 합계 모델+이 연결부는17,031tris다. KTP와 입자 렌더 비용은 별도다.

| 파츠 | 실제 FBX tris | Blender 정점 |
|---|---:|---:|
{parts}
| 합계 | {model['totalTris']:,} | {sum(r['vertices'] for r in model['parts']):,} |

모델 재질3개. 리깅·스키닝·보행·공격·AI 없음. Unity 실제 임포트도15,711tris다. Blender용 파일은 `WoodDeer_Working.blend`, FBX·텍스처·프리팹은 `Assets/_Project/Art/SpellVFX120/WoodDeer`에 있다. Unity에서 프리팹은 정적 모델이고, 연출은 Vfx120Effect의 곰 전용 분기가 담당한다.

## 표현 연결

`WoodDeerPresentation`을 곰 프로필만 활성화한다. 기존 프로필은 Baseline_Gom.asset에 보존한다. SummonPrefab 원형·놈·솜·몸·옴은 교체하지 않는다. SpellBook에 신규 게임 기능을 등록하지 않는다. 기존 Duration4.7도 변경하지 않고 표현 분기에서만 검토용4.6초를 쓴다.

플레이어 전방4m의 지점을 최초1회 지면 조회로 고정한다. 네 발은 개별 지면 높이를 사용하고 아래35cm만 제한적으로 피팅한다. 중심 기준 높이차15cm 초과 또는 지면 없음은 표시하지 않는다. 모든 지형·계단 검증을 의미하지 않는다.

0~0.35초 KTP Bottom 사본을 표시한다.0.2~1.2초 높이와 불규칙한 경계로 발에서 뿔까지 드러난다.1.2~3.8초 정적 몸을 유지하고 새잎만 미세하게 움직인다.3.8~4.6초 높은 부분부터 절삭 소멸하며 잎·목편 입자가 퍼지고 발치가 마지막에 사라진다. 모든 표현은 같은 경과 시간을 쓴다. 전신 스케일 애니메이션·가짜 보행·카메라 추종·전역 블룸 변경은 없다.

## 검증 상태

- 통과: Blender 빈 장면 FBX 왕복의 파츠별 삼각형 수. 원본 .blend 보존. 몸 정점 위치 보존.2K 베이크 이미지 파일.
- 통과: Unity import15,711tris·재질3개·셰이더 컴파일. 곰 전용 옵션과 게임 Duration 유지.
- 통과: {len(audit['passed'])}개 수치 검사.30/60/120fps×1배/0.2배에서 고정 중심·수명·끝의 입자/표시 정리·시간에 따른 몸 메시 불변·콜라이더/Animator 없음. 발 앵커 간격 최대{audit['maxFootGap']*1000:.2f}mm, 실제 발 메시 평지 간격2.5cm 이내.
- 확인: 같은 Blender 카메라의 외형 전후 렌더. 최종 미술 합격은 사용자에게 맡긴다.
- C2 촬영: {capture['status']}. 확보 영상 {len(clips)}개. 첫 시도2프레임, 재시도3프레임에서 시스템 커밋85%에 닿아 중단한 기록을 보존했다. 성공한 최신 캡처가 있으면 capture.json을 따른다. 한 번에 한 클립만 촬영하며 매12프레임 GC, 종료/중단 시 RenderTexture·카메라·모델 사본을 해제한다.
- 미검증: 실제 작도·적AI·소환수 전투/피해, 리깅·애니메이션, 전체 지형/카메라와 최종 연출 가시성. 실행하지 못한 C2 영상 검사를 수치검사 통과로 대신하지 않는다.

현재 상태: **{'VFX 시안 완료 / 사용자 검토 대기' if complete else '모델·코드 완료 / C2 영상 검증 미완료'}**. 사용자의 곰 검토 전에는 놈 제작을 시작하지 않는다.
'''
report=report.replace('15,711',f'{total:,}').replace('17,031',f'{total+1320:,}')
report=report.replace(f'Unity 실제 임포트도{total:,}tris',f'Unity 실제 임포트는{unity_tris:,}tris').replace(f'Unity import{total:,}tris',f'Unity import{unity_tris:,}tris')
report+=f'\n## 잎 목걸이 후속 수정\n\n뿔의 새잎 {model.get("antlerLeaves",20)}장을 유지하고, 목 표면을 따라 잎 {leaf_count}장과 얇은 연결 덩굴을 추가했다. 같은 잎 재질을 사용해 재질 수는 3개다. 직전 모델·FBX·이미지는 BeforeNecklace 폴더에 보존했다. C2 영상 상태는 위와 같으며 새 외형을 촬영했다고 보고하지 않는다.\n'
(OUT/'REPORT.md').write_text(report,encoding='utf-8')
videohtml=''.join(f'<section><h2>{"플레이 시점" if x=="play" else "외부 시점"}</h2><video controls preload="metadata" src="WoodDeer/{x}.mp4"></video></section>' for x in clips)
html=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>곰 · 목질 사슴</title><style>body{{max-width:1650px;margin:auto;padding:28px;background:#191d19;color:#eee9dc;font:17px/1.65 system-ui}}h1{{margin-bottom:4px}}a{{color:#bfd39b}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:18px}}img,video{{width:100%;background:#333}}.note{{padding:16px;background:#34372a}}@media(max-width:800px){{.pair{{grid-template-columns:1fr}}}}</style>
<h1>곰 · 목질 사슴</h1><p>수피 · 가슴과 발목의 뿌리 · 가지뿔의 새잎</p><p class="note"><b>정적 외형·등장 연출 검토 / 리깅·전투 미연결</b><br>{intro}</p>
<div class="pair"><section><h2>기존 사슴 · 동일 크기·카메라</h2><img src="WoodDeer/model_before.png" alt="기존 사슴"></section><section><h2>목질 외형 수정판</h2><img src="WoodDeer/model_after.png" alt="목질 사슴"></section></div><div class="pair">{videohtml}</div>
<p>모델15,711삼각형 · 재질3개 · 어깨 높이1.5m · 추가 Meshy 사용0 · 수치 검사{len(audit['passed'])}개 통과</p><p><a href="WoodDeer/WoodDeer_Working.blend">Blender 편집 원본</a> · <a href="WoodDeer/REPORT.md">변경·검증 보고서</a> · <a href="WoodDeer/fbx_roundtrip.json">FBX 왕복 통계</a></p>
<p>외형을 먼저 검토해 주세요. C2 촬영 중단 자료는 완성된 소환 연출 영상으로 표시하지 않습니다.</p></html>'''
html=html.replace('모델15,711',f'모델{total:,}').replace('가지뿔의 새잎</p>','가지뿔의 새잎 · 잎 목걸이</p>').replace('WoodDeer/model_after.png','WoodDeer/model_after.png?v=necklace1')
html=html.replace('<p>외형을 먼저', '<p><a href="WoodDeer/WoodDeer_Delivery.zip">모델·텍스처·프리팹·보고서 다운로드</a></p><div class="pair"><section><h2>잎 목걸이 추가 전</h2><img src="WoodDeer/BeforeNecklace/model_after.png"></section><section><h2>잎 목걸이 추가 후</h2><img src="WoodDeer/model_after.png?v=necklace1"></section></div><p>외형을 먼저')
PAGE.write_text(html,encoding='utf-8');print(intro)
