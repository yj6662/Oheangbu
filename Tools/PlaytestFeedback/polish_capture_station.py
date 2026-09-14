"""One preserved, authored review station. No camera movement, traversal or video."""
from pathlib import Path
import json,sys,shutil
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools/Unity'))
import world_macro
station=sys.argv[1]
plan=json.loads((ROOT/'Art/World/WorldMacro/Dressing/capture_plan.json').read_text(encoding='utf-8-sig'))
view=next(v for v in plan['views'] if v['id']==station)
label='Polish_'+station+'_after'
result=world_macro.call('DressingCapture',json.dumps(dict(label=label,dressing=True,position=view['position'],target=view['target'])))
source=Path(result['result'])
out=ROOT/'Art/PlaytestPolish/Vegetation/Screenshots';out.mkdir(parents=True,exist_ok=True)
for suffix in ('.png','.json'):shutil.copy2(source.with_suffix(suffix),out/(label+suffix))
print(out/(label+'.png'))
