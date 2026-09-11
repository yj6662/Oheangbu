"""Scope checkpoint and explicit opt-in integration for the area remake."""
import json,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Art/SpellVFX120/AreaFive';OUT.mkdir(exist_ok=True)
snapshot=OUT/'before_hashes.json'
if not snapshot.exists():
    keys=json.loads((OUT.parent/'BasicSix/before_hashes.json').read_text(encoding='utf-8'))
    keys.update({str(p.relative_to(ROOT)):'' for p in (ROOT/'Oheangbu/Assets/PolyOne/Water URP').rglob('*') if p.is_file()})
    snapshot.write_text(json.dumps({p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in keys},indent=2),encoding='utf-8')
S=ROOT/'Oheangbu/Assets/_Project/Scripts'
def edit(file,old,new):
    p=S/file;s=p.read_text(encoding='utf-8')
    if new in s:return
    assert old in s,(file,old)
    p.write_text(s.replace(old,new),encoding='utf-8')
edit('App/AreaImpactPlan.cs','public float ImpactTime;','public float ImpactTime;\n        public float LaunchTime;')
edit('App/AreaImpactPlan.cs','public sealed class AreaImpactPlan','public sealed class PlannedSpike { public Vector3 Point; public float RiseAt; }\n\n    public sealed class AreaImpactPlan')
edit('App/AreaImpactPlan.cs','public float Delay;','public float Delay;\n        public float CreatedAt;\n        public int ShotCount;\n        public float ShotInterval;\n        public readonly List<PlannedSpike> Spikes = new List<PlannedSpike>();')
edit('App/SpellVFX120/Vfx120Profile.cs','public enum Vfx120BotanicalKind','public enum Vfx120AreaRemake { None, BambooField, FlameCone, NeedleVolley, SandFront, WaterWave }\n    public enum Vfx120BotanicalKind')
edit('App/SpellVFX120/Vfx120Profile.cs','public bool KtpRectShield;','public bool KtpRectShield;\n        public Vfx120AreaRemake AreaRemake;\n        public GameObject AreaContactPrefab;\n        public float AreaCastSeconds = .32f;')
edit('App/SpellVFX120/Vfx120Effect.cs','private void Build()\n        {','private void Build()\n        {\n            if (Profile.AreaRemake != Vfx120AreaRemake.None) { BuildAreaRemake(); return; }')
edit('App/SpellVFX120/Vfx120Effect.cs','Age = Mathf.Max(0, seconds);','Age = Mathf.Max(0, seconds);\n            if(Profile.AreaRemake != Vfx120AreaRemake.None) { SampleAreaRemake(); return; }')
edit('App/SpellVFX120/Vfx120Effect.cs','private void OnDestroy()\n        {','private void OnDestroy()\n        {\n            ClearAreaRemake();')
edit('App/SpellVFX120/Vfx120Effect.cs','ClearGenerated();','ClearGenerated();\n            ClearAreaRemake();')
edit('App/CombatLoopWiring.cs','if(elementalGuard)source=emphasis.GuardContactPrefab;','if(elementalGuard)source=emphasis.GuardContactPrefab;\n            bool areaContact=emphasis!=null&&emphasis.AreaContactPrefab!=null;\n            if(areaContact)source=emphasis.AreaContactPrefab;')
edit('App/CombatLoopWiring.cs','elementalGuard?(emphasis.KtpRectShield?facing:Quaternion.identity):facing * Quaternion.Euler(_contactVfx.SourceEuler)','areaContact?facing:elementalGuard?(emphasis.KtpRectShield?facing:Quaternion.identity):facing * Quaternion.Euler(_contactVfx.SourceEuler)')
edit('App/CombatLoopWiring.cs','if(!elementalGuard)\n            {','if(!elementalGuard&&!areaContact)\n            {')
# Existing circle rules change only for the requested 고 glyph.
edit('App/CombatLoopWiring.cs','float impactTime = Time.time + delay;\n            foreach (var enemy in _targets)\n            {\n                if (enemy == null || !enemy.IsAlive) continue;\n                if (!AreaGeometry.InCircle',
'''if(cast.Letter=='고') AreaSpikePlanner.Fill(plan.Area, ++_areaSpikeSeed);
            float impactTime = Time.time + delay;
            foreach (var enemy in _targets)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (!AreaGeometry.InCircle''')
edit('App/CombatLoopWiring.cs','if (!AreaGeometry.InCircle(center, enemy.transform.position, radius, out _)) continue;\n                Schedule(plan, enemy, impactTime, cast.Power);','if (!AreaGeometry.InCircle(center, enemy.transform.position, radius, out _)) continue;\n                float at=cast.Letter==\'고\'?Time.time+AreaSpikePlanner.NearestRise(plan.Area,enemy.transform.position):impactTime;\n                Schedule(plan, enemy, at, cast.Power);')
edit('App/CombatLoopWiring.cs','private void ResolveCircleAttack(SpellCast cast)','private int _areaSpikeSeed;\n        private void ResolveCircleAttack(SpellCast cast)')
edit('App/CombatLoopWiring.cs','Area = new AreaImpactPlan { Shape = AreaShape.Volley, Direction = forward, Length = range, Speed = speed, Delay = delay, Radius = halfAngle }','Area = new AreaImpactPlan { Shape = AreaShape.Volley, Direction = forward, Length = range, Speed = speed, Delay = delay, Radius = halfAngle, CreatedAt=Time.time, ShotCount=shots, ShotInterval=interval }')
edit('App/CombatLoopWiring.cs','plan.Area.Shots.Add(hit);','hit.LaunchTime=Time.time+delay+i*interval;\n                plan.Area.Shots.Add(hit);')
q='Editor/SpellVFX120/Vfx120Queue.cs'
edit(q,'case "BasicSixBuild":','case "AreaFiveBuild": response.result = KtpAreaFiveBuild.Build(command.request); break;\n                    case "AreaFiveCapture": response.result = KtpAreaFiveCapture.Start(command.request); break;\n                    case "AreaFiveAudit": response.result = KtpAreaFiveAudit.Start(); break;\n                    case "BasicSixBuild":')
print('Area scope and integrations prepared.')
