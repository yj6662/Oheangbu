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
def write(name,a,fade=2.0):
 a=filt(a,2600);a-=a.mean();a*=.48/max(abs(a).max(),.001)
 # Gentle seam: both ends settle to zero, avoiding a repeated click.
 n=int(SR*fade);a[:n]*=np.linspace(0,1,n);a[-n:]*=np.linspace(1,0,n)
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
# --- #306 working-mine layer (PLAN §2-7 item 12; own seed so the clips above stay byte-identical) ---
r6=np.random.default_rng(306)
def ring(f,decay,dur):
 q=np.arange(int(SR*dur))/SR;return np.sin(2*np.pi*f*q)*np.exp(-q*decay)
def place(a,ev,at,gain):
 i=int(at*SR);size=min(len(ev),len(a)-i);a[i:i+size]+=ev[:size]*gain
def echo(a,taps=((.19,.3),(.41,.14),(.77,.06))):
 out=a.copy()
 for d,g in taps:j=int(d*SR);out[j:]+=a[:-j]*g
 return out
# 동발 삐걱: stick-slip creaks (pulse trains through wood resonances), sparse, over a faint room tone
n=SR*34;a=filt(r6.normal(size=n),160)*.02
body=sum(ring(f,d,.09)*g for f,d,g in ((210,38,1),(470,55,.5),(930,80,.25)))
for onset in (3.1,11.8,19.4,27.6):
 dur=r6.uniform(.7,1.5);t=0;rate0=r6.uniform(28,45)
 while t<dur:
  rate=rate0*(1+.6*np.sin(np.pi*t/dur));place(a,body,onset+t,np.sin(np.pi*t/dur)**1.5*.5);t+=1/rate*r6.uniform(.7,1.3)
reports.append(write('timber_creak',echo(a)))
# 잔모래 흘러내림: grain bursts after the blast, with the odd pebble
n=SR*26;a=np.zeros(n)
for onset,dur in ((1.5,2.6),(9.2,1.4),(15.8,3.1),(22.0,1.1)):
 m=int(dur*SR);env=np.sin(np.pi*np.arange(m)/m)**2;g=(r6.random(m)<.012)*r6.normal(size=m)
 g=filt(g,2400)-filt(g,700);place(a,g*env,onset,.9)
 for k in range(int(r6.integers(1,4))):place(a,ring(r6.uniform(900,1500),45,.12),onset+r6.uniform(0,dur),.35)
reports.append(write('sand_trickle',echo(a,((.13,.25),(.29,.1)))))
# 두레박 물방울: one drop into the sump (intermittent source plays it every 4-9 s)
q=np.arange(int(SR*1.6))/SR;drop=np.sin(2*np.pi*(820*q-190*q*q))*np.exp(-q*14)*np.minimum(q/.004,1)+.3*ring(410,6,1.6)
reports.append(write('bucket_drip',echo(np.concatenate([np.zeros(int(SR*.08)),drop]),((.21,.3),(.47,.13),(.83,.05))),fade=.05))
# 먼 두드림: distant pick/hammer knocks in groups, heavily low-passed, long tails
n=SR*42;a=filt(r6.normal(size=n),120)*.015
knock=ring(150,30,.3)*.6;knock[:int(SR*.08)]+=filt(r6.normal(size=int(SR*.08))*np.exp(-np.arange(int(SR*.08))/SR*60),420)
for onset in (4.0,17.5,31.0):
 for k in range(int(r6.integers(3,6))):place(a,knock,onset+k*r6.uniform(.55,.8),r6.uniform(.6,1))
reports.append(write('distant_knock',echo(filt(a,500),((.35,.4),(.8,.2),(1.4,.1)))))
# 도르래 삐걱: windlass pulley squeaks swaying in the wind
n=SR*22;a=filt(r6.normal(size=n),900)*.01;t=np.arange(n)/SR
for onset in (2.0,6.4,12.1,17.3):
 m=int(SR*r6.uniform(.35,.6));q=np.arange(m)/SR;f=r6.uniform(620,880)*(1+.25*np.sin(np.pi*q/q[-1]))
 place(a,np.sin(2*np.pi*np.cumsum(f)/SR)*np.sin(np.pi*q/q[-1])**2*(1+.5*np.sign(np.sin(2*np.pi*31*q))),onset,.5)
reports.append(write('pulley_creak',a))
(OUT/'manifest.json').write_text(json.dumps({'method':'procedural filtered synthesis','sampleRate':SR,'mono':True,'highCutHz':2600,'clips':reports,'listeningApproval':False},indent=2),encoding='utf-8')
print(json.dumps(reports))
