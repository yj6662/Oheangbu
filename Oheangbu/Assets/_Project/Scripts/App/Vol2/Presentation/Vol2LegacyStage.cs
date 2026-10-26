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

        /// <summary>Step 4: the old route swung a glyph by its old row's category (SwingEntry308): an install / summon / field slams, a buff draws, a domain is laid like a
        /// ward. The batchim glyphs keep their old swing this way; an attack / parry / ward glyph (or no row) keeps the frame's kind (<see cref="StateFor"/>).</summary>
        public static bool CategoryState(Data.Spell.SpellSwingProfile308 p, SpellCategory category, out string state, out float lockSeconds)
        {
            state = null; lockSeconds = 0.4f;
            if (p == null) return false;
            Data.Spell.SpellSwingProfile308.Entry e;
            switch (category)
            {
                case SpellCategory.Install: e = p.Install; break;
                case SpellCategory.Summon: e = p.Summon; break;
                case SpellCategory.Field: e = p.Field; break;
                case SpellCategory.Buff: e = p.Buff; break;
                case SpellCategory.Domain: e = p.Ward; break;
                default: return false;
            }
            if (string.IsNullOrEmpty(e.State)) return false;
            state = e.State; lockSeconds = e.LockSeconds;
            return true;
        }

        public void Swing(in Vol2LegacySwing s)
        {
            if (!Application.isPlaying) return;
            try
            {
                var profile = _wiring != null ? _wiring.SwingProfile308 : null;
                string state = StateFor(profile, s.Kind, s.Kind == Vol2LegacyKind.Area ? AreaOf(s.Letter) : AreaShape.None, s.Distance, out float lockSeconds, out float lead);
                var row = _wiring != null ? _wiring.LegacyRow308(s.Letter) : null;
                if (row != null && CategoryState(profile, row.Category, out string byCategory, out float catLock)) { state = byCategory; lockSeconds = catLock; lead = 0f; }
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
            // step 4: 각 낙 삭 악 - the old route presented them through EAGiyeokRuntime (its profile's own ink bodies), not through the visual set's catalogue prefab
            if (cue.Kind == Vol2LegacyKind.Single && _wiring != null && _wiring.EAGiyeok != null && EAGiyeokRuntime.Owns(cue.Letter))
            {
                var g = _wiring.EAGiyeok.PresentationPrefab308(cue.Letter);
                if (g != null) return g;
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
                    case Vol2LegacyKind.Self:
                        {
                            // the old stroke adapter's branch for a glyph that is neither an attack nor a parry: the body stands where the letter was drawn
                            Vector3 c = LetterCentre(cue);
                            live.Fx.Begin(c, null, c, live.Fx.Profile.Pigment);
                            break;
                        }
                    case Vol2LegacyKind.Domain:
                        // the old 결계 / field cast: the glyph's cue body at the caster's feet (its rim is the recipe's lasting part)
                        live.Fx.Begin(cue.Origin, null, cue.Origin + cue.Forward, live.Fx.Profile.Pigment);
                        break;
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

        // ---------------------------------------------------------------- step 4: the lasting old parts of the verb states, and the short pictures

        sealed class Part
        {
            public GameObject Go; public Vfx308ZoneBody Zone; public Vfx120Effect Aura; public Vfx120Profile Copy;
            public Vol2LegacyField Field; public string Prefab = "", Role = ""; public float Lead; public Vector3 BaseScale = Vector3.one;
            public bool Closed, Follows, Parented;
            /// <summary>Step 5a: drawn with a vol.2 variant (Vol2LegacyLastingSet) - it owns its density, so the Presence boost is not stacked on it.</summary>
            public bool Variant;
            /// <summary>Step 5a: a second aura laid with this one (an ㅓ own aura that is not a buff band also gets the element's dark brush wrap); closed / freed with it.</summary>
            public Part Twin;
            /// <summary>Step 5a: the KCISA pattern laid on the state (a child of the part: it goes with it), faded on the close, paled on the warning.</summary>
            public Vol2PatternDecal Decal;
        }

        // ---------------------------------------------------------------- step 5a: the vol.2 variants of the lasting parts (TEST, presentation only)

        /// <summary>TEST (step 5a): null = the set's own switch (Vol2LegacyLastingSet.Enabled); true / false = this stage draws the verb states with the vol.2 variants / the old
        /// parts as built (the capture's lasting=1|0).</summary>
        public bool? LastingOverride;
        Vol2LegacyLastingSet _lastingSet; bool _lastingLoaded;
        Vol2LegacyLastingSet LastingSet
        {
            get
            {
                if (!_lastingLoaded) { _lastingLoaded = true; _lastingSet = Resources.Load<Vol2LegacyLastingSet>(Vol2LegacyLastingSet.ResourcePath); }
                return _lastingSet;
            }
        }
        /// <summary>The variants are on for this stage (the set is there and switched on, or the override says so).</summary>
        public bool LastingVariants => LastingSet != null && (LastingOverride ?? LastingSet.Enabled);
        Vol2LegacyLastingSet Variants => LastingVariants ? LastingSet : null;
        /// <summary>TEST (step 5a): the settled shoulder camera's eye above the caster's feet (pivot 1.7 m + offset 0.55 m, pitch 0). A floating part stays under it: above the
        /// horizon a particle is drawn over the sky, and the ink-wash look washes it to paper.</summary>
        public float ShoulderEyeHeight = 2.25f;
        public int PartsVaried { get; private set; }
        public int PartsSkipped { get; private set; }

        /// <summary>A lasting part's own life is only a safety cap (seconds): its end is the executor's event (Close). Far longer than any vol.2 state.</summary>
        public const float PartLifeCap = 600f;
        /// <summary>TEST (presentation): how pale a warned part becomes (start colour alpha of what it still emits, and of what is out).</summary>
        public float WarnAlpha = 0.45f;
        /// <summary>TEST (presentation): the fade of a held stain / guard stroke when its state ends (they have no End / Break child of their own).</summary>
        public float HeldFadeSeconds = 0.5f;
        /// <summary>TEST (presentation, step 4 probe 2026-10-25): a lasting part stays dark until the executor's expiry warning instead of the old "dark 3 s, faint after"
        /// (LateAfter) - a 10 s residue went faint at 3 s and was hardly read from the shoulder camera. false = the old prefab's own LateAfter.</summary>
        public bool KeepDark = true;
        /// <summary>TEST (presentation): the foot stain's alpha at least this (the old foot stain's .12 is not seen on the 적로 soil from the shoulder camera).</summary>
        public float StainAlphaMin = 0.45f;

        readonly Dictionary<int, Part> _parts = new Dictionary<int, Part>();
        readonly List<int> _deadParts = new List<int>();
        readonly List<GameObject> _pulses = new List<GameObject>();
        int _nextPart;

        public int PartsLaid { get; private set; }
        public int PartsMissing { get; private set; }
        public int PartsClosed { get; private set; }
        public int PulsesShown { get; private set; }
        public int PulsesMissing { get; private set; }
        public string LastPart { get; private set; } = "";
        public string LastPulse { get; private set; } = "";
        public int LivePartCount { get { SweepParts(); return _parts.Count; } }
        /// <summary>Every donor prefab a part / pulse used (name -> count), for the capture's report.</summary>
        public readonly Dictionary<string, int> Used = new Dictionary<string, int>();

        Vfx120Profile ProfileOf(char letter)
        {
            var set = _wiring != null ? _wiring.VisualSet308 : null;
            if (letter == '\0' || set == null || !set.TryGet(letter, out var v) || v.FxPrefab == null) return null;
            var fx = v.FxPrefab.GetComponent<Vfx120Effect>();
            return fx != null ? fx.Profile : null;
        }

        GameObject BodyOf(char letter)
        {
            var set = _wiring != null ? _wiring.VisualSet308 : null;
            return letter != '\0' && set != null && set.TryGet(letter, out var v) ? v.FxPrefab : null;
        }

        /// <summary>The prefab a donor glyph's OLD profile holds for a field (read only; the main game's glyph table, the same one the old route used).</summary>
        public GameObject FieldOf(char letter, Vol2LegacyField field)
        {
            var p = ProfileOf(letter);
            if (p == null) return null;
            switch (field)
            {
                case Vol2LegacyField.Zone: return p.Zone308Prefab;
                case Vol2LegacyField.Mark: return p.Mark308Prefab;
                case Vol2LegacyField.Stain: return p.AreaStainPrefab;
                case Vol2LegacyField.Guard: return p.Guard308Prefab;
                case Vol2LegacyField.Contact: return p.AreaContactPrefab;
                case Vol2LegacyField.Burst: return p.Burst308Prefab;
                case Vol2LegacyField.Derived: return p.Derived308Prefab;
                case Vol2LegacyField.Aura: return p.Aura308Prefab != null ? BodyOf(letter) : null;
                default: return BodyOf(letter);
            }
        }

        GameObject Resolve(char donor, char fallback, Vol2LegacyField field, out char from)
        {
            if (field == Vol2LegacyField.Ward)
            {
                from = donor;
                var w = _wiring != null && _wiring.EAWards != null ? _wiring.EAWards.PresentationPrefab308(donor) : null;
                if (w == null) { from = fallback; w = _wiring != null && _wiring.EAWards != null ? _wiring.EAWards.PresentationPrefab308(fallback) : null; }
                return w;
            }
            if (field == Vol2LegacyField.Unravel)
            {
                // the 풀림 has no per-glyph prefab: its drop system is the Resources profile's; the donor only lends its pigment
                from = ProfileOf(donor) != null ? donor : fallback;
                var u = InkUnravel308.Profile;
                return u != null ? u.ParticlePrefab : null;
            }
            from = donor; var g = FieldOf(donor, field);
            if (g != null) return g;
            from = fallback; g = FieldOf(fallback, field);
            if (g != null) return g;
            if (field == Vol2LegacyField.Derived) { from = fallback; return BodyOf(fallback); }     // earth has no derived shot: its single body flies
            return null;
        }

        void Use(string name) { Used.TryGetValue(name, out int n); Used[name] = n + 1; }

        GameObject Spawn(GameObject prefab, Vector3 at, Quaternion rot)
        {
            var go = Object.Instantiate(prefab, at, rot);
            if (_wiring != null && go.scene != _wiring.gameObject.scene) SceneManager.MoveGameObjectToScene(go, _wiring.gameObject.scene);
            return go;
        }

        Transform Caster => _wiring != null ? _wiring.SummonPlayer : null;

        static Quaternion FaceEye(Vector3 at, Vector3 fallbackForward)
        {
            var cam = Camera.main;
            Vector3 f = cam != null ? at - cam.transform.position : fallbackForward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = fallbackForward; f.y = 0f;
            return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
        }

        public int Lay(in Vol2LegacyPart def)
        {
            if (!Application.isPlaying) return 0;
            SweepParts();
            var d = def;
            var prefab = Resolve(d.Donor, d.Fallback, d.Field, out char from);
            // step 5a: the vol.2 variants (same family, darker and denser; the old part when a variant is missing). 남긴다's residue stands as the element's growth on any frame;
            // a rim / seal has its variant prefab; a ward / aura gets its variant Aura308 prefab on its private profile copy (LayWard / LayAura)
            var vs = Variants; bool varied = false;
            if (vs != null)
            {
                if (d.Role == "seed-ink" && !vs.HeldSeedInk) { PartsSkipped++; LastPart = "seed-ink not held (step 5a: the seal above the body)"; return 0; }
                var growth = d.Role == "residue" ? vs.GrowthOf(d.Body) : null;
                var stake = d.Role == "seed" && d.Field == Vol2LegacyField.Mark ? vs.StakeOf(d.Body) : null;
                var stakeHost = stake != null ? TargetOf(d.HostId) : null;
                var band = d.Field == Vol2LegacyField.Ward ? vs.BandOf(d.Body) : null;
                if (growth != null) { prefab = growth; d.Field = Vol2LegacyField.Zone; varied = true; }
                else if (band != null)
                {
                    // a ring / fan state: the element's rim as a continuous dark band (a Zone part: its radius is its scale, so the trip ring still breathes through Place);
                    // the ward membrane's standing sheets did not show in the ink-wash look, only its ground dots (step 5a probe)
                    prefab = band; d.Field = Vol2LegacyField.Zone; varied = true;
                }
                else if (stakeHost != null)
                {
                    // 심는다 on a body: a planted ink stake (the element's guard strokes) at the body's side, rising over its silhouette - the old seal round its middle was
                    // lost on the fur (step 4); it stands beside the body, not in front of it, so the body stays readable
                    prefab = stake; d.Field = Vol2LegacyField.Guard; varied = true;      // d.Seconds = the seed's scheduled life (its held strokes' look is fitted to it)
                    if (d.Seconds <= 0f) d.Seconds = 8f;
                    d.Point = StakePoint(stakeHost, vs.StakeOut, d.Facing);
                }
                else if ((d.Field == Vol2LegacyField.Zone || d.Field == Vol2LegacyField.Mark) && prefab != null)
                {
                    var v = vs.VariantOf(prefab);
                    if (v != null) { prefab = v; varied = true; }
                }
            }
            if (prefab == null) { PartsMissing++; LastPart = "no " + d.Field + " on " + d.Donor + "/" + d.Fallback + " for " + d.Role; return 0; }
            var part = new Part { Field = d.Field, Prefab = prefab.name, Role = d.Role ?? "", Variant = varied };
            try
            {
                switch (d.Field)
                {
                    case Vol2LegacyField.Aura: LayAura(part, prefab, d); break;
                    case Vol2LegacyField.Ward: LayWard(part, prefab, d); break;
                    case Vol2LegacyField.Zone:
                    case Vol2LegacyField.Mark: LayZone(part, prefab, d); break;
                    default: LayHeld(part, prefab, d, from); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); FreePart(part, true); PartsMissing++; return 0; }
            if (part.Go == null) { FreePart(part, true); PartsMissing++; LastPart = "refused " + prefab.name; return 0; }
            if (vs != null) { try { LayPattern(part, d, vs); } catch (Exception e) { Debug.LogException(e); } }
            int id = ++_nextPart; if (id == 0) id = ++_nextPart;
            _parts.Add(id, part); PartsLaid++; if (part.Variant) PartsVaried++;
            LastPart = d.Role + " " + part.Prefab + " (" + from + ")" + (part.Twin != null ? " + " + part.Twin.Prefab : ""); Use(part.Prefab);
            return id;
        }

        public int PatternsLaid { get; private set; }
        /// <summary>Every KCISA pattern decal laid (prefab name -> count), for the capture's report.</summary>
        public readonly Dictionary<string, int> PatternsUsed = new Dictionary<string, int>();

        /// <summary>Step 5a: the KCISA pattern of a state, as ritual language on the ink part (DECISIONS #135 / #138 / #189): a fan state's pattern clipped to the fan
        /// from its apex; a ring state's pattern band on the ring (and round a disc residue, and under the ㅓ carried ring); a seed's seal standing over the body it waits on.
        /// The decal is a child of the part (it goes, warns and fades with it).</summary>
        void LayPattern(Part part, in Vol2LegacyPart d, Vol2LegacyLastingSet vs)
        {
            if (part.Go == null) return;
            GameObject prefab = null; Vector3 at; Quaternion rot; float scale;
            if (d.IsFan && part.Field == Vol2LegacyField.Zone)
            {
                prefab = Vol2LegacyLastingSet.Of(vs.PatFan, d.Body);
                Vector3 f = d.FanForward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = d.Facing;
                at = d.FanApex; rot = Quaternion.LookRotation(f.normalized, Vector3.up); scale = Mathf.Max(.5f, d.FanDepth);
            }
            else if (part.Field == Vol2LegacyField.Zone && (d.IsRing || d.Role == "residue" || d.Role == "carry" || d.Role == "orbit" || d.Role == "ground" || d.Role == "trip" || d.Role == "ring"))
            {
                prefab = Vol2LegacyLastingSet.Of(vs.PatRing, d.Body);
                at = part.Go.transform.position; rot = Quaternion.identity;
                scale = part.Go.transform.lossyScale.x / .93f;                     // the ring pattern's teeth lie at ~.86 - 1 of its radius: on the ring's middle line
            }
            else if (d.Role == "seed" && part.Field == Vol2LegacyField.Guard && TargetOf(d.HostId) is Transform host)
            {
                prefab = Vol2LegacyLastingSet.Of(vs.PatSeal, d.Body);
                float top = host.position.y + 1.4f; bool found = false; var bounds = new Bounds();
                foreach (var r in host.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || !r.enabled) continue;
                    if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
                }
                if (found) top = host.position.y + Mathf.Clamp(bounds.max.y - host.position.y, .6f, 1.8f);
                var caster = Caster; float ground = caster != null ? caster.position.y : host.position.y;
                float y = Mathf.Min(top + vs.PatSealAbove + vs.PatSealSize, ground + ShoulderEyeHeight - .15f - vs.PatSealSize);   // under the eye: over the sky the wash look pales it
                Vector3 c = found ? bounds.center : host.position; at = new Vector3(c.x, y, c.z);
                rot = FaceEye(at, d.Facing); scale = vs.PatSealSize;
            }
            else return;
            if (prefab == null) return;
            var go = Spawn(prefab, at, rot);
            go.name = "Vol2Legacy_pattern_" + prefab.name;
            go.transform.localScale = Vector3.one * scale;
            var decal = go.GetComponent<Vol2PatternDecal>();
            if (decal != null && decal.Shape == Vol2PatternDecal.Kind.Fan) { decal.HalfAngle = Mathf.Clamp(d.FanHalfAngle, 5f, 89f); decal.Rebuild(); }
            go.transform.SetParent(part.Go.transform, true);
            part.Decal = decal; PatternsLaid++;
            PatternsUsed.TryGetValue(prefab.name, out int n); PatternsUsed[prefab.name] = n + 1;
        }

        /// <summary>Step 5a: where a seed's stake stands - on the ground at the body's right side as the caster sees it (its renderers' half width + <paramref name="outFrom"/>),
        /// a little toward the caster.</summary>
        Vector3 StakePoint(Transform host, float outFrom, Vector3 facing)
        {
            float half = .6f; bool found = false; var bounds = new Bounds();
            foreach (var r in host.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
            }
            if (found) half = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z), .3f, 1.2f);
            var caster = Caster;
            Vector3 toCaster = caster != null ? caster.position - host.position : -facing; toCaster.y = 0f;
            toCaster = toCaster.sqrMagnitude > 1e-4f ? toCaster.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, -toCaster);
            Vector3 p = host.position + right * (half + outFrom) + toCaster * .25f;
            p.y = caster != null ? caster.position.y : host.position.y;
            return p;
        }

        /// <summary>The donor's glyph body as a lasting aura on the caster (what the old route's Aura request did): a private DontSave profile copy with a capped life, marked
        /// lasting so DryAura308 lets it dry when the state ends; it follows the caster.</summary>
        void LayAura(Part part, GameObject prefab, in Vol2LegacyPart def)
        {
            var vs = Variants;
            if (!StartAura(part, prefab, def, vs, null)) return;
            // step 5a: an own aura that is not a buff band (tree, sword form, companion) keeps its look and gets the element's dark brush wrap beside it
            if (vs == null || part.Variant) return;
            var wrap = vs.WrapOf(def.Body);
            var host = BodyOf(Vol2LegacyDonors.Of(Vol2LegacyDonors.Aura, def.Body));
            if (wrap == null || host == null) return;
            var twin = new Part { Field = Vol2LegacyField.Aura, Prefab = wrap.name, Role = def.Role + "-wrap", Variant = true };
            if (StartAura(twin, host, def, null, wrap)) { part.Twin = twin; part.Variant = true; }
            else FreePart(twin, true);
        }

        /// <summary>One lasting aura: the glyph body on a private DontSave profile copy (capped life, lasting, dark until warned), its Aura308 prefab = <paramref name="force"/>
        /// or the set's variant of it (step 5a), begun on the caster and following it.</summary>
        bool StartAura(Part part, GameObject prefab, in Vol2LegacyPart def, Vol2LegacyLastingSet vs, GameObject force)
        {
            var go = Spawn(prefab, def.Point, Quaternion.identity);
            go.name = "Vol2Legacy_" + part.Role + "_" + prefab.name; go.SetActive(true);
            part.Go = go;
            var fx = go.GetComponent<Vfx120Effect>();
            if (fx == null || fx.Profile == null) { part.Go = null; Object.Destroy(go); return false; }
            var copy = Object.Instantiate(fx.Profile); copy.hideFlags = HideFlags.DontSave; CopiesMade++;
            copy.Duration = PartLifeCap; copy.Aura308Lasting = true;
            if (KeepDark) copy.Aura308LateAfter = 0f;
            var variant = force != null ? force : vs != null ? vs.VariantOf(copy.Aura308Prefab) : null;
            if (variant != null) { copy.Aura308Prefab = variant; part.Variant = true; part.Prefab = variant.name; }
            part.Copy = copy; part.Aura = fx;
            fx.Profile = copy; fx.PreviewControlled = false; fx.DemonstrationCues = false;
            var caster = Caster;
            Vector3 at = caster != null ? caster.position : def.Point;
            Vector3 fwd = caster != null ? caster.forward : def.Facing;
            fx.Begin(at, null, at + fwd, copy.Pigment);
            if (caster != null && def.OnCaster) { go.transform.SetParent(caster, true); part.Parented = true; }
            return true;
        }

        /// <summary>The element's old ward membrane as a lasting state on a ring (what BeginWard plays for R0, but held: a DontSave profile copy, the state's radius, a capped
        /// life, lasting so DryAura308 lets it dry on the executor's end). The radius is live (Place) - a ring going out and back breathes with it.</summary>
        void LayWard(Part part, GameObject prefab, in Vol2LegacyPart def)
        {
            var go = Spawn(prefab, def.Point, Quaternion.identity);
            go.name = "Vol2Legacy_" + def.Role + "_" + prefab.name; go.SetActive(true);
            part.Go = go;
            var fx = go.GetComponent<Vfx120Effect>();
            if (fx == null || fx.Profile == null) { part.Go = null; Object.Destroy(go); return; }
            var copy = Object.Instantiate(fx.Profile); copy.hideFlags = HideFlags.DontSave; CopiesMade++;
            var ward = _wiring != null && _wiring.EAWards != null ? _wiring.EAWards.Profile308 : null;
            copy.Duration = PartLifeCap; copy.Aura308Lasting = true; copy.WardRadius = Mathf.Max(0.5f, def.Radius);
            if (ward != null) { copy.WardHeight = ward.Height; copy.WardFormation = ward.Formation; copy.WardFade = ward.Fade; }
            copy.WardGroundProbeHeight = .6f; copy.WardGroundProbeDepth = 3f;
            if (KeepDark) copy.Aura308LateAfter = 0f;
            var vs = Variants;
            var film = vs != null ? vs.VariantOf(copy.Aura308Prefab) : null;      // step 5a: the continuous ink band with its film and rising motes
            if (film != null) { copy.Aura308Prefab = film; part.Variant = true; part.Prefab = film.name; }
            part.Copy = copy; part.Aura = fx;
            fx.Profile = copy; fx.PreviewControlled = false; fx.DemonstrationCues = false;
            fx.Begin(def.Point, null, def.Point + (def.Facing.sqrMagnitude > 1e-4f ? def.Facing : Vector3.forward), copy.Pigment);
        }

        /// <summary>A Vfx308ZoneBody part (a 결계 rim / zone floor: on the ground, scaled by the radius like PutZone308; a seal: on its host like PutMark308). Its Life is only the cap.</summary>
        void LayZone(Part part, GameObject prefab, in Vol2LegacyPart def)
        {
            Transform host = def.Field == Vol2LegacyField.Mark ? TargetOf(def.HostId) : null;
            Vector3 at = def.Point;
            float scale = Mathf.Max(0.25f, def.Radius);
            var rot = Quaternion.identity;
            if (host != null)
            {
                // PutMark308: the middle of the host's renderers, sized by their bounds (clamped), parented so it follows
                Vector3 centre = host.position + Vector3.up; float radius = .8f; bool found = false; var bounds = new Bounds();
                foreach (var r in host.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || !r.enabled) continue;
                    if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
                }
                if (found) { centre = new Vector3(bounds.center.x, host.position.y + Mathf.Clamp(bounds.center.y - host.position.y, .5f, 2.5f), bounds.center.z); radius = Mathf.Max(bounds.extents.x, bounds.extents.z); }
                // step 4 probe: on the forest bear the old clamp (.4 - 1.5 of the extents) put the ring of strokes inside the fur - it stands outside the body now
                at = centre; scale = Mathf.Clamp(radius * SealOutside, .6f, 2.4f);
                var vs = Variants;
                if (part.Variant && vs != null)
                {
                    // step 5a: the seal variant is an upright plate (its ring in the local XY plane) with a post under it: it floats above the body's silhouette, turned to the eye,
                    // its post pointing down at the body it was planted on (the old ring round the middle was lost on the fur)
                    // the body's top (the renderers' bounds also hold the bear's tall ink crown: at most 1.8 m), and the seal kept under the eye's height - drawn
                    // over the sky the ink-wash look washes a particle to paper (the sky test reads depth; particles write none), seen 2026-10-25 step 5a probe
                    float topY = host.position.y + (found ? Mathf.Clamp(bounds.max.y - host.position.y, .6f, 1.8f) : 1.4f);
                    scale = Mathf.Max(.3f, vs.SealScale);
                    float y = topY + vs.SealAbove + .55f * scale;
                    var caster = Caster;
                    float ground = caster != null ? caster.position.y : host.position.y;
                    y = Mathf.Min(y, ground + ShoulderEyeHeight - .2f - .55f * scale);
                    at = new Vector3(centre.x, Mathf.Max(y, host.position.y + .9f), centre.z);
                    rot = FaceEye(at, def.Facing);
                }
            }
            var go = Spawn(prefab, at, rot);
            go.name = "Vol2Legacy_" + def.Role + "_" + prefab.name;
            part.BaseScale = prefab.transform.localScale;
            go.transform.localScale = part.BaseScale * scale;
            if (def.Field == Vol2LegacyField.Zone) foreach (var density in go.GetComponentsInChildren<Vfx308RingDensity>(true)) density.Apply(scale);   // counts per metre of rim
            var body = go.GetComponent<Vfx308ZoneBody>();
            if (body == null) { Object.Destroy(go); return; }
            part.Lead = body.LateBefore;
            body.Delay = 0f; body.Life = PartLifeCap; body.LateBefore = 0f;
            if (KeepDark) body.LateAfter = 0f;
            if (!part.Variant) Boost(go);                                        // a step 5a variant owns its density
            part.Go = go; part.Zone = body;
            if (host != null) { go.transform.SetParent(host, true); part.Parented = true; }
            else if (def.OnCaster && Caster != null)
            {
                var f = go.AddComponent<Vfx308ZoneFollow>(); f.Target = Caster; f.Body = body; part.Follows = true;
            }
        }

        /// <summary>A one-shot old prefab held as a state (the foot stain: one particle; the guard strokes): it is wrapped in a Vfx308ZoneBody of its own so the executor's event
        /// ends it (Fade - these prefabs have no End / Break child). The stain's size and alpha are the donor profile's (CombatLoopWiring.TrySpawnAreaStain308 sets the same).</summary>
        void LayHeld(Part part, GameObject prefab, in Vol2LegacyPart def, char donor)
        {
            var wrap = new GameObject("Vol2Legacy_" + def.Role + "_" + prefab.name);
            if (_wiring != null) SceneManager.MoveGameObjectToScene(wrap, _wiring.gameObject.scene);
            wrap.SetActive(false);
            wrap.transform.SetPositionAndRotation(def.Point, def.Field == Vol2LegacyField.Guard ? FaceEye(def.Point, def.Facing) : Quaternion.identity);
            var inner = Object.Instantiate(prefab, wrap.transform, false);
            inner.name = prefab.name; inner.transform.localPosition = Vector3.zero;
            float life = def.Seconds > 0f ? def.Seconds : PartLifeCap;
            foreach (var ps in inner.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main; main.playOnAwake = false;
                if (def.Field == Vol2LegacyField.Stain)
                {
                    var src = ProfileOf(donor);
                    main.startSize = Mathf.Max(.05f, src != null ? src.AreaStainDiameter : 2f) * Mathf.Clamp(def.Radius, .5f, 2f);
                    main.startLifetime = PartLifeCap;
                    main.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(StainAlphaMin, src != null ? src.AreaStainAlpha : .12f)));
                }
                else if (def.Field == Vol2LegacyField.Contact)
                {
                    // a HELD hit (step 4 probe: the hits read, the old floors / seals did not): its particles live as long as the state is scheduled to (the executor's
                    // ExpiresUs - now, handed in as Seconds), so their over-lifetime look runs over the state; the end is still the executor's event (Fade)
                    var lt = main.startLifetime;
                    float longest = lt.mode == ParticleSystemCurveMode.TwoConstants ? lt.constantMax : lt.constant;
                    if (def.Seconds > longest) main.startLifetimeMultiplier *= def.Seconds / Mathf.Max(0.05f, longest);
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                else if (def.Field == Vol2LegacyField.Guard)
                {
                    // 11.28: "Hold*" lives the guard clock (here: the strip's scheduled length), "Wet*" the bright window (a short first beat)
                    string n = ps.gameObject.name;
                    if (n.StartsWith("Hold", StringComparison.Ordinal)) main.startLifetime = life;
                    else if (n.StartsWith("Wet", StringComparison.Ordinal)) main.startLifetime = Mathf.Min(0.35f, life);
                }
            }
            if (def.Field == Vol2LegacyField.Guard && !part.Variant) Boost(wrap);
            var body = wrap.AddComponent<Vfx308ZoneBody>();
            body.Delay = 0f; body.Life = PartLifeCap; body.LateAfter = 0f; body.LateBefore = 0f; body.DryFade = HeldFadeSeconds;
            // a held hit on a body (a seed waiting on it) sits at its chest, on the side that faces the caster, and goes with it
            var host = def.Field == Vol2LegacyField.Contact ? TargetOf(def.HostId) : null;
            if (host != null)
            {
                Vector3 toCaster = Caster != null ? Caster.position - host.position : -def.Facing; toCaster.y = 0f;
                toCaster = toCaster.sqrMagnitude > 1e-4f ? toCaster.normalized : -def.Facing;
                wrap.transform.SetPositionAndRotation(host.position + Vector3.up * 0.9f + toCaster * 0.6f, FaceEye(host.position, def.Facing));
                wrap.transform.SetParent(host, true); part.Parented = true;
            }
            else if (def.Field == Vol2LegacyField.Contact) wrap.transform.rotation = FaceEye(def.Point, def.Facing);
            wrap.SetActive(true);
            part.Go = wrap; part.Zone = body;
        }

        /// <summary>TEST (presentation, step 4 probe 2026-10-25): the old rims / seals / floors were built faint (a 결계 under a figure's knee, 18 - 38 live particles on a 2 m
        /// ring) and a vol.2 state drawn with them was hardly read from the shoulder camera. A laid part's body systems (not its End / Break / React) emit
        /// <see cref="PresenceRate"/> times as often, <see cref="PresenceSize"/> times as large and <see cref="PresenceAlpha"/> times as opaque - on this instance only (no
        /// material, no colour hue, no light). 1 / 1 / 1 = the old look as built.</summary>
        public float PresenceRate = 2.5f, PresenceSize = 1.3f, PresenceAlpha = 1.5f;

        void Boost(GameObject root)
        {
            if (PresenceRate == 1f && PresenceSize == 1f && PresenceAlpha == 1f) return;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (UnderEndChild(ps.transform, root.transform)) continue;
                var main = ps.main;
                main.startSizeMultiplier *= PresenceSize;
                main.maxParticles = Mathf.Max(1, Mathf.RoundToInt(main.maxParticles * PresenceRate));
                var em = ps.emission; em.rateOverTimeMultiplier *= PresenceRate;
                var c = main.startColor;
                if (c.mode == ParticleSystemGradientMode.Color) { var k = c.color; k.a = Mathf.Clamp01(k.a * PresenceAlpha); main.startColor = new ParticleSystem.MinMaxGradient(k); }
                else if (c.mode == ParticleSystemGradientMode.TwoColors) { var a = c.colorMin; var b = c.colorMax; a.a = Mathf.Clamp01(a.a * PresenceAlpha); b.a = Mathf.Clamp01(b.a * PresenceAlpha); main.startColor = new ParticleSystem.MinMaxGradient(a, b); }
            }
        }

        public void Place(int handle, Vector3 point, float radius)
        {
            if (!_parts.TryGetValue(handle, out var part) || part.Go == null || part.Closed) return;
            if (!part.Parented && !part.Follows) part.Go.transform.position = point;
            if (part.Field == Vol2LegacyField.Zone && radius > 0f) part.Go.transform.localScale = part.BaseScale * Mathf.Max(0.25f, radius);
            if (part.Field == Vol2LegacyField.Ward && part.Copy != null && radius > 0f) part.Copy.WardRadius = Mathf.Max(0.5f, radius);      // the aura reads it every frame
        }

        /// <summary>A report line of the live parts (role, prefab, where, scale, live particles / playing systems) for the capture's log.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _parts)
            {
                var p = kv.Value; if (p.Go == null) continue;
                int n = 0, playing = 0, systems = 0;
                foreach (var ps in p.Go.GetComponentsInChildren<ParticleSystem>(true)) { systems++; n += ps.particleCount; if (ps.isPlaying) playing++; }
                sb.Append(p.Role).Append(' ').Append(p.Prefab).Append(" @").Append(p.Go.transform.position.ToString("F1")).Append(" s").Append(p.Go.transform.lossyScale.x.ToString("0.00"))
                  .Append(p.Parented ? " parented" : "").Append(p.Closed ? " closed" : "").Append(" particles ").Append(n).Append(" playing ").Append(playing).Append('/').Append(systems);
                foreach (var q in p.Twin != null && p.Twin.Go != null ? new[] { p, p.Twin } : new[] { p })
                {
                    foreach (var w in q.Go.GetComponentsInChildren<AuraBrushWrap308>(true)) sb.Append(" brush-wrap ").Append(w.Drawing ? "on " + w.UsedLength.ToString("0.00") + " m" : "off");
                    foreach (var w in q.Go.GetComponentsInChildren<Vol2LimbWrap>(true)) sb.Append(" sleeve-wrap ").Append(w.Drawing ? "on " + w.Bound : "off");
                }
                if (p.Twin != null) sb.Append(" + twin ").Append(p.Twin.Prefab);
                sb.Append("; ");
            }
            return sb.ToString();
        }

        public float WarnLead(int handle) => _parts.TryGetValue(handle, out var part) ? part.Lead : 0f;

        ParticleSystem.Particle[] Buffer = new ParticleSystem.Particle[512];
        /// <summary>TEST (presentation): a seal sits this many times the host's horizontal extent out from its centre (the old PutMark308 used 1).</summary>
        public float SealOutside = 1.35f;

        public void Warn(int handle)
        {
            if (!_parts.TryGetValue(handle, out var part) || part.Go == null || part.Closed) return;
            WarnPart(part);
            if (part.Twin != null && part.Twin.Go != null) WarnPart(part.Twin);
            if (part.Decal != null) part.Decal.Dim(WarnAlpha);
        }

        void WarnPart(Part part)
        {
            foreach (var ps in part.Go.GetComponentsInChildren<ParticleSystem>(false))
            {
                if (UnderEndChild(ps.transform, part.Go.transform)) continue;
                var main = ps.main; var c = main.startColor;
                if (c.mode == ParticleSystemGradientMode.Color) { var k = c.color; k.a *= WarnAlpha; main.startColor = new ParticleSystem.MinMaxGradient(k); }
                else if (c.mode == ParticleSystemGradientMode.TwoColors) { var a = c.colorMin; var b = c.colorMax; a.a *= WarnAlpha; b.a *= WarnAlpha; main.startColor = new ParticleSystem.MinMaxGradient(a, b); }
                if (Buffer.Length < ps.main.maxParticles) Buffer = new ParticleSystem.Particle[ps.main.maxParticles];
                int n = ps.GetParticles(Buffer);
                for (int i = 0; i < n; i++) { var col = Buffer[i].startColor; col.a = (byte)(col.a * WarnAlpha); Buffer[i].startColor = col; }
                if (n > 0) ps.SetParticles(Buffer, n);
            }
        }

        static bool UnderEndChild(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.parent == root && (t.name == "End" || t.name == "Break" || t.name == "React")) return true;
            return false;
        }

        public void Close(int handle, Vol2EndCode code, Vector3 direction, bool immediate)
        {
            if (!_parts.TryGetValue(handle, out var part)) return;
            if (immediate) { _parts.Remove(handle); FreePart(part, true); PartsClosed++; return; }
            if (part.Closed) return;
            part.Closed = true; PartsClosed++;
            if (part.Decal != null) part.Decal.Fade(Mathf.Max(.3f, HeldFadeSeconds * 2f));
            if (part.Twin != null && part.Twin.Aura != null) { try { if (!part.Twin.Aura.DryAura308() && part.Twin.Go != null) Object.Destroy(part.Twin.Go); } catch (Exception e) { Debug.LogException(e); } }
            try
            {
                if (part.Aura != null)
                {
                    if (!part.Aura.DryAura308() && part.Go != null) Object.Destroy(part.Go);
                    return;
                }
                if (part.Zone == null) { if (part.Go != null) Object.Destroy(part.Go); return; }
                if (part.Parented && part.Go != null)
                {
                    // the seal leaves the body it sat on (stays where it was) before it dries, like the old LetGo
                    part.Go.transform.SetParent(null, true);
                    if (_wiring != null) SceneManager.MoveGameObjectToScene(part.Go, _wiring.gameObject.scene);
                    part.Parented = false;
                }
                bool held = part.Field != Vol2LegacyField.Zone && part.Field != Vol2LegacyField.Mark;
                switch (code)
                {
                    case Vol2EndCode.Consume: if (held) part.Zone.Fade(HeldFadeSeconds * 0.5f); else part.Zone.Splash(); break;
                    case Vol2EndCode.Child:
                    case Vol2EndCode.Cleanse: if (held) part.Zone.Fade(HeldFadeSeconds); else part.Zone.React(direction); break;
                    case Vol2EndCode.Replace: part.Zone.Fade(0.4f); break;
                    case Vol2EndCode.HitCancel: part.Zone.Fade(0.25f); break;
                    case Vol2EndCode.Expire: if (held) part.Zone.Fade(HeldFadeSeconds); else part.Zone.Dry(); break;
                    default: part.Zone.Fade(0.3f); break;
                }
            }
            catch (Exception e) { Debug.LogException(e); _parts.Remove(handle); FreePart(part, true); }
        }

        public bool Standing(int handle)
        {
            SweepParts();
            return _parts.TryGetValue(handle, out var part) && part.Go != null;
        }

        void SweepParts()
        {
            _deadParts.Clear();
            foreach (var kv in _parts) if (kv.Value.Go == null) _deadParts.Add(kv.Key);
            foreach (int id in _deadParts) { var p = _parts[id]; _parts.Remove(id); FreePart(p, false); }
            _pulses.RemoveAll(g => g == null);
        }

        void FreePart(Part part, bool destroy)
        {
            // the twin goes with its part (its profile copy must not outlive the effect that reads it, so it is never left running alone)
            if (part.Twin != null) { var twin = part.Twin; part.Twin = null; FreePart(twin, true); }
            if (destroy && part.Go != null) { if (Application.isPlaying) Object.Destroy(part.Go); else Object.DestroyImmediate(part.Go); }
            if (part.Copy != null) { if (Application.isPlaying) Object.Destroy(part.Copy); else Object.DestroyImmediate(part.Copy); CopiesFreed++; }
            part.Go = null; part.Copy = null; part.Aura = null; part.Zone = null; part.Decal = null;
        }

        public void Pulse(in Vol2LegacyPulse pulse)
        {
            if (!Application.isPlaying) return;
            var prefab = Resolve(pulse.Donor, pulse.Fallback, pulse.Field, out char from);
            if (prefab == null) { PulsesMissing++; LastPulse = "no " + pulse.Field + " on " + pulse.Donor + "/" + pulse.Fallback + " for " + pulse.Role; return; }
            try
            {
                GameObject go = null;
                var target = TargetOf(pulse.TargetId);
                switch (pulse.Field)
                {
                    case Vol2LegacyField.Unravel:
                        {
                            // the old 풀림: points along the line come apart one after another and the drops are pulled into the swinging brush tip (or to the line's end)
                            var pts = new List<Vector3>(48);
                            for (int i = 0; i < 48; i++) { Vector3 p = Vector3.Lerp(pulse.From, pulse.To, i / 47f * 0.8f); pts.Add(p + UnityEngine.Random.insideUnitSphere * 0.25f); }
                            var src = ProfileOf(from);
                            var u = InkUnravel308.Begin(pts, pulse.To, src != null ? src.Pigment : Color.black);
                            go = u != null ? u.gameObject : null;
                            break;
                        }
                    case Vol2LegacyField.Derived:
                    case Vol2LegacyField.Body:
                        {
                            go = Spawn(prefab, pulse.From, Quaternion.identity);
                            go.name = "Vol2Legacy_" + pulse.Role + "_" + prefab.name; go.SetActive(true);
                            var fx = go.GetComponent<Vfx120Effect>();
                            if (fx == null || fx.Profile == null) { Object.Destroy(go); go = null; break; }
                            fx.PreviewControlled = false; fx.DemonstrationCues = false;
                            if (pulse.Seconds > 0f) fx.SetImpactClock(pulse.Seconds);
                            fx.Begin(pulse.From, target, pulse.To, fx.Profile.Pigment);
                            break;
                        }
                    default:
                        {
                            // a hit on its body (the chest, like the old area contacts), a burst / stain where it was asked; turned to the eye (local +Z away from it)
                            Vector3 at = target != null && pulse.Field == Vol2LegacyField.Contact ? target.position + Vector3.up * 0.9f : pulse.To;
                            var rot = FaceEye(at, Vector3.forward);
                            var ktp = KtpContactEffect.Spawn(prefab, at, rot, 1f, _wiring != null ? _wiring.gameObject.scene : SceneManager.GetActiveScene());
                            if (ktp != null) { go = ktp.gameObject; break; }
                            // not a particles-only contact (a burst with nested parts): played as it is and gone after its longest system
                            go = Spawn(prefab, at, rot); go.name = "Vol2Legacy_" + pulse.Role + "_" + prefab.name; go.SetActive(true);
                            float longest = 1f;
                            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; longest = Mathf.Max(longest, m.duration + m.startLifetime.constantMax + m.startDelay.constantMax); if (m.playOnAwake) continue; ps.Play(false); }
                            var res = go.AddComponent<Vfx308Residue>(); res.Life = Mathf.Min(longest, 4f);
                            break;
                        }
                }
                if (go == null) { PulsesMissing++; LastPulse = "refused " + prefab.name; return; }
                _pulses.Add(go); PulsesShown++; LastPulse = pulse.Role + " " + prefab.name + " (" + from + ")"; Use(prefab.name);
            }
            catch (Exception e) { Debug.LogException(e); PulsesMissing++; }
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
            SweepParts();
            BindRecovery();
            RouteAfterFog();
        }

        // ---- 2026-10-26 fog fix (VfxAfterFog308, Docs/Research/vfx120/fog_occlusion_20261026.md): the transparent pieces of the bodies, lasting parts and pulses
        // this stage laid draw after the realm fog (the fog of what stands behind them no longer paints over them). Re-routed when the count changes and every 20 sweeps.
        int _afterFogCount = -1, _afterFogTick;
        void RouteAfterFog()
        {
            if (!Application.isPlaying) return;
            int n = _live.Count + _parts.Count + _pulses.Count;
            if (n == _afterFogCount && ++_afterFogTick < 20) return;
            _afterFogCount = n; _afterFogTick = 0;
            foreach (var kv in _live) if (kv.Value.Go != null) VfxAfterFog308.Route(kv.Value.Go.transform);
            foreach (var kv in _parts) if (kv.Value.Go != null) VfxAfterFog308.Route(kv.Value.Go.transform);
            foreach (var g in _pulses) if (g != null) VfxAfterFog308.Route(g.transform);
        }
        // ---- end fog fix

        // ---------------------------------------------------------------- step 4: 걱, the recovery (the defence kit's: no vol.2 cast log, so no model instance)

        Vol2RecoveryExecutor _recovery; Action<Vol2RecoveryRecord> _onRecovery; int _recoverySearches, _recoveryPart;
        public int RecoveriesShown { get; private set; }
        /// <summary>batch 3 (2026-10-26): the free showcase draws 걱 itself (Vol2FsGeok, lab only) - the bridge keeps the buff swing but lays no aura while this is set.</summary>
        public bool RecoveryShownElsewhere;

        /// <summary>걱 never reaches the coordinator (the payment gate runs it in the defence kit: Vol2RecoveryExecutor). Its old look - the swing of a buff and the wood buff
        /// band on the caster (걱 was an EA buff: its band is 건's) - is laid on the kit's own Begin record and closed on the record that says it no longer runs
        /// (ran out = Dry, a hit / cancel = the short fade). Presentation only; the records are read, never answered.</summary>
        void BindRecovery()
        {
            if (_recovery != null || !Application.isPlaying || _recoverySearches >= 40) return;
            _recoverySearches++;
            var kit = _wiring != null ? _wiring.Vol2Defence : null;
            if (kit == null || kit.Recovery == null) return;
            _recovery = kit.Recovery;
            _recovery.Recorded += _onRecovery = OnRecovery;
        }

        void OnRecovery(Vol2RecoveryRecord rec)
        {
            try
            {
                if (rec.Kind == "Begin" && rec.Accepted && rec.Active)
                {
                    if (_recoveryPart != 0) Close(_recoveryPart, Vol2EndCode.Replace, Vector3.zero, false);
                    var caster = Caster;
                    Swing(new Vol2LegacySwing { Letter = '걱', Body = Body.Wood, Kind = Vol2LegacyKind.Self, ToApex = Vol2LegacyRecipeBase.MinToApex, FromClipStart = true });
                    if (RecoveryShownElsewhere) return;
                    _recoveryPart = Lay(new Vol2LegacyPart { Donor = '걱', Fallback = Vol2LegacyDonors.Of(Vol2LegacyDonors.Aura, Body.Wood), Field = Vol2LegacyField.Aura, Body = Body.Wood,
                        Point = caster != null ? caster.position : Vector3.zero, Facing = caster != null ? caster.forward : Vector3.forward, Radius = 1f, OnCaster = true, Role = "recovery" });
                    if (_recoveryPart != 0) RecoveriesShown++;
                    return;
                }
                if (_recoveryPart != 0 && !rec.Active && rec.Kind != "Rest")
                {
                    Close(_recoveryPart, rec.Kind == "Damage" || rec.Kind == "Cancel" ? Vol2EndCode.HitCancel : Vol2EndCode.Expire, Vector3.zero, false);
                    _recoveryPart = 0;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
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
            foreach (var part in _parts.Values) FreePart(part, true);
            _parts.Clear(); _recoveryPart = 0;
            foreach (var go in _pulses) if (go != null) { if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go); }
            _pulses.Clear();
        }

        public void Dispose()
        {
            if (_recovery != null && _onRecovery != null) _recovery.Recorded -= _onRecovery;
            _recovery = null; _onRecovery = null;
            ClearAll();
        }
    }
}
