"""Package the saved playtest contact repairs and their same-camera stills."""
from pathlib import Path
import hashlib
import html
import json
import shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Playtest/Polish'
VIEWS = {'inn_inside': '주막 내부: 지붕 틈·지형 관통·출입 동선',
         'inn_entry': '주막 입구: 바닥과 문턱 연결',
         'inn_side': '주막 측면: 벽·바닥 겹침 제거',
         'ramp_side': '폐광 경사로: 열린 측면 봉합',
         'mine_inside': '폐광 내부: 중복 암벽 조각과 조사 소품 접지',
         'branch': '지선 소지품: 지면 경사와 접촉'}

baseline = json.loads((OUT.parent / 'baseline.json').read_text(encoding='utf-8-sig'))
preserved = {p: hashlib.sha256((ROOT / p).read_bytes()).hexdigest() == digest
             for p, digest in baseline.items()}
assert all(preserved.values()), [p for p, same in preserved.items() if not same]
(OUT / 'preservation.json').write_text(json.dumps(preserved, indent=2), encoding='utf-8')
validation = (OUT / 'validation.txt').read_text(encoding='utf-8-sig')
assert 'FAIL ' not in validation, validation
shutil.copyfile(OUT.parent / 'static_audit.txt', OUT / 'route_validation.txt')
route = (OUT / 'route_validation.txt').read_text(encoding='utf-8-sig')
assert 'FAIL ' not in route
poses = {}
cards = []
for view, title in VIEWS.items():
    pair = []
    for stage in ('before', 'after'):
        src = OUT.parent.parent / f'Dressing/Polish_{stage}_{view}.json'
        data = json.loads(src.read_text(encoding='utf-8-sig'))
        shutil.copyfile(src, OUT / f'{stage}_{view}.json')
        pair.append(data)
        assert (OUT / f'{stage}_{view}.png').is_file()
    pose_error = max(abs(pair[0][field][axis] - pair[1][field][axis])
                     for field in ('position', 'euler') for axis in 'xyz')
    assert pose_error < .001 and pair[0]['fieldOfView'] == pair[1]['fieldOfView']
    poses[view] = {'poseMaxDifference': pose_error, 'fov': pair[0]['fieldOfView']}
    cards.append(f'<section><h2>{html.escape(title)}</h2><div class="pair">' +
                 ''.join(f'<figure><figcaption>{label}</figcaption><a href="{stage}_{view}.png"><img loading="lazy" src="{stage}_{view}.png" alt="{html.escape(title)} {label}"></a></figure>'
                         for stage, label in [('before', '수정 전'), ('after', '수정 후')]) + '</div></section>')
