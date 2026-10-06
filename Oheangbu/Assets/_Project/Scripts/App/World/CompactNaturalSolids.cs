using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using Oheangbu.App.World.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch=System.Diagnostics.Stopwatch;

namespace Oheangbu.App.World
{
    // SPEC-NATURE-COLLISION-306 / D306: play-mode-only stand-in colliders for the instanced sheet trees (upright capsules),
    // rocks (box or sphere) and props (oriented boxes) near the player; grass and shrubs stay soft (Contact265 bend).
    // Referenced, not global: the scene wires it; Sources empty -> every CompactRebuildArtRenderer in this scene.
    // Visible solids only (SPEC-WORLD-MAP A2): a candidate lives only while a renderer that draws its sheet is enabled.
    // No NavMesh, no Rigidbody, nothing saved; edit mode never creates a collider.
    [DefaultExecutionOrder(-60)]
    public sealed class CompactNaturalSolids : MonoBehaviour, INaturalSolidPool306
    {
        public NaturalSolidProfileSO Profile;
        public CompactRebuildArtRenderer[] Sources=Array.Empty<CompactRebuildArtRenderer>();
        public bool DiscoverInScene=true;
        public WorldMacroCombatWalker Walker;
        public WorldMacroPalanquinController Vehicle;
        [Tooltip("Fallback focus when no walker is wired (review rigs).")]
        public Transform Focus;
        public CompactEnvironmentContact265 Contacts;
        // Perf counters (SPEC-NATURE-COLLISION-306 §perf).
        public int Active {get;private set;}
        public int PeakActive {get;private set;}
        public int PoolSize {get;private set;}
        public int Candidates {get;private set;}
        public int Cells {get;private set;}
        public int SheetCount {get;private set;}
        // Profile.SkipPlacements resolved in the last Prepare (placements left visual only) / entries whose Id is not in its sheet.
        public int SkipListed {get;private set;}
        public int SkipStale {get;private set;}
        public long Activations {get;private set;}
        public long Releases {get;private set;}
        public long Deferred {get;private set;}
        public long Queries {get;private set;}
        public double LastMs {get;private set;}
        public double PeakMs {get;private set;}
        public double PrepareMs {get;private set;}
        public bool Backlog {get;private set;}
        public bool LayerMissing {get;private set;}
        public int Layer {get;private set;}
        public bool Ready=>prepared;
        enum Shape:byte{Capsule,Box,Sphere}
        struct Kind{public Shape Shape;public Vector3 Centre,Size;public float Radius,Height;public bool Wood;public int Sheet;}
        sealed class SheetUnit{public WorldMacroDressingSheetSO Sheet;public readonly List<CompactRebuildArtRenderer> Renderers=new List<CompactRebuildArtRenderer>();public bool Live;public int Placements;}
        sealed class Entry{public GameObject Go;public Collider Collider;public Shape Shape;public int Candidate=-1,ActiveAt=-1;}
        readonly List<SheetUnit> units=new List<SheetUnit>();
        readonly List<CompactRebuildArtRenderer> watched=new List<CompactRebuildArtRenderer>();
        readonly List<WorldMacroDressingSheetSO> watchedSheet=new List<WorldMacroDressingSheetSO>();
        readonly List<Kind> kinds=new List<Kind>();
        readonly List<Entry> pool=new List<Entry>();
        readonly List<int> active=new List<int>();
        readonly Stack<int>[] free={new Stack<int>(),new Stack<int>(),new Stack<int>()};
        readonly HashSet<Collider> owned=new HashSet<Collider>();
        readonly Dictionary<long,int> cellIndex=new Dictionary<long,int>();
        Vector3[] position=Array.Empty<Vector3>();
        float[] reach=Array.Empty<float>();
        int[] kindOf=Array.Empty<int>(),placementOf=Array.Empty<int>(),slot=Array.Empty<int>(),stamp=Array.Empty<int>(),cellStart=Array.Empty<int>(),cellCount=Array.Empty<int>(),members=Array.Empty<int>();
        float maxReach,cell=8,nextQuery;int generation;bool prepared,dirty,force,warned;
        Vector3 lastFrom,lastTo;bool hasLast;
        GameObject root;NaturalSolidProfileSO fallback;
        NaturalSolidProfileSO P{get{if(Profile!=null)return Profile;if(fallback==null){fallback=ScriptableObject.CreateInstance<NaturalSolidProfileSO>();fallback.hideFlags=HideFlags.DontSave;}return fallback;}}
        public void Invalidate(){dirty=true;}
        // #307 phase 1 item 5: true when an EnsureAround(p) now could not enable anything within `margin` of p that is not enabled
        // already: prepared and clean, no deferred backlog, no sheet visibility change pending, and p lies within ActivateRadius - margin
        // (flat) and margin (height) of the last full Update query (which enabled every live candidate within ActivateRadius of it).
        // Read-only: no query, no activation, no force.
        public bool Covers(Vector3 p,float margin)
        {
            if(!Application.isPlaying||!isActiveAndEnabled||!prepared||dirty||Backlog||!hasLast||!Finite(p)||!(margin>=0))return false;
            var q=P;float flat=Flat(p,lastFrom,lastTo,out float y);
            if(flat>q.ActivateRadius-margin||Mathf.Abs(p.y-y)>margin)return false;
            for(int i=0;i<watched.Count;i++){var r=watched[i];if(r!=null&&r.Sheet!=watchedSheet[i])return false;}
            foreach(var u in units)
            {
                if(u.Sheet==null||(u.Sheet.FixedPlacements?.Length??0)!=u.Placements)return false;
                bool live=false;foreach(var r in u.Renderers)if(r!=null&&r.isActiveAndEnabled&&r.Sheet==u.Sheet){live=true;break;}
                if(live!=u.Live)return false;
            }
            return true;
        }
        public bool Owns(Collider c)=>c!=null&&owned.Contains(c);
        void OnEnable(){force=true;}
        void OnDisable(){ReleaseAll();Backlog=false;hasLast=false;}   // #307: Covers must not trust a query whose solids were released (Update behaves the same: OnEnable forces a full query)
        void OnDestroy(){ReleaseAll();if(root!=null)Destroy(root);if(fallback!=null)Destroy(fallback);pool.Clear();owned.Clear();for(int s=0;s<free.Length;s++)free[s].Clear();PoolSize=0;}
        // Session track: call before a respawn/teleport ground test so the arrival point already has its trunks.
        public bool EnsureAround(Vector3 feet)
        {
            if(!Application.isPlaying||!isActiveAndEnabled)return false;
            if(!prepared||dirty)Prepare();if(!prepared)return false;
            // Additive: nothing near the walker is released here (the candidate may be rejected); the next Update re-queries from the real focus.
            RefreshLive();if(!prepared)return false;Query(feet,feet,false,false);force=true;return true;
        }
        void Update()
        {
            if(!Application.isPlaying)return;
            if(!prepared||dirty)Prepare();if(!prepared)return;
            if(RefreshLive())force=true;if(!prepared)return;
            if(!TryFocus(out var from,out var to))return;
            float now=Time.unscaledTime,moved=hasLast?Vector3.Distance(from,lastFrom):float.PositiveInfinity,ahead=hasLast?Vector3.Distance(to,lastTo):0;
            if(!(force||Backlog||moved>=P.RequeryDistance||now>=nextQuery&&(moved>.05f||ahead>.5f)))return;
            Query(from,to,!force&&hasLast&&moved<P.ReleaseRadius);force=false;lastFrom=from;lastTo=to;hasLast=true;nextQuery=now+P.RequeryInterval;
        }
        bool TryFocus(out Vector3 from,out Vector3 to)
        {
            var walker=Walker!=null?Walker:Contacts!=null&&Contacts.Session!=null?Contacts.Session.Walker:null;
            var vehicle=Vehicle!=null?Vehicle:Contacts!=null?Contacts.Vehicle:null;
            if(walker!=null&&walker.Seated&&vehicle!=null)
            {
                from=vehicle.transform.position;var v=vehicle.Body!=null?vehicle.Body.linearVelocity:Vector3.zero;
                to=from+Vector3.ClampMagnitude(v*P.VehicleLookAheadSeconds,P.VehicleLookAheadMax);return true;
            }
            if(walker!=null&&walker.Body!=null){from=to=walker.Body.transform.position;return true;}
            if(Focus!=null){from=to=Focus.position;return true;}
            from=to=default;return false;
        }
        // True when a sheet's visibility changed. A renderer that swapped sheets forces a rebuild.
        bool RefreshLive()
        {
            for(int i=0;i<watched.Count&&!dirty;i++){var r=watched[i];if(r!=null&&r.Sheet!=watchedSheet[i])dirty=true;}
            for(int i=0;i<units.Count&&!dirty;i++){var u=units[i];if(u.Sheet==null||(u.Sheet.FixedPlacements?.Length??0)!=u.Placements)dirty=true;}
            // Rebuild now: a query on the old arrays would index placements that no longer exist. Prepare sets force and Live.
            if(dirty){Prepare();return prepared;}
            bool changed=false;
            foreach(var u in units)
            {
                bool live=false;foreach(var r in u.Renderers)if(r!=null&&r.isActiveAndEnabled&&r.Sheet==u.Sheet){live=true;break;}
                if(live!=u.Live){u.Live=live;changed=true;}
            }
            return changed;
        }
        public void Prepare()
        {
            if(!Application.isPlaying)return;
            long start=Stopwatch.GetTimestamp();ReleaseAll();var p=P;
            units.Clear();kinds.Clear();watched.Clear();watchedSheet.Clear();cellIndex.Clear();prepared=false;dirty=false;force=true;hasLast=false;Backlog=false;
            Layer=LayerMask.NameToLayer(p.LayerName);LayerMissing=Layer<0;if(LayerMissing){Layer=0;if(!warned){warned=true;Debug.LogWarning("CompactNaturalSolids: layer '"+p.LayerName+"' missing in TagManager; solids use Default.");}}
            foreach(var e in pool)if(e.Go!=null)e.Go.layer=Layer;
            var renderers=new List<CompactRebuildArtRenderer>();
            if(Sources!=null)foreach(var r in Sources)if(r!=null&&!renderers.Contains(r))renderers.Add(r);
            if(renderers.Count==0&&DiscoverInScene&&gameObject.scene.IsValid())foreach(var go in gameObject.scene.GetRootGameObjects())foreach(var r in go.GetComponentsInChildren<CompactRebuildArtRenderer>(true))if(!renderers.Contains(r))renderers.Add(r);
            foreach(var r in renderers)
            {
                watched.Add(r);watchedSheet.Add(r.Sheet);if(r.Sheet==null)continue;
                var u=units.Find(x=>x.Sheet==r.Sheet);if(u==null){u=new SheetUnit{Sheet=r.Sheet,Placements=r.Sheet.FixedPlacements?.Length??0};units.Add(u);}
                u.Renderers.Add(r);if(Contacts==null&&r.Contacts!=null)Contacts=r.Contacts;
            }
            var pos=new List<Vector3>(4096);var rad=new List<float>(4096);var kin=new List<int>(4096);var plc=new List<int>(4096);maxReach=0;
            int skipped=0,stale=0;
            for(int s=0;s<units.Count;s++)
            {
                var sheet=units[s].Sheet;var lookup=new Dictionary<string,int>();
                if(sheet.Prototypes!=null)foreach(var proto in sheet.Prototypes){if(proto==null||string.IsNullOrEmpty(proto.Id)||lookup.ContainsKey(proto.Id))continue;lookup.Add(proto.Id,KindFor(proto,s,p));}
                var placements=sheet.FixedPlacements??Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>();
                var skip=SkipSet(sheet,placements,p,ref stale);if(skip!=null)skipped+=skip.Count;
                for(int i=0;i<placements.Length;i++)
                {
                    var fp=placements[i];if(fp==null||fp.PrototypeId==null||!lookup.TryGetValue(fp.PrototypeId,out int k)||k<0||Skipped(fp.Id,p.SkipPlacementPrefixes)||skip!=null&&skip.Contains(i))continue;
                    if(!Usable(fp)||!Reach(kinds[k],fp,p,out float r,out float top)||top<p.MinSolidHeight)continue;
                    pos.Add(fp.Position);rad.Add(r);kin.Add(k);plc.Add(i);if(r>maxReach)maxReach=r;
                }
            }
            SkipListed=skipped;SkipStale=stale;
            position=pos.ToArray();reach=rad.ToArray();kindOf=kin.ToArray();placementOf=plc.ToArray();
            int count=position.Length;slot=new int[count];stamp=new int[count];for(int i=0;i<count;i++)slot[i]=-1;generation=0;
            // 8 m grid in CSR form: members sorted by cell, each cell a (start,count) span.
            cell=Mathf.Max(2,p.CellSize);var cellOf=new int[count];var sizes=new List<int>();
            for(int i=0;i<count;i++){long key=Key(Mathf.FloorToInt(position[i].x/cell),Mathf.FloorToInt(position[i].z/cell));if(!cellIndex.TryGetValue(key,out int c)){c=sizes.Count;cellIndex.Add(key,c);sizes.Add(0);}cellOf[i]=c;sizes[c]++;}
            cellStart=new int[sizes.Count];cellCount=new int[sizes.Count];members=new int[count];
            for(int c=0,first=0;c<sizes.Count;first+=sizes[c],c++)cellStart[c]=first;
            for(int i=0;i<count;i++){int c=cellOf[i];members[cellStart[c]+cellCount[c]++]=i;}
            Candidates=count;Cells=sizes.Count;SheetCount=units.Count;
            EnsureRoot();for(int s=0;s<3;s++)while(CountOf((Shape)s)<p.InitialPoolPerShape&&NeedsShape((Shape)s))free[s].Push(Create((Shape)s));
            foreach(var u in units){u.Live=false;foreach(var r in u.Renderers)if(r!=null&&r.isActiveAndEnabled&&r.Sheet==u.Sheet){u.Live=true;break;}}
            prepared=true;PrepareMs=Milliseconds(start);
        }
        int KindFor(WorldMacroDressingSheetSO.Prototype proto,int sheet,NaturalSolidProfileSO p){if(!MakeKind(proto,sheet,p,out var kind))return -1;kinds.Add(kind);return kinds.Count-1;}
        static bool Usable(WorldMacroDressingSheetSO.FixedPlacement fp)=>Finite(fp.Position)&&Finite(fp.Euler)&&fp.Scale>0&&!float.IsInfinity(fp.Scale);
        // Flat reach from the pivot and top above it (the query radius).
        static bool Reach(Kind kind,WorldMacroDressingSheetSO.FixedPlacement fp,NaturalSolidProfileSO p,out float r,out float top)
        {
            float scale=fp.Scale;
            if(kind.Shape==Shape.Capsule){r=Mathf.Clamp(kind.Radius*scale,p.TreeRadiusMin,p.TreeRadiusMax)+new Vector2(kind.Centre.x,kind.Centre.z).magnitude*scale;top=Mathf.Min(kind.Height*scale,p.TreeHeightMax);return true;}
            var flat=new Vector2(kind.Centre.x,kind.Centre.z).magnitude;
            // Box/sphere top above the pivot; the pivot sits on the ground in every sheet bake.
            if(kind.Shape==Shape.Box){r=(flat+new Vector2(kind.Size.x,kind.Size.z).magnitude*.5f)*scale;top=(kind.Centre.y+kind.Size.y*.5f)*scale;}
            else{r=(flat+kind.Radius)*scale;top=(kind.Centre.y+kind.Radius)*scale;}
            // Tilted props reach at most their half-diagonal sideways.
            if(fp.Euler.x!=0||fp.Euler.z!=0)r=Mathf.Max(r,(kind.Centre.magnitude+kind.Size.magnitude*.5f+kind.Radius)*scale);
            return true;
        }
        // Profile.SkipPlacements of this sheet as placement indices: Index while its Id still matches, else found by Id (stale = not found).
        static HashSet<int> SkipSet(WorldMacroDressingSheetSO sheet,WorldMacroDressingSheetSO.FixedPlacement[] placements,NaturalSolidProfileSO p,ref int stale)
        {
            HashSet<int> set=null;if(p.SkipPlacements==null)return null;Dictionary<string,int> byId=null;
            foreach(var e in p.SkipPlacements)
            {
                if(e==null||e.Sheet!=sheet||string.IsNullOrEmpty(e.PlacementId))continue;
                int i=e.Index>=0&&e.Index<placements.Length&&placements[e.Index]?.Id==e.PlacementId?e.Index:-1;
                if(i<0){if(byId==null){byId=new Dictionary<string,int>();for(int k=0;k<placements.Length;k++){var id=placements[k]?.Id;if(id!=null&&!byId.ContainsKey(id))byId.Add(id,k);}}if(!byId.TryGetValue(e.PlacementId,out i)){stale++;continue;}}
                (set??=new HashSet<int>()).Add(i);
            }
            return set;
        }
        // Edit-mode footprint of every placement the pool would give a collider (same rules as Prepare; nothing is created):
        // the NaturalSolids306 route scan. Centre = collider centre on the pivot plane; Box = HalfSize rectangle turned by Yaw, else a Radius circle.
        // Listed = already in Profile.SkipPlacements (still reported so the scan shows it).
        public struct Footprint306{public int Placement;public string Id,PrototypeId;public Vector3 Pivot,Centre;public float Radius,Yaw,Top;public Vector2 HalfSize;public bool Box,Tree,Listed;}
        public static int Footprints306(WorldMacroDressingSheetSO sheet,NaturalSolidProfileSO p,List<Footprint306> into)
        {
            if(sheet==null||p==null||into==null)return 0;int n=0,stale=0;var lookup=new Dictionary<string,Kind?>();
            if(sheet.Prototypes!=null)foreach(var proto in sheet.Prototypes){if(proto==null||string.IsNullOrEmpty(proto.Id)||lookup.ContainsKey(proto.Id))continue;lookup.Add(proto.Id,MakeKind(proto,0,p,out var k)?k:(Kind?)null);}
            var placements=sheet.FixedPlacements??Array.Empty<WorldMacroDressingSheetSO.FixedPlacement>();var skip=SkipSet(sheet,placements,p,ref stale);
            for(int i=0;i<placements.Length;i++)
            {
                var fp=placements[i];if(fp==null||fp.PrototypeId==null||!lookup.TryGetValue(fp.PrototypeId,out var kk)||kk==null||Skipped(fp.Id,p.SkipPlacementPrefixes))continue;
                var kind=kk.Value;if(!Usable(fp)||!Reach(kind,fp,p,out float reach,out float top)||top<p.MinSolidHeight)continue;
                var f=new Footprint306{Placement=i,Id=fp.Id,PrototypeId=fp.PrototypeId,Pivot=fp.Position,Top=top,Tree=kind.Shape==Shape.Capsule,Listed=skip!=null&&skip.Contains(i),Yaw=fp.Euler.y};
                float scale=fp.Scale;var off=Quaternion.Euler(fp.Euler)*(kind.Centre*scale);off.y=0;f.Centre=fp.Position+off;
                if(kind.Shape==Shape.Capsule)f.Radius=Mathf.Clamp(kind.Radius*scale,p.TreeRadiusMin,p.TreeRadiusMax);
                else if(kind.Shape==Shape.Sphere)f.Radius=kind.Radius*scale;
                // Upright box: the exact rectangle; a tilted one keeps the conservative reach circle less the centre offset.
                else if(fp.Euler.x==0&&fp.Euler.z==0){f.Box=true;f.HalfSize=new Vector2(kind.Size.x,kind.Size.z)*.5f*scale;f.Radius=f.HalfSize.magnitude;}
                else f.Radius=Mathf.Max(.01f,reach-off.magnitude);
                into.Add(f);n++;
            }
            return n;
        }
        static bool MakeKind(WorldMacroDressingSheetSO.Prototype proto,int sheet,NaturalSolidProfileSO p,out Kind kind)
        {
            var c=proto.Category;kind=default;
            if(c==WorldMacroDressingSheetSO.Kind.Grass||c==WorldMacroDressingSheetSO.Kind.Shrub||c==WorldMacroDressingSheetSO.Kind.Prop&&!p.IncludeProps)return false;
            if(Contains(proto.Id,p.SkipPrototypeTokens))return false;
            kind=new Kind{Sheet=sheet};
            if(c==WorldMacroDressingSheetSO.Kind.Tree)
            {
                if(!(proto.Size.y>0)||!(proto.Radius>0))return false;
                kind.Shape=Shape.Capsule;kind.Radius=proto.Radius;kind.Height=proto.Size.y;kind.Wood=true;
                // The bake recentres each mesh on its bounds (Part.Local translation); the source pivot, i.e. the trunk base, sits at that translation.
                if(p.TrunkAtMeshOrigin&&proto.Lods!=null&&proto.Lods.Length>0&&proto.Lods[0]?.Parts!=null&&proto.Lods[0].Parts.Length>0&&proto.Lods[0].Parts[0]!=null)
                {var o=proto.Lods[0].Parts[0].Local.GetColumn(3);if(float.IsFinite(o.x)&&float.IsFinite(o.z))kind.Centre=new Vector3(o.x,0,o.z);}
            }
            else
            {
                if(!LocalBounds(proto,out var b))return false;
                kind.Wood=c==WorldMacroDressingSheetSO.Kind.Prop&&Contains(proto.Id,p.WoodPropTokens);
                if(c==WorldMacroDressingSheetSO.Kind.Rock&&p.Rocks==NaturalSolidProfileSO.RockShape.Sphere){kind.Shape=Shape.Sphere;kind.Centre=b.center;kind.Radius=(b.extents.x+b.extents.y+b.extents.z)/3*p.BoxShrink;}
                else{kind.Shape=Shape.Box;kind.Centre=b.center;kind.Size=b.size*p.BoxShrink;}
            }
            return true;
        }
        // LOD0 render bounds in prototype space (Part.Local applied); Size box on the pivot when LOD0 carries no mesh.
        static bool LocalBounds(WorldMacroDressingSheetSO.Prototype proto,out Bounds bounds)
        {
            bounds=default;bool any=false;
            if(proto.Lods!=null&&proto.Lods.Length>0&&proto.Lods[0]?.Parts!=null)foreach(var part in proto.Lods[0].Parts)
            {
                if(part==null||part.Mesh==null)continue;var mb=part.Mesh.bounds;
                for(int k=0;k<8;k++)
                {
                    var corner=part.Local.MultiplyPoint3x4(mb.center+Vector3.Scale(mb.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));
                    if(!Finite(corner))continue;if(!any){bounds=new Bounds(corner,Vector3.zero);any=true;}else bounds.Encapsulate(corner);
                }
            }
            if(!any&&proto.Size.x>0&&proto.Size.y>0&&proto.Size.z>0){bounds=new Bounds(Vector3.up*proto.Size.y*.5f,proto.Size);any=true;}
            return any&&bounds.size.x>0&&bounds.size.y>0&&bounds.size.z>0;
        }
        void Query(Vector3 from,Vector3 to,bool budgeted,bool release=true)
        {
            using(Perf307Markers.NaturalSolidsQuery.Auto()){long start=Stopwatch.GetTimestamp();var p=P;Queries++;generation++;if(generation==int.MaxValue){generation=1;Array.Clear(stamp,0,stamp.Length);}
            float act=p.ActivateRadius,rel=Mathf.Max(act,p.ReleaseRadius),vr=p.VerticalReach,vrel=vr+p.VerticalHysteresis,urgent=p.UrgentRadius;
            int budget=p.ActivationsPerTick,used=0;bool changed=false,backlog=false;
            float pad=act+maxReach;
            int x0=Mathf.FloorToInt((Mathf.Min(from.x,to.x)-pad)/cell),x1=Mathf.FloorToInt((Mathf.Max(from.x,to.x)+pad)/cell);
            int z0=Mathf.FloorToInt((Mathf.Min(from.z,to.z)-pad)/cell),z1=Mathf.FloorToInt((Mathf.Max(from.z,to.z)+pad)/cell);
            for(int cx=x0;cx<=x1;cx++)for(int cz=z0;cz<=z1;cz++)
            {
                if(!cellIndex.TryGetValue(Key(cx,cz),out int c))continue;
                for(int m=cellStart[c],end=m+cellCount[c];m<end;m++)
                {
                    int i=members[m];if(!units[kinds[kindOf[i]].Sheet].Live)continue;
                    float d=Flat(position[i],from,to,out float y)-reach[i];if(d>act||Mathf.Abs(position[i].y-y)>vr)continue;
                    stamp[i]=generation;if(slot[i]>=0)continue;
                    if(budgeted&&d>urgent&&used>=budget){backlog=true;Deferred++;continue;}
                    Activate(i);used++;changed=true;
                }
            }
            if(release)for(int a=active.Count-1;a>=0;a--)
            {
                var e=pool[active[a]];int i=e.Candidate;if(stamp[i]==generation)continue;
                float d=Flat(position[i],from,to,out float y)-reach[i];
                if(units[kinds[kindOf[i]].Sheet].Live&&d<=rel&&Mathf.Abs(position[i].y-y)<=vrel)continue;
                Release(active[a]);changed=true;
            }
            if(changed)Physics.SyncTransforms();
            if(release)Backlog=backlog;Active=active.Count;if(Active>PeakActive)PeakActive=Active;
            LastMs=Milliseconds(start);if(LastMs>PeakMs)PeakMs=LastMs;}
        }
        void Activate(int i)
        {
            var kind=kinds[kindOf[i]];var fp=units[kind.Sheet].Sheet.FixedPlacements[placementOf[i]];var p=P;
            int id=free[(int)kind.Shape].Count>0?free[(int)kind.Shape].Pop():Create(kind.Shape);var e=pool[id];
            var t=e.Go.transform;float scale=fp.Scale;
            switch(e.Collider)
            {
                case CapsuleCollider cap:
                {
                    float r=Mathf.Clamp(kind.Radius*scale,p.TreeRadiusMin,p.TreeRadiusMax),h=Mathf.Max(r,Mathf.Min(kind.Height*scale,p.TreeHeightMax));
                    var trunk=Quaternion.Euler(fp.Euler)*(kind.Centre*scale);trunk.y=0;
                    // Full radius from the pivot up (bottom cap below the ground), so the walker meets a wall, not a climbable dome.
                    t.SetPositionAndRotation(position[i]+trunk,Quaternion.identity);t.localScale=Vector3.one;cap.direction=1;cap.radius=r;cap.height=h+r;cap.center=Vector3.up*(h-r)*.5f;break;
                }
                case BoxCollider box:t.SetPositionAndRotation(position[i],Quaternion.Euler(fp.Euler));t.localScale=Vector3.one*scale;box.center=kind.Centre;box.size=kind.Size;break;
                case SphereCollider sphere:t.SetPositionAndRotation(position[i],Quaternion.Euler(fp.Euler));t.localScale=Vector3.one*scale;sphere.center=kind.Centre;sphere.radius=kind.Radius;break;
            }
            e.Candidate=i;slot[i]=id;e.ActiveAt=active.Count;active.Add(id);e.Collider.enabled=true;Activations++;
            if(Contacts!=null)Contacts.RegisterDynamic(e.Collider,kind.Wood);
        }
        void Release(int id)
        {
            var e=pool[id];if(e.Candidate<0)return;
            e.Collider.enabled=false;slot[e.Candidate]=-1;e.Candidate=-1;
            int at=e.ActiveAt,last=active.Count-1;if(at!=last){active[at]=active[last];pool[active[at]].ActiveAt=at;}active.RemoveAt(last);e.ActiveAt=-1;
            free[(int)e.Shape].Push(id);Releases++;
        }
        void ReleaseAll(){for(int a=active.Count-1;a>=0;a--)Release(active[a]);Active=0;}
        void EnsureRoot()
        {
            if(root!=null)return;
            root=new GameObject("NaturalSolids306_Pool"){hideFlags=HideFlags.DontSave};
            if(gameObject.scene.IsValid()&&gameObject.scene!=root.scene)SceneManager.MoveGameObjectToScene(root,gameObject.scene);
        }
        int Create(Shape shape)
        {
            EnsureRoot();var go=new GameObject("NatureSolid_"+shape+"_"+pool.Count){hideFlags=HideFlags.DontSave,layer=Layer};
            go.transform.SetParent(root.transform,false);
            Collider c=shape==Shape.Capsule?go.AddComponent<CapsuleCollider>():shape==Shape.Box?(Collider)go.AddComponent<BoxCollider>():go.AddComponent<SphereCollider>();
            c.enabled=false;c.hideFlags=HideFlags.DontSave;owned.Add(c);
            pool.Add(new Entry{Go=go,Collider=c,Shape=shape});PoolSize=pool.Count;return pool.Count-1;
        }
        int CountOf(Shape shape){int n=0;foreach(var e in pool)if(e.Shape==shape)n++;return n;}
        bool NeedsShape(Shape shape){foreach(var k in kinds)if(k.Shape==shape)return true;return false;}
        static bool Skipped(string id,string[] prefixes){if(id==null||prefixes==null)return false;foreach(var x in prefixes)if(!string.IsNullOrEmpty(x)&&id.StartsWith(x,StringComparison.Ordinal))return true;return false;}
        static bool Contains(string id,string[] tokens){if(id==null||tokens==null)return false;foreach(var x in tokens)if(!string.IsNullOrEmpty(x)&&id.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
        static long Key(int x,int z)=>((long)x<<32)^(uint)z;
        // Flat distance from p to segment a-b; y = segment height at the closest point.
        static float Flat(Vector3 p,Vector3 a,Vector3 b,out float y)
        {
            float dx=b.x-a.x,dz=b.z-a.z,l=dx*dx+dz*dz,t=l<.001f?0:Mathf.Clamp01(((p.x-a.x)*dx+(p.z-a.z)*dz)/l);
            float x=a.x+dx*t-p.x,z=a.z+dz*t-p.z;y=a.y+(b.y-a.y)*t;return Mathf.Sqrt(x*x+z*z);
        }
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        static double Milliseconds(long start)=>(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
    }
}
