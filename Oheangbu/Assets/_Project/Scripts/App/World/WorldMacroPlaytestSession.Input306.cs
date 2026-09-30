using Oheangbu.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Oheangbu.App.World
{
    // #306 §2-2: the [F] that closes a dialogue must never reopen one. The live UI can run on the lobby's DontDestroyOnLoad
    // GameplayRuntimeStateSO and restores timeScale on the closing frame, so the session accepts F only as a fresh press:
    // disarmed while blocked, re-armed on a frame where interaction is open and F is up, and never on the first open frame.
    // Same-target repeats wait Content.InteractRepeatCooldownSeconds [TEST]. Interact(id) itself stays ungated (editor checks call it).
    public sealed partial class WorldMacroPlaytestSession
    {
        bool interactArmed,interactPressFresh;int interactOpenFrame=-1;float interactOpenAt=float.NegativeInfinity,lastInteractAt=float.NegativeInfinity;string lastInteractTarget;
        public bool InteractArmed=>interactArmed;
        // Call when the live UI owns a different state SO (PauseCoordinator.SynchronizeRuntimeState / PlaytestUiRoot.BindScene).
        public void BindRuntimeState(GameplayRuntimeStateSO state){if(state!=null)RuntimeState=state;DisarmInteract();}
        void DisarmInteract(){interactArmed=false;interactPressFresh=false;interactOpenFrame=-1;}
        void TrackInteractArm()
        {
            var k=Keyboard.current;bool held=k!=null&&k.fKey.isPressed,pressed=k!=null&&k.fKey.wasPressedThisFrame;
            if(interactOpenFrame<0){interactOpenFrame=Time.frameCount;interactOpenAt=Time.unscaledTime;}
            interactPressFresh=interactArmed&&pressed&&Time.frameCount>interactOpenFrame;
            interactArmed=!held;
        }
        bool TakeFreshInteractPress(string target)
        {
            if(!interactPressFresh)return false;interactPressFresh=false;
            float cooldown=Content!=null?Mathf.Max(0,Content.InteractRepeatCooldownSeconds):0;
            if(target!=null&&target==lastInteractTarget&&Time.unscaledTime-Mathf.Max(interactOpenAt,lastInteractAt)<cooldown)return false;
            lastInteractTarget=target;lastInteractAt=Time.unscaledTime;return true;
        }

        // #306 §2-6 hook: near-field natural solids (CompactNaturalSolids). The pool tracks the walker/vehicle on its own Update;
        // the session only guarantees trunks exist at a point before it tests or uses that point. Explicit reference wins;
        // otherwise the first pool in this scene. A miss is re-resolved only on Teleport (rare), never per frame.
        [Tooltip("#306 near-field natural collider pool (a component implementing INaturalSolidPool306). Empty = scene lookup; none = no nature collision.")]
        public MonoBehaviour NaturalSolids;
        INaturalSolidPool306 naturalSolids;bool naturalSolidsResolved,naturalCoveredKnown;Vector3 naturalCovered;
        INaturalSolidPool306 ResolveNaturalSolids(bool retry)
        {
            if(naturalSolidsResolved&&(naturalSolids!=null||!retry))return naturalSolids;naturalSolidsResolved=true;
            naturalSolids=NaturalSolids as INaturalSolidPool306;
            if(naturalSolids==null&&NaturalSolids!=null)Debug.LogWarning("[WorldMacroPlaytest] NaturalSolids does not implement INaturalSolidPool306: "+NaturalSolids.name);
            if(naturalSolids==null&&gameObject.scene.IsValid())
                foreach(var root in gameObject.scene.GetRootGameObjects()){var found=root.GetComponentInChildren<INaturalSolidPool306>(true);if(found!=null){naturalSolids=found;break;}}
            return naturalSolids;
        }
        static bool Live(INaturalSolidPool306 pool)=>Application.isPlaying&&(pool is Object o?o!=null&&(!(o is Behaviour b)||b.isActiveAndEnabled):pool!=null);
        static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        float NaturalProbeReuse=>Content!=null?Mathf.Max(0,Content.NaturalSolidProbeReuseMeters):0;
        Vector3 NaturalFocus=>Walker==null||Walker.Body==null?naturalCovered:Walker.Seated&&DemoEscortSeat!=null&&DemoEscortSeat.Vehicle!=null?DemoEscortSeat.Vehicle.transform.position:Walker.Body.transform.position;
        bool EnsureNaturalAt(INaturalSolidPool306 pool,Vector3 feet){if(!pool.EnsureAround(feet))return false;naturalCovered=feet;naturalCoveredKnown=true;return true;}
        // Before a TrySafeFeet overlap test. Per-frame probes at the walker (and roadside probes while seated) reuse the last ensured point within
        // Content.NaturalSolidProbeReuseMeters [TEST]; a probe away from the walker returns true -> RestoreNaturalFocus after the test,
        // because the pool's EnsureAround re-centres its active set (it releases solids outside its radius of the probed point).
        bool ProbeNaturalSolids(Vector3 candidate)
        {
            if(!Finite(candidate))return false;var pool=ResolveNaturalSolids(false);if(!Live(pool))return false;
            float reuse=NaturalProbeReuse;bool near=Walker==null||Walker.Body==null||Vector3.Distance(candidate,NaturalFocus)<=reuse;
            if(near&&(Walker!=null&&Walker.Seated||naturalCoveredKnown&&Vector3.Distance(candidate,naturalCovered)<=reuse))return false;   // seated: the pool follows the vehicle corridor itself
            return EnsureNaturalAt(pool,candidate)&&!near;
        }
        void RestoreNaturalFocus(){var pool=ResolveNaturalSolids(false);if(Live(pool)&&Walker!=null&&Walker.Body!=null)EnsureNaturalAt(pool,NaturalFocus);}
        // Teleport: always ensure the arrival point (death, escort, terrain recovery, inn). A pool added after Start is picked up here.
        void EnsureNaturalSolidsForTeleport(Vector3 feet){if(!Finite(feet))return;var pool=ResolveNaturalSolids(true);if(Live(pool))EnsureNaturalAt(pool,feet);}
    }
}