(OUT / 'camera_comparison.json').write_text(json.dumps(poses, indent=2), encoding='utf-8')
report = '''# 플레이어 시점 접합부 정리 · 2026-09-13

현재 `W_WorldMacro_Playtest`의 폐광·지선·금표 주막에서 확인한 겹침, 열린 메시, 지면 접촉을 수정하고 씬에 저장했다. Computer Use 없이 기존 Unity Editor 파일 명령과 1920×1080 카메라 렌더만 사용했다.

- 주막 바닥을 약 20.7cm 올려 지형 관통을 제거했다. 외곽은 벽 안쪽으로 2cm 넣어 같은 면의 겹침을 없앴고 기존 목재 재질로 맞췄다.
- 지붕 아래 앞뒤 틈에 두께 30cm의 벽을 추가하고, 문턱에 완만한 닫힌 경사로를 연결했다.
- 휴식 오브젝트를 방 안쪽으로 3m 옮겼다. 표시물·상호작용 좌표를 함께 갱신했고 체크포인트와 NPC 접근 공간을 확인했다.
- 폐광 경사로의 측면과 밑면을 닫았다. 기존 윗면 정점·삼각형은 그대로여서 보행면과 적 경로가 유지된다.
- 폐광 외피를 뚫고 검은 삼각형 끝만 드러나던 중복 암벽 LOD 묶음 9개는 이 씬에서만 비활성화했다. 원본 오브젝트는 보존했다.
- 조사물·주막 소품·NPC 발을 바닥에 맞추고, 지선 소지품은 경사에 정렬해 약 5mm 접촉시켰다.

[동일 카메라 전후 이미지 6쌍](REVIEW.html)

검증: 새 메시 4개 모두 열린 모서리·뒤집힌 연결·퇴화 삼각형 0. 주막 바닥 3,040표본에서 지형 관통 0, 지붕 틈 1,612광선에서 누출 0, 출입 동선 171캡슐 표본에서 막힘 0. 기존 전체 경로 16,426표본 실패 0 및 3적 NavMesh/순찰 경로 통과. 체크포인트 복구 후보와 5개 상호작용 접근 위치도 통과했다.

보존: 기존 기준 파일 246개의 SHA-256이 모두 동일하다. 원본 매크로 씬·C2·프롤로그·공유 지형·원본 모델은 보존했다. 수정은 플레이테스트 씬, 전용 Playtest/ContentPositions 데이터 및 새 파생 메시 4개에 한정된다. 씬의 식생 진단 카운터도 캐시 초기화 값으로 재직렬화됐으며 식생 설정 변경은 없다. 콘텐츠 ID 83개 유지, 기존 씬 오브젝트 삭제 0.

범위: 첫 플레이 구간의 정지 시점과 물리 표본을 검사했다. 전체 5강토의 모든 시점, 실제 키·포인터 보행/전투, 런타임 프레임 품질을 전수 검증한 것은 아니다. 캡슐 캐릭터와 건축물은 기존 블록아웃 수준이다.

재검사: `python Tools/Unity/world_macro_playtest.py PolishValidate`, `python Tools/Unity/world_macro_playtest.py Audit`. Unity Editor에서 이 플레이테스트 씬이 열려 있어야 한다. `PolishApply`는 미적용 씬에 대한 최초 적용 명령이며 재적용을 거부한다.

수정 전 백업: `BeforePolish.unity`, `Before_Playtest.asset`, `Before_ContentPositions.asset`. 이후 작업을 덮어쓰지 않도록 복원 시 각 변경을 검토한다. 검사 원문은 `validation.txt`, `route_validation.txt`, `preservation.json`, `camera_comparison.json`에 있다.
'''
(OUT / 'REPORT.md').write_text(report, encoding='utf-8')
page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>플레이어 시점 정리 · 전후 비교</title>
<style>body{margin:0;background:#181c1a;color:#e6e8df;font:16px/1.6 system-ui,sans-serif}main{max-width:1600px;margin:auto;padding:36px 24px}h1{font-size:30px}h2{font-size:20px;margin:36px 0 12px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:16px}figure{margin:0}figcaption{margin-bottom:8px;color:#b7c6b6}img{display:block;width:100%;border-radius:6px}a{color:#a7cdb0}p{max-width:950px}@media(max-width:850px){.pair{grid-template-columns:1fr}}</style>
<main><h1>플레이어 시점 접합부 정리</h1><p>폐광 · 지선 · 금표 주막 / 2026-09-13<br>각 쌍은 같은 위치·방향·시야각의 1920×1080 Unity 렌더입니다. 이미지를 누르면 원본 크기로 볼 수 있습니다.</p><p>Computer Use 미사용. 주막 바닥 관통·지붕 틈, 경사로 측면, 중복 암벽 조각, 소품 접촉을 수정했습니다. 첫 구간의 정지 시점 및 물리 검사 결과이며 전체 월드 보행 검증은 아닙니다. <a href="REPORT.md">변경·검증 기록</a></p>'''
(OUT / 'REVIEW.html').write_text(page + ''.join(cards) + '</main></html>', encoding='utf-8')
print(f'Packaged {len(VIEWS)} matched camera pairs; {len(preserved)} protected files unchanged; all checks PASS.')
