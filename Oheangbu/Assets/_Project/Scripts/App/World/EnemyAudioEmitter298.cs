using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Presentation only. Attach to the EnemyVitals owner, including dynamically spawned actors.
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyVitals))]
    public sealed class EnemyAudioEmitter298 : MonoBehaviour
    {
        public EnemyAudioProfile298 Profile;
        public CompactSoundscape255 Soundscape;
        public int Played { get; private set; }
        public int FallbackPlayed { get; private set; }
        public int Dropped { get; private set; }
        public string LastRole { get; private set; }
        public string LastDropReason { get; private set; }
        public string Status => Profile == null ? "MissingProfile: generic fallback only" :
            !string.IsNullOrEmpty(Profile.MissingClips) ? "MissingClips: " + Profile.MissingClips :
            Soundscape == null ? "WaitingForSoundscape:MissingService" :
            !Soundscape.EnemyAudioReady298 ? "WaitingForSoundscape:" + Soundscape.EnemyAudioState298 : "Ready";
        public uint BoundLifeRevision => revision;
        EnemyVitals vitals;
        EnemyController enemy;
        CheongryongCombatController dragon;
        SouthGateGeneralController general;
        PrologueEncounter encounter;
        SouthGateGeneralAttackPlan lastGeneralPlan;
        WorldMacroPlaytestAudioProfileSO.Cue windupPlaying;
        readonly float[] next = new float[4];
        uint revision;
        long lastAttack, lastEndedAttack;
        int hitIndex;
        bool bound, deathPlayed;

        void OnEnable() => Rebind();
        void Update() { if (bound) SyncLife(); }
        void OnDisable() { StopWindup(); Unbind(); }
        void OnDestroy() => Unbind();

        // Import/spawn owners may call this after adding controllers. No global actor scan is needed.
        public void Rebind()
        {
            StopWindup(); Unbind();
            vitals = GetComponent<EnemyVitals>();
            if (vitals == null || !isActiveAndEnabled) return;
            enemy = GetComponent<EnemyController>();
            dragon = GetComponent<CheongryongCombatController>();
            general = GetComponent<SouthGateGeneralController>();
            encounter = GetComponent<PrologueEncounter>();
            revision = vitals.LifeRevision;
            ResetLife(); bound = true;
            vitals.DamageResolved += Damaged;
            vitals.Died += Died;
            if (encounter != null) encounter.PlayerDetected += Detected;
            if (dragon != null) { dragon.AttackStarted += DragonStarted; dragon.AttackEnded += DragonEnded; }
            else if (general != null) { general.AttackStarted += GeneralStarted; general.AttackEnded += GeneralEnded; }
            else if (enemy != null) { enemy.AttackTelegraphed += Telegraphed; enemy.AttackPresentationEnded += AttackEnded; }
        }

        void Unbind()
        {
            if (vitals != null) { vitals.DamageResolved -= Damaged; vitals.Died -= Died; }
            if (encounter != null) encounter.PlayerDetected -= Detected;
            if (dragon != null) { dragon.AttackStarted -= DragonStarted; dragon.AttackEnded -= DragonEnded; }
            if (general != null) { general.AttackStarted -= GeneralStarted; general.AttackEnded -= GeneralEnded; }
            if (enemy != null) { enemy.AttackTelegraphed -= Telegraphed; enemy.AttackPresentationEnded -= AttackEnded; }
            bound = false;
        }
        void ResetLife()
        {
            if (Soundscape != null) Soundscape.ReleaseEnemy298(this);
            System.Array.Clear(next, 0, next.Length);
            lastAttack = lastEndedAttack = 0; lastGeneralPlan = null; hitIndex = 0; deathPlayed = false; windupPlaying = null;
        }
        void SyncLife()
        {
            if (vitals != null && revision != vitals.LifeRevision) { revision = vitals.LifeRevision; ResetLife(); }
        }
        CompactSoundscape255 Audio()
        {
            if (Soundscape == null || !Soundscape.isActiveAndEnabled)
                Soundscape = FindFirstObjectByType<CompactSoundscape255>();
            return Soundscape;
        }

        bool HasWindupEvents => dragon != null && dragon.isActiveAndEnabled ||
            general != null && general.isActiveAndEnabled || enemy != null && enemy.isActiveAndEnabled && enemy.AttackProfile != null;

        // Queried before the old observer fires, independent of event-subscription order.
        public static bool OwnsCue(EnemyVitals owner, EnemyAudioRole298 role, CompactSoundscape255 audio)
        {
            if (owner == null || audio == null || !audio.EnemyAudioReady298) return false;
            var emitter = owner.GetComponent<EnemyAudioEmitter298>();
            return emitter != null && emitter.bound && emitter.isActiveAndEnabled &&
                emitter.Audio() == audio && emitter.Profile != null && emitter.Profile.Find(role) != null &&
                (role != EnemyAudioRole298.Windup || emitter.HasWindupEvents);
        }

        void Detected()
        {
            if (encounter != null && encounter.isActiveAndEnabled && encounter.Current == PrologueEncounter.Behaviour.Chase)
                NotifyAlert();
        }
        // Also available to future perception owners. Rebind while already chasing never emits it.
        public bool NotifyAlert()
        {
            if (!bound || vitals == null || !vitals.IsAlive) return false;
            SyncLife(); return Play(EnemyAudioRole298.Alert);
        }
        void Telegraphed(EnemyAttackCue cue)
        { if (enemy != null && enemy.isActiveAndEnabled && enemy.IsTelegraphing) Windup(cue.Attack.AttackId); }
        void DragonStarted(CheongryongAttackPlan plan)
        {
            // Earlier event subscribers may have cancelled/replaced the plan before this callback.
            if (dragon != null && dragon.isActiveAndEnabled && plan != null && !plan.IsCancelled &&
                ReferenceEquals(dragon.CurrentPlan, plan)) Windup(plan.Attack.AttackId);
        }
        void GeneralStarted(SouthGateGeneralAttackPlan plan)
        {
            SyncLife(); if (general == null || !general.isActiveAndEnabled || plan == null || plan.IsCancelled ||
                !ReferenceEquals(general.CurrentPlan, plan) || ReferenceEquals(lastGeneralPlan, plan)) return;
            lastGeneralPlan = plan; Windup(plan.Pulses.Count > 0 ? plan.Pulses[0].Attack.AttackId : 0);
        }
        void Windup(long attackId)
        {
            if (!bound || vitals == null || !vitals.IsAlive) return;
            SyncLife(); if (attackId != 0 && (attackId == lastAttack || attackId == lastEndedAttack)) return;
            lastAttack = attackId; StopWindup(); Play(EnemyAudioRole298.Windup);
        }
        void AttackEnded(AttackProvenance attack, bool cancelled)
        { lastEndedAttack = attack.AttackId; if (attack.AttackId == lastAttack) StopWindup(); }
        void DragonEnded(CheongryongAttackPlan plan, bool cancelled) { if (plan != null && plan.Attack.AttackId == lastAttack) StopWindup(); }
        void GeneralEnded(SouthGateGeneralAttackPlan plan, bool cancelled) { if (ReferenceEquals(plan, lastGeneralPlan)) StopWindup(); }
        void StopWindup()
        {
            if (Soundscape != null && windupPlaying != null) Soundscape.ReleaseEnemy298(this, windupPlaying);
            windupPlaying = null;
        }
        void Damaged(EnemyDamageResult result)
        {
            if (!bound || result.Target != vitals || result.AppliedDamage <= 0 || result.Killed) return;
            SyncLife(); Play(EnemyAudioRole298.Hit);
        }
        void Died()
        {
            if (!bound) return;
            // EnemyVitals advances LifeRevision between DamageResolved and Died.
            SyncLife(); if (deathPlayed) return;
            deathPlayed = true; StopWindup(); Play(EnemyAudioRole298.Death);
        }
        bool Play(EnemyAudioRole298 role)
        {
            LastRole = role.ToString();
            var audio = Audio();
            var cue = Profile != null ? Profile.Find(role, hitIndex) : null;
            if (cue == null)
            {
                // Existing actors already have the generic observer. Newly spawned ones do not.
                if (audio != null && !audio.ObservesEnemy298(vitals, role))
                {
                    string id = role == EnemyAudioRole298.Death ? "enemy_death" : role == EnemyAudioRole298.Windup ? "enemy_windup" : null;
                    if (id != null && audio.Emit(id, transform.position)) { FallbackPlayed++; return true; }
                }
                LastDropReason = "MissingCue:" + role; Dropped++; return false;
            }
            int slot = (int)role;
            if (Time.unscaledTime < next[slot]) return false;
            if (audio == null || !audio.EmitEnemy298(this, cue, Profile.ActorId + ":" + LastRole, transform.position))
            {
                LastDropReason = audio == null ? "MissingService" : !audio.EnemyAudioReady298 ? audio.EnemyAudioState298 : "PoolRejected";
                Dropped++; return false;
            }
            next[slot] = Time.unscaledTime + Mathf.Max(0f, cue.Cooldown);
            if (role == EnemyAudioRole298.Hit) hitIndex++;
            if (role == EnemyAudioRole298.Windup) windupPlaying = cue;
            Played++; return true;
        }
    }
}
