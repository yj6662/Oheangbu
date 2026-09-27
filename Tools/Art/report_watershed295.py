"""Independent protection, source ledger and review evidence for Watershed295.

This tool never invokes Unity or changes candidate assets. ``baseline`` records
the accepted input once; ``verify`` compares those bytes without resetting them.
"""
from __future__ import annotations

import argparse
import contextlib
from datetime import datetime, timezone
import hashlib
import html
import json
from pathlib import Path
import re
import shutil
import sys
import time
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Watershed295'
BASE = OUT / 'Baseline'
SOURCE = ROOT / 'Oheangbu/Assets/_Project/Art/World/Reworld292'
ORIGINAL_MANIFEST = OUT.parent / 'Highlands293/protected-baseline.json'


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1 << 20), b''):
            value.update(block)
    return value.hexdigest()


def relative(path: Path) -> str:
    return path.relative_to(ROOT).as_posix()


def write_json(path: Path, data) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.partial')
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf8')
    temporary.replace(path)


def read_json(path: Path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def reproduce(generated: Path | None = None) -> None:
    """Run the generator in a new evidence directory, leaving current output intact."""
    import build_watershed295 as generator
    import numpy as np
    import PIL
    generated = generated or OUT / 'Generated'
    stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S')
    replay = OUT / 'Reproduction' / stamp
    replay.mkdir(parents=True, exist_ok=False)
    before = {p.relative_to(generated).as_posix(): digest(p)
              for p in sorted(generated.rglob('*')) if p.is_file()}
    source = Path(generator.__file__)
    source_hash = digest(source)
    def generation_inputs() -> dict[str, str]:
        files = [ROOT / 'Tools/Art/build_reworld292.py']
        for folder in (OUT / 'InputSurface294', OUT / 'Controls'):
            if folder.exists():
                files.extend(p for p in folder.rglob('*') if p.is_file())
        return {relative(p): digest(p) for p in sorted(files)}
    input_hashes = generation_inputs()
    control_hashes = {k: v for k, v in input_hashes.items() if '/Controls/' in k}
    started = time.monotonic()
    print('Replaying generator into', relative(replay), flush=True)
    generator.OUT = replay
    with (replay / 'run.log').open('w', encoding='utf8') as log, contextlib.redirect_stdout(log):
        generator.run()
        generator.crossing_supports()
    after = {p.relative_to(replay).as_posix(): digest(p)
             for p in sorted(replay.rglob('*')) if p.is_file() and p.name != 'run.log'}
    missing = sorted(set(before) - set(after))
    extra = sorted(set(after) - set(before))
    different = sorted(name for name in set(before) & set(after) if before[name] != after[name])
    stable = all((generated / name).is_file() and digest(generated / name) == sha for name, sha in before.items())
    stable = stable and source_hash == digest(source)
    input_hashes_after = generation_inputs()
    inputs_stable = input_hashes == input_hashes_after
    stable = stable and inputs_stable
    semantic_differences = {}
    for name in different:
        if not name.endswith('.json'):
            continue
        left, right = read_json(generated / name), read_json(replay / name)
        if isinstance(left, dict) and isinstance(right, dict):
            semantic_differences[name] = [k for k in sorted(set(left) | set(right)) if left.get(k) != right.get(k)]
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(),
                  passed=not missing and not extra and not different and stable,
                  duration_seconds=round(time.monotonic() - started, 2),
                  method='import build_watershed295; set OUT to new evidence directory; run(); crossing_supports()',
                  python=sys.version, numpy=np.__version__, pillow=PIL.__version__,
                  generator=relative(source), generator_sha256=source_hash,
                  generation_inputs_sha256=input_hashes,
                  generation_inputs_after_sha256=input_hashes_after,
                  controls_sha256=control_hashes, generation_inputs_stayed_stable=inputs_stable,
                  replay_directory=relative(replay), compared_files=len(before),
                  identical_files=sum(before.get(k) == sha for k, sha in after.items()),
                  generated_and_source_stayed_stable=stable, missing=missing, extra=extra,
                  different=different, different_json_keys=semantic_differences,
                  expected_sha256=before, reproduced_sha256=after)
    report = 'reproduction.json' if generated == OUT / 'Generated' else 'reproduction-' + generated.name + '.json'
    write_json(OUT / report, result)
    print(json.dumps({k: v for k, v in result.items() if not k.endswith('_sha256')}, ensure_ascii=False, indent=2))
    if not result['passed']:
        raise RuntimeError('Independent replay differs; inspect reproduction.json before claiming determinism.')


def compare(expected: dict[str, str]) -> dict:
    changed, missing = [], []
    for key, sha256 in expected.items():
        path = ROOT / key
        if not path.is_file():
            missing.append(key)
        elif digest(path) != sha256:
            changed.append(key)
    return dict(files=len(expected), changed=changed, missing=missing,
                passed=not changed and not missing)


