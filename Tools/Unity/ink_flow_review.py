"""Build receipt-labelled local reviews. Never runs Unity, captures, or changes Assets."""
from pathlib import Path
import argparse
import base64
import datetime as dt
import html
import json
import math
import os
import re
import statistics
import struct
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/WorldMacro/Compact/InkLandscape/InkPaintingStudy/FlowRevision'
TITLES = {'inn': '주막 뒤 겹산', 'mountain_path': '산길 능선', 'office': '관아', 'DeepForest': '깊은 숲',
          'SouthGate': '남문', 'Jeokro': '적로', 'Cheolong': '철옹', 'Hyeongang': '현강', 'Hwanggyeong': '황경',
          'boundary_Hwanggyeong': '황경 경계', 'boundary_Cheongrim': '청림 경계', 'boundary_Jeokro': '적로 경계',
          'boundary_Cheolong': '철옹 경계', 'boundary_Hyeongang': '현강 경계', 'ground_detail': '가까운 지면'}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path and path.exists() else None


def resolve(value):
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def link(path):
    return quote(os.path.relpath(path, OUT).replace('\\', '/'), safe='/._-')


def latest(pattern):
    paths = list(OUT.glob(pattern))
    return max(paths, key=lambda p: p.stat().st_mtime_ns) if paths else None


def receipt(path):
    data = read(path)
    return {'path': link(path), 'data': data} if data is not None else None


def bools(data, *fields):
    return bool(data) and all(data.get(k) is True for k in fields)


def hex_hash(value):
    if not value:
        return None
    if re.fullmatch(r'[0-9a-fA-F]{64}', value):
        return value.lower()
    try:
        decoded = base64.b64decode(value, validate=True)
        return decoded.hex() if len(decoded) == 32 else None
    except (ValueError, TypeError):
        return None


def png_dimensions(path):
    with path.open('rb') as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n' or header[12:16] != b'IHDR':
        raise ValueError(f'Invalid PNG header: {path}')
    return struct.unpack('>II', header[16:24])


def collect_receipts():
    pointer = OUT / 'current_geometry_receipt.txt'
    geometry = resolve(pointer.read_text(encoding='utf-8-sig').strip()) if pointer.exists() else latest('GeometryApplication/**/application.json')
    bound_form_maps = [p for p in OUT.glob('FormMaps/*/form_map_receipt.json') if (read(p) or {}).get('bound') is True]
    active_form_map = max(bound_form_maps, key=lambda p: p.stat().st_mtime_ns) if bound_form_maps else None
    result = {'look': receipt(OUT / 'PersistentApplication/application.json'), 'geometry': receipt(geometry),
              'navigation': receipt(OUT / 'NavigationUpdate/progress.json'),
              'formMap': receipt(active_form_map),
              'finalGeography': receipt(latest('FinalGeographyFields/*/geography_field_application.json')),
              'prepared': receipt(OUT / 'mountain_form_prepared.json'),
              'physics': receipt(OUT / 'mountain_form_physics.json'), 'normals': receipt(OUT / 'mountain_form_normals.json')}
    geo = (result['geometry'] or {}).get('data', {})
    # Temporary sculpt trials update the convenience preparation file. The
    # installed scene must instead report the preparation bound to its export.
    if geo.get('exportReceiptPath'):
        export_path = resolve(geo['exportReceiptPath'])
        export = read(export_path)
        if export and export.get('preparationReceiptJson'):
            result['prepared'] = {'path': link(export_path), 'data': json.loads(export['preparationReceiptJson'])}
    for key, field in [('physics', 'physicsReceiptPath'), ('normals', 'normalReceiptPath')]:
        if geo.get(field):
            selected = receipt(resolve(geo[field]))
            if selected:
                result[key] = selected
    nav = (result['navigation'] or {}).get('data', {})
    if nav.get('generation'):
        result['navigationAudit'] = receipt(OUT / 'NavigationUpdate' / nav['generation'] / 'path_audit.json')
    return result


def stage_for_capture(label, check, receipts, kind):
    # A preview keeps the saved scene hash even when temporary geometry is visible.
    if kind == 'preview':
        return '임시 시험안 · 촬영 시작 상태로 복원'
    digest = hex_hash((check or {}).get('sourceSceneHash'))
    for key, name, flags in [('navigation', '지형·내비게이션을 저장한 씬', ('installed', 'sceneSaved')),
                             ('geometry', '산형을 영구 적용한 저장 씬', ('applied', 'sceneSaved')),
                             ('look', '외형만 영구 적용한 저장 씬', ('sceneSaved',))]:
        data = (receipts.get(key) or {}).get('data', {})
        if digest and bools(data, *flags) and digest == hex_hash(data.get('sceneAfterSha256')):
            return name + ' · 씬 해시 일치'
    if kind == 'installed' or label.startswith('installed_'):
        return '저장 상태 캡처로 지정됨 · 적용 단계의 씬 해시 일치 미확인'
    return '과거 비교 자료 · 현재 적용 단계로 간주하지 않음'


