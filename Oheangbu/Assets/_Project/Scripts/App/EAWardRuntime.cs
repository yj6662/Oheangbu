using System;
using Oheangbu.Combat;
using Oheangbu.App.SpellVFX120;
using UnityEngine;

namespace Oheangbu.App
{
    // Fixed area protection. It never registers a parry, refunds ink or damages enemies.
    public sealed class EAWardRuntime : IDisposable
    {
        readonly EAWardProfileSO profile;
        readonly PlayerVitals vitals;
        readonly CombatLoopWiring wiring;
        readonly Func<float> clock;
        readonly Func<IncomingDamageKind,float> modifier;
        RaycastHit[] hits = new RaycastHit[32];
        float began = float.NegativeInfinity;
        bool disposed;
        GameObject visual;
        Vfx120Profile visualProfile;
        public Vector3 Centre { get; private set; }
        public char Letter { get; private set; }
        public bool Available => !disposed && profile.Valid && vitals.Hp01 > 0 && wiring.isActiveAndEnabled;
        public bool HasVisual => visual != null;
        public bool Active(float now) => Available && now >= began + profile.Formation && now < began + profile.Duration - profile.Fade;
        public bool Exists(float now) => Available && now >= began && now < began + profile.Duration;
        public EAWardRuntime(EAWardProfileSO profile, PlayerVitals vitals, CombatLoopWiring wiring, Func<float> clock = null)
        {
            if(profile==null || !profile.Valid || vitals==null || wiring==null)throw new ArgumentException("Valid ward owners required");
            this.profile=profile;this.vitals=vitals;this.wiring=wiring;this.clock=clock??(()=>Time.time);
            modifier=IncomingScale;vitals.BindIncomingDamageScale(modifier);
        }
        public bool TryPrepare(char letter, out Vector3 centre)
        {
            centre=default;if(!Available || "구누무수우".IndexOf(letter)<0)return false;
            var foot=wiring.SummonPlayer.position;
            int count=ScenePhysicsQuery.RaycastAll(wiring.gameObject.scene,foot+Vector3.up*.5f,Vector3.down,2f,~0,ref hits);
            float nearest=float.PositiveInfinity;bool found=false;
            for(int i=0;i<count;i++)
            {
                var h=hits[i];if(h.normal.y<.5f||h.distance>=nearest||h.transform.IsChildOf(wiring.SummonPlayer)
                    ||h.collider.GetComponentInParent<EnemyVitals>()!=null)continue;
                nearest=h.distance;centre=h.point;found=true;
            }
            return found;
        }
        public bool Activate(char letter, Vector3 centre, float now)
        {
            int index="구누무수우".IndexOf(letter);
            if(!Available||index<0||!float.IsFinite(now)||!Finite(centre))return false;
            Clear();Centre=centre;Letter=letter;began=now;
            if(profile.PresentationPrefabs==null||index>=profile.PresentationPrefabs.Length||profile.PresentationPrefabs[index]==null)return true;
            visual=UnityEngine.Object.Instantiate(profile.PresentationPrefabs[index]);visual.name="EA_Ward_"+letter;
            var fx=visual.GetComponent<Vfx120Effect>();
            if(fx==null||fx.Profile==null){ClearVisual();return true;}
            visualProfile=UnityEngine.Object.Instantiate(fx.Profile);
            visualProfile.Duration=profile.Duration;visualProfile.WardRadius=profile.Radius;
            visualProfile.WardHeight=profile.Height;visualProfile.WardFormation=profile.Formation;visualProfile.WardFade=profile.Fade;
            // Stay on the player's floor in caves; do not pick a roof or the terrain above the tunnel.
            visualProfile.WardGroundProbeHeight=.6f;visualProfile.WardGroundProbeDepth=3f;
            fx.Profile=visualProfile;fx.PreviewControlled=false;fx.DemonstrationCues=false;
            fx.Begin(centre,null,centre+wiring.SummonPlayer.forward,fx.Profile.Pigment);
            return true;
        }
        public bool Contains(Vector3 point, float now)
        {
            if(!Active(now)||!Finite(point))return false;
            var delta=point-Centre;
            if(delta.y < -.3f || delta.y > profile.Height || new Vector2(delta.x,delta.z).sqrMagnitude > profile.Radius*profile.Radius)return false;
            // A wall through the circle cannot protect somebody in the next room.
            var from=Centre+Vector3.up*.6f;var to=point+Vector3.up*.6f;var ray=to-from;
            if(ray.sqrMagnitude<.0001f)return true;
            int count=ScenePhysicsQuery.RaycastAll(wiring.gameObject.scene,from,ray,ray.magnitude,~0,ref hits);
            for(int i=0;i<count;i++)
                if(!hits[i].transform.IsChildOf(wiring.SummonPlayer)&&hits[i].collider.GetComponentInParent<EnemyVitals>()==null)return false;
            return true;
        }
        float IncomingScale(IncomingDamageKind kind) =>
            (kind==IncomingDamageKind.ElementalRanged||kind==IncomingDamageKind.ElementalMelee) && Contains(wiring.SummonPlayer.position,clock())
            ? 1-profile.ElementalReduction : 1;
        public void Tick(float now){if(!Available||now>=began+profile.Duration)Clear();}
        void ClearVisual()
        {
            if(visual!=null)UnityEngine.Object.Destroy(visual);
            if(visualProfile!=null)UnityEngine.Object.Destroy(visualProfile);
            visual=null;visualProfile=null;
        }
        public void Clear(){began=float.NegativeInfinity;Letter=default;ClearVisual();}
        public void Dispose(){if(disposed)return;Clear();disposed=true;vitals.UnbindIncomingDamageScale(modifier);}
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
    }
}
