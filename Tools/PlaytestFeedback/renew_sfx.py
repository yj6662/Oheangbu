"""One fresh ElevenLabs request per role. No retries or old audio sources."""
from pathlib import Path
import sys,os,json
from concurrent.futures import ThreadPoolExecutor
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'PlaytestFeedback'))
import generate_assets as gen
gen.OUT=gen.ROOT/'Art/Audio/JourneyRenewal'
gen.OUT.mkdir(parents=True,exist_ok=True)
for line in (gen.ROOT/'.env').read_text(encoding='utf-8-sig').splitlines():
 if line.startswith('ELEVENLABS_API_KEY='):os.environ['ELEVENLABS_API_KEY']=line.split('=',1)[1].strip().strip('\"\'')
common=' Isolated close physical foley for a historical Korean action game. Clear attack, short dry decay. No speech, music, synthetic tones, cartoon sparkle, cinematic bass or ambience.'
roles={
'brush_stroke':(.8,'A firm ink-loaded bristle brush dragged once across rough handmade paper: intimate coarse fiber friction, small wet contact, bristles lifting cleanly.'),
'cast_wood':(1.1,'A green bamboo stalk bending hard then snapping outward in one sharp release, fibrous split and short coarse leaf lash. Dense woody attack.'),
'cast_fire':(1.1,'A fist-sized flame bursting abruptly from folded paper, fast hot ignition and forceful air displacement followed by a few dry embers. One compact burst.'),
'cast_earth':(1.2,'Heavy stone slab shifting then punching into packed earth once, blunt low rock knock, gritty soil burst, scattered pebbles settling.'),
'cast_metal':(1.1,'A narrow steel blade drawn abruptly against a rough iron edge: sharp short scraping attack then a dry metallic cut through air. Damped finish without bell ringing.'),
'cast_water':(1.1,'A concentrated heavy jet of water released from a small vessel, tight wet slap followed by a fast inward swirl. Weighty fluid pulse, not a tiny decorative droplet.'),
'impact':(.7,'One hard staff strike into dense wood and leather: crisp woody crack, compact weighty body thud, tiny splinters. Immediate tactile contact.'),
'player_hit':(.7,'A heavy blunt blow caught by thick layered cloth and leather armor: compressed low thump, short coarse fabric scrape. Close bodily contact without a vocal reaction.'),
'parry':(.9,'Two solid steel edges collide once under tension, a hard crisp initial clank followed by a very short scraped deflection. Dry controlled tail, no repeated ring.'),
'harvest':(1.2,'Thick liquid ink pulled rapidly across rough stone into a ceramic opening, coarse suction and a dense liquid gulp. Short inward physical movement.'),
'harvest_start':(.6,'A tiny ceramic bottle uncorking with a dry friction pop and a short thick liquid intake.'),
'harvest_loop':(2.2,'Steady close thick ink flowing through a narrow ceramic channel, smooth low liquid friction with subtle turbulent wet granules. Constant texture, no distinct starts, droplets, beats or ending gestures. Suitable for a seamless sustained loop.'),
'harvest_end':(.6,'A narrow stream of thick ink cuts off with one soft wet stop and a tiny ceramic contact, then silence.'),
'interact':(.6,'A small wooden tally pressed firmly onto folded coarse paper once. Dry tactile tap with short paper compression.'),
'rest':(1.5,'A person settling a heavy travel pack onto a wooden bench: soft layered cloth collapse, restrained leather creak and one warm low timber knock, calming complete gesture.'),
'summon_appear':(1.5,'A compact mass of folded paper rapidly unfurls and takes weight on stone, dense fiber crackle, short inward rush, firm soft landing. One physical materialization gesture.'),
'summon_release':(1.2,'A taut bundle of coarse paper loses tension and disperses outward, brief soft tearing fibers and dustlike rustle dying away.'),
'ui_paper':(.5,'One stiff coarse paper sheet gently slid sideways over wood, short dry papery friction. Quiet small gesture.'),
'ui_confirm':(.5,'One small hardwood token pressed into a shallow wooden slot: short decisive muted click, dry and satisfying.'),
'ui_back':(.5,'One small hardwood token lifted back out of a wooden slot, light friction followed by a soft dull knock. Restrained short cancel gesture.')}
gen.SFX={k:(d,p+common) for k,(d,p) in roles.items()}
if __name__=='__main__':
 assert sum(v[0] for v in gen.SFX.values())<=30
 assert all(len(v[1])<=450 for v in gen.SFX.values())
 with ThreadPoolExecutor(max_workers=3) as pool:list(pool.map(lambda name:gen.run('sfx',name),gen.SFX))
 rows=[json.loads((gen.OUT/'requests'/('sfx_'+n+'.json')).read_text(encoding='utf-8')) for n in gen.SFX]
 print(json.dumps({'success':sum(r['status']=='SUCCEEDED' for r in rows),'total':len(rows),'actual_units':sum(r.get('actual_usage') or 0 for r in rows)}))
 if any(r['status']!='SUCCEEDED' for r in rows):raise SystemExit(1)
