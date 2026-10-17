using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 D308-104 (e) SPEC-SPELL-VFX-OVERHAUL-308 11.37 6절: a range-ㄱ hit (곡 녹 목 속 옥) leaves one small ink stain on the
    // ground under the enemy — one per enemy while it lives (속's 24 shots still leave one). Presentation only: no rule reads it.
    public sealed partial class CombatLoopWiring
    {
        private const int StainMemory308 = 256;
        private readonly Dictionary<(int id, uint life), float> _stainUntil = new Dictionary<(int, uint), float>();
        private readonly RaycastHit[] _stainHits = new RaycastHit[16];

        /// <summary>Spawns the letter's foot stain under the struck enemy unless that enemy (this life) still wears one.</summary>
        private bool TrySpawnAreaStain308(EnemyVitals target, char letter)
        {
            if (_contactVfx == null || target == null) return false;
            var profile = _contactVfx.ForSpell(letter);
            if (profile == null || profile.AreaStainPrefab == null) return false;
            var key = (target.GetInstanceID(), target.LifeRevision);
            float now = Time.time;
            if (_stainUntil.TryGetValue(key, out float until) && now < until) return false;
            if (_stainUntil.Count >= StainMemory308) _stainUntil.Clear();

            Vector3 point = StainGround308(target, profile.AreaStainMinNormalY);
            // scale 1: the stain is sized in metres here, never by the KTP contact scale (D308-105)
            var effect = SpellVFX120.KtpContactEffect.Spawn(profile.AreaStainPrefab, point, Quaternion.identity, 1f, gameObject.scene);
            if (effect == null) return false;
            float seconds = Mathf.Max(.1f, profile.AreaStainSeconds);
            var root = effect.GetComponentInChildren<ParticleSystem>();
            if (root != null)
            {
                root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = root.main;
                main.startSize = Mathf.Max(.05f, profile.AreaStainDiameter);
                main.startLifetime = seconds;
                main.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(profile.AreaStainAlpha));
                root.Play(true);
            }
            _contacts.Add(effect);
            _stainUntil[key] = now + seconds;
            return true;
        }

        /// <summary>The ground under the enemy: the deploy layer's GroundWorldY ray rule, copied (that layer sleeps). Origin = body
        /// collider centre; nothing found = the collider's lowest point (pivot when there is no collider).</summary>
        private Vector3 StainGround308(EnemyVitals target, float minNormalY)
        {
            Collider body = null;
            foreach (var c in target.GetComponentsInChildren<Collider>(false))
                if (c.enabled && !c.isTrigger) { body = c; break; }
            Vector3 origin = body != null ? body.bounds.center : target.transform.position + Vector3.up * .8f;
            float height = body != null ? body.bounds.min.y : target.transform.position.y;
            int count = Physics.RaycastNonAlloc(origin + Vector3.up * 4f, Vector3.down, _stainHits, 14f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = _stainHits[i];
                if (hit.collider is CharacterController || hit.normal.y < minNormalY) continue;
                if (hit.rigidbody != null || hit.transform.IsChildOf(target.transform)) continue;   // the struck enemy is not ground
                if (hit.distance >= best) continue;
                best = hit.distance; height = hit.point.y;
            }
            return new Vector3(origin.x, height + .02f, origin.z);
        }

        private void ClearStains308() => _stainUntil.Clear();
    }
}
