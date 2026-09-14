"""Explicit bounded provider requests. Secrets stay in env; never auto-retry POSTs."""
import argparse, base64, hashlib, json, os, time, urllib.request, urllib.error
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/UIAudio/PlaytestFeedback'
OUT.mkdir(parents=True,exist_ok=True)

UI={
 'hp_stroke':'One single long horizontal Korean sumi ink brush stroke, dry bristle grain inside a mostly solid dense ink silhouette, organically tapered ends, very restrained and nearly straight. Wide thin horizontal shape for a game health bar. Pure black ink on a plain pure white background. No text, no characters, no symbols, no border, no shadows, no gradients. Single flat vector asset with generous blank margins. Aspect of the stroke 12 to 1.',
 'ink_bottle':'Single front elevation outline of a small traditional Korean portable ink bottle, squat rounded rectangular body, narrow short neck and a simple wooden stopper. Very thin hand-brushed charcoal contour, perfectly empty white interior so a game can display a liquid level inside it. Two quiet off-white glass edge highlights only. Flat black and white vector game HUD icon, plain white background. No lettering, no decorations, no ink inside, no perspective, no shadow, no scene. Tall compact icon.',
 'lock_ring':'A single open circular Korean calligraphy brush stroke for an action game lock-on reticle. One asymmetric dry-brush ink circle, thin with slight pressure variation and a subtle small gap at top right. Completely empty center. Pure black brush line on pure white background, restrained flat vector icon, no crosshair, no lettering, no embellishment, no shadow. Generous margins.',
 'prompt_paper':'One long narrow blank strip of handmade Korean hanji paper seen directly from above, gently irregular torn edges, creamy white face and just a few fine pale gray fibers near edges. Quiet elegant empty paper for a small game interaction caption. Extremely simple flat vector asset on pure white background. No text, no border frame, no ornament, no shadow, no perspective, no curled scroll ends. Width to height five to one.'
}
SFX={
 'brush_stroke':(.8,'A single soft close dry calligraphy brush swipe across fibrous Korean hanji paper, tiny bristle scrape and muted wet ink contact. Intimate tactile foley, quick attack and short release, isolated clean recording. No voice, music, ringing, background ambience or cinematic whoosh.'),
 'cast_wood':(1.1,'Short magical wooden talisman release: a tight bamboo flex and dry wooden click with a single low plucked geomungo-like string resonance, light leaf rush. Restrained Korean ink fantasy game sound, immediate attack, dry close finish. No speech, music phrase, electronic zap or large explosion.'),
 'cast_fire':(1.1,'Short talisman ignition, paper flick into a small fast real flame whoosh and dry crackle. Restrained close game casting cue with a faint low plucked wooden string tail, immediate attack. No music, voices, bomb, bass drop or long ambience.'),
 'cast_earth':(1.2,'Short earth talisman release, stone tapping stone with a compact low earthen thump and grit falling, subtle muted plucked wooden string resonance. Dry tactile Korean ink fantasy casting cue. No voice, music phrase, huge explosion or electronic sounds.'),
 'cast_metal':(1.1,'Short metal talisman release, a delicate small bronze ritual chime struck once and a fine sharpened metal swish, fast attack and restrained decaying ring. Clear close game cue, no voice, melody, repeated bell, sci-fi laser or huge impact.'),
 'cast_water':(1.2,'Short water talisman release, a compact soft water surge and one deep ceramic ink bowl droplet with a subtle muted string resonance. Immediate clear attack, quiet fluid tail. Dry isolated Korean ink fantasy game cue. No speech, music, ocean ambience or electronic zap.'),
 'impact':(.75,'One short solid combat impact into thick cloth and wood, crisp dry crack then muted body thud with a little granular debris. Clear quick tactile game hit feedback, no voice, blood, music, cinematic boom or long tail.'),
 'player_hit':(.7,'One restrained close hit against heavy woven clothing, low padded thump with a brief cloth scrape, immediate attack. Physical game player damage cue. No scream, speech, heartbeat, music or exaggerated bass.'),
 'parry':(.9,'One precise successful deflection, bamboo clap layered with a short bright bronze struck tone and brief dry air flick. Korean ritual material character, satisfying crisp immediate attack then quick decay. No music, voice, repeated impacts, sci-fi or explosion.'),
 'parry_v2':(1.5,'A loud clear single strike of a small bronze hand gong, immediately at the start. Sharp wooden clack layered with a resonant metallic ping, ringing decays quickly into silence. Dry close recorded sound effect for blocking a weapon. One impact only, no speech, no music, no background noise.'),
 'harvest':(1.4,'Ink gathering into a small ceramic bottle, a brief inward liquid trickle ending with a quiet rounded droplet and one muted low plucked wooden string. Tactile close fantasy recovery cue, no music phrase, voice, electronic sparkle or long ambience.'),
 'interact':(.55,'One very quiet short handmade paper touch and small wooden token tap, soft dry tactile interaction confirmation, clean isolated close recording. No voice, melody, chime, electronic click or ambience.'),
 'rest':(1.8,'A calm single low geomungo-like plucked wooden string, warm woody resonance with a faint soft cloth settling sound, brief peaceful resting confirmation. Dry restrained Korean timbre, one gesture only, no melody, music bed, voice or electronic sound.'),
 'summon_appear':(1.8,'A restrained ritual spirit materializing: dry hanji paper sweep, low wooden string pluck, a compact rising breath of air and small stone grains settling. Clear immediate start, Korean ink fantasy texture, no voices, melody, monster roar, cinematic boom or electronic zap.'),
 'summon_release':(1.3,'A quiet spirit dissolving, soft dry leaves and hanji fibers dispersing in a short airy outward sweep, one tiny wooden click ending. Restrained tactile close game cue, no voices, music, electronic sparkle or huge whoosh.')
}