def capture(label, view, receipts, kind, folder=OUT):
    stem = f'{label}_{view}'
    path, check_path, submit_path = folder / f'{stem}.png', folder / f'{stem}_checks.json', folder / f'{stem}_submissions.json'
    check, submissions = read(check_path), read(submit_path)
    size = png_dimensions(path) if path.exists() else None
    shaders = (check or {}).get('shaderChecks', [])
    valid = bool(path.exists() and size == (1920, 1080) and check and check.get('status') == 'PASS' and check.get('view') == view
                 and bools(check, 'bindingsRestored', 'sceneFileUnchanged', 'sourceMaterialsUnchanged')
                 and check.get('sceneDirtyBefore') is False and check.get('sceneDirtyAfter') is False
                 and shaders and all(v.startswith('PASS ') for v in shaders))
    version_path = folder / f'{stem}_version.json'
    version = read(version_path)
    visual = read(folder / f'{stem}_visual_review.json')
    if visual and visual.get('accepted') is False:
        valid = False
    return {'label': label, 'view': view, 'image': link(path) if path.exists() else None, 'size': list(size) if size else None,
            'version': link(version_path) if version else None,
            'versionStatus': (version or {}).get('status', '과거 촬영 · 개별 버전 원장 없음'),
            'validatedCapture': valid, 'status': '촬영 검사 통과' if valid else ('이미지 있음 · 검사 미완료/실패' if path.exists() else '미촬영'),
            'stage': stage_for_capture(label, check, receipts, kind), 'check': link(check_path) if check else None,
            'submissions': link(submit_path) if submissions else None, 'sourceSceneHash': (check or {}).get('sourceSceneHash'),
            'rendererBindingsInReceipt': (check or {}).get('rendererBindings'),
            'submissionSummary': {k: submissions.get(k) for k in ['drawCalls', 'submittedInstances', 'visibleTriangles', 'shadowEligibleTriangles']} if submissions else None,
            'materialVersionNote': ('재질·셰이더·씬의 촬영 전후 해시를 개별 버전 원장에 기록.' if version else '과거 촬영에는 개별 재질/FormMap 버전 지문이 없음. 적용 영수증은 별도로 표시.')
                + (f" 품질 {check['qualityName']} · 파이프라인 renderScale {check['pipelineRenderScale']:.2f} · 출력 {check.get('outputWidth', 1920)}×{check.get('outputHeight', 1080)}. 출력 해상도와 내부 렌더 해상도를 구분." if check and check.get('qualityName') and check.get('pipelineRenderScale') else ' 당시 활성 파이프라인의 renderScale은 직접 기록하지 않았음.')}


def archive_capture(folder, label, view, archive_views):
    result = capture(label, view['id'], {}, 'unknown', folder)
    previous = archive_views.get(view['id'])
    matched = bool(previous) and all(abs(previous[k][axis] - view[k][axis]) <= .001 for k in ['eye', 'target'] for axis in ['x', 'y', 'z'])
    result.update({'label': f'{folder.name}/{label}', 'validatedCapture': False,
                   'status': '과거 참고 이미지 · 현재 촬영 검사 대상 아님' if result['image'] else '과거 이미지 없음',
                   'stage': '이전 ' + folder.name + ' 세대 · 현재 저장 외형 기준선이 아님', 'sameViewDefinition': matched,
                   'materialVersionNote': ('구도 정의 일치. ' if matched else '구도 정의 일치 미확인. ') + '재질·형상 버전이 다른 과거 참고 자료.'})
    if not result['check'] and (folder / 'checks.json').exists():
        result['check'] = link(folder / 'checks.json')
    return result


def number(value, precision=3):
    return f'{value:,.{precision}f}' if isinstance(value, (int, float)) and math.isfinite(value) else '미측정'


