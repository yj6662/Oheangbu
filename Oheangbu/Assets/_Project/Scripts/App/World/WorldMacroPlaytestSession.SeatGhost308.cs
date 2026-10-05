using System.Collections.Generic;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-16b (relayout ext KE6, SPEC-VEHICLE-UX-308 / SPEC-CONTENT-PACING-308) [TEST] — 탑승 중 일반 적 충돌체.
    // While the player rides, Cull() already switches every ordinary enemy off (no behaviour, no attack, renderers hidden).
    // Its colliders stayed on, so an enemy that was fought on the road and then left behind by boarding remained an invisible solid
    // capsule in the car's way. Here the SOLID colliders of such an enemy go off for as long as the walker is seated and come back,
    // exactly the ones that were on, when the player gets down.
    // Who is left alone (review 2, finding 3):
    //   - 청룡 / 남문 장수 / the growth lesson: their own branch in Cull() (this method is not called for them);
    //   - every other boss (EnemyVitals.IsBoss: the field boss, the tutorial boss, mountain bosses): never passed through;
    //   - trigger colliders (hit boxes, sensors): they do not block the car, so they are never touched.
    // The switch is OFF in code: a scene gets the rule only when its session carries SeatedEnemiesPassThrough308 = true. The earth
    // scene tool writes it (ledger op "earth-flag") in the three #308 target scenes from earth308.json runtime.seated_pass_through;
    // every other scene that uses this session class keeps the behaviour it had.
    // Called from Cull() only (every 0.2 s; Cull returns early while gameplay input is blocked, so nothing changes in a cutscene).
    // No static state: the table lives on the session instance and is cleared with it.
    public sealed partial class WorldMacroPlaytestSession
    {
        [Header("#308 탑승 중 일반 적 충돌체 [TEST]")]
        [Tooltip("켜면 탑승 중(Walker.Seated)에는 꺼진 일반 적(보스 제외)의 막는 Collider(트리거 제외)도 끈다. 내리면 켜져 있던 것만 다시 켠다. 기본 끔 = #308 이전 동작(충돌체가 길에 남는다). 세 대상 씬에서는 Content308 Earth scene-apply가 earth308.json runtime.seated_pass_through에 따라 켠다.")]
        public bool SeatedEnemiesPassThrough308;

        // actor -> the colliders this rule switched off (only solid ones that were enabled)
        readonly Dictionary<PrologueEncounter,Collider[]> seatGhosts308=new Dictionary<PrologueEncounter,Collider[]>();
        /// <summary>Diagnostics: ordinary enemies whose colliders are held by this rule (off while riding, or waiting for a rest reset after dying).</summary>
        public int SeatGhostCount308=>seatGhosts308.Count;

        void SeatGhost308(PrologueEncounter actor,EnemyVitals vitals,bool alive)
        {
            bool have=seatGhosts308.TryGetValue(actor,out var held);
            if(!have&&!SeatedEnemiesPassThrough308)return;
            bool ghost=SeatedEnemiesPassThrough308&&Walker.Seated&&alive&&!vitals.IsBoss;
            if(ghost&&have)
            {
                // a rest reset (PrologueEncounter.ResetEncounter) may have switched some of them on again while riding: every one is looked at
                foreach(var c in held)if(c!=null&&c.enabled)c.enabled=false;
                return;
            }
            if(ghost)
            {
                actor.GetComponentsInChildren(false,cullColliders);
                int n=0;foreach(var c in cullColliders)if(c.enabled&&!c.isTrigger)n++;
                var off=new Collider[n];n=0;
                foreach(var c in cullColliders)if(c.enabled&&!c.isTrigger){c.enabled=false;off[n++]=c;}
                cullColliders.Clear();seatGhosts308.Add(actor,off);
                return;
            }
            if(!have)return;
            // An enemy that died meanwhile keeps its colliders off (PrologueEncounter.Died). The entry is KEPT until it lives again: a rest
            // reset switches on only the colliders the encounter collected in Awake, and a collider added later would stay off for good.
            if(!alive)return;
            seatGhosts308.Remove(actor);
            foreach(var c in held)if(c!=null)c.enabled=true;
        }
    }
}
