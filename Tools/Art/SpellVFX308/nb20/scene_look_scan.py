#!/usr/bin/env python3
"""NB20 calibration: read-only scan of a scene file's look stack (Volumes + their profile assets, camera renderer index, directional lights,
RenderSettings fog / ambient / skybox, LookDriver308) - no Unity, no scene open.
python Tools/Art/SpellVFX308/nb20/scene_look_scan.py Oheangbu/Assets/_Project/Scenes/World/W_Demo_Main.unity [more scenes]"""
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
ASSETS = REPO / 'Oheangbu' / 'Assets'
VOLUME = '172515602e62fb746b5d573b38a5fe58'
CAMDATA = 'a79441f348de89743a2939f4d699eac1'   # UniversalAdditionalCameraData


def guid_index():
    idx = {}
    for meta in ASSETS.rglob('*.meta'):
        try:
            for line in meta.open(encoding='utf-8', errors='ignore'):
                if line.startswith('guid:'):
                    idx[line.split()[1].strip()] = str(meta)[:-5].replace(str(REPO) + '\\', '')
                    break
        except OSError:
            pass
    return idx


def script_guid(name):
    for p in ASSETS.rglob(name + '.cs.meta'):
        for line in p.open(encoding='utf-8', errors='ignore'):
            if line.startswith('guid:'):
                return line.split()[1]
    return None


def docs(text):
    parts = re.split(r'^--- !u!(\d+) &(-?\d+).*$', text, flags=re.M)
    for i in range(1, len(parts) - 2, 3):
        yield parts[i], parts[i + 1], parts[i + 2]


def main():
    idx = guid_index()
    look = script_guid('LookDriver308')
    for scene in sys.argv[1:]:
        text = Path(scene).read_text(encoding='utf-8', errors='ignore')
        print('==', scene, len(text) // 1024, 'KB')
        names = {}
        for cls, fid, body in docs(text):
            if cls == '1':
                m = re.search(r'm_Name: (.*)', body)
                names[fid] = m.group(1).strip() if m else '?'
        owner = {}
        for cls, fid, body in docs(text):
            m = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', body)
            if m:
                owner[fid] = names.get(m.group(1), m.group(1))
        for cls, fid, body in docs(text):
            if cls == '114' and VOLUME in body:
                g = re.search(r'sharedProfile: \{fileID: -?\d+, guid: ([0-9a-f]+)', body)
                print('  Volume on %-30s global=%s priority=%s weight=%s enabled=%s profile=%s' % (owner.get(fid, '?'), re.search(r'm_IsGlobal: (\d)', body).group(1) if re.search(r'm_IsGlobal: (\d)', body) else '?',
                      (re.search(r'priority: ([-\d.]+)', body) or [None, '?'])[1], (re.search(r'weight: ([-\d.]+)', body) or [None, '?'])[1],
                      (re.search(r'm_Enabled: (\d)', body) or [None, '?'])[1], idx.get(g.group(1), g.group(1)) if g else '(none)'))
            elif cls == '114' and CAMDATA in body:
                print('  CameraData on %-26s rendererIndex=%s renderPostProcessing=%s volumeMask=%s' % (owner.get(fid, '?'), (re.search(r'm_RendererIndex: (-?\d+)', body) or [None, '?'])[1],
                      (re.search(r'm_RenderPostProcessing: (\d)', body) or [None, '?'])[1], (re.search(r'm_VolumeLayerMask:\s*\n\s*serializedVersion: \d+\s*\n\s*m_Bits: (\d+)', body) or [None, '?'])[1]))
            elif cls == '114' and look and look in body:
                print('  LookDriver308 on', owner.get(fid, '?'), ' | ', ' '.join(l.strip() for l in body.splitlines()[8:20]))
            elif cls == '108' and re.search(r'm_Type: 1\b', body):
                print('  Directional light on %-20s colour=%s intensity=%s' % (owner.get(fid, '?'), (re.search(r'm_Color: (\{[^}]*\})', body) or [None, '?'])[1], (re.search(r'm_Intensity: ([-\d.]+)', body) or [None, '?'])[1]))
            elif cls == '104':
                keys = ['m_Fog', 'm_FogColor', 'm_FogMode', 'm_FogDensity', 'm_LinearFogStart', 'm_LinearFogEnd', 'm_AmbientMode', 'm_AmbientSkyColor', 'm_AmbientEquatorColor', 'm_AmbientGroundColor', 'm_AmbientIntensity']
                vals = {k: (re.search(r'\b' + k + r': (.*)', body) or [None, '?'])[1] for k in keys}
                sk = re.search(r'm_SkyboxMaterial: \{fileID: -?\d+(?:, guid: ([0-9a-f]+))?', body)
                print('  RenderSettings', vals, 'skybox', idx.get(sk.group(1), sk.group(1)) if sk and sk.group(1) else '(none)')


if __name__ == '__main__':
    main()
