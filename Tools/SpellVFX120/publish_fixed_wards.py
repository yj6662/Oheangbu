from pathlib import Path
import json,subprocess,shutil,hashlib
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/FixedWards'
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
rows=[];audits=[]
for glyph in '구무수우누':
 r=json.loads((OUT/glyph/'report.json').read_text(encoding='utf-8'));a=json.loads((OUT/glyph/'audit.json').read_text(encoding='utf-8'))
 assert r['status']=='PASS_VFX_DIAGNOSTIC_VISUAL_PENDING' and a['status']=='PASS',(r,a)
 for c in r['clips']:
  frames=Path(c['folder']);mp4=frames.with_suffix('.mp4')
  if not mp4.exists() or mp4.stat().st_mtime<max(p.stat().st_mtime for p in frames.glob('*.jpg')):
   subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(frames/'%04d.jpg'),'-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(mp4)],check=True)
  subprocess.run([str(ff),'-v','error','-threads','2','-i',str(mp4),'-f','null','-'],check=True)
  for label,index in [('formation',10),('contact',28)]:shutil.copyfile(frames/f'{index:04d}.jpg',frames.parent/(frames.name+'_'+label+'.jpg'))
 r['auditChecks']=len(a['checks']);rows.append(r);audits.append(a);print(glyph+' encoded and decoded',flush=True)
