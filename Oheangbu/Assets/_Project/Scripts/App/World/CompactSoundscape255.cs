using System;
using System.Collections.Generic;
using Oheangbu.App.Demo;
using Oheangbu.App.World.UI;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Presentation only: observes committed state and physical movement, never advances progress.
    [DefaultExecutionOrder(2300)]
    public sealed class CompactSoundscape255 : MonoBehaviour
    {
        public CompactSoundPalette255 Palette;
        public WorldMacroPlaytestSession Session;
        public WorldMapBakedDataSO Map;
        public Transform[] Hearths = Array.Empty<Transform>();
        public int Played { get; private set; }
        public string LastCue { get; private set; }
        public readonly Dictionary<string,int> Counts = new Dictionary<string,int>();
        WorldMacroAudioVoicePool pool;
        UserSettingsService settings;
        PlayerVitals vitals;
        LockOn target;
        BrushStrokeFeedAdapter brush;
        DemoEscortPresentation escort;
        WorldMacroInnRestPresentation[] rests;
        readonly Dictionary<int,int> restPhase = new Dictionary<int,int>();
        readonly Dictionary<string,float> cooldowns = new Dictionary<string,float>();
        readonly List<Action> detach = new List<Action>();
        readonly List<EnemyEdge> enemies = new List<EnemyEdge>();
        readonly HashSet<EnemyVitals> observedDeaths298 = new HashSet<EnemyVitals>();
        readonly HashSet<EnemyVitals> observedWindups298 = new HashSet<EnemyVitals>();
        readonly bool[] looping = new bool[4];
        sealed class EnemyEdge { public EnemyController Actor; public bool Windup, Active; public Vector3 Previous;public float Walked; }
        Vector3 previous,companionPrevious;float companionWalked;bool companionTracked;
        float walked, nextAmbient, nextSettings;
        int jump, land, markers, inspections;
        bool initialized, dodging, dead, seated, carried, awaitingRespawn, focused = true;
        int currency;string saveError;
        FieldLiftState field;
        Vector3 Feet => Session.Walker.Body.transform.position;
        bool Audible => focused && Time.timeScale > .0001f && Palette != null && pool != null;
        bool Menu => PlaytestUiRoot.Instance != null && PlaytestUiRoot.Instance.IsMenuOpen;

        void Start()
        {
            if (Palette == null || Session == null || Session.Walker == null) { enabled=false; return; }
            pool = new WorldMacroAudioVoicePool(transform,16,4,false,"CompactSound255_");
            settings = FindFirstObjectByType<UserSettingsService>();
            vitals = Session.Walker.Body.GetComponent<PlayerVitals>();
            target = Session.Walker.Motor.GetComponent<LockOn>();
            brush = FindFirstObjectByType<BrushStrokeFeedAdapter>();
            escort = FindFirstObjectByType<DemoEscortPresentation>();
            rests = FindObjectsByType<WorldMacroInnRestPresentation>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            focused = Application.isFocused;
            if(vitals!=null)vitals.Died+=Died;
            if (target != null) target.Changed += LockChanged;
            if (brush != null) brush.CastRejected += Misfire;
            Session.DemoSouthGateOpened += GateOpened;
            foreach (var enemy in FindObjectsByType<EnemyVitals>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                var captured=enemy;
                observedDeaths298.Add(enemy);
                Action death=()=>{if(!EnemyAudioEmitter298.OwnsCue(captured,EnemyAudioRole298.Death,this))Emit("enemy_death",captured.transform.position);};
                Action weak=()=>Emit("groggy",captured.transform.position);
                enemy.Died+=death; enemy.WeakPointOpened+=weak;
                detach.Add(()=>{if(captured!=null){captured.Died-=death;captured.WeakPointOpened-=weak;}});
            }
            foreach (var actor in FindObjectsByType<EnemyController>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                observedWindups298.Add(actor.GetComponent<EnemyVitals>());
                enemies.Add(new EnemyEdge{Actor=actor,Previous=actor.transform.position,Windup=actor.IsTelegraphing,Active=actor.IsProjectileFlying||actor.IsRecovering});
            }
            foreach(var boss in FindObjectsByType<CheongryongCombatController>(FindObjectsSortMode.None))
            {
                var b=boss; observedWindups298.Add(b.Vitals); Action<CheongryongCombatState> change=s=>{if(s==CheongryongCombatState.Windup&&!EnemyAudioEmitter298.OwnsCue(b.Vitals,EnemyAudioRole298.Windup,this))Emit("enemy_windup",b.transform.position);if(s==CheongryongCombatState.Active)Emit("enemy_swing",b.transform.position);};
                b.StateChanged+=change;detach.Add(()=>{if(b!=null)b.StateChanged-=change;});
            }
            foreach(var boss in FindObjectsByType<SouthGateGeneralController>(FindObjectsSortMode.None))
            {
                var b=boss; observedWindups298.Add(b.Vitals); Action<SouthGateCombatState> change=state=>{if(state==SouthGateCombatState.Windup&&!EnemyAudioEmitter298.OwnsCue(b.Vitals,EnemyAudioRole298.Windup,this))Emit("enemy_windup",b.transform.position);if(state==SouthGateCombatState.Active)Emit("enemy_swing",b.transform.position);};
                b.StateChanged+=change;detach.Add(()=>{if(b!=null)b.StateChanged-=change;});
            }
            Snapshot(); initialized=true;
        }
        void Snapshot()
        {
            currency=Session.Progress?.ledger?.currency??0;saveError=Session.SaveError;
            previous=Feet;if(Session.DemoEscortCompanion!=null){companionPrevious=Session.DemoEscortCompanion.position;companionTracked=true;}companionWalked=0; var m=Session.Walker.Motor; jump=m.JumpSerial;land=m.LandingSerial;dodging=m.IsDodging;
            dead=vitals!=null&&vitals.Hp01<=0;seated=Session.Walker.Seated;carried=escort!=null&&escort.CargoCarried;
            markers=Session.Progress?.ui?.discoveredMarkers?.Count??0; inspections=Session.Progress?.escort?.InspectionsCleared??0;
            field=Session.DemoField!=null?Session.DemoField.State:FieldLiftState.None;
        }
        public bool Emit(string id,Vector3 point,float gain=1f)
        {
            if(!Audible)return false;
            var cue=Palette.Find(id);if(cue==null||cue.Clip==null)return false;
            if(cooldowns.TryGetValue(id,out float until)&&Time.unscaledTime<until)return false;
            cooldowns[id]=Time.unscaledTime+Mathf.Max(.04f,cue.Cooldown);
            if(!pool.Play(cue,point,gain,Palette.Mix!=null?Palette.Mix.Sfx:null))return false;
            Played++;LastCue=id;Counts[id]=Counts.TryGetValue(id,out int n)?n+1:1;return true;
        }
        public bool EnemyAudioReady298 => isActiveAndEnabled && Audible;
        // Distinguish intentional suspension from a missing service. This is diagnostic;
        // foreground/pause policy and the shared pool's lifetime remain unchanged.
        public string EnemyAudioState298 => !isActiveAndEnabled ? "Disabled" : Palette == null ? "MissingPalette" :
            pool == null ? "MissingPool" : !focused ? "Unfocused" : Time.timeScale <= .0001f ? "Paused" : "Ready";
        public int AudioFocusChanges298 { get; private set; }
        public int AudioLastFocusFrame298 { get; private set; }
        public bool ObservesEnemy298(EnemyVitals owner, EnemyAudioRole298 role) => owner != null &&
            (role == EnemyAudioRole298.Death ? observedDeaths298.Contains(owner) :
             role == EnemyAudioRole298.Windup && observedWindups298.Contains(owner));
        public bool EmitEnemy298(EnemyAudioEmitter298 owner, WorldMacroPlaytestAudioProfileSO.Cue cue, string id, Vector3 point)
        {
            if (!EnemyAudioReady298 || owner == null || !EnemyAudioProfile298.Playable(cue)) return false;
            // Actor cooldowns live on the emitter; profile concurrency still uses the one shared pool.
            if (!pool.Play(cue,point,1f,Palette.Mix!=null?Palette.Mix.Sfx:null,owner:owner)) return false;
            Played++;LastCue=id;Counts[id]=Counts.TryGetValue(id,out int n)?n+1:1;return true;
        }
        public void ReleaseEnemy298(EnemyAudioEmitter298 owner, WorldMacroPlaytestAudioProfileSO.Cue cue = null) => pool?.ReleaseOwner(owner,cue);
        void Died(){Emit("player_death",Feet,.8f);awaitingRespawn=true;}
        void LockChanged(){if(initialized)Emit(target.IsLocked?"lock_on":"lock_off",Feet,.5f);}
        void Misfire(){if(initialized)Emit("misfire",Feet,.5f);}
        void GateOpened()=>Emit("gate_open",Session.DemoSouthGateGeneral!=null?Session.DemoSouthGateGeneral.transform.position:Feet);
        void Update()
        {
            if(!initialized)return;
            if(Time.unscaledTime>=nextSettings){nextSettings=Time.unscaledTime+.5f;pool.ExternalGain=Palette.Mix!=null&&Palette.Mix.IsReady?1f:settings!=null?settings.EffectiveGameplayVolume:1f;}
            pool.Tick();
            if(!Audible){Silence();Snapshot();return;}
            var m=Session.Walker.Motor;var feet=Feet;
            float distance=Vector3.Distance(feet,previous);previous=feet;
            if(!Menu&&!Session.Walker.Seated&&m.IsLocomotionGrounded&&!m.IsDodging&&distance<2f)
            { walked+=distance;if(walked>1.65f){walked=0;Emit(Surface(feet),feet,.55f);} }
            else walked=0;
            if(m.JumpSerial!=jump)Emit("jump",feet,.65f);jump=m.JumpSerial;
            if(m.LandingSerial!=land)Emit("land",feet,.65f);land=m.LandingSerial;
            if(m.IsDodging&&!dodging)Emit("dodge",feet,.6f);dodging=m.IsDodging;
            bool nowDead=vitals!=null&&vitals.Hp01<=0;
            if(awaitingRespawn&&!nowDead){Emit("respawn",feet,.8f);awaitingRespawn=false;}dead=nowDead;
            int coins=Session.Progress?.ledger?.currency??0;if(coins>currency&&!Menu)Emit("currency",feet,.5f);currency=coins;
            if(!string.IsNullOrEmpty(Session.SaveError)&&Session.SaveError!=saveError&&!Menu)Emit("ui_error",feet,.5f);saveError=Session.SaveError;
            if(seated!=Session.Walker.Seated)Emit(Session.Walker.Seated?"vehicle_board":"vehicle_exit",feet,.75f);seated=Session.Walker.Seated;
            var companion=Session.DemoEscortCompanion;
            if(companion!=null)
            {
                float travel=companionTracked?Vector3.Distance(companion.position,companionPrevious):0;
                companionPrevious=companion.position;companionTracked=true;
                if(!Menu&&!seated&&(escort==null||escort.State!=DemoEscortPresentationState.Riding)&&travel<2f&&Vector3.Distance(feet,companion.position)<24f)
                {
                    companionWalked+=travel;if(companionWalked>1.65f){companionWalked=0;Emit(Surface(companion.position),companion.position,.36f);}
                }
                else companionWalked=0;
            }
            bool cargo=escort!=null&&escort.CargoCarried;
            if(cargo!=carried)Emit(cargo?"cargo_pickup":"cargo_setdown",Session.DemoEscortCompanion!=null?Session.DemoEscortCompanion.position:feet,.6f);carried=cargo;
            int checks=Session.Progress?.escort?.InspectionsCleared??0;
            if(checks>inspections)Emit("inspection_stamp",feet,.7f);inspections=checks;
            var state=Session.DemoField!=null?Session.DemoField.State:FieldLiftState.None;
            if(state!=field){if(state==FieldLiftState.Rising)Emit("field_rise",feet);if(state==FieldLiftState.Descending)Emit("field_lower",feet);}field=state;
            int discovered=Session.Progress?.ui?.discoveredMarkers?.Count??0;
            if(discovered>markers)Emit("map_reveal",feet,.35f);markers=discovered;
            foreach(var rest in rests)
            {
                if(rest==null)continue;int id=rest.GetInstanceID();int phase=!rest.IsActive?0:rest.Elapsed<.3f?1:rest.Elapsed<1.55f?2:3;
                restPhase.TryGetValue(id,out int old);
                if(phase==2&&old<2)Emit("door_open",rest.HingeWorld,.6f);
                if(phase==3&&old==2)Emit("door_close",rest.HingeWorld,.6f);restPhase[id]=phase;
            }
            foreach(var edge in enemies)
            {
                var a=edge.Actor;if(a==null||!a.isActiveAndEnabled){edge.Windup=edge.Active=false;edge.Walked=0;if(a!=null)edge.Previous=a.transform.position;continue;}
                float travel=Vector3.Distance(a.transform.position,edge.Previous);edge.Previous=a.transform.position;
                if(!Menu&&!a.AttackInProgress&&travel<2f&&Vector3.Distance(feet,a.transform.position)<24f){edge.Walked+=travel;if(edge.Walked>1.8f){edge.Walked=0;Emit(Surface(a.transform.position),a.transform.position,.4f);}}else edge.Walked=0;
                bool active=a.IsProjectileFlying||a.IsRecovering;
                if(a.IsTelegraphing&&!edge.Windup&&!EnemyAudioEmitter298.OwnsCue(a.GetComponent<EnemyVitals>(),EnemyAudioRole298.Windup,this))Emit("enemy_windup",a.transform.position,.65f);
                if(active&&!edge.Active)Emit("enemy_swing",a.transform.position,.65f);
                edge.Windup=a.IsTelegraphing;edge.Active=active;
            }
            if(Time.unscaledTime>=nextAmbient){nextAmbient=Time.unscaledTime+.25f;Ambience(feet,distance/Mathf.Max(.001f,Time.deltaTime));}
        }
        string Surface(Vector3 feet)
        {
            var zone=Map!=null?Map.ZoneAt(feet):null;if(zone!=null&&zone.ExploreWalkedPassages)return "step_stone";
            if(Physics.Raycast(feet+Vector3.up*.3f,Vector3.down,out var hit,1.8f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                string n=hit.collider.name.ToLowerInvariant();
                if(n.Contains("water")||n.Contains("stream"))return "step_water";
                if(n.Contains("wood")||n.Contains("deck")||n.Contains("bridge"))return "step_wood";
                if(n.Contains("rock")||n.Contains("stone")||n.Contains("stair"))return "step_stone";
                if(n.Contains("grass"))return "step_grass";
            }
            if(Map!=null)foreach(var line in Map.Lines){if(line.Kind!=WorldMapLineKind.Road&&line.Kind!=WorldMapLineKind.Trail)continue;for(int i=1;i<line.Points.Length;i++){var a=line.Points[i-1];var d=line.Points[i]-a;var p=new Vector2(feet.x,feet.z);var q=a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));if(Vector2.Distance(p,q)<4f)return "step_dirt";}}
            return Map!=null?"step_grass":"step_dirt";
        }
        void Ambience(Vector3 feet,float speed)
        {
            bool cave=Map!=null&&Map.ZoneAt(feet)?.ExploreWalkedPassages==true;
            Loop(0,"vehicle_roll_loop",feet,seated&&speed>.6f?Mathf.Clamp01(speed/9f)*.45f:0);
            Loop(1,"wind_loop",feet,cave?0:.3f);
            Vector3 nearest=feet;float river=1000;
            if(Map!=null&&!cave)foreach(var line in Map.Lines)
            {
                if(line.Kind!=WorldMapLineKind.River)continue;
                for(int i=1;i<line.Points.Length;i++)
                {
                    var a=line.Points[i-1];var b=line.Points[i];var p=new Vector2(feet.x,feet.z);var d=b-a;
                    var q=a+d*Mathf.Clamp01(Vector2.Dot(p-a,d)/Mathf.Max(.001f,d.sqrMagnitude));float r=Vector2.Distance(p,q);
                    if(r<river){river=r;nearest=new Vector3(q.x,feet.y,q.y);}
                }
            }
            Loop(2,"stream_loop",nearest,Mathf.Clamp01(1-river/22)*.6f);
            float fire=1000;nearest=feet;foreach(var h in Hearths)if(h!=null&&Vector3.Distance(feet,h.position)<fire){fire=Vector3.Distance(feet,h.position);nearest=h.position;}
            Loop(3,"fire_loop",nearest,Mathf.Clamp01(1-fire/12)*.5f);
        }
        void Loop(int slot,string id,Vector3 point,float gain)
        {
            bool on=gain>.01f;
            if(on&&!looping[slot])pool.Play(Palette.Find(id),point,1f,Palette.Mix!=null?Palette.Mix.Sfx:null,slot,true);
            else if(!on&&looping[slot])pool.ReleaseSlot(slot,.35f);
            if(on){pool.MoveSlot(slot,point);pool.SetSlotGain(slot,gain);}looping[slot]=on;
        }
        void Silence(){pool?.StopAll(false);Array.Clear(looping,0,looping.Length);walked=0;}
        void OnApplicationFocus(bool value){focused=value;AudioFocusChanges298++;AudioLastFocusFrame298=Time.frameCount;if(!value)Silence();}
        void OnApplicationPause(bool value){if(value)Silence();}
        void OnDisable(){Silence();}
        void OnDestroy()
        {
            if(vitals!=null)vitals.Died-=Died;
            if(target!=null)target.Changed-=LockChanged;if(brush!=null)brush.CastRejected-=Misfire;
            if(Session!=null)Session.DemoSouthGateOpened-=GateOpened;
            foreach(var action in detach)action();pool?.Dispose();pool=null;
        }
    }
}
