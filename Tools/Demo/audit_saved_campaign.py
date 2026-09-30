"""Inventory saved demo authoring, never runtime fixtures or reserved map markers."""
from pathlib import Path
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import re
import yaml

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
OUT = ROOT / 'Art/Demo/Integration'


def unity_data(path):
    text = path.read_text(encoding='utf-8-sig')
    return yaml.safe_load('\n'.join(text.splitlines()[3:]))['MonoBehaviour']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    content_path = PROJECT / 'Assets/_Project/Art/Demo/Foundation/Content.asset'
    campaign_path = content_path.with_name('Campaign.asset')
    scene_path = PROJECT / 'Assets/_Project/Scenes/World/W_Demo_Campaign.unity'
    content, campaign = unity_data(content_path), unity_data(campaign_path)
    actor_meta = PROJECT / 'Assets/_Project/Scripts/App/Prologue/PrologueEncounter.cs.meta'
    guid = re.search(r'^guid: (\w+)', actor_meta.read_text(), re.M)[1]
    tag_meta = PROJECT / 'Assets/_Project/Scripts/App/Demo/DemoEncounterExpansionTag.cs.meta'
    tag_guid = re.search(r'^guid: (\w+)', tag_meta.read_text(), re.M)[1] if tag_meta.exists() else None
    # Stream documents to avoid materializing the full terrain/vegetation scene as YAML objects.
    scene_actors, document, actor_objects, tags = [], [], {}, {}

    def consume(lines):
        source = ''.join(lines)
        if 'guid: ' + guid in source:
            actor = yaml.safe_load(source)['MonoBehaviour']
            scene_actors.append(actor.get('Id'))
            actor_objects[actor['m_GameObject']['fileID']] = actor.get('Id')
        elif tag_guid and 'guid: ' + tag_guid in source:
            tag = yaml.safe_load(source)['MonoBehaviour']
            tags[tag['m_GameObject']['fileID']] = tag

    with scene_path.open(encoding='utf-8-sig') as stream:
        for line in stream:
            if line.startswith('---'):
                consume(document)
                document = []
            elif not line.startswith('%'):
                document.append(line)
        consume(document)
    specs = content.get('Encounters', [])
    ids = [e['Id'] for e in specs]
    stages = campaign['Stages']
    checkpoints = content.get('Checkpoints', [])
    points = content.get('Points', [])
    test_reports = {}
    for relative in [
        'Chapter3/runtime_chapter3_guk_v6_pass.json', 'Chapter3/escort_state_tests.json',
        'Chapter3/escort_session_tests.json', 'Chapter3/escort_resume_tests.json',
        'Chapter3/south_gate_tests.json', 'Chapter3/south_gate_completion_tests.json',
        'Chapter3/south_gate_integration_tests.json', 'SouthGate/runtime_tests.json',
        'Chapter4/escort_runtime_tests.json',
    ]:
        path = ROOT / 'Art/Demo' / relative
        if not path.exists():
            test_reports[relative] = {'status': 'NOT_RUN'}
            continue
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        test_reports[relative] = {
            'status': data.get('status', 'UNKNOWN'),
            'sha256': digest(path), 'mtimeUtc': datetime.fromtimestamp(path.stat().st_mtime, timezone.utc).isoformat(),
            'passed': len(data.get('passed', [])), 'failed': data.get('failed', []),
            'checkStatusCounts': dict(Counter(c.get('status', 'UNKNOWN') for c in data.get('checks', []) if isinstance(c, dict))),
            'scope': data.get('scope'), 'cleaned': data.get('cleaned'), 'error': data.get('error'),
        }
    elite_ids = [actor_objects[obj] for obj, tag in tags.items() if tag.get('IsElite') and obj in actor_objects]
    normals = [e for e in specs if e['Id'] not in ('cheongryong', 'south_gate_general', 'demo_growth_lesson') and e['Id'] not in elite_ids]
    payload = {
        'generatedUtc': datetime.now(timezone.utc).isoformat(),
        'scope': 'Saved authoring inventory only. Enabled stages are not completion percentage or native play verification.',
        'inputs': {str(p.relative_to(ROOT)): digest(p) for p in (content_path, campaign_path, scene_path)},
        'stages': [{'id': s['Id'], 'implemented': bool(s['Implemented']), 'event': s['Event'], 'trigger': s['TriggerId'], 'reward': s['TongboReward']} for s in stages],
        'implementedStages': sum(bool(s['Implemented']) for s in stages),
        'sceneActors': scene_actors,
        'sceneMatchesContent': Counter(ids) == Counter(scene_actors),
        'duplicateActorIds': [i for i, count in Counter(ids).items() if count != 1],
        'encounters': specs,
        'normalCandidatesExcludingGrowthLesson': len(normals),
        'eliteIdsFromActualSceneTags': elite_ids,
        'newOrdinaryGroupIdsFromActualSceneTags': sorted({t['GroupId'] for t in tags.values() if not t.get('IsElite')}),
        'contentGroupsNotPhysicalEncounters': dict(Counter(e['ContentId'] for e in specs)),
        'explicitCheckpointIds': [c['Id'] for c in checkpoints],
        'restPointIds': [p['Id'] for p in points if p['Kind'] == 2],
        'dialoguePointIdsNotUniquePeople': [p['Id'] for p in points if p['Kind'] == 3],
        'reports': test_reports,
        'unverified': ['Native completion and duration', '32 normals / 12 physical groups / 2 elites',
                       'Unique NPC identities and complete dialogue', 'Checkpoint budget including office/inn/retry points',
                       'Difficulty and moving CPU/GPU performance'],
    }
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / 'saved_inventory.json').write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k: payload[k] for k in ('implementedStages', 'sceneMatchesContent', 'duplicateActorIds', 'normalCandidatesExcludingGrowthLesson', 'explicitCheckpointIds')}, ensure_ascii=False))


if __name__ == '__main__':
    main()
