using Oheangbu.Data.World;
using UnityEngine;
namespace Oheangbu.App.World
{
    // #308 D308-18 (3) "걸을 수 있는 땅에서만 갱신" (SPEC-WORLD-FALL-RULE-308).
    // The walking fall reference (recentGroundHeight, WorldMacroPlaytestSession.Terrain.cs) follows the feet only while the motor is
    // grounded AND a support within the walk slope limit lies right under the feet. Contact with a steep face (CollisionFlags.Below on
    // a cliff) no longer refreshes it, so a long descent on ground that cannot be walked is a fall.
    // The probe is the motor's own ground probe (PlayerMotor.ProbeGround: same origin, radius, layers, slope filter), with the gap and
    // the slope limit read from the traversal profile. The rule's numbers live in the profile; the probe geometry below (radius x .85,
    // lift .08, floor .05, 24 hits) is PlayerMotor.ProbeGround's (PlayerMotor.Locomotion.cs) copied - if the motor's probe changes, change it here
    // and in CliffBoundary308.FallSupport1b (the editor rim probe) too.
    public sealed partial class WorldMacroPlaytestSession
    {
        readonly RaycastHit[] fallSupportHits308=new RaycastHit[24];
        /// <summary>Read-only views for checks and the Play harness.</summary>
        public float FallReferenceHeight308=>recentGroundHeight;
        public bool FallReferenceKnown308=>groundHeightKnown;
        /// <summary>The verdict of the last walking tick: did the reference follow the feet?</summary>
        public bool FallReferenceRefreshed308 {get;private set;}
        bool FallReferenceRefreshes308()
        {
            var rules=Traversal.Rules;var motor=Walker.Motor;bool grounded=motor.IsLocomotionGrounded;
            bool found=false;float normalY=0,gap=float.PositiveInfinity;var body=Walker.Body;
            if(grounded&&rules.FallReferenceWalkableOnly&&body!=null)
                found=FallSupport308(body,motor.LocomotionProfile!=null?motor.LocomotionProfile.GroundLayers:(LayerMask)~0,
                    FallReferenceRule308.EffectiveSlopeDeg(rules.FallReferenceMaxSlopeDeg,body.slopeLimit),rules.FallReferenceSupportGap,out normalY,out gap);
            return FallReferenceRefreshed308=FallReferenceRule308.Refreshes(rules.FallReferenceWalkableOnly,grounded,Walker.Seated,found,normalY,gap,
                rules.FallReferenceSupportGap,rules.FallReferenceMaxSlopeDeg,body!=null?body.slopeLimit:0);
        }
        // The nearest support under the feet whose normal passes the limit (any collider of the ground layers: terrain, built floors,
        // bridges, the 국 deck). A hit that starts inside the sphere (distance 0: no point, a made-up normal) is not a support.
        bool FallSupport308(CharacterController body,LayerMask layers,float limitDeg,float maxGap,out float normalY,out float gap)
        {
            normalY=0;gap=float.PositiveInfinity;
            var t=body.transform;Vector3 feet=t.TransformPoint(body.center)-t.up*(body.height*.5f);
            float radius=Mathf.Max(.05f,body.radius*.85f);
            var hits=fallSupportHits308;
            int n=Physics.SphereCastNonAlloc(feet+Vector3.up*(radius+.08f),radius,Vector3.down,hits,.08f+maxGap,layers,QueryTriggerInteraction.Ignore);
            if(n>=hits.Length){hits=Physics.SphereCastAll(feet+Vector3.up*(radius+.08f),radius,Vector3.down,.08f+maxGap,layers,QueryTriggerInteraction.Ignore);n=hits.Length;}
            bool found=false;
            for(int i=0;i<n;i++)
            {
                var h=hits[i];
                if(h.collider==null||h.distance<=0||h.transform==t||h.transform.IsChildOf(t))continue;
                if(!FallReferenceRule308.Walkable(h.normal.y,limitDeg))continue;
                float g=Vector3.Dot(feet-h.point,t.up);
                if(g<gap){gap=g;normalY=h.normal.y;found=true;}
            }
            return found;
        }
    }
}