hashes=json.loads((OUT/'before_hashes.json').read_text(encoding='utf-8'))
changed=[p.replace('\\','/') for p,h in hashes.items() if hashlib.sha256((ROOT/p).read_bytes()).hexdigest()!=h]
expected={f'Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles/{p}.asset' for p in ['019_AD6C','067_BB34','091_C218','115_C6B0','043_B204']}
if set(changed)!=expected:raise RuntimeError(changed)
scope=dict(status='PASS',changed=changed,other115ProfilesUnchanged=True,spellBookUnchanged=True,supplierAssetsUnchanged=True,clips=10,stills=20,maxCommitRatio=max(c['commitRatio'] for r in rows for c in r['clips']),auditChecks=sum(len(a['checks']) for a in audits))
(OUT/'scope.json').write_text(json.dumps(scope,ensure_ascii=False,indent=2),encoding='utf-8')
template=(ROOT/'Tools/SpellVFX120/fixed_wards_review.html').read_text(encoding='utf-8')
(OUT.parent/'FIXED_WARDS_REVIEW.html').write_text(template.replace('__REPORTS__',json.dumps(rows,ensure_ascii=False)),encoding='utf-8')
text=f'''# 구·무·수·우·누 — 지점 고정형 결계 VFX

2026-09-10. 사용자 승인 계획에 따라 VFX와 진단 접촉을 제작했다. **실제 작도 실행·투사체 차단·피해 경감·패링 미연결**이다. SpellBook은 그대로15종이다.

## 변경

공통 반경3m·높이2.2m·수명4초·형성0.45초·소멸0.5초. 시전 중심·방향·지면을 저장하며 효과 부모와 검수 카메라가 움직여도 설치 지점을 유지한다. 전역 노출·블룸은 변경하지 않았다. 검수 카메라만 물 셰이더용 Depth/Opaque Texture를 요청한다.

- 구: 기존 목 지지대 메시6개와 원주를 따라 연결한 절차 덩굴3단, KTP 녹색 막. 접점의 짧은 휨과 잎·목편, 종료 파편.
- 무: 기존 돌 메시 표면을 재사용한8개 토벽. 하단은 지형에 맞춘 연결면과32cm 두께로 보강했다. 순차 형성, 국부 균열·돌가루, 종료 시 침식·하강.
- 수: 기존 금속 메시로 만든 기둥과 연결 테두리8판, 공급자 Bottom09-01의 Pattern 하위체를 별도 복제한 격자 문양. 접점 섬광·파편. 종료 시 중심선은 유지하면서 금속 선의 두께가 줄고 문양이 옅어진다.
- 우: 기존 AreaWater(PolyOne Water URP 기반) 그래프를 WardWater로 별도 복제했다. EffectTime을 공유하고 결계용 알파를 노출했다. 수막 정점의 형성·잔물결·접점 파문·하강과 하단 입자.
- 누: 얇고 흐르는 붉은 열막, 적은 경계 불씨, 접점에서만 화염·파편. 시험 투사체는 진단 도구에서 접촉 시 제거한다.

KTP Pattern 파티클 하위체를 유지해 바닥·패널·접촉에 사용했다. 별도 카메라 앞 발동은 없다. 접촉 문양은 크기 약0.7m·0.25초, 파편은0.6초 안에 정리한다. 원본의 크기와 운동은 이 표시 창에 맞춘 사본에서 조정했다. 새로운 Meshy 요청은0건이다.

## 연결 구조

`Vfx120Profile`에 기본 비활성 WardKind와 전용 설정·재질·프리팹을 추가했다. `Vfx120Effect`가 해당5종에 한해 `FixedWardVfx`로 분기한다. `ContactAt(id,point,normal)`은 표현 신호이며 전투 이벤트를 만들지 않는다. 형성 전·소멸 중·종료 후·중복ID·면에서 벗어난 신호는 거절한다.

지면은 초기 Raycast로 확인하고 프레임마다 재조회하지 않는다. 기둥은 수직을 유지하고 막 하단은 각 열의 지면 높이를 사용한다. 지면이 누락된 패널은 표시하지 않는다. 내부 카메라에는 구조물의 불투명도를 외부의18% 수준으로 낮추고 경계에서 부드럽게 전환한다. 월드 깊이 판정을 유지한다. 물 알파는 카메라별 렌더 콜백으로 적용한다.

## 검사 결과

- **통과:** 총{scope['auditChecks']}개 검사.5종 각각30/60/120fps·0.2배 시간 샘플링, 경사 지면·고정 중심·내외부 투명도 설정·접촉 중복 방지·서로 다른 면의 연속 접촉·종료 후 거절·잔존 없음·게임플레이 콜라이더 없음.
- **통과:** 시험 경사면의 막 외곽 하단 정점과 콜라이더 대조.1.2cm 표시 오프셋을 제외한 최대 오차 {max(a['groundError'] for a in audits)*1000:.3f}mm. 전체 지형이나 두께 안쪽 면의 전수 검사를 뜻하지 않는다.
- **통과:** C2 내부/외부10클립 모두 진단 접촉3회, 종료 후 입자·접촉·패널 정리.1080p24fps각6초 전체 디코딩. 내부 카메라 경계 통과 포함.
- **통과:** 다른115프로필·SpellBook·해시 목록의 공급자 원본 보존. 기존5프로필은 Unity FixedWards/Baseline_*.asset에 보존. 촬영 시작 최대 커밋{scope['maxCommitRatio']:.1%};85% 이상 새 촬영/계속 촬영 중단.
- **미검증:** 실제 입력·적 AI·실제 광역 방어, 전체 게임 CPU/GPU 성능, 모든 지형·다층 건물·움직이는 지면. 바닥 큰 문양은 중심 지지면의 평면 표현이며 심한 기복 전체에 투영하는 지형 데칼은 아니다. 비주얼은 사용자 검토 대상이다.
- **비주얼 판단:** 사용자 검토 대기. 이번 결과를 실제 방어 규칙의 완료로 보고하지 않는다.

## 결과 파일

`FIXED_WARDS_REVIEW.html`, `FixedWards/[글자]/play.mp4`, `external.mp4`,20정지 이미지와 원본 프레임,각 settings.json·audit.json·report.json,scope.json.

각 클립은 당시 런타임 어셈블리MVID를 기록한다. 구·무는 연결부 수정 뒤 재촬영했고, 수·누는 테두리 소멸·재 색상 보완 뒤 재촬영했다. 이 페이지의 우 영상은 최초 시안이며 내부 시야 개선판은 별도 비교 페이지에 있다. 지면 누락 방어 처리는 수치 검사로 확인했다.

우 내부 시야 후속 수정과 메모리 제한으로 중단된 재촬영 기록은 `FixedWards/WaterVisibility/REPORT.md` 및 `WATER_VISIBILITY_REVIEW.html`을 참조한다.

'''
(OUT/'REPORT.md').write_text(text,encoding='utf-8')
status=ROOT/'Docs/PROJECT_STATUS.md';entry='**2026-09-10 · 광역 결계5종:** 구·무·수·우·누의 지점 고정형 VFX와 진단 접촉 제작. 실제 방어 미연결. C2 영상10개·수치검사425개, 사용자 비주얼 검토 대기. [검토](http://127.0.0.1:8771/FIXED_WARDS_REVIEW.html) · [보고서](../Art/SpellVFX120/FixedWards/REPORT.md).\n\n';current=status.read_text(encoding='utf-8')
if not current.startswith('**2026-09-10 · 광역 결계5종:'):status.write_text(entry+current,encoding='utf-8')
print(json.dumps(scope,ensure_ascii=False),flush=True)
