"""Refresh the offline C02 hand-rig review from saved evidence only.

No Unity/Blender process, render, provider request, build, or gameplay input.
The scoped HAND_RIG_PASS must never be promoted to whole-player acceptance.
"""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import html
import json
import math
import os
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/PlayerPhase1/PlaytestReRig'


def read(relative):
    path = OUT / relative
    if not path.is_file():
        return {}
    try:
        return json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError):
        return {'status': 'READ_PENDING', 'scope': '작성 중이거나 읽을 수 없는 근거 파일. 갱신 후 다시 확인.'}


def esc(value):
    return html.escape(str(value), quote=True)


def href(path):
    return quote(Path(os.path.relpath(path, OUT)).as_posix(), safe='/.-_')


def link(path, title=None):
    return f'<a href="{href(path)}">{esc(title or path.name)}</a>'


def digest(path):
    if not path.is_file():
        return None
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def num(value, places=0):
    return f'{value:,.{places}f}' if isinstance(value, (float, int)) else '미검증'


def figure(name, title, detail=''):
    path = OUT / name
    if not path.is_file():
        return f'<article class="missing"><h3>{esc(title)}</h3><p>검증 이미지 미확보</p></article>'
    return f'<figure><a href="{href(path)}"><img src="{href(path)}" alt="{esc(title)}" loading="lazy"></a><figcaption><strong>{esc(title)}</strong><span>{esc(detail)}</span></figcaption></figure>'


