"""Deterministic runtime preparation, preserves all provider originals."""
import hashlib,json,subprocess,wave,xml.etree.ElementTree as ET
from pathlib import Path
import numpy as np
import imageio_ffmpeg
from PIL import Image
ET.register_namespace('', 'http://www.w3.org/2000/svg')
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/UIAudio/PlaytestFeedback'
UI=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldMacro/Playtest/HUD/Textures'
AUDIO=ROOT/'Oheangbu/Assets/_Project/Audio/PlaytestFeedback'
NODE=Path('C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe')
SHARP='C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/sharp'

def prep_ui():
 UI.mkdir(parents=True,exist_ok=True); entries=[]
 for f in sorted((OUT/'Originals/ui').glob('*.svg')):
  if f.stem=='prompt_paper':
   # Remove only the observed full-artboard background vector; retain paper paths.
   tree=ET.parse(f);r=tree.getroot()
   paths=[e for e in r if e.tag.endswith('path')]
   first=paths[0]
   if first.attrib.get('d','').startswith('M 0 0 L '):r.remove(first)
   source=OUT/(f.stem+'_transparent.svg');tree.write(source,encoding='utf-8',xml_declaration=True)
  else:source=f
  render=OUT/(f.stem+'_render.png')
  code=f"const s=require({json.dumps(SHARP)});s({json.dumps(str(source))}).resize(1536,1536,{{fit:'inside'}}).png().toFile({json.dumps(str(render))});"
  process=subprocess.run([str(NODE),'-e',code],capture_output=True)
  if process.returncode:raise RuntimeError(process.stderr.decode('utf-8','replace')[:1000])
  img=Image.open(render).convert('RGBA');a=np.array(img)
  if f.stem!='prompt_paper':
   # Vector-to-sprite monochrome stencil: white page is transparent; dry-brush gaps stay holes.
   lum=a[:,:,:3].astype(float).mean(axis=2);alpha=np.clip((255-lum)/225*255,0,255).astype(np.uint8)
   a[:,:,:3]=255;a[:,:,3]=alpha
  bbox=Image.fromarray(a[:,:,3]).point(lambda v:255 if v>10 else 0).getbbox()
  img=Image.fromarray(a).crop(bbox)
  if f.stem=='prompt_paper':img=img.transpose(Image.Transpose.ROTATE_90).resize((768,156),Image.Resampling.LANCZOS)
  maxsize=(1024,256) if f.stem=='hp_stroke' else (768,192) if f.stem=='prompt_paper' else (256,512) if f.stem=='ink_bottle' else (256,256)
  img.thumbnail(maxsize,Image.Resampling.LANCZOS)
  framed=Image.new('RGBA',(img.width+8,img.height+8));framed.alpha_composite(img,(4,4))
  dest=UI/(f.stem+'.png');framed.save(dest)
  entries.append({'name':f.stem,'source':str(f.relative_to(ROOT)).replace('\\','/'),'file':str(dest.relative_to(ROOT)).replace('\\','/'),'size':framed.size,'source_crop':bbox,'transparent_pixels':int(np.sum(np.array(framed)[:,:,3]==0)),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()})
 (OUT/'ui_assets.json').write_text(json.dumps(entries,indent=2),encoding='utf-8')
 print(json.dumps(entries),flush=True)

def prep_audio():
 AUDIO.mkdir(parents=True,exist_ok=True);stats=[]
 for f in sorted((OUT/'Originals/sfx').glob('*.mp3')):
  if f.stem=='parry' and (f.parent/'parry_v2.mp3').exists():continue
  canonical='parry' if f.stem=='parry_v2' else f.stem
  p=subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',str(f),'-f','f32le','-ac','1','-ar','44100','pipe:1'],check=True,capture_output=True)
  x=np.frombuffer(p.stdout,dtype='<f4').copy();peak=float(np.max(np.abs(x)));rms=float(np.sqrt(np.mean(x*x)))
  active=np.flatnonzero(np.abs(x)>max(.0005,peak*.025));start=max(0,int(active[0])-220) if len(active) else 0
  # Remove provider lead-in silence only, keep the requested duration's complete tail.
  y=x[start:];gain=min(2.,.70795/max(peak,1e-9));y*=gain
  n=min(132,len(y)//2);y[:n]*=np.linspace(0,1,n);y[-min(441,len(y)):]*=np.linspace(1,0,min(441,len(y)))
  dest=AUDIO/(canonical+'.wav')
  with wave.open(str(dest),'wb') as w:w.setnchannels(1);w.setsampwidth(2);w.setframerate(44100);w.writeframes(np.round(np.clip(y,-1,1)*32767).astype('<i2').tobytes())
  stats.append({'name':canonical,'source':str(f.relative_to(ROOT)).replace('\\','/'),'file':str(dest.relative_to(ROOT)).replace('\\','/'),'duration':len(y)/44100,'sample_rate':44100,'channels':1,'original_peak_dbfs':20*np.log10(max(peak,1e-9)),'rms_dbfs':20*np.log10(max(rms*gain,1e-9)),'peak_dbfs':20*np.log10(max(float(np.max(np.abs(y))),1e-9)),'gain_db':20*np.log10(gain),'leading_trim_seconds':start/44100,'clipped_samples':int(np.sum(np.abs(y)>=1)),'silent':rms<.0001,'sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'listening_review':'USER_REVIEW_PENDING'})
 (OUT/'audio_analysis.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
 print(json.dumps({'clips':len(stats),'seconds':sum(s['duration'] for s in stats),'clipping':sum(s['clipped_samples'] for s in stats),'silent':sum(s['silent'] for s in stats)}),flush=True)

if __name__=='__main__':
 import sys
 if sys.argv[1]=='ui':prep_ui()
 else:prep_audio()
