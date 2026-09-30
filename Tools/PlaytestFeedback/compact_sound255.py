"""Generate the audited compact-map cue inventory, once per role; retain provider originals."""
from pathlib import Path
import sys,os,json,importlib.util
from concurrent.futures import ThreadPoolExecutor
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/Audio/Compact255'
spec=importlib.util.spec_from_file_location('gen',ROOT/'Tools/PlaytestFeedback/generate_assets.py')
gen=importlib.util.module_from_spec(spec);spec.loader.exec_module(gen)
# Entries are playback roles, not one sound for every button or every spell variant.
GROUPS={
'core':{
'brush_stroke':(.8,'One soft ink brush stroke over fibrous paper, damp bristle contact, delicate friction.'),
'cast_wood':(1.1,'A thick green branch flexing and springing forward, muted woody creak and leaves.'),
'cast_fire':(1.1,'A compact flame igniting with a rounded soft air push and restrained embers.'),
'cast_earth':(1.2,'A heavy rock shifting into packed soil, low blunt knock and small grains settling.'),
'cast_metal':(1.1,'A heavy iron edge sweeping through air, muted brushed metal friction, damped short contact, no ringing.'),
'cast_water':(1.1,'A compact weighty surge of water, rounded wet push and short swirl.'),
'impact':(.7,'One padded staff impact into dense wood and heavy cloth, low dry body knock.'),
'player_hit':(.7,'A blunt impact absorbed by heavy layered clothing, soft compressed thump.'),
'parry':(.9,'One controlled iron blade deflection, warm damped metal clack, no high ping or ring.'),
'harvest':(1.2,'Thick ink drawn into a ceramic vessel, rounded low suction and liquid gulp.'),
'harvest_start':(.6,'A small ceramic stopper pulled free, soft friction pop.'),
'harvest_loop':(2.2,'Continuous gentle thick liquid flowing inside a narrow ceramic channel, even texture, seamless loop.'),
'harvest_end':(.6,'A thick ink stream softly stopping, small damp ceramic touch.'),
'interact':(.6,'A wood tally lightly pressed onto coarse paper, soft dry tap.'),
'rest':(1.5,'Heavy travelling clothes gently settling, relaxed soft fabric fold and calm low wood resonance.'),
'summon_appear':(1.5,'Dense paper fibres unfolding into a solid shape, soft low air rush and padded material contact.'),
'summon_release':(1.2,'Coarse paper fibres softly separating and dispersing into air.'),
'ui_paper':(.6,'One thick handmade paper sheet slid over smooth timber, very quiet soft friction.'),
'ui_confirm':(.5,'A small hardwood token seated into a wooden recess, soft rounded click.'),
'ui_back':(.5,'A small wooden token gently lifted and set aside, soft low touch.')},
'ui':{
'ui_focus':(.5,'Barely audible brush of a fingertip over coarse paper, one tiny soft touch.'),
'ui_select':(.5,'One light padded wooden counter tap, warm close contact.'),
'ui_error':(.6,'A wooden counter meeting a blocked slot, muted hollow double contact, understated refusal.'),
'ui_drag':(.6,'A small cloth pouch gently lifted off paper, soft friction.'),
'ui_drop':(.6,'A small cloth pouch set onto paper, one padded touch.'),
'equip':(.8,'A cloth equipment strap drawn snug and a small dull buckle seated.'),
'unequip':(.7,'A small leather strap loosened, muted buckle touch and cloth release.'),
'purchase':(1.0,'A few old bronze coins slid across a wooden counter, low damp coin contacts, no high jingles.'),
'upgrade':(1.4,'Two deliberate padded hammer taps on a tool, short coarse polishing rub, finished low wooden tap.'),
'clue':(1.0,'An old folded paper slip carefully uncovered and unfolded, delicate textured paper contact.'),
'currency':(.7,'Two old coins dropping softly into a cloth purse, muted rounded knocks.'),
'map_reveal':(1.2,'A damp calligraphy brush lightly filling a small paper area, soft wash and clean lift.'),
'settings_tick':(.5,'A tiny hardwood slider nudged one notch, faint warm dry click.')},
'movement':{
'step_dirt':(.65,'One human cloth shoe footstep on compact dirt with fine grit, soft weighted contact.'),
'step_stone':(.65,'One human cloth shoe footstep on rough stone, muted flat contact, no heel clack.'),
'step_wood':(.65,'One human soft boot footstep on old timber boards, low woody body and faint creak.'),
'step_grass':(.65,'One human soft shoe footstep into short grass and dry leaves, quiet compressed rustle.'),
'step_water':(.7,'One footstep in very shallow water and mud, soft rounded splash.'),
'jump':(.6,'A person gently pushing off the ground, short cloth movement and soft sole contact.'),
'land':(.9,'A person landing on both feet on packed ground, low weighted thump and brief clothing settle.'),
'dodge':(.9,'A quick clothed body roll over dry earth, soft layered fabric and gritty slide.'),
'vehicle_call':(1.2,'A small wooden tally tapped twice against a leather pouch, low contained ritual pulse.'),
'vehicle_call_fail':(.8,'A wood tally tapped against cloth without resonance, short muted incomplete gesture.'),
'vehicle_board':(1.0,'A foot on a wooden carriage step followed by a padded seat creak.'),
'vehicle_exit':(1.0,'A carriage seat softly releasing weight then one foot on packed dirt.'),
'vehicle_roll_loop':(5.0,'Continuous low wooden wheel and axle rumble on packed earth, even soft mechanical texture, seamless loop, no engine or voices.')},
'world':{
'door_open':(1.5,'A small heavy timber inn door opening slowly, warm low hinge creak and wooden latch release.'),
'door_close':(1.2,'A timber inn door gently settling shut, low padded wood contact and tiny latch.'),
'gate_open':(3.5,'Massive old timber gate leaves slowly opening on iron pivots, restrained deep wood strain and low iron friction.'),
'cargo_pickup':(1.1,'A heavy sealed cloth cargo bundle lifted from wood, fabric tension and rope creak.'),
'cargo_setdown':(1.2,'A heavy cloth-wrapped cargo bundle placed on timber, soft low weighty thud and rope slack.'),
'inspection_stamp':(.7,'A wood seal pressed once onto thick folded paper on a desk, soft solid knock.'),
'field_rise':(2.2,'A thick tree trunk slowly pushing upward through soil, low wood stretch and earth movement.'),
'field_lower':(1.8,'A thick wooden root settling back into earth, subdued creak and grainy soil closing.'),
'enemy_windup':(.9,'A fighter preparing a heavy staff swing, compact leather and cloth tension, soft wood grip creak.'),
'enemy_swing':(.8,'A heavy wooden pole swinging once through air, short rounded low rush.'),
'enemy_death':(1.1,'A heavy clothed body collapsing onto soft earth, blunt weight and cloth settling, no voice.'),
'groggy':(.9,'A heavy wooden support losing tension with one low dry knock and brief fibre creak.'),
'player_death':(1.3,'A heavy cloth bundle slowly dropping to earth, low dull contact and soft fabric spread.'),
'respawn':(1.5,'Soft fabric unfolding with a restrained inward air movement and gentle ground contact.'),
'lock_on':(.5,'One tiny low wooden bead contact, precise understated click.'),
'lock_off':(.5,'A tiny wooden bead softly sliding off a notch.'),
'block':(.8,'A padded wooden shield taking one glancing blow, low compact wood thump.'),
'misfire':(.8,'A damp brush mark softly drying and crumbling into paper dust, faint breathy friction.')},
'ambience':{
'wind_loop':(6.0,'Steady gentle wind through Korean mountain pine needles, soft broad airy rustle, seamless loop, no loud gusts.'),
'stream_loop':(6.0,'A small shallow mountain stream moving around stones, soft low water wash, seamless loop.'),
'fire_loop':(5.0,'A modest enclosed hearth burning gently, warm low air and tiny subdued wood crackles, seamless loop.'),
'cave_air_loop':(6.0,'Quiet low air moving in a deep abandoned mine, subtle hollow resonance, steady seamless loop, no horror stingers.'),
'cave_drip':(2.5,'Two spaced soft water drops falling into a small stone hollow in a mine, short dark natural reverberation.')}
}
COMMON=' Isolated natural foley for a Korean historical ink-wash game. Rounded soft transients and warm body. No shrill scrape, piercing highs, metallic ringing, voices, melody, synthetic chirps or cinematic boom.'
def inventory():
 OUT.mkdir(parents=True,exist_ok=True)
 rows=[dict(id=k,group=g,duration=d,prompt=p+COMMON,status='planned',loop=k.endswith('_loop')) for g,items in GROUPS.items() for k,(d,p) in items.items()]
 (OUT/'inventory.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
 files=[]
 for folder in ['App/World','App/Demo','Combat','Drawing']:
  for p in (ROOT/'Oheangbu/Assets/_Project/Scripts'/folder).rglob('*.cs'):
   lines=p.read_text(encoding='utf-8-sig').splitlines();hits=[dict(line=i+1,text=s.strip()) for i,s in enumerate(lines) if any(t in s for t in ['AudioClip','AudioSource','PlayUi(','event Action','?.Invoke('])]
   if hits:files.append(dict(path=str(p.relative_to(ROOT)),hits=hits))
 (OUT/'source-audit.json').write_text(json.dumps(files,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(dict(cues=len(rows),seconds=sum(x['duration'] for x in rows),source_files=len(files))))
def generate(group):
 for line in (ROOT/'.env').read_text(encoding='utf-8-sig').splitlines():
  if line.startswith('ELEVENLABS_API_KEY='):os.environ['ELEVENLABS_API_KEY']=line.split('=',1)[1].strip().strip('\"\'')
 assert sum(d for d,p in GROUPS[group].values())<=30
 gen.OUT=OUT/group;gen.OUT.mkdir(parents=True,exist_ok=True)
 gen.SFX={k:(d,p+COMMON) for k,(d,p) in GROUPS[group].items()}
 assert all(len(p)<=450 for d,p in gen.SFX.values())
 with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(lambda k:gen.run('sfx',k),gen.SFX))
 receipts=[json.loads(p.read_text()) for p in (gen.OUT/'requests').glob('*.json')]
 if any(x['status']!='SUCCEEDED' for x in receipts):raise RuntimeError('Generation failure retained; inspect ledger before any new request')
if __name__=='__main__':
 if sys.argv[1]=='inventory':inventory()
 else:generate(sys.argv[1])