def status_rows(receipts):
    d = lambda key: (receipts.get(key) or {}).get('data', {})
    look, geo, nav, form, physics, normals, art = [d(k) for k in ['look', 'geometry', 'navigation', 'prepared', 'physics', 'normals', 'formMap']]
    nav_audit = d('navigationAudit')
    rows = [
        ('외형 영구 적용', f"{look.get('status', '영수증 없음')} · 씬 저장 {look.get('sceneSaved', '미확인')} · 원본 유지 {look.get('sourcesUnchanged', '미확인')}"),
        ('산형 영구 적용', f"{geo.get('status', '적용 영수증 없음')} · 적용 {geo.get('applied', '미확인')} · 씬 저장 {geo.get('sceneSaved', '미확인')}"),
        ('내비게이션', f"{nav.get('status', '미측정/미베이크')} · 새 데이터 {nav.get('next', '미기록')}/20 · 저장 적용 {nav.get('installed', '미기록')} · 경로 검사 {nav_audit.get('status', '없음')} · 신규 실패 {nav_audit.get('newFailures', '미측정')} / 기존 실패 {nav_audit.get('preexistingFailures', '미측정')} / 적용 후 실패 {nav_audit.get('afterFailures', '미측정')}. Edit 경로 질의이며 실제 보행·차량 통과 완료와 구분."),
        ('형태별 먹·여백', f"{art.get('status', 'FormMap 바인딩 영수증 없음')} · 바인딩 {art.get('bound', False)} · alpha 픽셀 {art.get('alphaPixels', '미측정')} · 재질 나머지 값 유지 {art.get('otherMaterialPropertiesUnchanged', '미확인')}"),
        ('형상 준비 원장', f"변경 메시 {form.get('changedMeshes', '미측정')}개 · 최대 높이 변화 {number(form.get('maxDelta'))}m · 보호면 동일 {form.get('protectedFacesExact', '미확인')}. 준비 원장은 개별 이미지 버전 증명이 아님."),
        ('식생 지지면', f"내부 표본 {form.get('supportSamples', '미측정')}개 · 누락 {form.get('supportMissing', '미측정')}개 · 최대 차이 {number(form.get('maxFoliageSupportError'), 6)}m · 원시 경계 누락 {form.get('rawBoundaryReferenceMissing', '미측정')}개 별도 보존."),
        ('도로 물리', f"{physics.get('status', '미측정')} · 신규 구멍 {physics.get('newRoadHoles', '미측정')} / 높이 변경 {physics.get('changedRoadHeights', '미측정')} / 신규 경사 {physics.get('newGradeFindings', '미측정')}. 기존 구멍 {physics.get('preexistingRoadHoles', '미측정')} / 기존 경사 {physics.get('preexistingGradeFindings', '미측정')}는 별도 기록."),
        ('변경 산형 충돌', f"실제 Raycast {physics.get('terrainSamples', '미측정')}개 · CPU 최종 높이와 최대 차이 {number(physics.get('maximumCpuFinalError'), 6)}m. 실제 플레이 이동 검증과 구분."),
        ('도로 경계 법선', f"{normals.get('status', '미측정')} · 보존 공유 모서리 {normals.get('preservedEdgeAmbiguities', '미측정')} · 변경 형상 {normals.get('changedGeometry', '미측정')} · 미해결 {normals.get('unresolved', '미측정')}"),
        ('미술·플레이 판단', '영수증 통과는 수묵 미술 완성이 아님. 긴 직선 산벽과 균일한 먹면은 남은 미술 문제이며, 산별 먹 덩어리와 나무의 잔대비도 실제 이미지로 판단. 실제 보행·차량 및 최종 적용 버전의 성능은 해당 실측 없이는 미검증. 영상·새 빌드 없음.')]
    proof = physics.get('exportAuditProof')
    if proof:
        rows.insert(-1, ('내보낸 형상과 감사 연결', f"{proof.get('status', '상태 없음')} · 동일 메시 {proof.get('allMeshesMatched', '미확인')} / 설정 {proof.get('settingsMatched', '미확인')} / 접지 필드 {proof.get('fieldsMatched', '미확인')} · generation {proof.get('generation', '')}"))
    return rows


