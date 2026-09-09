using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    // Presentation only. The supplied combat clock is never advanced or re-issued here.
    // Meshes/materials are shared assets; per-instance pigment and erosion use property blocks.
    public sealed class Vfx120Effect : SpellSequenceEffect
    {
        public Vfx120Profile Profile;
        public bool PreviewControlled;
        // Both flags are required. No production Begin/Update enables demonstration cues.
        public bool DemonstrationCues;
        public enum Cue { None, Hit, TargetDefeated, Release, Parry, Break }
        public float Age { get; private set; }
        public float Life { get; private set; }
        public float ReceivedImpactClock { get; private set; }
        public float ReceivedGuardClock { get; private set; }
        public float ReceivedGuardBrightWindow { get; private set; }
        public Vector3 ReceivedOrigin { get; private set; }
        public Vector3 ReceivedFallback { get; private set; }
        public Transform ReceivedTarget { get; private set; }
        public AreaImpactPlan ReceivedAreaPlan { get; private set; }
        public int PartCount => _parts == null ? 0 : _parts.Length;
        public bool Begun { get; private set; }
        public int AccentCount => _accents == null ? 0 : _accents.Length;
        public float HitAt { get; private set; } = -1;
        public float AdditionalHitAt { get; private set; } = -1;
        public float TargetDefeatedAt { get; private set; } = -1;
        public float ReleaseAt { get; private set; } = -1;
        public float ParryAt { get; private set; } = -1;
        public float BreakAt { get; private set; } = -1;
        public int SignalCount { get; private set; }

        private Transform[] _parts;
        private Renderer[] _renderers;
        private Transform[] _accents;
        private Renderer[] _accentRenderers;
        private LineRenderer[] _ribbons;
        private Transform _seal;
        private Renderer _sealRenderer;
        private Vfx120Atmosphere _atmosphere;
        private MaterialPropertyBlock _block;
        private Vector3 _aim;
        private float _flight;
        private Vector3 _direction;
        private float _originGround, _targetGround;
        private float _startedAt;
        private Transform _secondaryTarget;
        private Transform[] _secondaryTargets;
        private Vector3[] _secondaryPoints;
        private readonly RaycastHit[] _groundHits = new RaycastHit[24];
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int ErodeId = Shader.PropertyToID("_Erode");
        private static readonly int AgeId = Shader.PropertyToID("_Age");

        public override void SetImpactClock(float duration) { ReceivedImpactClock = duration; }
        public override void SetAreaPlan(AreaImpactPlan plan) { ReceivedAreaPlan = plan; }
        // Target transforms are actor roots, matching Begin's target convention (+1.1m chest).
        // Copies are made only when an adapter supplies new targets, never while sampling.
        public void SetSecondaryTargets(Transform secondary, Transform[] splitTargets = null)
        {
            _secondaryTarget = secondary;
            _secondaryTargets = splitTargets == null ? null : (Transform[])splitTargets.Clone();
            _secondaryPoints = splitTargets == null ? null : new Vector3[splitTargets.Length];
        }

        // This API records presentation cues only. It never selects targets, deals damage,
        // reissues impacts, changes authoritative clocks, or automatically simulates a hit.
        public void Signal(Cue cue, Transform secondary = null)
        {
            if (!Begun || cue == Cue.None) return;
            if (secondary != null) _secondaryTarget = secondary;
            float at = PreviewControlled ? Age : Mathf.Max(Age, Time.time - _startedAt);
            switch (cue)
            {
                case Cue.Hit:
                    if (HitAt < 0) HitAt = at;
                    if (at >= _flight + .025f && AdditionalHitAt < 0) AdditionalHitAt = at;
                    break;
                case Cue.TargetDefeated: if (TargetDefeatedAt < 0) TargetDefeatedAt = at; break;
                case Cue.Release: if (ReleaseAt < 0) ReleaseAt = at; break;
                case Cue.Parry: if (ParryAt < 0) ParryAt = at; break;
                case Cue.Break: if (BreakAt < 0) BreakAt = at; break;
            }
            SignalCount++;
        }
        public void SetGuardClock(float duration, float brightWindow)
        {
            ReceivedGuardClock = duration;
            ReceivedGuardBrightWindow = brightWindow;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            HitAt = AdditionalHitAt = TargetDefeatedAt = ReleaseAt = ParryAt = BreakAt = -1;
            SignalCount = 0;
            ReceivedOrigin = origin;
            ReceivedTarget = target;
            ReceivedFallback = fallbackPoint;
            // The diagnostic record remains available even for a missing profile.
            if (Profile == null) return;
            _block = new MaterialPropertyBlock();
            transform.SetParent(null, true);
            transform.position = origin;
            transform.localScale = Vector3.one;
            _direction = fallbackPoint - origin;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Direction.sqrMagnitude > .01f)
                _direction = ReceivedAreaPlan.Direction;
            _direction.y = 0;
            if (_direction.sqrMagnitude < .01f) _direction = Vector3.forward;
            _direction.Normalize();
            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
            _aim = transform.InverseTransformPoint(TargetPoint());
            _originGround = GroundHeight(origin, origin.y - 1f) - origin.y;
            _targetGround = GroundHeight(HasSpatialPlan ? ReceivedAreaPlan.Point : TargetPoint(), origin.y - 1f) - origin.y;
            _flight = ReceivedImpactClock > 0 ? ReceivedImpactClock : Profile.Flight;
            if (HasSpatialPlan) _flight = Mathf.Max(.01f, ReceivedAreaPlan.Delay);
            Life = ReceivedGuardClock > 0 ? ReceivedGuardClock : Mathf.Max(Profile.Duration, _flight + .9f);
            _startedAt = Time.time;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley)
                foreach (var shot in ReceivedAreaPlan.Shots) Life = Mathf.Max(Life, shot.ImpactTime - _startedAt + .7f);
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Path)
                Life = Mathf.Max(Life, ReceivedAreaPlan.Delay + ReceivedAreaPlan.Length / Mathf.Max(.1f, ReceivedAreaPlan.Speed) + .7f);
            Build();
            Begun = true;
            Sample(0);
        }

        private Vector3 TargetPoint()
        {
            return ReceivedTarget != null ? ReceivedTarget.position + Vector3.up * 1.1f : ReceivedFallback;
        }

        private float GroundHeight(Vector3 point, float fallback)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 4, Vector3.down, _groundHits, 12, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity, height = fallback;
            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider is CharacterController || hit.normal.y < .45f) continue;
                if (ReceivedTarget != null && hit.transform.IsChildOf(ReceivedTarget)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance; height = hit.point.y;
            }
            return height;
        }

        private void Build()
        {
            int n = Mathf.Clamp(Profile.Count, 1, 32);
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Path && Profile.Family == "WaveCrest") n = 1;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley && ReceivedAreaPlan.Shots.Count > 0)
                n = Mathf.Min(32, ReceivedAreaPlan.Shots.Count);
            _parts = new Transform[n]; _renderers = new Renderer[n];
            int accentCount = Mathf.Max(n, Vfx120CueMotion.GetRequiredAccentCount(Profile.Glyph));
            accentCount = Mathf.Max(accentCount, Vfx120VariantMotion.GetSelfAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120SummonMotion.GetAccentCount(Profile));
            _accents = new Transform[accentCount]; _accentRenderers = new Renderer[accentCount];
            for (int i = 0; i < n; i++)
            {
                _parts[i] = MeshPart("Body_" + i, Profile.BodyMesh, Profile.BodyMaterial, out _renderers[i]);
            }
            for (int i = 0; i < accentCount; i++)
                _accents[i] = MeshPart("Flecks_" + i, Profile.AccentMesh,
                    Profile.Behavior == Vfx120Behavior.Summon && Profile.Family == "Beast" ? Profile.BodyMaterial : Profile.InkMaterial,
                    out _accentRenderers[i]);
            _seal = MeshPart("TraditionalMotif", QuadMesh.Value, Profile.PatternMaterial, out _sealRenderer);
            _ribbons = new LineRenderer[Mathf.Clamp(Profile.RibbonCount, 0, 3)];
            for (int i = 0; i < _ribbons.Length; i++)
            {
                var go = new GameObject("InkGesture_" + i);
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false; line.positionCount = 48;
                line.sharedMaterial = Profile.InkMaterial;
                line.numCornerVertices = 2; line.numCapVertices = 3;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.widthCurve = new AnimationCurve(new Keyframe(0, .03f), new Keyframe(.16f, .8f), new Keyframe(.65f, 1f), new Keyframe(1, .02f));
                _ribbons[i] = line;
            }
            _atmosphere = Vfx120Atmosphere.Create(Profile, transform);
        }

        private Transform MeshPart(string label, Mesh mesh, Material material, out Renderer renderer)
        {
            var go = new GameObject(label); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            renderer = mr; return go.transform;
        }

        private void Update()
        {
            if (!Begun || PreviewControlled) return;
            Sample(Age + Time.deltaTime);
            if (Age >= Life) Destroy(gameObject);
        }

        // Analytic sampling permits the same authored motion at 30/60/120 fps and in the review camera.
        public void Sample(float seconds)
        {
            if (!Begun || Profile == null) return;
            Age = Mathf.Max(0, seconds);
            _aim = transform.InverseTransformPoint(TargetPoint());
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Life - .7f, Life, Age));
            float grow = Mathf.SmoothStep(0, 1, Age / Mathf.Min(.28f, _flight));
            float arrive = Mathf.SmoothStep(0, 1, (Age - _flight) / .22f);
            Vector3 center = Center();
            bool authoritativeVolley = ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley
                && ReceivedAreaPlan.Shots.Count > 0;
            var cueContext = CueContext();
            bool cueMotion = !authoritativeVolley && Vfx120CueMotion.GetRequiredAccentCount(Profile.Glyph) > 0;
            if (cueMotion && Vfx120CueMotion.TrySample(cueContext, Vfx120CueMotion.PartRole.Body, 0, _parts.Length, out var anchor))
                center = anchor.Visible ? anchor.Position : Vector3.Lerp(Vector3.zero, _aim, Mathf.Clamp01(Age / Mathf.Max(.01f, _flight)));
            float size = Profile.Size;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Radius > 0) size = ReceivedAreaPlan.Radius;
            for (int i = 0; i < _parts.Length; i++)
            {
                float u = (i + .5f) / _parts.Length;
                float delay = Profile.Stagger * u;
                float pulse = Mathf.SmoothStep(0, 1, (Age - delay) / .25f) * fade;
                Vector3 local = LayoutPosition(u, Age, size);
                Vector3 axis = LayoutAxis(u, local);
                float length = 1;
                if (Profile.Behavior == Vfx120Behavior.Projectile)
                {
                    // A coherent flight body becomes splinters only after the authoritative arrival.
                    local = Vector3.Lerp(local * .12f, local, arrive);
                    axis = Vector3.Lerp(_aim.sqrMagnitude > .001f ? _aim.normalized : Vector3.forward, local.normalized, arrive);
                    length = Mathf.Lerp(1, .5f, arrive);
                }
                if (Profile.Behavior == Vfx120Behavior.Summon)
                {
                    local.y += Mathf.Sin(Age * 5 + u * 2) * .045f;
                    axis = Vector3.forward;
                }
                if (Profile.Family == "Tree" || Profile.Family == "WaveCrest") axis = Vector3.forward;
                if (Profile.Behavior == Vfx120Behavior.Heal)
                    local.y += Mathf.Repeat(Age * .55f + u, 1.8f);
                if (Profile.Behavior == Vfx120Behavior.Reserve)
                {
                    pulse *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Life * .25f, Life * .85f, Age));
                    local += Vector3.down * Age * .2f;
                }
                Vector3 partCenter = center;
                Vector3 partScale = Vector3.Scale(Profile.PartScale, new Vector3(1, 1, length)) * pulse;
                Vfx120VariantMotion.Apply(Profile, Age, _flight, Life, i, _parts.Length, _aim, ref partCenter, ref local, ref axis, ref partScale, size, _originGround, _targetGround);
                if (HasSpatialPlan)
                    ApplyAreaPose(u, pulse, ref partCenter, ref local, ref axis, ref partScale);
                else if (Profile.Glyph == "막")
                {
                    // One airborne stone becomes planted cover at the selected landing point.
                    // A Shield classification must not pull this attack back to the caster.
                    partCenter = center; local = Vector3.zero; axis = Vector3.forward;
                    partScale = i == 0 ? Profile.PartScale * pulse : Vector3.zero;
                }
                else if (Profile.Glyph == "뭄")
                {
                    float assembly=Mathf.SmoothStep(0,1,Mathf.InverseLerp(i*.10f,i*.10f+.25f,Age));
                    partCenter=new Vector3(0,_originGround+.12f,.65f+i*.66f);
                    local=Vector3.zero;axis=Vector3.forward;
                    partScale=Profile.PartScale*assembly*fade;
                }
                else if (Profile.Glyph == "무")
                {
                    // Earth cover stands on the caster's ground, with an open back for readability.
                    float angle = Mathf.Lerp(-Mathf.PI * .65f, Mathf.PI * .65f, u);
                    var bounds = Profile.BodyMesh.bounds.size;
                    float height = 1.25f + .22f * Mathf.Cos(angle);
                    partScale = new Vector3(.62f / bounds.x, height / bounds.y, .48f / bounds.z) * pulse;
                    partCenter = new Vector3(0, _originGround + height * pulse * .5f + .015f, 0);
                    local = new Vector3(Mathf.Sin(angle) * 1.25f, 0, Mathf.Cos(angle) * 1.25f);
                    axis = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                }
                if (authoritativeVolley && i < ReceivedAreaPlan.Shots.Count)
                {
                    var shot = ReceivedAreaPlan.Shots[i];
                    Vector3 end = shot.Target != null ? transform.InverseTransformPoint(shot.Target.transform.position + Vector3.up * 1.1f) : _aim;
                    float arrival = Mathf.Max(.04f, shot.ImpactTime - _startedAt);
                    float travel = end.magnitude / Mathf.Max(1, ReceivedAreaPlan.Speed);
                    float launch = Mathf.Max(0, arrival - travel);
                    float t = Mathf.InverseLerp(launch, arrival, Age);
                    partCenter = Vector3.Lerp(Vector3.zero, end, t);
                    local = Vector3.zero; axis = end.sqrMagnitude > .001f ? end.normalized : Vector3.forward;
                    partScale = Profile.PartScale * (Age < launch ? 0 : 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(arrival, arrival + .25f, Age)));
                }
                if (cueMotion && Vfx120CueMotion.TrySample(cueContext, Vfx120CueMotion.PartRole.Body, i, _parts.Length, out var bodyPose))
                {
                    ApplyCuePose(_parts[i], _renderers[i], bodyPose, Color.Lerp(Profile.Pigment, Profile.Accent, (i % 4) * .14f));
                    continue;
                }
                _renderers[i].enabled = true;
                _parts[i].localPosition = partCenter + local * grow;
                if (axis.sqrMagnitude < .001f) axis = Vector3.up;
                _parts[i].localRotation = Quaternion.LookRotation(axis, Mathf.Abs(axis.normalized.y) > .98f ? Vector3.forward : Vector3.up);
                _parts[i].localScale = partScale;
                if (Profile.Grounded && (Profile.Family == "Tree" || Profile.Family == "WaveCrest" || Profile.Behavior == Vfx120Behavior.Summon || Profile.Layout == Vfx120Layout.Field || Profile.Glyph == "막"))
                {
                    var e = Profile.BodyMesh.bounds.extents;
                    Quaternion q = _parts[i].localRotation;
                    float halfY = Mathf.Abs((q * Vector3.right).y) * e.x * partScale.x + Mathf.Abs((q * Vector3.up).y) * e.y * partScale.y + Mathf.Abs((q * Vector3.forward).y) * e.z * partScale.z;
                    var position = _parts[i].localPosition;
                    position.y = Mathf.Max(position.y, _targetGround + halfY + .015f);
                    _parts[i].localPosition = position;
                }
                Tint(_renderers[i], Color.Lerp(Profile.Pigment, Profile.Accent, (i % 4) * .14f), pulse, 1 - fade);
            }
            // Accent geometry has its own authored count; e.g. one tree has twelve leaves.
            for (int i = 0; i < _accents.Length; i++)
            {
                if (Vfx120VariantMotion.TrySampleSelfAccent(Profile, Age, Life, _originGround, i, _accents.Length, out var selfPose))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], selfPose, Color.Lerp(Profile.Pigment, Profile.Accent, .65f));
                    continue;
                }
                if (Vfx120SummonMotion.TrySampleAccent(Profile, Age, Life, _parts[0].localPosition,
                    _parts[0].localRotation, _parts[0].localScale, i, _accents.Length, out var summonPose))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], summonPose, Color.Lerp(Profile.Pigment, Profile.Accent, i % 3 == 0 ? .8f : .25f));
                    continue;
                }
                if (cueMotion && Vfx120CueMotion.TrySample(cueContext, Vfx120CueMotion.PartRole.Accent, i, _accents.Length, out var accentPose))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], accentPose, i % 3 == 0 ? Profile.Accent : Profile.Pigment);
                    continue;
                }
                float u = (i + .5f) / _accents.Length;
                float pulse = Mathf.SmoothStep(0, 1, (Age - Profile.Stagger * u) / .25f) * fade;
                Vector3 local = LayoutPosition(u, Age, size);
                float a = u * Mathf.PI * 2 + Age * (.5f + Profile.Turns * .2f);
                Vector3 dust = new Vector3(Mathf.Cos(a), Mathf.Sin(Age * 2 + u * 9) * .3f, Mathf.Sin(a));
                _accentRenderers[i].enabled = true;
                _accents[i].localPosition = center + local * (.5f + arrive * .5f) + dust * .2f;
                _accents[i].localRotation = Quaternion.Euler(u * 270 + Age * 50, a * Mathf.Rad2Deg, Age * 24);
                _accents[i].localScale = Vector3.one * (.045f + (i % 3) * .025f) * pulse;
                if (authoritativeVolley || HasSpatialPlan)
                {
                    int shotIndex = i % _parts.Length;
                    _accents[i].localPosition = _parts[shotIndex].localPosition + dust * .06f;
                    _accents[i].localScale *= _parts[shotIndex].localScale.sqrMagnitude > .000001f ? 1 : 0;
                }
                Tint(_accentRenderers[i], i % 3 == 0 ? Profile.Accent : Profile.Ink, pulse * .75f, 1 - fade);
            }
            bool guard = Profile.Behavior == Vfx120Behavior.Shield;
            if (Profile.Glyph == "막") guard = false;
            bool wideGuard = IsWideGuard();
            bool castOnly = Profile.Behavior == Vfx120Behavior.Projectile || Profile.Behavior == Vfx120Behavior.Weapon;
            bool selfZone = Profile.Glyph == "넉" || Profile.Glyph == "국";
            float ground = !selfZone && (Profile.Behavior == Vfx120Behavior.Bind || Profile.Behavior == Vfx120Behavior.Zone || Profile.Behavior == Vfx120Behavior.Burst || Profile.Behavior == Vfx120Behavior.Summon) ? _targetGround : _originGround;
            Vector3 sealCenter = guard && !wideGuard ? center : new Vector3(center.x, ground + .04f, center.z);
            if (castOnly) sealCenter = new Vector3(0, 0, .15f);
            _seal.localPosition = sealCenter;
            _seal.localRotation = (guard && !wideGuard) || castOnly ? Quaternion.identity : Quaternion.Euler(90, 0, 0);
            _seal.Rotate(Vector3.forward, Mathf.Sin(Age * .5f) * 2.5f, Space.Self);
            _seal.localScale = Vector3.one * size * 1.8f * (.8f + grow * .2f);
            if (HasSpatialPlan)
            {
                Vector3 planPoint = transform.InverseTransformPoint(ReceivedAreaPlan.Point);
                _seal.localPosition = new Vector3(planPoint.x, _targetGround + .035f, planPoint.z);
                _seal.localRotation = Quaternion.Euler(90, 0, 0);
                _seal.localScale = Vector3.one * (ReceivedAreaPlan.Shape == AreaShape.Circle ? ReceivedAreaPlan.Radius * 2 : .65f);
            }
            float sealAlpha = fade * grow * (guard ? .7f : .24f);
            if (castOnly) sealAlpha *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .5f, Age));
            if (ReceivedGuardClock > 0 && Age > ReceivedGuardBrightWindow) sealAlpha *= .45f;
            if (cueMotion) SampleCueSeal(cueContext, ref sealAlpha);
            Tint(_sealRenderer, Profile.Accent, sealAlpha, 1 - fade);
            for (int r = 0; r < _ribbons.Length; r++)
            {
                var line = _ribbons[r];
                // The cue meshes already provide the attached/transfer trajectory.
                // Generic orbit ribbons would falsely imply an active zone before a hit.
                line.enabled = !cueMotion && !HasSpatialPlan && Profile.Glyph != "막";
                if (!line.enabled) { line.widthMultiplier = 0; Tint(line, Profile.Ink, 0, 1); continue; }
                for (int j = 0; j < 48; j++)
                {
                    float t = j / 47f;
                    Vector3 p;
                    if (Profile.Behavior == Vfx120Behavior.Projectile)
                    {
                        float end = Mathf.Clamp01(Age / Mathf.Max(.01f, _flight));
                        float f = Mathf.Max(0, end - (1 - t) * .3f);
                        p = _aim * f + new Vector3(Mathf.Sin(t * 7 + r * 2) * .09f,
                            IsStoneFlight() ? 4 * f * (1 - f) * Mathf.Max(.4f, Profile.Lift) : Mathf.Sin(f * Mathf.PI) * Profile.Lift * .2f, 0);
                    }
                    else
                    {
                        float a = t * Mathf.PI * 2 * Mathf.Max(.5f, Profile.Turns) + r * 2.1f + Age * .25f;
                        float rad = size * (guard ? .65f : .35f + t * .35f);
                        p = center + new Vector3(Mathf.Cos(a) * rad, guard && !wideGuard ? Mathf.Sin(a) * rad : t * Profile.Lift, guard && !wideGuard ? .08f * r : Mathf.Sin(a) * rad);
                        if (Profile.Behavior == Vfx120Behavior.Wave) p.y = -.7f + Mathf.Sin(t * Mathf.PI) * Profile.Lift;
                        if (Profile.Behavior == Vfx120Behavior.Heal) p.y = t * Profile.Lift + Mathf.Repeat(Age * .3f + r * .3f, .6f);
                    }
                    line.SetPosition(j, p);
                }
                line.widthMultiplier = (.016f + .015f * r) * grow * fade;
                Tint(line, r == 1 ? Profile.Accent : Profile.Pigment, fade * .8f, 1 - fade);
            }
            if (_atmosphere != null) _atmosphere.Sample(Age, center, _flight, Life, PreviewControlled);
        }

        private Vfx120CueMotion.Context CueContext()
        {
            var context = Vfx120CueMotion.Context.Create(Profile.Glyph);
            context.Age = Age; context.Duration = Life; context.ImpactTime = _flight;
            context.Origin = Vector3.zero; context.Target = _aim;
            context.BaseScale = Profile.PartScale; context.GroundY = _targetGround;
            context.HitAt = IsAttachedSeal() ? AdditionalHitAt : HitAt;
            context.TargetDefeatedAt = TargetDefeatedAt;
            context.ReleaseAt = Profile.Glyph == "검" ? FirstIssued(ReleaseAt, BreakAt) : ReleaseAt;
            if (_secondaryTarget != null)
            {
                context.HasSecondary = true;
                context.Secondary = transform.InverseTransformPoint(_secondaryTarget.position + Vector3.up * 1.1f);
            }
            if (_secondaryTargets != null)
            {
                for (int i = 0; i < _secondaryTargets.Length; i++)
                    _secondaryPoints[i] = _secondaryTargets[i] != null
                        ? transform.InverseTransformPoint(_secondaryTargets[i].position + Vector3.up * 1.1f)
                        : new Vector3(float.NaN, float.NaN, float.NaN);
                context.SecondaryPoints = _secondaryPoints;
            }
            if (PreviewControlled && DemonstrationCues)
            {
                // Sampled clocks, not Signal calls: seeking/repeating a capture is deterministic.
                // These illustrative events never enter the public diagnostic event history.
                float later = Mathf.Max(_flight + .3f, Life * .48f);
                if ((Profile.Glyph == "녹" || Profile.Glyph == "안") && context.HitAt < 0)
                    context.HitAt = _flight;
                if (IsAttachedSeal() && context.HitAt < 0) context.HitAt = later;
                if (Profile.Glyph == "간" && context.TargetDefeatedAt < 0) context.TargetDefeatedAt = later;
                if (Profile.Glyph == "검" && context.ReleaseAt < 0) context.ReleaseAt = Life * .77f;
            }
            return context;
        }

        private static float FirstIssued(float a, float b) => a < 0 ? b : b < 0 ? a : Mathf.Min(a, b);
        private bool IsAttachedSeal() => Profile.Glyph == "감" || Profile.Glyph == "남" || Profile.Glyph == "맘"
            || Profile.Glyph == "삼" || Profile.Glyph == "암";

        private void ApplyCuePose(Transform part, Renderer renderer, Vfx120CueMotion.Pose pose, Color color)
        {
            part.localPosition = pose.Position; part.localRotation = pose.Rotation; part.localScale = pose.Scale;
            renderer.enabled = pose.Visible;
            Tint(renderer, color, pose.Alpha, 1 - pose.Alpha);
        }

        private void SampleCueSeal(Vfx120CueMotion.Context context, ref float alpha)
        {
            if (IsAttachedSeal())
            {
                Vector3 axis = _aim.sqrMagnitude > .001f ? _aim.normalized : Vector3.forward;
                float attached = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(_flight, _flight + .2f, Age));
                _seal.localPosition = _aim - axis * .26f;
                _seal.localRotation = Quaternion.LookRotation(-axis, Mathf.Abs(axis.y) > .98f ? Vector3.forward : Vector3.up);
                _seal.localScale = Vector3.one * .62f;
                float burst = FirstIssued(context.HitAt, context.ReleaseAt);
                float fadeAfterBurst = burst >= 0 ? 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(burst, burst + .3f, Age)) : 1;
                alpha *= attached * fadeAfterBurst;
            }
            else if (Profile.Glyph == "녹")
            {
                if (context.HitAt < 0 || Age < context.HitAt) { alpha = 0; return; }
                _seal.localPosition = new Vector3(_aim.x, _targetGround + .035f, _aim.z);
                _seal.localRotation = Quaternion.Euler(90, 0, 0);
                _seal.localScale = Vector3.one * Profile.Size * Mathf.SmoothStep(0, 1, (Age - context.HitAt) / .55f);
                alpha *= .7f;
            }
            else if (Profile.Glyph == "검")
            {
                _seal.localPosition = new Vector3(_aim.x, _targetGround + .025f, _aim.z);
                _seal.localRotation = Quaternion.Euler(90, 0, 0);
                if (context.ReleaseAt >= 0)
                    alpha *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(context.ReleaseAt, context.ReleaseAt + .4f, Age));
            }
            else // 간 / 안: a short source-side cast mark, no floating destination rune.
            {
                _seal.localPosition = new Vector3(0, _originGround + .035f, 0);
                _seal.localRotation = Quaternion.Euler(90, 0, 0);
                alpha *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .48f, Age));
            }
        }

        private Vector3 Center()
        {
            if (HasSpatialPlan)
            {
                Vector3 start = transform.InverseTransformPoint(ReceivedAreaPlan.Point);
                if (ReceivedAreaPlan.Shape == AreaShape.Path)
                    return start + AreaDirection() * Mathf.Clamp((Age - ReceivedAreaPlan.Delay) * Mathf.Max(0, ReceivedAreaPlan.Speed), 0, Mathf.Max(0, ReceivedAreaPlan.Length));
                return start; // Circle center / Cone emission point, never TargetPoint()+chest.
            }
            if (IsStoneFlight())
            {
                float t = Mathf.Clamp01(Age / Mathf.Max(.01f, _flight));
                Vector3 end = _aim;
                if (Profile.Glyph == "막")
                    end.y = _targetGround + (Profile.BodyMesh != null ? Profile.BodyMesh.bounds.extents.y * Profile.PartScale.y : .5f) + .015f;
                return Vector3.Lerp(Vector3.zero, end, t) + Vector3.up * (4 * t * (1 - t) * Mathf.Max(.4f, Profile.Lift));
            }
            var b = Profile.Behavior;
            if (Profile.Glyph == "국") return new Vector3(0, _originGround, 0);
            if (Profile.Glyph == "넉") return new Vector3(0, _originGround + .14f, 0);
            if (Profile.Glyph == "넌") return new Vector3(.48f, _originGround + 1.04f, .40f);
            if (Profile.Glyph == "넘") return new Vector3(0, _originGround + 1.30f, .30f);
            if (b == Vfx120Behavior.Projectile) return Vector3.Lerp(Vector3.zero, _aim, Mathf.Clamp01(Age / Mathf.Max(.01f, _flight)));
            if (b == Vfx120Behavior.Wave)
            {
                float speed = ReceivedAreaPlan != null ? Mathf.Max(.1f, ReceivedAreaPlan.Speed) : 2.2f;
                float len = ReceivedAreaPlan != null ? Mathf.Max(.1f, ReceivedAreaPlan.Length) : 5;
                float z = Mathf.Clamp((Age - _flight) * speed, 0, len);
                return new Vector3(0, Mathf.Lerp(_originGround, _targetGround, z / Mathf.Max(.1f, len)) + .12f, z);
            }
            if (b == Vfx120Behavior.Shield) return new Vector3(0, -.15f, .65f);
            if (b == Vfx120Behavior.Weapon) return new Vector3(.4f, .05f, .55f);
            if (b == Vfx120Behavior.Buff) return new Vector3(0, .1f, .15f);
            if (b == Vfx120Behavior.Heal || b == Vfx120Behavior.Buff || b == Vfx120Behavior.Weapon || b == Vfx120Behavior.Reserve)
                return new Vector3(0, _originGround + .2f, .35f);
            var p = _aim;
            if (Profile.Grounded) p.y = _targetGround + .1f;
            return p;
        }

        private bool HasSpatialPlan => ReceivedAreaPlan != null && (ReceivedAreaPlan.Shape == AreaShape.Circle
            || ReceivedAreaPlan.Shape == AreaShape.Path || ReceivedAreaPlan.Shape == AreaShape.Cone);
        private bool IsStoneFlight() => Profile.Glyph == "마" || Profile.Glyph == "막" || Profile.Glyph == "만"
            || Profile.Glyph == "맛" || Profile.Glyph == "망";

        private Vector3 AreaDirection()
        {
            var direction = transform.InverseTransformDirection(ReceivedAreaPlan.Direction);
            direction.y = 0;
            return direction.sqrMagnitude > .0001f ? direction.normalized : Vector3.forward;
        }

        private void ApplyAreaPose(float u, float pulse, ref Vector3 center, ref Vector3 local,
            ref Vector3 axis, ref Vector3 scale)
        {
            var plan = ReceivedAreaPlan;
            Vector3 direction = AreaDirection();
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            center = Center();
            if (plan.Shape == AreaShape.Circle)
            {
                float angle = u * 37.7f;
                float radius = Mathf.Sqrt(u) * Mathf.Max(0, plan.Radius);
                local = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                axis = Vector3.up;
                scale = Profile.PartScale * pulse;
            }
            else if (plan.Shape == AreaShape.Path)
            {
                local = Profile.Family == "WaveCrest" ? Vector3.zero : side * ((u - .5f) * 2 * Mathf.Max(0, plan.Radius));
                axis = direction;
                scale = Profile.PartScale * pulse;
                if (Profile.Family == "WaveCrest" && Profile.BodyMesh != null)
                    scale.x = 2 * Mathf.Max(0, plan.Radius) / Mathf.Max(.001f, Profile.BodyMesh.bounds.size.x) * pulse;
            }
            else if (plan.Shape == AreaShape.Cone)
            {
                float halfAngle = Mathf.Clamp(plan.Angle, 0, 89);
                float angle = (u - .5f) * 2 * halfAngle;
                Vector3 tongue = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                float ready = Mathf.SmoothStep(0, 1, Age / Mathf.Max(.05f, plan.Delay));
                float jet = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(plan.Delay, plan.Delay + .20f, Age));
                float reach = Mathf.Max(0, plan.Length) * jet;
                float visibleLength = Mathf.Max(.05f, reach);
                local = tongue * (visibleLength * .5f);
                axis = tongue;
                scale = Profile.PartScale * pulse;
                if (Profile.BodyMesh != null)
                    scale.z = visibleLength / Mathf.Max(.001f, Profile.BodyMesh.bounds.size.z);
                scale.x *= Mathf.Lerp(.10f, 1, jet);
                scale.y *= Mathf.Lerp(.10f, 1, jet);
                scale *= ready;
            }
        }

        private Vector3 LayoutPosition(float u, float time, float size)
        {
            float a = u * Mathf.PI * 2;
            float h = Profile.Lift;
            if (IsWideGuard()) return new Vector3(Mathf.Cos(a) * size * .65f, .25f + Mathf.Sin(u * Mathf.PI) * .35f, Mathf.Sin(a) * size * .65f);
            switch (Profile.Layout)
            {
                case Vfx120Layout.Spear: return new Vector3((u - .5f) * size * .35f, Mathf.Sin(u * 9) * .12f, u * .18f);
                case Vfx120Layout.Spiral: a += time * .6f; return new Vector3(Mathf.Cos(a) * size * u, h * u, Mathf.Sin(a) * size * u);
                case Vfx120Layout.Fan: a = Mathf.Lerp(-1.1f, 1.1f, u); return new Vector3(Mathf.Sin(a) * size, Mathf.Sin(u * Mathf.PI) * h * .4f, Mathf.Cos(a) * size * .65f);
                case Vfx120Layout.Rain: return new Vector3(Mathf.Sin(u * 71) * size, Mathf.Repeat(2.5f - time * 1.8f + u * 3, 2.5f), Mathf.Cos(u * 37) * size);
                case Vfx120Layout.Dome: return new Vector3(Mathf.Cos(a) * size * .6f, Mathf.Sin(a) * size * .6f, Mathf.Sin(u * Mathf.PI) * .4f);
                case Vfx120Layout.Field: a = u * 37.7f; return new Vector3(Mathf.Cos(a) * Mathf.Sqrt(u) * size, 0, Mathf.Sin(a) * Mathf.Sqrt(u) * size);
                case Vfx120Layout.Orbit: a += time * .85f; return new Vector3(Mathf.Cos(a) * size * .65f, .5f + Mathf.Sin(a * 2) * h * .2f, Mathf.Sin(a) * size * .65f);
                case Vfx120Layout.Wave: return new Vector3((u - .5f) * size * 2, Mathf.Sin(u * Mathf.PI) * h * .5f, Mathf.Sin(u * 10 + time * 3) * .22f);
                case Vfx120Layout.Cage: return new Vector3(Mathf.Cos(a) * size * .5f, .2f, Mathf.Sin(a) * size * .5f);
                case Vfx120Layout.Petals: return new Vector3(Mathf.Cos(a) * size * .4f, .15f + Mathf.Sin(time * 1.3f + u) * .05f, Mathf.Sin(a) * size * .4f);
                case Vfx120Layout.Pillar: return new Vector3(Mathf.Cos(a) * size * .22f, u * h, Mathf.Sin(a) * size * .22f);
                default: a = u * Mathf.PI * 2 * Profile.Turns + time * .6f; return new Vector3(Mathf.Cos(a) * size * .45f, u * h, Mathf.Sin(a) * size * .45f);
            }
        }

        private Vector3 LayoutAxis(float u, Vector3 p)
        {
            if (IsWideGuard()) return new Vector3(-p.x * .15f, 1, -p.z * .15f);
            if (Profile.Behavior == Vfx120Behavior.Shield && Profile.Family != "Ripple") return Vector3.up;
            switch (Profile.Layout)
            {
                case Vfx120Layout.Rain: return Vector3.down;
                case Vfx120Layout.Field: return new Vector3(Mathf.Sin(u * 31) * .3f, 1, Mathf.Cos(u * 23) * .3f);
                case Vfx120Layout.Cage: return Vector3.up + new Vector3(-p.x, 0, -p.z) * .2f;
                case Vfx120Layout.Petals: return new Vector3(p.x, .25f, p.z);
                case Vfx120Layout.Dome: return new Vector3(-p.y, p.x, .1f);
                case Vfx120Layout.Wave: return new Vector3(0, .5f, 1);
                case Vfx120Layout.Spear: return Vector3.forward;
                default: return new Vector3(-p.z, .2f, p.x);
            }
        }

        private bool IsWideGuard()
        {
            if (string.IsNullOrEmpty(Profile.Glyph)) return false;
            char glyph = Profile.Glyph[0];
            return glyph == '구' || glyph == '누' || glyph == '무' || glyph == '수' || glyph == '우';
        }

        private void Tint(Renderer renderer, Color color, float alpha, float erode)
        {
            _block.Clear(); _block.SetColor(ColorId, color); _block.SetFloat(AlphaId, alpha);
            _block.SetFloat(ErodeId, erode); _block.SetFloat(AgeId, Age);
            renderer.SetPropertyBlock(_block);
        }

        private static class QuadMesh
        {
            private static Mesh _value;
            public static Mesh Value
            {
                get
                {
                    if (_value != null) return _value;
                    _value = new Mesh { name = "Vfx120SharedMotifQuad" };
                    _value.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
                    _value.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                    _value.triangles = new[] { 0, 2, 1, 0, 3, 2 }; _value.RecalculateNormals(); _value.RecalculateBounds();
                    return _value;
                }
            }
        }
    }
}
