from pathlib import Path
import json,subprocess,wave,hashlib,html
import numpy as np
import imageio_ffmpeg
root=Path(__file__).resolve().parents[2];art=root/'Art/Audio/JourneyRenewal';out=root/'Oheangbu/Assets/_Project/Audio/JourneyRenewal';out.mkdir(parents=True,exist_ok=True)
ff=imageio_ffmpeg.get_ffmpeg_exe();rate=48000;rows=[]
cutoffs={'brush_stroke':3200,'cast_wood':4200,'cast_fire':3800,'cast_earth':3200,'cast_metal':4600,'cast_water':4200,'impact':4000,'player_hit':3200,'parry':4800,'harvest':3400,'harvest_loop':3000,'harvest_start':3400,'harvest_end':3000,'interact':3200,'rest':3200,'summon_appear':3800,'summon_release':3400,'ui_paper':2800,'ui_confirm':3200,'ui_back':2800}
def spectrum(x):
 power=np.abs(np.fft.rfft(x))**2;hz=np.fft.rfftfreq(len(x),1/rate)
 return float(100*power[hz>4000].sum()/max(1e-20,power.sum()))

for p in sorted((art/'Originals/sfx').glob('*.mp3')):
 cutoff=cutoffs[p.stem]
 filters=f'aformat=channel_layouts=stereo,pan=mono|c0=0.5*c0+0.5*c1,highpass=f=40,equalizer=f=3000:t=q:w=0.7:g=-4.5,lowpass=f={cutoff}:p=2,lowpass=f={cutoff}:p=2'
 raw=subprocess.check_output([ff,'-v','error','-i',str(p),'-af',filters,'-ar',str(rate),'-f','f32le','-'])
 x=np.frombuffer(raw,'<f4').astype(float);x-=x.mean();peak=np.max(np.abs(x));assert peak>1e-5,p
 if p.stem=='harvest_loop':
  n=int(.12*rate);w=np.linspace(0,1,n);x[-n:]=x[-n:]*(1-w)+x[:n]*w;x=x[n:]
 else:
  active=np.flatnonzero(np.abs(x)>peak*.008);x=x[max(0,active[0]-int(.006*rate)):min(len(x),active[-1]+int(.045*rate))]
  n=min(int(.018*rate),len(x)//8);x[:n]*=np.linspace(0,1,n);x[-n:]*=np.linspace(1,0,n)
 target=-30 if p.stem.startswith('ui_') or p.stem in ['brush_stroke','harvest_loop'] else -25
 x*=min(10**(target/20)/(np.sqrt(np.mean(x*x))+1e-9),10**(-10/20)/np.max(np.abs(x)))
 dest=out/(p.stem+'.wav')
 with wave.open(str(dest),'wb') as f:f.setnchannels(1);f.setsampwidth(2);f.setframerate(rate);f.writeframes(np.rint(x*32767).astype('<i2').tobytes())
 rows.append({'name':p.stem,'lowpass_hz':cutoff,'above_4khz_percent':spectrum(x),'seconds':len(x)/rate,'peak':float(np.max(np.abs(x))),'rms':float(np.sqrt(np.mean(x*x))),'clipped':int(np.sum(np.abs(x)>=1)),'sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
assert len(rows)==20 and all(r['clipped']==0 for r in rows)
(art/'analysis.json').write_text(json.dumps(rows,indent=2))
labels=['붓질','토 시전','화 시전','금 시전','수 시전','목 시전','갈무리','갈무리 종료','갈무리 지속','갈무리 시작','명중','상호작용','패링','피격','휴식','소환 등장','소환 해제','UI 돌아가기','UI 확인','종이 넘김']
body=''.join('<section><h2>'+label+'</h2><audio controls preload="none" src="../../../Oheangbu/Assets/_Project/Audio/JourneyRenewal/'+r['name']+'.wav"></audio><details><summary>수정 전 비교</summary><audio controls preload="none" src="BeforeSoftening/'+r['name']+'.wav"></audio></details></section>' for label,r in zip(labels,rows))
(art/'REVIEW.html').write_text('<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>효과음 · 고역 완화</title><style>body{max-width:760px;margin:40px auto;padding:0 24px;background:#e7e4dc;color:#292a27;font:16px/1.6 system-ui}h1{font-size:26px}h2{font-size:18px;font-weight:500}section{padding:16px 0;border-bottom:1px solid #cbc8bf}audio{width:100%}summary{cursor:pointer;font-size:14px;color:#62635e;margin:8px 0}</style><h1>효과음 · 고역 완화</h1><p>날카로운 고역과 순간 피크를 낮추고 시작과 끝을 부드럽게 다듬었습니다. 위 플레이어가 수정본입니다.</p>'+body+'</html>',encoding='utf-8')
print(json.dumps({'count':len(rows),'seconds':sum(r['seconds'] for r in rows),'clipped':sum(r['clipped'] for r in rows)}))