def performance(path, current=False):
    data = read(path)
    if data is None:
        return None
    phases = list(dict.fromkeys(p.get('phase') for m in data.get('measurements', []) for p in m.get('phases', [])))
    results = []
    for phase in phases:
        result = {'phase': phase}
        for side in ['before', 'after']:
            rows = [p for m in data.get('measurements', []) if m.get('side') == side for p in m.get('phases', []) if p.get('phase') == phase]
            for metric in ['cpuP50', 'cpuP95', 'cpuP99', 'gpuP50', 'gpuP95', 'gpuP99', 'frameP50', 'frameP95', 'frameP99']:
                values = [r[metric] for r in rows if isinstance(r.get(metric), (int, float)) and math.isfinite(r[metric]) and r[metric] > 0]
                result[f'{side}_{metric}'] = statistics.median(values) if values else None
        for metric in ['gpuP50', 'gpuP95', 'gpuP99']:
            values = [r.get(f'{metric}DeltaMs') for pair in data.get('pairs', []) if pair.get('comparisonContext') == 'MATCH'
                      for r in pair.get('phaseDeltas', []) if r.get('phase') == phase]
            values = [v for v in values if isinstance(v, (int, float)) and math.isfinite(v)]
            result[f'paired_{metric}DeltaMs'] = statistics.median(values) if values else None
        result['cpu8_33Met'] = result['after_cpuP50'] <= 8.33 if result['after_cpuP50'] is not None else None
        gpu = result['paired_gpuP50DeltaMs']
        result['gpuDeltaBelow0_5'] = gpu < .5 if gpu is not None else None
        results.append(result)
    material_only = str(data.get('scope', '')).startswith('MATERIAL_COST_ONLY:') or str(data.get('status', '')).endswith('_MATERIAL_COST')
    note = ('명시적으로 지정한 저장 재질 비용 짝비교. 같은 저장 산형·식생 배치·하늘에서 원장 원본 205개 재질과 현재 저장 파생 205개 재질을 비교하며, 지형과 외형 전체 변경의 성능 개선량은 아님.' if material_only else
            '명시적으로 지정한 적용 후 성능 원장. 실제 재질/FormMap 버전 일치는 원장 범위로 확인해야 함.') if current else '이전 셰이더 시험의 진단 측정. 이번 영구 산형·FormMap의 최종 성능으로 전용하지 않음.'
    return {'path': link(path), 'status': data.get('status'), 'utc': data.get('utc'), 'currentExplicitlySelected': current,
            'scope': data.get('scope'), 'sceneHash': data.get('sceneSha256After'), 'rows': results,
            'materialCostOnly': material_only, 'requestedPairs': data.get('requestedPairs'),
            'recordedPairs': len(data.get('pairs', [])),
            'matchedPairs': sum(p.get('comparisonContext') == 'MATCH' for p in data.get('pairs', [])),
            'measurements': data.get('measurements', []), 'pairs': data.get('pairs', []),
            'restored': data.get('restored'), 'sceneFileUnchanged': data.get('sceneFileUnchanged'),
            'navigationReceiptUnchanged': data.get('navigationReceiptUnchanged'),
            'navigationIdentity': data.get('navigationIdentity'), 'limitations': data.get('limitations', []),
            'cleanupErrors': data.get('cleanupErrors', []), 'error': data.get('error'), 'note': note}


def recorded(value):
    if value is None:
        return '미기록'
    if isinstance(value, bool):
        return '확인' if value else '아니오'
    return str(value)


def performance_metadata(perf):
    """Report receipt fields literally; selecting a file is not a completed measurement."""
    return [
        ('측정 상태', recorded(perf.get('status'))),
        ('측정 범위', recorded(perf.get('scope'))),
        ('반복', f"요청 {recorded(perf.get('requestedPairs'))}짝 · 기록 {perf['recordedPairs']}짝 · 조건 일치 {perf['matchedPairs']}짝"),
        ('식생 상주 비교', ' · '.join(
            f"짝 {pair.get('pair', index + 1)}: 최종 수량 동일 {recorded(pair.get('lastResidentPopulationEqual'))}, 양쪽 대기 작업 0 {recorded(pair.get('bothLastPendingChunksZero'))}"
            for index, pair in enumerate(perf.get('pairs', []))) or '미기록'),
        ('복원·저장 상태', f"재질 복원 {recorded(perf.get('restored'))} · 씬 파일 동일 {recorded(perf.get('sceneFileUnchanged'))} · 내비게이션 원장 동일 {recorded(perf.get('navigationReceiptUnchanged'))}"),
        ('측정 UTC', recorded(perf.get('utc')))]


