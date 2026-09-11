"""Preserve scope and derive separate review tooling for the six basic spells."""
import json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/SpellVFX120/BasicSix';OUT.mkdir(exist_ok=True)
snapshot=OUT/'before_hashes.json'
if not snapshot.exists():
    old=json.loads((OUT.parent/'Emphasis4/before_hashes.json').read_text(encoding='utf-8'))
    snapshot.write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
EDITOR=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120'
for filename,old,new in [('KtpRectShieldBuild.cs','static GameObject Shield','public static GameObject Shield'),('KtpRectShieldBuild.cs','static GameObject Contact','public static GameObject Contact'),('KtpOffsetGuardBuild.cs','static void Debris','public static void Debris')]:
    p=EDITOR/filename;s=p.read_text(encoding='utf-8');s=s.replace(old,new) if 'public '+old not in s else s;p.write_text(s,encoding='utf-8')
p=EDITOR/'KtpBasicSixCapture.cs'
s=(EDITOR/'KtpEmphasisCapture.cs').read_text(encoding='utf-8')
s=s.replace('KtpEmphasisCapture','KtpBasicSixCapture').replace('KtpEmphasis4Capture','KtpBasicSixCaptureState')
s=s.replace('new[]{"가","나","거","너"}','new[]{"사","마","아","서","머","어"}')
s=s.replace('"Emphasis4",report.glyph+"_v6"','"BasicSix",report.glyph').replace('BaselineV5_','BaselineBasicSix_').replace('V5 baseline missing','Basic six baseline missing')
s=s.replace('profile.Family=="Fire"||profile.Glyph=="너"?Element.Fire:Element.Wood','ElementFor(report.glyph)')
s=s.replace('element==Element.Fire?Element.Metal:Element.Earth','Countered(element)')
s=s.replace('report.glyph=="나"||report.glyph=="너"?Element.Fire:Element.Wood','ElementFor(report.glyph)')
s=s.replace('effect.SetImpactClock(.65f);','float flight=profile.Flight;effect.SetImpactClock(flight);')
s=s.replace('Time.time+.65f','Time.time+flight')
s=s.replace('if(element==Element.Wood)Vfx120Effect.SelectBambooGuard(effect);else Vfx120Effect.SelectFireGuard(effect);','// These three guards use the common native-field and contact path.')
s=s.replace('static void AddPending(','''static Element ElementFor(string glyph)=>glyph=="사"||glyph=="서"?Element.Metal:glyph=="마"||glyph=="머"?Element.Earth:Element.Water;
        static Element Countered(Element element)=>element==Element.Metal?Element.Wood:element==Element.Earth?Element.Water:Element.Fire;
        static void AddPending(''')
p.write_text(s,encoding='utf-8')
s=(EDITOR/'KtpEmphasisAudit.cs').read_text(encoding='utf-8').replace('KtpEmphasisAudit','KtpBasicSixAudit').replace('"Emphasis4","contact_audit.json"','"BasicSix","contact_audit.json"')
s=s.replace('cp.SpellProfiles.Length==4,"Exactly four spell contact overrides"','cp.SpellProfiles.Length==10,"Exactly ten selected spell contact overrides"').replace("'마'","'모'")
s=s.replace("new[]{'가','나'}","new[]{'가','나','사','마','아'}").replace("new[]{'거','너'}","new[]{'거','너','서','머','어'}")
s=s.replace("glyph=='가'?Element.Wood:Element.Fire","ElementFor(glyph)").replace("glyph=='거'?Element.Wood:Element.Fire","ElementFor(glyph)")
s=s.replace('element==Element.Wood?Element.Earth:Element.Metal','Countered(element)').replace('element==Element.Wood?Element.Fire:Element.Earth','Generated(element)')
s=s.replace('static void Run(Report r)', '''static Element ElementFor(char g)=>g=='가'||g=='거'?Element.Wood:g=='나'||g=='너'?Element.Fire:g=='사'||g=='서'?Element.Metal:g=='마'||g=='머'?Element.Earth:Element.Water;
        static Element Countered(Element e)=>e==Element.Wood?Element.Earth:e==Element.Fire?Element.Metal:e==Element.Earth?Element.Water:e==Element.Metal?Element.Wood:Element.Fire;
        static Element Generated(Element e)=>e==Element.Wood?Element.Fire:e==Element.Fire?Element.Earth:e==Element.Earth?Element.Metal:e==Element.Metal?Element.Water:Element.Wood;
        static void Run(Report r)''')
(EDITOR/'KtpBasicSixAudit.cs').write_text(s,encoding='utf-8')
q=EDITOR/'Vfx120Queue.cs';s=q.read_text(encoding='utf-8')
if 'case "BasicSixBuild"' not in s:s=s.replace('case "RectShieldBuild":','case "BasicSixBuild": response.result = KtpBasicSixBuild.Build(command.request); break;\n                    case "BasicSixCapture": response.result = KtpBasicSixCapture.Start(command.request); break;\n                    case "BasicSixAudit": response.result = KtpBasicSixAudit.Start(); break;\n                    case "RectShieldBuild":')
q.write_text(s,encoding='utf-8')
print('Scope preserved; six-spell review tooling prepared.')
