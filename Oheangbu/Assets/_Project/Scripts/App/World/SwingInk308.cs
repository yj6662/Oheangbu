using System.Collections.Generic;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.10 (D308-83a, user 2026-10-14: the spell VFX must grow out of the cast motions):
    // the ink of a swing. From the release on, the brush tip leaves a wet stroke in the air and throws drops the way it
    // moves; at the apex the ink is flung (sweep / arc), lands (slam), stands (plant) or rises (draw) - one prefab per
    // swing state. Presentation only: it listens to the swing events, reads the tip transform and writes nothing back, so
    // recognition, power, the apex and the lock cannot see it. Ink, not light: no emission anywhere.
    // Added by the gesture rig when the profile exists in Resources ("SwingInk308/SwingInk308Profile"); no profile = nothing.
    [DefaultExecutionOrder(1100)]   // after WorldMacroPlayerGestureRig (1000): the brush is where it will be drawn
    public sealed class SwingInk308 : MonoBehaviour
    {
        public const string ResourcePath = "SwingInk308/SwingInk308Profile";
        public const string TipName = "Tip", ApexName = "Apex", GroundName = "Ground";
        public SwingInk308Profile Profile;

        public struct Sample { public Vector3 Point; public float Time, Width; }

        sealed class Instance
        {
            public GameObject Root; public Transform Tip, Apex, Ground;
            public ParticleSystem[] TipSystems, ApexSystems, GroundSystems;
        }

        WorldMacroPlayerGestureRig rig;
        CombatLoopWiring wiring;
        Instance[] instances;
        Instance active;
        int entry = -1;
        float releasedAt, apexAt, followUntil;
        bool following, apexDone, has;
        Vector3 previous, velocity;
        Camera view;

        GameObject strokeHolder;
        MeshRenderer strokeRenderer;
        Mesh strokeMesh;
        float strokeSeconds = .4f;
        readonly List<Sample> samples = new List<Sample>(96);
        readonly List<Vector3> vertices = new List<Vector3>(192);
        readonly List<Vector2> uvs = new List<Vector2>(192);
        readonly List<Color> colours = new List<Color>(192);
        readonly List<int> triangles = new List<int>(576);
        readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>Read by checks.</summary>
        public bool Following => following;
        public int StrokePoints => samples.Count;
        public int Swings { get; private set; }
        public int Apexes { get; private set; }

        public static void Ensure(WorldMacroPlayerGestureRig owner)
        {
            if (owner == null || !Application.isPlaying || owner.GetComponent<SwingInk308>() != null) return;
            var profile = Resources.Load<SwingInk308Profile>(ResourcePath);
            if (profile == null || !profile.IsValid) return;
            owner.gameObject.AddComponent<SwingInk308>().Profile = profile;
        }

        void OnDisable() { Unsubscribe(); following = false; samples.Clear(); if (strokeRenderer != null) strokeRenderer.enabled = false; }

        void OnDestroy()
        {
            Unsubscribe();
            if (instances != null) foreach (var i in instances) if (i != null && i.Root != null) Destroy(i.Root);
            if (strokeHolder != null) Destroy(strokeHolder);
            if (strokeMesh != null) Destroy(strokeMesh);
        }

        void Unsubscribe()
        {
            if (!ReferenceEquals(wiring, null)) { wiring.SwingBegan308 -= OnBegan; wiring.SwingFizzled308 -= OnFizzled; }
            wiring = null;
        }

        bool Bind()
        {
            if (Profile == null || !Profile.IsValid) return false;
            if (rig == null) rig = GetComponent<WorldMacroPlayerGestureRig>();
            if (rig == null || rig.EffectTip == null) return false;
            var walker = rig.Walker308;
            CombatLoopWiring now = walker != null ? walker.Wiring : null;   // may be wired after this component woke
            if (!ReferenceEquals(now, wiring))
            {
                Unsubscribe();
                wiring = now;
                if (wiring != null) { wiring.SwingBegan308 += OnBegan; wiring.SwingFizzled308 += OnFizzled; }
            }
            if (instances == null) instances = new Instance[Profile.States.Length];
            if (strokeHolder == null)
            {
                strokeHolder = new GameObject("SwingInk308 Stroke");   // not parented: the stroke's vertices are world positions
                strokeMesh = new Mesh { name = "SwingInk308 Stroke" }; strokeMesh.MarkDynamic();
                strokeHolder.AddComponent<MeshFilter>().sharedMesh = strokeMesh;
                strokeRenderer = strokeHolder.AddComponent<MeshRenderer>();
                strokeRenderer.shadowCastingMode = ShadowCastingMode.Off; strokeRenderer.receiveShadows = false;
                strokeRenderer.lightProbeUsage = LightProbeUsage.Off; strokeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                strokeRenderer.enabled = false;
                strokeHolder.layer = 20;   // VfxAfterFog: drawn after the realm fog, like the spell prefabs
            }
            return true;
        }

        Instance Get(int index)
        {
            if (instances[index] != null) return instances[index].Root != null ? instances[index] : null;
            var prefab = Profile.States[index].Prefab;
            var made = new Instance();
            if (prefab != null)
            {
                made.Root = Instantiate(prefab); made.Root.name = prefab.name;
                made.Tip = made.Root.transform.Find(TipName); made.Apex = made.Root.transform.Find(ApexName); made.Ground = made.Root.transform.Find(GroundName);
            }
            made.TipSystems = Systems(made.Tip); made.ApexSystems = Systems(made.Apex); made.GroundSystems = Systems(made.Ground);
            instances[index] = made;
            return made.Root != null ? made : null;
        }

        static ParticleSystem[] Systems(Transform t) => t != null ? t.GetComponentsInChildren<ParticleSystem>(true) : new ParticleSystem[0];

        void OnBegan(SpellCast cast, string state, float apexTime, float lockUntil)
        {
            if (!isActiveAndEnabled || !Bind()) return;
            int index = Profile.IndexOf(state);
            if (index < 0) return;
            StopFollowing();
            entry = index; active = Get(index);
            var e = Profile.States[index];
            releasedAt = Time.time;
            apexAt = apexTime > releasedAt + .0001f ? apexTime : releasedAt + e.VisualApexSeconds;
            followUntil = apexAt + e.FollowAfterApex;
            following = true; apexDone = false; has = false; velocity = Vector3.zero;
            samples.Clear(); strokeSeconds = e.StrokeSeconds;
            if (strokeRenderer != null) { strokeRenderer.sharedMaterial = e.Stroke; strokeRenderer.enabled = e.Stroke != null; }
            Swings++;
        }

        void OnFizzled(SpellCast cast) => StopFollowing();

        void StopFollowing()
        {
            if (active != null) foreach (var ps in active.TipSystems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            following = false;
        }

        void LateUpdate()
        {
            if (!Bind()) return;
            float now = Time.time, dt = Time.deltaTime;
            if (view == null) view = Camera.main;
            Vector3 eye = view != null ? view.transform.position : transform.position + Vector3.up * 1.6f;
            if (following)
            {
                Vector3 tip = rig.EffectTip.position;
                var e = Profile.States[entry];
                bool jumped = has && (tip - previous).sqrMagnitude > Profile.ClearJump * Profile.ClearJump;
                if (jumped) { samples.Clear(); has = false; }
                if (has && dt > 0f) velocity = Vector3.Lerp(velocity, (tip - previous) / dt, 1f - Mathf.Exp(-30f * dt));
                bool hidden = (eye - tip).sqrMagnitude < Profile.HideWithinCamera * Profile.HideWithinCamera;
                if (active != null && active.Tip != null)
                {
                    active.Tip.position = tip;
                    if (!has) foreach (var ps in active.TipSystems) { ps.Clear(true); ps.Play(true); }   // started where the tip is, not where the prefab woke
                    foreach (var ps in active.TipSystems) { var em = ps.emission; em.enabled = !hidden; }
                }
                if (!hidden && e.Stroke != null)
                {
                    // the brush presses in over the first moments of the swing
                    float width = e.StrokeWidth * Mathf.SmoothStep(.35f, 1f, Mathf.Clamp01((now - releasedAt) / .1f));
                    if (samples.Count == 0 || (samples[samples.Count - 1].Point - tip).sqrMagnitude > Profile.MinVertexDistance * Profile.MinVertexDistance)
                        samples.Add(new Sample { Point = tip, Time = now, Width = width });
                }
                previous = tip; has = true;
                if (!apexDone && now >= apexAt) { apexDone = true; AtApex(tip); }
                if (now >= followUntil) StopFollowing();
            }
            // the stroke ages whether the tip is still followed or not
            while (samples.Count > 0 && now - samples[0].Time > strokeSeconds) samples.RemoveAt(0);
            if (strokeRenderer != null && strokeRenderer.enabled)
            {
                if (samples.Count < 2) { strokeMesh.Clear(); if (!following) strokeRenderer.enabled = false; }
                else BuildRibbon(strokeMesh, samples, now, strokeSeconds, eye, vertices, uvs, colours, triangles);
            }
        }

        void AtApex(Vector3 tip)
        {
            if (active == null) return;
            Vector3 flat = transform.forward; flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            if (active.Apex != null)
            {
                // the ink leaves the way the tip was going, never back at the caster
                Vector3 way = velocity.sqrMagnitude > .25f ? velocity.normalized : flat;
                if (Vector3.Dot(way, flat) < 0f) way = Vector3.Reflect(way, flat);
                active.Apex.SetPositionAndRotation(tip, Quaternion.LookRotation(way, Mathf.Abs(way.y) > .98f ? flat : Vector3.up));
                foreach (var ps in active.ApexSystems) { ps.Clear(true); ps.Play(true); }
            }
            if (active.Ground != null)
            {
                active.Ground.SetPositionAndRotation(GroundUnder(tip), Quaternion.LookRotation(flat, Vector3.up));
                foreach (var ps in active.GroundSystems) { ps.Clear(true); ps.Play(true); }
            }
            Apexes++;
        }

        Vector3 GroundUnder(Vector3 tip)
        {
            Transform own = transform.root;
            int count = Physics.RaycastNonAlloc(tip + Vector3.up * .6f, Vector3.down, hits, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; Vector3 point = new Vector3(tip.x, transform.position.y, tip.z);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.root == own || hits[i].distance >= best) continue;
                best = hits[i].distance; point = hits[i].point;
            }
            return point + Vector3.up * .03f;
        }

        // The stroke as a ribbon that faces the eye. Samples run oldest -> newest (the tip). UV.x: 0 at the tip, 1 at the
        // oldest end of the stroke's life; colour alpha: the ink left (the shader breaks the stroke with it, it does not fade it).
        // Shared with the edit-mode pose (Vfx308Build swingink:pose), so a still shows what the game builds.
        public static void BuildRibbon(Mesh mesh, List<Sample> list, float now, float seconds, Vector3 eye,
            List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri)
        {
            v.Clear(); uv.Clear(); c.Clear(); tri.Clear();
            int n = list.Count;
            if (n < 2) { mesh.Clear(); return; }
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                Vector3 along = list[Mathf.Min(i + 1, n - 1)].Point - list[Mathf.Max(i - 1, 0)].Point;
                Vector3 side = Vector3.Cross(along, eye - s.Point);
                side = side.sqrMagnitude > 1e-10f ? side.normalized : Vector3.up;
                float age = Mathf.Clamp01((now - s.Time) / Mathf.Max(.01f, seconds));
                // a rounded head at the tip, a body that narrows a little as it dries
                float head = Mathf.Clamp01((n - 1 - i) / 2.5f);
                float width = s.Width * Mathf.Lerp(.45f, 1f, Mathf.Sqrt(head)) * Mathf.Lerp(1f, .7f, age);
                float left = 1f - Mathf.SmoothStep(.45f, 1f, age);
                v.Add(s.Point - side * (width * .5f)); v.Add(s.Point + side * (width * .5f));
                uv.Add(new Vector2(age, 0f)); uv.Add(new Vector2(age, 1f));
                var colour = new Color(1f, 1f, 1f, left); c.Add(colour); c.Add(colour);
                if (i == 0) continue;
                int a = (i - 1) * 2;
                tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3); tri.Add(a + 2);
            }
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetColors(c); mesh.SetTriangles(tri, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
