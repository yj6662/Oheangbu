"""Single-command transport to the existing Unity Editor, no parallel scene mutation."""
import json,time,uuid,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Prologue'
def call(method):
    OUT.mkdir(parents=True,exist_ok=True)
    command=OUT/'command.json'
    if command.exists():raise RuntimeError('Unconsumed prologue command exists')
    token=uuid.uuid4().hex
    pending=OUT/(token+'.pending');pending.write_text(json.dumps(dict(id=token,method=method)),encoding='utf-8');pending.replace(command)
    response=OUT/('response_'+token+'.json');print(str(response),flush=True)
    deadline=time.monotonic()+180
    while not response.exists():
        if time.monotonic()>deadline:raise TimeoutError(response)
        time.sleep(.5)
    result=json.loads(response.read_text(encoding='utf-8-sig'));print(json.dumps(result,ensure_ascii=False),flush=True)
    if result['status']!='COMPLETE':raise RuntimeError(result)
    return result
if __name__=='__main__':call(sys.argv[1])
