"""Serialize one Unity Editor command. No build or walking is implicit."""
import argparse,json,time,uuid
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/PlaytestPolish/Unity'
def call(kind,method,argument='',timeout=600):
 OUT.mkdir(parents=True,exist_ok=True)
 pending=OUT/'command.json'
 if pending.exists(): raise RuntimeError('Unconsumed polish command; inspect before retrying.')
 token=uuid.uuid4().hex
 body={'id':token,'type':kind,'method':method,'argument':argument}
 temporary=OUT/(token+'.pending')
 temporary.write_text(json.dumps(body),encoding='utf-8');temporary.replace(pending)
 response=OUT/('response_'+token+'.json')
 print(response,flush=True)
 until=time.monotonic()+timeout
 while not response.exists():
  if time.monotonic()>until:raise TimeoutError(response)
  time.sleep(.4)
 result=json.loads(response.read_text(encoding='utf-8-sig'))
 print(json.dumps(result,ensure_ascii=False,indent=2),flush=True)
 if result['status']!='COMPLETE':raise RuntimeError(result.get('error'))
 return result
if __name__=='__main__':
 parser=argparse.ArgumentParser();parser.add_argument('type');parser.add_argument('method');parser.add_argument('argument',nargs='?',default='')
 a=parser.parse_args();call(a.type,a.method,a.argument)
