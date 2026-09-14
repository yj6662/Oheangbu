"""Read-only audit of exported PCM, including oversampled peaks and preserved sources."""
from pathlib import Path
import hashlib,json,wave,subprocess
import numpy as np
import imageio_ffmpeg
ROOT=Path(__file__).resolve().parents[3];ART=ROOT/'Art/PlaytestPolish/AudioInk';FOLDER=ROOT/'Oheangbu/Assets/_Project/Audio/PlaytestPolish/AudioInk'
report={'scope':'Offline exported PCM verification. No Unity playback/device analysis.','clips':[],'failures':[]}
for p in sorted(FOLDER.glob('*.wav')):
 with wave.open(str(p),'rb') as w:
  channels,rate,bits,n=w.getnchannels(),w.getframerate(),w.getsampwidth()*8,w.getnframes();x=np.frombuffer(w.readframes(n),'<i2').astype(np.float32)/32768
 peak=np.max(np.abs(x));up=np.frombuffer(subprocess.check_output([imageio_ffmpeg.get_ffmpeg_exe(),'-v','error','-i',str(p),'-ar','176400','-f','f32le','-']),'<f4')
 measured={'name':p.stem,'channels':channels,'sampleRate':rate,'bits':bits,'seconds':n/rate,'sample_peak_dbfs':float(20*np.log10(max(1e-9,peak))),'oversampled_4x_peak_dbfs':float(20*np.log10(max(1e-9,np.max(np.abs(up))))),'clipped_samples':int(np.sum(np.abs(x)>=1)),'seam_delta':float(abs(x[-1]-x[0])),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
 if channels!=1 or rate!=44100 or bits!=16 or np.max(np.abs(up))>=1:report['failures'].append(p.stem+': format or over-0dBFS')
 if p.stem!='harvest_loop' and (x[0]!=0 or x[-1]!=0):report['failures'].append(p.stem+': nonzero cut endpoint')
 if p.stem=='harvest_loop' and abs(x[-1]-x[0])>.001:report['failures'].append(p.stem+': loop seam exceeds .001 full scale')
 report['clips'].append(measured)
manifest=json.loads((ART/'before_manifest.json').read_text());checked=0
for item in manifest['files']:
 if '/Originals/' not in item['path'] and not item['path'].startswith('Oheangbu/Assets/_Project/Audio/PlaytestFeedback/'):continue
 original=ROOT/item['path']
 if hashlib.sha256(original.read_bytes()).hexdigest()!=item['sourceSha256']:report['failures'].append('Preserved original changed: '+item['path'])
 checked+=1
report['preserved_source_files_verified']=checked;report['status']='PASS' if not report['failures'] else 'FAIL'
(ART/'pcm_verification.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({'status':report['status'],'clips':len(report['clips']),'preserved':checked,'failures':report['failures']},indent=2))
