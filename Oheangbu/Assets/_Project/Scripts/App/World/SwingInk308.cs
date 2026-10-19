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
    // D308-90 (11.34 20th pass): from the apex the spell's pigment soaks into the swung arc, tip first, and the arc dries
    // slower (Profile.Soaks; no row for the cast = as before). Colour and stroke age only - the bridge stays ink.
    // Added by the gesture rig when the profile exists in Resources ("SwingInk308/SwingInk308Profile"); no profile = nothing.
    [DefaultExecutionOrder(1100)]   // after WorldMacroPlayerGestureRig (1000): the brush is where it will be drawn
    public sealed class SwingInk308 : MonoBehaviour
    {
        public const string ResourcePath = "SwingInk308/SwingInk308Profile";
        public const string TipName = "Tip", ApexName = "Apex", GroundName = "Ground", RunName = "Run";
        public SwingInk308Profile Profile;

        public struct Sample { public Vector3 Point; public float Time, Width; }

        sealed class Instance
        {
            public GameObject Root; public Transform Tip, Apex, Ground;
            public ParticleSystem[] TipSystems, ApexSystems, GroundSystems;
            public ParticleSystem Run;   // the slam prefab's ground-run ink (world space, emitted by script along the bridge)
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
        // D308-90 (SPEC-SPELL-VFX-OVERHAUL-308 11.34 20th pass): the soak row of this cast (-1 = none), its pigment in the
        // colour space the vertex colours are read in, and how fast the stroke ages after the apex (1 = as before)
        int soak = -1;
        Color soakPigment;
        float inkK = 1f;
        // D308-91 (11.35): the row's max and reach with this motion's additions, fixed once per cast
        float soakMax, soakReach;
        readonly List<Sample> samples = new List<Sample>(96);
        readonly List<Vector3> vertices = new List<Vector3>(192);
        readonly List<Vector2> uvs = new List<Vector2>(192);
        readonly List<Color> colours = new List<Color>(192);
        readonly List<int> triangles = new List<int>(576);
        readonly RaycastHit[] hits = new RaycastHit[8];

        // D308-84 (SPEC-SPELL-VFX-OVERHAUL-308 11.20): the bridge - the ink that goes from the tip to where the spell stands, laid
        // when the apex plan is made. Single: a stem toward the target, and the spell leaves from its end. Circle: from where the
        // tip struck, the ink runs over the ground to the centre and pools there before the judgement (apex + Delay). Ink only.
        readonly List<Sample> bridge = new List<Sample>(64);
        readonly List<Vector3> bridgePath = new List<Vector3>(64);
        readonly List<float> bridgeAt = new List<float>(64);
        GameObject bridgeHolder;
        MeshRenderer bridgeRenderer;
        Mesh bridgeMesh;
        float bridgeWidth, bridgeDry, bridgeUntil;
        bool bridgeFlat;
        Instance pool;
        Vector3 poolAt;
        float poolAtTime = float.PositiveInfinity;
        // circle: the ground-run ink is soaked blots laid where the running ink has reached (the flat stroke ribbon alone
        // vanished on brown ground, Play sw_m / sw_n) - index of the next bridge point to blot
        int runNext = int.MaxValue;
        float runSize;
        // the stem's end stays the launch origin until the stroke adapter spawns the spell (it queues the release and spawns later)
        float originPinnedUntil = float.NegativeInfinity;

        /// <summary>Read by checks.</summary>
        public bool Following => following;
        public int StrokePoints => samples.Count;
        public float StrokeSpan => samples.Count > 1 ? samples[samples.Count - 1].Time - samples[0].Time : 0f;   // ink-clock seconds
        public bool StrokeShown => strokeRenderer != null && strokeRenderer.enabled && samples.Count >= 2;
        public int SoakIndex => soak;
        public int Swings { get; private set; }
        public int Apexes { get; private set; }
        public int Bridges { get; private set; }
        public bool BridgeShown => bridgeRenderer != null && bridgeRenderer.enabled;

        // Where the swung brush tip was a moment ago: the spell that leaves at the apex starts there (Vfx120LaunchPoint), so it
        // comes out of the thrown ink and not out of a point in front of the camera. The dispatch runs in Update and this
        // component in LateUpdate, so the answer is the tip of the frame before - a few centimetres. Presentation origin
        // only: the target, the flight clock and the damage never read it.
        static Vector3 originTip;
        static float originAt = float.NegativeInfinity;
        const float OriginFresh = .12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { originTip = default; originAt = float.NegativeInfinity; }   // domain reload is off in this project

        public static bool TryOrigin(out Vector3 tip)
        {
            tip = originTip;
            return Application.isPlaying && Time.time - originAt <= OriginFresh && Time.time >= originAt;
        }

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
            if (bridgeHolder != null) Destroy(bridgeHolder);
            if (bridgeMesh != null) Destroy(bridgeMesh);
            if (pool != null && pool.Root != null) Destroy(pool.Root);
        }

        void Unsubscribe()
        {
            if (!ReferenceEquals(wiring, null)) { wiring.SwingBegan308 -= OnBegan; wiring.SwingFizzled308 -= OnFizzled; wiring.CastPlanned -= OnPlanned; }
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
                if (wiring != null) { wiring.SwingBegan308 += OnBegan; wiring.SwingFizzled308 += OnFizzled; wiring.CastPlanned += OnPlanned; }
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
            if (bridgeHolder == null)
            {
                bridgeHolder = new GameObject("SwingInk308 Bridge");
                bridgeMesh = new Mesh { name = "SwingInk308 Bridge" }; bridgeMesh.MarkDynamic();
                bridgeHolder.AddComponent<MeshFilter>().sharedMesh = bridgeMesh;
                bridgeRenderer = bridgeHolder.AddComponent<MeshRenderer>();
                bridgeRenderer.shadowCastingMode = ShadowCastingMode.Off; bridgeRenderer.receiveShadows = false;
                bridgeRenderer.lightProbeUsage = LightProbeUsage.Off; bridgeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                bridgeRenderer.enabled = false;
                bridgeHolder.layer = 20;
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
            float lead = wiring != null ? wiring.SwingClipLead308 : 0f;   // D308-84: the ward's short slam strikes this soon
            apexAt = apexTime > releasedAt + .0001f ? apexTime : releasedAt + (lead > 0f ? lead : e.VisualApexSeconds);
            followUntil = apexAt + e.FollowAfterApex;
            following = true; apexDone = false; has = false; velocity = Vector3.zero;
            samples.Clear(); strokeSeconds = e.StrokeSeconds;
            soak = Profile.SoakFor(cast.Element, cast.Letter);
            if (soak >= 0)
            {
                var row = Profile.Soaks[soak];
                inkK = Mathf.Max(.1f, row.AgingAfterApex * (1f - Mathf.Clamp(e.SoakLinger, 0f, .9f)));
                soakMax = Mathf.Clamp01(row.Max + Mathf.Max(0f, e.SoakMaxAdd));
                soakReach = row.Reach + Mathf.Max(0f, e.SoakReachAdd);
                soakPigment = QualitySettings.activeColorSpace == ColorSpace.Linear ? row.Pigment.linear : row.Pigment;   // vertex colours are not converted
            }
            else inkK = 1f;
            if (strokeRenderer != null) { strokeRenderer.sharedMaterial = e.Stroke; strokeRenderer.enabled = e.Stroke != null; }
            Swings++;
        }

        // a fizzle = the waiting cast was dropped, no spell: no soak. It can come on the apex frame itself
        // (TickSwing308 drops before it dispatches), so the test is apexDone (set in LateUpdate, after every Update), not the clock
        void OnFizzled(SpellCast cast)
        {
            StopFollowing();
            if (!apexDone) { soak = -1; inkK = 1f; }
        }

        // D308-90: the stroke's own clock - real time up to the apex, slower after it while a soak row holds (the arc stays
        // until the ember has left). Continuous at the apex, so nothing jumps.
        float InkNow(float now) => now <= apexAt ? now : apexAt + (now - apexAt) * inkK;

        void StopFollowing()
        {
            if (active != null) foreach (var ps in active.TipSystems) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            following = false;
        }

        void LateUpdate()
        {
            if (!Bind()) return;
            float now = Time.time, dt = Time.deltaTime, inkNow = InkNow(now);
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
                        samples.Add(new Sample { Point = tip, Time = inkNow, Width = width });
                }
                previous = tip; has = true;
                if (!hidden && now > originPinnedUntil) { originTip = tip; originAt = now; }
                if (!apexDone && now >= apexAt) { apexDone = true; AtApex(tip); }
                if (now >= followUntil) StopFollowing();
            }
            StepBridge(now, eye);
            // the stroke ages whether the tip is still followed or not (on the ink clock: D308-90)
            while (samples.Count > 0 && inkNow - samples[0].Time > strokeSeconds) samples.RemoveAt(0);
            if (strokeRenderer != null && strokeRenderer.enabled)
            {
                if (samples.Count < 2) { strokeMesh.Clear(); if (!following) strokeRenderer.enabled = false; }
                else BuildRibbon(strokeMesh, samples, inkNow, strokeSeconds, eye, vertices, uvs, colours, triangles, false, Front(now));
            }
        }

        // D308-90: how far the pigment has soaked back from the tip (real time from the apex; nothing before it)
        SoakFront Front(float now)
        {
            if (soak < 0 || Profile.Soaks == null || soak >= Profile.Soaks.Length || now <= apexAt) return default;
            var row = Profile.Soaks[soak];
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - apexAt) / row.Seconds));
            return new SoakFront { On = true, ApexAt = apexAt, Front = (soakReach + row.Soft) * progress, Soft = row.Soft, Max = soakMax, Pigment = soakPigment };
        }

        void AtApex(Vector3 tip)
        {
            if (active == null) return;
            Vector3 flat = transform.forward; flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            if (active.Apex != null)
            {
                // the ink leaves the way the tip was going, never back at the caster
                // Play 2026-10-14 (sw_j): a sideways swing threw its ink sideways - in third person straight at the camera. The
                // ink goes where the caster faces; the tip's own direction only leans it (profile.ApexLean).
                Vector3 way = velocity.sqrMagnitude > .25f ? velocity.normalized : flat;
                if (Vector3.Dot(way, flat) < 0f) way = Vector3.Reflect(way, flat);
                way = (flat + way * Mathf.Clamp01(Profile.ApexLean)).normalized;
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

        // The apex plan of this swing (CastPlanned is raised inside the apex dispatch, before the spell is spawned).
        void OnPlanned(CastPlan plan)
        {
            try
            {
                if (plan == null || !following || entry < 0 || rig == null || rig.EffectTip == null) return;
                float now = Time.time;
                if (now < apexAt - .05f || now > apexAt + .25f) return;   // not this swing's apex
                Vector3 tip = has ? previous : rig.EffectTip.position;
                var e = Profile.States[entry];
                if (bridgeRenderer != null) bridgeRenderer.sharedMaterial = e.Stroke;
                bridgePath.Clear(); bridgeAt.Clear(); bridge.Clear(); runNext = int.MaxValue;
                if (plan.Area == null || plan.Area.Shape == AreaShape.Volley)   // volley: the same stem, toward its first shot / fallback point
                {
                    // single: a stem from the tip toward the target, the spell leaves from its end
                    var target = plan.Hits.Count > 0 && plan.Hits[0].Target != null ? plan.Hits[0].Target.transform : null;
                    Vector3 flat = transform.forward; flat.y = 0f; flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                    if (target == null && plan.Area != null && plan.Area.Shots.Count > 0 && plan.Area.Shots[0].Target != null) target = plan.Area.Shots[0].Target.transform;
                    Vector3 end = target != null ? target.position + Vector3.up * 1.1f : plan.Area != null ? plan.Area.Point + Vector3.up * 1.1f : tip + flat * 10f;
                    Vector3 to = end - tip; float distance = to.magnitude;
                    if (distance < .3f) return;
                    float length = Mathf.Clamp(distance * .2f, .8f, 2f);
                    Vector3 stem = tip + to / distance * length;
                    for (int i = 0; i <= 8; i++) { bridgePath.Add(Vector3.Lerp(tip, stem, i / 8f)); bridgeAt.Add(now + .1f * i / 8f); }
                    bridgeFlat = false; bridgeWidth = .16f; bridgeDry = .55f;   // Play sw_m: .1 was lost in the apex ink
                    originTip = stem; originAt = now; originPinnedUntil = now + OriginFresh;   // the body forms at the stem's end (Vfx120LaunchPoint)
                }
                else if (plan.Area.Shape == AreaShape.Circle || plan.Area.Shape == AreaShape.Path || plan.Area.Shape == AreaShape.Cone)
                {
                    // circle: the ink runs over the ground from the struck point to the centre, arriving before the judgement
                    // path: to where the path starts; cone: a short run down the cone's direction (the fan rises out of it)
                    Vector3 from = GroundUnder(tip), centre = plan.Area.Point;
                    bool cone = plan.Area.Shape == AreaShape.Cone;
                    if (cone) { Vector3 d = plan.Area.Direction; d.y = 0f; d = d.sqrMagnitude > 1e-4f ? d.normalized : transform.forward; centre = from + d * Profile.ConeRunMeters; }
                    Vector3 run = centre - from; run.y = 0f; float distance = run.magnitude;
                    float travel = Mathf.Clamp(Mathf.Min(plan.Area.Delay - .1f, .3f), .08f, .3f);
                    int steps = Mathf.Clamp(Mathf.CeilToInt(distance / .5f), 2, 48);
                    for (int i = 0; i <= steps; i++)
                    {
                        float u = i / (float)steps;
                        bridgePath.Add(GroundAt(Vector3.Lerp(from, centre, u), from.y)); bridgeAt.Add(now + travel * u);
                    }
                    bridgeFlat = true; bridgeWidth = Mathf.Lerp(.85f, .45f, Mathf.Clamp01(distance / 20f)); bridgeDry = 1.5f;   // Play sw_m: .42 / .22 were not seen on the ground
                    poolAt = bridgePath[bridgePath.Count - 1]; poolAtTime = cone ? float.PositiveInfinity : now + travel;   // a cone has no pool at its end
                    var runner = Pool();
                    if (runner != null && runner.Run != null) { runner.Run.Play(true); runNext = 0; runSize = bridgeWidth * Profile.RunBlotScale; }
                }
                else return;
                bridgeUntil = bridgeAt[bridgeAt.Count - 1] + bridgeDry;
                if (bridgeRenderer != null) bridgeRenderer.enabled = e.Stroke != null;
                Bridges++;
            }
            catch (System.Exception ex) { Debug.LogException(ex, this); }
        }

        void StepBridge(float now, Vector3 eye)
        {
            if (poolAtTime <= now)
            {
                poolAtTime = float.PositiveInfinity;
                var made = Pool();
                if (made != null && made.Ground != null)
                {
                    Vector3 flat = transform.forward; flat.y = 0f;
                    made.Ground.SetPositionAndRotation(poolAt, Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up));
                    foreach (var ps in made.GroundSystems) { ps.Clear(true); ps.Play(true); }
                }
            }
            StepRun(now);
            if (bridgeRenderer == null || !bridgeRenderer.enabled) return;
            if (now > bridgeUntil) { bridgeRenderer.enabled = false; bridgeMesh.Clear(); return; }
            bridge.Clear();
            for (int i = 0; i < bridgePath.Count; i++)
            {
                if (bridgeAt[i] > now) break;   // the ink has not got there yet
                if ((eye - bridgePath[i]).sqrMagnitude < Profile.HideWithinCamera * Profile.HideWithinCamera) continue;
                bridge.Add(new Sample { Point = bridgePath[i], Time = bridgeAt[i], Width = bridgeWidth });
            }
            if (bridge.Count < 2) { bridgeMesh.Clear(); return; }
            BuildRibbon(bridgeMesh, bridge, now, bridgeDry, eye, vertices, uvs, colours, triangles, bridgeFlat);
        }

        // blot the ground where the running ink has got to (one blot per bridge point, a little jitter so the line is not a chain)
        void StepRun(float now)
        {
            if (runNext >= bridgePath.Count || pool == null || pool.Run == null) return;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            while (runNext < bridgePath.Count && bridgeAt[runNext] <= now)
            {
                Vector2 j = Random.insideUnitCircle * runSize * .12f;
                ep.position = bridgePath[runNext] + new Vector3(j.x, Profile.RunLift, j.y);
                ep.startSize = runSize * Random.Range(.85f, 1.15f);
                ep.rotation = Random.Range(0f, 360f);
                pool.Run.Emit(ep, 1);
                for (int k = 1; k < Profile.RunBlotsPerPoint; k++) { Vector2 o = Random.insideUnitCircle * runSize * .25f; ep.position += new Vector3(o.x, 0f, o.y); ep.rotation = Random.Range(0f, 360f); pool.Run.Emit(ep, 1); }
                runNext++;
            }
        }

        // the arrival pool: its own copy of the slam's prefab, so the strike's own ground ink under the tip keeps playing
        Instance Pool()
        {
            if (pool != null) return pool.Root != null ? pool : null;
            pool = new Instance();
            int slam = Profile.IndexOf("SwingSlam");
            var prefab = slam >= 0 ? Profile.States[slam].Prefab : null;
            if (prefab != null) { pool.Root = Instantiate(prefab); pool.Root.name = prefab.name + " (bridge pool)"; pool.Ground = pool.Root.transform.Find(GroundName); }
            pool.TipSystems = new ParticleSystem[0]; pool.ApexSystems = new ParticleSystem[0]; pool.GroundSystems = Systems(pool.Ground);
            var runPart = pool.Root != null ? pool.Root.transform.Find(RunName) : null;
            pool.Run = runPart != null ? runPart.GetComponent<ParticleSystem>() : null;
            return pool.Root != null ? pool : null;
        }

        Vector3 GroundAt(Vector3 at, float fallbackY)
        {
            Transform own = transform.root;
            int count = Physics.RaycastNonAlloc(new Vector3(at.x, at.y + 3f, at.z), Vector3.down, hits, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; Vector3 point = new Vector3(at.x, fallbackY, at.z);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.root == own || hits[i].distance >= best) continue;
                best = hits[i].distance; point = hits[i].point;
            }
            return point + Vector3.up * .04f;
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

        /// <summary>D308-90: the soak front for one ribbon build (default = no soak).</summary>
        public struct SoakFront { public bool On; public float ApexAt, Front, Soft, Max; public Color Pigment; }

        static readonly List<Vector2> soakUv = new List<Vector2>(192);

        // The stroke as a ribbon that faces the eye. Samples run oldest -> newest (the tip). UV.x: 0 at the tip, 1 at the
        // oldest end of the stroke's life; colour alpha: the ink left (the shader breaks the stroke with it, it does not fade it).
        // D308-90: UV1.x = how much of the soak pigment (colour rgb) the ink has taken; 0 everywhere without a soak front.
        // Shared with the edit-mode pose (Vfx308Build swingink:pose), so a still shows what the game builds.
        public static void BuildRibbon(Mesh mesh, List<Sample> list, float now, float seconds, Vector3 eye,
            List<Vector3> v, List<Vector2> uv, List<Color> c, List<int> tri, bool lieFlat = false, SoakFront soak = default)
        {
            v.Clear(); uv.Clear(); c.Clear(); tri.Clear(); soakUv.Clear();
            int n = list.Count;
            if (n < 2) { mesh.Clear(); return; }
            for (int i = 0; i < n; i++)
            {
                var s = list[i];
                Vector3 along = list[Mathf.Min(i + 1, n - 1)].Point - list[Mathf.Max(i - 1, 0)].Point;
                Vector3 side = Vector3.Cross(along, lieFlat ? Vector3.up : eye - s.Point);   // lieFlat: the bridge's ink on the ground (D308-84)
                side = side.sqrMagnitude > 1e-10f ? side.normalized : Vector3.up;
                float age = Mathf.Clamp01((now - s.Time) / Mathf.Max(.01f, seconds));
                // a rounded head at the tip, a body that narrows a little as it dries
                float head = Mathf.Clamp01((n - 1 - i) / 2.5f);
                float width = s.Width * Mathf.Lerp(.45f, 1f, Mathf.Sqrt(head)) * Mathf.Lerp(1f, .7f, age);
                float left = 1f - Mathf.SmoothStep(.45f, 1f, age);
                v.Add(s.Point - side * (width * .5f)); v.Add(s.Point + side * (width * .5f));
                uv.Add(new Vector2(age, 0f)); uv.Add(new Vector2(age, 1f));
                // D308-90: the soak runs back from the tip - a sample laid u seconds before the apex takes it once the front passes u
                float soaked = 0f;
                if (soak.On && soak.Soft > 0f) soaked = soak.Max * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((soak.Front - Mathf.Max(0f, soak.ApexAt - s.Time)) / soak.Soft));
                soakUv.Add(new Vector2(soaked, 0f)); soakUv.Add(new Vector2(soaked, 0f));
                var colour = soak.On ? new Color(soak.Pigment.r, soak.Pigment.g, soak.Pigment.b, left) : new Color(1f, 1f, 1f, left);
                c.Add(colour); c.Add(colour);
                if (i == 0) continue;
                int a = (i - 1) * 2;
                tri.Add(a); tri.Add(a + 1); tri.Add(a + 2); tri.Add(a + 1); tri.Add(a + 3); tri.Add(a + 2);
            }
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetUVs(1, soakUv); mesh.SetColors(c); mesh.SetTriangles(tri, 0, false);
            mesh.RecalculateBounds();
        }
    }
}
