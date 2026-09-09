"""Prepare the authored VFX catalog; does not change spell rules or touch Unity.

The source direction is intentionally richer than Vfx120Effect's current evaluator.
This manifest is executable art data, not evidence that event choreography is done.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Docs/Art/SpellVFX120/visual_direction_draft.json'
AUDIT = ROOT / 'Docs/Art/SpellVFX120/asset_audit.json'
PROFILE = ROOT / 'Oheangbu/Assets/_Project/Scripts/App/SpellVFX120/Vfx120Profile.cs'
FACTORY = ROOT / 'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120/Vfx120MeshFactory.cs'
CONNECTED = set('가나마사아고노모소오거너머서어')

# Explicit authoring, no index/modulo randomisation. Columns:
# glyph count size partScale.x/y/z flight turns lift stagger ribbons grounded mist
# Longitudinal meshes face +Z. Shape meshes retain their authored aspect before scale.
# Count is the number of visual parts, never gameplay projectiles or summon population.
AUTHORING = '''
가 1 .72 .85 .72 1.15 .60 0 .08 .00 1 0 0
각 8 .92 1.10 .90 2.05 .55 .30 1.70 .22 2 0 0
간 1 .62 .72 .64 .94 .60 .65 .16 .05 2 0 0
감 4 .34 .50 .72 .58 .55 .10 .08 .04 1 0 0
갓 1 .76 1.90 1.10 1.55 .40 0 .12 .00 1 0 0
강 3 .48 .65 .48 1.05 .58 .15 .04 .035 1 0 0
거 7 1.00 1.10 .85 1.55 .20 .12 .55 .018 1 0 0
걱 7 .55 1.00 .75 .58 .24 .15 .55 .34 1 1 0
건 2 .56 2.65 1.80 1.70 .22 0 1.10 .10 2 1 0
검 5 .66 1.35 1.10 1.95 .45 .20 1.60 .32 2 1 0
것 1 .22 .75 .80 1.25 .20 0 .10 .00 1 0 0
겅 2 .38 .48 .42 .62 .45 .10 .12 .06 1 0 0
고 17 1.75 1.22 1.05 1.85 .35 0 1.55 .025 0 1 1
곡 12 1.75 1.45 .82 1.60 .38 .65 .16 .50 2 1 0
곤 6 1.65 1.95 1.50 1.65 .34 .10 1.90 .055 1 1 1
곰 1 1.00 2.30 2.30 2.30 .55 .10 1.10 .00 2 1 0
곳 19 1.80 1.05 .85 1.35 .35 0 1.00 .045 0 1 0
공 11 1.55 1.65 1.25 2.10 .40 .18 1.55 .18 2 1 0
구 11 1.85 1.10 .82 2.15 .28 .08 1.50 .12 1 1 0
국 7 .62 1.60 1.40 2.35 .45 .10 2.20 .52 2 1 1
군 3 .48 1.15 .95 1.70 .32 .50 1.55 .20 1 1 0
굼 3 .65 2.10 1.40 1.35 .30 .08 .18 .18 1 1 0
굿 5 1.00 1.45 1.00 1.60 .25 .10 .62 .14 1 1 0
궁 3 .58 .82 .82 .65 .30 .40 .50 .22 2 1 0
나 3 .45 .95 .80 1.00 .70 .10 .25 .035 1 0 1
낙 6 .34 .75 .65 1.40 .62 .10 1.05 .13 1 0 1
난 1 .75 2.15 1.65 1.65 .78 .05 .42 .00 2 0 1
남 5 .30 .72 .72 .65 .60 .08 .18 .07 1 0 0
낫 2 1.10 1.65 .72 2.30 .65 .10 .20 .025 2 0 1
낭 1 .34 .85 .70 .72 .56 0 .03 .00 1 0 0
너 8 1.12 .95 .78 1.45 .20 .10 .66 .018 1 0 1
넉 12 .72 .48 .42 .90 .26 .20 .28 .11 1 1 1
넌 2 .28 1.00 .80 .90 .24 .75 .40 .10 2 0 0
넘 4 .52 .76 .72 .88 .25 .02 .65 .035 0 0 0
넛 1 .22 .92 .90 1.28 .20 0 .10 .00 1 0 1
넝 2 .38 .52 .48 .57 .45 .10 .17 .06 1 0 1
노 14 1.45 1.70 1.15 1.70 .35 .18 1.20 .055 2 1 1
녹 1 1.30 1.65 1.65 1.65 .40 .10 .34 .28 1 1 1
논 9 1.55 1.65 1.18 2.05 .72 .40 1.30 .08 2 1 1
놈 1 1.00 2.15 2.15 2.15 .52 .10 1.18 .00 2 1 1
놋 11 1.50 .82 .68 .95 .38 .08 .60 .08 1 1 1
농 6 1.65 1.55 1.55 1.55 .40 .26 .90 .25 1 1 1
누 12 1.85 1.15 .85 1.80 .28 .12 1.55 .11 1 1 1
눅 1 .54 1.60 1.60 1.60 .30 .10 .42 .00 1 1 1
눈 10 1.00 .85 .68 1.25 .36 .65 1.10 .22 2 1 1
눔 4 .43 .78 .70 1.00 .30 .15 .50 .17 1 1 0
눗 7 1.00 1.20 .88 1.62 .28 .15 .85 .12 1 1 1
눙 5 .52 .80 .60 .83 .30 .50 1.12 .25 1 1 0
마 1 .58 .90 .82 1.00 .95 .12 1.75 .00 1 0 1
막 3 .68 1.55 1.75 1.60 .95 0 1.45 .04 0 1 1
만 1 .60 1.12 1.05 1.18 .95 .18 1.60 .00 1 0 1
맘 4 .32 .80 .72 .75 .72 .06 .12 .06 1 0 0
맛 1 .58 1.28 .55 1.30 .85 0 1.25 .00 1 0 1
망 1 .66 1.35 .42 1.08 .88 .06 1.10 .00 2 0 1
머 3 1.08 1.25 1.65 1.45 .20 .04 .72 .035 0 0 1
먹 5 .58 1.20 .90 .64 .25 .12 .30 .14 0 0 0
먼 9 .56 1.00 .82 .76 .25 .06 .52 .04 0 0 0
멈 2 .48 .80 1.00 .78 .27 .18 .30 .08 1 0 0
멋 1 .22 1.18 1.35 1.36 .20 0 .12 .00 1 0 1
멍 2 .40 .36 .28 .50 .58 .10 .68 .065 1 0 1
모 18 1.55 1.00 .65 1.40 .40 .25 .50 .07 3 1 1
목 9 1.70 1.24 .90 1.95 .55 0 1.65 .62 1 1 1
몬 7 1.65 .84 .72 1.00 .40 .12 .72 .09 1 0 1
몸 1 1.00 2.80 2.80 2.80 .60 .06 1.40 .00 1 1 1
못 10 1.55 1.20 .55 1.18 .40 .08 .20 .12 2 1 1
몽 8 1.75 1.45 1.45 1.45 .42 .14 .20 .30 1 1 1
무 9 1.92 1.42 1.90 1.70 .30 .04 1.55 .11 0 1 1
묵 5 .56 1.00 .35 .82 .32 .12 .14 .23 1 1 0
문 3 .54 1.00 1.55 1.20 .32 .02 1.60 .25 0 1 1
뭄 8 1.85 1.40 .62 1.10 .45 .02 .25 .58 1 1 1
뭇 3 .72 1.00 .70 1.65 .32 .06 1.12 .23 0 1 1
뭉 7 .48 .78 .66 .76 .34 .24 .48 .17 1 1 0
사 1 .34 .56 .58 1.05 .16 0 .00 .00 1 0 0
삭 1 .40 .62 .58 1.52 .18 0 .00 .00 1 0 0
산 1 .30 .54 .46 .66 .18 0 .08 .00 0 0 0
삼 4 .29 .60 .55 .64 .23 .06 .12 .035 1 0 0
삿 1 .28 .42 .42 1.65 .14 0 .00 .00 1 0 0
상 1 .40 .55 .52 .90 .20 .06 .05 .00 1 0 0
서 4 .88 1.45 .86 1.40 .18 .02 .60 .018 0 0 0
석 3 .46 .65 .45 .75 .23 .08 .42 .18 0 0 0
선 2 .30 .68 .52 1.25 .22 .03 .16 .065 2 0 0
섬 4 .52 .72 .55 .92 .23 .12 .54 .12 2 0 0
섯 1 .22 .66 .72 1.35 .20 0 .10 .00 1 0 0
성 3 .38 .30 .28 .66 .22 0 .06 .045 1 0 0
소 11 1.45 .57 .54 1.12 .20 .03 .30 .40 1 0 0
속 7 1.35 .59 .55 1.00 .23 .65 .35 .18 2 0 0
손 4 .43 .68 .68 .75 .23 .45 .48 .24 1 0 0
솜 1 1.00 2.20 2.20 2.20 .48 .04 1.00 .00 1 1 0
솟 9 1.43 .48 .44 1.45 .20 .02 .28 .48 1 0 0
송 12 1.62 .48 .42 .90 .22 .10 1.25 .12 2 0 0
수 12 1.88 1.10 .90 1.88 .27 .02 1.40 .095 0 1 0
숙 2 .40 1.10 .92 1.08 .30 .80 .58 .14 2 1 0
순 6 .78 1.05 .72 1.10 .30 .06 .68 .20 0 1 0
숨 4 .48 .78 .55 .95 .32 .02 .88 .17 0 1 0
숫 1 .90 .70 .60 2.65 .40 0 .10 .00 1 1 1
숭 2 .40 .83 .83 .74 .30 .22 .62 .22 0 1 0
아 1 .45 1.35 1.20 .98 1.20 .72 .40 .00 2 0 0
악 1 .54 1.20 1.08 .96 1.00 1.10 .50 .00 2 0 0
안 1 .56 1.55 1.35 1.12 1.10 .65 .38 .00 2 0 0
암 1 .36 .90 .90 .90 .85 .15 .20 .00 1 0 0
앗 11 .84 1.00 .86 1.80 .85 .05 1.55 .12 1 0 0
앙 1 .48 1.25 1.12 .90 1.05 1.10 .42 .00 2 0 0
어 5 1.08 1.65 1.65 1.18 .20 .18 .65 .025 2 0 0
억 3 .38 .65 .65 .80 .27 .20 .36 .21 1 0 0
언 2 .36 1.00 .70 .88 .28 .75 .42 .18 2 0 0
엄 2 .47 1.10 1.10 .82 .28 .12 .54 .30 1 0 0
엇 1 .22 .78 .76 1.30 .20 0 .10 .00 2 0 0
엉 2 .40 .75 .68 .57 .72 .40 .22 .06 1 0 0
오 7 1.70 1.70 1.15 1.28 .45 .25 .86 .075 3 1 0
옥 8 1.70 1.65 1.22 1.35 .46 .65 .92 .08 3 1 0
온 6 1.62 1.65 1.05 1.25 .44 .22 .72 .10 2 1 0
옴 1 1.00 2.25 2.25 2.25 .56 .12 1.04 .00 2 1 0
옷 12 1.60 1.25 .85 1.00 .45 .20 .55 .14 2 1 0
옹 7 1.72 1.70 .95 1.20 .48 .14 .32 .14 3 1 0
우 9 1.92 1.45 1.40 1.32 .28 .18 1.45 .12 2 1 0
욱 3 .50 1.05 1.05 .88 .32 .15 1.16 .23 1 1 0
운 3 .86 1.45 1.45 1.45 .34 .28 .70 .24 1 1 1
움 4 .56 .85 .85 .78 .33 .22 .52 .20 1 1 0
웃 5 .85 1.08 .62 1.30 .30 .05 .30 .16 1 1 0
웅 7 1.78 1.35 1.30 1.10 .45 .20 .38 .24 2 1 0
'''

# Corrections reflect actual mesh topology/evaluator behavior, not just design labels.
# A Rain layout goes downward; it must not be used for roots/spikes rising from ground.
OVERRIDES = {
    # These are single attacks with an arrival burst, not instant area bursts.
    '갓': {'behavior': 'Projectile'},
    '난': {'behavior': 'Projectile'},
    '만': {'behavior': 'Projectile', 'accentFamily': 'Ember'},
    '목': {'layout': 'Field', 'family': 'Thorn'},
    '뭇': {'layout': 'Pillar'},
    '각': {'family': 'Vine', 'accentFamily': 'Leaf'},
    '감': {'family': 'Seal', 'accentFamily': 'Vine'},
    '건': {'family': 'Leaf', 'accentFamily': 'Lotus'},
    '검': {'family': 'Vine', 'accentFamily': 'Leaf'},
    '고': {'layout': 'Field', 'accentFamily': 'Leaf'},
    '곡': {'family': 'Vine', 'accentFamily': 'Leaf'},
    '곤': {'family': 'Vine', 'accentFamily': 'Thorn'},
    '곰': {'accentFamily': 'Leaf'}, '놈': {'accentFamily': 'Flame'},
    '몸': {'accentFamily': 'Rock'}, '솜': {'accentFamily': 'Shard'},
    '옴': {'accentFamily': 'Ripple'},
    '궁': {'family': 'Vine', 'accentFamily': 'Leaf'},
    '남': {'family': 'Ember', 'accentFamily': 'Lotus'},
    '녹': {'family': 'Lotus', 'accentFamily': 'Flame'},
    '놋': {'family': 'Shard', 'accentFamily': 'Ember'},
    '마': {'accentFamily': 'Sand'}, '막': {'family': 'Rock', 'accentFamily': 'Sand'},
    '맘': {'family': 'Seal', 'accentFamily': 'Rock'},
    '맛': {'accentFamily': 'Shard'}, '망': {'accentFamily': 'Ripple'},
    '머': {'family': 'Rock', 'accentFamily': 'Sand'},
    '먹': {'family': 'Shard', 'accentFamily': 'Sand'},
    '먼': {'family': 'Thorn', 'accentFamily': 'Ember'},
    '멈': {'family': 'Rock', 'accentFamily': 'Seal'},
    '모': {'family': 'Sand', 'accentFamily': 'Cloud'},
    '몬': {'family': 'Seal', 'accentFamily': 'Sand'},
    '못': {'family': 'Sand', 'accentFamily': 'Shard'},
    '산': {'family': 'Shard', 'accentFamily': 'Seal'},
    '삼': {'family': 'Shard', 'accentFamily': 'Seal'},
    '상': {'accentFamily': 'Ripple'}, '송': {'layout': 'Fan', 'accentFamily': 'Ripple'},
    '선': {'family': 'Shard', 'accentFamily': 'Ribbon'},
    '섬': {'family': 'Ribbon', 'accentFamily': 'Shard'},
    '손': {'family': 'Ring', 'accentFamily': 'Ember'},
    '아': {'family': 'Ember', 'accentFamily': 'Ripple'},
    '악': {'family': 'Ember', 'accentFamily': 'Ribbon'},
    '안': {'family': 'Ember', 'accentFamily': 'Ripple'},
    '앙': {'family': 'Ember', 'accentFamily': 'Ripple'},
    '엉': {'family': 'Ember', 'accentFamily': 'Ripple'},
    '암': {'family': 'Lotus', 'accentFamily': 'Shard'},
    '앗': {'family': 'Shard', 'accentFamily': 'Ripple'},
    '어': {'family': 'Ripple', 'accentFamily': 'Ribbon'},
    '옹': {'family': 'Ripple', 'accentFamily': 'Ribbon', 'layout': 'Wave'},
}

def enum_values(path: Path, name: str) -> set[str]:
    content = path.read_text(encoding='utf-8-sig')
    match = re.search(r'enum\s+' + re.escape(name) + r'\s*\{([^}]+)\}', content)
    if not match:
        raise ValueError(f'Missing C# enum: {name}')
    return {x.strip().split('=')[0].strip() for x in match.group(1).split(',') if x.strip()}

def rgba(value: str) -> dict[str, float]:
    value = value.lstrip('#')
    channels = [int(value[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return dict(zip(('r', 'g', 'b', 'a'), channels + [1.0]))

def read_authoring() -> dict[str, dict]:
    authored = {}
    for raw in AUTHORING.strip().splitlines():
        fields = raw.split()
        if len(fields) != 13:
            raise ValueError(f'Bad authoring row ({len(fields)}): {raw}')
        glyph, count, size, sx, sy, sz, flight, turns, lift, stagger, ribbons, grounded, mist = fields
        # Keep a strict assertion below; a malformed copied table must fail loudly.
        authored[glyph] = {'count': int(count), 'size': float(size),
            'partScale': {'x': float(sx), 'y': float(sy), 'z': float(sz)},
            'flight': float(flight), 'turns': float(turns), 'lift': float(lift),
            'stagger': float(stagger), 'ribbons': int(ribbons),
            'grounded': bool(int(grounded)), 'mist': bool(int(mist))}
    return authored

def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'Art/SpellVFX120/build_manifest.json')
    args = parser.parse_args()
    source = json.loads(SOURCE.read_text(encoding='utf-8-sig'))
    audit = json.loads(AUDIT.read_text(encoding='utf-8-sig'))
    reviewed = {x['unity_asset_path']: x for x in audit['recommended_sources']}
    behaviors = enum_values(PROFILE, 'Vfx120Behavior')
    layouts = enum_values(PROFILE, 'Vfx120Layout')
    family_match = re.search(r'Families\s*=\s*\{([^}]+)\}', FACTORY.read_text(encoding='utf-8-sig'))
    families = set(re.findall(r'"([A-Za-z]+)"', family_match.group(1)))
    if (ROOT / 'Art/SpellVFX120/Blender/StoneGuardian_Jangseung_B1.fbx').is_file():
        families.add('StoneGuardian')
    authored = read_authoring()
    spells, checks = [], []
    for design in source['spells']:
        glyph, recipe = design['glyph'], design['recipe']
        if glyph not in authored:
            raise ValueError(f'Missing numeric art direction: {glyph}')
        assigned = design['assignment'] == 'ASSIGNED_EXISTING_GAMEPLAY'
        definition = dict(glyph=glyph, title=design['visualTitle'], intent=design['gameplayEffectVerbatim'],
            family=recipe['primitive'], accentFamily=recipe['secondaryPrimitives'][0],
            behavior=recipe['behavior'], layout=recipe['layout'], assigned=assigned,
            connected=glyph in CONNECTED, duration=float(recipe['duration']),
            pigment=rgba(recipe['palette']['primary']), accent=rgba(recipe['palette']['secondary']))
        definition.update(authored[glyph])
        definition.update(OVERRIDES.get(glyph, {}))
        if glyph == '검':
            definition.update(family='Tree', behavior='Zone', layout='Field', count=1, size=2.3,
                partScale=dict(x=2.6,y=2.6,z=2.6), grounded=True, lift=0.0)
        if glyph == '안':
            definition['accentFamily'] = 'Ember'
        if glyph == '몸':
            definition.update(family='StoneGuardian', count=1, partScale=dict(x=2.2,y=2.2,z=2.2), grounded=True)
        if glyph == '뭄':
            definition.update(family='BridgeSlab', count=8, partScale=dict(x=1.5,y=1.0,z=.72), grounded=True, lift=0.0)
        if glyph == '건':
            definition.update(count=7, partScale=dict(x=.85,y=.45,z=.8))
        if glyph == '농':
            definition.update(pigment=rgba('#9D9990'),accent=rgba('#C5BCAC'))
        if glyph == '몽':
            definition.update(pigment=rgba('#6D6559'),accent=rgba('#A2947C'))
        if glyph in '오옥온옷옹':
            definition.update(family='WaveCrest', count=1 if glyph in '오온옹' else 2,
                partScale=dict(x=3.5,y=2.8 if glyph=='옹' else 2.0,z=2.2), lift=0.0, grounded=True)
        if not assigned:
            definition['behavior'] = 'Reserve'
        if definition['behavior'] not in behaviors or definition['layout'] not in layouts:
            raise ValueError(f'Invalid C# enum for {glyph}: {definition}')
        if not {definition['family'], definition['accentFamily']} <= families:
            raise ValueError(f'Invalid mesh factory family: {glyph}')
        if not 1 <= definition['count'] <= 32 or not 0 <= definition['ribbons'] <= 3:
            raise ValueError(f'Invalid visual object budget: {glyph}')
        if not (.2 <= definition['size'] <= 8 and .1 <= definition['flight'] <= 3):
            raise ValueError(f'Invalid spatial/time profile: {glyph}')
        if not all(math.isfinite(v) and v > 0 for v in definition['partScale'].values()):
            raise ValueError(f'Invalid mesh scale: {glyph}')
        candidates = design['patternSources']
        if not candidates:
            raise ValueError(f'No reviewed source pattern: {glyph}')
        pattern = candidates[0]['unity_asset_path']
        evidence = reviewed.get(pattern)
        if not evidence:
            raise ValueError(f'Pattern is outside reviewed audit: {pattern}')
        texture = ROOT / 'Oheangbu' / pattern
        guid_match = re.search(r'^guid:\s*([0-9a-f]{32})\s*$', Path(str(texture)+'.meta').read_text(), re.M)
        if not texture.is_file() or not guid_match or guid_match.group(1) != evidence['guid']:
            raise ValueError(f'Texture path/GUID mismatch: {pattern}')
        definition['pattern'] = pattern
        # Retain full prose, phases, event contract, UV-mask requirement and source provenance.
        definition['recipe'] = json.dumps({
            'design': design, 'authoredDefinitionOverrides': OVERRIDES.get(glyph, {}),
            'previewNumericAuthoring': authored[glyph], 'patternEvidence': evidence,
            'dispatchStatus': 'BASELINE_GAMEPLAY_ADAPTER' if glyph in CONNECTED else ('REVIEW_ONLY_UNCONNECTED' if assigned else 'INTENTIONAL_BLANK_GALLERY_ONLY'),
            'runtimeLimit': 'Recipe data alone does not implement per-glyph gameplay event choreography; inspect runtime and real capture separately.'
        }, ensure_ascii=False, separators=(',', ':'))
        spells.append(definition)
        checks.append({'glyph': glyph, 'pattern': pattern, 'guid': evidence['guid'], 'exists': True})
    if len(spells) != 120 or len({x['glyph'] for x in spells}) != 120:
        raise ValueError('Expected 120 unique spells')
    if sum(x['assigned'] for x in spells) != 100 or sum(x['connected'] for x in spells) != 15:
        raise ValueError('Assignment/connection contract changed')
    if any(x['connected'] and not x['assigned'] for x in spells):
        raise ValueError('Intentional blank must never claim a gameplay connection')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({'spells': spells}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    report = {'status': 'MANIFEST_SCHEMA_AND_SOURCE_VALIDATED_NOT_RUNTIME_PROOF', 'count': 120,
        'assigned': 100, 'connected': 15, 'unconnectedAssigned': 85, 'intentionalBlanks': 20,
        'sourceSha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'manifestSha256': hashlib.sha256(args.output.read_bytes()).hexdigest(),
        'uniqueNumericShapeMotionSignatures': len({json.dumps({k:v for k,v in s.items() if k in ('family','accentFamily','behavior','layout','count','partScale','flight','turns','lift','stagger','ribbons','grounded','mist')},sort_keys=True) for s in spells}),
        'patterns': checks, 'notes': [
            'One Beast body per summon proposal; not a gameplay summon-count rule.',
            'One Blade per weapon silhouette; not a weapon state-machine implementation.',
            'Rising thorn fields use Field, not the downward Rain evaluator.',
            'Water projectile silhouette uses scaled Ember geometry with Ripple/Ribbon accent.',
            'Colors and numeric uniqueness are not acceptance evidence for 120 distinct rendered effects.',
            'Some semantic choreography needs per-glyph runtime evaluators; serialized design is not implementation.'
        ]}
    args.output.with_name('build_manifest_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k not in ('patterns','notes')},ensure_ascii=False))

if __name__ == '__main__':
    main()
