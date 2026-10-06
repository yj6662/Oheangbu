# -*- coding: utf-8 -*-
"""SPEC-MAP-OVERHAUL-308 track B - static checks of the stage code and shaders (no editor, no Play, nothing heavy).

    python Tools/Art/map308_bcheck.py            every check, report -> Art/UI308/Map/bcheck308.json
    python Tools/Art/map308_bcheck.py --live     only: are the live files still the ones the stage copies were made from?
    python Tools/Art/map308_bcheck.py --cginc <MapFog308.cginc> [--report <file.json>]
                                                 read the fog function from THAT file instead of the stage copy (the live file
                                                 after a deploy, a stage file before one); --report keeps the shared report as it is

What it proves offline (the rest needs the editor or Play, see Tools/Unity/Stage308_map/README.md):
  live      the seven live files the stage replaces still equal Original/*.orig (else the stage copies must be re-made)
  AC-O12    no mutable static field and no singleton in the new run-time code or in the lines added to existing files
  AC-O8     no emission / HDR property in the new shaders or in the lines added to the paper shader; outputs are saturated
  shaders   preprocessor / brace balance, every #308 property has its uniform and the other way round, _MAP308 is a
            multi_compile keyword, and MapFog308.cginc carries the paper shader's own KnownPaper body unchanged
  #214      strips are multiplied by the paper's own crisp walked edge (MapFog308_Edge), the paper's terrain terms sit under
            `shown`, the ink rim lies inside the edge; the edge's numbers in the shaders, the notation asset's defaults and
            the staged bundles are the same. MapFog308_Edge is read in the formula the cginc carries (`MAPFOG308_E_FLOOR` in its
            text = formula F of #308 map fix 2, else the threshold formula):
              threshold  field = cover - (p.x + p.y x noise); its numbers are the notation's (_M308Edge / _S308Edge)
              F          field = min(depth, KCOVER x cover) - theta; its numbers are the cginc's own `#define MAPFOG308_E_*`
                         lines, checked here: FROM > 0, FROM + SPAN < 1, FLOOR >= .809 x KCOVER, and no notation value (p) is read
  icons     the icon shader and MapGlyph308Graphic carry the second ink layer (B channel) the bake writes
  contract  PersistentMinimap / MiniRoot / TwiceFoldedHanji / PrintedMapWindow / MapInput are still built, no Mask under the
            minimap, HudController.cs and the recognition files are not in the stage
Exit code 0 = every check holds.
"""
import hashlib, json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / 'Tools/Unity/Stage308_map'
LIVE = ROOT / 'Oheangbu/Assets/_Project'
UI = 'Scripts/App/World/UI'
REPLACED = {  # stage copy -> live file
    'App/World/UI/HudMinimap304.cs': UI + '/HudMinimap304.cs',
    'App/World/UI/WorldMapPresenter.cs': UI + '/WorldMapPresenter.cs',
    'App/World/UI/WorldMapPresenter.Mini306.cs': UI + '/WorldMapPresenter.Mini306.cs',
    'App/World/UI/WorldMapPresenter.Marks304.cs': UI + '/WorldMapPresenter.Marks304.cs',
    'App/World/UI/WorldMapPresenter.Page304.cs': UI + '/WorldMapPresenter.Page304.cs',
    'App/World/UI/MapStyle304SO.cs': UI + '/MapStyle304SO.cs',
    '_ProjectAssets/Resources/WorldMap/PaperMapSurface.shader': 'Resources/WorldMap/PaperMapSurface.shader',
}
NEW_RUNTIME = ['MapNotation308SO.cs', 'MapStrokes308Data.cs', 'MapStrokes308Mesh.cs', 'MapStrokes308Graphic.cs', 'MapGlyph308Graphic.cs',
               'WorldMapPresenter.Map308.cs', 'HudMinimap304.Map308.cs']
