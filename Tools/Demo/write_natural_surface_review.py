"""Assemble the compact ground/vegetation review from actual local receipts."""
from pathlib import Path
import hashlib
import html
import json
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Compact/NaturalSurface'
SCENE = ROOT / 'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity'

def blocks(path, types):
    text = path.read_text(encoding='utf-8-sig')
    return {(int(kind), ident): body for kind, ident, body in re.findall(
        r'^--- !u!(\d+) &(\S+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)
        if int(kind) in types}

before = blocks(OUT / 'before_open.unity', {4, 64, 65, 136, 146})
after = blocks(SCENE, {4, 64, 65, 136, 146})
changes = [str(k) for k in before.keys() | after.keys() if before.get(k) != after.get(k)]
manifest = json.loads((OUT.parent / 'Recovery/surface_manifest.json').read_text(encoding='utf-8-sig'))
mesh_changes = [m['asset'] for m in manifest['meshes'] if
    hashlib.sha256((ROOT / 'Oheangbu' / m['asset']).read_bytes()).hexdigest() != m['hash']]
preservation = {'scope': 'Serialized Transform and collider components versus this task open-scene backup; saved surface mesh SHA256 versus recovery export.',
                'componentCount': len(before), 'changedComponents': changes,
                'meshCount': len(manifest['meshes']), 'changedMeshes': mesh_changes,
                'pass': not changes and not mesh_changes}
(OUT / 'preservation.json').write_text(json.dumps(preservation, ensure_ascii=False, indent=2), encoding='utf-8')

reports = ROOT / 'Art/PlaytestRecovery/Performance'
measurements = {}
for mode in ('off', 'stream'):
    path = sorted(reports.glob(f'20260915*_{mode}_1080/measurement.json'))[-1]
    shutil.copy2(path, OUT / f'performance_{mode}.json')
    measurements[mode] = json.loads(path.read_text(encoding='utf-8-sig'))
final = measurements['stream']
names = {'cold': '初回表示', 'stationary': '정지', 'rotation': '연속 회전',
         'first_visit': '첫 이동', 'return_visit': '되돌아오기', 'returned_stationary': '복귀 후 정지'}
names['cold'] = '첫 표시'
rows, markdown = [], []
for phase in final['phases']:
    name = names[phase['phase']]
    off = next(p for p in measurements['off']['phases'] if p['phase'] == phase['phase'])
    values = [name, f"{off['frameP50']:.2f}", f"{phase['frameP50']:.2f}",
              f"{phase['frameP95']:.2f}", f"{phase['cpuP50']:.2f}", f"{phase['gpuP50']:.2f}"]
    rows.append('<tr>' + ''.join(f'<td>{v}</td>' for v in values) + '</tr>')
    markdown.append('| ' + ' | '.join(values) + ' |')

report = f'''# 축소 맵 — 흙길·풀 지면·바람 검토

## 산 모양에 대한 답
첨부 화면의 바늘 같은 봉우리는 자연스러운 산세라는 목표에 맞는 최종 표현이 아니다. 이전 압축 진단에서 수평 간격이 줄어드는 동안 높이가 유지되어 급경사가 커지는 문제가 기록되어 있다. 현재 저장 메시의 Tree 랜드마크 주변 600m를 다시 분석해도 표면 면적의 15.10%가 60도 초과, 7.91%가 75도 초과다. 이 표본은 첨부 카메라와 정확히 일치한다고 단정하지 않는다. 산세 자체는 이번 재질 작업에서 변경하지 않았다. 산을 완화하려면 도로·수면·시설 접지와 함께 고쳐야 한다.

## 적용
- 축소 씬 전용 지면 재질 3개, 렌더러 97개: 기존 보유 세연정 흙·풀·암석 색상/노멀 6장을 파생 복사했다. 원본 공급자 파일은 유지한다.
- 길과 마당은 흙, 나머지 완만한 지면은 풀 바탕. 급경사는 암석으로 혼합한다. 월드 좌표 UV, 근거리 노멀, 두 주파수와 넓은 명도 변화를 적용했다. 실제 지형을 울퉁불퉁하게 변형한 것은 아니다.
- 2K 밉맵·비등방성 필터링 4, 풀/흙/암석 반복 주기 약 7.14/6.25/5.56m. 전역 노출·후처리·그림자 설정은 그대로다.
- GPU 식생 인스턴싱·기존 LOD/카드·1ms 생성 예산·0.75ms 패킷 예산을 사용한다. 낮은 풀만 간격 .9m, 밀도 1.1, 크기 .85~1.10으로 보강했다. 길·건축물·물가·급사면 제외 조건은 유지한다.
- 식생 파생 재질 178개, 고정 나무 렌더러 894개에 사용한 파생 재질 12개에 GPU 바람을 적용했다. 뿌리는 고정하고 끝으로 갈수록 크게, 잎에는 작은 빠른 움직임을 더한다. Play 시간에 따라 정지하며 추가 개체별 Update나 풀 충돌체는 없다.
- 이미 조명이 포함된 원거리 카드에 주변광을 다시 곱해 어두워지는 것을 줄였다. 일부 카드의 검은 윤곽과 가까운 메시와의 질감 차이는 남아 있어 최종 자연스러움 승인은 보류한다.
- **실제 누락 수정:** 기존 축소 배치 에셋의 `Cells: []`를 확인했다. 생성 완료 후 dirty 표시가 빠져 데이터가 저장되지 않던 두 종료 경로를 고쳤다. 파생본에 370셀을 새로 저장했고 재컴파일·Play 왕복 후에도 유지됨을 확인했다. 기존 빈 에셋과 원본 씬은 보존했다.

## 검증
| 항목 | 상태 | 근거/제한 |
|---|---|---|
| 파생 셰이더 컴파일 | 통과 | 두 셰이더 오류 0. 식생의 기존 SurfaceVertex 경고 1개는 남음 |
| 지형/충돌체/시설 Transform 보존 | {'통과' if preservation['pass'] else '실패'} | 컴포넌트 {len(before)}개·메시 {len(manifest['meshes'])}개 비교. preservation.json |
| 370셀 디스크 보존 | 통과 | 재컴파일 및 Play 왕복 후 audit |
| 1080p 실제 씬 이미지 | 통과 | 검정 이미지 아님. 메인 전후 카메라 동일. 근접 이미지는 별도 구도이며 직접 전후 비교 아님 |
| 바람 연결 | 통과/외형 검토 필요 | GPU 셰이더와 공통 깊이·그림자 변형 경로, Play 시간 연결. 풍속·잎 모양 최종 승인 및 전 지역 육안 검사는 미검증 |
| 120fps 목표 | 실패 | 아래 실제 Editor Play 측정. CPU 병목, 목표 8.33ms 미달 |
| 산 형태 복구 | 미완료 | 진단만 수행. 도로 597개 기존 실패 표본도 이번 표면 작업으로 해결되지 않음 |
| 전 지역 직접 보행·장시간 성능 | 미검증 | 자동 전체 완주·영상·새 빌드 미제작 |

## 성능
{final['gpuDevice']} / {final['cpuDevice']} / Unity {final['unityVersion']}. 실제 GameView 원래 해상도 {final['nativeCameraWidth']}×{final['nativeCameraHeight']}; 비교용 렌더 타깃 1920×1080. 현재 Editor 품질 0(Mobile), VSync {final['vSync']}. PC 품질 프리셋이나 빌드 성능으로 일반화하지 않는다.

49초 실제 Play 렌더 프레임에서 정지·회전·224m 카메라 이동·복귀를 측정했다. 플레이어 자동 보행은 아니다. 시작 전 식생 캐시는 비웠고 미리 생성하지 않았다. 최종 {len(final['samples'])}프레임. 식생 끔은 절차 식생 렌더러만 끈 상태이며 고정 나무·지면·건축물은 남는다.

| 구간 | 식생 끔 프레임 중앙값 ms | 최종 켬 중앙값 ms | 최종 켬 p95 ms | CPU 중앙값 ms | GPU 중앙값 ms |
|---|---:|---:|---:|---:|---:|
{chr(10).join(markdown)}

시스템 커밋은 최종 약 {final['commit']*100:.1f}%. 프레임 비용은 전체 씬 및 Editor를 포함한다. 로딩 중 식생 셀 수가 증가하므로 49초 검사는 모든 원거리 셀 생성 후의 장시간 최악 비용을 보증하지 않는다. 정지 이미지 준비 시의 선행 생성 비용을 실제 이동 성능으로 사용하지 않았다. 더 많은 원거리 셀이 표시된 스크린샷의 제출 통계는 별도 `*_submissions.json`이다.

## 파일
실제 씬: `Assets/_Project/Scenes/World/W_Demo_Compact.unity`.
파생 에셋: `Assets/_Project/Art/World/WorldCompact/NaturalSurface/`.
셰이더: `Assets/_Project/Shaders/CompactNaturalGround.shader`, `CompactNaturalVegetation.shader`.
원본/열린 씬 백업: `before_disk.unity`, `before_open.unity`.
검토 페이지: `REVIEW.html`. 빌드 없음.
'''
(OUT / 'REPORT.md').write_text(report, encoding='utf-8')
page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>흙길과 풀 지면 — 축소 맵 검토</title><style>
body{margin:0;background:#181e19;color:#e7e4d7;font:16px/1.7 system-ui,sans-serif}main{max-width:1400px;margin:auto;padding:36px}h1{font-weight:600}p{max-width:1000px}.note{background:#323426;padding:20px;border-left:4px solid #b9a76d}img{width:100%;display:block;aspect-ratio:16/9;object-fit:contain;background:#101612}figure{margin:22px 0}figcaption{margin-top:8px;color:#c7c8b9}a{color:#c4ddb3}button{padding:10px 22px;margin-right:10px;background:#d1d6ba;border:0;cursor:pointer;font:inherit}.grid{display:grid;grid-template-columns:1fr 1fr;gap:20px}table{border-collapse:collapse;width:100%;font-variant-numeric:tabular-nums}td,th{padding:10px;border-bottom:1px solid #48513f;text-align:left}small{color:#c0c8bb}.scroll{overflow:auto}@media(max-width:800px){.grid{grid-template-columns:1fr}main{padding:16px}}</style>
<main><small>COMPACT / NATURAL SURFACE · 실제 Unity 씬 · 1920×1080 정지 이미지</small>
<h1>흙길, 풀 지면, 바람</h1>
<p class="note">산의 뾰족한 형태는 의도한 최종 자연 지형이 아닙니다. 이번에는 표면과 식생 누락을 수정했습니다. 산세는 그대로이며, <strong>120fps 목표와 최종 자연스러운 외형 승인은 아직 미달·미검증</strong>입니다.</p>
<p>보유 흙·풀·암석 텍스처와 노멀을 재사용했습니다. 길은 흙으로 구분하고, 완만한 지면에는 풀 바탕과 낮은 군락을 겹쳤습니다. 원본 자료·건물 크기·지형·콜라이더는 보존했습니다.</p>
<h2>같은 카메라 전후</h2><button onclick="pick('before')">수정 전</button><button onclick="pick('after')">수정 후</button>
<figure><img id="compare" src="after.png" alt="수정 후 실제 씬"><figcaption id="caption">수정 후 — 지면 재질·복구한 절차 식생·낮은 풀 보강</figcaption></figure>
<p>수정 전에는 배치 에셋의 셀 목록이 실제로 비어 있었습니다. 이번에 저장 누락을 수정해 370셀을 복구했습니다. 정지 이미지용 선행 생성은 성능 시험과 구분했습니다.</p>
<h2>지면과 길 가장자리</h2><figure><img src="close_after.png" alt="흙길과 풀 가장자리"><figcaption>별도 근접 구도. 바탕 텍스처는 빈 땅을 줄이고 입체 풀은 LOD·카드와 GPU 인스턴싱으로 표시합니다. 일부 풀·수목 카드의 거친 윤곽은 후속 보완 대상입니다.</figcaption></figure>
<h2>바람 구현</h2><p>뿌리는 고정하고 줄기·잎 끝을 GPU에서 변형합니다. 근거리 잎의 잔움직임과 넓은 바람을 겹쳤으며, 깊이·그림자 패스에도 같은 변형을 사용합니다. 개체별 Update와 풀 충돌체는 추가하지 않았습니다. 바람의 최종 강도·자연스러움은 플레이 검토가 필요합니다.</p>
<h2>실제 Play 성능</h2><p>1920×1080 진단 카메라, 첫 진입·회전·224m 이동·복귀. 미리 생성하지 않은 49초 측정이며 사용자 직접 보행이나 장시간 최악 비용 검증은 아닙니다. 현재 Editor 품질은 Mobile입니다.</p>
<div class="scroll"><table><thead><tr><th>구간</th><th>식생 끔 중앙값 ms</th><th>최종 켬 중앙값 ms</th><th>켜짐 p95 ms</th><th>CPU 중앙값 ms</th><th>GPU 중앙값 ms</th></tr></thead><tbody>ROWS</tbody></table></div>
<p>CPU 8.33ms 목표 미달. GPU 수치가 낮아도 전체 120fps 달성으로 판정하지 않았습니다. 고정 나무는 ‘식생 끔’ 비교에도 남습니다.</p>
<h2>남아 있는 일</h2><ul><li>압축으로 가팔라진 산세를 도로·시설 접지와 함께 완화.</li><li>일부 식생 카드의 검은 윤곽·LOD 질감 차이와 군락의 최종 외형 보완.</li><li>장시간 이동 및 PC 품질·빌드 성능 확인. 이번에는 새 빌드를 만들지 않았습니다.</li></ul>
<p><a href="REPORT.md">전체 기술 보고서</a> · <a href="performance_stream.json">최종 성능 원자료</a> · <a href="performance_off.json">식생 끔 원자료</a> · <a href="preservation.json">보존 검사</a> · <a href="mountain_diagnosis.json">산세 진단</a> · <a href="dressing_bake.json">370셀 생성 기록</a></p>
</main><script>function pick(v){document.querySelector('#compare').src=v+'.png';document.querySelector('#caption').textContent=v==='before'?'수정 전 — 원본 지면과 빈 절차 식생 셀':'수정 후 — 지면 재질·복구한 절차 식생·낮은 풀 보강';}</script></html>'''
(OUT / 'REVIEW.html').write_text(page.replace('ROWS', ''.join(rows)), encoding='utf-8')
print(json.dumps({'preservation': preservation, 'finalPhases': final['phases'], 'review': str(OUT / 'REVIEW.html')}, ensure_ascii=False))
