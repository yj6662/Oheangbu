using System;
using System.Collections.Generic;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    /// <summary>D308-150 V9: the Unity side of the vol.2 legacy bridge. It plays, for a vol.2 cast of a no-batchim glyph, what the OLD main game plays for that glyph:
    ///  - the swing of the great brush (SpellSwing308.PlayShow308: the same motion, marked show-only so the vol.2 swing driver never hits with it) with the old
    ///    category's state and position variant (SpellSwingProfile308: single / area / parry / ward);
    ///  - the brush-tip ink of that swing and its stem / ground run (SwingInk308.ShowSwing308 / ShowPlan308 = the old OnBegan / OnPlanned by hand);
    ///  - the glyph's own body from the main game's visual set (or, for a ward letter, the ward profile's membrane), started exactly as the old stroke adapter
    ///    starts it (BrushStrokeFeedAdapter.SpawnPattern, authored branch): SetImpactClock / SetAreaPlan / SetGuardClock, Begin from the brush tip
    ///    (Vfx120LaunchPoint), SelectBambooGuard / SelectFireGuard. The body lands and dies on its own clock (Vfx120Effect destroys itself).
    /// Presentation only: nothing here is read by a rule. The shared prefab / profile assets are never written; a ward body gets a private DontSave profile
    /// copy (what EAWardRuntime does), destroyed with it. One instance per vol.2 presenter; no statics.</summary>
    public sealed class Vol2LegacyStage : IVol2LegacyStage, IDisposable
    {
        sealed class Live
        {
            public GameObject Go; public Vfx120Effect Fx; public Vfx120Profile Copy; public char Letter; public Vol2LegacyKind Kind;
            public bool Ended; public int Contacts; public string Prefab = "";
        }

        /// <summary>TEST (presentation): how long the old ward membrane stands for the instant ㅜ ring. 0 = as long as the old ward did (the ward profile's Duration, 6 s in the
        /// lab): the membrane's ink loops stop emitting Aura308FadeSeconds (1 s) before the end, so a short cue (1.45 s was tried) shows almost nothing - faithful by default.</summary>
        public float WardCueSeconds = 0f;
        /// <summary>TEST (presentation): draw the old membrane on the vol.2 ring's band (true) or at the old ward's own radius (false, the default: faithful). Captured 2026-10-25:
        /// on the 2 m band the membrane's ground ink sits under the caster and the bear and is not seen from the shoulder camera; at the old 3 m it reads like the old ward.</summary>
        public bool WardOnRingBand = false;
        /// <summary>TEST (presentation): where the old guard (the parry letter's body) stands when there is no drawn letter: this far in front of the caster's chest.</summary>
        public float GuardAhead = 0.75f, GuardHeight = 1.35f;

        readonly Vol2CastCoordinator _coord;
        readonly CombatLoopWiring _wiring;
        readonly Func<Vector3?> _tip;
        readonly Dictionary<int, Live> _live = new Dictionary<int, Live>();
        readonly List<int> _dead = new List<int>();
        World.SpellSwing308 _swing; World.SwingInk308 _ink;
        int _swingSearches, _inkSearches, _next, _spikeSeed, _seedSeed, _volleySeed;

        public int Begun { get; private set; }
        public int Missing { get; private set; }
        public int SwingsShown { get; private set; }
        public int InkShown { get; private set; }
        public int StemsShown { get; private set; }
        public int CopiesMade { get; private set; }
        public int CopiesFreed { get; private set; }
        public int Released { get; private set; }
        public string LastPrefab { get; private set; } = "";
        public string LastNote { get; private set; } = "";
        public int LiveCount { get { Sweep(); return _live.Count; } }

        public Vol2LegacyStage(Vol2CastCoordinator coordinator, CombatLoopWiring wiring, Func<Vector3?> brushTip)
        { _coord = coordinator; _wiring = wiring; _tip = brushTip; }

        static Element ElementOf(Body b) => (Element)Enum.Parse(typeof(Element), b.ToString());

        World.SpellSwing308 Swinger()
        {
            if (_swing == null && _swingSearches < 40 && Application.isPlaying) { _swingSearches++; _swing = Object.FindFirstObjectByType<World.SpellSwing308>(); }
            return _swing;
        }

        World.SwingInk308 Ink()
        {
            if (_ink == null && _inkSearches < 40 && Application.isPlaying) { _inkSearches++; _ink = Object.FindFirstObjectByType<World.SwingInk308>(); }
            return _ink;
        }

        Transform TargetOf(long id)
        {
            if (id == 0 || _coord == null) return null;
            return _coord.Registry.Find(id) is Vol2EnemyTarget e && e.Vitals != null ? e.Vitals.transform : null;
        }

        // ---------------------------------------------------------------- the swing (old SwingBegan308 subscribers, by hand)

        /// <summary>The old category state and its position variant (CombatLoopWiring.SwingVariant308, same rule), the lock, and the ward lead.</summary>
        public static string StateFor(Data.Spell.SpellSwingProfile308 p, Vol2LegacyKind kind, AreaShape area, float distance, out float lockSeconds, out float lead)
        {
            lead = 0f; lockSeconds = 0.4f;
            if (p == null) return kind == Vol2LegacyKind.Single ? "SwingSweep" : kind == Vol2LegacyKind.Area ? "SwingArc" : kind == Vol2LegacyKind.Guard ? "SwingPlant" : "SwingSlam";
            var rule = p.Position;
            switch (kind)
            {
                case Vol2LegacyKind.Single:
                    lockSeconds = p.AttackSingle.LockSeconds;
                    return rule.Enabled && distance >= rule.SingleFarMeters && !string.IsNullOrEmpty(rule.SingleFarState) ? rule.SingleFarState : p.AttackSingle.State;
                case Vol2LegacyKind.Area:
                {
                    lockSeconds = p.AttackArea.LockSeconds;
                    string picked = null;
                    if (rule.Enabled)
                    {
                        if (area == AreaShape.Circle) picked = distance >= rule.CircleFarMeters ? rule.CircleFarState : rule.CircleNearState;
                        else if (area == AreaShape.Path) picked = rule.PathState;
                    }
                    return string.IsNullOrEmpty(picked) ? p.AttackArea.State : picked;
                }
                case Vol2LegacyKind.Guard:
                    lockSeconds = p.Parry.LockSeconds;
                    return p.Parry.State;
                default:
                    lockSeconds = p.Ward.LockSeconds;
                    if (rule.Enabled && !string.IsNullOrEmpty(rule.WardState)) { lead = rule.WardLeadSeconds; return rule.WardState; }
                    return p.Ward.State;
            }
        }

        AreaShape AreaOf(char letter)
        {
            var row = _wiring != null ? _wiring.LegacyRow308(letter) : null;
            return row != null ? row.AreaShape : AreaShape.Cone;
        }

        public void Swing(in Vol2LegacySwing s)
        {
            if (!Application.isPlaying) return;
            try
            {
                var profile = _wiring != null ? _wiring.SwingProfile308 : null;
                string state = StateFor(profile, s.Kind, s.Kind == Vol2LegacyKind.Area ? AreaOf(s.Letter) : AreaShape.None, s.Distance, out float lockSeconds, out float lead);
                float now = Time.time;
                // a waiting category comes down at its launch; the parry starts its clip from the start (the old apex 0); the ward starts its short slam `lead` before the apex
                float toApex = s.Kind == Vol2LegacyKind.Ward && lead > 0f ? lead : s.FromClipStart ? 10f : s.ToApex;
                var sw = Swinger();
                if (sw != null && sw.PlayShow308(state, toApex, Mathf.Max(lockSeconds, s.FromClipStart ? 0f : s.ToApex))) SwingsShown++;
                var ink = Ink();
                float apexTime = s.FromClipStart ? now : now + s.ToApex;
                if (ink != null && ink.ShowSwing308(s.Letter, ElementOf(s.Body), state, apexTime, s.Kind == Vol2LegacyKind.Ward ? lead : 0f)) InkShown++;
                LastNote = "swing " + state + " toApex " + toApex.ToString("0.00");
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ---------------------------------------------------------------- the body (old BrushStrokeFeedAdapter.SpawnPattern, authored branch)

        GameObject PrefabFor(in Vol2LegacyCue cue)
        {
            if (cue.Kind == Vol2LegacyKind.Ward && _wiring != null && _wiring.EAWards != null)
            {
                var ward = _wiring.EAWards.PresentationPrefab308(cue.Letter);
                if (ward != null) return ward;
            }
            var set = _wiring != null ? _wiring.VisualSet308 : null;
            return set != null && set.TryGet(cue.Letter, out var v) ? v.FxPrefab : null;
        }

        public int Begin(in Vol2LegacyCue cue)
        {
            if (!Application.isPlaying) return 0;
            Sweep();
            var prefab = PrefabFor(cue);
            if (prefab == null) { Missing++; LastNote = "no old body for " + cue.Letter; return 0; }
            var live = new Live { Letter = cue.Letter, Kind = cue.Kind, Prefab = prefab.name };
            try
            {
                var go = Object.Instantiate(prefab);
                live.Go = go; go.name = "Vol2Legacy_" + cue.Letter + "_" + cue.Kind;
                if (_wiring != null && go.scene != _wiring.gameObject.scene) SceneManager.MoveGameObjectToScene(go, _wiring.gameObject.scene);
                go.SetActive(true);
                live.Fx = go.GetComponent<Vfx120Effect>();
                if (live.Fx == null || live.Fx.Profile == null) { Release(live); Missing++; LastNote = "no Vfx120Effect / profile on " + prefab.name; return 0; }
                live.Fx.PreviewControlled = false; live.Fx.DemonstrationCues = false;
                switch (cue.Kind)
                {
                    case Vol2LegacyKind.Single: BeginSingle(live, cue); break;
                    case Vol2LegacyKind.Area: BeginArea(live, cue); break;
                    case Vol2LegacyKind.Guard: BeginGuard(live, cue); break;
                    default: BeginWard(live, cue); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Release(live); return 0; }
            int id = ++_next; if (id == 0) id = ++_next;
            _live.Add(id, live); Begun++; LastPrefab = live.Prefab;
            return id;
        }

        /// <summary>Where the old route's letter stood: the centre of the stroke adapter's drawing surface in front of the game camera (the old guard stands there and a
        /// launch without a fresh brush tip leaves from there). Without an adapter / camera: in front of the caster's chest.</summary>
        Vector3 LetterCentre(in Vol2LegacyCue cue)
        {
            var cam = Camera.main;
            var adapter = _wiring != null ? _wiring.BrushAdapter308 : null;
            if (cam != null && adapter != null) return cam.transform.position + cam.transform.forward * adapter.EffectiveSurfaceDistance(cam);
            return cue.Origin + Vector3.up * GuardHeight + cue.Forward * GuardAhead;
        }

        // the old adapter's launch: Vfx120LaunchPoint.Resolve(profile, camera, letter centre) - the swung brush tip / stem end when the swing ink is fresh, the letter
        // centre otherwise; the bridge passes the same fallback (the scene's brush tip only when there is no adapter to say where the letter was)
        Vector3 LaunchFallback(in Vol2LegacyCue cue)
        {
            if (_wiring != null && _wiring.BrushAdapter308 != null && Camera.main != null) return LetterCentre(cue);
            var tip = _tip != null ? _tip() : null;
            return tip ?? LetterCentre(cue);
        }

        void BeginSingle(Live live, in Vol2LegacyCue cue)
        {
            var target = cue.HasTarget ? TargetOf(cue.TargetId) : null;
            // the stem from the tip toward the target (the body forms at its end), as the old apex plan made it
            var plan = new CastPlan();
            if (target != null) plan.Hits.Add(new PlannedHit { Target = target.GetComponent<EnemyVitals>(), ImpactTime = Time.time + cue.ImpactClock, LaunchTime = Time.time });
            var ink = Ink();
            if (ink != null && ink.ShowPlan308(plan)) StemsShown++;
            Vector3 origin = Vfx120LaunchPoint.Resolve(live.Fx.Profile, Camera.main, LaunchFallback(cue));
            Vector3 miss = cue.FallbackPoint + Vector3.up * 1.1f;
            if (cue.ImpactClock > 0f) live.Fx.SetImpactClock(cue.ImpactClock);
            live.Fx.Begin(origin, target, miss, live.Fx.Profile.Pigment);
        }

        void BeginArea(Live live, in Vol2LegacyCue cue)
        {
            var target = cue.HasTarget ? TargetOf(cue.TargetId) : null;
            var cast = new CastPlan { Area = AreaPlan(cue, target) };
            var area = cast.Area;
            foreach (var shot in area.Shots) cast.Hits.Add(shot);
            var ink = Ink();
            if (ink != null && ink.ShowPlan308(cast)) StemsShown++;
            // the old adapter: circle / path aim at the target, the cone at nothing, the volley at its first candidate; the fallback is the plan's point
            Transform aim = area.Shape == AreaShape.Cone ? null : area.Shape == AreaShape.Volley ? (area.Shots.Count > 0 && area.Shots[0].Target != null ? area.Shots[0].Target.transform : null) : target;
            Vector3 fallback = area.Shape == AreaShape.Cone ? area.Point + AreaGeometry.Flat(area.Direction).normalized * Mathf.Max(1f, area.Length) : area.Point;
            Vector3 origin = Vfx120LaunchPoint.Resolve(live.Fx.Profile, Camera.main, LaunchFallback(cue));
            live.Fx.SetAreaPlan(area);
            live.Fx.Begin(origin, aim, fallback, live.Fx.Profile.Pigment);
        }

        /// <summary>The old area plan of the glyph (CombatLoopWiring.PlanCone / PlanCircle / PlanPath / PlanVolley geometry, word for word, WITHOUT scheduling anything):
        /// the legacy row's numbers, the vol.2 cast's position and aim, the target the vol.2 fan touches first. Presentation only.</summary>
        AreaImpactPlan AreaPlan(in Vol2LegacyCue cue, Transform target)
        {
            var row = _wiring != null ? _wiring.LegacyRow308(cue.Letter) : null;
            var cfg = _wiring != null ? _wiring.Config308 : null;
            var spec = row != null ? row.ToAreaSpec() : new AreaSpec(AreaShape.Cone, 0f, 0f, 0f, 0f, 0f, 0, 0f);
            float cfgDelay = cfg != null ? cfg.AreaImpactDelay : 0.3f;
            float delay = spec.ImpactDelay > 0f ? spec.ImpactDelay : cfgDelay;
            Vector3 origin = cue.Origin, forward = AreaGeometry.Flat(cue.Forward).normalized;
            switch (spec.Shape)
            {
                case AreaShape.Circle:
                {
                    float radius = spec.Radius > 0f ? spec.Radius : 2.5f;
                    float aimRange = spec.Length > 0f ? spec.Length : 10f;
                    Vector3 centre = target != null ? target.position : origin + forward * Mathf.Min(aimRange, Mathf.Max(1f, cue.FanDepth));
                    var plan = new AreaImpactPlan { Shape = AreaShape.Circle, Point = centre, Radius = radius, Delay = delay };
                    if (row != null && row.F(SpellGrammar308.SpikesKey, 0f) > 0f) AreaSpikePlanner.Fill(plan, ++_spikeSeed, AreaSpikeSpec.From(row));
                    return plan;
                }
                case AreaShape.Path:
                {
                    float halfWidth = spec.Radius > 0f ? spec.Radius : 1.5f, length = spec.Length > 0f ? spec.Length : 14f, speed = spec.Speed > 0f ? spec.Speed : 6f;
                    Vector3 direction = target != null ? AreaGeometry.Flat(target.position - origin) : forward;
                    direction = direction.sqrMagnitude > 0.001f ? direction.normalized : forward;
                    return new AreaImpactPlan { Shape = AreaShape.Path, Point = origin, Direction = direction, Radius = halfWidth, Length = length, Speed = speed, Delay = delay,
                        VisualSeed = row != null && row.F(SpellGrammar308.SeedKey, 0f) > 0f ? ++_seedSeed : 0 };
                }
                case AreaShape.Volley:
                    return Volley(spec, delay, origin, forward);
                default:
                    return new AreaImpactPlan { Shape = AreaShape.Cone, Point = origin, Direction = forward, Length = cfg != null ? cfg.AreaConeRange : cue.FanDepth, Delay = delay, Angle = cfg != null ? cfg.AreaConeAngle : 30f };
            }
        }

        AreaImpactPlan Volley(in AreaSpec spec, float delay, Vector3 origin, Vector3 forward)
        {
            float halfAngle = spec.Angle > 0f ? spec.Angle : 25f, range = spec.Length > 0f ? spec.Length : 16f, speed = spec.Speed > 0f ? spec.Speed : 32f;
            int shots = spec.Shots > 0 ? spec.Shots : 9; float interval = spec.Interval > 0f ? spec.Interval : 0.1f;
            float now = Time.time;
            var candidates = new List<(EnemyVitals vitals, float angle)>();
            if (_coord != null)
                foreach (var t in _coord.Targets)
                {
                    if (!(t is Vol2EnemyTarget e) || e.Vitals == null || !e.Vitals.IsAlive) continue;
                    if (!AreaGeometry.InCone(origin, forward, e.Vitals.transform.position, halfAngle, range, out float angle, out _)) continue;
                    candidates.Add((e.Vitals, angle));
                }
            candidates.Sort((a, b) => a.angle.CompareTo(b.angle));
            var plan = new AreaImpactPlan { Shape = AreaShape.Volley, Direction = forward, Length = range, Speed = speed, Delay = delay, Radius = halfAngle, CreatedAt = now, ShotCount = shots, ShotInterval = interval };
            if (spec.ScatterVolley)
            {
                var random = new System.Random(++_volleySeed); int[] order = new int[shots];
                for (int i = 0; i < shots; i++) order[i] = i;
                for (int i = shots - 1; i > 0; i--) { int j = random.Next(i + 1); int n = order[i]; order[i] = order[j]; order[j] = n; }
                for (int i = 0; i < shots; i++)
                {
                    float angle = ((order[i] + .2f + (float)random.NextDouble() * .6f) / shots * 2 - 1) * halfAngle;
                    Vector3 ray = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                    EnemyVitals hitTarget = null; float distance = range;
                    foreach (var c in candidates)
                    {
                        Vector3 relative = AreaGeometry.Flat(c.vitals.transform.position - origin); float along = Vector3.Dot(relative, ray);
                        if (along <= 0 || along > distance || (relative - ray * along).sqrMagnitude > .65f * .65f) continue;
                        hitTarget = c.vitals; distance = along;
                    }
                    float launch = now + delay + i * interval, impact = launch + distance / speed;
                    Vector3 point = origin + ray * distance + Vector3.up * (.65f + (float)random.NextDouble() * .8f);
                    plan.Shots.Add(new PlannedHit { Target = hitTarget, ImpactTime = impact, LaunchTime = launch, HasImpactPoint = true, ImpactPoint = point });
                }
                plan.Point = origin + forward * range;
                return plan;
            }
            if (candidates.Count == 0) { plan.Point = origin + forward * range; return plan; }
            for (int i = 0; i < shots; i++)
            {
                var target = candidates[i % candidates.Count].vitals;
                float flight = Vector3.Distance(origin, target.transform.position) / speed;
                plan.Shots.Add(new PlannedHit { Target = target, ImpactTime = now + delay + i * interval + flight, LaunchTime = now + delay + i * interval });
            }
            plan.Point = candidates[0].vitals.transform.position;
            return plan;
        }

        void BeginGuard(Live live, in Vol2LegacyCue cue)
        {
            var cfg = _wiring != null ? _wiring.Config308 : null;
            float duration = cfg != null ? cfg.GuardDuration : 1.2f;
            float window = cue.GuardWindow > 0f ? cue.GuardWindow : cfg != null ? cfg.ParryWindow : 0.5f;
            live.Fx.SetGuardClock(Mathf.Max(duration, window), window);
            // the old guard stands where the letter was drawn (the adapter's surface in front of the eye); the bamboo / pattern shield faces the camera's way
            Vector3 centre = LetterCentre(cue);
            Vector3 direction = centre;
            if (Vfx120Effect.IsBambooGuard(live.Fx.Profile) || live.Fx.Profile.KtpPatternShield)
            {
                var cam = Camera.main;
                Vector3 facing = cam != null ? cam.transform.forward : cue.Forward; facing.y = 0f;
                if (facing.sqrMagnitude < .001f) facing = cue.Forward;
                direction += facing.normalized;
            }
            live.Fx.Begin(centre, null, direction, live.Fx.Profile.Pigment);
            Vfx120Effect.SelectBambooGuard(live.Fx); Vfx120Effect.SelectFireGuard(live.Fx);
        }

        void BeginWard(Live live, in Vol2LegacyCue cue)
        {
            // a private copy of the membrane's profile (the shared asset is never written): the ring's band radius, the ward profile's height / formation / fade, a cue life
            var source = live.Fx.Profile;
            var copy = Object.Instantiate(source); copy.hideFlags = HideFlags.DontSave; CopiesMade++;
            live.Copy = copy;
            var ward = _wiring != null && _wiring.EAWards != null ? _wiring.EAWards.Profile308 : null;
            float life = WardCueSeconds > 0f ? WardCueSeconds : ward != null ? ward.Duration : source.Duration;
            copy.Duration = life;
            if (WardOnRingBand && cue.RingRadius > 0f) copy.WardRadius = cue.RingRadius;      // optional: on the band the vol.2 ring judges (the old ward stands at its own 3 m)
            if (ward != null) { copy.WardHeight = ward.Height; copy.WardFormation = Mathf.Min(ward.Formation, life * 0.4f); copy.WardFade = Mathf.Min(ward.Fade, life * 0.4f); }
            copy.WardGroundProbeHeight = .6f; copy.WardGroundProbeDepth = 3f;
            live.Fx.Profile = copy;
            Vector3 centre = cue.Origin;
            live.Fx.Begin(centre, null, centre + cue.Forward, copy.Pigment);
        }

        // ---------------------------------------------------------------- bookkeeping

        public void Contact(int handle, long targetId, Vector3 point)
        {
            if (_live.TryGetValue(handle, out var live)) live.Contacts++;
        }

        public bool Alive(int handle)
        {
            Sweep();
            return _live.TryGetValue(handle, out var live) && live.Go != null;
        }

        public void End(int handle, bool immediate)
        {
            if (!_live.TryGetValue(handle, out var live)) return;
            live.Ended = true;
            if (!immediate && live.Go != null) return;          // it finishes its own life; Sweep frees it then
            _live.Remove(handle);
            Release(live);
        }

        /// <summary>Bodies that ran out (Vfx120Effect destroyed itself) are forgotten and their profile copies freed. Called by the view every frame.</summary>
        public void Sweep()
        {
            _dead.Clear();
            foreach (var kv in _live) if (kv.Value.Go == null) _dead.Add(kv.Key);
            foreach (int id in _dead) { var live = _live[id]; _live.Remove(id); Release(live); }
        }

        void Release(Live live)
        {
            if (live.Go != null) { if (Application.isPlaying) Object.Destroy(live.Go); else Object.DestroyImmediate(live.Go); }
            if (live.Copy != null) { if (Application.isPlaying) Object.Destroy(live.Copy); else Object.DestroyImmediate(live.Copy); CopiesFreed++; }
            live.Go = null; live.Copy = null; live.Fx = null;
            Released++;
        }

        /// <summary>The hard stop (the view's ClearAll, the scope's end): every body and profile copy now.</summary>
        public void ClearAll()
        {
            foreach (var live in _live.Values) Release(live);
            _live.Clear();
        }

        public void Dispose() { ClearAll(); }
    }
}
