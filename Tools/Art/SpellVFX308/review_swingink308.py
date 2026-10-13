import subprocess, sys
from pathlib import Path
from PIL import Image
ROOT = Path('C:/Users/yj666/Oheangbu')
def call(cls, arg):
    r = subprocess.run([sys.executable, str(ROOT/'Tools/Unity/playtest_polish.py'), cls, 'Run', arg], capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.stdout
FEET='3340,100,2690'; EYE='3343.2,100.3,2693.6'; LOOK='3340,99.6,2690.8'
SHOTS={'sweep':[.2,.4,.5,.7],'arc':[.35,.65,.8,1.0],'slam':[.4,.67,.85,1.2],'plant':[.08,.16,.3,.5],'draw':[.12,.25,.45,.8]}
only=sys.argv[1:] or list(SHOTS)
rows=[]
for st in only:
    row=[]
    for t in SHOTS[st]:
        out=call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build', f'swingink:pose:{st}:{t}:{FEET}:0,0,1:{EYE}')
        if 'swing pose' not in out: print('FAIL', st, t, out[-300:])
        name=f'swing_{st}_{int(t*100):03d}'
        call('Oheangbu.EditorTools.WorldMacro.Presentation297', f'shot:{name}:{EYE}:{LOOK}:fov=50:w=1280:h=720:hideplayer')
        im=Image.open(ROOT/'Art/Presentation297/Stills'/f'{name}.png').convert('RGB').crop((240,60,1040,660)).resize((480,360))
        row.append(im)
    rows.append(row)
call('Oheangbu.EditorTools.SpellVFX120.Vfx308Build','swingink:pose:clear')
W=480*4; H=360*len(rows); sheet=Image.new('RGB',(W,H))
for y,row in enumerate(rows):
    for x,im in enumerate(row): sheet.paste(im,(x*480,y*360))
out=ROOT/'Art/SpellVFX308/Review/swingink_r1.png'; sheet.save(out); print(out)