def baseline() -> None:
    if (BASE / 'complete.json').exists():
        raise RuntimeError('Baseline already recorded; it must not be reset.')
    if BASE.exists() and any(BASE.iterdir()):
        raise RuntimeError('Partial baseline exists; inspect it before any retry.')
    BASE.mkdir(parents=True, exist_ok=True)
    original = read_json(ORIGINAL_MANIFEST)
    original_check = compare(original)
    if not original_check['passed']:
        raise RuntimeError('Existing original protection already differs: ' + str(original_check))
    paths = sorted(p for p in SOURCE.rglob('*') if p.is_file()
                   and 'Watershed295' not in p.relative_to(SOURCE).parts)
    all_inputs = {relative(p): digest(p) for p in paths}
    # Shared candidate shaders can gain opt-in 295 features. All existing asset
    # bytes, including 294 water, source terrain and the 294 scene, stay protected.
    protected = {p: sha for p, sha in all_inputs.items()
                 if '/Reworld292/Shaders/' not in p}
    upper = {p: sha for p, sha in all_inputs.items()
             if '/Reworld292/Highlands293/' in p}
    write_json(BASE / 'original-422.json', original)
    write_json(BASE / 'candidate-294.json', protected)
    write_json(BASE / 'upper-highlands293.json', upper)
    write_json(BASE / 'recovery-files.json', all_inputs)
    recovery = BASE / 'Candidate294.zip'
    with zipfile.ZipFile(recovery.with_suffix('.zip.partial'), 'w',
                         zipfile.ZIP_DEFLATED, compresslevel=1, allowZip64=True) as archive:
        for path in paths:
            archive.write(path, relative(path))
    recovery.with_suffix('.zip.partial').replace(recovery)
    # These small input copies allow independent height/protected-area checks
    # without extracting the full archive.
    for name in ('height.bytes', 'layout.json', 'routes.json', 'surface.png', 'cartography.png'):
        shutil.copy2(SOURCE / 'Surface' / name, BASE / name)
    final_check = compare(all_inputs)
    if not final_check['passed']:
        raise RuntimeError('Inputs changed while baseline was captured: ' + str(final_check))
    # A ZIP CRC pass confirms the archive is readable, while source SHA256 values
    # record the exact recovery content (the source was stable across the copy).
    with zipfile.ZipFile(recovery) as archive:
        corrupt = archive.testzip()
        if corrupt:
            raise RuntimeError('Recovery archive CRC failure: ' + corrupt)
    result = dict(recorded_utc=datetime.now(timezone.utc).isoformat(),
                  original_files=len(original), candidate_files=len(protected),
                  upper_highlands_files=len(upper), recovery_files=len(all_inputs),
                  recovery_bytes=recovery.stat().st_size, recovery_sha256=digest(recovery),
                  scope='Existing Reworld292 assets/scene plus original 422 manifest; shared shaders recorded for recovery but allowed to gain opt-in 295 code. No external project dependencies or saves are in this ZIP.')
    write_json(BASE / 'complete.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2), flush=True)


def verify() -> dict:
    if not (BASE / 'complete.json').is_file():
        raise RuntimeError('A completed pre-change baseline is required.')
    groups = {key: compare(read_json(BASE / file)) for key, file in (
        ('original', 'original-422.json'), ('candidate294', 'candidate-294.json'),
        ('upper_highlands293', 'upper-highlands293.json'))}
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(), groups=groups,
                  passed=all(group['passed'] for group in groups.values()))
    write_json(OUT / 'protection-check.json', result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if not result['passed']:
        raise RuntimeError('Protected source assets changed.')
    return result


def references() -> None:
    research = [
        dict(id='tsushima', title='Ghost of Tsushima — Crafting the world of Tsushima', authority='Sucker Punch environment art lead / PlayStation Blog',
             url='https://blog.playstation.com/2020/07/09/crafting-the-world-of-tsushima/',
             lesson='주요 식생 종류를 제한하고 질감 노이즈를 줄여 지역의 큰 형태와 여백을 읽게 한다. 절차적 도구는 저작한 구성을 유지하도록 사용한다.'),
        dict(id='horizon', title='Horizon Forbidden West — Materials and Textures', authority='Tom Jacobs, contributing technical artist',
             url='https://tomjacobs101.artstation.com/projects/PeBY5Z',
             lesson='자갈·흙·모래·돌의 높이 혼합과 강변 재질 연결을 참조한다. 해당 게임의 텍스처는 가져오지 않는다.'),
        dict(id='sekiro', title='Sekiro — Fountainhead Palace screenshots', authority='Vandal gameplay guide; visual reference, not a developer production source',
             url='https://vandal.elespanol.com/guias/guia-sekiro-shadows-die-twice-trucos-y-consejos/palacio-del-manantial',
             lesson='수몰 건축·넓은 수면·높은 관찰 지점의 대비를 참조한다. 실제 한국 건축과 이번 게임의 이동 규칙으로 다시 구성한다.'),
        dict(id='meonguri', title='한탄강 멍우리 협곡', authority='한탄강세계지질공원',
             url='https://hantangeopark.kr/bbs/content.php?co_id=sight_01_08',
             image_url='https://hantangeopark.kr/img/sight_slide_08_01.jpg',
             lesson='서로 다른 양안: 기반암 절벽과 완만한 자갈 둔치; 모든 물가에 같은 단면을 반복하지 않는다.'),
        dict(id='ageobong', title='충주호 악어봉', authority='한국관광공사',
             url='https://english.visitkorea.or.kr/svc/contents/contentsView.do?menuSn=351&vcontsId=191465',
             lesson='물속으로 이어지는 기존 산줄기·반도와 가지형 만. 인공 호수의 모습만 참조하며 자연 호수 생성 근거로 쓰지 않는다.'),
        dict(id='donggang', title='동강 어름치마을', authority='한국관광공사',
             url='https://korean.visitkorea.or.kr/detail/rem_detail.do?cotid=70806122-b452-42f1-b80b-dc444a5f5b4a',
             lesson='산이 감싸는 긴 굽이, 자갈톱과 깊은 소, 강변 접근부의 대비.'),
        dict(id='fluvial', title='River Systems and Fluvial Landforms', authority='US National Park Service',
             url='https://home.nps.gov/subjects/geology/fluvial-landforms.htm',
             lesson='분수계와 본류·지류, 저경사 범람원, 퇴적 지형을 먼저 정의한다.'),
        dict(id='point-bars', title='Mississippi alluvial landforms', authority='US Geological Survey',
             url='https://pubs.usgs.gov/ha/ha730/ch_f/F-text2.html',
             lesson='바깥굽이 침식·안굽이 퇴적·지류 합류부 선상지를 암석/자갈/진흙 마스크와 연결한다.'),
        dict(id='outlet', title='Devils Lake outlet and spill elevation', authority='US Geological Survey',
             url='https://pubs.usgs.gov/wri/2000/4174/report.pdf',
             lesson='호수 수위와 연결 영역은 유출턱에 종속된다. 목표 면적을 맞추는 원형 둘레 융기는 피한다.'),
        dict(id='riffle', title='Paria Riffle and the Colorado River', authority='US National Park Service',
             url='https://www.nps.gov/places/paria-riffle-and-the-colorado-river.htm',
             lesson='퇴적물과 수로 수축이 있는 합류부에 여울을 두고 넓고 깊은 수면과 구별한다.'),
    ]
    for item in research:
        item.update(use='Visual/research reference only; not imported into the game.',
                    license='No asset license granted by this ledger; original copyright applies.',
                    downloaded=False)
    source = read_json(OUT.parent / 'River294/sources.json')
    checked = []
    for item in source['cc0']:
        if digest(ROOT / item['path']) != item['sha256']:
            raise RuntimeError('Existing CC0 source hash differs: ' + item['path'])
        checked.append(item)
    write_json(OUT / 'references.json', dict(researched_utc=datetime.now(timezone.utc).isoformat(),
               research=research, available_cc0_sources=checked,
               existing_project_plants=source['existing_plants'],
               new_asset_downloads=0,
               usage_note='The CC0 list is an available local source inventory, not proof every item is used by 295. Final generated prototypes must be audited separately.'))
    print('Reference ledger:', len(research), 'research references;', len(checked), 'verified existing CC0 files.')


def audit_source_routes() -> None:
    """Find source route/water crossings; this does not claim they are traversable."""
    import numpy as np
    layout = read_json(BASE / 'layout.json')
    places = {p['Id']: np.array([p['XZ']['x'], p['XZ']['y']], dtype=float)
              for p in layout['Places']}
    crossings = []
    def cross(a, b):
        return float(a[0] * b[1] - a[1] * b[0])
    for route in layout['Routes']:
        points = [places[route['From']]] + [np.array([b['x'], b['y']], dtype=float)
                  for b in route['Bends']] + [places[route['To']]]
        for river in layout['Drainages']:
            centre = [np.array([b['x'], b['y']], dtype=float) for b in river['Centreline']]
            for a, b in zip(points, points[1:]):
                for c, d in zip(centre, centre[1:]):
                    v, w = b - a, d - c
                    denominator = cross(v, w)
                    if abs(denominator) < 1e-8:
                        continue
                    t = cross(c - a, w) / denominator
                    u = cross(c - a, v) / denominator
                    if 0 <= t <= 1 and 0 <= u <= 1:
                        xz = np.round(a + t * v, 3).tolist()
                        if any(p['route'] == route['Id'] and p['river'] == river['Id']
                               and np.linalg.norm(np.array(p['xz']) - xz) < .01 for p in crossings):
                            continue
                        crossings.append(dict(route=route['Id'], river=river['Id'], xz=xz,
                                              role=route['Role'], required_ability=route['RequiredAbility'],
                                              vehicle=bool(route.get('GradeForVehicle', False))))
    scene = SOURCE / 'W_Demo_Compact_Reworld.unity'
    names, transforms = {}, {}
    for block in re.split(r'(?m)^--- !u!', scene.read_text(encoding='utf8'))[1:]:
        match = re.match(r'(\d+) &(\d+)', block)
        if not match:
            continue
        cls, key = match.groups()
        if cls == '1':
            name = re.search(r'  m_Name: (.*)', block)
            active = re.search(r'  m_IsActive: (\d+)', block)
            names[key] = dict(name=name[1] if name else '', active=bool(int(active[1])) if active else None)
        elif cls == '4':
            obj = re.search(r'm_GameObject: \{fileID: (\d+)', block)
            parent = re.search(r'm_Father: \{fileID: (\d+)', block)
            position = re.search(r'm_LocalPosition: (.*)', block)
            if obj and parent:
                transforms[key] = (obj[1], parent[1], position[1] if position else '')
    bridges = []
    for key, (obj, parent, position) in transforms.items():
        if not re.search('bridge|교량|다리', names.get(obj, {}).get('name', ''), re.IGNORECASE):
            continue
        chain, current, visited = [], key, set()
        while current in transforms and current not in visited:
            visited.add(current)
            obj, parent, position = transforms[current]
            chain.append(dict(**names.get(obj, {}), local_position=position))
            current = parent
        bridges.append(dict(hierarchy=chain, active_in_hierarchy=all(n.get('active') for n in chain)))
    result = dict(crossings=crossings, named_bridges=bridges,
                  source_scene_sha256=digest(scene),
                  note='Name-based scene inventory and 2D source centerline intersections only. Not proof of bridge absence under other names or collider connectivity. New water routes need a fresh crossing audit and actual walking checks.')
    write_json(OUT / 'source-route-audit.json', result)
    print('Source route crossings:', len(crossings), '| named bridge roots:', len(bridges),
          '| active:', sum(b['active_in_hierarchy'] for b in bridges))


def validate_generated(generated: Path | None = None) -> dict:
    """Check the exported terrain independently of the generator's assertions."""
    import numpy as np
    generated = generated or OUT / 'Generated'
    required = ['height.bytes', 'waterlevel.bytes', 'wet.bytes', 'protected.bytes',
                'hydro.json', 'diagnostics.json', 'layout.json', 'routes.json', 'crossings.json']
    initial = {name: digest(generated / name) for name in required}
    hydro = read_json(generated / 'hydro.json')
    diagnostics = read_json(generated / 'diagnostics.json')
    width, height, cell = hydro['Width'], hydro['Height'], hydro['Cell']
    shape = (height, width)
    terrain = np.fromfile(generated / 'height.bytes', dtype='<f4').reshape(shape)
    old = np.fromfile(BASE / 'height.bytes', dtype='<f4').reshape(shape)
    water = np.fromfile(generated / 'waterlevel.bytes', dtype='<f4').reshape(shape)
    wet = np.fromfile(generated / 'wet.bytes', dtype='u1').reshape(shape)
    protected = np.fromfile(generated / 'protected.bytes', dtype='u1').reshape(shape)
    checks = []
    def check(name, passed, **details):
        checks.append(dict(name=name, passed=bool(passed), **details))
    check('finite terrain and water raster', np.isfinite(terrain).all() and np.isfinite(water).all())
    check('source height matches baseline', diagnostics['SourceHashes']['height.bytes'] == digest(BASE / 'height.bytes'))
    check('diagnostic height hash current', diagnostics.get('GeneratedHeightSHA256') == initial['height.bytes'])
    check('diagnostic water hash current', diagnostics.get('GeneratedWaterSHA256') == initial['waterlevel.bytes'])
    check('lake selection matches output', abs(diagnostics['SelectedLake']['Level'] - hydro['Lake']['Level']) < 1e-5)
    changed_protected = int(np.count_nonzero((protected != 0) & (terrain.view('<u4') != old.view('<u4'))))
    check('protected terrain is byte-identical', changed_protected == 0 and np.count_nonzero(protected) > 0,
          samples=int(np.count_nonzero(protected)), changed=changed_protected)
    # Derive a second mask from accepted 293 route profiles, rather than trusting
    # a generator to choose the very mask against which its work is checked.
    accepted = np.zeros(shape, dtype=bool)
    accepted_paths = list((OUT.parent / 'Highlands293/Variants').glob('*/section.json'))
    for path in accepted_paths:
        profile = read_json(path)['profile']
        for point, span in zip(profile['points'], profile['widths']):
            radius = 30 + span * .5
            x, z = point['x'] / cell, point['z'] / cell
            r = radius / cell
            x0, x1 = max(0, int(np.floor(x-r))), min(width-1, int(np.ceil(x+r)))
            z0, z1 = max(0, int(np.floor(z-r))), min(height-1, int(np.ceil(z+r)))
            zz, xx = np.ogrid[z0:z1+1, x0:x1+1]
            accepted[z0:z1+1, x0:x1+1] |= (xx-x)**2 + (zz-z)**2 <= r*r
    independently_changed = int(np.count_nonzero(accepted & (terrain.view('<u4') != old.view('<u4'))))
    check('accepted 293 profiles plus 30m independently protected', independently_changed == 0 and len(accepted_paths) == 15,
          profiles=len(accepted_paths), samples=int(np.count_nonzero(accepted)), changed=independently_changed)
    outside = (water < -9000)
    # The exported level raster extends across dry shoreline cells so clipping
    # can find h=water intersections. It is not a runtime water-query mask.
    unsupported_wet = int(np.count_nonzero((wet > 0) & outside))
    inconsistent_dry = int(np.count_nonzero((wet == 0) & ~outside & (terrain < water - .015)))
    check('wet occupancy and shoreline support agree', unsupported_wet == 0 and inconsistent_dry == 0,
          unsupported_wet=unsupported_wet, dry_below_plane=inconsistent_dry,
          dry_clipping_support=int(np.count_nonzero((wet == 0) & ~outside)))
    buried = int(np.count_nonzero((wet > 0) & (terrain > water + .1)))
    check('water raster is not below terrain', buried == 0, samples=buried)
    level = hydro['Lake']['Level']
    lake_wet = (wet > 0) & (np.abs(water-level) < .01)
    unsupported_boundary = np.zeros(shape, dtype=bool)
    for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        unsupported_boundary |= lake_wet & np.roll(outside & (terrain < level-.02), (dz, dx), axis=(0, 1))
    unsupported_boundary[[0, -1], :] = False
    unsupported_boundary[:, [0, -1]] = False
    check('lake wet boundary has terrain or water support', not unsupported_boundary.any(),
          unsupported_nodes=int(unsupported_boundary.sum()))
    reaches = {r['Id']: r for r in hydro['Reaches']}
    def position(row):
        return np.array([row['Position'][key] for key in ('x', 'y', 'z')], dtype=float)
    for name, reach in reaches.items():
        points = np.array([position(row) for row in reach['Rows']])
        uphill = int(np.count_nonzero(np.diff(points[:, 1]) > .001))
        widths = [row['LeftWidth'] + row['RightWidth'] for row in reach['Rows']]
        check(name + ' downstream profile', uphill == 0 and np.isfinite(points).all(), uphill=uphill,
              min_width=float(min(widths)), max_width=float(max(widths)))
        xx=np.clip(points[:, 0]/cell,0,width-1.00001); zz=np.clip(points[:, 2]/cell,0,height-1.00001)
        ix=xx.astype(int); iz=zz.astype(int); u=xx-ix; v=zz-iz
        centre=(1-u)*(1-v)*terrain[iz,ix]+u*(1-v)*terrain[iz,ix+1]+(1-u)*v*terrain[iz+1,ix]+u*v*terrain[iz+1,ix+1]
        dry_centres=int(np.count_nonzero(centre > points[:, 1] + .05))
        check(name + ' actual terrain below water centre', dry_centres == 0, dry_centres=dry_centres,
              minimum_depth_m=float(np.min(points[:, 1]-centre)))
        parent = reach.get('ParentId')
        if parent == 'lake':
            error = min(abs(points[0, 1] - hydro['Lake']['Level']), abs(points[-1, 1] - hydro['Lake']['Level']))
            check(name + ' lake endpoint level', error < .02, error_m=float(error))
        elif parent:
            if parent not in reaches:
                check(name + ' parent exists', False, parent=parent)
                continue
            parent_points = np.array([position(row) for row in reaches[parent]['Rows']])
            nearest = None
            for endpoint in (points[0], points[-1]):
                for a, b in zip(parent_points, parent_points[1:]):
                    direction = b[[0, 2]] - a[[0, 2]]
                    t = np.clip(np.dot(endpoint[[0, 2]] - a[[0, 2]], direction) / max(1e-9, np.dot(direction, direction)), 0, 1)
                    projected = a + t * (b - a)
                    distance = float(np.linalg.norm(endpoint[[0, 2]] - projected[[0, 2]]))
                    candidate = (distance, float(abs(endpoint[1] - projected[1])))
                    if nearest is None or distance < nearest[0]:
                        nearest = candidate
            check(name + ' confluence geometry', nearest[0] < 4.01 and nearest[1] < .03,
                  gap_m=nearest[0], level_error_m=nearest[1])
    triangles = 0
    mesh_hashes = {}
    invalid, degenerate, downward = 0, 0, 0
    for name in hydro['ChunkFiles']:
        path = generated / name
        mesh_hashes[name] = digest(path)
        mesh = read_json(path)
        vertices = np.array([[p[k] for k in ('x', 'y', 'z')] for p in mesh['Vertices']], dtype=float)
        indices = np.array(mesh['Triangles'], dtype=np.int64)
        if not np.isfinite(vertices).all() or indices.size % 3 or indices.min(initial=0) < 0 or indices.max(initial=-1) >= len(vertices):
            invalid += 1
            continue
        faces = vertices[indices.reshape((-1, 3))]
        area = np.cross(faces[:, 1] - faces[:, 0], faces[:, 2] - faces[:, 0])[:, 1]
        triangles += len(faces)
        degenerate += int(np.count_nonzero(np.abs(area) < .00001))
        downward += int(np.count_nonzero(area < -.00001))
    check('water meshes finite and indexed', invalid == 0, invalid_chunks=invalid, chunks=len(mesh_hashes), triangles=triangles)
    check('water mesh top faces', degenerate == 0 and downward == 0, degenerate=degenerate, downward=downward)
    check('triangle count matches generator', triangles == diagnostics.get('WaterTriangles'),
          independently_counted=triangles, generator_count=diagnostics.get('WaterTriangles'))
    final = {name: digest(generated / name) for name in required}
    check('generation stayed stable during independent validation', initial == final)
    delta = terrain.astype(float) - old
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(), passed=all(c['passed'] for c in checks),
                  checks=checks, input_sha256=initial, water_mesh_sha256=mesh_hashes,
                  metrics=dict(changed_samples=int(np.count_nonzero(delta)), total_samples=terrain.size,
                               max_cut_m=float(max(0, -delta.min())), max_fill_m=float(max(0, delta.max())),
                               cut_m3=float(np.maximum(-delta, 0).sum() * cell ** 2),
                               fill_m3=float(np.maximum(delta, 0).sum() * cell ** 2),
                               water_area_km2=float(np.count_nonzero(wet) * cell ** 2 / 1e6)),
                  limits='Offline export checks only. Unity rendering/query parity, physical walking, navigation, Play and art approval require fresh 295 evidence.')
    report = 'independent-checks.json' if generated == OUT / 'Generated' else 'independent-checks-' + generated.name + '.json'
    result['generated_directory'] = relative(generated)
    write_json(OUT / report, result)
    for item in checks:
        print(('PASS ' if item['passed'] else 'FAIL ') + item['name'])
    if not result['passed']:
        raise RuntimeError('Independent generated-data validation failed.')
    return result


