"""#302 trailer giant: 금강역사 기계 불상 from the user's three-view sheet (front/side/back) via Meshy multi-image to 3D.
One request only (credits are scarce): meshy-7.1, 2k geometry, 4K PBR, quad remesh 200k, A-pose.
The API key is read from .env and never printed. Task record: task.json next to this script.

usage: python guardian302.py submit | poll | download
"""
import base64, json, sys, time, urllib.request
from pathlib import Path

HERE = Path(__file__).parent
ROOT = HERE.parents[2]
OUT = ROOT / 'Art/Characters/Trailer302/GeumgangGuardian'
API = 'https://api.meshy.ai'
TASK = HERE / 'task.json'


def key():
    for line in open(ROOT / '.env', encoding='utf-8'):
        if line.startswith('MESHY_API_KEY='):
            return line.split('=', 1)[1].strip().strip('"')
    raise SystemExit('MESHY_API_KEY missing')


def call(method, path, body=None):
    req = urllib.request.Request(API + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={'Authorization': 'Bearer ' + key(), 'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=120) as r:
        return json.loads(r.read())


def uri(p):
    return 'data:image/png;base64,' + base64.b64encode((HERE / p).read_bytes()).decode()


def submit():
    if TASK.exists() and json.loads(TASK.read_text()).get('id'):
        raise SystemExit('already submitted: ' + TASK.read_text())
    before = call('GET', '/openapi/v1/balance')['balance']
    body = dict(image_urls=[uri('view_front.png'), uri('view_side.png'), uri('view_back.png')],
                ai_model='meshy-7.1', geometry_resolution='2k', should_texture=True, enable_pbr=True, texture_resolution='4k',
                should_remesh=True, topology='quad', target_polycount=200000, pose_mode='a-pose',
                remove_lighting=True, image_enhancement=True)
    r = call('POST', '/openapi/v1/multi-image-to-3d', body)
    rec = dict(id=r.get('result'), submitted=time.strftime('%Y-%m-%d %H:%M:%S'), balance_before=before,
               settings={k: v for k, v in body.items() if k != 'image_urls'}, images=['view_front.png', 'view_side.png', 'view_back.png'])
    TASK.write_text(json.dumps(rec, indent=1, ensure_ascii=False), encoding='utf-8')
    print('submitted', rec['id'], 'balance before', before)


def status():
    rec = json.loads(TASK.read_text())
    return rec, call('GET', '/openapi/v1/multi-image-to-3d/' + rec['id'])


def poll():
    while True:
        rec, t = status()
        print(time.strftime('%H:%M:%S'), t.get('status'), t.get('progress'), flush=True)
        if t.get('status') in ('SUCCEEDED', 'FAILED', 'CANCELED', 'EXPIRED'):
            rec['final'] = {k: t.get(k) for k in ('status', 'progress', 'consumed_credits', 'finished_at', 'task_error')}
            rec['balance_after'] = call('GET', '/openapi/v1/balance')['balance']
            TASK.write_text(json.dumps(rec, indent=1, ensure_ascii=False), encoding='utf-8')
            print(json.dumps(rec['final'], ensure_ascii=False), 'balance after', rec['balance_after'])
            return t
        time.sleep(20)


def download():
    rec, t = status()
    OUT.mkdir(parents=True, exist_ok=True)
    got = []
    for fmt, url in (t.get('model_urls') or {}).items():
        if url and fmt in ('glb', 'fbx', 'obj', 'usdz'):
            dst = OUT / f'GeumgangGuardian302.{fmt}'; urllib.request.urlretrieve(url, dst); got.append(dst.name)
    for i, tex in enumerate(t.get('texture_urls') or []):
        for kind, url in tex.items():
            if url:
                dst = OUT / f'GeumgangGuardian302_{kind}.png'; urllib.request.urlretrieve(url, dst); got.append(dst.name)
    if t.get('thumbnail_url'):
        urllib.request.urlretrieve(t['thumbnail_url'], OUT / 'thumbnail.png'); got.append('thumbnail.png')
    print('\n'.join(got))


if __name__ == '__main__':
    {'submit': submit, 'poll': poll, 'download': download}[sys.argv[1]]()
