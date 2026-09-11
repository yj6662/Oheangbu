"""Sequential, resumable one-glyph KTP migration through the Unity editor queue."""
import json,time,uuid
from pathlib import Path
o=Path('Art/SpellVFX120')
def call(method,request):
    command=o/'command.json'
    if command.exists():raise RuntimeError('Unconsumed Unity command')
    token=uuid.uuid4().hex;pending=o/(token+'.pending');pending.write_text(json.dumps(dict(id=token,method=method,request=request)),encoding='utf-8');pending.replace(command)
    response=o/('response_'+token+'.json');deadline=time.monotonic()+180
    while not response.exists():
        if time.monotonic()>deadline:raise TimeoutError(str(response))
        time.sleep(.3)
    data=json.loads(response.read_text(encoding='utf-8-sig'))
    if data['status']!='COMPLETE':raise RuntimeError(data)
    return data['result']
if __name__=='__main__':
    manifest=json.loads((o/'build_manifest.json').read_text(encoding='utf-8'))
    progress=o/'ORIGINAL_KTP_PROGRESS.json';d=json.loads(progress.read_text(encoding='utf-8')) if progress.exists() else dict(rows=[])
    for item in manifest['spells']:
        if not item['assigned']:continue
        glyph=item['glyph']
        if any(r['glyph']==glyph for r in d['rows']):continue
        result=json.loads(call('OriginalBuild',glyph));d['rows'].append(result)
        progress.write_text(json.dumps(d,ensure_ascii=False,indent=2),encoding='utf-8')
        print(glyph+' '+result['status'],flush=True)
