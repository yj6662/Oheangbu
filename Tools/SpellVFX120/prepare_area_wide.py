from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[2]
ED=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120'
OUT=ROOT/'Art/SpellVFX120/AreaWide';OUT.mkdir(exist_ok=True)
old=json.loads((OUT.parent/'AreaFive/before_hashes.json').read_text(encoding='utf-8'))
snapshot=OUT/'before_hashes.json'
if not snapshot.exists():snapshot.write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in old},indent=2),encoding='utf-8')
s=(ED/'KtpAreaFiveCapture.cs').read_text(encoding='utf-8').replace('KtpAreaFiveCapture','KtpAreaWideCapture').replace('"AreaFive"','"AreaWide"').replace('/AreaFive/Baseline_','/AreaWide/Baseline_')
s=s.replace('for(int i=0;i<4;i++)','for(int i=0;i<10;i++)').replace('if(index==3)','if(index==9)')
s=s.replace('logical+forward*(i==3?6:5+i*.7f)+right*(i==3?8:(i-1)*.6f)','logical+forward*(i==9?8:6+i/3*2)+right*(i==9?12:(i%3-1)*4)')
s=s.replace('"Assets/_Project/Data/Configs/SpellBook_Proto.asset"','row.variant=="before"?KtpAreaWideBuild.Folder+"/BaselineSpellBook.asset":"Assets/_Project/Data/Configs/SpellBook_Proto.asset"')
s=s.replace('ElementFor(report.glyph),9,area,1','ElementFor(report.glyph),spell.BasePower,area,1')
s=s.replace('three targets plus one outside target','nine targets plus one outside target').replace('Go baseline visuals replay the new staggered damage plan.','Before uses saved V1 profiles and SpellBook; after uses current profiles and SpellBook.')
(ED/'KtpAreaWideCapture.cs').write_text(s,encoding='utf-8')
s=(ED/'KtpAreaFiveAudit.cs').read_text(encoding='utf-8').replace('KtpAreaFiveAudit','KtpAreaWideAudit').replace('"AreaFive"','"AreaWide"')
s=s.replace('maxPeak<=1.16f&&minPeak>=.74f','maxPeak<=2.26f&&minPeak>=1.94f')
needle='pending.Clear();var selected=targets[0];'
addition='''pending.Clear();
   typeof(CombatLoopWiring).GetMethod("ResolveVolleyAttack",Hidden).Invoke(wiring,new object[]{new SpellCast('소',SpellKind.AttackArea,Element.Metal,8,new AreaSpec(AreaShape.Volley,45,0,16,32,1.45f,24,.045f,true),1)});
   Need(r,captured.Area.Shots.Count==24&&captured.Area.Shots.All(x=>x.HasImpactPoint),"wide volley has 24 fixed endpoints including misses");
   Need(r,captured.Hits.Count>0&&captured.Hits.Count<24&&captured.Hits.All(x=>x.Target.transform.position.x<20),"wide volley reserves only fan-ray intersections; no convergence on one target");
   Need(r,captured.Area.Shots.Max(x=>x.ImpactPoint.x)-captured.Area.Shots.Min(x=>x.ImpactPoint.x)>18,"wide volley spreads over eighteen meters at range");
   Need(r,captured.Hits.Sum(x=>x.Power)<=8.001f,"wide volley preserves maximum total power eight");
   pending.Clear();var selected=targets[0];'''
assert needle in s;s=s.replace(needle,addition)
needle='Gameplay(r);'
addition='''var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>("Assets/_Project/Data/Configs/SpellBook_Proto.asset");
   foreach(char g in new[]{'모','오'}){book.TryGet(g,out var entry);Need(r,entry.AreaRadius==6&&entry.BasePower==5,g+" live twelve-meter width and power five");}
   book.TryGet('소',out var volley);Need(r,volley.VolleyShots==24&&volley.AreaAngle==45&&volley.ScatterVolley,"live scatter volley tuning");
   Need(r,Vfx120Effect.BellWaveHeight(0,-1.3f,0)>Vfx120Effect.BellWaveHeight(.5f,-1.3f,0)&&Vfx120Effect.BellWaveHeight(.5f,-1.3f,0)>Vfx120Effect.BellWaveHeight(.9f,-1.3f,0),"wave silhouette descends from center to both sides");
   Need(r,Mathf.Abs(Vfx120Effect.BellWaveHeight(-.5f,-1.3f,.4f)-Vfx120Effect.BellWaveHeight(.5f,-1.3f,.4f))<.0001f,"wave bell silhouette symmetric");
   Gameplay(r);'''
assert needle in s;s=s.replace(needle,addition)
(ED/'KtpAreaWideAudit.cs').write_text(s,encoding='utf-8')
s=(ROOT/'Tools/SpellVFX120/run_area_five.py').read_text(encoding='utf-8').replace('AreaFive','AreaWide')
(ROOT/'Tools/SpellVFX120/run_area_wide.py').write_text(s,encoding='utf-8')
print('Wide capture, audit, sequential runner and preservation snapshot prepared')
