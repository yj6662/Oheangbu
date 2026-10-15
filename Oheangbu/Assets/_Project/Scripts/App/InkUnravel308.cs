using System.Collections.Generic;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App
{
    // #308 (SPEC-SPELL-VFX-OVERHAUL-308 11.25, user request 2026-10-15 "풀림 먼저 만들고"): the drawn letter comes apart into
    // ink that is pulled into the swinging brush tip. Points taken from the stroke meshes leave one after another over
    // `Seconds`; each drop is steered every frame so that it reaches the tip (World.SwingInk308.TryOrigin, the live tip of
    // the swing) when its life ends. Presentation only: it reads stroke meshes and the tip, writes nothing back, and the
    // cast clock does not wait for it. Destroys itself when the last drop has arrived.
    public sealed class InkUnravel308 : MonoBehaviour
    {
        static InkUnravel308Profile profile; static bool looked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { profile = null; looked = false; }   // domain reload is off in this project

        public static InkUnravel308Profile Profile
        {
            get
            {
                if (!looked) { looked = true; profile = Resources.Load<InkUnravel308Profile>(InkUnravel308Profile.ResourcesPath); }
                return profile != null && profile.IsValid ? profile : null;
            }
        }

        struct Pending { public Vector3 Point; public float At; }

        readonly List<Pending> pending = new List<Pending>(256);
        ParticleSystem system; ParticleSystem.Particle[] live;
        InkUnravel308Profile p; Vector3 target; Color tint; float startedAt; int next;

        /// <summary>Starts the unravel of these points (world space). Null when there is no valid profile or nothing to unravel.</summary>
        public static InkUnravel308 Begin(List<Vector3> points, Vector3 fallbackTarget, Color pigment)
        {
            var prof = Profile;
            if (prof == null || points == null || points.Count == 0 || !Application.isPlaying) return null;
            var go = Instantiate(prof.ParticlePrefab);
            go.name = "InkUnravel308";
            var u = go.AddComponent<InkUnravel308>();
            u.p = prof; u.system = go.GetComponentInChildren<ParticleSystem>(true);
            if (u.system == null) { Destroy(go); return null; }
            var main = u.system.main; main.maxParticles = Mathf.Max(main.maxParticles, prof.MaxParticles);
            u.live = new ParticleSystem.Particle[main.maxParticles];
            u.target = World.SwingInk308.TryOrigin(out var tip) ? tip : fallbackTarget;
            u.tint = Color.Lerp(new Color(.08f, .07f, .06f, 1f), pigment, prof.Tint); u.tint.a = 1f;
            u.startedAt = Time.time;
            // spread over the letter: every k-th point up to the cap, in a shuffled order so the letter frays all over at once
            int stride = Mathf.Max(1, Mathf.CeilToInt(points.Count / (float)prof.MaxParticles));
            for (int i = 0; i < points.Count; i += stride) u.pending.Add(new Pending { Point = points[i], At = Random.value * prof.Seconds });
            u.pending.Sort((a, b) => a.At.CompareTo(b.At));
            u.system.Play(true);
            return u;
        }

        void LateUpdate()
        {
            if (system == null) { Destroy(gameObject); return; }
            float now = Time.time, age = now - startedAt, dt = Time.deltaTime;
            if (World.SwingInk308.TryOrigin(out var tip)) target = tip;
            // leave the strokes
            while (next < pending.Count && pending[next].At <= age)
            {
                var e = new ParticleSystem.EmitParams
                {
                    position = pending[next].Point,
                    startLifetime = Random.Range(p.FlySeconds.x, p.FlySeconds.y),
                    startSize = Random.Range(p.Size.x, p.Size.y),
                    startColor = tint,
                    applyShapeToPosition = false,
                };
                e.velocity = (target - e.position) / e.startLifetime;
                system.Emit(e, 1);
                next++;
            }
            // pulled into the tip: each drop heads for where the tip is now, so as to arrive as its life ends
            int n = system.GetParticles(live);
            float k = 1f - Mathf.Exp(-p.Homing * dt);
            for (int i = 0; i < n; i++)
            {
                float left = Mathf.Max(live[i].remainingLifetime, .03f);
                Vector3 want = (target - live[i].position) / left;
                live[i].velocity = Vector3.Lerp(live[i].velocity, want, k);
            }
            system.SetParticles(live, n);
            if (next >= pending.Count && n == 0 && age > p.Seconds + .05f) Destroy(gameObject);
        }
    }
}
