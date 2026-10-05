# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 track A - one entry point for the offline map assets.

    python Tools/Art/map308_bake.py --stage base            -> Tools/Unity/Stage308_map/_ProjectAssets/Art/UI/UI308/Map (the scenes at base height)
    python Tools/Art/map308_bake.py --stage 1a              -> Tools/Unity/Stage308_map/Bundles/1a (the scenes after the cliff ledger applied 1a)
    python Tools/Art/map308_bake.py --stage 1b              -> Tools/Unity/Stage308_map/Bundles/1b (after the 1b tiles + the long wall: height_p1b,
                                                               the 1b plan lines, the two wings of the long wall; map308_lib.STAGES)
    options: --no-twin (skip the concept sheets and the legibility json), --no-guard (skip resource_guard --wait)
ALL bundles are kept side by side; `MapOverhaul308 apply` takes the one whose stage is the stage the scenes carry.

Steps (each heavy step waits for Tools/resource_guard.py first; nothing here uses the GPU, the editor or Play):
    1  cartography308.py --stage S --out <bundle>  -> the bundle, files 1-8
    2  cartography308.py --stage S --out <temp>    -> the same bake in a second process; every sha256 must match (AC-O1)
    3  map308_check.py --bundle <bundle>           -> contract / caps / road accuracy / role independence / kit binding / stale inputs
    4  map308_twin.py --bundle <bundle>            -> concept sheets + map308_legibility.json (AC-O6, O7, O9, O13): Art/UI308/Map
                                                      for base, Art/UI308/Map/stage_<S> for another stage
    5  manifest                                    -> map308_manifest.json: Tools/Unity/Stage308_map/ for base, in the bundle
                                                      folder for another stage (map308_check.json likewise)
