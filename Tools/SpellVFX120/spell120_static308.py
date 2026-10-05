#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""spell120_static308.py - read-only static census of the 120-glyph spell VFX catalogue.

Everything here is judged WITHOUT Unity: serialized YAML (.asset/.prefab/.mat), shader sources,
shader graphs and FBX geometry are read straight from disk. Nothing under Oheangbu/Assets is written.

  python Tools/SpellVFX120/spell120_static308.py            # census -> static308.json + STATIC308.md
  python Tools/SpellVFX120/spell120_static308.py --reindex  # rebuild the GUID index first (minutes, cold disk)

What it covers (COVERAGE.md section 9.2 item 1):
  catalogue integrity, both visual sets, broken GUIDs, which prefab the GAME plays per glyph versus the
  catalogue prefab, material emission / additive blending, Light components, colour source, a static
  triangle estimate per glyph, layer usage, particle-system census.
What it cannot cover: anything decided at run time (property blocks, generated meshes, real instance
counts, actual pixels). Those belong to the editor sweep (Spell120Sweep308) and to Play.

Status words: this tool reports a census. It never says a glyph is acceptable.
"""
import argparse
import colorsys
import datetime
import json
import os
import re
import struct
import sys
import tempfile
import time
import zlib
from collections import Counter, OrderedDict, defaultdict

BS = chr(92)
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..')).replace(BS, '/')
UNITY = REPO + '/Oheangbu'
ASSETS = UNITY + '/Assets'
ART = ASSETS + '/_Project/Art'
VFX = ART + '/SpellVFX120'
LIVE_DATA = ART + '/World/Architecture296/Data'
MAIN_SCENE = ASSETS + '/_Project/Scenes/World/W_Demo_Main.unity'
CATALOG = VFX + '/Data/VFX120_Catalog.asset'
VISUAL_SET_ORIGINAL = VFX + '/Data/SpellVisualSet_120.asset'
CSV = REPO + '/Docs/오행부_작도어휘_v0_1.csv'
NOTES = REPO + '/Art/SpellVFX120/Spell120_308'
COVERAGE_JSON = NOTES + '/coverage.json'
RUNTIME_CODE = ASSETS + '/_Project/Scripts/App/SpellVFX120'
TAG_MANAGER = UNITY + '/ProjectSettings/TagManager.asset'
QUARANTINE = os.path.normpath(os.path.join(REPO, '..', 'Oheangbu_Unused307')).replace(BS, '/')
INDEX_CACHE = os.path.join(tempfile.gettempdir(), 'oheangbu_guid_index308.json')

BUILTIN_GUIDS = {'0000000000000000e000000000000000', '0000000000000000f000000000000000'}
BUILTIN_MESH_TRIS = {10202: ('Cube', 12), 10206: ('Cylinder', 80), 10207: ('Sphere', 768),
                     10208: ('Capsule', 832), 10209: ('Plane', 200), 10210: ('Quad', 2)}
REF = re.compile(r'\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: (\d)\}')
MOMENTARY_ROLES = ('cast', 'impact', 'contact', 'debris', 'attack')
SUSTAINED_ROLES = ('body', 'field', 'actor')
FLIGHT_ROLES = ('flight',)
COLOUR_NAME = re.compile(r'colou?r|tint', re.I)
SKIP_RUNTIME_FIELD = re.compile(r'Clip$|Avatar|Controller|Animator|Audio|Sound', re.I)
EMISSION_NAME = re.compile(r'emis|glow|ember|bloom|shine|hdr|bright|intens', re.I)
PRESENTATIONS = ('WoodDeer', 'FireHaetae', 'MetalTiger', 'DokkaebiClub', 'WaterTurtle', 'StoneDokkaebi')
ELEMENT_OF_INITIAL = {'ㄱ': '목', 'ㄴ': '화', 'ㅁ': '토', 'ㅅ': '금', 'ㅇ': '수'}


def rel(path):
    """Repo-relative path for reports."""
    if not path:
        return path
    path = path.replace(BS, '/')
    return path[len(REPO) + 1:] if path.startswith(REPO + '/') else path


def short(path):
    """Compact path for tables: P/ = SpellVFX120 art root, D/ = live data copies."""
    if not path:
        return '-'
    p = rel(path)
    p = p.replace('Oheangbu/Assets/_Project/Art/SpellVFX120/', 'P/')
    p = p.replace('Oheangbu/Assets/_Project/Art/World/Architecture296/Data/', 'D/')
    p = p.replace('Oheangbu/Assets/_Project/', 'A/')
    p = p.replace('Oheangbu/Assets/', 'Assets/')
    p = re.sub(r'Oheangbu/Library/PackageCache/([^@/]+)@[0-9a-f]+/', r'Pkg/\1/', p)
    p = re.sub(r'D/(?:[0-9a-f]{32}_)+', 'D/…_', p)
    return p


def read(path):
    with open(path, 'r', encoding='utf-8', errors='replace') as fh:
        return fh.read()


def unquote(value):
    value = value.strip()
    if len(value) >= 2 and value[0] == '"' and value[-1] == '"':
        try:
            return json.loads(value)
        except ValueError:
            return value[1:-1]
    if len(value) >= 2 and value[0] == "'" and value[-1] == "'":
        return value[1:-1]
    return value


# ----------------------------------------------------------------------------------------------- index
def build_index(extra_roots=()):
    roots = [ASSETS, UNITY + '/Packages', UNITY + '/Library/PackageCache'] + list(extra_roots)
    rx = re.compile(r'^guid: ([0-9a-f]{32})', re.M)
    index = {}
    for root in roots:
        if not os.path.isdir(root):
            continue
        for folder, _, files in os.walk(root):
            for name in files:
                if not name.endswith('.meta'):
                    continue
                path = os.path.join(folder, name)
                try:
                    with open(path, 'r', encoding='utf-8', errors='replace') as fh:
                        head = fh.read(256)
                except OSError:
                    continue
                m = rx.search(head)
                if m:
                    index[m.group(1)] = path[:-5].replace(BS, '/')
    return index


def load_index(reindex, max_age_hours):
    if not reindex and os.path.isfile(INDEX_CACHE):
        age = time.time() - os.path.getmtime(INDEX_CACHE)
        if age < max_age_hours * 3600:
            with open(INDEX_CACHE, 'r', encoding='utf-8') as fh:
                data = json.load(fh)
            if data.get('repo') == REPO:
                return data['index'], age, False
    started = time.time()
    index = build_index()
    with open(INDEX_CACHE, 'w', encoding='utf-8') as fh:
        json.dump({'repo': REPO, 'index': index}, fh)
    print('GUID index rebuilt: %d metas in %.0fs -> %s' % (len(index), time.time() - started, INDEX_CACHE))
    return index, 0.0, True


# ----------------------------------------------------------------------------------------------- FBX
def _polygon_tris(indices):
    tris = 0
    run = 0
    for v in indices:
        run += 1
        if v < 0:
            tris += max(0, run - 2)
            run = 0
    return tris


def fbx_geometries(path):
    """[(name, triangles)] for every Geometry node of a binary or ASCII FBX (source geometry, before import)."""
    with open(path, 'rb') as fh:
        data = fh.read()
    if not data.startswith(b'Kaydara FBX Binary'):
        text = data.decode('latin-1', 'replace')
        out = []
        for m in re.finditer(r'Geometry: [^\n]*?"(?:Geometry::)?([^"]*)"[^\n]*\{', text):
            pm = re.search(r'PolygonVertexIndex: \*\d+ \{\s*a: ([^}]*)\}', text[m.end():])
            if pm:
                out.append((m.group(1), _polygon_tris(
                    [int(x) for x in pm.group(1).replace('\n', '').split(',') if x.strip()])))
        return out
    version = struct.unpack_from('<I', data, 23)[0]
    header = '<QQQB' if version >= 7500 else '<IIIB'
    hsize = struct.calcsize(header)
    out = []

    def properties(pos, count):
        props = []
        for _ in range(count):
            kind = chr(data[pos])
            pos += 1
            if kind in 'YCIFDL':
                size = {'Y': 2, 'C': 1, 'I': 4, 'F': 4, 'D': 8, 'L': 8}[kind]
                props.append((kind, data[pos:pos + size]))
                pos += size
            elif kind in 'fdlib':
                n, enc, clen = struct.unpack_from('<III', data, pos)
                pos += 12
                props.append((kind, (n, enc, pos, clen)))
                pos += clen
            elif kind in 'SR':
                n = struct.unpack_from('<I', data, pos)[0]
                pos += 4
                props.append((kind, data[pos:pos + n]))
                pos += n
            else:
                raise ValueError('unknown FBX property type %r' % kind)
        return props

    def walk(pos, end, geometry):
        while pos < end - hsize + 1:
            node_end, nprops, plen, nlen = struct.unpack_from(header, data, pos)
            if node_end == 0:
                return
            name = data[pos + hsize:pos + hsize + nlen].decode('latin-1')
            ppos = pos + hsize + nlen
            current = geometry
            if name == 'Geometry':
                label = ''
                for kind, value in properties(ppos, nprops):
                    if kind == 'S':
                        label = value.split(b'\x00')[0].decode('latin-1', 'replace')
                        break
                current = [label, None]
                out.append(current)
            elif name == 'PolygonVertexIndex' and geometry is not None:
                for kind, value in properties(ppos, nprops):
                    if kind == 'i':
                        n, enc, p, clen = value
                        raw = data[p:p + clen]
                        if enc == 1:
                            raw = zlib.decompress(raw)
                        geometry[1] = _polygon_tris(struct.unpack('<%di' % n, raw[:4 * n]))
            if ppos + plen < node_end:
                walk(ppos + plen, node_end, current)
            pos = node_end

    walk(27, len(data), None)
    return [(n, t) for n, t in out if t is not None]


# ----------------------------------------------------------------------------------------------- census
class Census(object):
    def __init__(self, index):
        self.index = index
        self._prefabs = {}
        self._materials = {}
        self._shaders = {}
        self._mesh_assets = {}
        self._fbx = {}
        self.layer_names = self._layers()

    # -- project settings
    def _layers(self):
        names = {}
        if not os.path.isfile(TAG_MANAGER):
            return names
        text = read(TAG_MANAGER)
        m = re.search(r'\n  layers:\n((?:  - .*\n|  -\n)+)', text)
        if m:
            for i, line in enumerate(m.group(1).splitlines()):
                names[i] = line[4:].strip()
        return names

    def layer_name(self, i):
        name = self.layer_names.get(i, '')
        return name if name else ('Layer%d' % i)

    def path_of(self, guid):
        return self.index.get(guid)

    # -- meshes
    def mesh_asset_tris(self, path):
        if path not in self._mesh_assets:
            tris = None
            try:
                with open(path, 'r', encoding='utf-8', errors='replace') as fh:
                    head = fh.read(131072)
                if '!u!43 ' in head[:400]:
                    sub = re.search(r'm_SubMeshes:\n((?:  [ -] .*\n)+)', head)
                    if sub:
                        tris = 0
                        for count, topo in re.findall(r'indexCount: (\d+)\n\s+topology: (\d+)', sub.group(1)):
                            if topo == '0':
                                tris += int(count) // 3
            except OSError:
                pass
            self._mesh_assets[path] = tris
        return self._mesh_assets[path]

    def fbx_info(self, path):
        if path not in self._fbx:
            try:
                self._fbx[path] = fbx_geometries(path)
            except Exception as error:  # a malformed vendor file must not stop the census
                self._fbx[path] = error
        return self._fbx[path]

    def mesh_tris(self, file_id, guid):
        """(tris or None, label, exact) for one mesh reference."""
        if guid in BUILTIN_GUIDS:
            name, tris = BUILTIN_MESH_TRIS.get(int(file_id), ('builtin#%s' % file_id, None))
            return tris, 'builtin:' + name, tris is not None
        path = self.path_of(guid)
        if not path:
            return None, 'MISSING:' + guid, False
        ext = os.path.splitext(path)[1].lower()
        if ext == '.asset':
            tris = self.mesh_asset_tris(path)
            return tris, short(path), tris is not None
        if ext == '.fbx':
            geos = self.fbx_info(path)
            if isinstance(geos, Exception) or not geos:
                return None, short(path), False
            if len(geos) == 1:
                return geos[0][1], short(path), True
            return max(t for _, t in geos), short(path) + ' (multi-mesh: largest of %d)' % len(geos), False
        return None, short(path), False

    def model_tris(self, guid):
        path = self.path_of(guid)
        geos = self.fbx_info(path) if path else None
        if not geos or isinstance(geos, Exception):
            return None
        return sum(t for _, t in geos)

    # -- shaders
    def shader(self, file_id, guid):
        key = (file_id, guid)
        if key in self._shaders:
            return self._shaders[key]
        info = {'path': None, 'kind': 'builtin', 'name': 'builtin#%s' % file_id, 'blendStatic': None,
                'propertyDriven': False, 'defaults': {}, 'emissionProps': [], 'hdrProps': [],
                'mentionsEmission': False, 'graphAlphaMode': None, 'graphSurface': None, 'queue': None}
        path = self.path_of(guid) if guid else None
        if guid and not path:
            info.update(kind='missing', name='MISSING:' + guid)
        elif path:
            info['path'] = path
            info['name'] = short(path)
            ext = os.path.splitext(path)[1].lower()
            text = read(path)
            if ext == '.shader':
                info['kind'] = 'shaderlab'
                body = re.sub(r'//[^\n]*', '', text)
                for hdr, name, kind, default in re.findall(
                        r'((?:\[[^\]]*\]\s*)*)(_\w+)\s*\(\s*"[^"]*"\s*,\s*([^)]+?)\s*\)\s*=\s*([^\n{]+)', body):
                    kind = kind.strip()
                    if kind in ('Float', 'Int', 'Integer') or kind.startswith('Range'):
                        try:
                            info['defaults'][name] = float(default.strip())
                        except ValueError:
                            pass
                    if '[HDR]' in hdr:
                        info['hdrProps'].append(name)
                    if EMISSION_NAME.search(name):
                        info['emissionProps'].append(name)
                blends = re.findall(r'\bBlend\s+(\[?\w+\]?)\s+(\[?\w+\]?)', body)
                if any(a.startswith('[') or b.startswith('[') for a, b in blends):
                    info['propertyDriven'] = True
                    info['blendProps'] = [(a.strip('[]'), b.strip('[]')) for a, b in blends
                                          if a.startswith('[') or b.startswith('[')][0]
                statics = [(a, b) for a, b in blends if not a.startswith('[') and not b.startswith('[')]
                if statics:
                    info['blendStatic'] = statics[0]
                elif not blends:
                    info['blendStatic'] = ('One', 'Zero')
                q = re.search(r'"Queue"\s*=\s*"([^"]+)"', body)
                info['queue'] = q.group(1) if q else None
                info['mentionsEmission'] = bool(re.search(r'emission|emissive|ember|glow|radiance', body, re.I))
                name = re.search(r'Shader\s+"([^"]+)"', text)
                if name:
                    info['shaderName'] = name.group(1)
                if 'UniversalPipeline' in text and 'Library/PackageCache' in path:
                    info['kind'] = 'urp'
                    info['propertyDriven'] = True
            elif ext == '.shadergraph':
                info['kind'] = 'shadergraph'
                m = re.search(r'"m_AlphaMode": (\d+)', text)
                info['graphAlphaMode'] = int(m.group(1)) if m else None
                m = re.search(r'"m_SurfaceType": (\d+)', text)
                info['graphSurface'] = int(m.group(1)) if m else None
                info['graphAllowOverride'] = '"m_AllowMaterialOverride": true' in text
                for name in re.findall(r'"m_OverrideReferenceName": "(_\w+)"', text):
                    if EMISSION_NAME.search(name):
                        info['emissionProps'].append(name)
                info['mentionsEmission'] = '"m_Name": "Emission"' in text or 'SurfaceDescription.Emission' in text
        self._shaders[key] = info
        return info

    # -- materials
    def material(self, guid):
        if guid in self._materials:
            return self._materials[guid]
        path = self.path_of(guid)
        if not path or not path.lower().endswith('.mat'):
            self._materials[guid] = None
            return None
        text = read(path)
        m = re.search(r'm_Shader: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}', text)
        shader = self.shader(m.group(1) if m else '0', m.group(2) if m else None)
        # anchored per line: neighbouring list entries share one newline
        floats = {k: float(v) for k, v in re.findall(r'^    - (_\w+): (-?[\d.]+(?:[eE][-+]?\d+)?)\s*$', text, re.M)}
        colors = {}
        for name, r, g, b, a in re.findall(
                r'^    - (_\w+): \{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+), a: ([-\d.eE+]+)\}', text, re.M):
            colors[name] = (float(r), float(g), float(b), float(a))
        keywords = []
        km = re.search(r'm_ValidKeywords:(.*?)\n  m_InvalidKeywords', text, re.S)
        if km:
            keywords = re.findall(r'- (\S+)', km.group(1))
        km = re.search(r'm_ShaderKeywords: (.*)', text)
        if km and km.group(1).strip():
            keywords += km.group(1).split()
        textures = []
        for name, tguid in re.findall(r'^    - (_\w+):\n\s+m_Texture: \{fileID: -?\d+, guid: ([0-9a-f]{32})', text, re.M):
            textures.append((name, tguid))
        queue = re.search(r'm_CustomRenderQueue: (-?\d+)', text)

        def number(name, default=None):
            if name in floats:
                return floats[name]
            return shader['defaults'].get(name, default)

        blend = 'unknown'
        how = ''
        if shader['kind'] == 'missing':
            blend = 'missing-shader'
        elif shader['kind'] == 'shadergraph':
            mode = shader['graphAlphaMode']
            how = 'graph m_AlphaMode=%s' % mode
            if shader.get('graphAllowOverride') and '_Blend' in floats:
                mode = int(floats['_Blend'])
                how = 'material _Blend=%s (graph allows override)' % mode
            if shader['graphSurface'] == 0 and not shader.get('graphAllowOverride'):
                blend = 'opaque'
            else:
                blend = {0: 'alpha', 1: 'premultiply', 2: 'additive', 3: 'multiply'}.get(mode, 'unknown')
        elif shader['propertyDriven']:
            src_name, dst_name = shader.get('blendProps', ('_SrcBlend', '_DstBlend'))
            src, dst = number(src_name), number(dst_name)
            how = '%s=%s %s=%s' % (src_name, src, dst_name, dst)
            if shader['kind'] == 'urp' and number('_Surface', 0) == 0:
                blend = 'opaque'
            elif dst is None:
                blend = 'unknown'
            elif int(dst) == 1:
                blend = 'additive'
            elif int(dst) == 0 and (src is None or int(src) in (1,)):
                blend = 'opaque'
            elif int(dst) == 10:
                blend = 'premultiply' if src is not None and int(src) == 1 else 'alpha'
            elif int(dst) in (0, 3) and src is not None and int(src) in (0, 2):
                blend = 'multiply'
        elif shader['blendStatic']:
            src, dst = shader['blendStatic']
            how = 'Blend %s %s' % (src, dst)
            if dst == 'One':
                blend = 'additive'
            elif dst == 'Zero':
                blend = 'opaque'
            elif dst == 'OneMinusSrcAlpha':
                blend = 'premultiply' if src == 'One' else 'alpha'
            else:
                blend = 'other'

        # m_Colors also stores plain vectors (tiling, fade parameters): only colour-named or [HDR] properties count
        hdr = {n: round(max(c[0], c[1], c[2]), 3) for n, c in colors.items()
               if max(c[0], c[1], c[2]) > 1.001 and (COLOUR_NAME.search(n) or n in shader['hdrProps'])
               and n != '_EmissionColor'}
        emission_color = colors.get('_EmissionColor')
        emission_max = max(emission_color[:3]) if emission_color else 0.0
        emission_keyword = '_EMISSION' in keywords
        emission_floats = {n: floats[n] for n in floats if EMISSION_NAME.search(n)
                           and n not in ('_EmissionEnabled', '_Usecenterglow')}
        emission_colors = {n: round(max(colors[n][:3]), 3) for n in colors
                           if EMISSION_NAME.search(n) and COLOUR_NAME.search(n) and n != '_EmissionColor'}
        # "emissive" = the material itself asks for more light than its texture/tint gives: URP emission keyword
        # with a non-black colour, an HDR colour above 1, or a brightness multiplier above 1
        # (KTP graphs: _Emission, default 1; combustion shader: _Intensity "Radiance").
        reasons = []
        if emission_keyword and emission_max > 0:
            reasons.append('_EMISSION keyword, _EmissionColor max %.2f' % emission_max)
        if hdr:
            reasons.append('HDR colour ' + ', '.join('%s=%.2f' % kv for kv in sorted(hdr.items())))
        for name, value in sorted(emission_floats.items()):
            if value > 1.001:
                reasons.append('%s=%.2f' % (name, value))
        for name, value in sorted(emission_colors.items()):
            if value > 1.001:
                reasons.append('%s max %.2f' % (name, value))
        info = {
            'guid': guid, 'path': path, 'shader': shader['name'], 'shaderKind': shader['kind'],
            'blend': blend, 'blendSource': how, 'additive': blend == 'additive',
            'emissive': bool(reasons), 'emissiveReasons': reasons,
            'emissionKeyword': emission_keyword, 'emissionColorMax': round(emission_max, 3),
            'hdrColors': hdr, 'emissionFloats': emission_floats, 'emissionColors': emission_colors,
            'shaderMentionsEmission': shader['mentionsEmission'],
            'queue': int(queue.group(1)) if queue else None,
            'baseColor': colors.get('_BaseColor') or colors.get('_Color') or colors.get('_TintColor'),
            'textures': [(n, short(self.path_of(g)) if self.path_of(g) else 'MISSING:' + g) for n, g in textures],
            'missingTextures': [g for _, g in textures if g not in BUILTIN_GUIDS and not self.path_of(g)],
        }
        self._materials[guid] = info
        return info

    # -- prefabs
    def prefab(self, path):
        if path in self._prefabs:
            return self._prefabs[path]
        text = read(path)
        parts = re.split(r'^--- !u!(\d+) &(-?\d+)( stripped)?\n', text, flags=re.M)
        objects, transforms = {}, {}
        renderers, filters, systems, lights, instances, behaviours = [], [], [], [], [], []
        raw_refs = []
        for i in range(1, len(parts), 4):
            cls, fid, stripped, body = int(parts[i]), parts[i + 1], parts[i + 2], parts[i + 3]
            if stripped:
                continue
            go = re.search(r'\n  m_GameObject: \{fileID: (-?\d+)\}', body)
            go = go.group(1) if go else None
            if cls == 1:
                layer = re.search(r'\n  m_Layer: (\d+)', body)
                name = re.search(r'\n  m_Name: (.*)', body)
                active = re.search(r'\n  m_IsActive: (\d)', body)
                objects[fid] = {'name': unquote(name.group(1)) if name else '', 'layer': int(layer.group(1)) if layer else 0,
                                'active': (active.group(1) == '1') if active else True}
            elif cls in (4, 224):
                father = re.search(r'\n  m_Father: \{fileID: (-?\d+)\}', body)
                transforms[fid] = (go, father.group(1) if father else '0')
            elif cls in (23, 137, 199, 96, 120, 212):
                enabled = re.search(r'\n  m_Enabled: (\d)', body)
                mats = re.search(r'\n  m_Materials:\n((?:  - .*\n)*)', body)
                entry = {'cls': cls, 'go': go, 'enabled': (enabled.group(1) == '1') if enabled else True,
                         'materials': REF.findall(mats.group(1)) if mats else []}
                if cls == 199:
                    mode = re.search(r'\n  m_RenderMode: (\d+)', body)
                    entry['renderMode'] = int(mode.group(1)) if mode else 0
                    entry['meshes'] = [(f, g) for f, g, _ in REF.findall(
                        '\n'.join(re.findall(r'\n  m_Mesh\d?: (\{[^}]*\})', body)))]
                if cls == 137:
                    mesh = re.search(r'\n  m_Mesh: (\{[^}]*\})', body)
                    entry['meshes'] = [(f, g) for f, g, _ in REF.findall(mesh.group(1))] if mesh else []
                renderers.append(entry)
            elif cls == 33:
                mesh = re.search(r'\n  m_Mesh: (\{[^}]*\})', body)
                filters.append({'go': go, 'mesh': [(f, g) for f, g, _ in REF.findall(mesh.group(1))] if mesh else []})
            elif cls == 198:
                def val(key, default='0'):
                    m = re.search(r'\n\s+' + key + r': ([-\d.eE+]+)', body)
                    return m.group(1) if m else default
                colour = re.search(r'\n    startColor:\n(?:.*\n){0,6}?\s+maxColor: \{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+)', body)
                shape = re.search(r'\n  ShapeModule:\n((?:    .*\n)+)', body)
                shape_block = shape.group(1) if shape else ''
                shape_type = re.search(r'^    type: (\d+)', shape_block, re.M)
                shape_enabled = re.search(r'^    enabled: (\d)', shape_block, re.M)
                shape_mesh = re.search(r'^    m_Mesh: (\{[^}]*\})', shape_block, re.M)
                systems.append({'go': go, 'looping': val('looping') == '1', 'prewarm': val('prewarm') == '1',
                                'length': float(val('lengthInSec', '0')), 'maxParticles': int(float(val('maxNumParticles', '0'))),
                                'playOnAwake': val('playOnAwake', '1') == '1',
                                'startColorMax': round(max(float(x) for x in colour.groups()), 3) if colour else None,
                                'shapeType': int(shape_type.group(1)) if shape_type else None,
                                'shapeEnabled': (shape_enabled.group(1) == '1') if shape_enabled else False,
                                'shapeMesh': [(f, g) for f, g, _ in REF.findall(shape_mesh.group(1))] if shape_mesh else []})
            elif cls == 108:
                def lv(key, default='0'):
                    m = re.search(r'\n  ' + key + r': ([-\d.eE+]+)', body)
                    return m.group(1) if m else default
                enabled = re.search(r'\n  m_Enabled: (\d)', body)
                lights.append({'go': go, 'enabled': (enabled.group(1) == '1') if enabled else True,
                               'type': int(lv('m_Type')), 'intensity': float(lv('m_Intensity')),
                               'range': float(lv('m_Range'))})
            elif cls == 1001:
                src = re.search(r'm_SourcePrefab: \{fileID: (-?\d+), guid: ([0-9a-f]{32})', body)
                parent = re.search(r'm_TransformParent: \{fileID: (-?\d+)\}', body)
                overrides = re.findall(r'objectReference: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: (\d)\}', body)
                layer = re.findall(r'propertyPath: m_Layer\n\s+value: (\d+)', body)
                instances.append({'guid': src.group(2) if src else None, 'parent': parent.group(1) if parent else '0',
                                  'overrides': overrides, 'layerOverrides': [int(x) for x in layer]})
            elif cls == 114:
                script = re.search(r'm_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32})', body)
                refs = []
                for line in body.splitlines():
                    for f, g, t in REF.findall(line):
                        key = line.strip().split(':')[0].lstrip('- ')
                        if key != 'm_Script':
                            refs.append((key, f, g))
                behaviours.append({'go': go, 'script': script.group(1) if script else None, 'refs': refs})
            for line in body.splitlines():
                if 'guid:' in line:
                    for f, g, t in REF.findall(line):
                        raw_refs.append((cls, line.strip().split(':')[0].lstrip('- '), f, g))
        go_transform = {go: (tid, father) for tid, (go, father) in transforms.items() if go}

        def active(go):
            seen = 0
            while go and seen < 64:
                info = objects.get(go)
                if info is None:
                    return True
                if not info['active']:
                    return False
                tid_father = go_transform.get(go)
                if not tid_father or tid_father[1] in ('0', None):
                    return True
                parent = transforms.get(tid_father[1])
                go = parent[0] if parent else None
                seen += 1
            return True

        for group in (renderers, filters, systems, lights, behaviours):
            for entry in group:
                entry['active'] = active(entry['go'])
                info = objects.get(entry['go'])
                entry['goName'] = info['name'] if info else ''
                entry['layer'] = info['layer'] if info else 0
        parsed = {'path': path, 'objects': objects, 'renderers': renderers, 'filters': filters, 'systems': systems,
                  'lights': lights, 'instances': instances, 'behaviours': behaviours, 'rawRefs': raw_refs,
                  'layers': Counter(o['layer'] for o in objects.values())}
        self._prefabs[path] = parsed
        return parsed


class Bucket(object):
    """Accumulates everything one glyph variant reaches, by role."""

    def __init__(self, census):
        self.c = census
        self.materials = OrderedDict()   # guid -> {'roles': set, 'via': set}
        self.mesh_instances = []         # (role, label, tris, exact, source)
        self.systems = []                # (role, source, system dict, renderer dict or None)
        self.lights = []                 # (role, source, light dict)
        self.layers = Counter()
        self.broken = []                 # {guid, in, key, role, effect}
        self.prefabs = OrderedDict()     # path -> role
        self.notes = []

    def add_material(self, guid, role, via):
        if guid in BUILTIN_GUIDS:
            return
        if not self.c.path_of(guid):
            self.broken.append({'guid': guid, 'in': short(via), 'key': 'material', 'role': role,
                                'effect': 'renderer draws the magenta error material or nothing'})
            return
        entry = self.materials.setdefault(guid, {'roles': set(), 'via': set()})
        entry['roles'].add(role)
        entry['via'].add(short(via))

    def add_mesh(self, file_id, guid, role, source, count=1, uncertain=False):
        tris, label, exact = self.c.mesh_tris(file_id, guid)
        if label.startswith('MISSING:'):
            return False
        self.mesh_instances.append({'role': role, 'mesh': label, 'tris': tris, 'count': count,
                                    'exact': exact and not uncertain, 'source': short(source)})
        return True

    def walk_prefab(self, guid, role, via, depth=0, seen=None):
        seen = seen or set()
        path = self.c.path_of(guid)
        if not path:
            if guid not in BUILTIN_GUIDS:
                self.broken.append({'guid': guid, 'in': short(via), 'key': 'prefab', 'role': role,
                                    'effect': 'nothing is instantiated for this slot'})
            return
        ext = os.path.splitext(path)[1].lower()
        if ext == '.fbx':
            tris = self.c.model_tris(guid)
            self.mesh_instances.append({'role': role, 'mesh': short(path) + ' (whole model)', 'tris': tris, 'count': 1,
                                        'exact': tris is not None, 'source': short(via)})
            return
        if ext != '.prefab' or path in seen or depth > 6:
            return
        seen = seen | {path}
        self.prefabs.setdefault(path, role)
        parsed = self.c.prefab(path)
        for layer, n in parsed['layers'].items():
            self.layers[layer] += n
        active_filters = {}
        for f in parsed['filters']:
            if f['active']:
                active_filters[f['go']] = f
        fbx_refs = defaultdict(Counter)   # (guid, role) -> fileID -> references
        reported = set()

        def mesh_ref(fid, g, sub_role, label, effect):
            mesh_path = self.c.path_of(g)
            if g not in BUILTIN_GUIDS and not mesh_path:
                reported.add(g)
                self.broken.append({'guid': g, 'in': short(path), 'key': label, 'role': sub_role, 'effect': effect})
            elif mesh_path and mesh_path.lower().endswith('.fbx'):
                fbx_refs[(g, sub_role)][fid] += 1
            else:
                self.add_mesh(fid, g, sub_role, path)

        for r in parsed['renderers']:
            if not r['active'] or not r['enabled']:
                continue
            sub_role = role
            if role == 'flight':
                # Bolt300 contract: children of "Contact" burst at the impact, "Travel" flies.
                sub_role = 'contact' if self._under(parsed, r['go'], 'Contact') else 'flight'
            for f, g, t in r['materials']:
                self.add_material(g, sub_role, path)
            if r['cls'] == 23:
                f = active_filters.get(r['go'])
                if f:
                    for fid, g in f['mesh']:
                        mesh_ref(fid, g, sub_role, 'MeshFilter.m_Mesh (%s)' % r['goName'], 'this mesh renderer draws nothing')
            elif r['cls'] == 137:
                for fid, g in r.get('meshes', []):
                    mesh_ref(fid, g, sub_role, 'SkinnedMeshRenderer.m_Mesh (%s)' % r['goName'],
                             'this skinned renderer draws nothing')
        # Sub-meshes of one model file are addressed by hashed fileIDs that cannot be mapped to geometry
        # names offline: when a prefab uses as many distinct ids as the file has geometries the sum is exact.
        for (g, sub_role), ids in fbx_refs.items():
            geos = self.c.fbx_info(self.c.path_of(g))
            label = short(self.c.path_of(g))
            if isinstance(geos, Exception) or not geos:
                self.mesh_instances.append({'role': sub_role, 'mesh': label, 'tris': None, 'count': 1,
                                            'exact': False, 'source': short(path)})
            elif len(geos) == 1:
                self.mesh_instances.append({'role': sub_role, 'mesh': label, 'tris': geos[0][1],
                                            'count': sum(ids.values()), 'exact': True, 'source': short(path)})
            else:
                top = sorted((t for _, t in geos), reverse=True)[:len(ids)]
                exact = len(ids) == len(geos) and all(n == 1 for n in ids.values())
                self.mesh_instances.append({'role': sub_role,
                                            'mesh': label + ' (%d of %d sub-meshes)' % (len(ids), len(geos)),
                                            'tris': sum(top), 'count': 1, 'exact': exact, 'source': short(path)})
        renderer_by_go = {r['go']: r for r in parsed['renderers'] if r['cls'] == 199}
        for s in parsed['systems']:
            if not s['active']:
                continue
            sub_role = role
            if role == 'flight':
                sub_role = 'contact' if self._under(parsed, s['go'], 'Contact') else 'flight'
            r = renderer_by_go.get(s['go'])
            mesh_tris = None
            if r is not None and r.get('renderMode') == 4:
                for fid, g in r.get('meshes', []):
                    if g in BUILTIN_GUIDS or self.c.path_of(g):
                        t, _, _ = self.c.mesh_tris(fid, g)
                        mesh_tris = max(mesh_tris or 0, t or 0)
                    else:
                        reported.add(g)
                        self.broken.append({'guid': g, 'in': short(path),
                                            'key': 'ParticleSystemRenderer.m_Mesh (%s, mesh render mode)' % s['goName'],
                                            'role': sub_role,
                                            'effect': 'mesh-mode particle system has no mesh: it draws nothing'})
            elif r is not None:
                for fid, g in r.get('meshes', []):
                    if g not in BUILTIN_GUIDS and not self.c.path_of(g):
                        reported.add(g)
                        self.broken.append({'guid': g, 'in': short(path),
                                            'key': 'ParticleSystemRenderer.m_Mesh (%s, render mode %s: mesh unused)'
                                                   % (s['goName'], r.get('renderMode')),
                                            'role': sub_role, 'effect': 'no visible effect (billboard mode ignores the mesh)'})
            # ParticleSystemShapeType: 6 Mesh (13 MeshRenderer / 14 SkinnedMeshRenderer read a scene reference, not m_Mesh)
            for fid, g in s.get('shapeMesh', []):
                if g in BUILTIN_GUIDS or self.c.path_of(g):
                    continue
                reported.add(g)
                reads = s.get('shapeEnabled') and s.get('shapeType') == 6
                self.broken.append({'guid': g, 'in': short(path),
                                    'key': 'ParticleSystem.ShapeModule.m_Mesh (%s, shape type %s%s)'
                                           % (s['goName'], s.get('shapeType'), '' if s.get('shapeEnabled') else ', shape off'),
                                    'role': sub_role,
                                    'effect': 'mesh-shaped emitter has no mesh: particles are not emitted from the intended surface' if reads
                                    else 'no visible effect (this emission shape does not read a mesh)'})
            self.systems.append({'role': sub_role, 'source': short(path), 'name': s['goName'], 'looping': s['looping'],
                                 'prewarm': s['prewarm'], 'length': s['length'], 'maxParticles': s['maxParticles'],
                                 'meshTris': mesh_tris, 'startColorMax': s['startColorMax'],
                                 'rendererEnabled': bool(r and r['enabled'])})
        for l in parsed['lights']:
            if l['active'] and l['enabled']:
                self.lights.append({'role': role, 'source': short(path), 'name': l['goName'], 'type': l['type'],
                                    'intensity': l['intensity'], 'range': l['range']})
        for inst in parsed['instances']:
            for f, g, t in inst['overrides']:
                p = self.c.path_of(g)
                if p and p.lower().endswith('.mat'):
                    self.add_material(g, role, path)
            for layer in inst['layerOverrides']:
                self.layers[layer] += 1
            if inst['guid']:
                self.walk_prefab(inst['guid'], role, path, depth + 1, seen)
        for b in parsed['behaviours']:
            for key, f, g in b['refs']:
                p = self.c.path_of(g)
                if not p:
                    if g not in BUILTIN_GUIDS:
                        reported.add(g)
                        self.broken.append({'guid': g, 'in': short(path), 'key': 'MonoBehaviour.' + key, 'role': role,
                                            'effect': 'script field is null at run time'})
                    continue
                low = p.lower()
                if low.endswith('.prefab'):
                    self.walk_prefab(g, role, path, depth + 1, seen)
                elif low.endswith('.mat'):
                    self.add_material(g, role, path)
        # anything else that does not resolve (particle shape meshes, sub-emitters, textures on inactive objects ...)
        other = defaultdict(Counter)
        here = short(path)
        reported |= set(b['guid'] for b in self.broken if b['in'] == here)
        for cls, key, f, g in parsed['rawRefs']:
            if g in BUILTIN_GUIDS or g in reported or self.c.path_of(g):
                continue
            other[g][(cls, key)] += 1
        class_name = {198: 'ParticleSystem', 199: 'ParticleSystemRenderer', 23: 'MeshRenderer', 33: 'MeshFilter',
                      1001: 'PrefabInstance', 114: 'MonoBehaviour', 137: 'SkinnedMeshRenderer'}
        for g, keys in other.items():
            (cls, key), n = keys.most_common(1)[0]
            effect = {198: 'particle module reference is null (shape mesh / sub-emitter): that module falls back or emits nothing',
                      199: 'renderer reference on an inactive or disabled object: no visible effect now',
                      23: 'renderer reference on an inactive or disabled object: no visible effect now',
                      33: 'mesh filter on an inactive object or without a renderer: no visible effect now'}.get(
                cls, 'reference is null at run time')
            self.broken.append({'guid': g, 'in': short(path), 'key': '%s.%s x%d' % (class_name.get(cls, 'class%d' % cls), key, sum(keys.values())),
                                'role': role, 'effect': effect})

    @staticmethod
    def _under(parsed, go, ancestor_name):
        return Bucket._ancestor(parsed, go, ancestor_name)

    @staticmethod
    def _ancestor(parsed, go, ancestor_name):
        if '_go_parent' not in parsed:
            text = read(parsed['path'])
            parts = re.split(r'^--- !u!(\d+) &(-?\d+)( stripped)?\n', text, flags=re.M)
            tr = {}
            for i in range(1, len(parts), 4):
                if int(parts[i]) in (4, 224) and not parts[i + 2]:
                    body = parts[i + 3]
                    g = re.search(r'\n  m_GameObject: \{fileID: (-?\d+)\}', body)
                    f = re.search(r'\n  m_Father: \{fileID: (-?\d+)\}', body)
                    tr[parts[i + 1]] = (g.group(1) if g else None, f.group(1) if f else '0')
            go_parent = {}
            for tid, (g, father) in tr.items():
                parent = tr.get(father)
                go_parent[g] = parent[0] if parent else None
            parsed['_go_parent'] = go_parent
        hop = 0
        while go and hop < 64:
            info = parsed['objects'].get(go)
            if info and info['name'] == ancestor_name:
                return True
            go = parsed['_go_parent'].get(go)
            hop += 1
        return False


# ----------------------------------------------------------------------------------------------- profile
def parse_fields(text):
    """Top-level serialized fields of a single-object YAML asset: name -> raw text (may span lines)."""
    body = text.split('\nMonoBehaviour:\n', 1)[-1]
    fields = OrderedDict()
    current = None
    for line in body.splitlines():
        m = re.match(r'^  ([A-Za-z_]\w*):(.*)$', line)
        if m:
            current = m.group(1)
            fields[current] = m.group(2).strip()
        elif current is not None and (line.startswith('   ') or line.startswith('  -')):
            fields[current] += '\n' + line
    return fields


def colour(raw):
    m = re.match(r'\{r: ([-\d.eE+]+), g: ([-\d.eE+]+), b: ([-\d.eE+]+)', raw or '')
    return tuple(float(x) for x in m.groups()) if m else None


def hexcolour(rgb):
    if not rgb:
        return None
    return '#%02X%02X%02X' % tuple(max(0, min(255, int(round(c * 255)))) for c in rgb)


def profile_fields_used_by(code_files, field_names):
    used = set()
    for path in code_files:
        if not os.path.isfile(path):
            continue
        text = read(path)
        for name in re.findall(r'\b(?:p|profile|Profile|_profile|source)\.(\w+)', text):
            if name in field_names:
                used.add(name)
    return used


def runtime_role_of(field):
    """Roles of prefabs a live data asset (summon / EA profile copy) hands to the game."""
    if 'Presentation' in field:
        return 'actor'      # the summoned creature itself: on screen for its whole active time
    if field.endswith('Seal'):
        return 'cast'       # formation seal: plays once while the creature forms
    if field.endswith('Debris'):
        return 'debris'
    return 'attack'         # attack bodies (flame cone, root strike): one-shot per attack


def role_of(field):
    if field in ('NativeCastPrefab',):
        return 'cast'
    if field in ('NativeImpactPrefab',):
        return 'impact'
    if field in ('GuardContactPrefab', 'AreaContactPrefab', 'WardContactPrefab'):
        return 'contact'
    if field.endswith('Debris') or field == 'WardDebrisPrefab':
        return 'debris'
    if field in ('NativeFieldPrefab', 'WardPatternPrefab') or field.endswith('Seal'):
        return 'field'
    if field == 'Bolt300Prefab':
        return 'flight'
    return 'body'


def analyse_profile(census, profile_path, code_usage, sustain_seconds):
    text = read(profile_path)
    fields = parse_fields(text)
    names = set(fields)

    def num(name, default=0.0):
        try:
            return float(fields.get(name, default))
        except (TypeError, ValueError):
            return default

    glyph = unquote(fields.get('Glyph', ''))
    path_kind, used = 'general', None
    for name in PRESENTATIONS:
        if fields.get(name + 'Presentation', '0') == '1':
            path_kind, used = name + 'Presentation', code_usage.get(name, set())
            break
    if used is None and int(num('WardKind')) != 0:
        path_kind, used = 'FixedWard', code_usage.get('FixedWard', set())
    if used is None and int(num('AreaRemake')) != 0:
        path_kind, used = 'AreaRemake', code_usage.get('AreaRemake', set())
    if used is None:
        used = names - set(n for p in PRESENTATIONS for n in names if n.startswith(p)) - \
            set(n for n in names if n.startswith('Ward'))
        if 'guid' in fields.get('Bolt300Prefab', ''):
            # Vfx120Effect.Bolt300: the flight body replaces the bespoke/procedural body. IsBambooBolt and IsFireBolt
            # both require Bolt300Prefab == null, and the generated parts are suppressed by ReplacesProcedural.
            path_kind = 'Bolt300'
            used = used - {'BodyMesh', 'AccentMesh', 'GuardianMeshes', 'BodyMaterial', 'InkMaterial', 'PatternMaterial',
                           'MistMaterial', 'BotanicalPrefab', 'SummonPrefab'}
            if int(num('NativeBodyMotion')) == 0:
                used = used - {'NativeBodyPrefab'}
    replaced_by = None
    if path_kind == 'Bolt300':
        replaced_by = 'Bolt300Prefab'
    elif path_kind == 'general':
        if 'guid' in fields.get('BotanicalPrefab', '') and int(num('BotanicalKind')) != 0:
            replaced_by = 'BotanicalPrefab'
        elif 'guid' in fields.get('NativeBodyPrefab', ''):
            replaced_by = 'NativeBodyPrefab'
        elif 'guid' in fields.get('SummonPrefab', ''):
            replaced_by = 'SummonPrefab'
        elif 'guid' in fields.get('NativeFieldPrefab', '') and fields.get('NativeReplaceBody', '0') == '1':
            replaced_by = 'NativeFieldPrefab (NativeReplaceBody)'
    bucket = Bucket(census)
    unused_refs = []
    mesh_fields = {}
    for field, raw in fields.items():
        if field in ('m_Script',):
            continue
        refs = REF.findall(raw)
        if not refs:
            continue
        role = role_of(field)
        on_path = field in used
        for file_id, guid, kind in refs:
            path = census.path_of(guid)
            if guid in BUILTIN_GUIDS:
                if 'Mesh' in field and on_path:
                    bucket.add_mesh(file_id, guid, role, profile_path)
                continue
            if not path:
                entry = {'guid': guid, 'in': short(profile_path), 'key': 'Profile.' + field, 'role': role,
                         'effect': 'profile slot is null at run time'}
                (bucket.broken if on_path else unused_refs).append(entry)
                continue
            if not on_path:
                unused_refs.append({'field': field, 'target': short(path)})
                # broken references further down an unused slot are still worth one line
                if path.lower().endswith('.prefab'):
                    probe = Bucket(census)
                    probe.walk_prefab(guid, role, profile_path)
                    for b in probe.broken:
                        b['unusedPath'] = True
                        unused_refs.append(b)
                continue
            low = path.lower()
            if low.endswith('.mat'):
                bucket.add_material(guid, role, profile_path)
            elif low.endswith('.prefab') or low.endswith('.fbx') and kind == '3' and 'Mesh' not in field:
                bucket.walk_prefab(guid, role, profile_path)
            elif 'Mesh' in field or low.endswith('.fbx') or census.mesh_asset_tris(path) is not None:
                tris, label, exact = census.mesh_tris(file_id, guid)
                mesh_fields.setdefault(field, []).append({'mesh': label, 'tris': tris, 'exact': exact})
    info = {
        'glyph': glyph, 'path': profile_path, 'family': unquote(fields.get('Family', '')),
        'title': unquote(fields.get('Title', '').split('\n')[0]) if fields.get('Title') else '',
        'assigned': fields.get('Assigned', '0') == '1', 'behavior': int(num('Behavior')), 'layout': int(num('Layout')),
        'count': int(num('Count', 10)), 'ribbonCount': int(num('RibbonCount', 2)), 'duration': num('Duration', 3),
        'flight': num('Flight', .6), 'useMist': fields.get('UseMist', '1') == '1',
        'useOriginalKtp': fields.get('UseOriginalKtp', '0') == '1',
        'ktpBrightness': num('KtpBrightness', 1), 'ktpContactBrightness': num('KtpContactBrightness', 0),
        'pigment': colour(fields.get('Pigment')), 'accent': colour(fields.get('Accent')), 'ink': colour(fields.get('Ink')),
        'dispatch': path_kind, 'meshFields': mesh_fields, 'unusedRefs': unused_refs, 'bodyReplacedBy': replaced_by,
        # Vfx120AreaRemake: 1 BambooField (17 planned spikes), 3 NeedleVolley (24 shots in the live book)
        'areaBodyCount': {1: 17, 3: 24}.get(int(num('AreaRemake')), 1) if path_kind == 'AreaRemake' else 1,
        'usedFields': sorted(f for f in used if f in names and REF.search(fields[f] or '')),
        'life': max(num('Duration', 3), num('Flight', .6) + .9),
    }
    return info, bucket


BEHAVIOR = ['Projectile', 'Bind', 'Heal', 'Shield', 'Zone', 'Summon', 'Weapon', 'Buff', 'Burst', 'Wave', 'Reserve']
LIGHT_TYPE = {0: 'Spot', 1: 'Directional', 2: 'Point', 3: 'Area'}


def summarise(census, info, bucket, state, sustain_seconds, tri_budget):
    """Turn a profile + bucket into the per-variant record written to static308.json."""
    life = info['life']
    long_lived = life >= sustain_seconds
    mats = []
    for guid, usage in bucket.materials.items():
        m = census.material(guid)
        if m is None:
            continue
        roles = sorted(usage['roles'])
        sustained_role = any(r in SUSTAINED_ROLES for r in roles)
        flight_role = not sustained_role and any(r in FLIGHT_ROLES for r in roles)
        mats.append({
            'flightRole': flight_role,
            'path': short(m['path']), 'roles': roles, 'shader': m['shader'], 'blend': m['blend'],
            'blendSource': m['blendSource'], 'additive': m['additive'], 'emissive': m['emissive'],
            'emissiveReasons': m['emissiveReasons'], 'hdrColors': m['hdrColors'], 'queue': m['queue'],
            'hasTexture': any(not t[1].startswith('MISSING') for t in m['textures']),
            'missingTextures': m['missingTextures'],
            'sustainedRole': sustained_role, 'shaderEmissionTerm': m['shaderMentionsEmission'],
        })
    add_s = [m for m in mats if m['additive'] and m['sustainedRole']]
    add_f = [m for m in mats if m['additive'] and m['flightRole']]
    add_m = [m for m in mats if m['additive'] and not m['sustainedRole'] and not m['flightRole']]
    emi_s = [m for m in mats if m['emissive'] and m['sustainedRole']]
    emi_f = [m for m in mats if m['emissive'] and m['flightRole']]
    emi_m = [m for m in mats if m['emissive'] and not m['sustainedRole'] and not m['flightRole']]

    # triangles
    body_field = [m for m in bucket.mesh_instances if m['role'] in SUSTAINED_ROLES + FLIGHT_ROLES]
    momentary = [m for m in bucket.mesh_instances if m['role'] not in SUSTAINED_ROLES + FLIGHT_ROLES]
    prefab_sustained = sum((m['tris'] or 0) * m['count'] for m in body_field)
    prefab_momentary = sum((m['tris'] or 0) * m['count'] for m in momentary)
    body = sum((x['tris'] or 0) for x in info['meshFields'].get('BodyMesh', []))
    accent = sum((x['tris'] or 0) for x in info['meshFields'].get('AccentMesh', []))
    guardian = sum((x['tris'] or 0) for x in info['meshFields'].get('GuardianMeshes', []))
    other = sum((x['tris'] or 0) for f, xs in info['meshFields'].items()
                if f not in ('BodyMesh', 'AccentMesh', 'GuardianMeshes') for x in xs)
    procedural = state in ('shared', 'blank') and info['dispatch'] == 'general'
    multiplier = info.get('areaBodyCount', 1)
    if multiplier > 1:
        # Vfx120Effect.AreaRemake instantiates NativeBodyPrefab once per spike (17) or per shot (24 in the live book)
        extra = sum((m['tris'] or 0) * m['count'] * (multiplier - 1) for m in body_field if m['role'] == 'body')
        prefab_sustained += extra
    if info['dispatch'] == 'AreaRemake':
        profile_tris = guardian + other
        basis = 'AreaRemake: NativeBodyPrefab x %d (count fixed in Vfx120Effect.AreaRemake)' % multiplier
    elif info.get('bodyReplacedBy'):
        # Vfx120Effect.ReplacesProcedural: a prefab body suppresses the generated BodyMesh/AccentMesh parts
        profile_tris = guardian + other
        basis = 'body replaced by %s: profile BodyMesh/AccentMesh are not drawn' % info['bodyReplacedBy']
    elif procedural:
        count = max(1, min(32, info['count']))
        profile_tris = count * body + count * accent
        basis = 'procedural: Count(%d) x BodyMesh(%d) + Count x AccentMesh(%d)' % (count, body, accent)
    else:
        profile_tris = body + accent + guardian + other
        basis = 'dedicated path: each profile mesh counted once (real multiplicity is decided in code)'
    unknown = [m['mesh'] for m in bucket.mesh_instances if m['tris'] is None] + \
              [x['mesh'] for xs in info['meshFields'].values() for x in xs if x['tris'] is None]
    estimate = profile_tris + prefab_sustained
    particles = bucket.systems
    max_particles = sum(s['maxParticles'] for s in particles)
    looping_s = [s for s in particles if s['looping'] and s['role'] in SUSTAINED_ROLES]
    looping_m = [s for s in particles if s['looping'] and s['role'] not in SUSTAINED_ROLES]
    mesh_particle_tris = sum(s['maxParticles'] * s['meshTris'] for s in particles if s['meshTris'])

    layers = {census.layer_name(k): v for k, v in sorted(bucket.layers.items())}
    after_fog = any(census.layer_name(k) == 'VfxAfterFog' for k in bucket.layers)

    pig, acc = info['pigment'], info['accent']
    hsv = colorsys.rgb_to_hsv(*pig) if pig else None
    acc_hsv = colorsys.rgb_to_hsv(*acc) if acc else None
    textured = sorted(set(m['path'] for m in mats if m['hasTexture'] and m['sustainedRole']))
    hdr_particles = [s for s in particles if s['startColorMax'] and s['startColorMax'] > 1.001]

    record = OrderedDict()
    record['profile'] = short(info['path'])
    record['dispatch'] = info['dispatch']
    record['family'] = info['family']
    record['behavior'] = BEHAVIOR[info['behavior']] if 0 <= info['behavior'] < len(BEHAVIOR) else str(info['behavior'])
    record['layout'] = info['layout']
    record['count'] = info['count']
    record['durationSeconds'] = info['duration']
    record['flightSeconds'] = info['flight']
    record['lifeSeconds'] = round(life, 3)
    record['longLived'] = long_lived
    record['usedFields'] = info['usedFields']
    record['unusedRefs'] = info['unusedRefs']
    record['prefabs'] = [{'path': short(p), 'role': r} for p, r in bucket.prefabs.items()]
    record['materials'] = mats
    record['emission'] = {
        'additiveSustained': [m['path'] for m in add_s], 'additiveMomentary': [m['path'] for m in add_m],
        'additiveFlight': [m['path'] for m in add_f],
        'emissiveSustained': [{'path': m['path'], 'why': m['emissiveReasons']} for m in emi_s],
        'emissiveMomentary': [{'path': m['path'], 'why': m['emissiveReasons']} for m in emi_m],
        'emissiveFlight': [{'path': m['path'], 'why': m['emissiveReasons']} for m in emi_f],
        'ktpBrightness': info['ktpBrightness'], 'ktpContactBrightness': info['ktpContactBrightness'],
        'hdrParticleStartColours': [{'source': s['source'], 'name': s['name'], 'max': s['startColorMax'],
                                     'role': s['role']} for s in hdr_particles],
        # census class, not a verdict: "sustained candidate" = an additive or emissive material sits in a role that
        # lives for the whole effect AND the effect lives at least --sustain-seconds.
        'sustainedCandidate': bool((add_s or emi_s) and long_lived),
        'shortLivedOnly': bool((add_s or emi_s) and not long_lived),
        'momentaryOnly': bool(not add_s and not emi_s and (add_m or emi_m or add_f or emi_f)),
    }
    record['lights'] = bucket.lights
    record['particles'] = {
        'systems': len(particles), 'maxParticlesSum': max_particles,
        'loopingSustained': [{'source': s['source'], 'name': s['name']} for s in looping_s],
        'loopingMomentary': [{'source': s['source'], 'name': s['name']} for s in looping_m],
        'prewarm': sum(1 for s in particles if s['prewarm']),
        'meshParticleUpperTris': mesh_particle_tris,
        'billboardUpperTris': 2 * sum(s['maxParticles'] for s in particles if not s['meshTris']),
    }
    record['triangles'] = {
        'estimate': estimate, 'basis': basis, 'profileMeshes': info['meshFields'],
        'prefabSustained': prefab_sustained, 'prefabMomentary': prefab_momentary,
        'instances': bucket.mesh_instances, 'unknownMeshes': sorted(set(unknown)),
        'budget': tri_budget, 'overBudget': estimate > tri_budget,
        'note': 'static estimate of mesh bodies only; generated meshes, ribbons and particles are not included. '
                'The editor sweep records the rendered count per beat.',
    }
    record['layers'] = {'serialized': layers, 'usesVfxAfterFog': after_fog,
                        'note': 'objects created by Vfx120Effect at run time stay on Default (no code assigns a layer)'}
    record['colour'] = {
        'pigment': hexcolour(pig), 'accent': hexcolour(acc), 'ink': hexcolour(info['ink']),
        'pigmentHsv': [round(x, 3) for x in hsv] if hsv else None,
        'accentHsv': [round(x, 3) for x in acc_hsv] if acc_hsv else None,
        'texturedSustainedMaterials': textured,
        'source': 'profile pigment/accent/ink via property block' + (' + textures' if textured else ''),
    }
    record['brokenRefs'] = bucket.broken
    return record


# ----------------------------------------------------------------------------------------------- inputs
def read_csv_glyphs():
    rows = []
    with open(CSV, 'r', encoding='utf-8-sig') as fh:
        lines = fh.read().splitlines()
    import csv as csvmod
    reader = csvmod.reader(lines)
    header = next(reader)
    for row in reader:
        if not row or not row[0].strip():
            continue
        rows.append(dict(zip(header, row)))
    return rows


def read_catalog():
    text = read(CATALOG)
    entries = []
    for g, pf, pg, rf, rg, c in re.findall(
            r'- Glyph: (.*)\n\s+Profile: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}\n'
            r'\s+Prefab: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}\n\s+GameplayConnected: (\d)', text):
        entries.append({'glyph': unquote(g), 'profileGuid': pg or None, 'prefabGuid': rg or None,
                        'prefabFileId': rf, 'connected': c == '1'})
    return entries


def read_visual_set(path):
    text = read(path)
    out = []
    for letter, fid, guid, scale, arc in re.findall(
            r'- Letter: (.*)\n\s+FxPrefab: \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}), type: \d)?\}\n'
            r'\s+ScaleMul: ([-\d.eE+]+)\n\s+ArcHeight: ([-\d.eE+]+)', text):
        out.append({'letter': unquote(letter), 'guid': guid or None, 'scale': float(scale), 'arc': float(arc)})
    return out


def find_live_visual_set(index):
    """The visual set the main scene's BrushStrokeFeedAdapter really references."""
    found = []
    if os.path.isfile(MAIN_SCENE):
        with open(MAIN_SCENE, 'r', encoding='utf-8', errors='replace') as fh:
            for line in fh:
                if '_visualSet:' in line:
                    m = REF.search(line)
                    if m:
                        found.append(m.group(2))
    paths = sorted(set(index.get(g, 'MISSING:' + g) for g in found))
    return paths, len(found)