def performance_html(perf):
    if not perf:
        return '<p>성능 원장 없음 · 미측정.</p>'
    rows = []
    for r in perf['rows']:
        cells = [r['phase']]
        for metric in ['cpuP50', 'cpuP95', 'cpuP99', 'gpuP50', 'gpuP95', 'gpuP99', 'frameP50', 'frameP95', 'frameP99']:
            cells.append(number(r[f'before_{metric}']) + ' → ' + number(r[f'after_{metric}']))
        cells += [number(r['paired_gpuP50DeltaMs']), number(r['paired_gpuP95DeltaMs']), number(r['paired_gpuP99DeltaMs']),
                  '미측정' if r['cpu8_33Met'] is None else ('충족' if r['cpu8_33Met'] else '8.33ms 미달성'),
                  '미측정' if r['gpuDeltaBelow0_5'] is None else ('<0.5ms' if r['gpuDeltaBelow0_5'] else '≥0.5ms 조정 필요')]
        rows.append('<tr>' + ''.join(f'<td>{html.escape(v)}</td>' for v in cells) + '</tr>')
    headers = ['구간', 'CPU p50', 'CPU p95', 'CPU p99', 'GPU p50', 'GPU p95', 'GPU p99', 'Frame p50', 'Frame p95', 'Frame p99', '짝별 ΔGPU p50 중앙값', '짝별 ΔGPU p95 중앙값', '짝별 ΔGPU p99 중앙값', 'CPU 기준', 'GPU 증가 기준']
    metadata = '<table>' + ''.join(f'<tr><th>{html.escape(k)}</th><td>{html.escape(v)}</td></tr>' for k, v in performance_metadata(perf)) + '</table>'
    warnings = ([f"오류: {perf['error']}"] if perf.get('error') else []) + [f'복원 오류: {v}' for v in perf.get('cleanupErrors', [])]
    warnings_html = ''.join(f'<p class="notice">{html.escape(v)}</p>' for v in warnings)
    limitations = ''.join(f'<li>{html.escape(str(v))}</li>' for v in perf.get('limitations', []))
    details = f'<details><summary>원장에 기록된 측정 한계</summary><ul>{limitations}</ul></details>' if limitations else ''
    empty = '<p>측정 구간 수치 없음 · 미측정.</p>' if not rows else ''
    return f'<p>{html.escape(perf["note"])} <a href="{perf["path"]}">전체 p95/p99·원시 원장</a></p>{metadata}{warnings_html}<p class="muted">단위 ms. 반복별 백분위 값의 중앙값이며 전체 프레임을 합친 백분위가 아님. CPU 값에는 실제 Editor·월드 비용이 포함됨. GPU 누락·0 값과 원장에 없는 ΔGPU p99는 미측정이며 추정하지 않음. 표의 기준은 기록된 수치만 평가하며 측정 완료나 실제 플레이 성능 통과를 뜻하지 않음.</p>{empty}<div class="scroll"><table><tr>' + ''.join(f'<th>{v}</th>' for v in headers) + '</tr>' + ''.join(rows) + '</table></div>' + details


def performance_markdown(perf):
    if not perf:
        return '\n\n성능 원장 없음 · 미측정.\n'
    result = '\n\n' + perf['note'] + '\n\n'
    result += '\n'.join(f'- **{key}**: {value}' for key, value in performance_metadata(perf))
    result += '\n\n각 수치는 반복별 백분위의 중앙값(ms)이며 전체 프레임을 합친 백분위가 아닙니다. 원장에 없는 값과 GPU 0은 미측정입니다.\n\n'
    result += '| 구간 | CPU 전→후 p50 / p95 / p99 | GPU 전→후 p50 / p95 / p99 | Frame 전→후 p50 / p95 / p99 | 짝별 ΔGPU p50 / p95 / p99 | 기준 |\n|---|---|---|---|---|---|\n'
    for row in perf['rows']:
        cells = [str(row['phase'])]
        for kind in ['cpu', 'gpu', 'frame']:
            cells.append(' / '.join(number(row[f'before_{kind}P{q}']) + '→' + number(row[f'after_{kind}P{q}']) for q in [50, 95, 99]))
        cells.append(' / '.join(number(row[f'paired_gpuP{q}DeltaMs']) for q in [50, 95, 99]))
        cpu = 'CPU 미측정' if row['cpu8_33Met'] is None else ('CPU ≤8.33ms' if row['cpu8_33Met'] else 'CPU 8.33ms 미달성')
        gpu = 'GPU 미측정' if row['gpuDeltaBelow0_5'] is None else ('ΔGPU <0.5ms' if row['gpuDeltaBelow0_5'] else 'ΔGPU ≥0.5ms 조정 필요')
        cells.append(cpu + ' · ' + gpu)
        result += '| ' + ' | '.join(v.replace('|', '\\|') for v in cells) + ' |\n'
    if perf.get('error'):
        result += '\n원장 오류: ' + str(perf['error']) + '\n'
    for error in perf.get('cleanupErrors', []):
        result += '\n복원 오류: ' + str(error) + '\n'
    if perf.get('limitations'):
        result += '\n원장에 기록된 한계:\n\n' + '\n'.join('- ' + str(v) for v in perf['limitations']) + '\n'
    return result + '\n[성능 원시 원장](' + perf['path'] + ')\n'


