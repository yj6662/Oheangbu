"""Reproducible editorial derivatives of owned ElevenLabs recordings; no API calls.
Originals and the prior Unity WAV assets are read only. Requires numpy and imageio_ffmpeg.
"""
from pathlib import Path
import subprocess, json, hashlib, wave, math, html
import numpy as np
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[3]
ART=ROOT/'Art/PlaytestPolish/AudioInk'
SRC=ROOT/'Art/UIAudio/PlaytestFeedback/Originals/sfx'
OLD=ROOT/'Oheangbu/Assets/_Project/Audio/PlaytestFeedback'
OUT=ROOT/'Oheangbu/Assets/_Project/Audio/PlaytestPolish/AudioInk'
FF=imageio_ffmpeg.get_ffmpeg_exe(); RATE=44100
OUT.mkdir(parents=True,exist_ok=True); ART.mkdir(parents=True,exist_ok=True)
# role: source-rate multiplier, lowpass Hz, RMS target dBFS, peak ceiling dBFS
ROLES={
 'brush_stroke':(.84,3200,-29,-12),'cast_wood':(1,6000,-24,-7),
 'cast_fire':(1,5800,-25,-8),'cast_earth':(1,4200,-24,-7),
 'cast_metal':(.50,4200,-25,-8),'cast_water':(1,6200,-26,-8),
 'impact':(1,6200,-24,-8),'player_hit':(1,5200,-23,-7),
 'parry':(.91,5900,-25,-8),'harvest':(.9,3800,-28,-10),
 'interact':(.8,3200,-28,-11),'rest':(1,5800,-27,-10),
 'summon_appear':(.72,4800,-26,-9),'summon_release':(.9,5000,-27,-10)}
def decode(path,filters=''):
 if path.suffix.lower()=='.wav' and not filters:
  with wave.open(str(path),'rb') as w:
   assert w.getframerate()==RATE and w.getsampwidth()==2
   x=np.frombuffer(w.readframes(w.getnframes()),'<i2').astype(np.float32)/32768
   return x.reshape(-1,w.getnchannels()).mean(axis=1)
 cmd=[FF,'-v','error','-i',str(path),'-vn']
 # Average stereo for world playback, explicitly avoiding FFmpeg's +3dB mono sum.
 cmd+=['-af','aformat=channel_layouts=stereo,pan=mono|c0=0.5*c0+0.5*c1'+(','+filters if filters else ''),'-ar',str(RATE),'-f','f32le','-']
 return np.frombuffer(subprocess.check_output(cmd),'<f4').copy()
