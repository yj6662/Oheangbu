from pathlib import Path
from PIL import Image, ImageDraw
import json, shutil, hashlib
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Art/PlayerPhase1'
src=Path('C:/Users/yj666/Desktop/새 폴더')
names={'Body':'ChatGPT Image 2026년 9월 8일 오후 07_15_17 (3).png','InnerTop':'ChatGPT Image 2026년 9월 8일 오후 07_15_17 (5).png','Durumagi':'ChatGPT Image 2026년 9월 8일 오후 07_15_17 (4).png','OutfitReference':'ChatGPT Image 2026년 9월 8일 오후 07_15_31 (1).png'}
boxes={'Body':[(0,50,608,867),(608,50,766,867),(766,50,1405,867),(1405,50,1672,867)],'InnerTop':[(0,55,630,350),(633,55,788,350),(788,55,1385,350),(1385,55,1672,350)],'Durumagi':[(0,95,579,826),(580,95,785,826),(785,95,1405,826),(1405,95,1672,826)]}
records=[]
for kind,name in names.items():
 p=src/name; dest=OUT/'Source/Original'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
 if kind not in boxes:continue
 im=Image.open(p).convert('RGB')
 for view,box in zip(['front','side_facing_left','back','side_facing_right'],boxes[kind]):
  crop=im.crop(box)
  # Remove adjacent view fragments without changing the intended garment.
  if kind=='Durumagi' and view=='front':ImageDraw.Draw(crop).rectangle((520,280,579,731),fill=(177,177,177))
  if kind=='InnerTop' and view=='front':ImageDraw.Draw(crop).rectangle((620,150,630,295),fill=(177,177,177))
  side=max(crop.size)+40;canvas=Image.new('RGB',(side,side),(177,177,177));canvas.paste(crop,((side-crop.width)//2,(side-crop.height)//2));canvas.thumbnail((1024,1024),Image.Resampling.LANCZOS)
  out=OUT/'Source/Views'/kind/(view+'.png');out.parent.mkdir(parents=True,exist_ok=True);canvas.save(out)
  records.append(dict(part=kind,view=view,source=name,box=box,path=str(out.relative_to(OUT)),sha256=hashlib.sha256(out.read_bytes()).hexdigest(),mirrored=False))
shutil.copy2('C:/Users/yj666/Desktop/PROMPT-Codex-Meshy7-Player-Phase1.md',OUT/'Source/production-instructions.md')
(OUT/'Source/image_manifest.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
print('Prepared',len(records),'undistorted views')
