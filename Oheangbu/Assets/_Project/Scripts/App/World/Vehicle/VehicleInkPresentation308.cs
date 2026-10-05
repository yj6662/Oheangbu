using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>
    /// #308 D308-8 (SPEC-VEHICLE-UX-308 §5) [TEST]: 마석 자동차의 먹 소환(먹이 모여 차가 선다)·먹 회수(차가 먹으로 흩어진다) 연출.
    /// 표현 전용: 차의 물리·좌석·저장은 건드리지 않고, 차 MeshRenderer의 enabled만 잠시 끄고 되돌린다(원래 값 그대로).
    ///   · 먹 껍질 = 차 메시(LOD0)를 Oheangbu/VehicleInkShell308 재질로 Graphics.RenderMesh 한 번 더 그린다(새 GameObject·Renderer 없음,
    ///     투명 큐, 깊이·그림자 패스 없음, 발광 없음, 최대 채널 .85 LDR). 덮임(_Coverage)이 높이 순서(아래→위) 또는 번짐 노이즈로 오간다.
    ///   · 먹 가루 = 이 컴포넌트 자식 ParticleSystem 하나(M_InkDust, Oheangbu/InkSpray)를 Emit으로 직접 쏜다(방출 모듈 끔).
    /// 소환: Gather(먹이 아래에서 차 모양으로 차오름, 차 숨김) → Hold(가득 덮인 채 차를 켬) → Recede(먹이 물러나며 차가 드러남, 역번짐).
    /// 회수: Cover(먹이 번져 차를 덮음) → Scatter(차를 끄고 먹 실루엣이 위에서부터 흩어짐) → 콜백(호출자가 차를 비활성).
    /// D308-8c: 호출 획의 먹이 날아오는 동안 기다리는 Await(lead 초)를 앞에 둘 수 있다 — 소환은 차를 숨긴 채, 회수는 차를 보인 채 기다린다.
    /// 시간은 Time.deltaTime(일시정지 중 멈춤). 정적 가변 필드 없음(Shader.PropertyToID 상수만).
    /// </summary>
    [DefaultExecutionOrder(200), DisallowMultipleComponent]
    public sealed class VehicleInkPresentation308 : MonoBehaviour
    {
        public enum Phase { Idle, Gather, Hold, Recede, Cover, Scatter, Await }

        public VehicleUx308ProfileSO Profile;
        public Phase Current { get; private set; }
        public bool Busy => Current != Phase.Idle;
        public bool CarHidden { get; private set; }
        public float Coverage { get; private set; }
        public float HeightOrder { get; private set; }
        public int Materializations { get; private set; }
        public int Dissolves { get; private set; }
        /// <summary>Await phases entered (D308-8c lead). A diagnostic counter like the two above: a single long frame can pass the whole
        /// lead, so a per-frame observer cannot rely on seeing Current == Await.</summary>
        public int Awaits { get; private set; }
        public int ShellParts => shell.Count;
        public int HiddenRenderers { get; private set; }
        public int ShellDrawsLastFrame { get; private set; }
        public int ParticlesEmitted { get; private set; }
        public string LastIssue { get; private set; } = "";
        public Bounds CarBounds => bounds;
        public WorldMacroPalanquinController Vehicle => vehicle;

        static readonly int CoverageId = Shader.PropertyToID("_Coverage"), HeightOrderId = Shader.PropertyToID("_HeightOrder"),
            GroundId = Shader.PropertyToID("_Ground"), HeightId = Shader.PropertyToID("_Height"), NoiseScaleId = Shader.PropertyToID("_NoiseScale"),
            NoiseAmountId = Shader.PropertyToID("_NoiseAmount"), EdgeWidthId = Shader.PropertyToID("_EdgeWidth"), InflateId = Shader.PropertyToID("_Inflate"),
            InkId = Shader.PropertyToID("_InkColor"), EdgeId = Shader.PropertyToID("_EdgeColor"), SeedId = Shader.PropertyToID("_Seed");

        struct Part { public Transform Transform; public Mesh Mesh; }

        WorldMacroPalanquinController vehicle;
        Action done;
        float elapsed, seed, lead;
        Phase afterAwait;
        uint renderingLayers = 1u;
        Bounds bounds;
        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<bool> hiddenStates = new List<bool>();
        readonly List<Part> shell = new List<Part>();
        readonly HashSet<Renderer> higherLods = new HashSet<Renderer>();
        MaterialPropertyBlock block;
        ParticleSystem dust;

        /// <summary>Summon: the car (already placed and active) is hidden and forms out of ink. Returns false (and leaves the car
        /// visible) when there is nothing to present. <paramref name="leadSeconds"/> &gt; 0 (D308-8c): the hidden car waits that long
        /// (Await) while the call stroke's ink flies in, then the gather starts.</summary>
        public bool BeginMaterialize(WorldMacroPalanquinController car, Transform[] exclude, float leadSeconds = 0f)
        {
            Cancel();
            if (!Collect(car, exclude)) return false;
            SetCarHidden(true);
            elapsed = 0f; Coverage = 0f; HeightOrder = 1f;
            if (leadSeconds > 0f) { Current = Phase.Await; Awaits++; afterAwait = Phase.Gather; lead = leadSeconds; }
            else { Current = Phase.Gather; EmitGather(); }
            return true;
        }

        /// <summary>Recall: ink covers the visible car, the car is switched off under the full shell and the shell scatters.
        /// <paramref name="onDone"/> runs once at the end (the caller deactivates the car there). With nothing to present it runs now.
        /// <paramref name="leadSeconds"/> &gt; 0 (D308-8c): the visible car waits that long (Await) while the stroke's ink flies in.</summary>
        public bool BeginDissolve(WorldMacroPalanquinController car, Transform[] exclude, Action onDone, float leadSeconds = 0f)
        {
            Cancel();
            if (!Collect(car, exclude)) { onDone?.Invoke(); return false; }
            done = onDone;
            elapsed = 0f; Coverage = 0f; HeightOrder = 0f;
            if (leadSeconds > 0f) { Current = Phase.Await; Awaits++; afterAwait = Phase.Cover; lead = leadSeconds; }
            else Current = Phase.Cover;
            return true;
        }

        /// <summary>Stops at once: car renderers back to their recorded state, no callback. Particles fade on their own.</summary>
        public void Cancel()
        {
            SetCarHidden(false);
            Current = Phase.Idle; done = null; Coverage = 0f;
            vehicle = null; shell.Clear();
        }

        /// <summary>Recall that must not wait (death / terrain recovery): the callback runs now, renderers are restored first.</summary>
        public void Finish()
        {
            var callback = Current == Phase.Cover || Current == Phase.Scatter || (Current == Phase.Await && afterAwait == Phase.Cover) ? done : null;
            Cancel();
            callback?.Invoke();
        }

        bool Collect(WorldMacroPalanquinController car, Transform[] exclude)
        {
            hidden.Clear(); hiddenStates.Clear(); shell.Clear(); higherLods.Clear(); LastIssue = "";
            vehicle = car;
            if (car == null || !car.gameObject.activeInHierarchy) { LastIssue = "no active vehicle"; vehicle = null; return false; }
            if (Profile == null) { LastIssue = "no VehicleUx308 profile"; vehicle = null; return false; }
            foreach (var lod in car.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = lod.GetLODs();
                for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) higherLods.Add(r);
            }
            bool any = false;
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == null || Excluded(r.transform, exclude)) continue;
                hidden.Add(r); hiddenStates.Add(r.enabled);
                if (!r.enabled || !r.gameObject.activeInHierarchy || higherLods.Contains(r)) continue;
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                shell.Add(new Part { Transform = r.transform, Mesh = filter.sharedMesh });
                if (!any) { bounds = r.bounds; renderingLayers = r.renderingLayerMask; any = true; } else bounds.Encapsulate(r.bounds);
            }
            if (!any) { bounds = car.Hull != null ? car.Hull.bounds : new Bounds(car.transform.position, Vector3.one * 2f); }
            seed = UnityEngine.Random.value * 37f;
            if (Profile.ShellMaterial == null) LastIssue = "ShellMaterial empty: particles only";
            return true;
        }

        static bool Excluded(Transform t, Transform[] exclude)
        {
            if (exclude == null) return false;
            foreach (var root in exclude) if (root != null && t.IsChildOf(root)) return true;
            return false;
        }

        void SetCarHidden(bool hide)
        {
            if (hide == CarHidden) return;
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i] != null) hidden[i].enabled = !hide && hiddenStates[i];
            CarHidden = hide; HiddenRenderers = hide ? hidden.Count : 0;
        }

        void Update()
        {
            if (!Busy || Profile == null) return;
            if (vehicle == null) { Cancel(); return; }
            elapsed += Mathf.Max(0f, Time.deltaTime);
            switch (Current)
            {
                case Phase.Await:
                    Coverage = 0f;
                    if (elapsed >= lead) { Next(afterAwait); if (afterAwait == Phase.Gather) EmitGather(); }
                    break;
                case Phase.Gather:
                    Coverage = Smooth(elapsed / Mathf.Max(.05f, Profile.GatherSeconds)); HeightOrder = 1f;
                    if (elapsed >= Profile.GatherSeconds) { Next(Phase.Hold); Coverage = 1f; SetCarHidden(false); }
                    break;
                case Phase.Hold:
                    Coverage = 1f;
                    if (elapsed >= Profile.HoldSeconds) Next(Phase.Recede);
                    break;
                case Phase.Recede:
                    Coverage = 1f - Smooth(elapsed / Mathf.Max(.05f, Profile.RecedeSeconds)); HeightOrder = 0f;
                    if (elapsed >= Profile.RecedeSeconds) { Materializations++; Current = Phase.Idle; Coverage = 0f; vehicle = null; shell.Clear(); }
                    break;
                case Phase.Cover:
                    Coverage = Smooth(elapsed / Mathf.Max(.05f, Profile.CoverSeconds)); HeightOrder = 0f;
                    if (elapsed >= Profile.CoverSeconds) { Next(Phase.Scatter); Coverage = 1f; SetCarHidden(true); EmitScatter(); }
                    break;
                case Phase.Scatter:
                    Coverage = 1f - Smooth(elapsed / Mathf.Max(.05f, Profile.ScatterSeconds)); HeightOrder = 1f;
                    if (elapsed >= Profile.ScatterSeconds)
                    {
                        Dissolves++;
                        var callback = done; done = null;
                        // the car stays hidden until the caller deactivates it; then its renderers get their recorded state back
                        Current = Phase.Idle; Coverage = 0f;
                        callback?.Invoke();
                        SetCarHidden(false); vehicle = null; shell.Clear();
                    }
                    break;
            }
        }

        void Next(Phase phase) { Current = phase; elapsed = 0f; }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        void LateUpdate()
        {
            ShellDrawsLastFrame = 0;
            if (!Busy || Profile == null || Profile.ShellMaterial == null || shell.Count == 0 || Coverage <= 0f) return;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetFloat(CoverageId, Coverage); block.SetFloat(HeightOrderId, HeightOrder);
            block.SetFloat(GroundId, bounds.min.y - .05f); block.SetFloat(HeightId, Mathf.Max(.2f, bounds.size.y + .1f));
            block.SetFloat(NoiseScaleId, Profile.NoiseScale); block.SetFloat(NoiseAmountId, Profile.NoiseShare);
            block.SetFloat(EdgeWidthId, Profile.EdgeWidth); block.SetFloat(InflateId, Profile.Inflate); block.SetFloat(SeedId, seed);
            block.SetColor(InkId, Ldr(Profile.InkColour)); block.SetColor(EdgeId, Ldr(Profile.EdgeColour));
            var rp = new RenderParams(Profile.ShellMaterial)
            {
                matProps = block, layer = vehicle != null ? vehicle.gameObject.layer : 0, renderingLayerMask = renderingLayers,   // the car's own layers
                shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            foreach (var part in shell)
            {
                if (part.Transform == null || part.Mesh == null) continue;
                var matrix = part.Transform.localToWorldMatrix;
                for (int s = 0; s < part.Mesh.subMeshCount; s++) { Graphics.RenderMesh(rp, part.Mesh, s, matrix); ShellDrawsLastFrame++; }
            }
        }

        static Color Ldr(Color c) => new Color(Mathf.Clamp(c.r, 0f, .85f), Mathf.Clamp(c.g, 0f, .85f), Mathf.Clamp(c.b, 0f, .85f), Mathf.Clamp01(c.a));

        // ---------- ink dust ----------

        static readonly List<ParticleSystemVertexStream> DustStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent,
        };

        ParticleSystem Dust()
        {
            if (dust != null) return dust;
            if (Profile == null || Profile.InkParticleMaterial == null) return null;
            var go = new GameObject("VehicleInk308_Dust") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false);
            dust = go.AddComponent<ParticleSystem>();
            dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main;
            // looping with the emission module off: the system stays alive and simulates only what Emit adds
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = 256; main.gravityModifier = 0f;
            var emission = dust.emission; emission.enabled = false;
            var shape = dust.shape; shape.enabled = false;
            var colour = dust.colorOverLifetime; colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .2f), new GradientAlphaKey(.8f, .7f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(fade);
            var size = dust.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, .6f), new Keyframe(.5f, 1f), new Keyframe(1f, .85f)));
            var drag = dust.limitVelocityOverLifetime; drag.enabled = true; drag.drag = new ParticleSystem.MinMaxCurve(1.2f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sharedMaterial = Profile.InkParticleMaterial; renderer.SetActiveVertexStreams(DustStreams);
            dust.Play(true);
            return dust;
        }

        Color DustColour()
        {
            var c = Ldr(Profile.InkColour); c.a = Mathf.Clamp01(Profile.ParticleAlpha); return c;
        }

        void EmitGather()
        {
            var ps = Dust(); if (ps == null || Profile.GatherParticles <= 0) return;
            float life = Mathf.Max(.15f, Profile.GatherSeconds + Profile.HoldSeconds);
            Vector3 c = bounds.center, e = bounds.extents * 1.8f + Vector3.one * .4f;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = DustColour() };
            for (int i = 0; i < Profile.GatherParticles; i++)
            {
                var from = c + Vector3.Scale(UnityEngine.Random.onUnitSphere, e); from.y = Mathf.Max(from.y, bounds.min.y);
                var to = c + Vector3.Scale(UnityEngine.Random.insideUnitSphere, bounds.extents * .7f);
                p.position = from;
                p.velocity = (to - from) / life + (to - from).normalized * Profile.GatherInward * .25f;
                p.startLifetime = life * UnityEngine.Random.Range(.85f, 1.05f);
                p.startSize = UnityEngine.Random.Range(Profile.ParticleSize.x, Profile.ParticleSize.y);
                p.rotation = UnityEngine.Random.Range(0f, 360f);
                ps.Emit(p, 1); ParticlesEmitted++;
            }
        }

        void EmitScatter() => EmitScatterAt(bounds);

        /// <summary>Ink dust scattering out of <paramref name="area"/> (dust only, no shell). Also used for the old spot of a car that
        /// is moved to the front while it is still out, so it does not just vanish there.</summary>
        public void EmitScatterAt(Bounds area)
        {
            var ps = Dust(); if (ps == null || Profile == null || Profile.ScatterParticles <= 0) return;
            Vector3 c = area.center, e = area.extents;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = DustColour() };
            for (int i = 0; i < Profile.ScatterParticles; i++)
            {
                var at = c + Vector3.Scale(new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)), e);
                var outward = at - c; outward.y = 0f; outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : UnityEngine.Random.onUnitSphere;
                p.position = at;
                p.velocity = outward * Profile.ScatterOutward * UnityEngine.Random.Range(.6f, 1.2f) + Vector3.up * Profile.ScatterRise * UnityEngine.Random.Range(.4f, 1.2f);
                p.startLifetime = Mathf.Max(.2f, Profile.ScatterSeconds) * UnityEngine.Random.Range(1.1f, 1.7f);
                p.startSize = UnityEngine.Random.Range(Profile.ParticleSize.x, Profile.ParticleSize.y);
                p.rotation = UnityEngine.Random.Range(0f, 360f);
                ps.Emit(p, 1); ParticlesEmitted++;
            }
        }

        void OnDisable() { if (Busy) Finish(); }
        void OnDestroy() { if (dust != null) Destroy(dust.gameObject); }
    }
}
