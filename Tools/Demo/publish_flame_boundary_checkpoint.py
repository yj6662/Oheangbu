"""Publish measured cone clipping evidence; preserve the first Nom review as historical evidence."""
from pathlib import Path
import json, shutil

root=Path(__file__).resolve().parents[2]
base=root/'Art/Demo/Summons'; out=base/'FireHaetae/Boundary';out.mkdir(parents=True,exist_ok=True)
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
gpu=read(base/'flame_pixels.json');checks=read(base/'flame_tests.json');run=read(base/'runtime_flame_tests.json')
assert gpu['status']=='PASS_GPU_HORIZONTAL_EXTENT' and not gpu['failed']
assert checks['status']=='PASS' and not checks['failed'] and len(checks['passed'])==35
assert run['status']=='PASS_RUNTIME_API' and not run['failed']
save=read(root/'Art/Demo/Chapter2/runtime_tests.json')
assert save['suffixRestored'] and not save['active'] and not save['holdingPlay']
for name in ('flame_pixels.json','flame_geometry.json','flame_tests.json','runtime_flame_tests.json','haetae_scene_audit.json'):
    shutil.copy2(base/name,out/name)
shutil.copy2(root/'Art/Demo/Chapter2/runtime_tests.json',out/'save_restore.json')
for view in ('external','player'):
    for suffix in ('_1920x1080.png','_capture.json'):
        name='nom_flame_'+view+'_v2'+suffix;shutil.copy2(root/'Art/Demo/Foundation'/name,out/name)
    capture=read(out/('nom_flame_'+view+'_v2_capture.json'));assert capture['commitRatio']<.85
before=max(r['maximumVisibleRange'] for r in gpu['rows'] if not r['constrained'])
after=max(r['maximumVisibleRange'] for r in gpu['rows'] if r['constrained'])
text=f'''# 놈 화염 범위 보정

기존 KTP 화염을 확대한 상태에서 실제 입자 중심은 약5.32m, GPU 진단의 밝은 픽셀은 최대{before:.3f}m까지 확인됐다. 피해 사거리는4.5m이므로 표현과 판정이 어긋났다.

원본 AdditiveBlend_Scroll/AlphaBlend_Scroll 그래프·텍스처·flipbook·색상 경로를 보존한 사본에서 마지막 출력에 월드 부채꼴 마스크를 추가했다. 공격 시작에 고정한 입 위치·방향·사거리·반각·수직 높이를 사용한다. 경계 안쪽15cm에서 옅어지고 바깥 픽셀은 제거한다. 밝기는 기존의70%로 시작한다. 피해·먹·시간 규칙은 바꾸지 않았다.

## 확인한 결과

- GPU: 원점/정북과 이동된 원점/37도 방향, 각6시점의 실제 PS·재질 출력. 보정 전 범위 밖 픽셀 재현, 보정 후 최대{after:.3f}m·범위 밖 픽셀0. 총8검사 통과.512×512 정사영 진단, 래스터 경계 허용4cm·밝기 문턱0.025다. 수평 범위 확인이며 모든 시점·수직 범위·벽 차폐를 대신하지 않는다.
- 재질: 공격별 사본만 사용하고 원본은 보존했다. EditMode 검사에서도 해제되도록 수명주기를 수정했다. 반복 공격/교체/정리 포함35검사 통과.
- 실제 데모: 진단 시전·먹1.00→0.70·양수 피해{run['hits']}회/누적{run['damageApplied']:.0f}·20초 활동 후 소멸 통과. 새로운 플레이/외부1080p 두 장을 확인했다. 사용자 저장·시험 슬롯·GameView 해상도 복원.
- GPU 비용/실제120fps·수동 손글씨 전투·실제 장치 SFX는 미검증. 새 빌드·영상 없음.

화염의 밝은 덩어리감, 해태의 실제 경사 발 접지와 플레이어 겹침은 아직 보완 대상이다. 이번 보정은 최종 미술 승인이 아니다. 기하 진단의 billboard 외곽에는 투명 texel도 포함되므로 실제 밝은 픽셀 범위와 구분한다.

기존 v1 화면과 이번 v2는 같은 카메라 배치의 실제 분사 장면이다. 정확히 같은 입자 시각으로 맞춘 픽셀 비교는 아니며, 범위 수정의 수치 근거는 독립 GPU 진단이다.
'''
(out/'REPORT.md').write_text(text,encoding='utf-8')
html='''<!doctype html><html lang="ko"><meta charset="utf-8"><title>놈 화염 범위 보정</title>
<style>body{background:#181a19;color:#e5e1d8;font:17px/1.65 sans-serif;margin:32px auto;max-width:1400px}a{color:#dbc28f}img{width:100%}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}h1,h2{font-weight:500}.note{background:#282a27;padding:20px}</style>
<h1>놈 · 화염 범위 보정</h1><p>실제 데모 진단 시전 / 수동 전투와 최종 미술 미검증</p>
<p class="note">피해 사거리 4.5m에 맞춰 보이는 화염을 제한했습니다. 원본 KTP는 유지하고 경계 안쪽에서만 부드럽게 사라집니다. GPU 진단: 보정 전 최대 6.114m → 보정 후 4.492m. 밝기는 70%입니다.</p>
<p><a href="REPORT.md">검증 범위와 남은 문제</a> · <a href="flame_pixels.json">GPU 측정</a> · <a href="../REVIEW.html">기존 리그·전투 검토</a></p>
<h2>외부 시점</h2><div class="pair"><figure><figcaption>기존 v1</figcaption><img src="../nom_flame_external_v1_1920x1080.png"></figure><figure><figcaption>보정 v2</figcaption><img src="nom_flame_external_v2_1920x1080.png"></figure></div>
<h2>플레이 시점</h2><img src="nom_flame_player_v2_1920x1080.png"><p>플레이어와 소환수의 겹침, 경사 접지감, 화염 덩어리감은 남은 보완 대상입니다. 비교 화면의 입자 시각은 완전히 동일하지 않습니다.</p></html>'''
(out/'REVIEW.html').write_text(html,encoding='utf-8')
index=base/'FireHaetae/REVIEW.html';body=index.read_text(encoding='utf-8')
if 'Boundary/REVIEW.html' not in body:
    at=body.find('<h1');body=body[:at]+'<p><a href="Boundary/REVIEW.html">최신: 화염 범위 보정과 실제 데모 화면</a></p>'+body[at:]
    index.write_text(body,encoding='utf-8')
print(out/'REVIEW.html')
