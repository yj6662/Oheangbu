"""Read-only source audit. Writes reports only; never controls Unity or edits assets."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'

def run():
    source = OUT / 'layout.json'
    layout = json.loads(source.read_text(encoding='utf-8-sig'))
    places, routes = layout['Places'], layout['Routes']
    ids = [p['Id'] for p in places]
    checks = []
    def check(value, name):
        checks.append(dict(status='PASS' if value else 'FAIL', check=name))
    check(len(ids) == len(set(ids)), 'unique POI IDs')
    check(len({r['Id'] for r in routes}) == len(routes), 'unique route IDs')
    check(all(r['From'] in ids and r['To'] in ids for r in routes), 'route endpoints exist')
    check(all(0 <= p['XZ']['x'] <= layout['Extent']['x'] and 0 <= p['XZ']['y'] <= layout['Extent']['y'] for p in places), 'POIs inside declared extent')
    check(all(r['Width'] > 0 for r in routes), 'positive route widths')
    check(all(r.get('RequiredAbility') for r in routes if r['Role'] == 3), 'ability routes declare their ability')
    def reachable(abilities):
        seen = {'mine'}
        changed = True
        while changed:
            before = len(seen)
            for r in routes:
                if r.get('RequiredAbility') and r['RequiredAbility'] not in abilities:
                    continue
                if r['From'] in seen:
                    seen.add(r['To'])
                if not r.get('OneWay', False) and r['To'] in seen:
                    seen.add(r['From'])
            changed = before != len(seen)
        return seen
    before, after = reachable(set()), reachable({'국'})
    check({'relay', 'merchant', 'logging', 'sanctuary'}.issubset(before), 'local exploration reachable before guk in topology')
    check('high_cache' not in before and 'high_cache' in after, 'guk reward is gated in directed topology')
    check(set(ids).issubset(after), 'no disconnected POIs after declared ability unlock')
    # These are known missing implementation contracts, not failures of the topology tests.
    gaps = [
        'Layout export is a snapshot; confirm the live Unity asset matches before applying it.',
        'OneWay/RequiredAbility are declarations, not a generic physical gate. Progression251 Guk has a separate live lift proof; other gates remain unverified.',
        'Realm sequence and palace gates are not encoded in the route schema.',
        'Mine/inn, Village245 and Progression251 declare gameplay/map/checkpoint bindings; validate version-specific NavMesh/runtime evidence separately. Remaining realms are not migrated.',
        'Candidate terrain uses a separate slice save generation. Canonical rebuild-v2 still belongs to the old terrain.',
        'Candidate runtime fixtures do not approve manual exploration, visuals, vehicle passage or frame performance.',
    ]
    paths = [source, ROOT/'Docs/Specs/SPEC-COMPACT-REBUILD.md',
             ROOT/'Oheangbu/Assets/_Project/Scripts/Data/World/CompactWorldLayoutSO.cs',
             ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/WorldLayout.asset',
             ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/Campaign.asset',
             ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Data/03_Content.asset',
             ROOT/'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity']
    # Candidate-only systems also invalidate this snapshot; canonical assets alone
    # cannot detect a changed village catalog or a new runtime transaction path.
    receipt_path = OUT/'migration_slice.json'
    if receipt_path.exists():
        receipt = json.loads(receipt_path.read_text(encoding='utf-8-sig'))
        candidate = ROOT/'Oheangbu'/receipt['scene']
        paths += [receipt_path, candidate, candidate.parent/'Content.asset', candidate.parent/'Map.asset']
        paths += list((candidate.parent/'Village245').glob('*.asset'))
        paths += list((candidate.parent/'Progression251').glob('*'))
        paths += list((candidate.parent/'Escort252').glob('*'))
        paths += list((candidate.parent/'SouthGate253').glob('*'))
        paths += list((candidate.parent/'Branches259').glob('*'))
        paths += list((candidate.parent/'Detail261').glob('*'))
        paths += list((candidate.parent/'Arrival262').glob('*'))
        paths += list((candidate.parent/'Sinmok263').glob('*'))
        paths += list((candidate.parent/'Sinmok271').glob('*'))
        paths += list((candidate.parent/'RoadProps264').glob('*'))
        paths += list((candidate.parent/'Contact265').glob('*'))
        paths += list((candidate.parent/'Grass266').glob('*'))
        paths += [candidate.parent/'Cartography.png', candidate.parent/'PaintedTerrain_Cheongrim.png', candidate.parent/'ExploredTerrain.png']
        paths += [candidate.parent/'Navigation.asset', candidate.parent/'Geography.asset']
    for pattern in ('*Equipment*.cs', 'WorldMacroProgress.cs', 'RuntimePlayerStats.cs', 'WorldMacroPlaytestSession*.cs', 'WorldMacroPalanquin*.cs', 'CompactRebuildVehicle250.cs', 'CompactRebuildProgression251*.cs', 'CompactRebuildEscort252*.cs', 'CompactRebuildSouthGate253*.cs', 'SouthGateDoorPresentation.cs', 'CompactRebuildNavigation.cs', 'DemoGukRevisitSite.cs', 'DemoGrowthLessonLink.cs', 'DemoEscort*.cs'):
        paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts').rglob(pattern))
    for pattern in ('CompactBranches259*.cs', 'CompactDetail261*.cs', 'CompactArrival262*.cs', 'CompactSinmok263*.cs', 'CompactSinmok271*.cs', 'CompactRoadProps264*.cs', 'CompactContact265*.cs', 'CompactGrass266*.cs', 'CompactGrass267*.cs', 'CompactChroma268*.cs'):
        paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro').glob(pattern))
    for pattern in ('WorldLocation*.cs','WorldRealmAtmosphere.cs','MapData.cs','WorldMapPresenter*.cs','WorldMapBakedDataSO.cs','WorldLoading*.cs','PlaytestUiRoot*.cs'):
        paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts/App/World').rglob(pattern))
    paths += list((ROOT/'Oheangbu/Assets/_Project/Art/UI/Loading270').glob('*'))
    paths += [ROOT/'Oheangbu/ProjectSettings/EditorBuildSettings.asset', ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/WorldMacro/CompactLoading270.cs', ROOT/'Docs/Specs/SPEC-LOBBY-LOADING-270.md']
    paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts/App').rglob('Sinmok*.cs'))
    paths += list((ROOT/'Oheangbu/Assets/_Project/Shaders').glob('CompactMountainMist.shader'))
    paths += [ROOT/'Docs/Specs/SPEC-SINMOK-REALMS-263.md', OUT/'Sinmok263/routes.json',
              ROOT/'Tools/Art/plan_sinmok263.py', ROOT/'Tools/Art/tint_realm_maps263.py']
    paths += [OUT/'art_placements.json', OUT/'Cartography/placements.json']
    paths += [OUT/'RoadProps264/placements.json', ROOT/'Docs/Specs/SPEC-ROAD-PROPS-264.md', ROOT/'Docs/Specs/SPEC-ENVIRONMENT-CONTACT-265.md']
    paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts/App/World').glob('Compact*265.cs'))
    paths += list((ROOT/'Oheangbu/Assets/_Project/Scripts/App/World').glob('CompactGrass*266.cs'))
    paths += [ROOT/'Docs/Specs/SPEC-CHEONGRIM-CHROMA-268.md', ROOT/'Oheangbu/Assets/_Project/Shaders/InkChroma268.hlsl', ROOT/'Docs/Specs/SPEC-WORLD-GRASS-266.md', ROOT/'Docs/Specs/SPEC-CHEONGRIM-GRASS-WIND-267.md', ROOT/'Oheangbu/Assets/_Project/Shaders/InkPaintingStudy/InkPaintingGround.shader']
    paths += [ROOT/'Oheangbu/Assets/_Project/Scripts/App/World/CompactRebuildArtRenderer.cs', ROOT/'Oheangbu/Assets/_Project/Shaders/CompactNaturalVegetation.shader', ROOT/'Oheangbu/Assets/_Project/Shaders/InkPaintingStudy/InkPaintingVegetation.shader']
    hashes = {str(p.relative_to(ROOT)).replace('\\', '/'): hashlib.sha256(p.read_bytes()).hexdigest() for p in paths if p.is_file()}
    report = dict(checked_utc=datetime.now(timezone.utc).isoformat(), scope='Static exported topology only; no Unity or runtime mutation', checks=checks, known_gaps=gaps, inputs=hashes)
    previous = OUT/'consistency.json'
    old = json.loads(previous.read_text(encoding='utf-8')) if previous.exists() else None
    report['inputs_changed_since_previous'] = old is None or hashes != old.get('inputs')
    previous.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    (OUT/'consistency.txt').write_text('\n'.join([report['scope'], report['checked_utc']] + [f"{c['status']} {c['check']}" for c in checks] + ['OPEN '+g for g in gaps]), encoding='utf-8')
    print(json.dumps(dict(passed=sum(c['status']=='PASS' for c in checks), failed=sum(c['status']=='FAIL' for c in checks), known_gaps=len(gaps), inputs_changed=report['inputs_changed_since_previous'])))
    return int(any(c['status']=='FAIL' for c in checks))

if __name__ == '__main__':
    raise SystemExit(run())