SHADERS = STAGE / '_ProjectAssets/Art/UI/UI308/Map/Shaders'
PAPER = STAGE / '_ProjectAssets/Resources/WorldMap/PaperMapSurface.shader'
RECOGNITION = ['DrawingInputController.cs', 'RecognitionPipeline.cs', 'JamoMatcher.cs', 'HangulComposer.cs', 'JamoTemplateLibrarySO.cs']
# edge formula F (#308 map fix 2): the 14 numbers of MapFog308_Edge are #define lines of the cginc, not notation values
EDGE_F_NAMES = ('FROM', 'SPAN', 'KCOVER', 'FLOOR', 'D1', 'D2', 'HALF1', 'HALF2', 'PHASE', 'PK', 'RK', 'RIM0', 'RIM1', 'RIMBREAK')
# #214: the caller anti-aliases the edge over 1 screen px, and across a lattice corner KCOVER x cover climbs at up to 1.618 x KCOVER
# per cell (heading 31.7 deg). Half of that is the least FLOOR (screen px) that keeps the edge off an unwalked pixel: .809 x KCOVER
# = 1.2135 at KCOVER 1.5 (Art/UI308/Map/mapfix2/judge_leak.md; with the threshold taken away FLOOR 1.2 leaks 4 px, 1.0 leaks 8 px:
# Tools/Unity/Stage308_mapfix2/Offline/mapfix2_final_checks.py repro).
EDGE_F_FLOOR_PER_KCOVER = .809
sys.path.insert(0, str(Path(__file__).resolve().parent))
import map308_edge3 as E3       # #308 map 3: text lines and constant rules of a cginc that has `#define MAPFOG308_E_GATE0`

checks = []


def check(ok, label):
    checks.append(dict(ok=bool(ok), label=label)); print(('ok      ' if ok else 'FAILED  ') + label)


def text(p):
    return Path(p).read_bytes().decode('utf-8-sig').replace('\r\n', '\n')


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def added_lines(stage_rel):
    """lines of the stage copy that are not in its .orig (multiset difference of the stripped lines: a re-indented line is not new)."""
    orig = text(STAGE / 'Original' / (Path(stage_rel).name + '.orig')).split('\n'); new = text(STAGE / stage_rel).split('\n')
    pool = {}
    for l in orig: pool[l.strip()] = pool.get(l.strip(), 0) + 1
    out = []
    for l in new:
        if pool.get(l.strip(), 0) > 0: pool[l.strip()] -= 1
        else: out.append(l)
    return out


FIELD = re.compile(r'^\s*(?:(?:public|private|internal|protected)\s+)*static\s+(?!readonly\b|class\b|partial\b)(?:[\w<>\[\],\.\?]+\s+)+(\w+)\s*(=[^>]|;|,)')
READONLY = re.compile(r'^\s*(?:(?:public|private|internal|protected)\s+)*static\s+readonly\s+([\w<>\[\],\.\?]+)\s+(\w+)')


def static_scan(lines):
    mutable, readonly = [], []
    for l in lines:
        s = l.split('//')[0]
        if 'static' not in s or 'const ' in s: continue
        m = READONLY.match(s)
        if m: readonly.append((m.group(1), m.group(2))); continue
        m = FIELD.match(s)
        if m and '(' not in s.split('=')[0]: mutable.append(s.strip()[:120])
    return mutable, readonly


def balance(src):
    depth = 0; low = 0
    for l in src.split('\n'):
        t = l.strip()
        if t.startswith('#if'): depth += 1
        elif t.startswith('#endif'): depth -= 1
        low = min(low, depth)
    code = re.sub(r'//[^\n]*', '', src)
    return depth, low, code.count('{') - code.count('}'), code.count('(') - code.count(')')


def normalise(body):
    out = []
    for l in body.split('\n'):
        l = l.split('//')[0].strip()
        if l: out.append(re.sub(r'\s+', '', l))
    return '\n'.join(out)


