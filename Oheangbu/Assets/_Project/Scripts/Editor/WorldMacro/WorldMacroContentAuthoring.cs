using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Oheangbu.Data.World;
using Oheangbu.App.World;
using Oheangbu.App.World.Dressing;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroContentAuthoring
    {
        public const string RootName="WorldMacro_Content_Layout";
        const string Folder=WorldMacroBuilder.Folder+"/Content";
        public static string Output=>WorldMacroBuilder.Output+"/Content";
        static WorldMacroContentSheetSO sheet;
        static readonly RaycastHit[] hits=new RaycastHit[256];
        static readonly List<Bounds> occupied=new List<Bounds>();
        static readonly List<MeshFilter> waters=new List<MeshFilter>();
        static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
        [Serializable]class Export{public string utc,scope;public WorldMacroContentSheetSO.Entry[] entries;public string[] checks,conflicts;public int enemyObjects,dungeons,npcs,events;}
        static void Require(){if(EditorApplication.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new Exception("Macro Edit scene required");if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("System commit >=85%; authoring stopped");}
        static WorldMacroContentSheetSO Load()=>AssetDatabase.LoadAssetAtPath<WorldMacroContentSheetSO>(Folder+"/Content.asset");
        static float Ground(Vector3 p)
        {
            float y=float.NegativeInfinity;int n=Physics.RaycastNonAlloc(new Vector3(p.x,2400,p.z),Vector3.down,hits,4800,1,QueryTriggerInteraction.Ignore);
            if(n==hits.Length)throw new Exception("Ground query overflow");
            for(int i=0;i<n;i++)if(hits[i].collider.name.StartsWith("Terrain_"))y=Mathf.Max(y,hits[i].point.y);
            return y;
        }
        static bool Dry(Vector3 p)
        {
            if(!float.IsFinite(p.y)||!WorldMacroTerrain.Contains(WorldMacroBuilder.Sheet,p.x,p.z))return false;
            foreach(var mf in waters)
            {
                var bounds=mf.GetComponent<Renderer>().bounds;
                if(p.x<bounds.min.x||p.x>bounds.max.x||p.z<bounds.min.z||p.z>bounds.max.z||p.y>bounds.max.y+.3f)continue;
                // Authoring only: exact current water triangles, rather than nominal river width.
                var mesh=mf.sharedMesh;var v=mesh.vertices;var t=mesh.triangles;var m=mf.transform.localToWorldMatrix;
                for(int i=0;i<t.Length;i+=3)
                {
                    var a=m.MultiplyPoint3x4(v[t[i]]);var b=m.MultiplyPoint3x4(v[t[i+1]]);var c=m.MultiplyPoint3x4(v[t[i+2]]);
                    float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.00001f)continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/d,w=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/d;
                    if(u>=0&&w>=0&&u+w<=1&&u*a.y+w*b.y+(1-u-w)*c.y>p.y-.1f)return false;
                }
            }
            return true;
        }
        static float RoadDistance(Vector3 p)
        {float d=float.PositiveInfinity;foreach(var r in WorldMacroBuilder.Sheet.Routes)for(int i=1;i<r.Points.Length;i++)d=Mathf.Min(d,WorldMacroTerrain.SegmentDistance(p.x,p.z,r.Points[i-1],r.Points[i],out _)-r.Width*.5f);return d;}
        static Vector3 Anchor(string id,out float yaw)
        {
            var t=GameObject.Find(WorldMacroLandmarkAuthoring.RootName)?.transform.Find(id);yaw=t!=null?t.eulerAngles.y:0;
            if(t!=null)return t.position;
            var site=WorldMacroBuilder.Sheet.FindSite(id);if(site==null)throw new Exception("Missing anchor "+id);return site.Position;
        }
        static bool Fit(WorldMacroContentSheetSO.Entry e,Vector3 p,out float level)
        {
            level=float.NegativeInfinity;float lo=float.PositiveInfinity;
            bool dungeon=e.Kind==MacroContentKind.Dungeon;var q=Quaternion.Euler(0,e.Yaw,0);
            float half=dungeon?8:e.Kind==MacroContentKind.Boss?12:e.Kind==MacroContentKind.Encounter?6:1;
            var box=new Bounds(p+q*new Vector3(0,0,dungeon?20:0),new Vector3(dungeon?55:half*2,10000,dungeon?55:half*2));
            if(occupied.Any(b=>b.Intersects(box)))return false;
            for(int z=0;z<(dungeon?6:3);z++)for(int x=-1;x<=1;x++)
            {
                var s=p+q*new Vector3(x*half,0,dungeon?-12+z*11.2f:(z-1)*half);s.y=Ground(s);
                if(!Dry(s))return false;level=Mathf.Max(level,s.y);lo=Mathf.Min(lo,s.y);
                if(RoadDistance(s)<(dungeon?5:1))return false;
            }
            return level-lo<(dungeon?4.5f:e.Kind==MacroContentKind.Boss?3:2);
        }
        static void Resolve(WorldMacroContentSheetSO.Entry e)
        {
            if(e.Resolved&&e.Preserve){ReserveDungeon(e);return;}
            var a=Anchor(e.Anchor,out float yaw);e.Yaw=yaw;
            var requested=a+Quaternion.Euler(0,yaw,0)*e.Offset;
            for(int ring=0;ring<85;ring++)for(int k=0;k<(ring==0?1:20);k++)
            {
                float angle=(k*137.508f+ring*23)*Mathf.Deg2Rad;
                var p=requested+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*12;
                p.y=Ground(p);if(!Fit(e,p,out float level))continue;
                e.Position=new Vector3(p.x,e.Kind==MacroContentKind.Dungeon?level+.18f:p.y+.04f,p.z);e.Resolved=true;
                ReserveDungeon(e);
                return;
            }
            throw new Exception("No dry accessible placement found: "+e.Id);
        }
        static void ReserveDungeon(WorldMacroContentSheetSO.Entry e){if(e.Kind==MacroContentKind.Dungeon)occupied.Add(new Bounds(e.Position+Quaternion.Euler(0,e.Yaw,0)*new Vector3(0,0,20),new Vector3(55,10000,55)));}
        static Material Mat(string id,Color c)
        {
            if(materials.TryGetValue(id,out var m)&&m!=null)return m;string path=Folder+"/"+id+".mat";m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));m.SetColor("_BaseColor",c);AssetDatabase.CreateAsset(m,path);}materials[id]=m;return m;
        }
        static GameObject Shape(Transform parent,string name,PrimitiveType kind,Vector3 pos,Vector3 size,Material material,bool solid=false)
        {var g=GameObject.CreatePrimitive(kind);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;if(!solid)Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
        static void Cube(Transform p,string name,Vector3 at,Vector3 size,Material m,bool solid=true)=>Shape(p,name,PrimitiveType.Cube,at,size,m,solid);
        static Material Earth=>Mat("Stone",new Color(.29f,.28f,.25f));static Material Wood=>Mat("Timber",new Color(.28f,.22f,.16f));
        static Material Paper=>Mat("Paper",new Color(.57f,.54f,.45f));
        static Color EnemyColor(string role)=>role.Contains("Fire")?new Color(.4f,.19f,.12f):role.Contains("Wood")?new Color(.2f,.32f,.2f):role.Contains("Water")?new Color(.2f,.29f,.34f):role.Contains("Metal")?new Color(.42f,.43f,.4f):role.Contains("Earth")?new Color(.38f,.3f,.16f):new Color(.27f,.26f,.25f);
        static void Person(Transform root,string name,Vector3 pos,Material m,float height,bool npc)
        {
            Shape(root,name,PrimitiveType.Capsule,pos+Vector3.up*height*.5f,new Vector3(height*.33f,height*.5f,height*.33f),m);
            if(npc){Shape(root,"ClothHat",PrimitiveType.Cylinder,pos+Vector3.up*(height-.12f),new Vector3(.62f,.06f,.62f),Wood);}
        }
        static void Dungeon(Transform v,WorldMacroContentPoint point,WorldMacroContentSheetSO.Entry e)
        {
            var m=e.Role=="Warehouse"?Wood:Earth;
            foreach(float z in new[]{6f,22f,38f})
            {
                Cube(v,"Room_Floor",new Vector3(0,-.25f,z),new Vector3(12,.5f,12),Earth);
                Cube(v,"Left_Wall",new Vector3(-6,2,z),new Vector3(.5f,4,12),m);Cube(v,"Right_Wall",new Vector3(6,2,z),new Vector3(.5f,4,12),m);
                foreach(float dz in new[]{-6f,6f})foreach(float x in new[]{-4f,4f})Cube(v,"Door_Jamb",new Vector3(x,2,z+dz),new Vector3(4,4,.5f),m);
                if(e.Role!="Tomb")Cube(v,"Roof",new Vector3(0,4.3f,z),new Vector3(12.5f,.5f,12.5f),m);
            }
            foreach(float z in new[]{14f,30f}){Cube(v,"Corridor",new Vector3(0,-.25f,z),new Vector3(4,.5f,4),Earth);foreach(float x in new[]{-2.25f,2.25f})Cube(v,"Corridor_Wall",new Vector3(x,2,z),new Vector3(.5f,4,4),m);}
            Cube(v,"End_Wall",new Vector3(0,2,44),new Vector3(4,4,.5f),m);
            var points=new List<Vector3>();
            for(int i=0;i<=12;i++){float z=-12+i;var p=v.TransformPoint(new Vector3(0,0,z));float g=Ground(p);float y=Mathf.Lerp(g+.05f,e.Position.y,Mathf.Clamp01((z+12)/12));points.Add(new Vector3(0,y-e.Position.y,z));}
            for(int i=1;i<points.Count;i++)
            {
                var a=points[i-1];var b=points[i];var d=b-a;var g=Shape(v,"Entry_Ramp",PrimitiveType.Cube,(a+b)*.5f-Vector3.up*.15f,new Vector3(4,.3f,d.magnitude+.03f),Earth,true);g.transform.localRotation=Quaternion.LookRotation(d,Vector3.up);
            }
            point.InspectionPath=points.Concat(new[]{new Vector3(0,.05f,6),new Vector3(0,.05f,22),new Vector3(0,.05f,38)}).ToArray();
            for(int i=0;i<3;i++)Person(v,"DungeonEnemy_"+i,new Vector3(i%2==0?3:-3,0,6+i*16),Mat("Enemy_Neutral",EnemyColor("Neutral")),1.65f,false);
            Cube(v,"Evidence_Altar",new Vector3(0,.45f,41),new Vector3(1.8f,.9f,1),Earth);
        }
        static WorldMacroContentPoint Build(Transform parent,WorldMacroContentSheetSO.Entry e)
        {
            var g=new GameObject(e.Id+"__"+e.Label);g.transform.SetParent(parent,false);g.transform.SetPositionAndRotation(e.Position,Quaternion.Euler(0,e.Yaw,0));
            var point=g.AddComponent<WorldMacroContentPoint>();point.Id=e.Id;point.CombatRole=e.Role;point.LeashRadius=e.Kind==MacroContentKind.Boss?25:18;point.CombatConnected=false;
            var v=new GameObject("Visual").transform;v.SetParent(g.transform,false);point.Visual=v;
            if(e.Kind==MacroContentKind.Dungeon)Dungeon(v,point,e);
            else if(e.Kind==MacroContentKind.Encounter||e.Kind==MacroContentKind.Boss)
            {
                for(int i=0;i<e.Count;i++){var p=new Vector3((i-(e.Count-1)*.5f)*2.1f,0,i%2*1.8f);p.y=Ground(v.TransformPoint(p))-e.Position.y+.04f;Person(v,"Enemy_"+i,p,Mat("Enemy_"+e.Role,EnemyColor(e.Role)),e.Kind==MacroContentKind.Boss?3.5f:1.7f,false);}
                point.InspectionPath=new[]{new Vector3(-4,0,0),new Vector3(3,0,4),new Vector3(4,0,-3)};
            }
            else if(e.Kind==MacroContentKind.Npc)Person(v,"NPC_Proxy",Vector3.zero,Mat("NPC",new Color(.42f,.39f,.32f)),1.7f,true);
            else if(e.Kind==MacroContentKind.Rest)
            {Cube(v,"Shrine_Base",new Vector3(0,.3f,0),new Vector3(1.5f,.6f,1.2f),Earth);Cube(v,"Shrine_Roof",new Vector3(0,1.9f,0),new Vector3(2,.2f,1.7f),Wood);foreach(float x in new[]{-.6f,.6f})Cube(v,"Post",new Vector3(x,1.2f,0),new Vector3(.12f,1.5f,.12f),Wood);}
            else {Cube(v,"Evidence_Platform",new Vector3(0,.35f,0),new Vector3(1.2f,.7f,.8f),Earth);Cube(v,"Inscribed_Surface",new Vector3(0,.82f,0),new Vector3(.8f,.24f,.5f),Paper,false);}
            return point;
        }
        public static string Install()
        {
            Require();Directory.CreateDirectory(Output);Directory.CreateDirectory(Folder);Physics.SyncTransforms();
            string scene=WorldMacroBuilder.ScenePath;if(!File.Exists(Output+"/BeforeContent.unity"))File.Copy(scene,Output+"/BeforeContent.unity");
            sheet=Load();if(sheet==null){sheet=ScriptableObject.CreateInstance<WorldMacroContentSheetSO>();JsonUtility.FromJsonOverwrite(File.ReadAllText(Output+"/seed.json"),sheet);AssetDatabase.CreateAsset(sheet,Folder+"/Content.asset");}
            waters.Clear();waters.AddRange(Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m=>m.name.StartsWith("Water_")&&m.sharedMesh!=null));occupied.Clear();
            var landmarks=GameObject.Find(WorldMacroLandmarkAuthoring.RootName);if(landmarks!=null)foreach(Transform t in landmarks.transform){var b=new Bounds(t.position,new Vector3(10,10000,10));foreach(var c in t.GetComponentsInChildren<Collider>())b.Encapsulate(c.bounds);b.size=new Vector3(b.size.x+5,10000,b.size.z+5);occupied.Add(b);}
            foreach(var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c=>c.name.StartsWith("Massing_"))){var b=c.bounds;b.size=new Vector3(b.size.x+3,10000,b.size.z+3);occupied.Add(b);}
            var old=GameObject.Find(RootName);if(old!=null){foreach(var p in old.GetComponentsInChildren<WorldMacroContentPoint>(true)){var entry=Array.Find(sheet.Entries,e=>e.Id==p.Id);if(entry!=null&&entry.Preserve){entry.Position=p.transform.position;entry.Yaw=p.transform.eulerAngles.y;}}}
            foreach(var e in sheet.Entries){Resolve(e);EditorUtility.SetDirty(sheet);}
            // Resolve all placements before replacing the existing layout.
            if(old!=null)Object.DestroyImmediate(old);
            var root=new GameObject(RootName);var all=new List<WorldMacroContentPoint>();
            foreach(var region in sheet.Entries.GroupBy(e=>e.Realm)){var group=new GameObject(region.Key).transform;group.SetParent(root.transform,false);foreach(var e in region)all.Add(Build(group,e));}
            var preview=root.AddComponent<WorldMacroContentPreview>();preview.Sheet=sheet;preview.Walker=Object.FindFirstObjectByType<WorldMacroReviewController>();preview.Points=all.ToArray();
            var dressing=AssetDatabase.LoadAssetAtPath<WorldMacroDressingSheetSO>(WorldMacroDressingAuthoring.SheetPath);
            var zones=dressing.PreservedAreas.Where(a=>!a.Id.StartsWith("content:")).ToList();
            foreach(var e in sheet.Entries){bool d=e.Kind==MacroContentKind.Dungeon;zones.Add(new WorldMacroDressingSheetSO.PreserveArea{Id="content:"+e.Id,Centre=e.Position+Quaternion.Euler(0,e.Yaw,0)*new Vector3(0,0,d?16:0),HalfSize=d?new Vector2(11,32):Vector2.one*(e.Kind==MacroContentKind.Boss?16:e.Kind==MacroContentKind.Encounter?9:3),Yaw=e.Yaw,ExcludeProcedural=true});}
            dressing.PreservedAreas=zones.ToArray();EditorUtility.SetDirty(dressing);Object.FindFirstObjectByType<WorldMacroDressingRenderer>()?.ResetCache();
            AssetDatabase.SaveAssets();WorldMacroWaterAuthoring.SaveScene();return Audit();
        }
        public static string Audit()
        {
            sheet=Load();var root=GameObject.Find(RootName);if(sheet==null||root==null)throw new Exception("Content not installed");Physics.SyncTransforms();
            var points=root.GetComponentsInChildren<WorldMacroContentPoint>(true);var checks=new List<string>();
            void Check(bool ok,string name){checks.Add((ok?"PASS ":"FAIL ")+name);}
            Check(sheet.Entries.Select(e=>e.Id).Distinct().Count()==sheet.Entries.Length,"unique persistent content IDs");Check(points.Length==sheet.Entries.Length,"every content entry has an actual scene object");
            Check(sheet.Entries.All(e=>e.Resolved&&float.IsFinite(e.Position.y)&&WorldMacroTerrain.Contains(WorldMacroBuilder.Sheet,e.Position.x,e.Position.z)),"resolved finite positions within world outline");
            Check(sheet.Entries.All(e=>string.IsNullOrEmpty(e.Requires)||sheet.Entries.Any(t=>t.Id==e.Requires)),"event prerequisite references exist");
            Check(sheet.Entries.All(e=>!string.IsNullOrEmpty(e.Source)),"Bible section provenance on all entries");
            Check(points.All(p=>!p.CombatConnected),"layout preview does not alter existing combat/save state");
            Check(points.Where(p=>Array.Find(sheet.Entries,e=>e.Id==p.Id).Kind!=MacroContentKind.Dungeon).All(p=>!Physics.OverlapCapsule(p.transform.position+Vector3.up*.30f,p.transform.position+Vector3.up*1.47f,.28f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(root.transform))),"content approach capsules outside existing structures");
            int pathSamples=0,blocked=0;var conflicts=new HashSet<string>();foreach(var p in points.Where(p=>Array.Find(sheet.Entries,e=>e.Id==p.Id).Kind==MacroContentKind.Dungeon))
            {var path=p.InspectionPath;for(int i=1;i<path.Length;i++){int n=Mathf.CeilToInt(Vector3.Distance(path[i-1],path[i])/.5f);for(int k=0;k<=n;k++){var foot=p.transform.TransformPoint(Vector3.Lerp(path[i-1],path[i],k/(float)Mathf.Max(1,n)));pathSamples++;var overlaps=Physics.OverlapCapsule(foot+Vector3.up*.30f,foot+Vector3.up*1.47f,.28f,~0,QueryTriggerInteraction.Ignore);if(overlaps.Length>0){blocked++;foreach(var c in overlaps)conflicts.Add(p.Id+" : "+c.name+" segment "+i);}}}}
            Check(blocked==0,"dungeon centre-route capsule clearance: "+blocked+" blocked / "+pathSamples+" samples (not user traversal)");
            var export=new Export{utc=DateTime.UtcNow.ToString("o"),scope="First-pass content positions and accessible modular dungeon layouts. NPC/evidence F inspection is preview only; no campaign quests, boss AI or reward gates are declared complete.",entries=sheet.Entries,checks=checks.ToArray(),enemyObjects=root.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("Enemy_")||t.name.StartsWith("DungeonEnemy_")),dungeons=sheet.Entries.Count(e=>e.Kind==MacroContentKind.Dungeon),npcs=sheet.Entries.Count(e=>e.Kind==MacroContentKind.Npc),events=sheet.Entries.Count(e=>e.Kind==MacroContentKind.Event||e.Kind==MacroContentKind.Evidence)};
            export.conflicts=conflicts.ToArray();File.WriteAllText(Output+"/placement.json",JsonUtility.ToJson(export,true));return string.Join("\n",checks)+"\n"+string.Join("\n",conflicts);
        }
        public static string Visit(string id)
        {sheet=Load();var e=Array.Find(sheet.Entries,p=>p.Id==id);if(e==null)throw new Exception("Unknown content ID");var w=Object.FindFirstObjectByType<WorldMacroReviewController>();var p=e.Position+Quaternion.Euler(0,e.Yaw,0)*new Vector3(0,0,-15);p.y=Ground(p)+.04f;w.ResumeWalkAt(p,e.Yaw);return "Review position: "+e.Label;}
        public static string PlayChecks()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Play mode required");
            var p=Object.FindFirstObjectByType<WorldMacroContentPreview>();
            if(p==null||p.VisitedCount!=0)throw new Exception("Fresh preview session required");
            var results=new List<string>();
            void Check(bool ok,string name){results.Add((ok?"PASS ":"FAIL ")+name);}
            Check(!p.Inspect("not_a_content_id")&&p.VisitedCount==0,"unknown ID rejected");
            p.Inspect("cargo_delivery");Check(p.VisitedCount==0,"delivery blocked before contract");
            p.Inspect("cargo_contract");Check(p.VisitedCount==0,"contract blocked before Wangso");
            p.Inspect("wangso_w1");p.Inspect("cargo_contract");p.Inspect("cargo_delivery");
            Check(p.VisitedCount==3,"Wangso -> contract -> delivery preview sequence");
            p.Inspect("cargo_delivery");Check(p.VisitedCount==3,"repeated inspection is idempotent");
            p.Inspect("mine_inquiry");Check(p.VisitedCount==4&&!string.IsNullOrEmpty(p.LastFeedback),"evidence feedback available");
            Check(p.Points.Any(t=>!t.Visual.gameObject.activeSelf),"distant content culled by live Update");
            p.enabled=false;Check(p.FocusedId==null&&p.Points.All(t=>t.Visual.gameObject.activeSelf),"disable restores edit visibility and clears focus");p.enabled=true;
            string result=string.Join("\n",results)+"\nNOT_TESTED physical F key and user traversal; Inspect API exercised in Play.\n";
            File.WriteAllText(Output+"/play_checks.txt",result);return result;
        }
        public static string Repair()
        {
            Require();sheet=Load();var root=GameObject.Find(RootName);
            foreach(var e in sheet.Entries)
                if(e.Id=="sunken_store"||Physics.OverlapCapsule(e.Position+Vector3.up*.30f,e.Position+Vector3.up*1.47f,.28f,~0,QueryTriggerInteraction.Ignore).Any(c=>!c.transform.IsChildOf(root.transform)))e.Resolved=false;
            EditorUtility.SetDirty(sheet);return Install();
        }
        public static string Capture(string id)
        {
            Require();sheet=Load();bool interior=id.EndsWith("_interior");string key=interior?id.Substring(0,id.Length-9):id;
            var e=Array.Find(sheet.Entries,p=>p.Id==key);if(e==null)throw new Exception("Unknown content ID "+key);var q=Quaternion.Euler(0,e.Yaw,0);
            Vector3 eye=e.Position+q*(interior?new Vector3(0,1.65f,2):e.Kind==MacroContentKind.Dungeon?new Vector3(27,24,-20):new Vector3(7,3,-12));
            if(!interior)eye.y=Mathf.Max(eye.y,Ground(eye)+1.7f);
            var target=e.Position+q*(interior?new Vector3(0,1.5f,23):new Vector3(0,1,e.Kind==MacroContentKind.Dungeon?17:0));
            WorldMacroDressingProbe.Capture("content_"+id,true,eye.x,eye.y,eye.z,target.x,target.y,target.z);
            foreach(var ext in new[]{".png",".json"})File.Copy(WorldMacroBuilder.Output+"/Dressing/content_"+id+ext,Output+"/"+id+ext,true);
            return Output+"/"+id+".png";
        }
    }
}
