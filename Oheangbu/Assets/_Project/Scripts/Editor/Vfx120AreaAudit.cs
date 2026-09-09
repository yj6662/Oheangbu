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
            public string scope = "Actual VFX transforms: translated/rotated Path origin and front, width, Circle center/radius, Cone extent and supplied Volley impacts. No damage or art verdict.";
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
            Vfx120Effect Spawn(string glyph, AreaImpactPlan plan)
            {
                var entry = catalog.Entries.First(e=>e.Glyph==glyph);
                var go=Object.Instantiate(entry.Prefab); temporary.Add(go); go.hideFlags=HideFlags.HideAndDontSave;
                var fx=go.GetComponent<Vfx120Effect>(); fx.PreviewControlled=true; fx.SetAreaPlan(plan);
                fx.Begin(new Vector3(-3,1.5f,-5), null, new Vector3(-7,1.1f,3), Color.white);
                return fx;
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
                    var fx=Spawn("노",plan);fx.Sample(1);
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
            }
            finally { foreach(var go in temporary) if(go!=null)Object.DestroyImmediate(go); }
            report.status=report.rows.All(r=>r.status=="PASS")?"PASS_SPATIAL_CONTRACT_ONLY":"FAIL";
            string result=JsonUtility.ToJson(report,true);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"Art/SpellVFX120/area_audit.json"),result);
            return result;
        }
    }
}
