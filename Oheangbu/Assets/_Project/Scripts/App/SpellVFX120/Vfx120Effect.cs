using UnityEngine;
using UnityEngine.Rendering;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    // Presentation only. The supplied combat clock is never advanced or re-issued here.
    // Meshes/materials are shared assets; per-instance pigment and erosion use property blocks.
    public sealed partial class Vfx120Effect : SpellSequenceEffect
    {
        public Vfx120Profile Profile;
        public bool PreviewControlled;
        public FixedWardVfx FixedWard {get;private set;}
        public WoodDeerVfx WoodDeer {get;private set;}
        public FireHaetaeVfx FireHaetae {get;private set;}
        public MetalTigerVfx MetalTiger {get;private set;}
        public DokkaebiClubVfx DokkaebiClub {get;private set;}
        private void ClearDokkaebiClub(){if(DokkaebiClub==null)return;DokkaebiClub.Dispose();if(Application.isPlaying)Destroy(DokkaebiClub);else DestroyImmediate(DokkaebiClub);DokkaebiClub=null;}
        public WaterTurtleVfx WaterTurtle {get;private set;}
        private void ClearWaterTurtle(){if(WaterTurtle==null)return;WaterTurtle.Dispose();if(Application.isPlaying)Destroy(WaterTurtle);else DestroyImmediate(WaterTurtle);WaterTurtle=null;}
        public StoneDokkaebiVfx StoneDokkaebi {get;private set;}
        private void ClearStoneDokkaebi(){if(StoneDokkaebi==null)return;StoneDokkaebi.Dispose();if(Application.isPlaying)Destroy(StoneDokkaebi);else DestroyImmediate(StoneDokkaebi);StoneDokkaebi=null;}
        private void ClearMetalTiger(){if(MetalTiger==null)return;MetalTiger.Dispose();if(Application.isPlaying)Destroy(MetalTiger);else DestroyImmediate(MetalTiger);MetalTiger=null;}
        private void ClearFireHaetae(){if(FireHaetae==null)return;FireHaetae.Dispose();if(Application.isPlaying)Destroy(FireHaetae);else DestroyImmediate(FireHaetae);FireHaetae=null;}
        private void ClearWoodDeer(){if(WoodDeer==null)return;WoodDeer.Dispose();if(Application.isPlaying)Destroy(WoodDeer);else DestroyImmediate(WoodDeer);WoodDeer=null;}
        private void ClearFixedWard(){if(FixedWard==null)return;FixedWard.Dispose();if(Application.isPlaying)Destroy(FixedWard);else DestroyImmediate(FixedWard);FixedWard=null;}
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
        private Transform _projectileVisualAnchor;
        private float _projectileVisualLaunchedAt;
        private Vfx120InterceptionMotion.Target[] _interceptions;
        // Call after Begin. Positions and times are local to this effect. A caller's
        // array is copied once and never mutated by the presentation instance.
        public void SetInterceptionTargets(Vfx120InterceptionMotion.Target[] targets)
        {
            if (targets == null) { _interceptions = null; return; }
            int count = Mathf.Min(Vfx120InterceptionMotion.MaxTargets, targets.Length);
            _interceptions = new Vfx120InterceptionMotion.Target[count];
            System.Array.Copy(targets, _interceptions, count);
        }
        public bool ConfirmInterception(int index, Vector3 localPoint, float at)
        {
            if (_interceptions == null || index < 0 || index >= _interceptions.Length
                || !Vfx120InterceptionMotion.Finite(localPoint) || float.IsNaN(at) || float.IsInfinity(at)) return false;
            if (_interceptions[index].HitConfirmed) return false;
            var t = _interceptions[index]; t.Point = localPoint; t.HitAt = at; t.HitConfirmed = true;
            if (!Vfx120InterceptionMotion.Confirmed(t, Life)) return false;
            _interceptions[index] = t; return true;
        }
        // Optional presentation attachment. Caller supplies the actual projectile and
        // its launch time relative to this effect; neither its pose nor speed is written.
        public void SetProjectileVisualAnchor(Transform anchor, float launchedAt)
        {
            _projectileVisualAnchor = anchor;
            _projectileVisualLaunchedAt = float.IsNaN(launchedAt) || float.IsInfinity(launchedAt)
                ? Age : Mathf.Max(0, launchedAt);
        }
        // Read-only, already sampled ground height for presentation capture proxies.
        public float TargetGroundWorldY => transform.TransformPoint(new Vector3(0, _targetGround, 0)).y;
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
        public Vfx120TraditionalMotif NativeCast => _nativeCast;
        public Vfx120TraditionalMotif NativeImpact => _nativeImpact;
        public Vfx120TraditionalMotif NativeField => _nativeField;
        public bool NativeFieldConfigured { get; private set; }
        public bool NativeReplacesProcedural => _nativeReplaceProcedural;
        public float NativeImpactStartedAt { get; private set; } = -1;
        public string NativeDiagnostic { get; private set; } = "UNASSIGNED";
        public Vfx120MeshySummon MeshyModel => _meshySummon;
        public bool MeshyConfigured { get; private set; }
        public string MeshyDiagnostic { get; private set; } = "UNASSIGNED";
        private bool ReplacesProcedural => _nativeReplaceProcedural || MeshyConfigured || NativeBodyConfigured || BotanicalConfigured || BambooGuardConfigured || WoodWardConfigured || RegrowthConfigured || BloomConfigured || WoodSwordConfigured || CompanionSeedsConfigured || IsBambooSpikes(Profile) || IsVineField(Profile) || IsBambooBolt(Profile) || IsSeedTransfer(Profile) || IsSeedPod(Profile) || IsLeafCut(Profile) || IsWetRoot(Profile) || IsRootLift(Profile) || IsStakeField(Profile) || IsWoodLift(Profile) || IsFireBolt(Profile) || IsFireGuard(Profile) || IsFireAura(Profile) || IsFireCompanions(Profile);
        private float _summonStrikeAt = -1;
        public void SetSummonStrikeClock(float seconds) { _summonStrikeAt = seconds; }
        private bool HasGuardianRig => Profile.Glyph == "몸" && Profile.GuardianMeshes != null
            && Profile.GuardianMeshes.Length == Vfx120GuardianMotion.PartCount && Profile.GuardianPivots != null
            && Profile.GuardianPivots.Length == Vfx120GuardianMotion.PartCount;

        private Transform[] _parts;
        private Renderer[] _renderers;
        private Transform[] _accents;
        private Renderer[] _accentRenderers;
        private LineRenderer[] _ribbons;
        private Transform _seal;
        private Renderer _sealRenderer;
        private Vfx120Atmosphere _atmosphere;
        private Vfx120TraditionalMotif _nativeCast, _nativeImpact, _nativeField;
        private Vfx120MeshySummon _meshySummon;
        private bool _nativeReplaceProcedural, _nativeImpactAttempted;
        private Vfx120EnvironmentMotion.Context _environmentGeometry = Vfx120EnvironmentMotion.Context.Create();
        private Vfx120EnvironmentFixture _environmentFixture;
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
        private static readonly int SoftId = Shader.PropertyToID("_Soft");

        public override void SetImpactClock(float duration) { ReceivedImpactClock = duration; }
        public override void SetAreaPlan(AreaImpactPlan plan) { ReceivedAreaPlan = plan; }
        // Coordinates are in this effect's local frame. Geometry is copied once on
        // assignment; cue times and preview flags remain owned by Begin/Signal.
        public void SetEnvironmentGeometry(Vfx120EnvironmentMotion.Context geometry)
        {
            _environmentGeometry = geometry;
            _environmentGeometry.PathPoints = geometry.PathPoints == null ? null : (Vector3[])geometry.PathPoints.Clone();
        }
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
            // This only consumes the already-issued visual hit cue. It cannot issue
            // a hit, retime damage, or extend Life. Repeated hits do not multiply motifs.
            if (cue == Cue.Hit && !PreviewControlled && Profile != null && !IsEmberCharge && !Vfx120InterceptionMotion.IsPrepared(Profile) && !IsBambooGuard(Profile) && !IsBloom(Profile) && !IsWoodSword(Profile) && !IsBambooSpikes(Profile) && !IsSeedPod(Profile) && !IsLeafCut(Profile) && !IsWetRoot(Profile) && !IsRootLift(Profile) && !IsStakeField(Profile))
                TryStartNativeImpact(at);
        }
        public void SetGuardClock(float duration, float brightWindow)
        {
            ReceivedGuardClock = duration;
            ReceivedGuardBrightWindow = brightWindow;
        }

        public override void Begin(Vector3 origin, Transform target, Vector3 fallbackPoint, Color tint)
        {
            if (Begun) ClearGenerated();
            ClearFixedWard();
            ClearWoodDeer();
            ClearFireHaetae();
            ClearMetalTiger();
            ClearDokkaebiClub();
            ClearWaterTurtle();
            ClearStoneDokkaebi();
            ClearAreaRemake();
            Begun = false;
            _projectileVisualAnchor = null; _projectileVisualLaunchedAt = 0;
            _interceptions = null;
            ClearMeshy(); MeshyConfigured = false; MeshyDiagnostic = "UNASSIGNED";
            ClearBotanical();
            ClearBambooGuard();
            ClearWoodWard();
            ClearRegrowth();
            ClearBloom();
            ClearWoodSword();
            ClearCompanionSeeds();
            ClearBambooSpikes();
            ClearVineField();
            ClearBambooBolt();
            ClearSeedTransfer();
            ClearSeedPod();
            ClearLeafCut();
            ClearWetRoot();
            ClearRootLift();
            ClearStakeField();
            ClearWoodLift();
            ClearFireBolt();
            ClearFireGuard();
            ClearFireAura();
            ClearFireCompanions();
            ClearNative();
            NativeBodyConfigured = false;
            NativeBodyDiagnostic = "UNASSIGNED";
            NativeFieldConfigured = _nativeReplaceProcedural = _nativeImpactAttempted = false;
            _externalGuardContact = false;
            NativeImpactStartedAt = -1;
            NativeDiagnostic = "UNASSIGNED";
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
            if (!ReplacesProcedural && PreviewControlled && DemonstrationCues && Vfx120EnvironmentMotion.Supports(Profile))
            {
                bool ground = Profile.Glyph == "웅";
                _environmentFixture = Vfx120EnvironmentFixture.Create(Profile.Glyph, transform,
                    new Vector3(0, _targetGround + (ground ? .035f : 1.0f), 3.1f),
                    ground ? Vector3.up : Vector3.back, true);
            }
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
            if(Profile.WoodDeerPresentation){Life=WoodDeerVfx.ReviewLife;WoodDeer=gameObject.AddComponent<WoodDeerVfx>();WoodDeer.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.FireHaetaePresentation){Life=FireHaetaeVfx.ReviewLife;FireHaetae=gameObject.AddComponent<FireHaetaeVfx>();FireHaetae.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.MetalTigerPresentation){Life=MetalTigerVfx.ReviewLife;MetalTiger=gameObject.AddComponent<MetalTigerVfx>();MetalTiger.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.DokkaebiClubPresentation){Life=DokkaebiClubVfx.ReviewLife;DokkaebiClub=gameObject.AddComponent<DokkaebiClubVfx>();DokkaebiClub.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.WaterTurtlePresentation){Life=WaterTurtleVfx.ReviewLife;WaterTurtle=gameObject.AddComponent<WaterTurtleVfx>();WaterTurtle.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.StoneDokkaebiPresentation){Life=StoneDokkaebiVfx.ReviewLife;StoneDokkaebi=gameObject.AddComponent<StoneDokkaebiVfx>();StoneDokkaebi.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if(Profile.WardKind!=Vfx120WardKind.None){Life=Profile.Duration;FixedWard=gameObject.AddComponent<FixedWardVfx>();FixedWard.Configure(Profile,ReceivedOrigin,transform.forward);return;}
            if (Profile.AreaRemake != Vfx120AreaRemake.None) { BuildAreaRemake(); return; }
            BuildNative();
            BuildBambooGuard();
            BuildWoodWard();
            BuildRegrowth();
            BuildBloom();
            BuildWoodSword();
            BuildCompanionSeeds();
            BuildBambooSpikes();
            BuildVineField();
            BuildBambooBolt();
            BuildSeedTransfer();
            BuildSeedPod();
            BuildLeafCut();
            BuildWetRoot();
            BuildRootLift();
            BuildStakeField();
            BuildWoodLift();
            BuildFireBolt();
            BuildFireGuard();
            BuildFireAura();
            BuildFireCompanions();
            BuildElementWash();
            BuildMeshy();
            BuildBotanical();
            if (ReplacesProcedural)
            {
                // Configure succeeded before suppressing the old generator. No disabled
                // duplicate atmosphere or mesh hierarchy is built behind the native field.
                _parts = new Transform[0]; _renderers = new Renderer[0];
                _accents = new Transform[0]; _accentRenderers = new Renderer[0];
                _ribbons = new LineRenderer[0];
                return;
            }
            int n = Mathf.Clamp(Profile.Count, 1, 32);
            if (HasGuardianRig) n = Vfx120GuardianMotion.PartCount;
            if (Vfx120EnvironmentMotion.Supports(Profile)) n = Vfx120EnvironmentMotion.GetBodyCount(Profile);
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Path && Profile.Family == "WaveCrest") n = 1;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley && ReceivedAreaPlan.Shots.Count > 0)
                n = Mathf.Min(32, ReceivedAreaPlan.Shots.Count);
            _parts = new Transform[n]; _renderers = new Renderer[n];
            int accentCount = Mathf.Max(n, Vfx120CueMotion.GetRequiredAccentCount(Profile.Glyph));
            accentCount = Mathf.Max(accentCount, Vfx120VariantMotion.GetSelfAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120VariantMotion.GetGroundFogAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120SummonMotion.GetAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120WeaponMotion.GetAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120WaterMotion.GetAccentCount(Profile));
            accentCount = Mathf.Max(accentCount, Vfx120EnvironmentMotion.GetAccentCount(Profile));
            bool projectileAccents = !HasSpatialPlan && !(ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley);
            if (projectileAccents) accentCount = Mathf.Max(accentCount, Vfx120VariantMotion.GetProjectileAccentCount(Profile));
            if (HasGuardianRig) accentCount = 0;
            if (Vfx120MetalDetailMotion.SuppressGenericAccents(Profile)) accentCount = 0;
            _accents = new Transform[accentCount]; _accentRenderers = new Renderer[accentCount];
            for (int i = 0; i < n; i++)
            {
                _parts[i] = MeshPart("Body_" + i, HasGuardianRig ? Profile.GuardianMeshes[i] : Profile.BodyMesh, Profile.BodyMaterial, out _renderers[i]);
            }
            for (int i = 0; i < accentCount; i++)
                _accents[i] = MeshPart("Flecks_" + i, projectileAccents && Profile.Glyph == "만" ? Profile.BodyMesh : Profile.AccentMesh,
                    projectileAccents && Profile.Glyph == "만" || Profile.Behavior == Vfx120Behavior.Summon && Profile.Family == "Beast" ? Profile.BodyMaterial : Profile.InkMaterial,
                    out _accentRenderers[i]);
            _seal = MeshPart("TraditionalMotif", QuadMesh.Value, Profile.PatternMaterial, out _sealRenderer);
            // Summons retain their current creature/body. Only the ground seal is replaced.
            // Setting the GO inactive prevents later renderer.enabled writes restoring it.
            if (NativeFieldConfigured) _seal.gameObject.SetActive(false);
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
            if (label.StartsWith("Body_") && (Profile.Glyph == "막" || Profile.Glyph == "뭄")
                && material != null && material.HasProperty("_Visibility"))
            { mr.shadowCastingMode = ShadowCastingMode.On; mr.receiveShadows = true; }
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
            if(FixedWard!=null){FixedWard.Sample(Age);return;}
            if(WoodDeer!=null){WoodDeer.Sample(Age);return;}
            if(FireHaetae!=null){FireHaetae.Sample(Age);return;}
            if(MetalTiger!=null){MetalTiger.Sample(Age);return;}
            if(DokkaebiClub!=null){DokkaebiClub.Sample(Age);return;}
            if(WaterTurtle!=null){WaterTurtle.Sample(Age);return;}
            if(StoneDokkaebi!=null){StoneDokkaebi.Sample(Age);return;}
            if(Profile.AreaRemake != Vfx120AreaRemake.None) { SampleAreaRemake(); return; }
            if(IsEmberCharge&&PreviewControlled&&DemonstrationCues&&Age>=1.8f&&EmberChargeDetonatedAt<0)DetonateEmberCharge(1.8f);
            _aim = transform.InverseTransformPoint(TargetPoint());
            SampleNative();
            SampleBambooGuard();
            SampleWoodWard();
            SampleRegrowth();
            SampleBloom();
            SampleWoodSword();
            SampleCompanionSeeds();
            SampleBambooSpikes();
            SampleVineField();
            SampleBambooBolt();
            SampleSeedTransfer();
            SampleSeedPod();
            SampleLeafCut();
            SampleWetRoot();
            SampleRootLift();
            SampleStakeField();
            SampleWoodLift();
            SampleFireBolt();
            SampleFireGuard();
            SampleFireAura();
            SampleFireCompanions();
            SampleOriginalContacts();
            if(Profile.UseOriginalKtp&&IsBambooBolt(Profile)){if(_boltInk!=null)_boltInk.enabled=false;if(_boltLeaves!=null)foreach(var leaf in _boltLeaves)if(leaf!=null)leaf.enabled=false;}
            SampleMeshy();
            SampleBotanical();
            if (ReplacesProcedural) return;
            float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Life - .7f, Life, Age));
            float grow = Mathf.SmoothStep(0, 1, Age / Mathf.Min(.28f, _flight));
            float arrive = Mathf.SmoothStep(0, 1, (Age - _flight) / .22f);
            Vector3 center = Center();
            var guardianContext = new Vfx120GuardianMotion.Context();
            if (HasGuardianRig)
            {
                guardianContext = new Vfx120GuardianMotion.Context { Age = Age, Life = Life, FormationTime = _flight,
                    Height = Profile.PartScale.y, OriginGround = _originGround, TargetGround = _targetGround,
                    StrikeAt = _summonStrikeAt, HitAt = HitAt, Demonstration = PreviewControlled && DemonstrationCues,
                    HasTarget = ReceivedTarget != null, Target = _aim, Pivots = Profile.GuardianPivots,
                    RightFistContact = Profile.GuardianFistContact };
                if (Vfx120GuardianMotion.TrySampleAnchor(guardianContext, out var anchorPoint, out _)) center = anchorPoint;
            }
            bool authoritativeVolley = ReceivedAreaPlan != null && ReceivedAreaPlan.Shape == AreaShape.Volley
                && ReceivedAreaPlan.Shots.Count > 0;
            bool projectileMotion = !HasSpatialPlan && !authoritativeVolley && Vfx120VariantMotion.GetProjectileAccentCount(Profile) > 0;
            if (projectileMotion && Vfx120VariantMotion.TrySampleProjectileCenter(Profile, Age, _flight, Life, _aim, _targetGround, out var projectileCenter))
                center = projectileCenter;
            var cueContext = CueContext();
            var environmentContext = EnvironmentContext();
            if (_environmentFixture != null) _environmentFixture.Sample(environmentContext);
            if (Vfx120EnvironmentMotion.Supports(Profile) && environmentContext.HasSurface)
                center = environmentContext.SurfacePoint;
            var waterContext = Vfx120WaterMotion.Context.Create();
            waterContext.Age = Age; waterContext.Life = Life; waterContext.Flight = _flight;
            waterContext.Origin = Vector3.zero; waterContext.Target = _aim; waterContext.Center = center;
            waterContext.OriginGround = _originGround; waterContext.TargetGround = _targetGround;
            waterContext.HitAt = HitAt; waterContext.ReleaseAt = ReleaseAt;
            waterContext.PreviewControlled = PreviewControlled; waterContext.DemonstrationCues = DemonstrationCues;
            bool cueMotion = !authoritativeVolley && Vfx120CueMotion.GetRequiredAccentCount(Profile.Glyph) > 0;
            if (cueMotion && Vfx120CueMotion.TrySample(cueContext, Vfx120CueMotion.PartRole.Body, 0, _parts.Length, out var anchor))
                center = anchor.Visible ? anchor.Position : Vector3.Lerp(Vector3.zero, _aim, Mathf.Clamp01(Age / Mathf.Max(.01f, _flight)));
            float size = Profile.Size;
            if (ReceivedAreaPlan != null && ReceivedAreaPlan.Radius > 0) size = ReceivedAreaPlan.Radius;
            for (int i = 0; i < _parts.Length; i++)
            {
                if (HasGuardianRig)
                {
                    if (Vfx120GuardianMotion.TrySampleBody(guardianContext, i, out var guardianPose))
                        ApplyCuePose(_parts[i], _renderers[i], guardianPose,
                            Color.Lerp(Profile.Pigment, Profile.Accent, i == 2 ? .32f : i % 3 == 0 ? .14f : .03f));
                    else _renderers[i].enabled = false;
                    continue;
                }
                if (Vfx120EnvironmentMotion.TrySampleBody(Profile, environmentContext, i, _parts.Length, out var environmentBody))
                {
                    ApplyCuePose(_parts[i], _renderers[i], environmentBody, Profile.Pigment);
                    continue;
                }
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
                if (!Vfx120MetalDetailMotion.TryApply(Profile, Age, _flight, Life, i, _parts.Length, _aim,
                    ref partCenter, ref local, ref axis, ref partScale, size, _originGround, _targetGround,
                    Profile.Glyph == "선" && _projectileVisualAnchor != null,
                    _projectileVisualAnchor != null ? transform.InverseTransformPoint(_projectileVisualAnchor.position) : Vector3.zero,
                    _projectileVisualAnchor != null ? transform.InverseTransformDirection(_projectileVisualAnchor.forward) : Vector3.forward,
                    _projectileVisualLaunchedAt))
                    Vfx120VariantMotion.Apply(Profile, Age, _flight, Life, i, _parts.Length, _aim, ref partCenter, ref local, ref axis, ref partScale, size, _originGround, _targetGround);
                Vfx120WaterMotion.Apply(Profile, waterContext, i, _parts.Length, ref partCenter, ref local, ref axis, ref partScale);
                if (HasSpatialPlan)
                    ApplyAreaPose(u, pulse, ref partCenter, ref local, ref axis, ref partScale);
                else if (Profile.Glyph == "막")
                {
                    // One airborne stone becomes planted cover at the selected landing point.
                    // A Shield classification must not pull this attack back to the caster.
                    partCenter = center; local = Vector3.zero; axis = Vector3.forward;
                    partScale = i == 0 ? Profile.PartScale * pulse : Vector3.zero;
                    if (Profile.BodyMaterial != null && Profile.BodyMaterial.HasProperty("_Visibility"))
                        partScale = i == 0 ? Profile.PartScale * grow : Vector3.zero;
                }
                else if (Profile.Glyph == "뭄")
                {
                    float assembly=Mathf.SmoothStep(0,1,Mathf.InverseLerp(i*.10f,i*.10f+.25f,Age));
                    partCenter=new Vector3(0,_originGround+.12f,.65f+i*.66f);
                    local=Vector3.zero;axis=Vector3.forward;
                    partScale=Profile.PartScale*assembly*fade;
                    if (Profile.BodyMaterial != null && Profile.BodyMaterial.HasProperty("_Formation"))
                    {
                        // One UV-preserving bridge is revealed from the near bank.
                        // Never multiply the complete bridge into a row of eight copies.
                        partScale=Profile.PartScale;
                        var b=Profile.BodyMesh.bounds;
                        partCenter=new Vector3(0,_originGround-b.min.y*partScale.y+.015f,
                            .45f-b.min.z*partScale.z);
                    }
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
                bool nearBankBridge = Profile.Glyph == "뭄" && Profile.BodyMaterial != null
                    && Profile.BodyMaterial.HasProperty("_Formation");
                if (!nearBankBridge && Profile.Grounded && (HasSpatialPlan || !Vfx120WaterMotion.Supports(Profile)) && (Profile.Family == "Tree" || Profile.Family == "WaveCrest" || Profile.Behavior == Vfx120Behavior.Summon || Profile.Layout == Vfx120Layout.Field || Profile.Glyph == "막"))
                {
                    var e = Profile.BodyMesh.bounds.extents;
                    Quaternion q = _parts[i].localRotation;
                    float halfY = Mathf.Abs((q * Vector3.right).y) * e.x * partScale.x + Mathf.Abs((q * Vector3.up).y) * e.y * partScale.y + Mathf.Abs((q * Vector3.forward).y) * e.z * partScale.z;
                    var position = _parts[i].localPosition;
                    float seating = Profile.Glyph == "막" && Profile.BodyMaterial != null
                        && Profile.BodyMaterial.HasProperty("_Visibility") ? -.16f : .015f;
                    position.y = Mathf.Max(position.y, _targetGround + halfY + seating);
                    _parts[i].localPosition = position;
                }
                if(Vfx120VariantMotion.TrySampleGroundFogBodyTint(Profile,Age,Life,i,out var fogTint,out var fogOpacity))
                    Tint(_renderers[i],fogTint,pulse*fogOpacity,1-fade,true);
                else Tint(_renderers[i], Color.Lerp(Profile.Pigment, Profile.Accent, (i % 4) * .14f), pulse, 1 - fade);
            }
            // Accent geometry has its own authored count; e.g. one tree has twelve leaves.
            for (int i = 0; i < _accents.Length; i++)
            {
                if(Vfx120VariantMotion.TrySampleGroundFogAccent(Profile,Age,_flight,Life,_aim,size,_targetGround,i,_accents.Length,out var fogAccent))
                {
                    ApplyCuePose(_accents[i],_accentRenderers[i],fogAccent,Color.Lerp(Profile.Ink,Profile.Pigment,i%3==0?.40f:.16f));
                    continue;
                }
                if (projectileMotion && Vfx120VariantMotion.TrySampleProjectileAccent(Profile, Age, _flight, Life,
                    _aim, _targetGround, i, _accents.Length, out var projectileAccent))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], projectileAccent, Color.Lerp(Profile.Pigment, Profile.Accent, i % 3 == 0 ? .45f : .1f));
                    continue;
                }
                if (Vfx120EnvironmentMotion.TrySampleAccent(Profile, environmentContext, i, _accents.Length, out var environmentAccent))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], environmentAccent,
                        Vfx120EnvironmentMotion.GetAccentColor(Profile, i));
                    continue;
                }
                if (Vfx120WaterMotion.TrySampleAccent(Profile, waterContext, i, _accents.Length, out var waterPose))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], waterPose, Vfx120WaterMotion.GetAccentColor(Profile, i));
                    continue;
                }
                if (Vfx120WeaponMotion.TrySample(Profile, Age, Life, _parts[0].localPosition,
                    _parts[0].localRotation, _parts[0].localScale, i, out var weaponPose))
                {
                    ApplyCuePose(_accents[i], _accentRenderers[i], weaponPose, Vfx120WeaponMotion.GetAccentColor(Profile, i));
                    continue;
                }
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
            if (HasGuardianRig) ground = center.y;
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
            if (Profile.Glyph == "엄") sealAlpha = 0;
            if (castOnly) sealAlpha *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .5f, Age));
            if (ReceivedGuardClock > 0 && Age > ReceivedGuardBrightWindow) sealAlpha *= .45f;
            if (cueMotion) SampleCueSeal(cueContext, ref sealAlpha);
            if (Profile.Glyph == "산" || Profile.Glyph == "상" || Vfx120InterceptionMotion.IsPrepared(Profile)) sealAlpha = 0;
            if (Vfx120EnvironmentMotion.Supports(Profile)) sealAlpha = 0;
            Tint(_sealRenderer, Profile.Accent, sealAlpha, 1 - fade);
            for (int r = 0; r < _ribbons.Length; r++)
            {
                var line = _ribbons[r];
                // The cue meshes already provide the attached/transfer trajectory.
                // Generic orbit ribbons would falsely imply an active zone before a hit.
                line.enabled = !HasGuardianRig && !cueMotion && !projectileMotion && !HasSpatialPlan && Profile.Glyph != "막"
                    && !Vfx120EnvironmentMotion.Supports(Profile)
                    && Vfx120WaterMotion.GetAccentCount(Profile) == 0 && Profile.Behavior != Vfx120Behavior.Weapon;
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
            if (_atmosphere != null)
            {
                var atmosphereCenter=center;
                // Cloud emitters add .7 m internally. Mud fog stays at ankle level.
                if(Profile.Glyph=="몽")atmosphereCenter.y=_targetGround-.60f;
                _atmosphere.Sample(Age, atmosphereCenter, _flight, Life, PreviewControlled);
            }
        }

        private Vfx120EnvironmentMotion.Context EnvironmentContext()
        {
            var context = _environmentGeometry;
            context.Age = Age; context.Life = Life; context.Origin = Vector3.zero;
            context.HitAt = HitAt; context.ReleaseAt = ReleaseAt;
            context.PreviewControlled = PreviewControlled; context.DemonstrationCues = DemonstrationCues;
            if (_environmentFixture != null) _environmentFixture.FillContext(ref context);
            return context;
        }

        private void OnDestroy()
        {
            ClearWoodDeer();
            ClearFireHaetae();
            ClearMetalTiger();
            ClearDokkaebiClub();
            ClearWaterTurtle();
            ClearStoneDokkaebi();
            ClearFixedWard();
            ClearAreaRemake();
            ClearBotanical();
            ClearBambooGuard();
            ClearWoodWard();
            ClearRegrowth();
            ClearBloom();
            ClearWoodSword();
            ClearCompanionSeeds();
            ClearBambooSpikes();
            ClearVineField();
            ClearBambooBolt();
            ClearSeedTransfer();
            ClearSeedPod();
            ClearLeafCut();
            ClearWetRoot();
            ClearRootLift();
            ClearStakeField();
            ClearWoodLift();
            ClearFireBolt();
            ClearFireGuard();
            ClearFireAura();
            ClearFireCompanions();
            ClearMeshy();
            ClearNative();
            if (_environmentFixture != null) { _environmentFixture.Dispose(); _environmentFixture = null; }
        }

        private void BuildMeshy()
        {
            if (Profile.SummonPrefab == null) return;
            if (float.IsNaN(Profile.SummonScale) || float.IsInfinity(Profile.SummonScale) || Profile.SummonScale <= 0
                || float.IsNaN(Profile.SummonYaw) || float.IsInfinity(Profile.SummonYaw))
            { MeshyDiagnostic = "SCALE_OR_YAW_INVALID_PROCEDURAL_FALLBACK"; return; }
            Vector3 point = new Vector3(_aim.x, _targetGround + .01f, _aim.z);
            if (_nativeField != null)
            {
                // Match the actual circle's horizontal center. Its raised transparent
                // floor decal is +.06; the model's authored ground pivot is +.01.
                point = _nativeField.transform.localPosition;
                point.y = (Profile.NativeFieldRole == Vfx120TraditionalMotif.Role.Shield ? _originGround : _targetGround) + .01f;
            }
            else if (HasSpatialPlan)
            {
                point = transform.InverseTransformPoint(ReceivedAreaPlan.Point);
                point.y = _targetGround + .01f;
            }
            var host = new GameObject("MeshySummon");
            host.transform.SetParent(transform, false);
            host.transform.localPosition = point;
            host.transform.localRotation = Quaternion.Euler(0, Profile.SummonYaw, 0);
            host.transform.localScale = Vector3.one * Profile.SummonScale;
            var summon = host.AddComponent<Vfx120MeshySummon>();
            if (!summon.Configure(Profile.SummonPrefab, Life, PreviewControlled))
            {
                MeshyDiagnostic = summon.Diagnostic + " (procedural fallback preserved)";
                DisposeNativeHost(host);
                return;
            }
            _meshySummon = summon; MeshyConfigured = true;
            MeshyDiagnostic = summon.Diagnostic;
        }

        private void SampleMeshy()
        {
            if (_meshySummon == null) return;
            if (PreviewControlled) _meshySummon.Sample(Age);
            else if (Age >= Life) ClearMeshy();
        }

        private void ClearMeshy()
        {
            if (_meshySummon == null) return;
            var host = _meshySummon.gameObject;
            _meshySummon.Clear(); _meshySummon = null;
            DisposeNativeHost(host);
        }

        private void ClearGenerated()
        {
            // Re-Begin owns only this effect's previous generated children. Replacing a
            // procedural model with a Meshy source must not leave the previous body alive.
            if (_parts != null) foreach (var part in _parts) if (part != null) DisposeNativeHost(part.gameObject);
            if (_accents != null) foreach (var part in _accents) if (part != null) DisposeNativeHost(part.gameObject);
            if (_ribbons != null) foreach (var ribbon in _ribbons) if (ribbon != null) DisposeNativeHost(ribbon.gameObject);
            if (_seal != null) DisposeNativeHost(_seal.gameObject);
            if (_atmosphere != null) DisposeNativeHost(_atmosphere.gameObject);
            if (_environmentFixture != null) _environmentFixture.Dispose();
            _parts = null; _renderers = null; _accents = null; _accentRenderers = null;
            _ribbons = null; _seal = null; _sealRenderer = null; _atmosphere = null; _environmentFixture = null;
        }

        private void BuildNative()
        {
            if (Profile.NativeCastPrefab != null)
                _nativeCast = CreateNative(Profile.NativeCastPrefab, Vfx120TraditionalMotif.Role.Cast,
                    new Vector3(0, 0, .65f), Mathf.Min(1.2f, Life));
            if (Profile.NativeFieldPrefab != null)
            {
                var role = Profile.NativeFieldRole;
                if (role != Vfx120TraditionalMotif.Role.Summon && role != Vfx120TraditionalMotif.Role.Shield)
                    NativeDiagnostic = "FIELD_ROLE_UNSUPPORTED_FALLBACK";
                else
                {
                    Vector3 point = role == Vfx120TraditionalMotif.Role.Shield
                        ? new Vector3(0, _originGround + .06f, 0)
                        : new Vector3(_aim.x, _targetGround + .06f, _aim.z);
                    if(Profile.KtpPatternShield && role==Vfx120TraditionalMotif.Role.Shield)point=new Vector3(0,0,.7f);
                    if (role == Vfx120TraditionalMotif.Role.Summon && HasSpatialPlan)
                    {
                        point = transform.InverseTransformPoint(ReceivedAreaPlan.Point);
                        point.y = _targetGround + .06f;
                    }
                    _nativeField = CreateNative(Profile.NativeFieldPrefab, role, point, Life);
                    NativeFieldConfigured = _nativeField != null;
                    _nativeReplaceProcedural = NativeFieldConfigured && Profile.NativeReplaceBody;
                }
            }
        }

        private Vfx120TraditionalMotif CreateNative(GameObject source, Vfx120TraditionalMotif.Role role,
            Vector3 position, float lifetime)
        {
            if (source == null || lifetime <= 0 || float.IsNaN(Profile.NativeScale)
                || float.IsInfinity(Profile.NativeScale) || Profile.NativeScale <= 0
                || (role == Vfx120TraditionalMotif.Role.Impact && (float.IsNaN(Profile.NativeImpactScale)
                    || float.IsInfinity(Profile.NativeImpactScale) || Profile.NativeImpactScale <= 0)))
            { NativeDiagnostic = "NATIVE_SETTINGS_INVALID_FALLBACK"; return null; }
            var host = new GameObject("Native_" + role);
            host.transform.SetParent(transform, false);
            host.transform.localPosition = position;
            host.transform.localScale = Vector3.one * Profile.NativeScale
                * (role == Vfx120TraditionalMotif.Role.Impact ? Profile.NativeImpactScale : 1f);
            if (Profile.KtpEmphasis)
            {
                float multiplier = role == Vfx120TraditionalMotif.Role.Cast ? Profile.KtpCastMultiplier
                    : role == Vfx120TraditionalMotif.Role.Impact ? Profile.KtpImpactMultiplier : Profile.KtpFieldMultiplier;
                host.transform.localScale *= float.IsFinite(multiplier) && multiplier > 0 ? multiplier : 1f;
                host.transform.localPosition += role == Vfx120TraditionalMotif.Role.Cast ? Profile.KtpCastOffset
                    : role == Vfx120TraditionalMotif.Role.Impact ? Profile.KtpImpactOffset : Profile.KtpFieldOffset;
            }
            var motif = host.AddComponent<Vfx120TraditionalMotif>();
            var options = Vfx120TraditionalMotif.Settings.DefaultFor(role);
            options.PreserveAuthored=Profile.UseOriginalKtp;
            options.Brightness = Profile.KtpEmphasis ? Profile.KtpBrightness : 1f;
            options.PatternFocus = Profile.KtpEmphasis;
            options.FiniteWindow = Profile.KtpPatternShield && role==Vfx120TraditionalMotif.Role.Shield;
            options.Sustain=Profile.UseOriginalKtp&&(role==Vfx120TraditionalMotif.Role.Shield||role==Vfx120TraditionalMotif.Role.Summon);
            options.Lifetime = lifetime;
            options.FadeSeconds = Mathf.Min(options.FadeSeconds, lifetime);
            options.PreviewControlled = PreviewControlled;
            // Local/Shape-scaled vendor PS otherwise ignore the companion seal's
            // authored carrier scale. Opt in only for the new spatial body profiles.
            options.HierarchyScaling = Profile.NativeBodyPrefab != null || Profile.BotanicalPrefab != null
                || Profile.Glyph == "오" || Profile.Glyph == "막" || Profile.Glyph == "뭄"
                || Vfx120FrostCordMotion.IsPrepared(Profile) || Vfx120InterceptionMotion.IsPrepared(Profile) || IsBambooGuard(Profile) || IsWoodWard(Profile) || IsRegrowth(Profile) || IsBloom(Profile) || IsWoodSword(Profile) || IsCompanionSeeds(Profile) || IsBambooSpikes(Profile) || IsVineField(Profile) || IsBambooBolt(Profile) || IsSeedTransfer(Profile) || IsSeedPod(Profile) || IsLeafCut(Profile) || IsWetRoot(Profile) || IsRootLift(Profile) || IsStakeField(Profile) || IsWoodLift(Profile) || IsFireBolt(Profile) || IsFireGuard(Profile) || IsFireAura(Profile) || IsFireCompanions(Profile);
            // Curated source already owns its offset/rotation correction. Do not
            // normalize ContentTransform or modify the authored child hierarchy here.
            if (!motif.Configure(source, Profile.Pigment, Profile.Ink, role, options))
            {
                NativeDiagnostic = role + ": " + motif.Diagnostic + " (procedural fallback preserved)";
                DisposeNativeHost(host);
                return null;
            }
            if (NativeDiagnostic == "UNASSIGNED") NativeDiagnostic = "CONFIGURED_RUNTIME_VISUAL_UNVERIFIED";
            return motif;
        }

        private bool _externalGuardContact;
        private float NativeImpactClock()
        {
            if (_externalGuardContact && (IsBambooGuard(Profile) || IsFireGuard(Profile))) return -1;
            if(IsFireGuard(Profile))return FireGuardClock;
            if(IsEmberCharge)return EmberChargeDetonatedAt;
            if(IsSeedPod(Profile))return SeedPodDetonatedAt;
            if(IsLeafCut(Profile))return LeafCutAt;
            if(IsWetRoot(Profile))return WetRootAt;
            if(IsRootLift(Profile))return RootLiftConfigured?RootLiftDelay:-1;
            if(IsStakeField(Profile))return StakeFieldConfigured?StakeFieldDelay:-1;
            if (IsBambooSpikes(Profile)) return BambooSpikesConfigured ? BambooSpikesDelay : -1;
            if (IsWoodSword(Profile)) return WoodSwordContactClock;
            if (IsBloom(Profile)) return BloomClock;
            if (IsBambooGuard(Profile)) return BambooParryClock;
            if (Vfx120InterceptionMotion.IsPrepared(Profile))
            {
                int first = Vfx120InterceptionMotion.FirstConfirmed(_interceptions, Life);
                return first >= 0 ? _interceptions[first].HitAt : -1;
            }
            // Profile.Flight alone is an illustrative duration, not an issued impact.
            // Existing attack adapters explicitly provide ReceivedImpactClock.
            if (HitAt >= 0) return HitAt;
            if (ReceivedImpactClock > 0) return _flight;
            return PreviewControlled && DemonstrationCues && (!Profile.UseOriginalKtp || Profile.Behavior==Vfx120Behavior.Projectile || Profile.Behavior==Vfx120Behavior.Bind || Profile.Behavior==Vfx120Behavior.Burst || Profile.Behavior==Vfx120Behavior.Wave || IsFireBolt(Profile) || IsFireJet(Profile)) ? _flight : -1;
        }

        private Vector3 NativeImpactPoint()
        {
            if(IsSeedPod(Profile))return transform.InverseTransformPoint(PodPoint());
            if(IsLeafCut(Profile))return transform.InverseTransformPoint(_cutPoint);
            if(IsWetRoot(Profile))return transform.InverseTransformPoint(_wetAt>=0?WetPoint(0,0):TargetPoint());
            if (IsWoodSword(Profile)) return transform.InverseTransformPoint(_swordContactPoint);
            if (IsBloom(Profile)) return new Vector3(0,.02f,.18f);
            if (IsBambooGuard(Profile)) return _guardContact;
            if (Vfx120InterceptionMotion.IsPrepared(Profile))
            {
                int first = Vfx120InterceptionMotion.FirstConfirmed(_interceptions, Life);
                if (first >= 0) return _interceptions[first].Point;
            }
            // Circle/Path plans own a ground impact/start point; Cone's point is
            // the caster, so its existing resolved target/fallback remains the impact.
            if (HasSpatialPlan && ReceivedAreaPlan.Shape != AreaShape.Cone)
                return transform.InverseTransformPoint(ReceivedAreaPlan.Point);
            return transform.InverseTransformPoint(TargetPoint());
        }

        private void TryStartNativeImpact(float at)
        {
            if (Profile.UseOriginalKtp && !PreviewControlled) return;
            if (_nativeImpactAttempted || Profile.NativeImpactPrefab == null || at < 0 || at >= Life) return;
            _nativeImpactAttempted = true;
            _nativeImpact = CreateNative(Profile.NativeImpactPrefab, Vfx120TraditionalMotif.Role.Impact,
                NativeImpactPoint(), Mathf.Min(1.6f, Life - at));
            if (_nativeImpact != null) NativeImpactStartedAt = at;
        }

        private readonly System.Collections.Generic.List<Vfx120TraditionalMotif> _originalContacts=new System.Collections.Generic.List<Vfx120TraditionalMotif>();
        private readonly System.Collections.Generic.List<float> _originalContactTimes=new System.Collections.Generic.List<float>();
        public bool ConfirmOriginalImpact(Vector3 worldPoint)
        {
            if(Profile==null||!Profile.UseOriginalKtp||Age>=Life||!Vfx120InterceptionMotion.Finite(worldPoint))return false;
            var fx=CreateNative(Profile.NativeImpactPrefab,Vfx120TraditionalMotif.Role.Impact,transform.InverseTransformPoint(worldPoint),1);
            if(fx==null)return false;_originalContacts.Add(fx);_originalContactTimes.Add(Age);return true;
        }
        private void SampleOriginalContacts()
        {
            for(int i=_originalContacts.Count-1;i>=0;i--){var fx=_originalContacts[i];if(fx==null){_originalContacts.RemoveAt(i);_originalContactTimes.RemoveAt(i);continue;}if(PreviewControlled)fx.Sample(Age-_originalContactTimes[i]);}
        }
        private void SampleNative()
        {
            SampleElementWash();
            if (PreviewControlled)
            {
                if (_nativeCast != null) _nativeCast.Sample(Age);
                if (_nativeField != null) _nativeField.Sample(Age);
            }
            float impactAt = NativeImpactClock();
            if (!(Profile.UseOriginalKtp&&(IsFireAura(Profile)||IsFireCompanions(Profile)||IsCompanionSeeds(Profile))) && Profile.NativeImpactPrefab != null && impactAt >= 0 && Age >= impactAt && Age < Life)
                TryStartNativeImpact(impactAt);
            if (PreviewControlled && _nativeImpact != null)
            {
                // A preview can seek backwards without manufacturing or repeating Signal.
                float impactAge = NativeImpactStartedAt >= 0 ? Age - NativeImpactStartedAt : -1;
                _nativeImpact.Sample(impactAge);
            }
            // The existing root Life remains the hard boundary even if a source's
            // authored particles/Animator would otherwise run longer.
            if (Age >= Life)
            {
                if(Profile.UseOriginalKtp&&PreviewControlled){if(_nativeField!=null)_nativeField.Release();return;}
                if (PreviewControlled)
                {
                    if (_nativeCast != null) _nativeCast.Sample(_nativeCast.EndsAt);
                    if (_nativeField != null) _nativeField.Sample(_nativeField.EndsAt);
                    if (_nativeImpact != null) _nativeImpact.Sample(_nativeImpact.EndsAt);
                }
                else if(Profile.UseOriginalKtp)ReleaseOriginalTails();
                else ClearNative();
            }
        }

        private void ReleaseOriginalTails()
        {
            foreach(var contact in _originalContacts){var fx=contact;ReleaseOriginalTail(ref fx,false);}_originalContacts.Clear();_originalContactTimes.Clear();
            ReleaseOriginalTail(ref _nativeCast,false);ReleaseOriginalTail(ref _nativeImpact,false);ReleaseOriginalTail(ref _nativeField,true);
        }
        private static void ReleaseOriginalTail(ref Vfx120TraditionalMotif motif,bool release)
        {
            if(motif==null)return;if(release)motif.Release();
            if(motif.IsComplete){ClearNative(ref motif);return;}
            motif.transform.SetParent(null,true);motif.DestroyHostOnCompletion=true;motif=null;
        }
        private void ClearNative()
        {
            foreach(var contact in _originalContacts){var fx=contact;ClearNative(ref fx);}_originalContacts.Clear();_originalContactTimes.Clear();
            ClearNative(ref _nativeCast); ClearNative(ref _nativeImpact); ClearNative(ref _nativeField);
            ClearNative(ref _nativeBody);
        }

        private static void ClearNative(ref Vfx120TraditionalMotif motif)
        {
            if (motif == null) return;
            var host = motif.gameObject;
            motif.Clear(); motif = null;
            DisposeNativeHost(host);
        }

        private static void DisposeNativeHost(GameObject host)
        {
            if (host == null) return;
            host.SetActive(false);
            if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
        }

        private Vfx120CueMotion.Context CueContext()
        {
            var context = Vfx120CueMotion.Context.Create(Profile.Glyph);
            context.Age = Age; context.Duration = Life; context.ImpactTime = _flight;
            context.Origin = Vector3.zero; context.Target = _aim;
            context.BaseScale = Profile.PartScale; context.GroundY = _targetGround;
            context.FrostCords = Vfx120FrostCordMotion.IsPrepared(Profile);
            context.InterceptionFan = Vfx120InterceptionMotion.IsPrepared(Profile);
            context.Interceptions = _interceptions;
            context.AccentBoundsSize = Profile.AccentMesh != null ? Profile.AccentMesh.bounds.size : context.AccentBoundsSize;
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
                if ((Profile.Glyph == "녹" || Profile.Glyph == "안" || Profile.Glyph == "상") && context.HitAt < 0)
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
                {
                    float seating = Profile.BodyMaterial != null && Profile.BodyMaterial.HasProperty("_Visibility") ? -.16f : .015f;
                    end.y = _targetGround + (Profile.BodyMesh != null ? Profile.BodyMesh.bounds.extents.y * Profile.PartScale.y : .5f) + seating;
                }
                return Vector3.Lerp(Vector3.zero, end, t) + Vector3.up * (4 * t * (1 - t) * Mathf.Max(.4f, Profile.Lift));
            }
            var b = Profile.Behavior;
            if (Profile.Glyph == "국") return new Vector3(0, _originGround, 0);
            if (Profile.Glyph == "넉") return new Vector3(0, _originGround + .14f, 0);
            if (Profile.Glyph == "넌") return new Vector3(.48f, _originGround + 1.04f, .40f);
            if (Profile.Glyph == "넘") return new Vector3(0, _originGround + 1.30f, .30f);
            if (Profile.Glyph == "엄") return new Vector3(-.28f, _originGround + .92f, .22f);
            if (Profile.Glyph == "우") return new Vector3(0, _originGround, 0);
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
                if (Profile.Family == "Sand")
                {
                    // A rising dry wash: tilting the broad XZ grain face avoids an invisible floor stripe.
                    axis = (direction + Vector3.up * (.9f + .3f * Mathf.Sin(u * 17 + Age * 3))).normalized;
                    local.y = .32f + .10f * Mathf.Sin(u * 19 + Age * 4);
                }
            }
            else if (plan.Shape == AreaShape.Cone)
            {
                float halfAngle = Mathf.Clamp(plan.Angle, 0, 89);
                int index = Mathf.Min(_parts.Length - 1, Mathf.FloorToInt(u * _parts.Length));
                int rays = Mathf.Max(1, (_parts.Length + 1) / 2);
                int row = index / rays;
                float spread = rays > 1 ? (index % rays) / (float)(rays - 1) : .5f;
                float angle = (spread - .5f) * 2 * halfAngle;
                Vector3 tongue = Quaternion.AngleAxis(angle, Vector3.up) * direction;
                float ready = Mathf.SmoothStep(0, 1, Age / Mathf.Max(.05f, plan.Delay));
                // The cone's damage occurs at Delay. Its visible reach must already
                // arrive then, rather than beginning to unfold after damage.
                float jet = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, plan.Delay - .22f), Mathf.Max(.01f, plan.Delay), Age));
                float reach = Mathf.Max(0, plan.Length) * jet;
                float visibleLength = Mathf.Max(.05f, reach * .48f);
                local = tongue * reach * (row == 0 ? .25f : .75f);
                local.y = .22f + .12f * Mathf.Sin(Age * 7 + index * 1.7f);
                axis = (tongue + Vector3.up * (.10f + .09f * Mathf.Sin(Age * 6 + index))).normalized;
                scale = Profile.PartScale;
                if (Profile.BodyMesh != null)
                    scale.z = visibleLength / Mathf.Max(.001f, Profile.BodyMesh.bounds.size.z);
                scale.x *= Mathf.Lerp(.10f, .62f, jet);
                if (Profile.BodyMesh != null)
                    scale.y = (.32f + .15f * Mathf.Sin(index * 2 + Age * 5)) / Mathf.Max(.001f, Profile.BodyMesh.bounds.size.y);
                scale.y *= Mathf.Lerp(.10f, 1, jet);
                scale *= ready * pulse;
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

        private void Tint(Renderer renderer, Color color, float alpha, float erode, bool softFog = false)
        {
            _block.Clear(); _block.SetColor(ColorId, color); _block.SetFloat(AlphaId, alpha);
            _block.SetFloat(ErodeId, erode); _block.SetFloat(AgeId, Age);
            // Source-textured stone shares the cutout surface with botanical assets.
            // Its rigid geometry fades by visibility instead of stretching at expiry.
            _block.SetFloat("_Visibility", alpha);
            if (Profile.Glyph == "뭄")
                _block.SetFloat("_Formation", Mathf.SmoothStep(0, 1, Age / .95f));
            // Only muddy fog body layers feather their UV edges. Shared Cloud
            // materials, native particle instances and other glyphs stay unchanged.
            if(softFog)_block.SetFloat(SoftId,.75f);
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
