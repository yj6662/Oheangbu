from pathlib import Path
import json,subprocess,wave,hashlib,html
import numpy as np
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[2];ART=ROOT/'Art/Audio/Compact255';OUT=ROOT/'Oheangbu/Assets/_Project/Audio/Compact255'
OUT.mkdir(parents=True,exist_ok=True);rate=48000;ff=imageio_ffmpeg.get_ffmpeg_exe();rows=[]
for item in json.loads((ART/'inventory.json').read_text()):
 name=item['id'];src=ART/item['group']/'Originals/sfx'/(name+'.mp3')
 ui=item['group']=='ui' or name.startswith('ui_');ambient=item['group']=='ambience'
 cutoff=3000 if ui or ambient else 3800
 filters=f'aformat=channel_layouts=stereo,pan=mono|c0=0.5*c0+0.5*c1,highpass=f=45,equalizer=f=3000:t=q:w=0.7:g=-4,lowpass=f={cutoff}:p=2,lowpass=f={cutoff}:p=2'
 raw=subprocess.check_output([ff,'-v','error','-i',str(src),'-af',filters,'-ar',str(rate),'-f','f32le','-'])
 x=np.frombuffer(raw,'<f4').astype(float);x-=x.mean();peak=max(abs(x));assert peak>1e-5,name
 if item['loop']:
  n=min(int(.16*rate),len(x)//6);w=np.linspace(0,1,n);x[-n:]=x[-n:]*(1-w)+x[:n]*w;x=x[n:]
 else:
  active=np.flatnonzero(abs(x)>peak*.006);x=x[max(0,active[0]-int(.012*rate)):min(len(x),active[-1]+int(.07*rate))]
  n=min(int(.016*rate),len(x)//8);x[:n]*=np.linspace(0,1,n);x[-n:]*=np.linspace(1,0,n)
 target=-31 if ui or ambient else -28 if name.startswith('step_') or name=='brush_stroke' else -25
 x*=min(10**(target/20)/(np.sqrt(np.mean(x*x))+1e-9),10**(-10/20)/max(abs(x)))
 dest=OUT/(name+'.wav')
 with wave.open(str(dest),'wb') as f:f.setnchannels(1);f.setsampwidth(2);f.setframerate(rate);f.writeframes(np.rint(x*32767).astype('<i2').tobytes())
 power=abs(np.fft.rfft(x))**2;freq=np.fft.rfftfreq(len(x),1/rate)
 rows.append(dict(id=name,group=item['group'],loop=item['loop'],seconds=len(x)/rate,peak_db=20*np.log10(max(abs(x))),rms_db=20*np.log10(np.sqrt(np.mean(x*x))),above4khz_percent=float(100*power[freq>4000].sum()/max(1e-20,power.sum())),loop_seam=float(abs(x[0]-x[-1])) if item['loop'] else None,sha256=hashlib.sha256(dest.read_bytes()).hexdigest()))
assert len(rows)==69 and all(r['peak_db']<=-9.99 for r in rows)
(ART/'analysis.json').write_text(json.dumps(rows,indent=2))
body=''.join('<section><p>'+html.escape(r['group']+' · '+r['id'])+'</p><audio controls preload="none" src="../../../Oheangbu/Assets/_Project/Audio/Compact255/'+r['id']+'.wav"></audio></section>' for r in rows)
(ART/'REVIEW.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><title>축소맵 효과음 검토</title><style>body{max-width:900px;margin:40px auto;background:#e9e5db;color:#302f2a;font:16px/1.6 system-ui}section{display:inline-block;width:44%;margin:0 2% 16px;border-bottom:1px solid #bcb9b0}audio{width:100%}h1{font-size:26px}</style><h1>축소맵 효과음 69종</h1><p>새 생성 원본에서 날카로운 고역과 피크를 줄인 제작본입니다. 아래 재생은 개별 음원 청음이며, 게임 내 연결·음량 검증과 구분합니다.</p>'+body+'</html>',encoding='utf-8')
print(json.dumps(dict(count=len(rows),seconds=sum(r['seconds'] for r in rows),max_peak_db=max(r['peak_db'] for r in rows))))