def review() -> None:
    """Build a review from evidence actually present; missing checks stay pending."""
    cameras_file = OUT / 'cameras.json'
    cameras = read_json(cameras_file)['Views'] if cameras_file.exists() else []
    kaesong_file = OUT / 'cameras-kaesong.json'
    if kaesong_file.exists():
        cameras += read_json(kaesong_file)['Views']
    pairs, captures = [], []
    from PIL import Image
    terrain_file = OUT / 'Generated/height.bytes'
    current_height = digest(terrain_file) if terrain_file.exists() else None
    water_file = OUT / 'Generated/waterlevel.bytes'
    current_water = digest(water_file) if water_file.exists() else None
    before_file = OUT / 'InputSurface294/height.bytes'
    baseline_height = digest(before_file) if before_file.exists() else None
    candidate_art = ROOT / 'Oheangbu/Assets/_Project/Art/World/Watershed295'
    appearance_files = [p for folder in ('Materials', 'Dressing', 'Shaders')
                        for p in (candidate_art / folder).rglob('*') if p.is_file() and p.suffix != '.meta']
    appearance_time = max((p.stat().st_mtime for p in appearance_files), default=0)
    for index, camera in enumerate(cameras):
        for raw in (False, True):
            name = f"{index}-{camera['Id']}" + ('-raw' if raw else '') + '.png'
            before, after = OUT / 'Captures/before' / name, OUT / 'Captures/after' / name
            if not before.is_file() or not after.is_file():
                continue
            if after.stat().st_mtime + .01 < appearance_time:
                continue
            receipt_file = Path(str(after) + '.json')
            if receipt_file.exists():
                receipt = read_json(receipt_file)
                if receipt['HeightSHA256'] != current_height or receipt.get('WaterSHA256') != current_water or receipt['View']['Eye'] != camera['Eye'] or receipt['View']['Target'] != camera['Target']:
                    continue
            elif terrain_file.exists() and after.stat().st_mtime < terrain_file.stat().st_mtime:
                # A capture made before the current terrain was produced is
                # historical evidence and must not illustrate the current map.
                continue
            before_receipt = Path(str(before) + '.json')
            if before_receipt.exists():
                receipt = read_json(before_receipt)
                if receipt['Stage'] != 'before' or receipt['HeightSHA256'] != baseline_height or receipt['View']['Eye'] != camera['Eye'] or receipt['View']['Target'] != camera['Target']:
                    continue
            for stage, path in [('before', before), ('after', after)]:
                with Image.open(path) as bitmap:
                    if bitmap.size != (1920, 1080):
                        raise RuntimeError('Unexpected capture dimensions: ' + str(path))
                captures.append(dict(view=camera['Id'], raw=raw, stage=stage,
                                     path=relative(path), sha256=digest(path), eye=camera['Eye'], target=camera['Target']))
            pairs.append(dict(id=camera['Id'], raw=raw, label=camera['Label'] + (' · 지형 원형' if raw else ''),
                              before=before.relative_to(OUT).as_posix(), after=after.relative_to(OUT).as_posix()))
    write_json(OUT / 'captures.json', captures)
    evidence = []
    for name, label in [('protection-check.json', '기존 후보·고산·원본 보존'),
                        ('independent-checks.json', '생성 데이터 독립 검사'), ('checks.txt', 'Unity 구조·수면·컬링·본선 지지'),
                        ('reproduction.json', '별도 폴더에서 생성 재현'),
                        ('supports-idempotence.json', '교량 보정 반복 후 동일성'),
                        ('Analysis/dressing-tone-independent.json', '호안 배치·지면색·수관 마스크 보호'),
                        ('walk-first-visit.txt', '첫 방문 실제 크기 캡슐 보행'),
                        ('walk-highlands293.txt', '승인 고산 계단·보행교와 주 등산로 캡슐 보행'),
                        ('navigation.txt', '새 충돌 기반 길찾기'),
                        ('actor-navigation.txt', '배치된 배우·순찰 지지와 길찾기'),
                        ('Mum/checks.json', '뭄 작도·형성·생명주기 자동 검사'),
                        ('Mum/play-checks.json', '후보 Play의 뭄·휴식·낙사·저장 복귀'),
                        ('Mum/terrain-play-checks.json', '철옹 실제 양안에서 뭄 생성·왕복·정리'),
                        ('Mum/hyeongang-play-checks.json', '현강 실제 양안에서 뭄 생성·왕복·정리')]:
        path = OUT / name
        if not path.is_file():
            evidence.append((label, '미실행', None))
            continue
        text = path.read_text(encoding='utf-8-sig')
        if path.suffix == '.json':
            item = json.loads(text)
            state = ('PASS' if item.get('passed') is True else
                     f"{len(item['passed'])} PASS · {len(item.get('failed', []))} FAIL"
                     if isinstance(item.get('passed'), list) else 'FAIL')
            if 'status' in item and 'checks' in item and 'failures' in item:
                state = f"{item['status']} · 검사 {len(item['checks'])} · 실패 {len(item['failures'])}"
            recorded_height = (item.get('input_sha256', {}).get('height.bytes') or item.get('expected_sha256', {}).get('height.bytes')
                               or item.get('height_sha256') or item.get('terrainSites', {}).get('sourceHeightSha256'))
            if recorded_height and recorded_height != current_height:
                state = '이전 후보 결과 · 현재 지형 재검사 필요'
            if name == 'Analysis/dressing-tone-independent.json' and path.stat().st_mtime + .01 < appearance_time:
                state += ' · 후속 변경 후 재검사 필요'
        else:
            passed = len(re.findall(r'^PASS\b', text, re.M))
            failed = len(re.findall(r'^FAIL\b', text, re.M))
            if name == 'walk-highlands293.txt':
                passed = len(re.findall(r': PASS;', text))
                failed = len(re.findall(r': FAIL;', text))
            state = f'{passed} PASS · {failed} FAIL'
            relevant_time = appearance_time if name == 'checks.txt' else 0
            if name in ('walk-first-visit.txt', 'navigation.txt'):
                relevant_time = (OUT / 'Generated/routes.json').stat().st_mtime
            if path.stat().st_mtime + .01 < relevant_time:
                state += ' · 후속 변경 후 재검사 필요'
        evidence.append((label, state, name))
    checks_html = ''.join('<tr><td>'+html.escape(label)+'</td><td>'+html.escape(state)+'</td><td>'+('<a href="'+file+'">원문</a>' if file else '')+'</td></tr>' for label,state,file in evidence)
    perf_html = ''
    for path in sorted((OUT / 'Perf').glob('*/frame-times.txt')) if (OUT / 'Perf').exists() else []:
        stale = path.stat().st_mtime + .01 < max(appearance_time, terrain_file.stat().st_mtime)
        title = path.parent.name + (' · 후속 변경 전 측정' if stale else '')
        walk = path.parent / 'walk-play.txt'
        walk_text = walk.read_text(encoding='utf-8-sig') if walk.exists() else '실제 Play 보행 결과 미확인'
        perf_html += '<details><summary>'+html.escape(title)+'</summary><pre>'+html.escape(path.read_text(encoding='utf8'))+'</pre><pre>'+html.escape(walk_text)+'</pre></details>'
    if not perf_html:
        perf_html = '<p>새 Play 성능 측정 미실행. 이전 #293 수치를 이번 결과로 인용하지 않는다.</p>'
    kaesong_html = ''
    if (OUT / 'KaesongSources.json').exists():
        kaesong_html = ('<h2>개성풍 침수 도성</h2><p>만월대의 산지 지형·석축 단·비대칭 배치를 참고한 가상 도성이다. '
                        '목조건물은 보유한 조선시대 제주목 관아 혼화각 에셋을 이 배치에 맞게 활용했다. '
                        '<a href="KaesongSources.json">건축 해석·사용 에셋</a> · <a href="KaesongResearch.json">조사 출처</a></p>')
    mum_html = ''
    for mum_name, capture_prefix in [('terrain-play-checks.json', 'terrain'), ('hyeongang-play-checks.json', 'hyeongang')]:
        mum_path = OUT / 'Mum' / mum_name
        if mum_path.exists():
            mum = read_json(mum_path)
            if mum.get('finished') and mum.get('restored') and mum.get('terrainMode') and mum.get('status') == 'PASS' and mum.get('terrainSites', {}).get('sourceHeightSha256') == current_height:
                start, end = mum.get('terrainStart', {}), mum.get('terrainEnd', {})
                site = next((s for group in ('sites', 'boundedJoinCandidates', 'rejectedCandidates')
                             for s in mum.get('terrainSites', {}).get(group, []) if s.get('id') == mum.get('terrainAcceptedSite')), {})
                realm = {'Cheolong': '철옹', 'Hyeongang': '현강', 'Jeokro': '적로', 'Hwanggyeong': '황경', 'Geumpyo': '금표'}.get(site.get('realm'), '후보 지역')
                span = sum((end.get(k, 0) - start.get(k, 0)) ** 2 for k in ('x', 'z')) ** .5
                hazards = mum.get('terrainHazards', [])
                drown_start = next((h['time'] for h in hazards if h.get('cause') == 'DeepWater' and h.get('drowning')), None)
                drown_death = next((h for h in hazards if h.get('cause') == 'DEATH:DeepWater'), None)
                fall_death = next((h for h in hazards if h.get('cause') == 'DEATH:Fall'), None)
                drown_seconds = f"{drown_death['time'] - drown_start:.3f}초" if drown_death and drown_start is not None else '기록 없음'
                fall_drop = (f"{mum['terrainHazardStart']['y'] - fall_death['feet']['y']:.3f}m"
                             if fall_death and mum.get('terrainHazardCause') == 'Fall' else '기록 없음')
                state = str(mum.get('status', '미확인'))
                mum_html += (f'<h2>{html.escape(realm)}에서 뭄과 환경 사망</h2><p>{html.escape(state)} · 검사 {len(mum.get("checks", []))} · '
                            f'실패 {len(mum.get("failures", []))}. {html.escape(realm)}의 실제 양안에서 {span:.2f}m 다리를 만들고 실제 플레이어 캡슐로 왕복했다. '
                            '격리된 시험 해금과 저장 슬롯을 사용했다. <a href="Mum/terrain-play-checks.json">Play 원문</a></p>')
                values = [('왕복 보행 시간', f'{mum.get("terrainWalkSeconds", 0):.2f}초'),
                          ('다리 지지 확인', f'{mum.get("terrainSupportedSamples", 0)}회'),
                          ('보행 중 최대 상판 높이 차', f'{mum.get("terrainMaxDeckError", 0):.3f}m'),
                          ('실제 익사 카운트다운', drown_seconds), ('낙사 전 실제 하강', fall_drop)]
                mum_html += '<table>' + ''.join('<tr><th>'+html.escape(k)+'</th><td>'+html.escape(v)+'</td></tr>' for k, v in values) + '</table>'
                mum_captures = []
                for filename, caption in [(f'{capture_prefix}-placement.png', '가까운 마른 강둑 · 작도 후 완성된 다리'),
                                          (f'{capture_prefix}-return.png', '건넌 강둑 · 돌아오기 전 반대편을 바라본 화면')]:
                    capture = OUT / 'Mum' / filename
                    receipt_path = Path(str(capture) + '.json')
                    if not capture.exists() or not receipt_path.exists():
                        continue
                    receipt = read_json(receipt_path)
                    if receipt.get('sourceHeightSha256') != current_height or receipt.get('site') != mum.get('terrainAcceptedSite'):
                        continue
                    if any(abs(receipt.get('bridgeStart', {}).get(k, float('inf')) - start.get(k, 0)) > .01 or
                           abs(receipt.get('bridgeEnd', {}).get(k, float('inf')) - end.get(k, 0)) > .01 for k in ('x', 'y', 'z')):
                        continue
                    link = capture.relative_to(OUT).as_posix()
                    mum_captures.append(dict(path=relative(capture), sha256=digest(capture), receipt=relative(receipt_path),
                                             receipt_sha256=digest(receipt_path), site=receipt['site'], height_sha256=current_height))
                    mum_html += '<figure><a href="'+link+'"><img src="'+link+'" alt="실제 강에서 생성한 뭄 다리"></a><figcaption>'+html.escape(caption)+' · <a href="'+link+'.json">실제 플레이어·카메라 기록</a></figcaption></figure>'
                write_json(OUT / 'Mum' / ('captures.json' if capture_prefix == 'terrain' else capture_prefix + '-captures.json'), mum_captures)
                mum_html += '<p>익사·낙사 후 실제 저장 지점 복귀와 임시 다리 정리를 확인한 자동 Play 검사다. 수동 작도 입력·최종 보스 획득 흐름은 별도다.</p>'
    refs = read_json(OUT / 'references.json') if (OUT / 'references.json').exists() else {'research': []}
    sources_html = ''.join('<li><a href="'+html.escape(r['url'],quote=True)+'">'+html.escape(r['title'])+'</a> — '+html.escape(r['lesson'])+'</li>' for r in refs['research'])
    document = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>현강 수계 #295 검토</title>
