// Only the engine/event boundary is faked. Checks compile the three actual production source files.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static void Destroy(Object o) {} public static void DestroyImmediate(Object o) {} }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component
    {
        public bool isActiveAndEnabled = true;
        public static T FindFirstObjectByType<T>() where T : class => Oheangbu.App.World.CompactSoundscape255.Current as T;
    }
    public class ScriptableObject : Object {}
    public class GameObject : Object
    {
        readonly Dictionary<Type,Component> all = new();
        public Transform transform;
        public GameObject(string n = "") { transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; all[typeof(T)] = c; return c; }
        public T GetComponent<T>() where T : Component => all.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }
    public class Transform : Component { public Vector3 position; public Transform Find(string n) => null; public void SetParent(Transform t, bool p) {} }
    public struct Vector3 { public float x,y,z; }
    public class AudioClip : Object { public int samples=48000; public float length=1; public AudioDataLoadState loadState=AudioDataLoadState.Loaded; }
    public enum AudioDataLoadState { Loaded, Failed }
    public enum AudioRolloffMode { Logarithmic }
    public class AudioSource : Component
    {
        public bool isPlaying,isVirtual,loop,playOnAwake,ignoreListenerPause;
        public float volume,pitch,dopplerLevel,minDistance,maxDistance,spatialBlend;
        public AudioClip clip;
        public AudioRolloffMode rolloffMode;
        public Audio.AudioMixerGroup outputAudioMixerGroup;
        public void Stop() => isPlaying=false;
        public void Play() => isPlaying=true;
    }
    public static class AudioSettings { public static double dspTime; public static int outputSampleRate=48000; }
    public static class Application { public static bool isPlaying; }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static float Clamp01(float x)=>Math.Clamp(x,0,1); }
    public class CreateAssetMenuAttribute : Attribute { public string menuName; }
    public class DisallowMultipleComponentAttribute : Attribute {}
    public class RequireComponentAttribute : Attribute { public RequireComponentAttribute(Type t) {} }
}
namespace UnityEngine.Audio { public class AudioMixerGroup {} }
namespace Oheangbu.Combat
{
    using UnityEngine;
    public readonly struct AttackProvenance { public readonly long AttackId; public AttackProvenance(long id) { AttackId=id; } }
    public struct EnemyDamageResult { public EnemyVitals Target; public float AppliedDamage; public bool Killed; }
    public struct EnemyAttackCue { public AttackProvenance Attack; }
    public class EnemyVitals : MonoBehaviour
    {
        public uint LifeRevision;
        public bool IsAlive=true;
        public event Action<EnemyDamageResult> DamageResolved;
        public event Action Died;
        public void Hit(bool killed=false)
        { DamageResolved?.Invoke(new EnemyDamageResult{Target=this,AppliedDamage=3,Killed=killed}); if(killed) { IsAlive=false; LifeRevision++; Died?.Invoke(); } }
        public void RepeatDeath() => Died?.Invoke();
        public void Restore() { IsAlive=true; LifeRevision++; }
    }
    public class EnemyController : MonoBehaviour
    {
        public object AttackProfile=new();
        public bool IsTelegraphing;
        public event Action<EnemyAttackCue> AttackTelegraphed;
        public event Action<AttackProvenance,bool> AttackPresentationEnded;
        public void Start(long id) { IsTelegraphing=true; AttackTelegraphed?.Invoke(new EnemyAttackCue{Attack=new AttackProvenance(id)}); }
        public void End(long id) { IsTelegraphing=false; AttackPresentationEnded?.Invoke(new AttackProvenance(id),true); }
        public void StaleStart(long id) => AttackTelegraphed?.Invoke(new EnemyAttackCue{Attack=new AttackProvenance(id)});
    }
}
namespace Oheangbu.App.Demo
{
    using UnityEngine; using Oheangbu.Combat;
    public class CheongryongAttackPlan { public AttackProvenance Attack; public bool IsCancelled; }
    public class SouthGateAttackPulse { public AttackProvenance Attack; }
    public class SouthGateGeneralAttackPlan { public List<SouthGateAttackPulse> Pulses=new(); public bool IsCancelled; }
    public class CheongryongCombatController : MonoBehaviour
    {
        public event Action<CheongryongAttackPlan> AttackStarted;
        public event Action<CheongryongAttackPlan,bool> AttackEnded;
        public CheongryongAttackPlan CurrentPlan;
        public void Start(CheongryongAttackPlan p) { CurrentPlan=p; AttackStarted?.Invoke(p); }
        public void End(CheongryongAttackPlan p) { p.IsCancelled=true; CurrentPlan=null; AttackEnded?.Invoke(p,true); }
    }
    public class SouthGateGeneralController : MonoBehaviour
    {
        public event Action<SouthGateGeneralAttackPlan> AttackStarted;
        public event Action<SouthGateGeneralAttackPlan,bool> AttackEnded;
        public SouthGateGeneralAttackPlan CurrentPlan;
        public void Start(SouthGateGeneralAttackPlan p) { CurrentPlan=p; AttackStarted?.Invoke(p); }
        public void End(SouthGateGeneralAttackPlan p) { p.IsCancelled=true; CurrentPlan=null; AttackEnded?.Invoke(p,true); }
        public void StaleStart(SouthGateGeneralAttackPlan p) => AttackStarted?.Invoke(p);
    }
}
namespace Oheangbu.App.Prologue
{
    public class PrologueEncounter : UnityEngine.MonoBehaviour
    {
        public enum Behaviour { Patrol, Chase, Return, Dead }
        public Behaviour Current;
        public event Action PlayerDetected;
        public void Detect()
        { var before=Current;Current=Behaviour.Chase;if(before!=Behaviour.Chase)PlayerDetected?.Invoke(); }
    }
}
namespace Oheangbu.App.World
{
    using UnityEngine; using Oheangbu.Combat;
    public class WorldMacroPlaytestAudioProfileSO
    { public class Cue { public AudioClip Clip; public float SpatialBlend,Cooldown,Volume=.7f,AttackSeconds=.012f,ReleaseSeconds=.04f; public int MaxConcurrent=3; } }
    public class WorldMacroAudioEnvelope : Component
    {
        public EnvelopeState State=new();
        public class EnvelopeState
        {
            public bool IsSilent;
            public float ExternalGain;
            public void Silence()=>IsSilent=true;
            public void Release(float s)=>IsSilent=true;
            public void Start(int rate,float length,float attack,float release,float gain,bool loop)=>IsSilent=false;
        }
    }
    public class CompactSoundscape255 : MonoBehaviour
    {
        public static CompactSoundscape255 Current;
        public bool EnemyAudioReady298=true, Observed;
        public string EnemyAudioState298 = "Ready";
        public WorldMacroAudioVoicePool Pool;
        public List<string> Played=new();
        public List<WorldMacroPlaytestAudioProfileSO.Cue> Cues=new();
        public bool ObservesEnemy298(EnemyVitals v,EnemyAudioRole298 r)=>Observed;
        public bool Emit(string id,Vector3 point) { Played.Add(id); return true; }
        public bool EmitEnemy298(EnemyAudioEmitter298 owner,WorldMacroPlaytestAudioProfileSO.Cue cue,string id,Vector3 point)
        {
            if (!EnemyAudioReady298) return false;
            bool ok=Pool==null||Pool.Play(cue,point,1,null,owner:owner);
            if(ok) { Played.Add(id); Cues.Add(cue); } return ok;
        }
        public void ReleaseEnemy298(EnemyAudioEmitter298 owner,WorldMacroPlaytestAudioProfileSO.Cue cue=null) => Pool?.ReleaseOwner(owner,cue);
    }
}
