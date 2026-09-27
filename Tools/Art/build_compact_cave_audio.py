"""Deterministic low-register cave ambience; no external samples or API calls."""
from pathlib import Path
import json, wave
import numpy as np
OUT=Path(__file__).resolve().parents[2]/'Art/Audio/Cave235'
OUT.mkdir(parents=True,exist_ok=True)
SR=24000
rng=np.random.default_rng(235)
def filt(a,hz):
 f=np.fft.rfftfreq(len(a),1/SR);return np.fft.irfft(np.fft.rfft(a)/(1+(f/hz)**8),n=len(a))
def write(name,a):
 a=filt(a,2600);a-=a.mean();a*=.48/max(abs(a).max(),.001)
 # Gentle seam: both ends settle to zero, avoiding a repeated click.
 n=SR*2;a[:n]*=np.linspace(0,1,n);a[-n:]*=np.linspace(1,0,n)
 with wave.open(str(OUT/(name+'.wav')),'wb') as f:
  f.setnchannels(1);f.setsampwidth(2);f.setframerate(SR);f.writeframes((a*32767).astype('<i2').tobytes())
 return {'file':name+'.wav','seconds':len(a)/SR,'peak':float(abs(a).max()),'rms':float(np.sqrt(np.mean(a*a)))}
reports=[]
n=SR*32;t=np.arange(n)/SR;a=filt(rng.normal(size=n),190)*.06
for onset in [2.3,5.9,10.2,16.7,23.6,28.9]:
 q=np.arange(int(SR*2.2))/SR
 drop=(np.sin(2*np.pi*(610*q-60*q*q))*np.exp(-q*10)+.36*np.sin(2*np.pi*310*q)*np.exp(-q*3))*np.minimum(q/.012,1)
 i=int(onset*SR);a[i:i+len(q)]+=drop*.16
 for delay,gain in [(0.23,.25),(.53,.12),(.91,.05)]:
  j=i+int(delay*SR);size=min(len(drop),len(a)-j);a[j:j+size]+=drop[:size]*gain*.16
reports.append(write('deep_water',a))
n=SR*28;t=np.arange(n)/SR;a=filt(rng.normal(size=n),1000);a-=filt(a,85)
a*=.35+.14*np.sin(t*.33)+.09*np.sin(t*.81)
reports.append(write('exit_air',a))
n=SR*39;a=np.zeros(n)
for onset,duration in [(5.4,1.2),(20.1,.7),(33.3,1.5)]:
 q=np.arange(int(SR*duration))/SR;envelope=np.sin(np.pi*q/duration)**2
 scrape=filt(rng.normal(size=len(q)),1100)*envelope*(.4+.2*np.sin(q*33))
 scrape+=np.sin(2*np.pi*125*q)*envelope*.012
 i=int(onset*SR);a[i:i+len(q)]+=scrape
reports.append(write('side_gallery_rub',a))
(OUT/'manifest.json').write_text(json.dumps({'method':'procedural filtered synthesis','sampleRate':SR,'mono':True,'highCutHz':2600,'clips':reports,'listeningApproval':False},indent=2),encoding='utf-8')
print(json.dumps(reports))