<style>*{box-sizing:border-box}body{margin:0;background:#ede9df;color:#28332e;font:16px/1.65 "Malgun Gothic",sans-serif}main{max-width:1480px;margin:auto;padding:32px 28px 70px}h1{font-size:34px;margin:6px 0}h2{margin-top:36px}p{max-width:1000px}a{color:#315d50}button{font:inherit;padding:8px 12px;background:transparent;border:1px solid #879c90;cursor:pointer}button[aria-pressed=true]{background:#314d42;color:white}nav{display:flex;flex-wrap:wrap;gap:8px;margin:20px 0 12px}.viewer{position:relative;aspect-ratio:16/9;background:#bbc4ba}.viewer img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}#before{clip-path:inset(0 50% 0 0)}#slider{width:100%;accent-color:#315d50}.tag{position:absolute;top:10px;background:#243f32cf;color:white;padding:4px 10px}.left{left:10px}.right{right:10px}table{border-collapse:collapse;width:100%}td,th{text-align:left;border-bottom:1px solid #aab5ab;padding:10px}figure{margin:20px 0}figure img{max-width:100%;max-height:800px;object-fit:contain}pre{white-space:pre-wrap;font-size:13px;background:#e2e3d9;padding:16px}.note{border-left:3px solid #637d70;padding-left:15px}details{border-bottom:1px solid #aab5ab;padding:10px 0}</style>
<main><small>오행부 · #295 · TEST</small><h1>현강 침수 분지와 자연 하천</h1><p>실제 한국 수변의 산줄기·절벽·자갈톱 관계를 적용한 별도 후보다. 기존 #294와 정본은 복구 원장으로 보호한다. 아래는 생성·구조·보행·시각·성능의 실제 증거이며 미실행 검사는 별도로 표시한다.</p>
<nav id="views"></nav><div class="viewer" id="viewer"><img id="after" alt="수정 후"><img id="before" alt="수정 전"><span class="tag left">#294</span><span class="tag right">#295</span></div><input id="slider" aria-label="수정 전 화면 비율" type="range" min="0" max="100" value="50"><p id="capture-state"></p>
<h2>수계와 절토·성토</h2><figure><a href="Generated/watershed-topdown.png"><img src="Generated/watershed-topdown.png" alt="새 수계 조감"></a><figcaption>생성 데이터의 전체 수계. 실제 Unity 시각 검토와 구분한다.</figcaption></figure><p><a href="Generated/hyeongang-topdown.png">현강 확대</a> · <a href="Generated/earthworks.png">절토·성토 지도</a> · <a href="Generated/diagnostics.json">생성 수치</a> · <a href="independent-checks.json">독립 검사</a></p>
__KAESONG__
<h2>검증</h2><p>첫 방문 자동 보행은 통로의 물리적 연결을 확인한다. 대화·보스전·문 개방을 잇는 전체 진행은 별도 검증 대상이다.</p><table><tr><th>항목</th><th>상태</th><th>증거</th></tr>__CHECKS__</table>
__MUM__
<h2>새 Play 측정</h2>__PERF__<p class="note">Editor 수치는 독립 빌드·120fps 달성이나 사용자 미술 승인을 뜻하지 않는다. 수동 플레이와 실제 진행 검사는 별도다.</p>
<h2>참조 자료</h2><ul>__SOURCES__</ul><p>위 이미지·게임 화면은 연구 참조다. 게임에 사용하는 에셋의 라이선스와 분리했다. <a href="references.json">출처·라이선스 원장</a> · <a href="captures.json">캡처 해시</a> · <a href="Baseline/complete.json">복구 기준</a> · <a href="source-route-audit.json">기존 동선 감사</a></p></main>
<script>const pairs=__PAIRS__;const nav=document.querySelector('#views'),before=document.querySelector('#before'),after=document.querySelector('#after');function select(i){before.src=pairs[i].before;after.src=pairs[i].after;[...nav.children].forEach((b,k)=>b.setAttribute('aria-pressed',i===k));document.querySelector('#capture-state').textContent=pairs[i].label+' · 동일한 카메라 위치/방향';}pairs.forEach((p,i)=>{let b=document.createElement('button');b.textContent=p.label;b.onclick=()=>select(i);nav.appendChild(b)});function selectHash(){const key=decodeURIComponent(location.hash.slice(1));const found=pairs.findIndex(p=>!p.raw&&(p.id===key||p.label===key));select(found>=0?found:0)}if(pairs.length){selectHash();window.addEventListener('hashchange',selectHash)}else{document.querySelector('#viewer').hidden=true;document.querySelector('#slider').hidden=true;document.querySelector('#capture-state').textContent='동일 시점 전후 캡처 미실행';}document.querySelector('#slider').oninput=e=>before.style.clipPath=`inset(0 ${100-e.target.value}% 0 0)`;</script></html>'''
    document = document.replace('__CHECKS__', checks_html).replace('__PERF__', perf_html).replace('__SOURCES__', sources_html).replace('__PAIRS__', json.dumps(pairs, ensure_ascii=False)).replace('__KAESONG__', kaesong_html).replace('__MUM__', mum_html)
    (OUT / 'REVIEW.html').write_text(document, encoding='utf8')
    print('Review generated from', len(pairs), 'capture pairs; absent evidence remains unverified.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('baseline', 'verify', 'references', 'audit-source-routes', 'validate-generated', 'reproduce', 'review'))
    args = parser.parse_args()
    {'baseline': baseline, 'verify': verify, 'references': references,
     'audit-source-routes': audit_source_routes, 'validate-generated': validate_generated, 'reproduce': reproduce,
     'review': review}[args.command]()