def source_links():
    sources = [('논문 페이지별 근거·적용 범위', OUT / 'REFERENCE_METHOD.md'), ('진행 기록', OUT / 'WORKLOG.md'),
               ('레거시 실제 셰이더 출처', OUT.parent.parent / 'BroadBrush/LEGACY_SOURCE.md'),
               ('FormMap 앵커 제안', ROOT / 'Tools/Unity/Staging/FormMapAnchors/form_map_anchors_proposal.json'),
               ('FormMap v2 · 실제 산면 재등록', ROOT / 'Tools/Unity/Staging/FormMapAnchors/form_map_anchors_v2_loaded_faces.json'),
               ('FormMap v2 변경 근거', ROOT / 'Tools/Unity/Staging/FormMapAnchors/V2_LOADED_FACES_NOTES.md'),
               ('형태별 먹 표현 셰이더', ROOT / 'Oheangbu/Assets/_Project/Shaders/InkPaintingStudy/InkPaintingStudy.hlsl')]
    for name in ['가상현실속3D공간에서의동양화기법재현방법연구해돌산수화의사례를중심으로.pdf', 'PA-04-09.pdf', '000000011103_20260917074313.pdf']:
        sources.append((name, ROOT.parent / 'Desktop' / name))
    return [{'title': title, 'href': link(path)} for title, path in sources if path.exists()]


def render(summary, rows):
    payload = json.dumps(summary['views'], ensure_ascii=False).replace('<', '\\u003c')
    options = ''.join(f'<option value="{i}">{html.escape(v["title"])} · {html.escape(v["id"])}</option>' for i, v in enumerate(summary['views']))
    table = ''.join(f'<tr><th>{html.escape(k)}</th><td>{html.escape(v)}</td></tr>' for k, v in rows)
    receipts = ' · '.join(f'<a href="{r["path"]}">{html.escape(k)} 영수증</a>' for k, r in summary['receipts'].items() if r)
    sources = ' · '.join(f'<a href="{r["href"]}">{html.escape(r["title"])}</a>' for r in summary['sources'])
    page = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>겹산과 먹획 · 저장 상태 비교</title><style>