def run():
    static = read('Validation/static_checks.json')
    contact = read('Validation/hand_contact.json')
    surface = read('Validation/hand_surface_sampling.json')
    roundtrip = read('Validation/fbx_roundtrip.json')
    imported = read('Unity/import_inspection.json')
    gate = read('Exports/rig_gate.json')
    installed = read('Unity/installation.json')
    snapshot = read('Unity/runtime_snapshot.json')
    source = read('Validation/source_inspection.json')
    math_report = read('Unity/gesture_math.json')
    runtime_qa = read('Unity/runtime_gesture_qa.json')
    gait_feet = read('Unity/isolated_gait_foot_clearance.json')
    foot_run = next((c for c in gait_feet.get('clips', []) if c.get('clip', '').endswith('_Run')), {})
    foot_samples = foot_run.get('samples', [])
    residual_y = min((s['correctedWholeMeshMinimumY'] for s in foot_samples), default=None)
    tip_max = max((c['observed'] for c in runtime_qa.get('checks', [])
                   if c.get('name', '').endswith('_near_tip_pixels')), default=None)
    runtime_summary = (f'합성 표현 진단 {runtime_qa.get("passed", "?")}개 통과 / {runtime_qa.get("failed", "?")}개 실패. '
                       f'1080p 9개 목표점 붓끝 투영 오차 최대 {num(tip_max, 3)}px. 실제 필기·시전 입력이나 실측 프레임률 검사가 아니다.')
    foot_summary = (f'120개 달리기 진단 자세의 발바닥 최소 높이: 좌 {num(foot_run.get("minimumLeftSoleY", 0) * 1000, 2)} → '
                    f'{num(foot_run.get("correctedMinimumLeftSoleY", 0) * 1000, 2)}mm, 우 '
                    f'{num(foot_run.get("minimumRightSoleY", 0) * 1000, 2)} → '
                    f'{num(foot_run.get("correctedMinimumRightSoleY", 0) * 1000, 2)}mm. '
                    f'평면 y=0 기준이며 전체 메시의 최저점은 여전히 {num(residual_y * 1000 if residual_y is not None else None, 2)}mm다. '
                    '도포 등 잔여 관통 부위의 식별·시각 검수와 실제 경사·계단은 미완료다.')
    source_path = Path(source.get('source', ''))
    preserved = digest(source_path) == source.get('source_sha256') if source_path.is_file() else None
    exported_hash = digest(OUT / 'Exports/Player_C02_ReRig.fbx')
    hash_match = bool(exported_hash and gate.get('sourceFbxSha256') == exported_hash)
    surface_hash_match = bool(exported_hash and surface.get('sourceFbxSha256') == exported_hash)
    penetration = surface.get('maximum_sampled_penetration_m')
    surface_pass = (surface_hash_match and str(surface.get('status', '')).startswith('PASS')
                    and isinstance(penetration, (int, float)) and math.isfinite(penetration)
                    and penetration <= .0005 and surface.get('ray_misses') == 0)
    if surface_hash_match and (str(surface.get('status', '')).startswith('FAIL')
                               or isinstance(penetration, (int, float)) and penetration > .0005):
        gate_status = '수정 필요 · 손–붓 표면 관통'
    elif not surface_pass:
        gate_status = '미검증 · 현재 FBX의 손 표면 검사 필요'
    elif not hash_match:
        gate_status = '미검증 · 현재 FBX의 리그 판정 갱신 필요'
    else:
        gate_status = gate.get('status', '미검증')
    surface_note = (f'삼각형 내부 {num(surface.get("samples"))}개 표본 / 간격 {num(surface.get("sampling_step_m", 0) * 1000, 3)}mm / '
                    f'최대 관통 {num(penetration * 1000 if isinstance(penetration, (int, float)) else None, 3)}mm. '
                    f'현재 FBX 해시 일치={surface_hash_match}. 유한 표본이며 모든 연속 표면·미래 자세의 무관통 증명이 아님.')
    now = datetime.now(timezone.utc).isoformat(timespec='seconds')
    body = installed.get('bodyTriangles', static.get('total_tris'))
    near = installed.get('nearTriangles')
    brush = installed.get('brushTriangles')
    combined = body + near + brush if all(isinstance(v, int) for v in (body, near, brush)) else None
    weights = max((part.get('max_weights', 0) for part in static.get('parts', [])), default=0)
    error = max((part.get('weight_sum_max_error', 0) for part in static.get('parts', [])), default=0)
    gaps = contact.get('closest_pad_gap_m', {})
    gap_text = ' / '.join(f'{name} {num(gaps[name] * 1000, 3)}mm' for name in ('Thumb', 'Index', 'Middle', 'Ring', 'Pinky') if name in gaps)

    rows = [
        ('원본 보존', '통과' if preserved else '미검증/불일치', '현재 원본 .blend SHA-256을 source_inspection 기록과 비교. 얼굴·체형·의상 원형과 텍스처는 기존 C02 사용.', 'Validation/source_inspection.json'),
        ('정적 메시·웨이트', static.get('status', '미검증'), f'전체 {num(body)} tris, 미할당·음수·비정상 웨이트는 원장 참조. 최대 {weights}개, 합계 최대 오차 {error:.8g}.', 'Validation/static_checks.json'),
        ('실제 붓대·손 피부 접촉', contact.get('status', '미검증'), '변형 후 손 정점과 실제 붓대 표면의 수치 검사. 연속 삼각형 충돌 또는 모든 동작의 무관통 증명은 아님.', 'Validation/hand_contact.json'),
        ('손 삼각형 내부 표면 검사', surface.get('status', '미검증'), surface_note, 'Validation/hand_surface_sampling.json'),
        ('FBX 빈 장면 재임포트', roundtrip.get('status', '미검증'), f'골격 {roundtrip.get("bones", "?")}개, 손가락 {roundtrip.get("finger_bones", "?")}개. Armature 1개, 진단 포즈 표식 포함. 런타임 동작은 FBX 애니메이션이 아님.', 'Validation/fbx_roundtrip.json'),
        ('Unity Humanoid 임포트', imported.get('status', '미검증'), f'Avatar valid={imported.get("avatarValid", "미검증")}, human={imported.get("avatarHuman", "미검증")}, 손가락 매핑={imported.get("fingerBonesMapped", "미검증")}. 휴식 자세 임포트 검사.', 'Unity/import_inspection.json'),
        ('손·상체 한정 종합 판정', gate_status, f'gate 원장={gate.get("status", "미검증")}. 정점 접촉 PASS만으로 합격시키지 않는다. 현재 FBX의 내부 표면 검사와 원장 해시가 일치해야 HAND_RIG_PASS를 표시한다. 하체·천 물리는 별도 범위.', 'Exports/rig_gate.json'),
        ('Playtest 전용 설치', installed.get('status', '미검증'), '별도 모델·프리팹·프로필. Root Motion OFF, 근접 팔에 별도 Animator 없음.', 'Unity/installation.json'),
        ('회전 계산 검사', math_report.get('status', '미검증'), f'축 {math_report.get("axisCases", "?")}개, 손가락 복귀 {math_report.get("fingerCycles", "?")}회. 순수 수학 검사이며 실제 동작 자연스러움의 증거는 아님.', 'Unity/gesture_math.json'),
        ('현재 런타임 스냅샷', snapshot.get('status', '미검증'), f'UTC {snapshot.get("utc", "미확보")}. skinWeights={snapshot.get("skinWeights", "미검증")}, active lease={snapshot.get("activeSkinningLeases", "미검증")}. 한 상태 표본을 전체 기능 PASS로 취급하지 않음.', 'Unity/runtime_snapshot.json'),
    ]
    expected = [
        ('runtime_gesture_qa.json', '런타임 리그 합성 진단', '현재 리그에 합성 표현 상태를 적용한 검사. 실제 손글씨 입력·전투 검증과 구분.'),
        ('current_skin_contact.json', '현재 Unity 피부 접촉', '현재 런타임 스키닝 표면과 붓 접촉 검사. 정적 Blender 접촉과 구분.'),
        ('isolated_gait.json', '격리 보행 검사', '별도 동작 시험의 실제 결과. 사용자 보행 완주·전체 천 물리와 구분.'),
    ]
    for filename, title, fallback in expected:
        candidates = [path for folder in ('Unity', 'Validation') for path in (OUT / folder).rglob(filename)]
        path = max(candidates, key=lambda item: item.stat().st_mtime) if candidates else OUT / 'Unity' / filename
        data = read(path.relative_to(OUT))
        checks = data.get('checks', [])
        checks = checks if isinstance(checks, list) else []
        counts = {key: sum(check.get('status') == key for check in checks if isinstance(check, dict)) for key in ('PASS', 'FAIL', 'UNVERIFIED')}
        summary = f'PASS {counts["PASS"]} / FAIL {counts["FAIL"]} / UNVERIFIED {counts["UNVERIFIED"]}. ' if checks else ''
        scope = summary + str(data.get('scope', data.get('detail', fallback)))
        if not path.is_file():
            scope = '검사 원장 미확보. ' + scope
        rows.append((title, data.get('status', '미검증'), scope, path.relative_to(OUT).as_posix()))
    # Future saved check suites are included without assigning broader acceptance.
    known = {Path(row[3]).name for row in rows} | {'gesture_calibration.json'}
    for path in sorted((OUT / 'Unity').glob('*.json')):
        if path.name in known:
            continue
        data = read(path.relative_to(OUT))
        if not isinstance(data, dict):
            continue
        checks = data.get('checks', [])
        if not isinstance(checks, list):
            checks = []
        counts = {key: sum(c.get('status') == key for c in checks if isinstance(c, dict)) for key in ('PASS', 'FAIL', 'UNVERIFIED')}
        summary = f'PASS {counts["PASS"]} / FAIL {counts["FAIL"]} / UNVERIFIED {counts["UNVERIFIED"]}. ' if checks else ''
        rows.append((path.stem, data.get('status', '원장 확인'), summary + str(data.get('scope', data.get('detail', '저장된 원장 범위만 확인.'))), path.relative_to(OUT).as_posix()))

    checked_rows = []
    profile_hash = digest(ROOT / 'Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/PlayerGesture_C02_ReRig.asset')
    code_hash = digest(ROOT / 'Oheangbu/Assets/_Project/Scripts/App/World/WorldMacroPlayerGestureRig.cs')
    for title, status, scope, path in rows:
        data = read(path)
        evidence_hash = data.get('sourceFbxSha256') if isinstance(data, dict) else None
        if evidence_hash and evidence_hash != exported_hash:
            scope = f'원장 판정={status}. 이 결과의 FBX 해시가 현재 내보내기와 다르므로 현재 합격 근거로 사용하지 않음. ' + scope
            status = '미검증 · 이전 FBX 근거'
        if isinstance(data, dict) and ((data.get('gestureProfileSha256') and data['gestureProfileSha256'] != profile_hash)
                or (data.get('gestureCodeSha256') and data['gestureCodeSha256'] != code_hash)):
            scope = f'원장 판정={status}. 실행 후 표현 코드 또는 프로필이 달라져 현재 합격 근거로 사용하지 않음. ' + scope
            status = '미검증 · 이전 표현 코드/설정 근거'
        checked_rows.append((title, status, scope, path))
    rows = checked_rows

    table = '<table><thead><tr><th>검사</th><th>원장 판정</th><th>범위·한계</th></tr></thead><tbody>'
    for title, status, scope, path in rows:
        evidence_label = link(OUT / path, title) if (OUT / path).is_file() else esc(title)
        table += f'<tr><td>{evidence_label}</td><td><code>{esc(status)}</code></td><td>{esc(scope)}</td></tr>'
    table += '</tbody></table>'

    pairs = '<div class="grid two">' + figure('Images/before_Right_palm.png', '수정 전 · 오른손 손바닥', '손가락 본 추가 전 C02 원형') + figure('Images/grip_palm_final.png', '수정 후 · 오른손 파지', '최종 정적 파지. 전후 포즈·카메라가 달라 픽셀 정합 비교가 아님') + figure('Images/before_Right_side.png', '수정 전 · 오른손 측면') + figure('Images/grip_oblique_final.png', '수정 후 · 붓대 접촉 사선', '붓 전체를 늘리지 않고 손가락을 자루에 맞춤') + '</div>'
    poses = '<div class="grid two">' + ''.join(figure(f'Images/{name}.png', title, 'Blender 진단용 정지 자세 · 생산용 모션 검증과 구분') for name, title in [('neutral_front', '휴식 자세'), ('arms_down', '팔 내리기'), ('arms_raised', '팔 올리기'), ('elbow_flex', '팔꿈치 깊게 굽히기')]) + '</div>'
    latest = {}
    capture_reports = list((OUT / 'Unity/Screenshots').glob('*.json')) + list((OUT / 'Unity').glob('runtime_capture_*.json'))
    for path in sorted(capture_reports, key=lambda p: p.stat().st_mtime):
        data = read(path.relative_to(OUT))
        image_path = Path(data.get('image', ''))
        if image_path.is_file():
            latest[data.get('view', data.get('captureState', path.stem))] = (path, data, image_path)
    titles = {'hands': 'Unity · 손과 붓', 'front': 'Unity · 전신 정면', 'back': 'Unity · 전신 후면', 'side': 'Unity · 측면', 'shoulder': 'Unity · 숄더뷰', 'hand-palm': 'Unity · 손바닥', 'behind': 'Unity · 뒤 숄더', 'play': 'Unity · 플레이 시점', 'drawing': 'Unity · 작도 시점', 'center': '합성 작도 · 화면 중앙', 'top-left': '합성 작도 · 왼쪽 위', 'bottom-right': '합성 작도 · 오른쪽 아래', 'harvest': '합성 갈무리 · 플레이 시점', 'world-drawing': '합성 작도 · 외부 전신', 'world-harvest': '합성 갈무리 · 외부 전신'}
    runtime_images = '<div class="grid two">'
    for view, (path, data, image_path) in latest.items():
        runtime_images += figure(image_path.relative_to(OUT), titles.get(view, 'Unity · ' + view), f'{data.get("width", 1920)}×{data.get("height", 1080)} · {data.get("utc", "")} · {"입력·시전 없이 적용한 표현 진단 자세" if data.get("captureState") else "현재 플레이 모델 자세"}')
    runtime_images += '</div>' if latest else '<p>Unity 검증 스크린샷 미확보.</p></div>'

    files = [
        (OUT / 'Work/Player_C02_Assembly.blend', '몸·손·붓 조립 Blender'),
        (OUT / 'Work/Player_C02_ReRig.blend', '수정 Blender 원본'),
        (OUT / 'Exports/Player_C02_ReRig.fbx', '최종 내보내기 FBX'),
        (OUT / 'Validation/Player_C02_Reimport_Check.blend', 'FBX 빈 장면 재임포트 Blender'),
        (source_path, '보존한 C02 원본 Blender'),
        (ROOT / 'Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/PF_Player_C02_ReRig.prefab', 'Unity 프리팹'),
        (ROOT / 'Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/PlayerGesture_C02_ReRig.asset', 'Unity 손·몸 동작 프로필'),
        (ROOT / 'Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Textures/Integrated_B2_C3_M0_BaseColor.png', 'C02 원본 베이스 컬러 0'),
        (ROOT / 'Oheangbu/Assets/_Project/Art/C02_RigFaceLab/Textures/Integrated_B2_C3_M1_BaseColor.png', 'C02 원본 베이스 컬러 1'),
    ]
    file_links = '<ul class="files">' + ''.join(f'<li>{link(path, title)}</li>' for path, title in files if path.is_file()) + '</ul>'
    changes = [
        '기존 C02의 얼굴·체형·의상 디자인과 텍스처를 유지한 별도 사본이다. 손 부위만 세분화하고 양손 손가락 30개 본을 추가했다.',
        '중립 자세 높이를 기준으로 1.75m를 다시 맞췄다. 평상시 오른팔을 내려 붓을 들고, 작도 시 손목·팔꿈치·어깨·가슴을 연결하며 갈무리 효과의 시작점을 실제 붓끝에 연결했다.',
        '기존 대기·걷기·달리기를 새 Humanoid에 재사용했다. 발바닥이 평면 아래로 들어가는 구간에만 길이를 유지한 다리 보정을 추가했으며, 새 방향별 이동·회피·피격 클립을 제작한 것은 아니다.',
        '기존 본의 꼬리 길이가 관절 간격보다 100배 큰 문제를 기존 로컬 축 방향으로 보정했다. 관절 머리 위치와 축·바인드 기준을 유지하는 방식이다. 캐릭터 전체를 100배 축소한 변경이 아니다.',
        f'붓대 접촉을 위해 휴식 메시의 손 정점 {contact.get("corrected_vertices", "?")}개를 최대 {num(contact.get("max_rest_skin_correction_m", 0) * 1000, 3)}mm 보정했다. 붓 자루 스케일은 {contact.get("shaft_scale", "미검증")}다.',
        f'정점당 최대 {weights}개 웨이트를 유지한다. 런타임 Unlimited 스키닝은 활성 동안 lease로 관리한다. 전역 품질 설정을 영구적으로 바꾸는 합격 근거는 아니다.',
        'HAND_RIG_PASS는 손·상체 정적 변형 및 임포트의 한정 판정이다. 정점 접촉 검사만으로 합격시키지 않으며 삼각형 내부 표면 검사도 현재 FBX에 대해 통과해야 한다. 전체 보행·천 물리·카메라 전환·실제 손글씨 입력의 합격을 포함하지 않는다.',
    ]
    pending = [
        foot_summary,
        '원형에서 이어진 손 피부와 소매의 낮은 해상도·거친 표면은 남아 있다. 손가락 본과 파지 수치 통과는 근접 외형의 최종 완성을 뜻하지 않는다.',
        '걷기·달리기·회피·좌우 이동 중 발 미끄러짐, 전체 도포/소매 관통과 자연스러움: 이 손·상체 한정 판정으로는 미검증.',
        '모든 미래 자세의 피부–붓 연속 삼각형 충돌: 미검증. 정점 표본 및 0.5mm 간격 삼각형 내부 표본은 해당 정적 자세의 유한 샘플이다.',
        '천 물리의 중력 정착·복원·런타임 비용과 전체 하체 리그: 이번 HAND_RIG_PASS 범위 밖.',
        'Unity 상태 전환·작도·갈무리·화면 끝 추종은 해당 런타임 원장이 있을 때에만 그 범위로 읽는다. 정지 이미지 하나로 실제 플레이 완료를 판단하지 않는다.',
        '최종 얼굴·손·파지 인상과 움직임의 자연스러움은 사용자 검토 대기. 전체 작업 완료나 미술 승인으로 표기하지 않는다.',
    ]
    style = '''*{box-sizing:border-box}body{margin:0;background:#181b19;color:#e4e7df;font:16px/1.65 "Segoe UI","Malgun Gothic",sans-serif}main{max-width:1360px;margin:auto;padding:34px 28px 80px}h1{font-size:34px;line-height:1.2;margin:14px 0}h2{font-size:25px;margin:54px 0 18px}h3{font-size:18px}p{max-width:100ch}a{color:#b9d4c3;text-decoration:none}a:hover{text-decoration:underline}.muted,figcaption span{color:#a5b0a8}.note{border-left:3px solid #c3a774;background:#282a24;padding:16px 22px}.metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:24px 0}.metric{background:#252b27;padding:16px}.metric b{display:block;font-size:27px;color:#dfe4d8}.metric span{font-size:14px;color:#abb6ad}.grid{display:grid;gap:18px}.two{grid-template-columns:repeat(2,minmax(0,1fr))}figure{margin:0;background:#222824;border:1px solid #394039;border-radius:7px;overflow:hidden}figure img{width:100%;height:auto;display:block}figcaption{padding:13px 16px}figcaption strong,figcaption span{display:block}figcaption span{font-size:14px;margin-top:4px}table{width:100%;border-collapse:collapse;font-size:14px}th,td{text-align:left;padding:13px;border-bottom:1px solid #384039;vertical-align:top}th{background:#29322b}td:first-child{width:18%}td:nth-child(2){width:23%}code{font-family:Consolas,monospace;overflow-wrap:anywhere;color:#dfcb9f}li{margin:9px 0}.files{display:grid;grid-template-columns:repeat(2,1fr)}nav{display:flex;gap:20px;flex-wrap:wrap;border-bottom:1px solid #3c443e;padding:16px 0}footer{margin-top:45px;color:#9aa99d;font-size:13px}.tablewrap{overflow:auto}@media(max-width:780px){main{padding:24px 15px}.two{grid-template-columns:1fr}.metrics{grid-template-columns:repeat(2,1fr)}.files{display:block}h1{font-size:29px}}'''
    page = f'''<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>C02 손·상체 리깅 검토</title><style>{style}</style></head><body><main>
<p class="muted">오행부 · C02 플레이테스트 파생본</p><h1>손과 붓, 상체 리깅 검토</h1>
<p>기존 얼굴과 의상 원형을 유지하고 손가락 골격·파지를 보완한 작업본입니다. 아래 결과는 저장된 Blender·Unity 근거를 읽은 것으로, 전체 캐릭터 완성이나 사용자의 비주얼 승인을 뜻하지 않습니다.</p>
<div class="note"><strong>{esc(gate_status)} · 손·상체 한정</strong><br>미검증 영역은 남겨 두었습니다. 중간 빌드·영상은 만들지 않으며, 전체 작업을 마친 뒤 최종 빌드를 한 번 제작합니다.</div>
<p><strong>표면 검사: {esc(surface.get('status', '미검증'))}</strong><br>{esc(surface_note)} 정점 검사와 결론이 다르면 표면 검사의 실패를 우선 표시합니다.</p>
<nav><a href="#hands">손 전후</a><a href="#poses">상체 자세</a><a href="#unity">Unity 현재 모습</a><a href="#checks">검사 근거</a><a href="#files">결과 파일</a><a href="REPORT.md">보고서</a></nav>
<div class="metrics"><div class="metric"><b>{num(body)}</b><span>전신·눈꺼풀 tris</span></div><div class="metric"><b>{num(near)}</b><span>근접 팔 tris · 별도 표현</span></div><div class="metric"><b>{num(brush)}</b><span>붓 tris</span></div><div class="metric"><b>{roundtrip.get('bones', '?')} / {roundtrip.get('finger_bones', '?')}</b><span>전체 본 / 손가락 본</span></div></div>
<p class="muted">세 표시 자산 단순 합계 {num(combined)} tris. 카메라별 숨김·그림자 패스에 따라 실제 그리기 비용은 달라지며, 이 합계는 프레임 성능 측정값이 아닙니다.</p>
<h2>변경 범위</h2><ul>{''.join(f'<li>{esc(item)}</li>' for item in changes)}</ul>
<div class="note"><strong>이번 실행에서 확인한 결과</strong><p>{esc(runtime_summary)}</p><p>{esc(foot_summary)}</p></div>
<h2 id="hands">오른손 · 원형과 파지</h2><p>이미지 안에 라벨을 덧붙이지 않았습니다. 수정 전후의 카메라와 포즈가 다른 자료이므로 형태 확인용으로 비교합니다. 작은 이미지도 클릭하면 원본을 볼 수 있습니다.</p>{pairs}
<div class="note"><strong>손끝 정점 접촉 표본 · 단독 합격 기준 아님</strong><p>{esc(gap_text)}</p><p>정점 표본 최대 관통 {num(contact.get('max_penetration_m', 0) * 1000, 3)}mm. 실제 붓대 메시와 스키닝한 손 피부 정점을 사용했습니다. 삼각형 사이의 관통을 놓칠 수 있어 아래 표면 검사가 우선합니다.</p><p><strong>{esc(surface.get('status', '미검증'))}</strong> · {esc(surface_note)}</p></div>
<h2 id="poses">상체 진단 자세</h2>{poses}
<h2 id="unity">Unity · 저장된 최신 시점별 이미지</h2><p>뷰 이름별 가장 최근 저장 이미지를 표시합니다. 캡처는 당시의 현재 자세이며, 직접 이동·필기 입력이나 전체 플레이 완주 증거가 아닙니다.</p>{runtime_images}
<h2 id="checks">검사 근거와 범위</h2><div class="tablewrap">{table}</div>
<h2>남은 판정</h2><ul>{''.join(f'<li>{esc(item)}</li>' for item in pending)}</ul>
<h2 id="files">결과 파일</h2>{file_links}
<details><summary>저장된 전체 JSON 원장</summary><ul>{''.join(f'<li>{link(path, path.relative_to(OUT).as_posix())}</li>' for folder in ['Validation', 'Exports', 'Unity'] for path in sorted((OUT/folder).glob('*.json')))}</ul><p>draft/inspection/calibration 파일은 중간 기록 또는 입력 자료이며 최종 PASS 판정으로 합산하지 않습니다.</p></details>
<footer>갱신 UTC {now} · 로컬 파일만 사용 · 네트워크/자동 재생/렌더링/빌드 없음<br>재생성: <code>python Tools/Unity/update_rerig_review.py</code></footer></main></body></html>'''
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'REVIEW.html').write_text(page, encoding='utf-8')
    report = [
        '# C02 손·상체 리깅 검토', '', f'갱신 UTC: {now}', '',
        f'현재 판정: **{gate_status} — hands_and_upper_body 한정**. 전체 캐릭터·의복·보행 완성 판정은 아니다.',
        f'표면 검사: **{surface.get("status", "미검증")}**. {surface_note}',
        f'gate 원장에는 `{gate.get("status", "미검증")}`가 기록되어 있어도 현재 삼각형 내부 검사 FAIL을 덮지 않는다.',
        '손·상체 런타임 보정은 설치되어 있으며 실제 플레이 및 사용자 미술 검토는 남아 있다. 중간 빌드·영상은 제작하지 않는다. 작업 완료 후 최종 빌드를 한 번 구성한다.', '',
        runtime_summary, '', foot_summary, '',
        '## 변경 범위', '', *['- ' + item for item in changes], '',
        '## 실제 메시 통계', '',
        '| 대상 | 삼각형 |', '|---|---:|', f'| 전신·눈꺼풀 | {num(body)} |', f'| 근접 팔 | {num(near)} |', f'| 붓 | {num(brush)} |', f'| 자산 단순 합계 | {num(combined)} |', '',
        f'본 {roundtrip.get("bones", "미검증")}개, 손가락 본 {roundtrip.get("finger_bones", "미검증")}개. 최대 웨이트 {weights}개. 자산 합계는 실측 렌더 비용이 아니다.', '',
        '## 접촉 검사', '', gap_text, '',
        f'정점 표본 최대 관통 {num(contact.get("max_penetration_m", 0) * 1000, 3)}mm. 최대 휴식 메시 보정 {num(contact.get("max_rest_skin_correction_m", 0) * 1000, 3)}mm, 정점 {contact.get("corrected_vertices", "?")}개. 정점 표본 검사이며 단독 합격 기준이 아니다. 삼각형 내부 검사: {surface_note}', '',
        '## 저장된 검사', '', '| 항목 | 원장 판정 | 범위·한계 |', '|---|---|---|',
        *[f'| ' + (f'[{title}]({quote(path, safe="/._-")})' if (OUT / path).is_file() else title) + f' | {status} | {scope.replace(chr(10), " ").replace("|", "/")} |' for title, status, scope, path in rows], '',
        '## 이미지', '',
        '- 손 전후: before_Right_palm/side 및 grip_palm_final/grip_oblique_final. 포즈·카메라가 달라 픽셀 정합 비교가 아니다.',
        '- 상체: neutral_front, arms_down, arms_raised, elbow_flex. Blender 진단용 정지 자세다.',
        f'- Unity: 시점별 최신 저장 이미지 {len(latest)}개. 촬영 JSON에 당시 시간·시점·크기·자원 정리 범위가 기록된다.',
        '- 현재 오프라인 [검토 페이지](REVIEW.html)에서 원본 이미지를 열 수 있다. 사용자 비주얼 승인은 미완료다.', '',
        '## 미검증·범위 밖', '', *['- ' + item for item in pending], '',
        '## 결과 파일', '', *[f'- [{title}]({href(path)})' for path, title in files if path.is_file()], '',
        '## 원본·내보내기 식별', '',
        f'- 원본 .blend SHA-256: `{source.get("source_sha256", "미확보")}` / 현재 파일 일치: {preserved}',
        f'- 내보낸 FBX SHA-256: `{exported_hash or "미확보"}` / gate 기록 일치: {hash_match}', '',
        '## 갱신', '', '`python Tools/Unity/update_rerig_review.py`', '',
        '이 스크립트는 저장된 파일만 읽는다. 공급자 호출·Unity/Blender 실행·스크린샷·빌드·영상·자동 보행은 수행하지 않는다.', '',
    ]
    (OUT / 'REPORT.md').write_text('\n'.join(report), encoding='utf-8')
    print(json.dumps({'review': str(OUT/'REVIEW.html'), 'report': str(OUT/'REPORT.md'), 'gate': gate_status, 'evidenceRows': len(rows), 'runtimeViews': len(latest), 'buildInvoked': False}, ensure_ascii=False))


if __name__ == '__main__':
    run()
