from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];E=ROOT/'Oheangbu/Assets/_Project/Scripts/Editor/SpellVFX120'
s=(E/'KtpBasicSixCapture.cs').read_text(encoding='utf-8').replace('KtpBasicSixCapture','KtpAreaFiveCapture').replace('new[]{"사","마","아","서","머","어"}','new[]{"고","노","소","모","오"}').replace('"BasicSix",report.glyph','"AreaFive",report.glyph').replace('"/KtpEmphasis/BaselineBasicSix_"','"/AreaFive/Baseline_"')
s=s.replace('public int frames,damageEvents,contactCount,peakParticles;','public int frames,damageEvents,contactCount,peakParticles;\n            public int expectedHits,uniqueContacts,outsideHits;')
s=s.replace('static AsyncOperation releaseUnused;','static AsyncOperation releaseUnused;\n        static readonly HashSet<int> contactIds=new HashSet<int>();\n        static string sourceTag;')
s=s.replace('row.contactCount=Math.Max(row.contactCount,contacts.Length);','row.contactCount=Math.Max(row.contactCount,contacts.Length);foreach(var c in contacts)contactIds.Add(c.GetInstanceID());row.uniqueContacts=contactIds.Count;')
s=s.replace('if(row.contactCount!=1||row.duplicateImpact||!row.ended||(!guard&&row.damageEvents!=1))','if(row.uniqueContacts!=row.expectedHits||row.duplicateImpact||!row.ended||row.damageEvents!=row.expectedHits||row.outsideHits!=0)')
s=s.replace('guard=profile.Behavior==Vfx120Behavior.Shield;impact=false;frame=0;','guard=false;impact=false;frame=0;contactIds.Clear();')
s=s.replace('if(external){camera.transform.position=origin-forward*3+Vector3.Cross(Vector3.up,forward)*5+Vector3.up*2;camera.transform.LookAt(origin+forward*2);}','sourceTag=source.tag;source.tag="Untagged";camera.tag="MainCamera";')
start=s.index('            float flight=profile.Flight;effect.SetImpactClock(flight);')
end=s.index('            row.configuredCastScale=',start)
s=s[:start]+'''            var planned=PlanArea();row.expectedHits=planned.Hits.Count;
            effect.SetAreaPlan(planned.Area);effect.Begin(origin,null,planned.Area.Point+forward*planned.Area.Length,Color.white);
            start=Time.time;
            if(external){camera.transform.position=cameraPosition-forward*1+Vector3.Cross(Vector3.up,forward)*7+Vector3.up*3;camera.transform.LookAt(cameraPosition+forward*6);}
            row.cameraPosition=camera.transform.position;row.cameraEuler=camera.transform.eulerAngles;
'''+s[end:]
start=s.index('        static Element ElementFor(');end=s.index('        static void AddPending(',start)
s=s[:start]+'''        static Element ElementFor(string g)=>g=="고"?Element.Wood:g=="노"?Element.Fire:g=="소"?Element.Metal:g=="모"?Element.Earth:Element.Water;
        static Element Countered(Element e)=>Element.Fire;
        static CastPlan PlanArea()
        {
            var logical=cameraPosition;logical.y=Ground(logical);rig.transform.SetPositionAndRotation(logical,Quaternion.LookRotation(forward));Set(wiring,"_playerTransform",rig.transform);
            var targets=(List<EnemyVitals>)Get(wiring,"_targets");targets.Clear();
            var right=Vector3.Cross(Vector3.up,forward);
            for(int i=0;i<4;i++)
            {
                EnemyVitals targetVitals;
                if(i==0)targetVitals=enemy;
                else{var obj=Object.Instantiate(targetRoot);owned.Add(obj);targetVitals=obj.GetComponent<EnemyVitals>();int index=i;targetVitals.HpChanged+=()=>{if(index==3)row.outsideHits++;else{row.damageEvents++;row.actualHitAt=Time.time-start;}};}
                var point=logical+forward*(i==3?6:5+i*.7f)+right*(i==3?8:(i-1)*.6f);point.y=Ground(point);targetVitals.transform.position=point;targets.Add(targetVitals);
            }
            AreaShape shape=report.glyph=="고"?AreaShape.Circle:report.glyph=="노"?AreaShape.Cone:report.glyph=="소"?AreaShape.Volley:AreaShape.Path;
            var area=new AreaSpec(shape,25,2.3f,12,report.glyph=="오"?3:report.glyph=="소"?32:6,.4f,9,.1f);
            CastPlan plan=null;Action<CastPlan> record=x=>plan=x;wiring.CastPlanned+=record;
            Invoke(wiring,shape==AreaShape.Circle?"ResolveCircleAttack":shape==AreaShape.Cone?"ResolveConeAttack":shape==AreaShape.Volley?"ResolveVolleyAttack":"ResolvePathAttack",new SpellCast(report.glyph[0],SpellKind.AttackArea,ElementFor(report.glyph),9,area,1));
            wiring.CastPlanned-=record;if(plan==null)throw new Exception("No area plan");return plan;
        }
        static float Ground(Vector3 p){return Physics.Raycast(p+Vector3.up*5,Vector3.down,out var h,20,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)?h.point.y:0;}
'''+s[end:]
s=s.replace('if(rig!=null)rig.SetActive(false);','if(source!=null&&sourceTag!=null){source.tag=sourceTag;sourceTag=null;}if(rig!=null)rig.SetActive(false);')
(E/'KtpAreaFiveCapture.cs').write_text(s,encoding='utf-8')
print('Area capture created with production Circle/Cone/Volley/Path plans and 4 test targets.')