def request(url,key,header,body=None):
 value=('Bearer '+key) if header=='Authorization' else key
 req=urllib.request.Request(url,data=None if body is None else json.dumps(body).encode(),headers={header:value,'Content-Type':'application/json'})
 with urllib.request.urlopen(req,timeout=180) as r:
  return r.read(),{k:v for k,v in r.headers.items() if k.lower() in ['request-id','x-request-id','history-item-id','character-cost','x-character-cost','content-type']}

def run(kind,name):
 ledger=OUT/'requests'/f'{kind}_{name}.json';ledger.parent.mkdir(exist_ok=True)
 if ledger.exists():
  print(json.dumps({'name':name,'status':'ALREADY_ATTEMPTED','ledger':str(ledger)}));return
 prior=[json.loads(p.read_text(encoding='utf-8')) for p in ledger.parent.glob(f'{kind}_*.json')]
 if kind=='ui' and len(prior)>=5:raise RuntimeError('Recraft five-request cap reached')
 if kind=='sfx' and sum(e['request'].get('duration_seconds',0) for e in prior)+SFX[name][0]>30:raise RuntimeError('SFX 30-second cap reached')
 if kind=='ui':
  model='recraftv4_vector'; prompt=UI[name]
  body={'model':model,'prompt':prompt,'n':1,'size':'2:1' if name in ['hp_stroke','prompt_paper'] else '1:1','response_format':'b64_json'}
  url='https://external.api.recraft.ai/v1/images/generations'; key=os.environ['RECRAFT_API_KEY']; header='Authorization'; estimate=80
 else:
  model='eleven_text_to_sound_v2';duration,prompt=SFX[name]
  body={'model_id':model,'text':prompt,'duration_seconds':duration,'prompt_influence':.6,'loop':False}
  url='https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128'; key=os.environ['ELEVENLABS_API_KEY'];header='xi-api-key';estimate=duration*40
 entry={'provider':'Recraft' if kind=='ui' else 'ElevenLabs','name':name,'utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'request':body,'estimated_units':estimate,'status':'REQUEST_STARTED','actual_usage':None}
 ledger.write_text(json.dumps(entry,ensure_ascii=False,indent=2),encoding='utf-8')
 try:
  raw,headers=request(url,key,header,body)
  entry['response_headers']=headers
  if kind=='ui':
   data=json.loads(raw); item=data['data'][0]; entry['actual_usage']=data.get('credits'); entry['response_metadata']={k:v for k,v in data.items() if k!='data'}
   if 'b64_json' in item:raw=base64.b64decode(item['b64_json'])
   else:
    download=item['url']; entry['download_host']=urllib.parse.urlparse(download).hostname
    # Provider-issued asset URL, no auth forwarded.
    with urllib.request.urlopen(download,timeout=60) as r:raw=r.read()
   ext='.svg' if b'<svg' in raw[:1000] else '.png'
  else:
   ext='.mp3'
   entry['actual_usage']=next((float(v) for k,v in headers.items() if k.lower() in ['character-cost','x-character-cost']),None)
  dest=OUT/'Originals'/kind/(name+ext);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(raw)
  entry.update(status='SUCCEEDED',file=str(dest.relative_to(ROOT)).replace('\\','/'),bytes=len(raw),sha256=hashlib.sha256(raw).hexdigest())
 except urllib.error.HTTPError as e:
  detail=e.read(1600).decode('utf-8','replace');entry.update(status='HTTP_ERROR',http_status=e.code,error=detail)
 except Exception as e:entry.update(status='UNKNOWN_RESULT_NO_AUTO_RETRY',error=type(e).__name__)
 ledger.write_text(json.dumps(entry,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({k:v for k,v in entry.items() if k not in ['request','response_metadata']},ensure_ascii=False),flush=True)

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('kind',choices=['ui','sfx']);p.add_argument('name');a=p.parse_args()
 run(a.kind,a.name)
