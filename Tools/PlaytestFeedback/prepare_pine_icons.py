"""Rasterize provider vectors to white transparent masks without changing their shapes."""
import json,subprocess
from pathlib import Path
import numpy as np
from PIL import Image
from prepare_assets import NODE,SHARP,ROOT
out=ROOT/'Art/World/PineRest/MenuIcons'
dest=ROOT/'Oheangbu/Assets/_Project/Art/World/PineRestGame/MenuIcons'
dest.mkdir(parents=True,exist_ok=True)
board=Image.new('RGBA',(600,200),(36,39,38,255))
for i,name in enumerate(['resume','save','exit']):
 source=out/'Originals/ui'/(name+'.svg');render=out/(name+'_render.png')
 code=f"require({json.dumps(SHARP)})({json.dumps(str(source))}).resize(768,768).png().toFile({json.dumps(str(render))});"
 subprocess.run([str(NODE),'-e',code],check=True)
 a=np.array(Image.open(render).convert('RGBA'));alpha=((255-a[:,:,:3].astype(float).mean(axis=2))*(a[:,:,3]/255)).astype('uint8')
 mask=Image.fromarray(alpha);bbox=mask.point(lambda x:255 if x>16 else 0).getbbox();mask=mask.crop(bbox);mask.thumbnail((208,208),Image.Resampling.LANCZOS)
 icon=Image.new('RGBA',(256,256),(255,255,255,0));fg=Image.new('RGBA',mask.size,(255,255,255,255));fg.putalpha(mask);icon.alpha_composite(fg,((256-mask.width)//2,(256-mask.height)//2));icon.save(dest/(name+'.png'))
 preview=icon.resize((144,144),Image.Resampling.LANCZOS);board.alpha_composite(preview,(i*200+28,28))
board.save(out/'preview.png')
print('Prepared three 256px transparent icons')
