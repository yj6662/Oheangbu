from pathlib import Path
import json, shutil, subprocess

root=Path(__file__).resolve().parents[2]
out=root/'Art/SpellVFX120'
ward=out/'FixedWards'
source=ward/'우'
dest=ward/'WaterVisibility'
dest.mkdir(exist_ok=True)
shutil.copyfile(source/'report.json',dest/'capture_attempt2.json')
shutil.copyfile(source/'audit.json',dest/'audit.json')
shutil.copyfile(source/'play/0020.jpg',dest/'after.jpg')
ff=next((Path.home()/'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries').glob('*.exe'))
# Only frames 0..131 belong to this revision; later source frames are from the old review.
subprocess.run([str(ff),'-y','-v','error','-threads','2','-framerate','24','-i',str(source/'play/%04d.jpg'),'-frames:v','132','-c:v','libx264','-threads','2','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',str(dest/'play.mp4')],check=True)
subprocess.run([str(ff),'-v','error','-threads','2','-i',str(dest/'play.mp4'),'-f','null','-'],check=True)
note='''# 우 내부 시야 수정 · 2026-09-10

수막의 Preserve Specular 설정 때문에 알파가 낮아도 강한 반사가 남았다. WardWater 사본에서 이를 해제해 반사에도 투명도가 적용되도록 수정했다. 내부 물 알파0.0684→0.019, 굴절0.015→0.0015. 패널 문양 RGB와 알파 각각30%로 감쇠한다. 외부 알파0.38은 유지하지만 반사광도 알파를 따르므로 외부 흰 번짐도 줄어든다. 바닥 문양·접촉 반응·다른 속성의 설정은 변경하지 않았다.

- 통과: 우 수치검사85개. 내부/외부 감쇠, 고정 위치, 접촉·종료 정리, 시간·지면 검사.
- 확인: 같은 C2 내부 카메라의 수정 전후 정지 이미지. 수정판1080p24fps132프레임(5.5초) 전체 디코딩. 형성·세 진단 접촉·경계 통과·소멸 구간 포함.
- 중단: 첫 촬영132프레임, 두 번째3프레임에서 시스템 커밋85% 도달. 두 번째0..2프레임과 첫 촬영3..131프레임은 동일 코드·설정·시각·결정적 시뮬레이션이다. 기존132..143프레임은 새 영상에 포함하지 않았다. 촬영 자원 정리 완료.
- 미검증: 수정 후 외부 시점 재촬영,144프레임 끝의 캡처 자동 종료 검사,실제 입력·적 AI·방어 규칙. 실제 방어 미연결 유지.

이전 전체5종 영상은 기존 검토 페이지에 보존한다. WaterVisibilityBefore에 수정 전 우 영상·보고서,WaterVisibility에 수정 후 내부 영상·이미지·audit.json·중단 기록을 제공한다. 최종 투명도는 사용자 검토 대상이다.
'''
(dest/'REPORT.md').write_text(note,encoding='utf-8')
html='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>우 · 내부 시야 개선</title>
<style>body{max-width:1700px;margin:auto;padding:25px;background:#171b19;color:#eee;font:17px/1.6 system-ui}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}video,img{width:100%}button{padding:12px;margin:8px;background:#345044;color:white;border:0}a{color:#acdcd8}@media(max-width:800px){.pair{grid-template-columns:1fr}}</style>
<h1>우 · 내부 시야 개선</h1><p>수막의 강한 반사와 굴절을 줄이고, 내부 패널 문양을 옅게 조정했습니다.</p>
<p>VFX 진단 영상 · 실제 방어 미연결. 수정판은5.5초입니다. 메모리85% 제한으로 외부 재촬영은 중단했습니다.</p>
<div class="pair"><section><h2>수정 전 · 내부</h2><video controls preload="metadata" src="FixedWards/WaterVisibilityBefore/play.mp4"></video></section><section><h2>수정 후 · 내부</h2><video controls preload="metadata" src="FixedWards/WaterVisibility/play.mp4"></video></section></div>
<button onclick="document.querySelectorAll('video').forEach(v=>{v.currentTime=0;v.play()})">함께 재생</button><button onclick="document.querySelectorAll('video').forEach(v=>{v.pause();v.currentTime=20/24})">동일 순간 비교</button>
<div class="pair"><img alt="수정 전" src="FixedWards/WaterVisibilityBefore/play_before.jpg"><img alt="수정 후" src="FixedWards/WaterVisibility/after.jpg"></div>
<p>우 수치검사85개 통과. 외부 시점 재촬영·실제 방어는 미검증. <a href="FixedWards/WaterVisibility/REPORT.md">변경과 검증 기록</a> · <a href="FIXED_WARDS_REVIEW.html#우">이전5종 검토</a></p></html>'''
(out/'WATER_VISIBILITY_REVIEW.html').write_text(html,encoding='utf-8')
for path in [out/'FIXED_WARDS_REVIEW.html',root/'Tools/SpellVFX120/fixed_wards_review.html']:
 s=path.read_text(encoding='utf-8')
 if 'WATER_VISIBILITY_REVIEW.html' not in s:
  s=s.replace('<nav id="glyphs">','<p><a href="WATER_VISIBILITY_REVIEW.html">우 내부 시야 개선판 · 수정 전후 비교</a></p><nav id="glyphs">')
 path.write_text(s,encoding='utf-8')
print('Water visibility comparison published; complete-capture claims intentionally excluded.')
