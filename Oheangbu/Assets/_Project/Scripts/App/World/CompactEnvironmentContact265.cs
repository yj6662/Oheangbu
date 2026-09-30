using System;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Data.World;
using Oheangbu.App.World.Vehicle;
namespace Oheangbu.App.World
{
    // Candidate presentation only. Fixed blockers remain available to every actor and projectile.
    public sealed class CompactEnvironmentContact265 : MonoBehaviour
    {
        [Serializable] public struct Surface { public Collider Collider; public bool Wood; }
        public WorldMacroPlaytestSession Session;
        public CompactRebuildArtRenderer Art;
        public CompactGrassField266 GrassField;
        public CompactSoundscape255 Sound;
        public WorldMacroPalanquinController Vehicle;
        public Surface[] Surfaces=Array.Empty<Surface>();
        public Mesh ChipMesh;
        public Material ChipMaterial;
        public const int ChipLimit=8, PulseLimit=8;
        public int ContactsAccepted {get;private set;}
        public int SoftQueries {get;private set;}
        public int ActiveChips {get;private set;}
        public readonly Vector4[] BendPoints=new Vector4[PulseLimit];
        readonly Vector3[] pulsePositions=new Vector3[PulseLimit];
        readonly float[] pulseTimes=new float[PulseLimit];
        readonly float[] pulseRadii=new float[PulseLimit];
        readonly Dictionary<Vector2Int,List<Vector3>> cells=new Dictionary<Vector2Int,List<Vector3>>();
        readonly Dictionary<Collider,bool> surfaces=new Dictionary<Collider,bool>();
        // #306 pooled stand-ins (CompactNaturalSolids); kept apart so Prepare() does not forget them.
        readonly Dictionary<Collider,bool> dynamicSurfaces=new Dictionary<Collider,bool>();
        readonly Dictionary<Collider,float> nextContact=new Dictionary<Collider,float>();
        readonly Rigidbody[] chips=new Rigidbody[ChipLimit];
        readonly float[] retireAt=new float[ChipLimit];
        float nextSoft;int pulseCursor;Vector3 previous;bool tracked;
        static Vector2Int Cell(Vector3 p)=>new Vector2Int(Mathf.FloorToInt(p.x/32),Mathf.FloorToInt(p.z/32));
        public void Prepare()
        {
            cells.Clear();surfaces.Clear();nextContact.Clear();
            foreach(var s in Surfaces)if(s.Collider!=null)surfaces[s.Collider]=s.Wood;
            if(Art!=null&&Art.Sheet!=null){var soft=new HashSet<string>();foreach(var p in Art.Sheet.Prototypes)if(p.Category==WorldMacroDressingSheetSO.Kind.Grass||p.Category==WorldMacroDressingSheetSO.Kind.Shrub)soft.Add(p.Id);
                foreach(var p in Art.Sheet.FixedPlacements)if(soft.Contains(p.PrototypeId)){var key=Cell(p.Position);if(!cells.TryGetValue(key,out var bucket)){bucket=new List<Vector3>();cells.Add(key,bucket);}bucket.Add(p.Position);}}
            for(int i=0;i<PulseLimit;i++){pulseTimes[i]=float.NegativeInfinity;BendPoints[i]=Vector4.zero;}tracked=false;
        }
        void Start(){Prepare();CreateChips();}
        public bool HasSoftVegetation(Vector3 point,float radius)
        {
            if(GrassField!=null&&GrassField.HasGrass(point,radius+1.2f))return true;
            var c=Cell(point);float sq=radius*radius;int reach=Mathf.CeilToInt(radius/32);
            for(int x=-reach;x<=reach;x++)for(int z=-reach;z<=reach;z++)if(cells.TryGetValue(c+new Vector2Int(x,z),out var list))foreach(var p in list)
                if(Mathf.Abs(p.y-point.y)<1.8f&&new Vector2(p.x-point.x,p.z-point.z).sqrMagnitude<sq)return true;
            return false;
        }
        public int DynamicSurfaces=>dynamicSurfaces.Count;
        // A reused pool collider re-registers with its new material; a released one may stay (it is disabled, so no contact).
        public void RegisterDynamic(Collider collider,bool wood){if(collider!=null)dynamicSurfaces[collider]=wood;}
        public void UnregisterDynamic(Collider collider){if(collider!=null){dynamicSurfaces.Remove(collider);nextContact.Remove(collider);}}
        public bool TryContact(Collider other,Vector3 point,float speed,float now,bool present=true)
        {
            if(other==null||!(surfaces.TryGetValue(other,out bool wood)||dynamicSurfaces.TryGetValue(other,out wood))||speed<.65f||!float.IsFinite(speed))return false;
            if(nextContact.TryGetValue(other,out float next)&&now<next)return false;
            nextContact[other]=now+.55f;ContactsAccepted++;
            if(present){Sound?.Emit(wood?"contact_wood265":"contact_stone265",point,Mathf.Clamp(speed*.12f,.12f,.5f));if(!wood&&speed>1.6f)KickChip(point,now);}
            return true;
        }
        public void AddPulse(Vector3 point,float radius,float now){pulsePositions[pulseCursor]=point;pulseTimes[pulseCursor]=now;pulseRadii[pulseCursor]=radius;BendPoints[pulseCursor]=new Vector4(point.x,point.y,point.z,radius);pulseCursor=(pulseCursor+1)%PulseLimit;}
        public void TickPulses(float now){for(int i=0;i<PulseLimit;i++){var p=pulsePositions[i];float life=Mathf.Clamp01(1-(now-pulseTimes[i])/1.25f);BendPoints[i]=new Vector4(p.x,p.y,p.z,life*pulseRadii[i]);}}
        void LateUpdate()
        {
            if(Session==null||Session.Walker==null)return;
            if(Session.GameplayInputBlocked){tracked=false;Array.Clear(BendPoints,0,BendPoints.Length);return;}
            var feet=Session.Walker.Seated&&Vehicle!=null?Vehicle.transform.position:Session.Walker.Body.transform.position;
            float travel=tracked?Vector3.Distance(feet,previous):0;previous=feet;tracked=true;TickPulses(Time.time);
            if(travel>8){Array.Clear(BendPoints,0,BendPoints.Length);for(int i=0;i<PulseLimit;i++)pulseTimes[i]=float.NegativeInfinity;return;}
            if(travel>.003f&&Time.time>=nextSoft){nextSoft=Time.time+.18f;SoftQueries++;float radius=Session.Walker.Seated?2.6f:1.5f;if(HasSoftVegetation(feet,radius)){AddPulse(feet,radius,Time.time);Sound?.Emit("contact_leaf265",feet,.25f);}}
            RetireChips(Time.time);
        }
        public void CreateChips()
        {
            if(ChipMesh==null||ChipMaterial==null)return;
            for(int i=0;i<ChipLimit;i++){if(chips[i]!=null)continue;var g=new GameObject("ContactChip265_"+i,typeof(MeshFilter),typeof(MeshRenderer),typeof(SphereCollider),typeof(Rigidbody));g.transform.SetParent(transform,false);g.GetComponent<MeshFilter>().sharedMesh=ChipMesh;g.GetComponent<Renderer>().sharedMaterial=ChipMaterial;
                var b=ChipMesh.bounds;g.transform.localScale=Vector3.one*(.12f/Mathf.Max(.01f,b.size.magnitude));var c=g.GetComponent<SphereCollider>();c.center=b.center;c.radius=b.extents.magnitude*.65f;
                var body=g.GetComponent<Rigidbody>();body.mass=.04f;body.interpolation=RigidbodyInterpolation.None;body.isKinematic=true;body.useGravity=true;chips[i]=body;
                if(Session!=null){Physics.IgnoreCollision(c,Session.Walker.Body);foreach(var actor in Session.Actors)if(actor!=null)foreach(var ac in actor.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(c,ac);}
                if(Vehicle!=null)foreach(var vc in Vehicle.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(c,vc);
                for(int j=0;j<i;j++)if(chips[j]!=null)Physics.IgnoreCollision(c,chips[j].GetComponent<Collider>());g.SetActive(false);
            }
        }
        public bool KickChip(Vector3 point,float now)
        {
            for(int i=0;i<ChipLimit;i++)if(chips[i]!=null&&!chips[i].gameObject.activeSelf){var b=chips[i];b.transform.position=point+Vector3.up*.18f;b.gameObject.SetActive(true);b.isKinematic=false;b.linearVelocity=new Vector3(Mathf.Sin(i*2.4f)*.35f,.65f,Mathf.Cos(i*2.4f)*.35f);retireAt[i]=now+2;ActiveChips++;return true;}return false;
        }
        public void RetireChips(float now){for(int i=0;i<ChipLimit;i++)if(chips[i]!=null&&chips[i].gameObject.activeSelf&&(now>=retireAt[i]||chips[i].IsSleeping())){chips[i].isKinematic=true;chips[i].gameObject.SetActive(false);ActiveChips--;}}
        void OnDisable(){Array.Clear(BendPoints,0,BendPoints.Length);RetireChips(float.MaxValue);}
    }
}