:root{color-scheme:dark;font:16px/1.7 system-ui,"Malgun Gothic",sans-serif;background:#1c1d19;color:#e9e5da}*{box-sizing:border-box}body{max-width:1800px;margin:auto;padding:30px 24px 70px}h1{font-size:clamp(27px,4vw,43px);line-height:1.3}h2{font-size:24px}a{color:#d7c397}p{max-width:1200px}.notice{border-left:3px solid #c8b382;background:#302f27;padding:14px 20px}section{border-top:1px solid #55564b;margin-top:30px;padding-top:18px}.controls{display:flex;gap:10px;flex-wrap:wrap;align-items:center}button,select{font:inherit;background:#292b23;color:#eee;border:1px solid #727265;padding:7px 14px;max-width:100%}button{cursor:pointer}button.selected{background:#c5b99c;color:#23231d}#frames{display:grid;grid-template-columns:minmax(0,1fr);gap:16px}#frames.compare{grid-template-columns:repeat(2,minmax(0,1fr))}figure{margin:16px 0;min-width:0}img{display:block;width:100%;height:auto}figcaption,.muted{color:#b9b8ad;font-size:14px;overflow-wrap:anywhere}.missing{display:grid;place-content:center;aspect-ratio:16/9;background:#282923;border:1px dashed #777;padding:24px;text-align:center}table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #4b4c41;padding:10px;text-align:left;vertical-align:top}th{min-width:130px}.scroll{overflow-x:auto}pre{white-space:pre-wrap;overflow-wrap:anywhere}#position{font-variant-numeric:tabular-nums}@media(max-width:700px){body{padding:18px 12px}#frames.compare{grid-template-columns:minmax(0,1fr)}th{min-width:95px}}</style><body>
<p class="muted">@DATE@ · 진행 중</p><h1>산의 형태와 먹 덩어리,<br>저장된 장면으로 비교</h1><p>길·시설을 보존하는 산형과 산면별 먹·여백을 비교합니다. 그림 출처와 기술 적용 상태를 따로 표시합니다.</p>
<p class="notice">미술 완성·실제 이동·최종 성능은 기술 영수증만으로 통과 처리하지 않습니다. 기준선이 없는 구도에 과거 이미지를 대신 끼우지 않습니다.</p><p>@COUNT@</p>
<div class="controls"><label for="view">구도</label><select id="view">@OPTIONS@</select><button data-mode="after" class="selected">적용 후 / 현재</button><button data-mode="before">저장 외형 기준선</button><button data-mode="compare">기준선과 나란히</button><button data-mode="archive">과거 참고와 나란히</button></div><p id="position" class="muted"></p><div id="frames"></div>
<section><h2>적용 영수증과 남은 확인</h2><table>@TABLE@</table><p>@RECEIPTS@</p><p class="muted">영수증의 자체 상태를 표시합니다. HTML 생성기는 Unity 상태를 재검사하지 않습니다.</p></section>
<section><h2>성능</h2><p>@PERFNOTICE@</p>@PERF@</section>
<section><h2>논문과 실제 구현 출처</h2><p>박민지·성정환(2019), 「가상현실 속 3D 공간에서의 동양화 기법 재현방법 연구 — ‘해돌 산수화’의 사례를 중심으로」 37쪽 그림 4–5는 산 형태에 맞춘 수작업 먹 텍스처를, 38쪽 그림 6–7은 Unlit 표현과 산 하부·하늘·구름의 종이색 연결을 보여 줍니다. 현재 FormMap은 그 저작 원리를 실제 게임 산면의 월드 좌표 필드에 적용한 별도 구현이며, 논문의 텍스처나 산 모델을 가져온 것이 아닙니다.</p><p>다른 두 논문의 획 내부 먹량과 농·중·담 안료층 연결은 표면 표현의 보조 근거입니다. 논문의 성능이나 2D 배경을 현재 게임의 3D 산 구현 성과로 전용하지 않습니다. 남아 있는 긴 직선 산벽과 균일한 먹면은 논문 인용이나 기술 검사 통과로 해결되었다고 간주하지 않습니다.</p><p>@SOURCES@</p></section>
<details><summary>15개 저장 상태 촬영 명령</summary><p>현재 열려 있는 저장 씬을 촬영합니다. 적용 후 실행한 capture-before는 과거 기준선을 복원하지 않습니다.</p><pre>@COMMANDS@</pre></details>
<script>const views=@DATA@;let current=0,mode='after';const frames=document.getElementById('frames');
function item(c,title){const f=document.createElement('figure');const h=document.createElement('h2');h.textContent=title;f.append(h);if(c.image){const a=document.createElement('a');a.href=c.image;a.target='_blank';const img=document.createElement('img');img.src=c.image;img.width=1920;img.height=1080;img.alt=views[current].title+' '+title;img.addEventListener('error',()=>{img.replaceWith(Object.assign(document.createElement('p'),{textContent:'이미지를 불러오지 못했습니다.'}))});a.append(img);f.append(a)}else{const p=document.createElement('div');p.className='missing';p.textContent='미촬영 · 이 구도의 해당 기준 이미지는 없습니다.';f.append(p)}const caption=document.createElement('figcaption');caption.textContent=c.label+' · '+c.status+' · '+c.stage+(c.image?' · '+c.size.join('×')+' 실제 Unity 렌더':'');f.append(caption);for(const k of ['check','submissions','version'])if(c[k]){const a=document.createElement('a');a.href=c[k];a.textContent=k==='check'?'촬영 원장':k==='version'?' 재질·셰이더 버전':' 실제 제출량';f.append(a,document.createTextNode(' '))}if(c.image){const note=document.createElement('p');note.className='muted';note.textContent=c.materialVersionNote;f.append(note)}return f}
function update(){const v=views[current];frames.replaceChildren();frames.classList.toggle('compare',mode==='compare'||mode==='archive');if(mode==='before'||mode==='compare')frames.append(item(v.before,'저장 외형 기준선'));if(mode==='archive')frames.append(item(v.archive,'과거 참고 · 현재 기준선 아님'));if(mode==='after'||mode==='compare'||mode==='archive')frames.append(item(v.after,'적용 후 / 현재'));const fmt=p=>[p.x,p.y,p.z].map(n=>Number(n).toFixed(1)).join(', ');document.getElementById('position').textContent=v.id+' · eye ('+fmt(v.eye)+') → target ('+fmt(v.target)+') · 60° / 16:9';for(const b of document.querySelectorAll('[data-mode]'))b.classList.toggle('selected',b.dataset.mode===mode)}document.getElementById('view').addEventListener('change',e=>{current=Number(e.target.value);update()});for(const b of document.querySelectorAll('[data-mode]'))b.addEventListener('click',()=>{mode=b.dataset.mode;update()});update();</script></body></html>'''
    replacements = {'@DATE@': html.escape(summary['generatedUtc']), '@COUNT@': f"적용 후 촬영 검사 {summary['afterValid']}/15개 · 저장 외형 기준선 {summary['beforeValid']}/15개.",
                    '@OPTIONS@': options, '@TABLE@': table, '@RECEIPTS@': receipts, '@SOURCES@': sources,
                    '@COMMANDS@': html.escape('\n'.join(summary['captureCommands'])), '@DATA@': payload,
                    '@PERFNOTICE@': '측정 원장을 명시적으로 선택했습니다. 완료 여부와 비교 가능한 범위는 아래 원장의 상태·범위에 따릅니다.' if summary['currentPerformance'] else '이번 영구 산형·FormMap 버전의 최종 성능은 미측정입니다.',
                    '@PERF@': performance_html(summary['currentPerformance'] or summary['historicalPerformance'])}
    for key, value in replacements.items():
        page = page.replace(key, value)
    return page


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--label', '--after-label', dest='after_label', default='installed_v3')
    parser.add_argument('--before-label', default='installed_look_only')
    parser.add_argument('--after-kind', choices=['installed', 'preview', 'unknown'], default='installed')
    parser.add_argument('--archive-folder', type=Path, default=OUT.parent.parent / 'Painterly', help='Clearly separate historical comparison set')
    parser.add_argument('--archive-label', default='after')
    parser.add_argument('--performance-current', type=Path, help='Explicit measured summary.json; never auto-promote old diagnostics')
    parser.add_argument('--performance-historical', type=Path, help='Explicit older version measurement, labelled historical')
    parser.add_argument('--require-all', action='store_true', help='Require15 validated AFTER captures; missing baseline remains labelled')
    parser.add_argument('--require-baseline-all', action='store_true')
    parser.add_argument('--print-capture-commands', action='store_true')
    parser.add_argument('--dry-run', action='store_true', help='Validate and render in memory, without writing review artifacts')
    args = parser.parse_args()
    for label in [args.after_label, args.before_label, args.archive_label]:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', label):
            parser.error('Capture labels may contain letters, digits, underscore or hyphen only')
    views = read(OUT / 'views.json')['views']
    if len(views) != 15 or len({v['id'] for v in views}) != 15:
        raise ValueError('Expected15 existing unique fixed views')
    commands = [f'ink-study:capture-installed:{args.after_label}:{v["id"]}' for v in views]
    if args.print_capture_commands:
        print('\n'.join(commands)); return
    receipts = collect_receipts()
    archive_folder = resolve(str(args.archive_folder))
    archive_views = {v['id']: v for v in (read(archive_folder / 'views.json') or {}).get('views', [])}
    entries = [{**v, 'title': TITLES.get(v['id'], v['id']), 'before': capture(args.before_label, v['id'], receipts, 'installed'),
                'after': capture(args.after_label, v['id'], receipts, args.after_kind),
                'archive': archive_capture(archive_folder, args.archive_label, v, archive_views)} for v in views]
    after_valid, before_valid = (sum(v[side]['validatedCapture'] for v in entries) for side in ['after', 'before'])
    if args.require_all and after_valid != 15:
        raise ValueError(f'After set incomplete:{after_valid}/15 validated; no report written')
    if args.require_baseline_all and before_valid != 15:
        raise ValueError(f'Baseline incomplete:{before_valid}/15 validated; no report written')
    summary = {'status': 'REVIEW_IN_PROGRESS_NOT_ART_APPROVAL', 'generatedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
               'beforeLabel': args.before_label, 'afterLabel': args.after_label, 'beforeValid': before_valid, 'afterValid': after_valid,
               'views': entries, 'receipts': receipts, 'captureCommands': commands, 'sources': source_links(),
               'archiveFolder': str(archive_folder), 'archiveLabel': args.archive_label,
               'currentPerformance': performance(resolve(str(args.performance_current)), True) if args.performance_current else None,
               'historicalPerformance': performance(resolve(str(args.performance_historical)) if args.performance_historical else latest('Performance/*/summary.json'))}
    if args.performance_current and summary['currentPerformance'] is None:
        raise ValueError('Explicit current performance receipt does not exist')
    if args.performance_historical and summary['historicalPerformance'] is None:
        raise ValueError('Explicit historical performance receipt does not exist')
    rows = status_rows(receipts); page = render(summary, rows)
    if not args.dry_run:
        (OUT / 'REVIEW.html').write_text(page, encoding='utf-8')
        (OUT / 'review_summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
        report = f'# 저장 상태 수묵 산형 리뷰\n\n진행 중이며 미술·플레이 최종 완료가 아닙니다.\n\n적용 후 촬영 검사:{after_valid}/15. 저장 외형 기준선:{before_valid}/15.\n\n'
        report += '\n'.join(f'- **{key}**: {value}' for key, value in rows)
        report += performance_markdown(summary['currentPerformance'] or summary['historicalPerformance'])
        report += '\n\n[15구도 인터랙티브 리뷰](REVIEW.html) · [이미지별 출처와 영수증](review_summary.json)\n'
        (OUT / 'REPORT.md').write_text(report, encoding='utf-8')
    print(json.dumps({'dryRun': args.dry_run, 'afterValid': after_valid, 'beforeValid': before_valid, 'views': len(entries),
                      'htmlBytes': len(page.encode('utf-8')), 'review': str(OUT / 'REVIEW.html')}, ensure_ascii=False))


if __name__ == '__main__':
    main()