def edge_defines(src):
    """the `#define MAPFOG308_E_<name> <number | float2(a,b)>` lines of a cginc -> {name: float | (float, float)}"""
    out = {}
    for m in re.finditer(r'^[ \t]*#define[ \t]+MAPFOG308_E_(\w+)[ \t]+(float2\(([^)]*)\)|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)', src, re.M):
        out[m.group(1)] = tuple(float(v) for v in m.group(3).split(',')) if m.group(3) else float(m.group(2))
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    live_only = '--live' in sys.argv
    arg = lambda name: Path(sys.argv[sys.argv.index(name) + 1]) if name in sys.argv else None
    cginc = arg('--cginc') or SHADERS / 'MapFog308.cginc'      # default: the stage copy next to the two shaders, as before
    # #308 map 3: the paper shader deployed TOGETHER with that cginc. --paper <file> names it; else the one that lies in the same tree as the
    # cginc (<tree>/Art/UI/UI308/Map/Shaders/MapFog308.cginc <-> <tree>/Resources/WorldMap/PaperMapSurface.shader: the live project, or a
    # stage's _ProjectAssets); else the Stage308_map copy. Only the #214 MapFog308_Edge row reads it.
    cg_tree = Path(cginc).resolve().parents[5] if len(Path(cginc).resolve().parents) > 5 else None
    pair = arg('--paper') or (cg_tree / 'Resources/WorldMap/PaperMapSurface.shader' if cg_tree is not None else PAPER)
    if not Path(pair).is_file(): pair = PAPER
    # ---- live drift
    drift = []
    for stage_rel, live_rel in REPLACED.items():
        orig = STAGE / 'Original' / (Path(stage_rel).name + '.orig'); live = LIVE / live_rel
        same = live.exists() and orig.exists() and sha(live) == sha(orig)
        print(('SAME    ' if same else 'CHANGED ') + live_rel)
        if not same: drift.append(live_rel)
    check(not drift, 'live: the %d replaced files still equal Original/*.orig%s' % (len(REPLACED), '' if not drift else ' - CHANGED: ' + ', '.join(drift)))
    if live_only:
        print('CHECK ' + ('ok' if not drift else 'FAILED: re-make the stage copies from the new live files')); sys.exit(0 if not drift else 1)

    # ---- AC-O12 static state
    mutable, readonly = [], []
    for name in NEW_RUNTIME:
        m, r = static_scan(text(STAGE / 'App/World/UI' / name).split('\n'))
        mutable += [name + ': ' + x for x in m]; readonly += [(name,) + x for x in r]
    for stage_rel in REPLACED:
        if not stage_rel.endswith('.cs'): continue
        m, r = static_scan(added_lines(stage_rel))
        mutable += [Path(stage_rel).name + ' (added): ' + x for x in m]; readonly += [(Path(stage_rel).name,) + x for x in r]
    check(not mutable, 'AC-O12 mutable static fields in the new run-time code and the added lines: %d%s' % (len(mutable), '' if not mutable else ' -> ' + ' | '.join(mutable)))
    allowed = {'int', 'Comparison<Range>'}   # shader property ids and one ordering delegate: written once, never changed
    odd = [r for r in readonly if r[1] not in allowed]
    check(not odd, 'AC-O12 static readonly fields are immutable values only (%s)%s'
          % (', '.join(sorted({'%s %s' % (r[1], r[2]) for r in readonly})) or 'none', '' if not odd else ' - not on the allowed list: %s' % odd))
    singleton = []
    for name in NEW_RUNTIME:
        if re.search(r'static\s+\w+\s+Instance\b', text(STAGE / 'App/World/UI' / name)): singleton.append(name)
    check(not singleton, 'AC-O12 no singleton (static Instance) in the new run-time code')
    calls = [n for n in NEW_RUNTIME if 'PlaytestUiRoot.Instance' in text(STAGE / 'App/World/UI' / n)]
    check(not calls, 'the new run-time code reaches no global (PlaytestUiRoot.Instance): %s' % (calls or 'none'))

    # ---- shaders
    stroke = text(SHADERS / 'MapStroke308.shader'); icon = text(SHADERS / 'MapIcon308.shader'); fog = text(cginc); paper = text(PAPER)
    paper_added = '\n'.join(added_lines('_ProjectAssets/Resources/WorldMap/PaperMapSurface.shader'))
    for name, src in (('MapStroke308.shader', stroke), ('MapIcon308.shader', icon), ('PaperMapSurface.shader', paper), ('MapFog308.cginc', fog)):
        d, low, braces, parens = balance(src)
        check(d == 0 and low == 0 and braces == 0 and parens == 0, 'shader %s: #if / #endif, braces and parentheses balance' % name)
    for name, src in (('MapStroke308.shader', stroke), ('MapIcon308.shader', icon), ('PaperMapSurface.shader (added lines)', paper_added)):
        code = re.sub(r'//[^\n]*', '', src)
        check(not re.search(r'\[HDR\]|_Emission|emission', code, re.I), 'AC-O8 %s: no HDR property, no emission' % name)
    check('saturate(a*known*window*group)' in stroke and 'saturate(rgb)' in stroke and 'saturate(a*i.color.a)' in icon, 'AC-O8 the strip and icon outputs are saturated (LDR)')
    for name, src, prefix in (('MapStroke308.shader', stroke, '_S308'), ('PaperMapSurface.shader', paper, '_M308'), ('MapIcon308.shader', icon, '_Ink')):
        props = set(re.findall(r'^\s*(%s\w+)\(' % prefix, src, re.M))
        uniforms = set()
        for decl in re.findall(r'(?:^|;)\s*(?:float4|float|fixed4|sampler2D)\s+([^;]+)(?=;)', src, re.M):
            uniforms |= {u.strip() for u in decl.split(',') if re.fullmatch(prefix + r'\w+', u.strip())}
        used = set(re.findall(r'(%s\w+)' % prefix, re.sub(r'//[^\n]*', '', src.split('CGPROGRAM')[1])))
        check(props == uniforms == used, 'shader %s: %d %s properties = uniforms = uses%s'
              % (name, len(props), prefix, '' if props == uniforms == used else ' - properties %s, uniforms %s, used %s' % (sorted(props ^ uniforms), sorted(uniforms ^ used), sorted(props ^ used))))
    check('#pragma multi_compile_local _ _MAP308' in paper and '#pragma multi_compile_local _ _MINI_HUD' in paper, 'AC-E3 _MAP308 and _MINI_HUD are multi_compile keywords')
    check('#include "Assets/_Project/Art/UI/UI308/Map/Shaders/MapFog308.cginc"' in paper and '#include "MapFog308.cginc"' in stroke, 'the paper and the strips include the one MapFog308.cginc')
    bare = [re.sub(r'//[^\n]*', '', s).split('CGPROGRAM')[1] for s in (stroke, paper)]
    check(all(not re.search(r'(sampler2D|float4|float)\s[^;()=]*\b(_FogTex|_FogTex_TexelSize|_FogSoft|_FogNoise)\b[^;()=]*;', s) for s in bare),
          'the including shaders do not declare the fog uniforms again')
    orig = text(STAGE / 'Original/PaperMapSurface.shader.orig')
    a = orig.index('float KnownPaper(float2 uv)'); b = orig.index('float MapLum304(float2 uv)')
    c = fog.index('float MapFog308_Known(float2 uv)'); d = fog.index('#endif', c)
    want = normalise(orig[a:b]).replace('KnownPaper', 'MapFog308_Known').replace('ValueNoise304', 'MapFog308_Noise')
    check(normalise(fog[c:d]) == want, 'MapFog308_Known is the paper shader\'s KnownPaper body, unchanged (statement by statement)')
    legacy = paper[paper.index('#ifndef _MAP308'):paper.index('// ---------------------------------------------------------------- #308 notation')]
    a = orig.index('fixed4 terrainSample=tex2D(_MapTex,worldUv);'); b = orig.index('#ifdef UNITY_UI_CLIP_RECT')
    check(normalise(orig[a:b]) + '\n#else' == normalise(legacy.split('\n', 1)[1]), 'without _MAP308 the paper shader composes exactly as before (the #304 / #307 block is untouched)')

    # ---- #214 in code
    check('a*known*window*group' in stroke and 'MapFog308_Edge(worldUv,_S308Edge)' in stroke and 'MapFog308_Known' not in bare[0],
          '#214 strips: alpha x the crisp walked edge of the shared fog texture (MapFog308_Edge, the paper\'s function)')
    block = paper[paper.index('#308 notation'):]
    check('fixed3 col=lerp(unk,lerp(land,inLand,interiorOn),shown);' in block and 'col=lerp(unk,col,inside);' in block,
          '#214 paper: every terrain term is under `shown`; unwalked land and off-map are the wash only')
    code308 = re.sub(r'//[^\n]*', '', paper[paper.index('// ---------------------------------------------------------------- #308 notation'):])   # the _MAP308 branch alone
    check('MapFog308_Edge(worldUv,_M308Edge)' in code308 and 'walkedCells' not in code308 and 'MapFog308_Known' not in code308,
          '#308 paper: the _MAP308 branch draws the crisp edge only (no #307 soft-edge term inside it)')
    check('float crisp=saturate(edgePx+.5);' in code308 and 'float shown=lerp(crisp,' in code308
          and ('float rim=crisp*saturate(_M308Rim.y-edgePx+.5)' in code308 or E3.PAPER3 in code308) and 'col=lerp(col,lerp(unk,_M308Ink.rgb,_M308Rim.x),rim);' in code308,
          '#214 paper: the ink rim is the outermost px INSIDE the crisp edge (x crisp), ink over the unwalked wash; 1 px anti-aliased edge')
    a = fog.index('float2 MapFog308_Edge(float2 uv,float4 p)'); b = fog.index('float MapFog308_Known(float2 uv)')
    edge_fn = re.sub(r'//[^\n]*', '', fog[a:b])
    formula_f = 'MAPFOG308_E_FLOOR' in fog      # which MapFog308_Edge this cginc carries (F = #308 map fix 2); the edge rows below follow it
    map3 = 'MAPFOG308_E_GATE0' in fog           # #308 map 3: bites (25 taps under #if MAPFOG308_E_DEEP) + the continuous rim gate
    taps = a < b and edge_fn.count('tex2D(_FogTex') == (25 if map3 else 9) and 'min(min(w00,w10),min(w01,w11))' in edge_fn
    if not formula_f:
        check(taps and 'return float2(cover-(p.x+p.y*n),cover);' in edge_fn,
              '#214 MapFog308_Edge: 9 point taps, a lattice corner = min of its four cells (0 on every unwalked cell), field = cover - threshold')
    else:
        # #308 map 3: the rim line that goes with the cginc is the one of the paper shader deployed WITH it (pair), not the Stage308_map copy
        pair_src = text(pair); mark308 = '// ---------------------------------------------------------------- #308 notation'
        pair308 = re.sub(r'//[^\n]*', '', pair_src[pair_src.index(mark308):]) if mark308 in pair_src else ''
        pair_name = str(pair).replace('\\', '/').split('Oheangbu/', 1)[-1]
        map4 = map3 and E3.is_map4(pair308)       # #308 map 4: the sheet's thin whole rim (the paper reads _M308Rim.z / .w)
        map4_ok = (not map4 and not any(t in edge_fn for t in E3.TEXT4)) or (map4 and E3.map4_block(pair308) and all(t in edge_fn for t in E3.TEXT4))
        check(taps and map4_ok and (all(t in edge_fn for t in E3.TEXT3) and E3.PAPER3 in pair308 and 'float MapFog308_Rim(float y,float edgePx,float rimPx)' in fog if map3 else
                        'float land=min(depth,MAPFOG308_E_KCOVER*cover);' in edge_fn and 'return float2(land-theta,gate);' in edge_fn
                        and 'theta=max(theta,MAPFOG308_E_FLOOR/pxPerCell);' in edge_fn and E3.PAPER3 not in pair308),
              ('#214 MapFog308_Edge (formula map3): 25 point taps in the text (16 of them under #if MAPFOG308_E_DEEP), a lattice corner = min of its four cells '
               '(0 on every unwalked cell), field = land - theta with theta never under FLOOR screen px, and the paper shader deployed with it (%s) '
               'draws the rim with MapFog308_Rim x crisp%s' % (pair_name, '; map 4: its sheet rim is the three lines under #ifndef _MINI_HUD, and the cginc turns the edge waves off on a one-row fog' if map4 else '')) if map3 else
              ('#214 MapFog308_Edge (formula F): 9 point taps, a lattice corner = min of its four cells (0 on every unwalked cell), '
               'field = min(depth, KCOVER x cover) - theta, theta never under FLOOR screen px; the paper shader deployed with it (%s) keeps the rim line of formula F' % pair_name))
        D = edge_defines(fog); missing = [k for k in EDGE_F_NAMES if k not in D] + (E3.missing3(D) if map3 else [])
        rules = []
        if not missing:
            need = EDGE_F_FLOOR_PER_KCOVER * D['KCOVER']
            rules = [('FROM %g > 0' % D['FROM'], D['FROM'] > 0), ('FROM + SPAN %g < 1' % (D['FROM'] + D['SPAN']), D['FROM'] + D['SPAN'] < 1),
                     ('FLOOR %g >= .809 x KCOVER %g = %g' % (D['FLOOR'], D['KCOVER'], need), D['FLOOR'] >= need)]
            if map3: rules = E3.constant_rules3(D)
        broken = [t for t, ok in rules if not ok]; reads_p = bool(re.search(r'\bp\.[xyzw]', edge_fn))
        check(not missing and not broken and not reads_p,
              '#214 MapFog308_Edge (formula F) constants = the cginc\'s own #define MAPFOG308_E_* (%d of %d): %s; the function reads no notation value (p)%s%s%s'
              % (len(EDGE_F_NAMES) - len(missing), len(EDGE_F_NAMES), ', '.join(t for t, _ in rules) or 'not checked',
                 ' - MISSING: ' + ', '.join(missing) if missing else '', ' - BROKEN: ' + ', '.join(broken) if broken else '', ' - READS p' if reads_p else ''))
    # one set of numbers: shader defaults = notation asset defaults = the staged bundles
    def vec(src, name):
        m = re.search(name + r'\("[^"]*",Vector\)=\(([^)]*)\)', src)
        return [float(v) for v in m.group(1).split(',')] if m else None
    so = text(STAGE / 'App/World/UI/MapNotation308SO.cs')
    m1 = re.search(r'EdgeThreshold = new Vector2\(([\d.]+)f, ([\d.]+)f\)', so); m2 = re.search(r'EdgeNoiseCells = new Vector2\(([\d.]+)f, ([\d.]+)f\)', so)
    so_edge = [float(m1.group(1)), float(m1.group(2)), float(m2.group(1)), float(m2.group(2))] if m1 and m2 else None
    wts = re.search(r'fade1\)\*([\d.]+)\+lerp\(\.5,MapFog308_Noise\(c\*p\.w\+17\.3\),fade2\)\*([\d.]+);', edge_fn)
    bundles = [d for d in [STAGE / '_ProjectAssets/Art/UI/UI308/Map'] + sorted((STAGE / 'Bundles').glob('*')) if (d / 'map308_notation.json').exists()]
    notes = {str(d.relative_to(STAGE)).replace('\\', '/'): json.loads((d / 'map308_notation.json').read_text(encoding='utf-8')) for d in bundles}
    same = []
    for name, n in notes.items():
        ce = n['values']['fogEdge'].get('crispEdge', {})
        want = [ce.get('thresholdFrom'), ce.get('thresholdSpan')] + list(ce.get('noiseCellsPerFogCell', []))
        # formula F has no octave weights and reads none of these four numbers: there this row only keeps property, asset and bundle in step
        weights = formula_f or (wts is not None and [float(wts.group(1)), float(wts.group(2))] == ce.get('noiseWeights'))
        # #308 map 4: the rim numbers are a NAMED variant since map 3 (fogEdge.variant / variants). A bundle that names one must carry that
        # variant's (inkAlpha, px), and the paper's property default must be one row of its table (R2: what a material shows before the
        # notation is applied). A bundle of the older tool (no table) keeps the old rule: its numbers = the property default.
        # The active variant is PINNED outside the bundles (review of map 4, F7): the data file beside this tool says which variant the
        # minimap draws and its numbers (minimap.variant / inkAlpha / px - DECISIONS D308-24 answer 9: R3). A bundle that names another
        # variant, or other numbers under that name, fails here - three bundles agreeing with each other is not enough.
        RIM_PIN = ROOT / 'Tools/Art/map308_rim_views.json'
        try: _pin = json.loads(RIM_PIN.read_text(encoding='utf-8'))['minimap']; pin = [str(_pin['variant']), float(_pin['inkAlpha']), float(_pin['px'])]
        except (OSError, KeyError, ValueError, TypeError): pin = None
        fe = n['values']['fogEdge']; table = fe.get('variants')
        if table is None: rim_ok = vec(paper, '_M308Rim')[:2] == [fe['inkAlpha'], fe['px']]
        else: rim_ok = (fe.get('variant') in table and [fe['inkAlpha'], fe['px']] == [table[fe['variant']]['inkAlpha'], table[fe['variant']]['px']]
                        and vec(paper, '_M308Rim')[:2] in [[v['inkAlpha'], v['px']] for v in table.values()]
                        and pin is not None and [str(fe.get('variant')), float(fe['inkAlpha']), float(fe['px'])] == pin)
        same.append(want == vec(paper, '_M308Edge') == vec(stroke, '_S308Edge') == so_edge and weights and rim_ok)
    rim_names = sorted({str(n['values']['fogEdge'].get('variant', 'unnamed')) for n in notes.values()})
    check(bool(same) and all(same) and len(rim_names) <= 1, 'crisp edge numbers: paper _M308Edge / _M308Rim = strips _S308Edge = MapNotation308SO defaults = every staged bundle (%s): %s%s; rim variant of the bundles: %s; pinned by map308_rim_views.json: %s'
          % (', '.join("%s '%s'" % (k, v['stage']) for k, v in notes.items()) or 'none staged', vec(paper, '_M308Edge'),
             ' - kept in step only: formula F does not read _M308Edge / _S308Edge' if formula_f else '', ' / '.join(rim_names) or 'none',
             ('%s a%g %g px' % tuple(pin) if pin else 'NO PIN (file or its minimap block missing)') if notes and any(n['values']['fogEdge'].get('variants') for n in notes.values()) else 'not asked (no bundle names a variant)'))
    keep = lambda n: json.dumps([n['version'], n['strokeClasses'], n['strokeAtlas'], n['values'], n['terrain'], n['glyphs'], n['icons']['secondInk'], n['frames'], n['sizes'], n['zoomBands']], sort_keys=True)
    check(len({keep(n) for n in notes.values()}) <= 1, 'the staged bundles differ only in what the height stage changes (same contract, classes, values, glyphs, frames): %d bundle(s)' % len(notes))
    # ---- icons: the second ink layer
    glyph_cs = text(STAGE / 'App/World/UI/MapGlyph308Graphic.cs'); icon_code = re.sub(r'//[^\n]*', '', icon)
    check('_Ink2Chan' in icon_code and 'i.data.y' in icon_code and 'i.data.z' in icon_code and 'float a=1-(1-rim)*(1-ink)*(1-ink2);' in icon_code
          and 'new Vector4(rimAlpha, cell >= 0 ? secondAlpha : 0f, secondLight, 0f)' in glyph_cs and 'public void SetSecondInk(float alpha, bool light)' in glyph_cs,
          'icons: UI/MapIcon308 draws the second ink (B) over the first, MapGlyph308Graphic sends its alpha (uv1.y) and tone (uv1.z); a pattern quad sends 0')
    check(all(n['icons'].get('secondInk', {}).get('glyphs') == ['player'] for n in notes.values()) and 'm.SetColor("_Ink2"' in text(STAGE / 'App/World/UI/WorldMapPresenter.Map308.cs'),
          'icons: the bundles name the brush mark as the only glyph with a second ink; the icon material gets the ink token as _Ink2')
    terrain_terms = [l for l in block.split('\n') if re.search(r'\bter\.|pat\.[rgb]\b', l)]
    check(all(('land' in l or 'wash=' in l or 'forest=' in l or 'rock=' in l or 'dm=' in l or 'ripple=' in l) for l in terrain_terms),
          '#214 paper: the terrain picture and the pattern channels r g b feed only the walked-land colour (%d lines)' % len(terrain_terms))
    mini = text(STAGE / 'App/World/UI/HudMinimap304.cs')
    check('if (marker.Kind == MapMarkerKind304.Objective) { objectives++; continue; }' in mini, '#214 minimap: the objective is still left off')
    bake_roles = (ROOT / 'Tools/Art/cartography308.py')
    check(not bake_roles.exists() or 'Role' not in re.sub(r'#[^\n]*|"""[\s\S]*?"""', '', text(bake_roles)).replace('M.road_class', ''), '#214 the bake never reads a route Role (track A source, if present)')

    # ---- contracts
    presenter = text(STAGE / 'App/World/UI/WorldMapPresenter.cs')
    check('"PersistentMinimap"' in mini and '"MiniRoot"' in mini and all(n in presenter for n in ('"TwiceFoldedHanji"', '"PrintedMapWindow"', '"MapInput"', '"PersistentMinimap"')),
          'AC-E2 name contract: PersistentMinimap, MiniRoot, TwiceFoldedHanji, PrintedMapWindow, MapInput are still built')
    hud308 = text(STAGE / 'App/World/UI/HudMinimap304.Map308.cs')
    check(not re.search(r'AddComponent<\s*(RectMask2D|Mask)\s*>|typeof\((RectMask2D|Mask)\)', mini + hud308 + text(STAGE / 'App/World/UI/MapStrokes308Graphic.cs')),
          'AC-E2 no Mask / RectMask2D is added under the minimap (the strip shader clips to the window)')
    code = re.sub(r'//[^\n]*', '', '\n'.join(added_lines('App/World/UI/HudMinimap304.cs')) + '\n' + hud308)
    check('uvRect' not in code, 'AC-E2 MiniRoot.uvRect is not touched by any added code (stays 0,0,1,1)')
    staged = [p.name for p in STAGE.rglob('*.cs')]
    check('HudController.cs' not in staged, 'HudController.cs is not in the stage (Stage308_hud owns it)')
    check(not [n for n in RECOGNITION if n in staged], 'the five recognition files are not in the stage')
    check(not (STAGE / 'App/World/UI/MapDiscovery.cs').exists(), 'MapDiscovery.cs is unchanged (the region gate uses Reveal\'s existing `visible` filter): cell size, radius and save format stay')
    check(all((LIVE / UI / n).exists() is False for n in NEW_RUNTIME), 'the new files do not exist in the live project yet (nothing was written under Oheangbu/Assets)')

    failed = sum(1 for c in checks if not c['ok'])
    rep_file = arg('--report') or ROOT / 'Art/UI308/Map/bcheck308.json'; rep_file.parent.mkdir(parents=True, exist_ok=True)
    report = dict(tool='Tools/Art/map308_bcheck.py', stage=str(STAGE.relative_to(ROOT)).replace('\\', '/'), checks=checks, failed=failed,
                  stage_sha256={str(p.relative_to(STAGE)).replace('\\', '/'): sha(p) for p in sorted(STAGE.rglob('*')) if p.is_file() and p.suffix in ('.cs', '.shader', '.cginc')})
    if arg('--cginc') or formula_f:      # (a default run on the threshold formula writes the report it always wrote)
        report.update(cginc=str(cginc).replace('\\', '/'), cginc_sha256=sha(cginc), edge_formula='map3' if 'MAPFOG308_E_GATE0' in text(cginc) else 'F' if formula_f else 'threshold',
                      paper_pair=str(pair).replace('\\', '/'), paper_pair_sha256=sha(pair))
    rep_file.write_bytes((json.dumps(report, ensure_ascii=False, indent=1) + '\n').encode('utf-8'))
    print('bcheck308 %s %d/%d -> %s' % ('ok' if failed == 0 else 'FAILED', len(checks) - failed, len(checks), str(arg('--report') or 'Art/UI308/Map/bcheck308.json').replace('\\', '/')))
    sys.exit(0 if failed == 0 else 1)


if __name__ == '__main__':
    main()