def scene_guids():
    guids = set()
    if os.path.isfile(MAIN_SCENE):
        with open(MAIN_SCENE, 'r', encoding='utf-8', errors='replace') as fh:
            for line in fh:
                if 'guid:' in line:
                    for m in re.finditer(r'guid: ([0-9a-f]{32})', line):
                        guids.add(m.group(1))
    return guids


def effect_profile_of(census, prefab_path):
    """Profile guid carried by the Vfx120Effect on a prefab root (None when the prefab has no such component)."""
    parsed = census.prefab(prefab_path)
    for b in parsed['behaviours']:
        for key, f, g in b['refs']:
            if key == 'Profile':
                return g
    return None


# ----------------------------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--reindex', action='store_true', help='rebuild the GUID index (several minutes on a cold disk)')
    ap.add_argument('--index-max-age-hours', type=float, default=12.0)
    ap.add_argument('--sustain-seconds', type=float, default=1.5,
                    help='reporting threshold only: an effect that lives at least this long counts as "sustained"')
    ap.add_argument('--tri-budget', type=int, default=17000, help='SPEC-SPELL-FX-REWORK per-glyph line')
    ap.add_argument('--out', default=NOTES)
    ap.add_argument('--probe-quarantine', action='store_true',
                    help='also look for broken GUIDs in ../Oheangbu_Unused307 (reads every .meta there; slow)')
    ap.add_argument('--quiet', action='store_true')
    args = ap.parse_args()
    started = time.time()

    index, index_age, rebuilt = load_index(args.reindex, args.index_max_age_hours)
    census = Census(index)

    csv_rows = read_csv_glyphs()
    catalog = read_catalog()
    coverage = {}
    if os.path.isfile(COVERAGE_JSON):
        with open(COVERAGE_JSON, 'r', encoding='utf-8') as fh:
            for g in json.load(fh).get('glyphs', []):
                coverage[g['glyph']] = g

    # profiles were serialized at different times: older files lack the newer fields, so take the union
    field_names = set()
    for e in catalog:
        p = census.path_of(e['profileGuid']) if e['profileGuid'] else None
        if p:
            field_names |= set(parse_fields(read(p)).keys())
    code_usage = {}
    for name in PRESENTATIONS:
        code_usage[name] = profile_fields_used_by([RUNTIME_CODE + '/%sVfx.cs' % name], field_names)
    code_usage['FixedWard'] = profile_fields_used_by([RUNTIME_CODE + '/FixedWardVfx.cs'], field_names)
    code_usage['AreaRemake'] = profile_fields_used_by(
        [RUNTIME_CODE + '/Vfx120Effect.AreaRemake.cs', RUNTIME_CODE + '/Vfx120Effect.ProceduralRift.cs',
         RUNTIME_CODE + '/ProceduralRiftPath.cs'], field_names)

    # ---- visual sets
    live_paths, live_refs = find_live_visual_set(index)
    live_path = live_paths[0] if live_paths and not live_paths[0].startswith('MISSING') else None
    vs_original = read_visual_set(VISUAL_SET_ORIGINAL)
    vs_live = read_visual_set(live_path) if live_path else []
    main_guids = scene_guids()

    def set_report(entries, label):
        letters = [e['letter'] for e in entries]
        want = [r['글자'] for r in csv_rows]
        return {
            'asset': label, 'count': len(entries), 'uniqueLetters': len(set(letters)),
            'missingGlyphs': [g for g in want if g not in letters],
            'extraGlyphs': [g for g in letters if g not in want],
            'duplicateLetters': [g for g, n in Counter(letters).items() if n > 1],
            'nullPrefabs': [e['letter'] for e in entries if not e['guid']],
            'unresolvedPrefabs': [e['letter'] for e in entries if e['guid'] and not index.get(e['guid'])],
            'nonDefaultScale': [(e['letter'], e['scale']) for e in entries if abs(e['scale'] - 1) > 1e-4],
            'nonZeroArc': [(e['letter'], e['arc']) for e in entries if abs(e['arc']) > 1e-4],
        }

    # ---- live data assets that reference VFX content (EA runtimes, summons)
    live_refs_by_glyph = defaultdict(list)
    catalog_prefab_glyph = {e['prefabGuid']: e['glyph'] for e in catalog if e['prefabGuid']}
    profile_copies = {}
    live_assets = []
    if os.path.isdir(LIVE_DATA):
        for name in sorted(os.listdir(LIVE_DATA)):
            if not name.endswith('.asset'):
                continue
            full = LIVE_DATA + '/' + name
            if re.search(r'_\d{3}_[0-9A-F]{4}\.asset$', name):
                meta = read(full + '.meta')
                m = re.search(r'guid: ([0-9a-f]{32})', meta)
                profile_copies[m.group(1) if m else name] = full
                continue
            if 'SpellVisualSet' in name:
                continue
            if os.path.getsize(full) > 4_000_000:
                continue
            text = read(full)
            m = re.search(r'm_Script: \{fileID: -?\d+, guid: ([0-9a-f]{32})', text)
            if not m:
                continue
            fields = parse_fields(text)
            letter = unquote(fields.get('Letter', '')) if 'Letter' in fields else None
            hits = []
            for field, raw in fields.items():
                if field == 'm_Script' or SKIP_RUNTIME_FIELD.search(field):
                    continue
                for f, g, t in REF.findall(raw):
                    p = index.get(g)
                    if not p:
                        continue
                    if g in catalog_prefab_glyph:
                        hits.append((field, g, catalog_prefab_glyph[g]))
                    elif letter and (p.lower().endswith('.prefab') or p.lower().endswith('.mat')
                                     or p.lower().endswith('.fbx') or 'Mesh' in field):
                        hits.append((field, g, letter))
                    elif '/SpellVFX120/' in p and p.lower().endswith('.prefab'):
                        hits.append((field, g, None))
            if hits:
                live_assets.append({'asset': full, 'inMainScene': bool(m) and (re.search(
                    r'guid: ([0-9a-f]{32})', read(full + '.meta')).group(1) in main_guids), 'hits': hits})
                for field, g, glyph in hits:
                    live_refs_by_glyph[glyph].append({'asset': full, 'field': field, 'guid': g,
                                                      'target': index.get(g)})

    # ---- per glyph
    glyph_records = []
    all_broken = []
    order_matches = [e['glyph'] for e in catalog] == [r['글자'] for r in csv_rows]
    vs_live_by = {e['letter']: e for e in vs_live}
    vs_orig_by = {e['letter']: e for e in vs_original}
    mismatch_prefab_profile, stale_flags = [], []
    for i, entry in enumerate(catalog):
        glyph = entry['glyph']
        row = csv_rows[i] if i < len(csv_rows) else {}
        cov = coverage.get(glyph, {})
        vfx_cov = cov.get('vfx', {})
        impl = cov.get('impl', {})
        state = vfx_cov.get('state') or ('blank' if row.get('분류', '') == '공백' else 'unknown')
        rec = OrderedDict()
        rec['glyph'] = glyph
        rec['index'] = i + 1
        rec['id'] = '%03d_%04X' % (i + 1, ord(glyph[0])) if glyph else '%03d_????' % (i + 1)
        rec['element'] = row.get('속성', ELEMENT_OF_INITIAL.get(row.get('초성', ''), ''))
        rec['vowel'] = row.get('중성', '')
        rec['final'] = row.get('종성', '')
        rec['category'] = row.get('분류', '')
        rec['csvEffect'] = row.get('효과', '')
        rec['bodyState'] = state
        rec['implStatus'] = impl.get('status')
        rec['reachableInGame'] = impl.get('reachableInGame')
        profile_path = census.path_of(entry['profileGuid']) if entry['profileGuid'] else None
        prefab_path = census.path_of(entry['prefabGuid']) if entry['prefabGuid'] else None
        rec['catalog'] = {'profile': short(profile_path), 'prefab': short(prefab_path),
                          'gameplayConnectedFlag': entry['connected']}
        if prefab_path:
            carried = effect_profile_of(census, prefab_path)
            if carried != entry['profileGuid']:
                mismatch_prefab_profile.append({'glyph': glyph, 'prefabCarries': short(census.path_of(carried)) if carried else None,
                                                'catalogSays': short(profile_path)})
        variants = OrderedDict()
        if profile_path:
            info, bucket = analyse_profile(census, profile_path, code_usage, args.sustain_seconds)
            if prefab_path:
                bucket.walk_prefab(entry['prefabGuid'], 'body', CATALOG)
            if info['glyph'] != glyph:
                rec.setdefault('problems', []).append('profile Glyph %r differs from catalogue glyph' % info['glyph'])
            variants['catalog'] = summarise(census, info, bucket, state, args.sustain_seconds, args.tri_budget)
        # what the main scene plays for a successful cast: the live visual set entry
        live_entry = vs_live_by.get(glyph)
        live = OrderedDict()
        live['visualSetPrefab'] = short(census.path_of(live_entry['guid'])) if live_entry and live_entry['guid'] else None
        live['sameAsCatalog'] = bool(live_entry and live_entry['guid'] == entry['prefabGuid'])
        live['originalSetPrefab'] = short(census.path_of(vs_orig_by[glyph]['guid'])) if glyph in vs_orig_by and vs_orig_by[glyph]['guid'] else None
        live['runtimeData'] = [{'asset': short(x['asset']), 'field': x['field'], 'target': short(x['target'])}
                               for x in live_refs_by_glyph.get(glyph, [])]
        resolves = impl.get('status') in ('implemented', 'implemented_with_defect', 'partial')
        live['castResolvesInGame'] = resolves if impl else None
        # reachability in the main game comes from coverage.json too: a resolver path is not the same as a cast the
        # player can make (unlock order, missing unlock content)
        live['reachableInGame'] = impl.get('reachableInGame') if impl else None
        # A summon whose combat actor comes from runtime data owns its whole presentation: CombatLoopWiring passes
        # externallyOwned to BrushStrokeFeedAdapter.SetPatternSummonPose and SpawnPattern returns before the visual-set
        # prefab is spawned ("retain the stroke fade without a second static animal").
        summon_actor = [x for x in live_refs_by_glyph.get(glyph, [])
                        if x['field'] == 'PresentationPrefab' and x['guid'] != entry['prefabGuid']]
        live['catalogPrefabPlayed'] = bool(resolves) if impl else None
        if not impl:
            live['plays'] = 'unknown (coverage.json missing)'
        elif not resolves:
            live['plays'] = 'nothing: the cast misfires before any effect is spawned'
        elif vfx_cov.get('mainScenePlays') and 'MumBridgeService' in str(vfx_cov.get('mainScenePlays')):
            live['plays'] = 'own mesh (MumBridgeService), not the catalogue prefab'
            live['catalogPrefabPlayed'] = False
        elif summon_actor:
            live['plays'] = ('summon combat actor from runtime data (%s), not the catalogue prefab: an externally owned summon '
                             'skips the cast visual' % short(summon_actor[0]['target']))
            live['catalogPrefabPlayed'] = False
        elif live_entry and live_entry['guid'] and live_entry['guid'] != entry['prefabGuid']:
            live['plays'] = 'different prefab from the catalogue: ' + (live['visualSetPrefab'] or '-')
        else:
            live['plays'] = 'the catalogue prefab'
        if live_entry and live_entry['guid'] and live_entry['guid'] != entry['prefabGuid']:
            lp = census.path_of(live_entry['guid'])
            if lp:
                carried = effect_profile_of(census, lp)
                cp = census.path_of(carried) if carried else None
                live['profile'] = short(cp)
                if cp:
                    info2, bucket2 = analyse_profile(census, cp, code_usage, args.sustain_seconds)
                    bucket2.walk_prefab(live_entry['guid'], 'body', live_path)
                    variants['live'] = summarise(census, info2, bucket2, 'own', args.sustain_seconds, args.tri_budget)
        # runtime data prefabs (summon actor, seal, debris, EA prefabs that are not the catalogue prefab)
        extra = Bucket(census)
        extra_targets = []
        for x in live_refs_by_glyph.get(glyph, []):
            if x['guid'] == entry['prefabGuid'] or not x['target']:
                continue
            low = x['target'].lower()
            role = runtime_role_of(x['field'])
            if low.endswith('.prefab') or low.endswith('.fbx'):
                extra.walk_prefab(x['guid'], role, x['asset'])
                extra_targets.append(x)
            elif low.endswith('.mat'):
                extra.add_material(x['guid'], role, x['asset'])
                extra_targets.append(x)
        if extra_targets:
            fake = {'glyph': glyph, 'path': extra_targets[0]['asset'], 'family': 'runtime-data', 'behavior': 5,
                    'layout': 0, 'count': 1, 'duration': 20.0, 'flight': 0.0, 'ktpBrightness': 1, 'ktpContactBrightness': 0,
                    'pigment': None, 'accent': None, 'ink': None, 'dispatch': 'runtime-data', 'meshFields': {},
                    'unusedRefs': [], 'usedFields': sorted(set(x['field'] for x in extra_targets)), 'life': 20.0}
            variants['runtimeData'] = summarise(census, fake, extra, 'own', args.sustain_seconds, args.tri_budget)
            variants['runtimeData']['note'] = ('prefabs and materials that a live data asset (summon / EA profile copy) hands '
                                               'to the game for this glyph; life is the summon active time, not a profile value')
        rec['live'] = live
        rec['variants'] = variants
        # stale GameplayConnected flag
        actually = bool(resolves) if impl else None
        if impl and actually != entry['connected']:
            stale_flags.append({'glyph': glyph, 'flag': entry['connected'], 'castResolves': actually})
        for vname, v in variants.items():
            for b in v['brokenRefs']:
                b2 = dict(b)
                b2.update(glyph=glyph, variant=vname)
                all_broken.append(b2)
            for b in v.get('unusedRefs', []):
                if 'guid' in b:
                    b2 = dict(b)
                    b2.update(glyph=glyph, variant=vname, unusedPath=True)
                    all_broken.append(b2)
        glyph_records.append(rec)

    # ---- profile copies under the live data folder
    copy_report = {'count': len(profile_copies), 'referencedByMainScene': [], 'referencedByLiveAssets': [], 'divergent': [],
                   'identical': 0, 'noOriginal': []}
    originals_by_name = {}
    for e in catalog:
        p = census.path_of(e['profileGuid']) if e['profileGuid'] else None
        if p:
            originals_by_name[os.path.basename(p)] = p
    live_asset_text = {}
    for guid, path in profile_copies.items():
        base = re.sub(r'^(?:[0-9a-f]{32}_)+', '', os.path.basename(path))
        if guid in main_guids:
            copy_report['referencedByMainScene'].append(short(path))
        orig = originals_by_name.get(base)
        if not orig:
            copy_report['noOriginal'].append(short(path))
            continue
        a, b = parse_fields(read(orig)), parse_fields(read(path))
        skip = ('m_Name', 'm_EditorClassIdentifier')
        changed = sorted(k for k in set(a) & set(b) if k not in skip and a[k] != b[k])
        missing = sorted(k for k in set(a) - set(b) if k not in skip)
        extra = sorted(k for k in set(b) - set(a) if k not in skip)
        remapped, scalar, stale_targets = [], [], []
        for k in changed:
            ra, rb = REF.search(a[k] or ''), REF.search(b[k] or '')
            if ra and rb:
                remapped.append(k)
                pa, pb = index.get(ra.group(2)), index.get(rb.group(2))
                if pa and pb and os.path.isfile(pa) and os.path.isfile(pb) and os.path.getsize(pa) < 2_000_000:
                    if re.sub(r'\n  m_Name: .*', '', read(pa)) != re.sub(r'\n  m_Name: .*', '', read(pb)):
                        stale_targets.append(k)
            else:
                scalar.append(k)
        if changed or missing or extra:
            copy_report['divergent'].append({
                'copy': short(path), 'original': short(orig), 'inMainScene': guid in main_guids,
                'changedFields': changed, 'missingInCopy': missing, 'onlyInCopy': extra,
                'referenceRemapped': remapped, 'scalarChanged': scalar, 'remappedTargetContentDiffers': stale_targets,
                'fieldCount': len(changed) + len(missing) + len(extra),
                'note': 'a field missing in the copy loads with the class default, not with the original value'})
        else:
            copy_report['identical'] += 1
    # who points at the copies: prefabs anywhere in the VFX tree or the live folder
    copy_guids = set(profile_copies)
    pointing = Counter()
    for root in (LIVE_DATA, VFX):
        for folder, _, files in os.walk(root):
            for name in files:
                if not name.endswith(('.prefab', '.asset')):
                    continue
                full = os.path.join(folder, name).replace(BS, '/')
                if os.path.getsize(full) > 3_000_000:
                    continue
                try:
                    text = read(full)
                except OSError:
                    continue
                if 'guid:' not in text:
                    continue
                for g in set(re.findall(r'guid: ([0-9a-f]{32})', text)):
                    if g in copy_guids:
                        pointing[short(full)] += 1
    copy_report['referencedByLiveAssets'] = [{'asset': a, 'copies': n} for a, n in pointing.most_common(12)]
    copy_report['referencingAssetCount'] = len(pointing)

    # ---- quarantine probe for broken guids (cheap: only when the folder exists and there are few guids)
    broken_guids = sorted(set(b['guid'] for b in all_broken))
    quarantine = {'folder': rel(QUARANTINE), 'exists': os.path.isdir(QUARANTINE), 'searched': False, 'found': {},
                  'note': 'not searched (pass --probe-quarantine)'}
    if args.probe_quarantine and quarantine['exists'] and broken_guids:
        wanted = set(broken_guids)
        rx = re.compile(r'^guid: ([0-9a-f]{32})', re.M)
        metas = 0
        for folder, _, files in os.walk(QUARANTINE):
            for name in files:
                if not name.endswith('.meta'):
                    continue
                metas += 1
                try:
                    with open(os.path.join(folder, name), 'r', encoding='utf-8', errors='replace') as fh:
                        m = rx.search(fh.read(256))
                except OSError:
                    continue
                if m and m.group(1) in wanted:
                    quarantine['found'][m.group(1)] = os.path.join(folder, name)[:-5].replace(BS, '/')
        quarantine['searched'] = True
        quarantine['metasRead'] = metas
        quarantine['note'] = ('searched %d metas: %d of %d broken GUIDs are in the quarantine folder'
                              % (metas, len(quarantine['found']), len(wanted)))

    # ---- aggregation
    def variant(rec, name='catalog'):
        return rec['variants'].get(name)

    def glyphs_where(pred, name='catalog'):
        out = []
        for rec in glyph_records:
            v = variant(rec, name)
            if v is not None and pred(rec, v):
                out.append(rec['glyph'])
        return out

    assigned = [r for r in glyph_records if r['category'] != '공백']
    played = [r for r in glyph_records if r['live'].get('castResolvesInGame')]
    totals = OrderedDict()
    totals['glyphs'] = len(glyph_records)
    totals['assigned'] = len(assigned)
    totals['castResolvesInGame'] = len(played)
    totals['reachableFromStart'] = len([r for r in played if r['live'].get('reachableInGame') == 'yes_from_start'])
    totals['reachableAfterCheongryong'] = len([r for r in played if r['live'].get('reachableInGame') == 'yes_after_cheongryong_unlock'])
    totals['resolvesButNotReachable'] = len([r for r in played if not str(r['live'].get('reachableInGame') or '').startswith('yes')])
    totals['catalogPrefabNotPlayed'] = len([r for r in played if r['live'].get('catalogPrefabPlayed') is False])
    totals['sustainedAdditiveOrEmissive'] = len(glyphs_where(lambda r, v: v['emission']['sustainedCandidate']))
    totals['sustainedAdditive'] = len(glyphs_where(lambda r, v: v['emission']['additiveSustained'] and v['longLived']))
    totals['sustainedEmissiveMaterial'] = len(glyphs_where(lambda r, v: v['emission']['emissiveSustained'] and v['longLived']))
    totals['anyAdditive'] = len(glyphs_where(lambda r, v: v['emission']['additiveSustained'] or v['emission']['additiveMomentary']))
    totals['lights'] = len(glyphs_where(lambda r, v: v['lights']))
    totals['loopingSustainedParticles'] = len(glyphs_where(lambda r, v: v['particles']['loopingSustained']))
    totals['overTriBudget'] = len(glyphs_where(lambda r, v: v['triangles']['overBudget']))
    totals['usesVfxAfterFogCatalog'] = len(glyphs_where(lambda r, v: v['layers']['usesVfxAfterFog']))
    totals['usesVfxAfterFogLive'] = sum(1 for r in glyph_records
                                        if (variant(r, 'live') or variant(r, 'catalog') or {}).get('layers', {}).get('usesVfxAfterFog'))
    totals['brokenRefsUsedPath'] = len([b for b in all_broken if not b.get('unusedPath')])
    totals['brokenRefsUnusedPath'] = len([b for b in all_broken if b.get('unusedPath')])
    totals['brokenGuidsDistinct'] = len(broken_guids)

    # static look-alikes: same family + behaviour + layout + body mesh + accent mesh
    groups = defaultdict(list)
    for rec in assigned:
        v = variant(rec)
        if not v:
            continue
        key = (rec['element'], v['family'], v['behavior'], v['layout'],
               tuple(x['mesh'] for x in v['triangles']['profileMeshes'].get('BodyMesh', [])),
               tuple(x['mesh'] for x in v['triangles']['profileMeshes'].get('AccentMesh', [])), v['dispatch'])
        groups[key].append(rec['glyph'])
    by_glyph = {rec['glyph']: rec for rec in glyph_records}

    def differing(glyphs):
        """Scalar authoring values that still differ inside a group (the group key ignores them)."""
        out = []
        for label, pick in (('수량', lambda v: v['count']), ('수명', lambda v: v['durationSeconds']),
                            ('비행 시간', lambda v: v['flightSeconds']), ('보조색', lambda v: v['colour']['accent']),
                            ('안료', lambda v: v['colour']['pigment']),
                            # the glyph's own catalogue prefab (P/Prefabs/<id>) differs by definition: compare what it pulls in
                            ('쓰는 프리팹', lambda v: tuple(sorted(x['path'] for x in v.get('prefabs') or []
                                                              if not x['path'].startswith('P/Prefabs/'))))):
            try:
                if len(set(pick(variant(by_glyph[g])) for g in glyphs)) > 1:
                    out.append(label)
            except (KeyError, TypeError):
                pass
        return out

    lookalikes = [{'element': k[0], 'family': k[1], 'behavior': k[2], 'layout': k[3],
                   'bodyMesh': list(k[4]), 'accentMesh': list(k[5]), 'glyphs': g,
                   'stillDiffersIn': differing(g),
                   # the group compares CATALOGUE prefabs: these members are drawn by another prefab in the game
                   'replacedInGame': [x for x in g if by_glyph[x]['live'].get('castResolvesInGame')
                                      and not by_glyph[x]['live'].get('sameAsCatalog')]}
                  for k, g in groups.items() if len(g) > 1]
    lookalikes.sort(key=lambda x: (-len(x['glyphs']), x['element']))
    colour_groups = defaultdict(list)
    for rec in assigned:
        v = variant(rec)
        if v:
            colour_groups[(rec['element'], v['colour']['pigment'], v['colour']['accent'])].append(rec['glyph'])
    same_colour = [{'element': k[0], 'pigment': k[1], 'accent': k[2], 'glyphs': g}
                   for k, g in colour_groups.items() if len(g) > 1]
    same_colour.sort(key=lambda x: -len(x['glyphs']))
    saturated = []
    for rec in assigned:
        v = variant(rec)
        if not v:
            continue
        for label in ('pigmentHsv', 'accentHsv'):
            hsv = v['colour'][label]
            if hsv and hsv[1] >= 0.8 and hsv[2] >= 0.9:
                saturated.append({'glyph': rec['glyph'], 'which': label[:-3], 'hsv': hsv})

    integrity = OrderedDict()
    integrity['catalogEntries'] = len(catalog)
    integrity['csvRows'] = len(csv_rows)
    integrity['glyphOrderMatchesCsv'] = order_matches
    integrity['duplicateGlyphs'] = [g for g, n in Counter(e['glyph'] for e in catalog).items() if n > 1]
    integrity['duplicatePrefabs'] = [g for g, n in Counter(e['prefabGuid'] for e in catalog).items() if n > 1]
    integrity['duplicateProfiles'] = [g for g, n in Counter(e['profileGuid'] for e in catalog).items() if n > 1]
    integrity['unresolvedProfiles'] = [e['glyph'] for e in catalog if not e['profileGuid'] or not index.get(e['profileGuid'])]
    integrity['unresolvedPrefabs'] = [e['glyph'] for e in catalog if not e['prefabGuid'] or not index.get(e['prefabGuid'])]
    integrity['prefabProfileMismatch'] = mismatch_prefab_profile
    integrity['assignedFlagMismatch'] = [r['glyph'] for r in glyph_records
                                         if variant(r) and (r['category'] != '공백') != bool(
                                             parse_fields(read(census.path_of(catalog[r['index'] - 1]['profileGuid']))).get('Assigned') == '1')]
    integrity['visualSets'] = {
        'original': set_report(vs_original, rel(VISUAL_SET_ORIGINAL)),
        'live': set_report(vs_live, rel(live_path) if live_path else None),
        'mainSceneReferences': {'scene': rel(MAIN_SCENE), 'fieldHits': live_refs, 'distinctAssets': [rel(p) for p in live_paths]},
        'differences': [{'glyph': g, 'original': short(census.path_of(vs_orig_by[g]['guid'])) if vs_orig_by[g]['guid'] else None,
                         'live': short(census.path_of(vs_live_by[g]['guid'])) if g in vs_live_by and vs_live_by[g]['guid'] else None}
                        for g in vs_orig_by if g in vs_live_by and vs_orig_by[g]['guid'] != vs_live_by[g]['guid']],
        'liveDiffersFromCatalog': [r['glyph'] for r in glyph_records if not r['live']['sameAsCatalog']],
    }
    integrity['staleGameplayConnectedFlags'] = stale_flags
    integrity['brokenRefs'] = all_broken
    integrity['brokenGuids'] = broken_guids
    integrity['quarantineProbe'] = quarantine
    integrity['profileCopiesInLiveData'] = copy_report
    integrity['liveDataAssets'] = [{'asset': short(a['asset']), 'inMainScene': a['inMainScene'],
                                    'refs': [{'field': f, 'target': short(index.get(g)), 'glyph': gl} for f, g, gl in a['hits']]}
                                   for a in live_assets]

    findings = build_findings(glyph_records, totals, integrity, lookalikes, same_colour, saturated, args)

    out = OrderedDict()
    out['meta'] = OrderedDict([
        ('tool', 'Tools/SpellVFX120/spell120_static308.py'),
        ('generatedUtc', datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')),
        ('status', 'CENSUS_ONLY - static read of serialized assets, no Unity, no pixels, no verdict'),
        ('guidIndex', {'entries': len(index), 'rebuiltThisRun': rebuilt, 'ageSeconds': round(index_age)}),
        ('parameters', {'sustainSeconds': args.sustain_seconds, 'triBudget': args.tri_budget}),
        ('sources', {'catalog': rel(CATALOG), 'visualSetOriginal': rel(VISUAL_SET_ORIGINAL),
                     'visualSetLive': rel(live_path) if live_path else None, 'mainScene': rel(MAIN_SCENE),
                     'csv': rel(CSV), 'coverage': rel(COVERAGE_JSON), 'liveData': rel(LIVE_DATA)}),
        ('layerNames', {str(k): v for k, v in census.layer_names.items() if v}),
        ('definitions', OrderedDict([
            ('role', 'body/field/flight/actor = lives for the effect; cast/impact/contact/debris = one-shot'),
            ('additive', 'destination blend factor One (shader source, shader graph m_AlphaMode 2, or material _DstBlend 1)'),
            ('emissive', 'URP _EMISSION keyword with a non-black colour, any HDR colour above 1, or an emission-like '
                         'custom property switched on in the material'),
            ('sustainedCandidate', 'additive or emissive material in a sustained role AND effect life >= sustainSeconds. '
                                   'A census class for the ArtAudio Bible rule (sustained emission forbidden for spells); '
                                   'whether it reads as light is a visual question for the sweep'),
            ('triangles.estimate', 'profile meshes (procedural path: Count x body + Count x accent) + meshes serialized in '
                                   'sustained-role prefabs. Generated meshes, ribbons, particles excluded'),
            ('dispatch', 'which Vfx120Effect.Build branch the profile takes; only fields that branch reads are counted'),
        ])),
        ('codeFieldUsage', {k: sorted(v) for k, v in code_usage.items()}),
        ('elapsedSeconds', round(time.time() - started, 1)),
    ])
    out['totals'] = totals
    out['findings'] = findings
    out['integrity'] = integrity
    out['lookAlikeGroups'] = lookalikes
    out['sameColourGroups'] = same_colour
    out['saturatedColours'] = saturated
    out['glyphs'] = glyph_records

    os.makedirs(args.out, exist_ok=True)
    json_path = os.path.join(args.out, 'static308.json')
    with open(json_path, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(out, fh, ensure_ascii=False, indent=1, default=lambda o: sorted(o) if isinstance(o, set) else str(o))
        fh.write('\n')
    md_path = os.path.join(args.out, 'STATIC308.md')
    with open(md_path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(render_markdown(out))
    if not args.quiet:
        print('wrote', rel(json_path.replace(BS, '/')))
        print('wrote', rel(md_path.replace(BS, '/')))
        print(json.dumps(totals, ensure_ascii=False))
        for f in findings[:15]:
            print('%2d. [%s] %s (%s)' % (f['rank'], f['severity'], f['title'], f['count']))
    return 0


# ----------------------------------------------------------------------------------------------- findings
def build_findings(records, totals, integrity, lookalikes, same_colour, saturated, args):
    def cat(rec):
        return rec['variants'].get('catalog')

    def played_variant(rec):
        """What the game shows for this glyph when the cast resolves: the live prefab if it differs, the runtime data
        when the catalogue prefab is not spawned at all (summon combat actor), None when nothing of the catalogue is drawn."""
        if rec['live'].get('catalogPrefabPlayed') is False:
            return rec['variants'].get('runtimeData')
        return rec['variants'].get('live') or rec['variants'].get('catalog')

    def reach(glyph_records_):
        """Split a list by how the main game reaches the cast (coverage.json)."""
        start = [r['glyph'] for r in glyph_records_ if r['live'].get('reachableInGame') == 'yes_from_start']
        boss = [r['glyph'] for r in glyph_records_ if r['live'].get('reachableInGame') == 'yes_after_cheongryong_unlock']
        rest = [r['glyph'] for r in glyph_records_ if r['glyph'] not in start + boss]
        parts = ['처음부터 %d자(%s)' % (len(start), ''.join(start) or '–'), '청룡 처치 뒤 %d자(%s)' % (len(boss), ''.join(boss) or '–')]
        if rest:
            parts.append('도달 불가 %d자(%s)' % (len(rest), ''.join(rest)))
        return ' / '.join(parts)

    findings = []

    def add(severity, key, title, glyphs, detail, numbers=None):
        glyphs = list(glyphs)
        if not glyphs and key != 'integrity':
            severity = 'D'   # nothing found: kept in the list as a confirmed zero
        findings.append(OrderedDict([('severity', severity), ('id', key), ('title', title), ('count', len(glyphs)),
                                     ('glyphs', glyphs), ('numbers', numbers or {}), ('detail', detail)]))

    resolves = [r for r in records if r['live'].get('castResolvesInGame')]
    # 1. sustained additive / emissive in what the game plays
    hot_live = [r for r in resolves if played_variant(r) and played_variant(r)['emission']['sustainedCandidate']]
    add('A', 'emission-sustained-live', '게임이 실제로 재생하는 글자 중 지속 역할에 가산·발광 재질이 있는 것',
        [r['glyph'] for r in hot_live],
        '지속 역할(body/field) 재질이 가산 블렌드이거나 밝기 배율·HDR 색이 1을 넘고 수명이 %.1f초 이상. '
        'ArtAudio Bible 39행(술식=순간 발광만, 지속 금지)의 조사 대상: ' % args.sustain_seconds
        + ', '.join('%s(%.1f초)' % (r['glyph'], played_variant(r)['lifeSeconds']) for r in hot_live)
        + '. 이 가운데 가산 블렌드 재질이 지속 역할에 있는 것 %d자(%s), 밝기 배율·HDR 색만 있는 것 %d자(%s). '
          '도달: %s. '
          '결계의 문양(`WardPatternPrefab`)은 코드가 결계 수명 내내 유지한다(FixedWardVfx: Lifetime=Duration, 실행 중 밝기 배율 1.8). '
          '소환 다섯(곰놈몸솜옴)은 이 목록에 없다: 게임은 카탈로그 프리팹을 재생하지 않고 전투 본체를 띄우며'
          '(BrushStrokeFeedAdapter.SpawnPattern이 외부 소유 소환에서 돌아간다), 진 문양은 형성 시간(FormationSeconds 1.2초, 밝기 배율 0.65) 동안만 보인다. '
          '카탈로그 프리팹의 정지 표현(WoodDeerVfx 등)은 진을 표현 수명 4.6초 내내 유지하므로 발견 「카탈로그 전체」에는 남는다.'
        % (sum(1 for r in hot_live if played_variant(r)['emission']['additiveSustained']),
           ''.join(r['glyph'] for r in hot_live if played_variant(r)['emission']['additiveSustained']),
           sum(1 for r in hot_live if not played_variant(r)['emission']['additiveSustained']),
           ''.join(r['glyph'] for r in hot_live if not played_variant(r)['emission']['additiveSustained']),
           reach(hot_live)),
        {'ofCastsThatResolve': len(resolves),
         'additive': sum(1 for r in hot_live if played_variant(r)['emission']['additiveSustained'])})
    hot_all = [r for r in records if cat(r) and cat(r)['emission']['sustainedCandidate']]
    add('A', 'emission-sustained-all', '카탈로그 전체에서 지속 역할에 가산·발광 재질이 있는 글자',
        [r['glyph'] for r in hot_all],
        '원소별: ' + ', '.join('%s %d' % (e, sum(1 for r in hot_all if r['element'] == e)) for e in '목화토금수'),
        {'additive': totals['sustainedAdditive'], 'emissiveMaterial': totals['sustainedEmissiveMaterial']})
    flight_hot = [r for r in resolves if played_variant(r) and (played_variant(r)['emission']['additiveFlight']
                                                               or played_variant(r)['emission']['emissiveFlight'])]
    add('B', 'emission-flight-live', '게임이 재생하는 비행체(Bolt300)에 가산·발광 재질이 있는 글자',
        [r['glyph'] for r in flight_hot],
        '비행 시간(프로필 Flight %s초) 동안만 보이는 몸체. 순간 발광으로 볼지 지속으로 볼지는 화면에서 정한다: '
        % '·'.join(sorted(set('%.2f' % played_variant(r)['flightSeconds'] for r in flight_hot)))
        + ', '.join('%s(%s)' % (r['glyph'], ', '.join(os.path.basename(p) for p in played_variant(r)['emission']['additiveFlight']))
                    for r in flight_hot))
    runtime_hot = [r for r in records if r['variants'].get('runtimeData')
                   and (r['variants']['runtimeData']['emission']['additiveSustained']
                        or r['variants']['runtimeData']['emission']['emissiveSustained'])]
    add('A', 'emission-runtime-data', '소환·EA 런타임 데이터가 넘기는 프리팹·재질 중 지속 가산·발광',
        [r['glyph'] for r in runtime_hot],
        '소환수 본체는 활동 시간(데모 20초) 내내 화면에 있다. 카탈로그 프리팹이 아니라 라이브 데이터 사본이 가리키는 자산 기준.')
    # 2. broken references on a used path
    used_broken = [b for b in integrity['brokenRefs'] if not b.get('unusedPath')]
    visible = [b for b in used_broken if 'no visible effect' not in b.get('effect', '')]
    add('A' if visible else 'C', 'broken-guid-used', '쓰는 경로에 끊긴 GUID가 있는 글자',
        sorted(set(b['glyph'] for b in used_broken)),
        '끊긴 GUID %d종, 참조 %d건(입자 렌더러의 메시 칸 %d건 + 방출 형상의 메시 칸 %d건 + 그 밖 %d건). '
        '화면 영향이 있는 것(메시 모드 입자·메시 형상 방출기·메시 필터·재질) %d건.'
        % (len(set(b['guid'] for b in used_broken)), len(used_broken),
           sum(1 for b in used_broken if b['key'].startswith('ParticleSystemRenderer.')),
           sum(1 for b in used_broken if b['key'].startswith('ParticleSystem.ShapeModule.')),
           sum(1 for b in used_broken if not b['key'].startswith(('ParticleSystemRenderer.', 'ParticleSystem.ShapeModule.'))),
           len(visible)),
        {'distinctGuids': len(set(b['guid'] for b in used_broken)), 'references': len(used_broken),
         'visibleEffect': len(visible)})
    # 3. live differs from catalogue
    diff = [r for r in records if not r['live']['sameAsCatalog']]
    add('B', 'live-differs', '메인 씬이 카탈로그와 다른 프리팹을 재생하는 글자',
        [r['glyph'] for r in diff],
        '메인 시각 세트 사본이 가리키는 프리팹이 카탈로그 프리팹과 다르다. 카탈로그만 찍는 검사는 이 글자의 실제 화면을 보지 못한다: '
        + ', '.join('%s→%s' % (r['glyph'], r['live']['visualSetPrefab']) for r in diff))
    # 4. catalogue-only
    misfire = [r for r in records if r['category'] != '공백' and r['live'].get('castResolvesInGame') is False]
    add('B', 'catalogue-only', '배정됐지만 게임에서 재생되지 않는 글자(시전이 불발)',
        [r['glyph'] for r in misfire], '프리팹은 시각 세트에 올라 있으나 리졸버가 false를 돌려줘 생성되지 않는다.')
    # 5. shared bodies
    shared = [r for r in records if r['bodyState'] == 'shared']
    add('B', 'shared-body', '공용 몸체(개별 제작 없음)인 배정 글자', [r['glyph'] for r in shared],
        '원소별: ' + ', '.join('%s %d' % (e, sum(1 for r in shared if r['element'] == e)) for e in '목화토금수'))
    shared_live = [r for r in shared if r['live'].get('castResolvesInGame')]
    add('A', 'shared-body-live', '게임에서 재생되는데 공용 몸체인 글자', [r['glyph'] for r in shared_live],
        '시전이 풀리는 글자 가운데 개별 제작이 없는 것. 도달: %s.' % reach(shared_live))
    # 6. look-alike groups
    biggest = lookalikes[:6]
    add('B', 'lookalike-groups', '카탈로그 프리팹의 계열·동작·배치·몸체 메시·보조 메시·분기가 같은 글자 묶음',
        [g for grp in lookalikes for g in grp['glyphs']],
        '%d묶음. 큰 순: ' % len(lookalikes) + ' / '.join(
            ''.join(g['glyphs']) + '(%s·%s; 묶음 안에서 다른 값: %s%s)' % (
                g['element'], g['family'], '·'.join(g['stillDiffersIn']) or '없음',
                '; 게임에서는 다른 프리팹: ' + ''.join(g['replacedInGame']) if g['replacedInGame'] else '')
            for g in biggest)
        + '. 완전히 같은 구성이라는 뜻이 아니다: 위 여섯 항목만 같고 수량·수명·색은 다를 수 있다.',
        {'groups': len(lookalikes)})
    # 7. tri budget
    over = [r for r in records if cat(r) and cat(r)['triangles']['overBudget']]
    near = [r for r in records if cat(r) and not cat(r)['triangles']['overBudget'] and cat(r)['triangles']['estimate'] > 12000]
    add('B', 'tri-budget', '정적 추정 삼각형이 글자당 %d선을 넘는 글자' % args.tri_budget, [r['glyph'] for r in over],
        ', '.join('%s %s' % (r['glyph'], format(cat(r)['triangles']['estimate'], ',')) for r in
                  sorted(over, key=lambda r: -cat(r)['triangles']['estimate']))
        + ' · 12k~17k: ' + ', '.join('%s %s' % (r['glyph'], format(cat(r)['triangles']['estimate'], ',')) for r in
                                     sorted(near, key=lambda r: -cat(r)['triangles']['estimate'])),
        {'over': len(over), 'between12kAnd17k': len(near)})
    runtime_over = [r for r in records if r['variants'].get('runtimeData')
                    and r['variants']['runtimeData']['triangles']['estimate'] > args.tri_budget]
    add('C', 'tri-budget-runtime', '런타임 데이터 자산(소환 본체 등)의 정적 삼각형이 %d를 넘는 글자' % args.tri_budget,
        [r['glyph'] for r in runtime_over],
        ', '.join('%s %s' % (r['glyph'], format(r['variants']['runtimeData']['triangles']['estimate'], ',')) for r in runtime_over))
    # 8. fog layer
    no_fog = [r for r in resolves if played_variant(r) and not played_variant(r)['layers']['usesVfxAfterFog']
              and not str(r['live'].get('plays', '')).startswith('own mesh')]
    add('B', 'fog-layer', '게임에서 재생되는 글자 중 VfxAfterFog 레이어를 쓰지 않는 것', [r['glyph'] for r in no_fog],
        '카탈로그 기준 사용 %d자, 메인이 재생하는 프리팹 기준 사용 %d자. Renderer297 월드 안개가 깊이 없는 투명체를 지운다(SPEC-PLAYER-FEEL-300 110행). '
        '화면 영향은 Play에서만 확인된다.' % (totals['usesVfxAfterFogCatalog'], totals['usesVfxAfterFogLive']),
        {'catalogUsing': totals['usesVfxAfterFogCatalog'], 'liveUsing': totals['usesVfxAfterFogLive']})
    # 9. lights
    lit = [r for r in records if any(v['lights'] for v in r['variants'].values())]
    add('A' if lit else 'D', 'lights', 'Light 컴포넌트가 직렬화된 글자', [r['glyph'] for r in lit],
        '프리팹 닫힘 안의 활성 Light. 런타임 코드가 만드는 Light는 Vfx120 경로에 없다(FlameJetEffect는 옛 경로).')
    # 10. looping particles in sustained roles
    loop = [r for r in records if cat(r) and cat(r)['particles']['loopingSustained']]
    add('C', 'looping-particles', '지속 역할에 반복(looping) 입자가 있는 글자', [r['glyph'] for r in loop],
        '수명 종료 시 코드가 방출을 멈추는지는 Play 잔존 검사 대상.')
    # 11. stale flags
    stale = integrity['staleGameplayConnectedFlags']
    add('C', 'stale-connected-flag', '카탈로그 GameplayConnected 표기가 실제와 다른 글자', [s['glyph'] for s in stale],
        '표기 1인데 불발 %d자, 표기 0인데 시전이 풀림 %d자.' % (sum(1 for s in stale if s['flag']), sum(1 for s in stale if not s['flag'])))
    # 12. unused-path broken guids
    unused_broken = [b for b in integrity['brokenRefs'] if b.get('unusedPath')]
    add('D', 'broken-guid-unused', '쓰지 않는 슬롯에만 끊긴 GUID가 남은 글자', sorted(set(b['glyph'] for b in unused_broken)),
        '프로필이 아직 가리키지만 현재 분기가 읽지 않는 슬롯. 화면 영향 없음, 분기가 바뀌면 드러난다.')
    # 13. colour
    add('C', 'same-colour', '같은 원소 안에서 안료·보조색이 완전히 같은 글자 묶음',
        [g for grp in same_colour for g in grp['glyphs']],
        '%d묶음. 큰 순: ' % len(same_colour) + ' / '.join('%s %s×%d' % (g['element'], g['pigment'], len(g['glyphs'])) for g in same_colour[:6]),
        {'groups': len(same_colour)})
    add('C', 'saturated-colour', '프로필 색이 채도 0.8·명도 0.9 이상인 글자(네온 후보)', sorted(set(s['glyph'] for s in saturated)),
        ', '.join('%s(%s S%.2f V%.2f)' % (s['glyph'], s['which'], s['hsv'][1], s['hsv'][2]) for s in saturated[:12]))
    bright = [r for r in records if cat(r) and (cat(r)['emission']['ktpBrightness'] > 1.001 or cat(r)['emission']['ktpContactBrightness'] > 1.001)]
    add('C', 'ktp-brightness', 'KTP 문양 밝기 배율이 1을 넘는 글자', [r['glyph'] for r in bright],
        ', '.join('%s(문양 %.2f·접촉 %.2f)' % (r['glyph'], cat(r)['emission']['ktpBrightness'], cat(r)['emission']['ktpContactBrightness']) for r in bright))
    # 14. profile copies
    copies = integrity['profileCopiesInLiveData']
    by_id = {r['id']: r['glyph'] for r in records}

    def copy_glyph(d):
        return by_id.get(re.sub(r'.*_(\d{3}_[0-9A-F]{4})\.asset$', r'\1', d['copy']), d['copy'])

    scalar_diff = [d for d in copies['divergent'] if d['scalarChanged']]
    stale_target = [d for d in copies['divergent'] if d['remappedTargetContentDiffers']]
    in_scene = [d for d in copies['divergent'] if d['inMainScene']]
    add('B' if (scalar_diff or stale_target) else 'C', 'profile-copies',
        '라이브 데이터 폴더의 프로필 사본 가운데 원본과 어긋난 것(값 또는 가리키는 재질 사본의 내용)',
        sorted(set(copy_glyph(d) for d in scalar_diff + stale_target)),
        '사본 %d개(같은 프로필의 사본이 둘인 경우 포함). 참조만 재질 사본으로 바뀐 것 %d, 스칼라 값이 다른 것 %d, '
        '바뀐 참조의 대상 내용이 원본 재질과 다른 것 %d. 메인 씬이 직접 참조하는 사본 %d개(%s), '
        '조사 범위(라이브 데이터·VFX 폴더)에서 사본을 가리키는 다른 자산 %d개. 참조되지 않는 사본은 화면에 영향이 없다.'
        % (copies['count'], sum(1 for d in copies['divergent'] if d['referenceRemapped'] and not d['scalarChanged']),
           len(scalar_diff), len(stale_target), len(copies['referencedByMainScene']),
           ', '.join('%s: 참조 %s%s' % (copy_glyph(d), '·'.join(d['referenceRemapped'][:4]),
                                     (' / 대상 내용 다름 ' + '·'.join(d['remappedTargetContentDiffers']))
                                     if d['remappedTargetContentDiffers'] else '')
                     for d in in_scene) or '없음',
           copies['referencingAssetCount']),
        {'copies': copies['count'], 'scalarDiff': len(scalar_diff), 'staleTargets': len(stale_target),
         'referencedByMainScene': len(copies['referencedByMainScene'])})
    term = []
    for r in records:
        shown = ([played_variant(r)] if played_variant(r) and r['live'].get('castResolvesInGame') else []) + \
                ([r['variants']['runtimeData']] if r['variants'].get('runtimeData') else [])
        if any(m['shaderEmissionTerm'] and m['sustainedRole'] and not m['emissive'] and not m['additive']
               for v in shown for m in v['materials']):
            term.append(r)
    add('C', 'shader-emission-term',
        '재질 값으로는 잡히지 않지만 셰이더 소스에 발광류 항(emission·ember·glow·radiance)이 있는 지속 재질을 쓰는 글자',
        [r['glyph'] for r in term],
        '셰이더 코드가 스스로 밝기를 더하는지는 정적으로 판정하지 못한다. 스윕 화면에서 볼 목록(게임이 재생하는 프리팹과 런타임 데이터 기준).')
    # 15. integrity
    vs = integrity['visualSets']
    problems = []
    if integrity['catalogEntries'] != 120:
        problems.append('catalog %d' % integrity['catalogEntries'])
    for key in ('original', 'live'):
        rep = vs[key]
        if rep['count'] != 120 or rep['missingGlyphs'] or rep['duplicateLetters'] or rep['unresolvedPrefabs'] or rep['nullPrefabs']:
            problems.append('%s set: %d entries, missing %d, unresolved %d' % (key, rep['count'], len(rep['missingGlyphs']), len(rep['unresolvedPrefabs'])))
    if integrity['prefabProfileMismatch']:
        problems.append('prefab/profile mismatch %d' % len(integrity['prefabProfileMismatch']))
    add('A' if problems else 'D', 'integrity', '카탈로그·시각 세트 무결성',
        [m['glyph'] for m in integrity['prefabProfileMismatch']],
        ('문제: ' + '; '.join(problems)) if problems else
        '카탈로그 120, 원본 세트 %d, 메인 세트 %d, 글자 순서 CSV 일치=%s, 프리팹-프로필 불일치 0, 미해결 프리팹 0.'
        % (vs['original']['count'], vs['live']['count'], integrity['glyphOrderMatchesCsv']))
    order = {'A': 0, 'B': 1, 'C': 2, 'D': 3}
    findings.sort(key=lambda f: (order[f['severity']], -f['count'] if f['id'] != 'integrity' else 0))
    for i, f in enumerate(findings):
        f['rank'] = i + 1
        f.move_to_end('rank', last=False)
    return findings


# ----------------------------------------------------------------------------------------------- markdown
def render_markdown(out):
    t = out['totals']
    integ = out['integrity']
    vs = integ['visualSets']
    recs = out['glyphs']
    lines = []
    w = lines.append
    w('# 술식 120 VFX 정적 조사 (STATIC308)')
    w('')
    w('생성 %s · 도구 `%s` · 원자료 `static308.json`' % (out['meta']['generatedUtc'], out['meta']['tool']))
    w('')
    w('Unity 없이 직렬화 자산(.asset·.prefab·.mat), 셰이더 소스, 셰이더 그래프, FBX 원본만 읽은 조사다. '
      '화소를 본 것이 아니고 합격 판정도 아니다. 실행 중에 정해지는 것(프로퍼티 블록, 생성 메시, 실제 인스턴스 수)은 '
      '여기에 없고 편집기 스윕(Spell120Sweep308)과 Play에서 본다. 프로젝트 자산은 하나도 바꾸지 않았다.')
    w('')
    w('## 1. 요약 수치')
    w('')
    w('| 항목 | 수 |')
    w('|---|---|')
    w('| 카탈로그 칸 / CSV 행 | %d / %d |' % (integ['catalogEntries'], integ['csvRows']))
    w('| 시각 세트 원본 / 메인 사본 | %d / %d |' % (vs['original']['count'], vs['live']['count']))
    w('| 배정 글자 | %d |' % t['assigned'])
    w('| 효과 코드가 풀리는 글자(coverage.json 기준) | %d |' % t['castResolvesInGame'])
    w('| 그중 메인 게임에서 도달: 처음부터 / 청룡 처치 뒤 / 도달 불가 | %d / %d / %d |'
      % (t.get('reachableFromStart', 0), t.get('reachableAfterCheongryong', 0), t.get('resolvesButNotReachable', 0)))
    w('| 풀리지만 카탈로그 프리팹은 재생되지 않는 글자(소환 전투 본체·뭄 다리) | %d |' % t.get('catalogPrefabNotPlayed', 0))
    w('| 메인이 카탈로그와 다른 프리팹을 재생 | %d |' % len(vs['liveDiffersFromCatalog']))
    w('| 지속 역할에 가산·발광 재질(수명 %.1f초 이상) | %d (가산 %d, 발광 속성 %d) |'
      % (out['meta']['parameters']['sustainSeconds'], t['sustainedAdditiveOrEmissive'], t['sustainedAdditive'], t['sustainedEmissiveMaterial']))
    w('| 가산 재질이 하나라도 있는 글자 | %d |' % t['anyAdditive'])
    w('| Light 컴포넌트가 직렬화된 글자 | %d |' % t['lights'])
    w('| 지속 역할 반복 입자 | %d |' % t['loopingSustainedParticles'])
    w('| 정적 추정 삼각형 %s 초과 | %d |' % (format(out['meta']['parameters']['triBudget'], ','), t['overTriBudget']))
    w('| VfxAfterFog 레이어 사용(카탈로그 / 메인 재생 프리팹) | %d / %d |' % (t['usesVfxAfterFogCatalog'], t['usesVfxAfterFogLive']))
    w('| 끊긴 GUID(종) · 쓰는 경로 참조 · 안 쓰는 슬롯 참조 | %d · %d · %d |'
      % (t['brokenGuidsDistinct'], t['brokenRefsUsedPath'], t['brokenRefsUnusedPath']))
    w('')
    w('## 2. 발견 순위')
    w('')
    w('등급은 조사 우선순위다. A = 규칙(발광·무결성)에 닿거나 게임 화면에 바로 나오는 것, B = 전면 교체 대상 선별에 쓰는 것, '
      'C = 기록해 둘 것, D = 이상 없음 확인.')
    w('')
    w('| # | 등급 | 발견 | 수 | 글자 | 내용 |')
    w('|---|---|---|---|---|---|')
    for f in out['findings']:
        glyphs = ''.join(f['glyphs']) if all(len(g) == 1 for g in f['glyphs']) else ', '.join(f['glyphs'])
        if len(glyphs) > 70:
            glyphs = glyphs[:70] + '…'
        w('| %d | %s | %s | %d | %s | %s |' % (f['rank'], f['severity'], f['title'], f['count'], glyphs or '–',
                                                f['detail'].replace('|', '/')))
    w('')
    w('## 3. 무결성')
    w('')
    w('- 카탈로그 %d칸, 글자 순서가 CSV와 %s. 중복 글자 %d, 중복 프리팹 %d, 중복 프로필 %d.'
      % (integ['catalogEntries'], '같다' if integ['glyphOrderMatchesCsv'] else '다르다',
         len(integ['duplicateGlyphs']), len(integ['duplicatePrefabs']), len(integ['duplicateProfiles'])))
    w('- 프리팹의 `Vfx120Effect.Profile`이 카탈로그 프로필과 다른 칸: %d. `Assigned` 표기가 CSV 분류와 다른 칸: %d.'
      % (len(integ['prefabProfileMismatch']), len(integ['assignedFlagMismatch'])))
    for key, label in (('original', '원본'), ('live', '메인 사본')):
        rep = vs[key]
        w('- 시각 세트 %s `%s`: %d항목, 고유 글자 %d, 빠진 글자 %d, 남는 글자 %d, 빈 프리팹 %d, 풀리지 않는 프리팹 %d, 배율≠1 %d, 포물선≠0 %d.'
          % (label, rep['asset'], rep['count'], rep['uniqueLetters'], len(rep['missingGlyphs']), len(rep['extraGlyphs']),
             len(rep['nullPrefabs']), len(rep['unresolvedPrefabs']), len(rep['nonDefaultScale']), len(rep['nonZeroArc'])))
    w('- 메인 씬 `%s`의 `_visualSet` 참조 %d건이 가리키는 자산: %s.'
      % (vs['mainSceneReferences']['scene'], vs['mainSceneReferences']['fieldHits'],
         ', '.join('`%s`' % short(REPO + '/' + p) for p in vs['mainSceneReferences']['distinctAssets']) or '없음'))
    w('- 두 세트가 다른 글자: %s.' % (', '.join('%s(%s → %s)' % (d['glyph'], d['original'], d['live']) for d in vs['differences']) or '없음'))
    copies = integ['profileCopiesInLiveData']
    w('- 라이브 데이터 폴더(`%s`)의 프로필 사본 %d개: 필드까지 같은 것 %d, 재질 참조만 사본으로 바뀐 것 %d, 스칼라 값이 다른 것 %d, 원본 없음 %d. '
      '조사 범위(라이브 데이터·VFX 폴더)에서 사본을 가리키는 자산 %d개, 메인 씬 직접 참조 %d개.'
      % (short(LIVE_DATA + '/'), copies['count'], copies['identical'],
         sum(1 for d in copies['divergent'] if d['referenceRemapped'] and not d['scalarChanged']),
         sum(1 for d in copies['divergent'] if d['scalarChanged']), len(copies['noOriginal']),
         copies['referencingAssetCount'], len(copies['referencedByMainScene'])))
    if copies['referencedByLiveAssets']:
        w('  - 가리키는 자산(상위): ' + ', '.join('`%s`(%d)' % (x['asset'], x['copies']) for x in copies['referencedByLiveAssets'][:6]))
    remap = Counter(f for d in copies['divergent'] for f in d['referenceRemapped'])
    w('  - 사본에서 참조가 바뀐 필드: %s. 스칼라 값이 다른 사본 %d개, 바뀐 참조의 대상 내용이 원본과 다른 사본 %d개.'
      % (', '.join('%s(%d)' % kv for kv in remap.most_common(8)) or '없음',
         sum(1 for d in copies['divergent'] if d['scalarChanged']),
         sum(1 for d in copies['divergent'] if d['remappedTargetContentDiffers'])))
    for d in copies['divergent']:
        if d['inMainScene']:
            w('  - 메인 씬이 직접 참조하는 사본 `%s`: 참조 변경 %s, 스칼라 변경 %s, 대상 내용 다름 %s.'
              % (d['copy'], '·'.join(d['referenceRemapped']) or '없음', '·'.join(d['scalarChanged']) or '없음',
                 '·'.join(d['remappedTargetContentDiffers']) or '없음'))
    only_copy = Counter(f for d in copies['divergent'] for f in d['onlyInCopy'])
    only_orig = Counter(f for d in copies['divergent'] for f in d['missingInCopy'])
    w('  - 직렬화 시점 차이: 원본에 없고 사본에만 있는 필드 %d종, 사본에 없고 원본에만 있는 필드 %d종(없는 쪽은 클래스 기본값으로 불러온다).'
      % (len(only_copy), len(only_orig)))
    w('')
    w('### 끊긴 GUID')
    w('')
    by_guid = defaultdict(list)
    for b in integ['brokenRefs']:
        by_guid[b['guid']].append(b)
    if not by_guid:
        w('없음.')
    else:
        w('| GUID | 쓰는 경로 글자 | 안 쓰는 슬롯 글자 | 어디서 | 필드 | 화면 영향 |')
        w('|---|---|---|---|---|---|')
        for guid, items in sorted(by_guid.items(), key=lambda kv: -len(kv[1])):
            used = sorted(set(b['glyph'] for b in items if not b.get('unusedPath')))
            unused = sorted(set(b['glyph'] for b in items if b.get('unusedPath')) - set(used))
            where = Counter(b['in'] for b in items).most_common(4)
            keys = Counter(b['key'].split(' (')[0] for b in items).most_common(2)
            used_items = [b for b in items if not b.get('unusedPath')]
            seen_items = [b for b in used_items if 'no visible effect' not in b.get('effect', '')]
            latent = [b for b in items if b.get('unusedPath') and 'no visible effect' not in b.get('effect', '')]
            if seen_items:
                effect = Counter(b.get('effect', '') for b in seen_items).most_common(1)[0][0]
                effect = ('있음 — 그 렌더러가 아무것도 그리지 않는다' if 'draws nothing' in effect
                          else '있음 — 메시 형상 방출기가 메시를 잃었다' if 'mesh-shaped emitter' in effect else '있음 — ' + effect)
            elif used_items:
                effect = '없음 — 방출 형상이 메시를 읽지 않는 종류이고 입자 렌더러는 빌보드 모드라 메시 칸을 읽지 않는다'
            else:
                effect = '없음 — 현재 분기가 읽지 않는 슬롯' + ('(쓰이게 되면 메시 형상 방출기가 메시 없이 돈다)' if latent else '')
            w('| `%s` | %s (%d) | %s (%d) | %s | %s | %s |' % (
                guid, ''.join(used) or '–', len(used), ''.join(unused) or '–', len(unused),
                ', '.join('`%s`' % x[0] for x in where), ', '.join(x[0] for x in keys), effect))
        q = integ['quarantineProbe']
        w('')
        if q.get('searched'):
            w('격리 폴더(`%s`)의 .meta %d개를 읽어 확인: 끊긴 GUID %d종 가운데 %d종이 그 안에 있다(0이면 #307 격리로 끊긴 것이 아니다).'
              % (q['folder'], q.get('metasRead', 0), len(integ['brokenGuids']), len(q.get('found', {}))))
        else:
            w('격리 폴더(`%s`)는 이번 실행에서 확인하지 않았다(`--probe-quarantine`).' % q['folder'])
    w('')
    w('## 4. 게임이 실제로 재생하는 것')
    w('')
    w('| 글자 | 카탈로그 프리팹 | 메인이 재생 | 메인 프로필 | 런타임 데이터가 넘기는 것 |')
    w('|---|---|---|---|---|')
    for r in recs:
        live = r['live']
        if live['sameAsCatalog'] and not live['runtimeData']:
            continue
        runtime = ', '.join('%s=%s' % (x['field'], x['target']) for x in live['runtimeData'][:5])
        if len(live['runtimeData']) > 5:
            runtime += ' 외 %d' % (len(live['runtimeData']) - 5)
        plays = '`%s`' % live['visualSetPrefab'] if not live['sameAsCatalog'] else '같음'
        if live.get('catalogPrefabPlayed') is False and live.get('castResolvesInGame'):
            plays = '재생 안 함(전투 본체가 대신)' if 'summon combat actor' in str(live.get('plays')) else '재생 안 함(자체 메시)'
        w('| %s | `%s` | %s | %s | %s |' % (r['glyph'], r['catalog']['prefab'], plays,
                                           '`%s`' % live.get('profile') if live.get('profile') else '–', runtime or '–'))
    w('')
    w('표에 없는 글자는 메인 시각 세트가 카탈로그 프리팹을 그대로 가리키고 런타임 데이터 참조도 없다. '
      '시전이 불발인 %d자는 프리팹이 세트에 올라 있어도 생성되지 않는다.'
      % sum(1 for r in recs if r['category'] != '공백' and r['live'].get('castResolvesInGame') is False))
    w('')
    w('## 5. 발광·가산 블렌드')
    w('')
    w('정의: 가산 = 목적지 블렌드 계수 One. 발광 = URP `_EMISSION`+색, 1을 넘는 HDR 색, 1을 넘는 밝기 배율'
      '(KTP 그래프 `_Emission`, 연소 셰이더 `_Intensity`). 역할은 프로필 필드로 나눈다: 몸체·필드=지속, '
      '비행체(Bolt300 Travel)=비행 중만, 시전·착탄·접촉·파편=순간. '
      '「지속 후보」 = 그런 재질이 지속 역할에 있고 수명이 %.1f초 이상. '
      '기준은 ArtAudio Bible 39행(술식은 순간 발광만, 지속 금지)이며 기본안이다(사용자 확인 대기, 질문 9). '
      '이 표는 조사 대상 목록이고 빛으로 보이는지는 스윕 화면에서 정한다.' % out['meta']['parameters']['sustainSeconds'])
    w('')
    w('| 원소 | 지속 후보 | 짧은 수명만 | 순간 역할만 | 없음 |')
    w('|---|---|---|---|---|')
    for e in '목화토금수':
        rows = [r for r in recs if r['element'] == e and r['variants'].get('catalog')]
        s = [r['glyph'] for r in rows if r['variants']['catalog']['emission']['sustainedCandidate']]
        sh = [r['glyph'] for r in rows if r['variants']['catalog']['emission']['shortLivedOnly']]
        mo = [r['glyph'] for r in rows if r['variants']['catalog']['emission']['momentaryOnly']]
        none = [r['glyph'] for r in rows if r['glyph'] not in s + sh + mo]
        w('| %s | %s (%d) | %s (%d) | %s (%d) | %s (%d) |' % (e, ''.join(s) or '–', len(s), ''.join(sh) or '–', len(sh),
                                                             ''.join(mo) or '–', len(mo), ''.join(none) or '–', len(none)))
    w('')
    mat_use = defaultdict(lambda: {'glyphs': [], 'roles': set(), 'blend': '', 'shader': '', 'why': []})
    for r in recs:
        for vname, v in r['variants'].items():
            for m in v['materials']:
                if (m['additive'] or m['emissive']) and m['sustainedRole']:
                    u = mat_use[m['path']]
                    if r['glyph'] not in u['glyphs']:
                        u['glyphs'].append(r['glyph'])
                    u['roles'].update(m['roles'])
                    u['blend'], u['shader'], u['why'] = m['blend'], m['shader'], m['emissiveReasons']
    w('지속 역할에 쓰인 가산·발광 재질 %d종(쓰는 글자 수 순, 상위 25):' % len(mat_use))
    w('')
    w('| 재질 | 셰이더 | 블렌드 | 발광 근거 | 글자 수 | 글자 |')
    w('|---|---|---|---|---|---|')
    for path, u in sorted(mat_use.items(), key=lambda kv: -len(kv[1]['glyphs']))[:25]:
        g = ''.join(u['glyphs'])
        w('| `%s` | `%s` | %s | %s | %d | %s |' % (path, u['shader'], u['blend'], '; '.join(u['why']) or '–', len(u['glyphs']),
                                                 g if len(g) <= 40 else g[:40] + '…'))
    w('')
    lit = [(r, vname, v) for r in recs for vname, v in r['variants'].items() if v['lights']]
    w('### Light 컴포넌트')
    w('')
    if not lit:
        w('프리팹 닫힘 안에 활성 Light가 없다(120칸 + 메인 재생 프리팹 + 런타임 데이터 자산). '
          'Vfx120 런타임 코드도 Light를 만들지 않는다(`AddComponent<Light>`는 옛 `FlameJetEffect`에만 있다).')
    else:
        w('| 글자 | 변형 | 프리팹 | 이름 | 종류 | 세기 | 범위 | 역할 |')
        w('|---|---|---|---|---|---|---|---|')
        for r, vname, v in lit:
            for l in v['lights']:
                w('| %s | %s | `%s` | %s | %s | %.2f | %.1f | %s |' % (r['glyph'], vname, l['source'], l['name'],
                                                                     LIGHT_TYPE.get(l['type'], l['type']), l['intensity'], l['range'], l['role']))
    w('')
    w('## 6. 삼각형 정적 추정')
    w('')
    w('글자당 %s선은 SPEC-SPELL-FX-REWORK 6-8절의 값이다. 추정 = 프로필 메시(공용 몸체는 Count배, 전용 몸체는 1회) + '
      '지속 역할 프리팹에 직렬화된 메시. 실행 중 생성 메시·리본·입자는 빠져 있고, 전용 몸체의 실제 반복 수는 코드가 정하므로 '
      '아래 수는 하한에 가깝다. 실제 그려진 수는 스윕이 박자마다 기록한다.' % format(out['meta']['parameters']['triBudget'], ','))
    w('')
    w('| 순위 | 글자 | 추정 tris | 분기 | 순간 역할 프리팹 tris | 직렬화된 입자 상한 합(실행 상한 아님) |')
    w('|---|---|---|---|---|---|')
    ranked = sorted([r for r in recs if r['variants'].get('catalog')], key=lambda r: -r['variants']['catalog']['triangles']['estimate'])
    for i, r in enumerate(ranked[:20]):
        v = r['variants']['catalog']
        w('| %d | %s | %s%s | %s | %s | %s |' % (i + 1, r['glyph'], format(v['triangles']['estimate'], ','),
                                               ' **초과**' if v['triangles']['overBudget'] else '',
                                               v['dispatch'] + (' · 공용' if r['bodyState'] == 'shared' else ''),
                                               format(v['triangles']['prefabMomentary'], ','), format(v['particles']['maxParticlesSum'], ',')))
    w('')
    rt = [(r, r['variants']['runtimeData']) for r in recs if r['variants'].get('runtimeData')]
    if rt:
        w('런타임 데이터 자산(소환 본체·EA 프리팹) 기준:')
        w('')
        w('| 글자 | 추정 tris | 지속 가산 | 지속 발광 | 필드 |')
        w('|---|---|---|---|---|')
        for r, v in rt:
            w('| %s | %s | %d | %d | %s |' % (r['glyph'], format(v['triangles']['estimate'], ','), len(v['emission']['additiveSustained']),
                                             len(v['emission']['emissiveSustained']), ', '.join(v['usedFields'][:6])))
        w('')
    w('## 7. 레이어')
    w('')
    layer_use = Counter()
    for r in recs:
        v = r['variants'].get('catalog')
        if v:
            for name in v['layers']['serialized']:
                layer_use[name] += 1
    w('- 카탈로그 프리팹과 그 닫힘에 직렬화된 레이어(글자 수): ' + ', '.join('%s %d' % kv for kv in layer_use.most_common()))
    fog_live = [r['glyph'] for r in recs if (r['variants'].get('live') or {}).get('layers', {}).get('usesVfxAfterFog')]
    w('- 메인이 재생하는 프리팹 가운데 `VfxAfterFog`를 쓰는 글자: %s.' % (''.join(fog_live) or '없음'))
    w('- `Vfx120Effect`가 실행 중에 만드는 자식은 레이어를 지정하지 않아 Default에 남는다(런타임 코드에 `.layer =` 0건).')
    w('')
    w('## 8. 색 출처')
    w('')
    w('- 색은 프로필의 Pigment·Accent·Ink를 프로퍼티 블록으로 넣는 구조다. 원소 팔레트 SO를 읽지 않는다.')
    w('- 같은 원소 안에서 안료·보조색이 완전히 같은 묶음 %d개(상위): %s.'
      % (len(out['sameColourGroups']), ' / '.join('%s %s·%s ×%d(%s)' % (g['element'], g['pigment'], g['accent'], len(g['glyphs']),
                                                                    ''.join(g['glyphs'])[:24]) for g in out['sameColourGroups'][:6]) or '없음'))
    w('- 채도 0.8·명도 0.9 이상인 프로필 색: %d건.' % len(out['saturatedColours']))
    w('')
    w('| 원소 | 안료 종류 수 | 대표 안료(쓰는 글자 수) |')
    w('|---|---|---|')
    for e in '목화토금수':
        pig = Counter(r['variants']['catalog']['colour']['pigment'] for r in recs if r['element'] == e and r['variants'].get('catalog'))
        w('| %s | %d | %s |' % (e, len(pig), ', '.join('%s(%d)' % kv for kv in pig.most_common(4))))
    w('')
    w('## 9. 계열·동작·배치·메시가 같은 묶음')
    w('')
    w('카탈로그 프리팹의 계열·동작·배치·몸체 메시·보조 메시·분기 여섯 항목이 같은 배정 글자다. 구성이 완전히 같다는 뜻이 아니다: '
      '수량·수명·보조색 같은 값은 묶음 안에서 다를 수 있고(아래 칸), 동작 코드가 갈라 줄 수도 있다. 실패 확정이 아니라 '
      '스윕 시트에서 먼저 나란히 볼 목록이다.')
    w('')
    w('| 원소 | 계열 | 동작 | 몸체 메시 | 글자 | 묶음 안에서 다른 값 | 게임에서는 다른 프리팹 |')
    w('|---|---|---|---|---|---|---|')
    for g in out['lookAlikeGroups']:
        w('| %s | %s | %s | %s | %s (%d) | %s | %s |' % (
            g['element'], g['family'], g['behavior'], ', '.join('`%s`' % m for m in g['bodyMesh']) or '–',
            ''.join(g['glyphs']), len(g['glyphs']), '·'.join(g.get('stillDiffersIn') or []) or '없음',
            ''.join(g.get('replacedInGame') or []) or '–'))
    w('')
    w('## 10. 이 조사가 말하지 못하는 것')
    w('')
    w('- 화면에 실제로 무엇이 보이는지. 재질이 가산이어도 어두운 색이면 빛으로 읽히지 않을 수 있고, 반대도 있다.')
    w('- 프로퍼티 블록으로 실행 중에 바뀌는 밝기(`KtpBrightness`, `_Emission` 덮어쓰기)와 색.')
    w('- 코드가 만드는 메시·리본·입자의 실제 수, 전용 몸체의 반복 수.')
    w('- 메인 렌더러(Renderer297)의 안개·먹 씻김 뒤 모습. 레이어 표는 직렬화 값만 본다.')
    w('- 「시전이 풀리는가」는 `coverage.json`(오늘 조사)에서 가져온 값이다. 이 도구가 리졸버를 실행한 것이 아니다.')
    w('')
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    sys.exit(main())