The frames are the theme kit's sprites (SPEC-UI-THEME-308): the bundle has no frame picture; the manifest records the kit
atlas sha seen at bake time. Writes nothing under Oheangbu/.
"""
import argparse, json, shutil, struct, subprocess, sys, tempfile, time
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_lib as M

PY = sys.executable
BUNDLE = ['map308_terrain.png', 'map308_pattern.png', 'map308_strokes.bytes', 'map308_stroke_atlas.png', 'map308_icons_L.png',
          'map308_icons_S.png', 'map308_reveal_regions.bytes', 'map308_notation.json']
CONCEPT = ['map308_roads_abc.png', 'map308_notation_sheet.png', 'map308_mini_mock.png', 'map308_full_mock.png', 'map308_legibility.json']
DESCR = {
    'map308_terrain.png': ('R slope wash 22-45 deg, G forest density, B water signed distance (128 = shore, +-32 m), A elevation y / 400 m', 'linear, mips, bilinear, clamp'),
    'map308_pattern.png': ('R tree dots (threshold levels), G ripples, B rock strokes, A wash grain; tileable', 'linear, mips, repeat'),
    'map308_strokes.bytes': ("'MS08' v1: stroke table, points, 250 m bin table, references, CRC32", 'TextAsset'),
    'map308_stroke_atlas.png': ('8 brush rows x 64 px: A ink, R paper underlay, G B 0', 'linear, mips, repeat U / clamp V'),
    'map308_icons_L.png': ('8 x 8 glyph cells of 64 px: R ink, G paper plate, B second ink (player only), A = max(R, G)', 'linear, no mips, clamp'),
    'map308_icons_S.png': ('8 x 8 glyph cells of 32 px: same cell numbers and channels', 'linear, no mips, clamp'),
    'map308_reveal_regions.bytes': ('125 x 188 uint8, south row first: 0 common, 1-254 closed region, 255 no standable ground', 'TextAsset'),
    'map308_notation.json': ('notation tables, inputs and outputs sha256, stage', 'TextAsset -> MapNotation308SO'),
}


def run(args, guard=True):
    if guard:
        subprocess.run([PY, str(M.TOOLS.parent / 'resource_guard.py'), '--wait'], check=True)
    t = time.time()
    r = subprocess.run([PY] + args)
    return r.returncode, time.time() - t


def describe(p):
    d = dict(file=p.name, bytes=p.stat().st_size, sha256=M.sha256(p))
    if p.suffix == '.png':
        im = Image.open(p); d['size'] = list(im.size); d['channels'] = im.mode
    elif p.name == 'map308_strokes.bytes':
        b = p.read_bytes(); ns, npnt = struct.unpack('<II', b[32:40]); d['size'] = [ns, npnt]; d['channels'] = 'strokes, points'
    elif p.name == 'map308_reveal_regions.bytes':
        d['size'] = [M.FOG_W, M.FOG_H]; d['channels'] = 'uint8'
    if p.name in DESCR: d['content'], d['import'] = DESCR[p.name]
    return d


def main():
    M.utf8()
    ap = argparse.ArgumentParser(description='SPEC-MAP-OVERHAUL-308 offline assets (track A)')
    ap.add_argument('--stage', default='base', choices=sorted(M.STAGES)); ap.add_argument('--no-twin', action='store_true'); ap.add_argument('--no-guard', action='store_true')
    a = ap.parse_args(); g = not a.no_guard; T = {}; t0 = time.time()
    out = M.bundle_dir(a.stage); side = M.STAGE_DIR if a.stage == 'base' else out; concept = M.concept_dir(a.stage)
    print(f'[map308_bake] stage {a.stage} -> {M.rel(out)}')
    rc, T['bake'] = run([str(M.TOOLS / 'cartography308.py'), '--stage', a.stage, '--out', str(out)], g)
    if rc: sys.exit(rc)
    tmp = Path(tempfile.mkdtemp(prefix='map308_run2_'))
    try:
        rc, T['bake_again'] = run([str(M.TOOLS / 'cartography308.py'), '--stage', a.stage, '--out', str(tmp)], g)
        if rc: sys.exit(rc)
        diff = [n for n in BUNDLE if M.sha256(out / n) != M.sha256(tmp / n)]
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print('[map308_bake] two runs: ' + ('IDENTICAL sha256 for all 8 files' if not diff else 'DIFFERENT: ' + ', '.join(diff)))
    if diff: sys.exit(1)
    rc, T['check'] = run([str(M.TOOLS / 'map308_check.py'), '--bundle', str(out), '--report', str(side / 'map308_check.json')], g)
    check_ok = rc == 0
    twin_ok = None
    if not a.no_twin:
        rc, T['twin'] = run([str(M.TOOLS / 'map308_twin.py'), '--bundle', str(out), '--out', str(concept)], g); twin_ok = rc == 0
    note = json.loads((out / 'map308_notation.json').read_text(encoding='utf-8'))
    kit = M.THEME_DIR / 'theme308_atlas.png'; kitj = M.THEME_DIR / 'theme308_atlas.json'
    man = dict(
        id='map308_manifest', spec='SPEC-MAP-OVERHAUL-308', track='A (offline assets)', contractVersion=M.CONTRACT_VERSION, stage=a.stage,
        stageFolder=M.rel(out), deployTo='Oheangbu/Assets/_Project/Art/UI/UI308/Map/', bakedFor=note['bakedFor'],
        twoRunsIdentical=True, checkOk=check_ok, twinOk=twin_ok,
        bundle=[describe(out / n) for n in BUNDLE],
        inputs=note['inputs'],
        tools=[dict(file=M.rel(M.TOOLS / n), sha256=M.sha256(M.TOOLS / n)) for n in
               ('map308_bake.py', 'cartography308.py', 'map308_lib.py', 'map308_art.py', 'map308_ridges.py', 'map308_check.py', 'map308_twin.py')],
        otherBundles={s: M.rel(M.bundle_dir(s)) for s in sorted(M.STAGES) if s != a.stage and (M.bundle_dir(s) / 'map308_notation.json').exists()},
        frames=dict(source='theme kit (SPEC-UI-THEME-308): no frame picture in this bundle', slots=note['frames']['slots'],
                    kitAtlas=dict(path=M.rel(kit), sha256=M.sha256(kit) if kit.exists() else None),
                    kitAtlasJson=dict(path=M.rel(kitj), sha256=M.sha256(kitj) if kitj.exists() else None),
                    note='the kit sha is recorded at bake time only; the bundle does not depend on it. Deploy the kit FIRST: '
                         'MapOverhaul308 apply binds the frames by cell name from the kit atlas it finds'),
        concept=[describe(concept / n) for n in CONCEPT if (concept / n).exists()],
        counts=note['counts'])
    sha = M.write_text(side / 'map308_manifest.json', M.dumps(man))
    print(f'[map308_bake] manifest {M.rel(side / "map308_manifest.json")} {sha[:16]}')
    for b in man['bundle']: print(f"  {b['file']:28s} {b['bytes']:>9d} B  {b['sha256']}")
    print('[map308_bake] seconds: ' + ', '.join(f'{k} {v:.1f}' for k, v in T.items()) + f', total {time.time() - t0:.1f}')
    print(f"[map308_bake] check {'ok' if check_ok else 'FAILED'}" + ('' if twin_ok is None else f", twin {'ok' if twin_ok else 'FAILED'}"))
    sys.exit(0 if check_ok and twin_ok is not False else 1)


if __name__ == '__main__':
    main()
