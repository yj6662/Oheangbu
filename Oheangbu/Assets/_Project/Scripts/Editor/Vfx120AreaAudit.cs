using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    // Spatial assertions exercise the actual VFX prefab, not a duplicate of its evaluator.
    public static class Vfx120AreaAudit
    {
        [Serializable] class Row { public string name, status, error; public float maximumError; }
        [Serializable] class Report
        {
            public string status, unityVersion;
            public string scope = "Actual VFX transforms: translated/rotated Path origin and front, width, Circle center/radius, Cone extent, supplied Volley impacts, rising-ground water travel and short guard expiry. Edit-mode samples only; no damage, native Update/Destroy or art verdict.";
            public List<Row> rows = new List<Row>();
        }
        static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static float FlatDistance(Vector3 a, Vector3 b) { a.y=0; b.y=0; return Vector3.Distance(a,b); }

        public static string Run()
        {
            Need(!EditorApplication.isPlayingOrWillChangePlaymode, "Edit mode required");
            var report = new Report { unityVersion=Application.unityVersion };
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>("Assets/_Project/Art/SpellVFX120/Data/VFX120_Catalog.asset");
            var temporary = new List<GameObject>();
            Vfx120Effect Spawn(string glyph, AreaImpactPlan plan, Vector3? origin=null,
                Vector3? fallback=null, float guardDuration=0)
            {
                var entry = catalog.Entries.First(e=>e.Glyph==glyph);
                var go=Object.Instantiate(entry.Prefab); temporary.Add(go); go.hideFlags=HideFlags.HideAndDontSave;
                var fx=go.GetComponent<Vfx120Effect>(); fx.PreviewControlled=true; fx.SetAreaPlan(plan);
                if(guardDuration>0) fx.SetGuardClock(guardDuration,guardDuration*.5f);
                fx.Begin(origin ?? new Vector3(-3,1.5f,-5), null, fallback ?? new Vector3(-7,1.1f,3), Color.white);
                return fx;
            }
            void GroundPad(Vector3 center, float top)
            {
                var go=new GameObject("Water audit ground");temporary.Add(go);go.hideFlags=HideFlags.HideAndDontSave;
                go.transform.position=new Vector3(center.x,top-.1f,center.z);
                go.AddComponent<BoxCollider>().size=new Vector3(1.2f,.2f,1.2f);
            }
            void Test(string label, Action<Row> action)
            {
                var row=new Row{name=label}; report.rows.Add(row);
                try { action(row); row.status="PASS"; }
                catch(Exception e){row.status="FAIL";row.error=e.Message;}
                finally { foreach(var go in temporary) if(go!=null) Object.DestroyImmediate(go); temporary.Clear(); }
            }
            try
            {
                Test("path_plan_origin_direction_speed_and_width", row=>
                {
                    var plan=new AreaImpactPlan{Shape=AreaShape.Path,Point=new Vector3(2,0,1),Direction=Vector3.right,Radius=2.2f,Length=10,Speed=4,Delay=.4f};
                    string before=JsonUtility.ToJson(plan); var fx=Spawn("오",plan);
                    foreach(float age in new[]{.2f,.4f,.9f,1.4f,2.4f})
                    {
                        fx.Sample(age);var part=fx.transform.Find("Body_0");
                        var expected=plan.Point+plan.Direction*Mathf.Clamp((age-plan.Delay)*plan.Speed,0,plan.Length);
                        float error=FlatDistance(part.position,expected); row.maximumError=Mathf.Max(row.maximumError,error);
                        Need(error<.015f,"Wave front differs from authoritative path at "+age+": "+error);
                        if(age>1) Need(Mathf.Abs(part.GetComponent<Renderer>().bounds.size.z-plan.Radius*2)<.08f,"Wave rendered width differs from path width");
                    }
                    Need(JsonUtility.ToJson(plan)==before,"Path plan was mutated");
                });
                Test("circle_uses_plan_center_and_radius",row=>
                {
                    var plan=new AreaImpactPlan{Shape=AreaShape.Circle,Point=new Vector3(6,0,8),Direction=Vector3.forward,Radius=2.7f,Delay=.4f};
                    var fx=Spawn("고",plan);fx.Sample(1);
                    for(int i=0;i<fx.PartCount;i++)
                    {
                        float radius=FlatDistance(fx.transform.Find("Body_"+i).position,plan.Point);
                        row.maximumError=Mathf.Max(row.maximumError,Mathf.Max(0,radius-plan.Radius));
                        Need(radius<=plan.Radius+.015f,"Thorn pivot outside Circle radius: "+radius);
                    }
                });
                Test("cone_uses_caster_origin_angle_and_reach",row=>
                {
                    var plan=new AreaImpactPlan{Shape=AreaShape.Cone,Point=new Vector3(2,0,1),Direction=Vector3.right,Angle=27,Length=8,Delay=.4f};
                    var fx=Spawn("노",plan);fx.Sample(plan.Delay);
                    float farthest=0;Vector3 side=Vector3.Cross(Vector3.up,plan.Direction);
                    for(int i=0;i<fx.PartCount;i++)
                    {
                        var part=fx.transform.Find("Body_"+i);Vector3 d=part.position-plan.Point;
                        float forward=Vector3.Dot(d,plan.Direction),lateral=Mathf.Abs(Vector3.Dot(d,side));
                        Need(forward>=-.02f && forward<=plan.Length+.02f,"Cone pivot behind origin or beyond range");
                        Need(lateral<=Mathf.Tan(plan.Angle*Mathf.Deg2Rad)*forward+.08f,"Cone pivot outside supplied half angle");
                        var bounds=part.GetComponent<Renderer>().bounds; farthest=Mathf.Max(farthest,bounds.max.x-plan.Point.x);
                    }
                    row.maximumError=Mathf.Max(0,plan.Length*.75f-farthest);
                    Need(farthest>=plan.Length*.75f,"Cone effect never reaches most of the planned range");
                    fx.Sample(fx.Life);
                    for(int i=0;i<fx.PartCount;i++)
                        Need(fx.transform.Find("Body_"+i).localScale.sqrMagnitude<.000001f,
                            "Cone geometry survives its authoritative lifetime");
                });
                Test("volley_each_target_and_impact_clock",row=>
                {
                    var plan=new AreaImpactPlan{Shape=AreaShape.Volley,Point=new Vector3(0,1,7),Direction=Vector3.forward,Length=12,Speed=18,Delay=.2f};
                    for(int i=0;i<2;i++)
                    {
                        var target=new GameObject("Volley probe");temporary.Add(target); target.hideFlags=HideFlags.HideAndDontSave;
                        target.transform.position=new Vector3(i*2,0,7+i);
                        plan.Shots.Add(new PlannedHit{Target=target.AddComponent<EnemyVitals>(),ImpactTime=Time.time+.5f+i*.3f,Power=1});
                    }
                    var fx=Spawn("소",plan); Need(fx.PartCount==2,"Part count differs from supplied shot count");
                    for(int i=0;i<2;i++)
                    {
                        fx.Sample(.5f+i*.3f);
                        float error=Vector3.Distance(fx.transform.Find("Body_"+i).position,plan.Shots[i].Target.transform.position+Vector3.up*1.1f);
                        row.maximumError=Mathf.Max(row.maximumError,error); Need(error<.01f,"Volley arrives at wrong target/clock: "+error);
                    }
                });
                foreach(string glyph in new[]{"온","옷","옹"})
                Test("water_rising_ground_mid_travel_"+glyph,row=>
                {
                    // Temporary collider-only pads let Begin read real ground heights.
                    // A remote high-altitude fixture avoids existing scene surfaces;
                    // the pads are removed by Test's finally, without editing scene assets.
                    var origin=new Vector3(1500,1000,1500);
                    var direction=Quaternion.Euler(0,37,0)*Vector3.forward;
                    var target=origin+direction*5+Vector3.up;
                    float startGround=origin.y-1, endGround=origin.y;
                    GroundPad(origin,startGround);GroundPad(target,endGround);Physics.SyncTransforms();
                    var fx=Spawn(glyph,null,origin,target);
                    Need(fx.ReceivedAreaPlan==null,"Water test unexpectedly has an authoritative area plan");
                    var part=fx.transform.Find("Body_0");var renderer=part.GetComponent<Renderer>();
                    foreach(float afterFlight in new[]{.65f,1.30f})
                    {
                        fx.Sample(fx.Profile.Flight+afterFlight);
                        // Observe travel instead of duplicating the evaluator's speed.
                        float progress=Vector3.Dot(part.position-origin,direction)/5;
                        Need(progress>.1f && progress<.8f,"Sample did not reach a middle travel position");
                        Need(renderer.enabled && renderer.bounds.size.y>.03f,"Water body is not visible geometry");
                        float expectedBottom=Mathf.Lerp(startGround,endGround,progress)+.025f;
                        float actualBottom=renderer.bounds.min.y;
                        float error=Mathf.Abs(actualBottom-expectedBottom);
                        row.maximumError=Mathf.Max(row.maximumError,error);
                        Need(error<.006f,"Water bottom was lifted to target ground instead of current travel ground: "+error);
                        Need(actualBottom<endGround-.15f,"Water prematurely occupies destination ground height");
                    }
                });
                Test("water_short_guard_accents_expire_at_life",row=>
                {
                    var fx=Spawn("우",null,guardDuration:.5f);
                    Need(Mathf.Abs(fx.Life-.5f)<.00001f,"Supplied short guard clock was not retained");
                    var block=new MaterialPropertyBlock();int alphaId=Shader.PropertyToID("_Alpha");
                    fx.Sample(.2f);int visibleBefore=0;
                    for(int i=0;i<fx.AccentCount;i++)
                    {
                        var renderer=fx.transform.Find("Flecks_"+i).GetComponent<Renderer>();
                        renderer.GetPropertyBlock(block);
                        if(renderer.enabled && block.GetFloat(alphaId)>.001f)visibleBefore++;
                    }
                    Need(visibleBefore>0,"Expiry check is vacuous: water accents never appeared");
                    foreach(float age in new[]{fx.Life,fx.Life+1f/120f})
                    {
                        fx.Sample(age);
                        for(int i=0;i<fx.AccentCount;i++)
                        {
                            var renderer=fx.transform.Find("Flecks_"+i).GetComponent<Renderer>();
                            renderer.GetPropertyBlock(block);float alpha=block.GetFloat(alphaId);
                            row.maximumError=Mathf.Max(row.maximumError,Mathf.Abs(alpha));
                            Need(!renderer.enabled && Mathf.Abs(alpha)<.000001f,
                                "Water accent remains visible at/after the supplied lifetime: "+i+" / "+alpha);
                        }
                    }
                });
            }
            finally { foreach(var go in temporary) if(go!=null)Object.DestroyImmediate(go); }
            report.status=report.rows.All(r=>r.status=="PASS")?"PASS_SPATIAL_CONTRACT_ONLY":"FAIL";
            string result=JsonUtility.ToJson(report,true);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"Art/SpellVFX120/area_audit.json"),result);
            return result;
        }
    }
}
