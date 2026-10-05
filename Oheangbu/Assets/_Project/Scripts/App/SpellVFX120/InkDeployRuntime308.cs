using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>What Configure needs from the cast. Everything is read; nothing is written back to the cast or its plan.</summary>
    public struct DeployCast308
    {
        public SpellDeploy308ProfileSO Profile;
        public SpellDeploy308MapSO.Row Row;
        public Vector3 Origin;            // world
        public Transform Target;          // lock-on target root (may be null)
        public Vector3 FallbackPoint;     // world
        public AreaImpactPlan Plan;       // may be null
        public float ImpactClock;         // s: flight time (single) - 0 = none
        public float Grade01;
        public Color Tint;
        public float HoldSeconds;         // > 0: the body stands this long (ward / guard / install)
        public float WardRadius, WardHeight;
        public int Seed;
        public bool Triggered;
        public DeployTier308 Tier;
        public Vector3 CameraPosition;    // used when there is no host camera (preview)
        public bool HasCameraPosition;
        // ---- #308 forms2: read like everything above, never written back
        public float GuardWindow, GuardLife;          // parry: the rule's window and lifetime as the wiring handed them over (0 = none)
        public float FadeSeconds;                     // ward: the rule's fade at the end of its lifetime
        public float PathArc, PathCurve, PathEase;    // single attack: the letter's own flight path numbers (the old bolt's)
        public Vector3 Anchor;                        // summon: where the summon really stands (world)
        public bool HasAnchor;
        /// <summary>The caller asks for the forms2 forms (guard stroke, fence, comet, loose mark, curtain) where the row and the
        /// profile allow them. Set by the seams this stage reviewed - the effect hook (Vfx120Effect.BuildDeploy308), the
        /// director's own curtain, the previews. Off (the default) = the category form, whatever the row says: a cast somebody
        /// else builds (the spell presenter's stage, which borrows rows under another Category) is drawn as it always was.</summary>
        public bool NewForms;
        /// <summary>#308 forms3 fix pass: the spell presenter's standing cover wall (its Stand act). The ward row is then drawn as
        /// rising strokes on the ring (InkDeployForms308.Cover) instead of the category's column ring. Off = as it always was.</summary>
        public bool Cover;
    }

    // SPEC-SPELL-DEPLOY-308 section 2: the three-beat clock of one deploy. Lives on the effect object (beside FixedWardVfx).
    // Sample(seconds) is deterministic. The mesh is assembled once in Configure; a cel tick only changes the property block
    // (revealed cel, advance, melt, column, momentary glow) and re-aims the stroke root. Without a host it draws its own burst
    // and drops every residue / impact / hit-splash request. Presentation only: the judgement clock is read, never advanced.
    // D308-10c, the three levels of a cast:
    //   ordinary  - the strokes glow faintly on the cel they are born and sink into ink (no screen frame, no pause)
    //   it hit    - the wiring confirmed a hit (ConfirmHit): ink bursts out of the hit point (InkDeployForms308.ComposeHit)
    //   groggy    - the judged target is groggy on the burst's first cel (host.TargetGroggy): impact frames + the cut pause
    public sealed class InkDeployRuntime308 : MonoBehaviour
    {
        public const int MaxDrops = 64, MaxBirths = 96, MaxShots = 32, MaxPendingHits = 4;
        /// <summary>Where a cast meets its target: the effect system's chest convention (Vfx120Effect.TargetPoint).</summary>
        public const float ChestHeight = 1.1f;
        private static readonly int CelId = Shader.PropertyToID("_Cel");
        private static readonly int AdvanceId = Shader.PropertyToID("_Advance");
        private static readonly int MeltId = Shader.PropertyToID("_Melt");
        private static readonly int RiseId = Shader.PropertyToID("_ColumnRise");
        private static readonly int DrainId = Shader.PropertyToID("_ColumnDrain");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int TintAmountId = Shader.PropertyToID("_TintAmount");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int GlowCelsId = Shader.PropertyToID("_GlowCels");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowRimId = Shader.PropertyToID("_GlowRim");

        private DeployCast308 _cast;
        private ISpellDeployHost308 _host;
        private InkBurstBuffer308 _buffer;
        private Mesh _mesh;
        private bool _rented, _ownsMesh;
        private Transform _root;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private readonly DeployDrop308[] _drops = new DeployDrop308[MaxDrops];
        private readonly float[] _births = new float[MaxBirths];
        private readonly Vector3[] _shotPoints = new Vector3[MaxShots];
        private readonly int[] _shotCels = new int[MaxShots];
        private readonly DeployDrop308[] _hitDrops = new DeployDrop308[InkDeployForms308.MaxHitDrops];
        private readonly Transform[] _pendingHits = new Transform[MaxPendingHits];
        private DeployFormStats308 _stats;
        private float _cel, _impactTime, _holdEnd, _meltEnd, _pause, _pauseAllowed, _distance0, _pathStart, _pathSeconds;
        private int _impactCel, _lastCel = int.MinValue, _nextDrop, _heldOwner, _pendingHitCount, _hitDropCount, _glowEndCel, _hitEndCel;
        private bool _configured, _impactSent, _released, _area;
        private Vector3 _targetGroundWorld, _eyeWorld;
        private float _hitGroundY;
        // ---- #308 forms2
        private DeployForm308 _form;
        private DeployFormInput308 _input;            // kept for the guard stroke: its look is composed again when its phase changes
        private GuardTimeline308 _guard;
        private GuardPhase308 _guardPhase;
        private bool _guardBroken;
        private int _breakCel;
        private float _flightAdvance = -1f;           // comet: the advance written on the last frame (-1 = cel steps, the first form)
        private Vector3 _cometSide, _cometGoal;       // comet: where the target was when it was last seen (it may be destroyed in flight)
        private bool _cometGoalSeen;

        public DeployForm308 Form => _form;
        /// <summary>O3: this body stands (guard stroke, ward fence / wall, cover wall): it rents a standing slot of the host.</summary>
        public bool Standing { get; private set; }
        public GuardPhase308 GuardPhase => _guardPhase;
        public GuardTimeline308 GuardTimeline => _guard;
        /// <summary>Times the mesh was assembled again after Configure (the guard stroke's phase changes: at most two).</summary>
        public int MeshRebuilds { get; private set; }
        public float FlightAdvance => _flightAdvance;

        public bool Configured => _configured;
        public float Duration { get; private set; }
        public float ImpactTime => _impactTime;
        public float JudgementSeconds { get; private set; }
        public int ImpactCel => _impactCel;
        public float CelSeconds => _cel;
        public DeployFormStats308 Stats => _stats;
        public int BirthCount => Mathf.Min(_stats.Births, MaxBirths);
        public float BirthCel(int index) => _births[index];
        public int CurrentCel => _lastCel;
        public int ImpactRequests { get; private set; }
        public int ResidueRequests { get; private set; }
        /// <summary>D308-10c: times this cast asked the host whether its target is groggy (0 or 1), and the answer.</summary>
        public int GroggyAsked { get; private set; }
        public bool GroggyAtImpact { get; private set; }
        /// <summary>Hits the wiring confirmed for this cast, and the splashes shown for them (at most Hit.MaxPerCast).</summary>
        public int HitsConfirmed { get; private set; }
        public int HitSplashes { get; private set; }
        /// <summary>The glow amount written to the material on the last cel tick: 0 in the hold, the melt and a standing ward.</summary>
        public float GlowNow { get; private set; }
        /// <summary>Presentation second from which nothing of this burst glows: the last cel tick at or before the end of the burst
        /// beat (of a column's rise). The hold and the melt lie wholly after it.</summary>
        public float GlowEnd => _glowEndCel * _cel;
        public float PauseSeconds => _pause;
        public int HitDropCount => _hitDropCount;
        public DeployDrop308 HitDrop(int index) => _hitDrops[index];
        public DeployCategory308 Category => _cast.Row.Category;
        public Element CastElement => _cast.Row.Element;
        public bool IsAreaCast => _cast.Plan != null;
        public float HoldEnd => _holdEnd;
        public float MeltEnd => _meltEnd;
        public Transform Root => _root;
        public int DropCount => Mathf.Min(_stats.Drops, MaxDrops);
        public DeployDrop308 Drop(int index) => _drops[index];
        public int VertexCount => _buffer != null ? _buffer.VertexCount : 0;
        public int DroppedPrimitives => _buffer != null ? _buffer.Dropped : 0;
        public Renderer BurstRenderer => _renderer;
        public Mesh BurstMesh => _mesh;

        /// <summary>The judgement clock T of a cast: flight time (single), plan delay (area), otherwise ignition + silence.</summary>
        public static float JudgementClock(SpellDeploy308ProfileSO profile, in DeployCast308 cast)
        {
            if (cast.Plan != null && cast.Plan.Shape != AreaShape.None) return Mathf.Max(0f, cast.Plan.Delay);
            if (cast.Row.Category == DeployCategory308.AttackSingle && cast.ImpactClock > 0f) return cast.ImpactClock;
            if (cast.Row.Category == DeployCategory308.AttackArea && cast.ImpactClock > 0f) return cast.ImpactClock;
            return profile.Beats.Ignite + profile.Beats.Silence;
        }

        /// <summary>The burst's first cel is always the judgement clock (within one cel).</summary>
        public static int ImpactCelOf(float clock, float celSeconds) => clock <= celSeconds * .5f ? 0 : Mathf.Max(1, Mathf.RoundToInt(clock / celSeconds));

        /// <summary>Checks only: route requests to a counting host after a hostless Configure (the mesh stays the runtime's own).</summary>
        public void AttachHostForChecks(ISpellDeployHost308 host) { _host = host; _pauseAllowed = host != null ? Mathf.Max(0f, host.CutPause) : 0f; }

        public bool Configure(in DeployCast308 cast, ISpellDeployHost308 host)
        {
            Release();
            var p = cast.Profile;
            if (p == null) return false;
            _cast = cast; _host = host;
            var tier = p.Tier(host != null ? host.Tier : cast.Tier);
            _cel = p.CelSeconds(tier);
            // #308 forms2: which form (the map row's Retire + the profile switches decide), and whether it stands
            _form = InkForms2Rules308.FormOf(p, cast.Row, cast.Triggered, cast.GuardLife > 0f, cast.NewForms, cast.Target != null || cast.Plan != null);
            Standing = InkForms2Rules308.Standing(cast.Row.Category, cast.HoldSeconds, _form);
            _guard = InkForms2Rules308.GuardTimeline(p, _cel, cast.GuardWindow, cast.GuardLife);
            _guardPhase = GuardPhase308.Window; _guardBroken = false; _breakCel = 0; _flightAdvance = -1f; MeshRebuilds = 0; _cometGoalSeen = false;
            float clock = InkForms2Rules308.NoLead(_form) ? 0f : JudgementClock(p, cast);
            _impactCel = ImpactCelOf(clock, _cel);
            JudgementSeconds = clock;
            // presentation runs on whole cels: the burst's first cel is the cel nearest to the judgement clock (within one cel)
            _impactTime = _impactCel * _cel;
            // D308-10c: the cut pause belongs to the impact frame - it is taken only by a cast that asks for one (a groggy target)
            _pauseAllowed = host != null ? Mathf.Max(0f, host.CutPause) : 0f;
            _pause = 0f;
            _area = cast.Row.Category == DeployCategory308.AttackArea;
            _pathStart = _pathSeconds = 0f;
            int burstCels = Mathf.Max(1, Mathf.RoundToInt(p.Beats.Burst / _cel));
            // ignition shrinks first, then the silence, when the clock is shorter than their sum
            float lead = Mathf.Min(clock, p.Beats.Ignite + p.Beats.Silence);
            int igniteEnd = Mathf.Max(1, Mathf.CeilToInt(lead / _cel));

            Camera camera = host != null ? host.ViewCamera : null;
            Vector3 cameraWorld = camera != null ? camera.transform.position : cast.HasCameraPosition ? cast.CameraPosition : cast.Origin - transform.forward * 1.6f + Vector3.up * .2f;
            _eyeWorld = cameraWorld;

            // stroke root: at the origin, +Z toward the target (single) or along the plan direction (area), re-aimed per cel
            Vector3 targetWorld = cast.Target != null ? cast.Target.position + Vector3.up * ChestHeight : cast.FallbackPoint;
            Vector3 forward = targetWorld - cast.Origin;
            if (cast.Plan != null && cast.Plan.Direction.sqrMagnitude > .01f) forward = cast.Plan.Direction;
            bool single = cast.Row.Category == DeployCategory308.AttackSingle && cast.Plan == null;
            if (!single) forward.y = 0f;
            if (forward.sqrMagnitude < .0001f) forward = transform.forward;
            if (_root == null)
            {
                var go = new GameObject("InkDeploy308") { hideFlags = HideFlags.DontSave };
                _root = go.transform;
                go.AddComponent<MeshFilter>();
                _renderer = go.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off; _renderer.receiveShadows = false;
                _renderer.lightProbeUsage = LightProbeUsage.Off; _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            _root.gameObject.layer = Mathf.Clamp(p.RenderLayer, 0, 31);
            _root.SetParent(transform, false);
            _root.SetPositionAndRotation(cast.Origin, Quaternion.LookRotation(forward.normalized, Vector3.up));
            _root.localScale = Vector3.one;
            _distance0 = Mathf.Max(.5f, Vector3.Distance(cast.Origin, targetWorld));
            _cometSide = InkForms2Rules308.CometSide(cast.Origin, targetWorld, cast.Seed);
            if (_form == DeployForm308.Guard) PlaceGuard(cameraWorld, camera != null ? camera.transform.forward : forward);

            if (host == null || !host.RentBurst(this, out _buffer, out _mesh))
            {
                if (host != null) { _configured = false; _renderer.enabled = false; return false; }   // pool exhausted and nothing could be freed
                _buffer = new InkBurstBuffer308(tier.VertsPerBurst);
                _mesh = new Mesh { name = "InkDeploy308", hideFlags = HideFlags.DontSave };
                _mesh.MarkDynamic(); _ownsMesh = true; _rented = false;
            }
            else { _rented = true; _ownsMesh = false; }
            _buffer.Clear();

            float groundTarget = GroundWorldY(cast.Plan != null ? cast.Plan.Point : targetWorld, cast.Origin.y - 1f);
            // #308 forms2: a ward's centre and a summon's place ARE the ground points the rule found (the ward runtime probes the
            // player's own floor; a ray from above would find a cave's roof)
            float groundOrigin = _form == DeployForm308.Fence || cast.HasAnchor ? cast.Origin.y : GroundWorldY(cast.Origin, cast.Origin.y - 1f);
            _targetGroundWorld = new Vector3(targetWorld.x, groundTarget, targetWorld.z);
            var input = new DeployFormInput308
            {
                Profile = p, Tier = tier, Category = cast.Row.Category, Frame = cast.Row.Frame, Element = cast.Row.Element, Final = cast.Row.Final,
                Grade = p.GradeIndex(cast.Grade01), Shape = cast.Plan != null ? cast.Plan.Shape : AreaShape.None,
                Target = _root.InverseTransformPoint(targetWorld), Camera = _root.InverseTransformPoint(cameraWorld),
                GroundY = _root.InverseTransformPoint(new Vector3(targetWorld.x, groundTarget, targetWorld.z)).y,
                OriginGroundY = _root.InverseTransformPoint(new Vector3(cast.Origin.x, groundOrigin, cast.Origin.z)).y,
                Seed = cast.Seed, ImpactCel = _impactCel, BurstCels = burstCels, IgniteEndCel = igniteEnd, CelSeconds = _cel,
                WardRadius = cast.WardRadius, WardHeight = cast.WardHeight, Triggered = cast.Triggered,
                PlanPoint = _root.InverseTransformPoint(cast.Plan != null ? cast.Plan.Point : targetWorld), PlanDirection = Vector3.forward,
                Form = _form, GuardPhase = GuardPhase308.Window, PhaseCel = 0, GuardEndCel = _guard.EndCel, MeltCels = Mathf.Max(1, Mathf.RoundToInt(p.Beats.Melt / _cel)),
                Anchor = cast.HasAnchor ? _root.InverseTransformPoint(cast.Anchor) : Vector3.zero, HasAnchor = cast.HasAnchor, Cover = cast.Cover,
            };
            if (cast.Plan != null)
            {
                var plan = cast.Plan;
                input.Radius = plan.Radius; input.Length = plan.Length; input.HalfAngleDeg = plan.Angle; input.Speed = plan.Speed;
                if (plan.Shape == AreaShape.Volley)
                {
                    int shots = Mathf.Min(MaxShots, plan.Shots.Count);
                    float created = plan.CreatedAt;
                    for (int i = 0; i < shots; i++)
                    {
                        var shot = plan.Shots[i];
                        Vector3 point = shot.HasImpactPoint ? shot.ImpactPoint : shot.Target != null ? shot.Target.transform.position + Vector3.up * .8f : plan.Point;
                        _shotPoints[i] = _root.InverseTransformPoint(point);
                        _shotCels[i] = ImpactCelOf(Mathf.Max(0f, shot.ImpactTime - created), _cel);
                    }
                    input.Shots = shots; input.ShotPoints = _shotPoints; input.ShotCels = _shotCels;
                    if (shots > 0) { _impactCel = input.ImpactCel = _shotCels[0]; _impactTime = _impactCel * _cel; }
                }
                if (plan.Shape == AreaShape.Path) { _pathStart = Mathf.Max(0f, plan.Delay); _pathSeconds = plan.Length / Mathf.Max(.1f, plan.Speed); }
            }
            _input = input;
            _stats = InkDeployForms308.Compose(input, _buffer, _drops, _births);
            _buffer.Apply(_mesh);
            _root.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer.sharedMaterial = p.BurstMaterial;
            _renderer.enabled = p.BurstMaterial != null && _buffer.VertexCount > 0;

            // timeline (presentation seconds): impact -> burst -> hold -> melt
            bool areaHold = _area || cast.Row.Category == DeployCategory308.Field;
            float hold = areaHold ? p.Beats.HoldArea : p.Beats.HoldSingle;
            float burstEnd = Mathf.Max(_impactTime + p.Beats.Burst, (_stats.LastBirthCel + 1) * _cel);
            if (_pathSeconds > 0f) burstEnd = Mathf.Max(burstEnd, _pathStart + _pathSeconds);
            _holdEnd = burstEnd + hold;
            if (cast.HoldSeconds > 0f) _holdEnd = Mathf.Max(_holdEnd, cast.HoldSeconds - p.Beats.Melt);
            // #308 forms2: a fence starts to go when the ward's own fade begins; a guard stroke is gone when the rule's lifetime is
            // over (never later, however short the hold made it)
            if (_form == DeployForm308.Fence && cast.HoldSeconds > 0f && cast.FadeSeconds > 0f) _holdEnd = Mathf.Max(burstEnd, cast.HoldSeconds - cast.FadeSeconds);
            if (_form == DeployForm308.Guard) _holdEnd = Mathf.Max(_cel, cast.GuardLife - p.Beats.Melt);
            _meltEnd = _holdEnd + p.Beats.Melt;
            Duration = _meltEnd + _pauseAllowed + .05f;
            // the glow is momentary: it ends with the burst beat (a column's with its rise), whatever the data says. Cel ticks are
            // the only moments the material changes, so the end is the last tick at or before that moment.
            _glowEndCel = InkDeployForms308.GlowEndCel(burstEnd, _impactTime, p.Beats.Burst, _cel, _stats.HasColumn);
            if (_form == DeployForm308.Guard) _glowEndCel = _guard.GlowEndCel;   // only the cels the stroke is drawn on
            // round 2 ("a momentary glow at birth, GlowCels cels"): a standing form glows on the first GlowCels cels of its burst
            // only. A stroke born later in the form (a fence post round the ring, an outer curtain stroke) is born as ink -
            // before this every late stroke had its own cels and the form's glow ran on for 4 - 6 cels.
            else if (_form != DeployForm308.Category && _form != DeployForm308.Comet) _glowEndCel = Mathf.Min(_glowEndCel, (_form == DeployForm308.Loose ? 0 : _impactCel) + p.GlowCels);   // a loose mark is born on cel 0
            // forms4 (P3): the cover wall is an everyday ward that STANDS - like the fence it glows on its two birth cels only
            // (it took the burst beat's whole length: 7 cels)
            else if (_cast.Cover && _cast.Row.Category == DeployCategory308.Ward && p.Cover.Enabled && !_cast.Triggered) _glowEndCel = InkDeployForms308.CoverGlowEndCel(p, _glowEndCel, _impactCel);
            // every hit this cast schedules lands inside its burst beat (the judged clock, a corridor's front, a volley's shots):
            // one cel later it takes no more, so a cast that is only holding or melting never claims another cast's hit
            _hitEndCel = Mathf.CeilToInt(burstEnd / _cel) + 1;

            if (_block == null) _block = new MaterialPropertyBlock();
            Color tint = cast.Tint; tint.a = 1f;
            float wash = p.ElementOf(cast.Row.Element).Tint;
            _block.SetColor(TintId, tint);
            _block.SetFloat(TintAmountId, p.TintMax * wash);
            _block.SetFloat(SeedId, (cast.Seed & 1023) * .013f);
            _block.SetColor(GlowColorId, GlowHue(tint, wash));
            _block.SetFloat(GlowCelsId, p.GlowCels);
            _block.SetFloat(GlowRimId, Mathf.Clamp01(p.Stroke.GlowRim));
            _lastCel = int.MinValue; _nextDrop = 0; _impactSent = false; _released = false;
            ImpactRequests = ResidueRequests = GroggyAsked = HitsConfirmed = HitSplashes = 0;
            GroggyAtImpact = false; GlowNow = 0f;
            _pendingHitCount = _hitDropCount = 0;
            _heldOwner = 0;
            _configured = true;
            host?.NotifyDeployBegan(cast.Row.Char);
            Sample(0f);
            return true;
        }

        /// <summary>The glow's hue at full value (brightest channel 1): the element wash colour, or the paper's own pale value for an
        /// element that takes no wash (Metal reads by thinness, not colour). LDR by construction.</summary>
        public static Color GlowHue(Color tint, float washWeight)
        {
            float max = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            Color hue = max > .02f ? new Color(tint.r / max, tint.g / max, tint.b / max, 1f) : Color.white;
            return Color.Lerp(new Color(1f, .97f, .9f, 1f), hue, Mathf.Clamp01(washWeight));
        }

        private readonly RaycastHit[] _groundHits = new RaycastHit[16];
        private float GroundWorldY(Vector3 point, float fallback, Transform skip = null)
        {
            var residue = _cast.Profile.Residue;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 4f, Vector3.down, _groundHits, 14f, residue.GroundLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, height = fallback;
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider is CharacterController || hit.normal.y < residue.MinNormalY) continue;
                if (_cast.Target != null && hit.transform.IsChildOf(_cast.Target)) continue;
                if (skip != null && (hit.rigidbody != null || hit.transform.IsChildOf(skip))) continue;   // the struck enemy is not ground
                if (hit.distance >= best) continue;
                best = hit.distance; height = hit.point.y;
            }
            return height;
        }

        /// <summary>Presentation seconds for real seconds: the layer's own clock stops for CutPause on the burst's first cel.</summary>
        public float PresentationTime(float seconds)
        {
            if (_pause <= 0f || seconds <= _impactTime) return seconds;
            return seconds <= _impactTime + _pause ? _impactTime : seconds - _pause;
        }

        public void Sample(float seconds)
        {
            if (!_configured || _released) return;
            var p = _cast.Profile;
            float t = PresentationTime(Mathf.Max(0f, seconds));
            if (t >= _meltEnd) { Release(); return; }
            // #308 forms2, every frame (the cel tick below changes the look, these only move the root):
            if (_form == DeployForm308.Guard) FollowGuard();
            if (_stats.Comet && p.Comet.PerFrame) FollowComet(t);
            int cel = Mathf.FloorToInt(t / _cel + .0001f);
            // a cut pause taken on the impact cel holds the clock there: the cel never steps back
            if (cel == _lastCel || (_pause > 0f && cel < _lastCel)) return;
            _lastCel = cel;
            if (_form == DeployForm308.Guard)
            {
                var phase = InkForms2Rules308.GuardPhaseAt(_guard, cel, _guardBroken);
                if (phase != _guardPhase) Recompose(phase, cel);
            }

            Aim();
            float advance = 1f;
            if (_stats.HasAdvance)
            {
                if (_stats.HeadOnlyAdvance)
                {
                    // Path: the long stroke lengthens at the plan's speed, quantised to cels
                    float q = _pathSeconds > 0f ? Mathf.Clamp01((cel * _cel - _pathStart) / _pathSeconds) : 1f;
                    advance = Mathf.Clamp01(q);
                }
                else
                {
                    // single: the stroke head is the judged projectile's place, quantised to cels
                    float q = _impactCel <= 0 ? 1f : Mathf.Clamp01(cel / (float)_impactCel);
                    Vector3 target = _cast.Target != null ? _cast.Target.position + Vector3.up * ChestHeight : _cast.FallbackPoint;
                    advance = q * Mathf.Clamp(Vector3.Distance(_root.position, target) / _distance0, .25f, 4f);
                }
            }
            if (_flightAdvance >= 0f) advance = _flightAdvance;   // comet: the frame's own value, not the cel step
            if (_form == DeployForm308.Guard) advance = _guardPhase == GuardPhase308.Broken ? Mathf.Clamp01((cel - _breakCel + 1) / (float)Mathf.Max(1, p.Guard.BreakCels)) : 1f;
            float melt = t <= _holdEnd ? 0f : Mathf.Clamp01(Mathf.Ceil((t - _holdEnd) / _cel) * _cel / Mathf.Max(_cel, p.Beats.Melt));
            float rise = 1f, drain = 0f;
            if (_stats.HasColumn)
            {
                int riseCels = Mathf.Max(1, Mathf.RoundToInt(p.Beats.Burst / _cel));
                rise = Mathf.Clamp01((cel - _impactCel + 1) / (float)riseCels);
                float riseEnd = _impactTime + riseCels * _cel;
                float settle = _stats.ColumnHolds ? p.Stroke.ColumnHoldDrain : 1f;
                float settleSeconds = Mathf.Max(_cel, _stats.ColumnHolds ? p.Beats.HoldSingle : p.Beats.HoldSingle + p.Beats.Melt);
                if (t > riseEnd) drain = settle * Mathf.Clamp01(Mathf.Ceil((t - riseEnd) / _cel) * _cel / settleSeconds);
                if (_stats.ColumnHolds && t > _holdEnd) drain = Mathf.Lerp(settle, 1f, melt);
                if (_stats.ColumnHolds) melt = 0f;   // the wall drains instead of melting
            }
            // D308-10c momentary glow: a stroke glows for GlowCels cels after its birth (the shader's falloff), and only inside
            // the burst beat - the hold, the melt and a ward's standing wall get 0 from here
            GlowNow = InkDeployForms308.GlowAmount(p, cel, _glowEndCel, melt);
            // look2: the standing forms are wide - at the full birth glow they flash as a flat element-coloured shape
            // (forms3 fix pass: a cover's rising strokes stand like the forms2 forms - the standing share of the birth glow)
            if ((_form != DeployForm308.Category && _form != DeployForm308.Comet) || (_cast.Cover && _cast.Row.Category == DeployCategory308.Ward && p.Cover.Enabled && !_cast.Triggered)) GlowNow *= Mathf.Clamp01(p.StandingGlow);
            _block.SetFloat(CelId, cel);
            _block.SetFloat(AdvanceId, advance);
            _block.SetFloat(MeltId, melt);
            _block.SetFloat(RiseId, rise);
            _block.SetFloat(DrainId, drain);
            _block.SetFloat(GlowId, GlowNow);
            if (_renderer != null) _renderer.SetPropertyBlock(_block);

            if (_host == null) return;
            // residue and air drops whose cel has come (the form sorted nothing: scan the short list)
            for (int i = 0; i < _stats.Drops && i < MaxDrops; i++)
            {
                if (_drops[i].Cel < 0 || _drops[i].Cel > cel) continue;
                Emit(_drops[i]);
                _drops[i].Cel = -1;
            }
            if (!_impactSent && cel >= _impactCel)
            {
                _impactSent = true;
                // D308-10c: impact frames only for a spell that lands on a GROGGY enemy (the rule is InkDeployForms308.ImpactWanted).
                // Single: the judged target. Area: any target the plan scheduled a hit on. Combo trigger: the triggered target,
                // read after the trigger (it made the target groggy, or landed on a groggy one). Everything else asks for nothing
                // at all: no full-screen frame, no local form, and so no HUD reaction and no cut pause.
                if (InkDeployForms308.ImpactEligible(p, _cast.Row, _cast.Triggered))
                {
                    bool groggy = false;
                    if (InkDeployForms308.ImpactAsksGroggy(p, _cast.Row, _cast.Triggered)) { GroggyAsked++; groggy = _host.TargetGroggy(_cast.Target, _cast.Plan); }
                    GroggyAtImpact = groggy;
                    if (InkDeployForms308.ImpactWanted(p, _cast.Row, _cast.Triggered, groggy))
                    {
                        Vector3 point = _stats.HasAdvance && !_stats.HeadOnlyAdvance
                            ? (_cast.Target != null ? _cast.Target.position + Vector3.up * ChestHeight : _cast.FallbackPoint)
                            : _cast.Plan != null ? ImpactPointOf(_cast.Plan)
                            : _cast.Target != null ? _cast.Target.position + Vector3.up * ChestHeight : _cast.FallbackPoint;
                        int frames = _cast.Triggered ? 3 : p.Grade(_cast.Grade01).ImpactFrames;
                        _host.RequestImpact(new ImpactRequest308 { WorldPoint = point, Frames = frames, Strength = 1f });
                        ImpactRequests++;
                        _pause = _pauseAllowed;
                    }
                }
            }
            if (_pendingHitCount > 0 && cel >= _impactCel) FlushHits();
        }

        // ---- D308-10c: the confirmed hit ("the effect that bursts out when the enemy is hit")

        /// <summary>Would this cast take a confirmed hit on `target` by a spell of `element`? Attack casts only (and the triggered
        /// combo burst): a single cast takes its own judged target, an area cast any enemy its plan reaches.</summary>
        public bool AcceptsHit(Transform target, Element element)
        {
            if (!_configured || _released || target == null || _cast.Profile == null || !_cast.Profile.Hit.Enabled) return false;
            var category = _cast.Row.Category;
            bool attack = category == DeployCategory308.AttackSingle || category == DeployCategory308.AttackArea || _cast.Triggered;
            if (!attack || _cast.Row.Element != element || _lastCel > _hitEndCel || HitSplashes + _pendingHitCount >= _cast.Profile.Hit.MaxPerCast) return false;
            if (_cast.Plan != null) return true;
            return _cast.Target != null && (target == _cast.Target || target.IsChildOf(_cast.Target) || _cast.Target.IsChildOf(target));
        }

        /// <summary>The wiring CONFIRMED a hit of this cast on `target` (EnemyDamageResolved: damage was applied). The splash is
        /// shown on the burst's first cel when the confirmation comes before it (at most half a cel), at once otherwise.
        /// A cast whose hits are never confirmed (a miss, a target that died first) keeps the plain burst. Presentation only.</summary>
        public bool ConfirmHit(Transform target)
        {
            if (_host == null || !AcceptsHit(target, _cast.Row.Element) || _pendingHitCount >= MaxPendingHits) return false;
            HitsConfirmed++;
            _pendingHits[_pendingHitCount++] = target;
            if (_lastCel >= _impactCel) FlushHits();
            return true;
        }

        private void FlushHits()
        {
            var p = _cast.Profile;
            Camera camera = _host != null ? _host.ViewCamera : null;
            Vector3 eye = camera != null ? camera.transform.position : _eyeWorld;
            for (int i = 0; i < _pendingHitCount; i++)
            {
                var target = _pendingHits[i]; _pendingHits[i] = null;
                if (target == null || HitSplashes >= p.Hit.MaxPerCast) continue;
                Vector3 point = target.position + Vector3.up * ChestHeight;
                ComposeHit(point, eye, GroundWorldY(point, point.y - ChestHeight, target));
                for (int k = 0; k < _hitDropCount; k++) EmitHit(_hitDrops[k]);
                HitSplashes++;
            }
            _pendingHitCount = 0;
        }

        /// <summary>Fills the hit splash for a hit at `point` (world) without sending anything: FlushHits and the edit-mode preview
        /// read it back through HitDropCount / HitDrop. Deterministic for a cast (its seed + the number of splashes so far).</summary>
        public int ComposeHit(Vector3 point, Vector3 eyeWorld, float groundY)
        {
            var p = _cast.Profile;
            _hitGroundY = groundY;
            _hitDropCount = p == null ? 0 : InkDeployForms308.ComposeHit(new DeployHitInput308
            {
                Profile = p, Tier = _host != null ? _host.Tier : _cast.Tier, Element = _cast.Row.Element, Grade = _cast.Triggered ? 2 : p.GradeIndex(_cast.Grade01),
                Point = point, ToEye = eyeWorld - point, GroundY = groundY, Seed = _cast.Seed * 31 + 17 + HitSplashes * 7919,
            }, _hitDrops);
            return _hitDropCount;
        }

        private void EmitHit(in DeployDrop308 drop)
        {
            var p = _cast.Profile;
            if (drop.Air)
            {
                _host.SpawnAir(drop.Local, drop.Velocity, drop.Size, drop.Cell, drop.Life > 0f ? drop.Life : 1.6f, drop.Still ? 0f : 1f, _hitGroundY);
                ResidueRequests++;
                return;
            }
            _host.StampResidue(new ResidueStamp308
            {
                Point = drop.Local + Vector3.up * .5f, Forward = _root != null ? _root.forward : Vector3.forward, Width = drop.Size, Length = drop.Size, Cell = drop.Cell,
                Opacity = p.Residue.Opacity, Life = p.SpellLife,
            });
            ResidueRequests++;
        }

        /// <summary>Where an area cast's impact frame opens: a cone / corridor starts at the caster's feet, so its middle is used.</summary>
        public static Vector3 ImpactPointOf(AreaImpactPlan plan)
        {
            Vector3 point = plan.Point + Vector3.up * .6f;
            if (plan.Shape != AreaShape.Cone && plan.Shape != AreaShape.Path) return point;
            Vector3 direction = new Vector3(plan.Direction.x, 0f, plan.Direction.z);
            return direction.sqrMagnitude > 1e-4f && plan.Length > 0f ? point + direction.normalized * (plan.Length * .55f) : point;
        }

        private void Aim()
        {
            // re-aim only while a single cast is in flight: the judged target may move
            if (_root == null || !_stats.HasAdvance || _stats.HeadOnlyAdvance || _lastCel > _impactCel || _flightAdvance >= 0f) return;
            Vector3 target = _cast.Target != null ? _cast.Target.position + Vector3.up * ChestHeight : _cast.FallbackPoint;
            Vector3 forward = target - _root.position;
            if (forward.sqrMagnitude > .01f) _root.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void Emit(in DeployDrop308 drop)
        {
            var p = _cast.Profile;
            Vector3 world = _root.TransformPoint(drop.Local);
            if (drop.Air)
            {
                _host.SpawnAir(world, _root.TransformDirection(drop.Velocity), drop.Size, drop.Cell, 1.6f, 1f, _targetGroundWorld.y);
                ResidueRequests++;
                return;
            }
            bool held = InkForms2Rules308.DropHeld(drop, p.CategoryOf(_cast.Row.Category).HeldResidue);
            if (held && _heldOwner == 0) _heldOwner = GetInstanceID();
            _host.StampResidue(new ResidueStamp308
            {
                Point = world + Vector3.up * .5f, Forward = _root.forward, Width = drop.Size, Length = drop.Size, Cell = drop.Cell,
                Opacity = p.Residue.Opacity, Life = held ? p.FieldTail : p.SpellLife, Held = held, Owner = held ? _heldOwner : 0,
            });
            ResidueRequests++;
        }

        // ---- #308 forms2

        private void PlaceGuard(Vector3 eye, Vector3 viewForward)
        {
            InkForms2Rules308.GuardPose(_cast.Profile, eye, viewForward, out Vector3 position, out Vector3 flat);
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(flat, Vector3.up));
        }

        // the guard stroke is held in front of the body: it follows the camera's heading (not its pitch) every frame
        private void FollowGuard()
        {
            Camera camera = _host != null ? _host.ViewCamera : null;
            if (camera == null || _root == null) return;   // no camera (preview): it stays where Configure put it
            PlaceGuard(camera.transform.position, camera.transform.forward);
        }

        // The comet is the judged flight made visible: the same clock (the flight time the wiring planned the hit with) and the
        // same target transform, read every frame. Nothing is simulated and nothing is fed back; only the root moves and one
        // property changes. After the impact cel it stays where it arrived (the burst is the impact).
        private void FollowComet(float t)
        {
            if (_root == null || JudgementSeconds <= 0f || t > _impactTime + _cel) return;
            // a target that is destroyed in flight leaves the comet flying to where it last was (never a jump to the cast's fallback point)
            if (_cast.Target != null) { _cometGoal = _cast.Target.position + Vector3.up * ChestHeight; _cometGoalSeen = true; }
            Vector3 target = _cometGoalSeen ? _cometGoal : _cast.FallbackPoint;
            Vector3 line = target - _cast.Origin;
            float u = InkForms2Rules308.CometU(t, JudgementSeconds);
            Vector3 offset = InkForms2Rules308.CometOffset(u, _cast.PathArc, _cast.PathCurve, _cometSide);
            _root.SetPositionAndRotation(_cast.Origin + offset, line.sqrMagnitude > .01f ? Quaternion.LookRotation(line.normalized, Vector3.up) : _root.rotation);
            _flightAdvance = InkForms2Rules308.CometEase(u, _cast.PathEase) * Mathf.Clamp(line.magnitude / _distance0, .25f, 4f);
            if (_lastCel == int.MinValue || _block == null || _renderer == null) return;   // the first cel tick writes the whole block
            _block.SetFloat(AdvanceId, _flightAdvance);
            _renderer.SetPropertyBlock(_block);
        }

        // the guard stroke's look changes at most twice (window -> dry, or a parry breaks it): the mesh is assembled again then
        private void Recompose(GuardPhase308 phase, int cel)
        {
            _guardPhase = phase;
            if (phase == GuardPhase308.Broken) _breakCel = cel;
            if (_buffer == null || _mesh == null) return;
            _input.GuardPhase = phase; _input.PhaseCel = cel;
            _buffer.Clear();
            _stats = InkDeployForms308.Compose(_input, _buffer, _drops, _births);
            _buffer.Apply(_mesh);
            if (_renderer != null) _renderer.enabled = _cast.Profile.BurstMaterial != null && _buffer.VertexCount > 0;
            MeshRebuilds++;
        }

        /// <summary>The rule resolved an impact on the guard this stroke shows (the wiring's re-broadcast, relayed by the host).
        /// Only a parry changes the stroke: it is cut and goes within Guard.BreakCels cels - the rule has consumed the guard.
        /// Presentation only: nothing is fed back.</summary>
        public void NoteGuardContact(GuardContact308 contact)
        {
            if (!_configured || _released || _form != DeployForm308.Guard || contact != GuardContact308.Success || _guardBroken) return;
            _guardBroken = true;
            _holdEnd = (Mathf.Max(0, _lastCel) + 1) * _cel;
            _meltEnd = _holdEnd + Mathf.Max(1, _cast.Profile.Guard.BreakCels) * _cel;
        }

        /// <summary>The guard this stroke shows is gone on the rule side (replaced, cleared, expired early): it melts now.</summary>
        public void EndGuard()
        {
            if (!_configured || _released || _form != DeployForm308.Guard || _guardBroken) return;
            float now = (Mathf.Max(0, _lastCel) + 1) * _cel;
            if (now >= _holdEnd) return;
            _holdEnd = now; _meltEnd = now + _cast.Profile.Beats.Melt;
        }

        /// <summary>Hand the burst back (pool pressure, end of the timeline, destruction). Held marks start to dry.</summary>
        public void Release()
        {
            if (_released && !_configured) return;
            _released = true; _configured = false;
            GlowNow = 0f;
            for (int i = 0; i < MaxPendingHits; i++) _pendingHits[i] = null;
            _pendingHitCount = 0;
            if (_renderer != null) _renderer.enabled = false;
            if (_host != null && _heldOwner != 0) _host.ReleaseHeld(_heldOwner);
            _heldOwner = 0;
            if (_rented && _host != null) _host.ReturnBurst(this, _buffer, _mesh);
            else if (_ownsMesh && _mesh != null) { if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh); }
            _rented = _ownsMesh = false; _buffer = null; _mesh = null;
        }

        public void Dispose()
        {
            Release();
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject); else DestroyImmediate(_root.gameObject);
            _root = null; _renderer = null;
        }

        private void OnDestroy() { Dispose(); }
    }
}