def level(x,rms,peak,fade=.018):
 x=np.asarray(x,dtype=np.float64);x-=x.mean()
 active=x[np.abs(x)>max(1e-5,np.max(np.abs(x))*.03)]
 gain=min(10**(rms/20)/(np.sqrt(np.mean(active**2))+1e-9),10**(peak/20)/(np.max(np.abs(x))+1e-9))
 x*=gain;n=min(int(RATE*fade),len(x)//4)
 x[:n]*=np.sin(np.linspace(0,np.pi/2,n))**2;x[-n:]*=np.cos(np.linspace(0,np.pi/2,n))**2
 return x.astype(np.float32)
def trim(x):
 active=np.flatnonzero(np.abs(x)>max(1e-5,np.max(np.abs(x))*.003))
 if not len(active):return x
 return x[max(0,active[0]-int(.009*RATE)):min(len(x),active[-1]+int(.025*RATE))]
def write(name,x):
 with wave.open(str(OUT/(name+'.wav')),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(np.rint(np.clip(x,-.999,.999)*32767).astype('<i2').tobytes())
def stats(x):
 f=np.fft.rfft(x);power=np.abs(f)**2;freq=np.fft.rfftfreq(len(x),1/RATE)
 return {'seconds':len(x)/RATE,'peak_dbfs':float(20*np.log10(max(1e-9,np.max(np.abs(x))))),'rms_dbfs':float(20*np.log10(max(1e-9,np.sqrt(np.mean(x*x))))),'above_6khz_percent':float(100*power[freq>6000].sum()/max(1e-12,power.sum())),'clipped_samples':int(np.sum(np.abs(x)>=1)),'first':float(x[0]),'last':float(x[-1])}
report={'scope':'Offline PCM/source analysis; Unity output/device audition remains unverified','sample_rate':RATE,'channels':1,'clips':{}}
processed={}
for name,(rate,cut,rms,peak) in ROLES.items():
 source=SRC/((name if name!='parry' else 'parry_v2')+'.mp3')
 if not source.exists():source=SRC/(name+'.mp3')
 filters=f'asetrate={RATE*rate},aresample={RATE},highpass=f=65,lowpass=f={cut}:p=2,lowpass=f={cut}:p=2'
 x=level(trim(decode(source,filters)),rms,peak);write(name,x);processed[name]=x
 report['clips'][name]={'source':str(source.relative_to(ROOT)),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'rate_multiplier':rate,'lowpass_hz':cut,'before':stats(decode(OLD/(name+'.wav'))),'after':stats(x)}
# Continuous extraction uses a seam-overlapped quiet interior, never the terminal pluck.
base=processed['harvest'];start=int(len(base)*.18);end=int(len(base)*.76);loop=base[start:end].copy()
n=min(int(.09*RATE),len(loop)//4);blend=np.linspace(0,1,n)
seam=loop[-n:]*(1-blend)+loop[:n]*blend
loop=np.concatenate([seam,loop[n:-n]])
# DC/level only: a looping file must not fade to silence at every wrap.
loop-=loop.mean();loop*=min(10**(-30/20)/(np.sqrt(np.mean(loop**2))+1e-9),10**(-13/20)/(np.max(np.abs(loop))+1e-9))
variants={'harvest_loop':loop,'harvest_start':level(base[:int(.26*RATE)],-28,-12,.025),
 'harvest_end':level(base[-int(.3*RATE):],-31,-13,.04),
 'ui_paper':level(processed['brush_stroke'][:int(.4*RATE)],-31,-14,.025),
 'ui_confirm':level(processed['interact'][:int(.27*RATE)],-29,-12,.018),
 'ui_back':level(processed['brush_stroke'][:int(.28*RATE)][::-1],-30,-13,.025)}
for name,x in variants.items():write(name,x);report['clips'][name]={'after':stats(x),'derived_from':'owned processed brush/interact/harvest; no synthesis provider'}
# Compare the old legal impact burst with new three-voice cap and fixed mixer headroom.
def composite(x,interval,cap,gain):
 out=np.zeros(RATE*4);ends=[];accepted=0
 for t in np.arange(0,2.2,interval):
  ends=[e for e in ends if e>t]
  if len(ends)>=cap:continue
  i=int(t*RATE);n=min(len(x),len(out)-i);out[i:i+n]+=x[:n]*gain;ends.append(t+len(x)/RATE);accepted+=1
 return {'accepted_voices':accepted,**stats(out)}
report['impact_stress']={'before':composite(decode(OLD/'impact.wav'),.055,11,.72),'after':composite(processed['impact'],.085,3,.65*10**(-6/20)),'caveat':'Offline worst-position additive approximation, not an actual Unity output measurement.'}
report['loop_seam_delta']=float(abs(loop[-1]-loop[0]))
(ART/'audio_analysis.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
rows=[]
for name,v in report['clips'].items():
 before=OLD/(name+'.wav'); after=OUT/(name+'.wav')
 oldaudio=f'<audio controls preload="none" src="{before.as_uri()}"></audio>' if before.exists() else '단계별 새 편집본'
 rows.append(f'<article><h2>{html.escape(name)}</h2><div>기존 {oldaudio}</div><div>수정 <audio controls preload="none" src="{after.as_uri()}"></audio></div><p>Peak {v["after"]["peak_dbfs"]:.1f} dBFS · 6 kHz 이상 {v["after"]["above_6khz_percent"]:.2f}%</p></article>')
(ART/'REVIEW.html').write_text('<!doctype html><meta charset="utf-8"><title>효과음·먹 수급 검토</title><style>body{background:#e9e2d4;color:#302a24;font:18px sans-serif;max-width:1100px;margin:50px auto}article{display:inline-block;width:48%;vertical-align:top;border-top:1px solid #837763;padding:10px 0}audio{width:330px}h1{font-size:30px}</style><h1>효과음과 먹 수급 개선</h1><p>원본을 보존한 편집 파생본입니다. 재생은 직접 선택하며 자동 재생하지 않습니다. 실제 Unity 출력·청취·게임 화면은 별도 검증 대상입니다.</p>'+''.join(rows),encoding='utf8')
print(json.dumps({'output':str(OUT),'clips':len(report['clips']),'impact':report['impact_stress']},indent=2))
