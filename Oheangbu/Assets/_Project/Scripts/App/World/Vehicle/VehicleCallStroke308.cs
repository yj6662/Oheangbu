using System.Collections.Generic;
using Oheangbu.BrushRender;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.World.Vehicle
{
    /// <summary>
    /// #308 D308-8c (SPEC-VEHICLE-UX-308 §3b) [TEST]: 자동차 호출 획의 먹 자취와 흐름. 표현 전용.
    ///   · 자취: 붓끝(WorldMacroPlayerGestureRig.WorldTip)이 허공에 닿아 있는 동안(AirStrokeContact308) 실제로 지난 자리를 모아 카메라를
    ///     보는 먹 리본 하나로 그린다. RibbonMeshBuilder + Oheangbu/InkStroke 재질 사본이고 _FlashAdd·_FlashStrength·_FlashTint = 0이다
    ///     (발광 없음, 먹색 LDR ≤ .85). 호출 순간 뒤 TrailMeltSeconds에 마른다.
    ///   · 흐름: 호출 순간 자취 끝에서 차 자리(먹 껍질 중심)로 먹 획 하나가 날아간다(InkRibbon306 — 방어 성공 획과 같은 리본, 같은 비행 꼴).
    ///     VehicleInkPresentation308은 FlowSeconds × GatherAfterFlow 뒤 모임(소환)·덮임(회수)을 시작해 이어받는다.
    ///   · 거절·취소(피격)·자리 없음: 흐름 없이 자취가 그 자리에서 마른다.
    /// 인식 불가침: 이 컴포넌트는 Oheangbu.Drawing·Spellcraft·BrushStrokeFeedAdapter를 참조하지 않는다. BrushStrokePoint는 인식용
    /// Core StrokePoint와 다른 표현 전용 형이라 이 점들이 작도 인식기·필세 계산으로 갈 길이 없다.
    /// 붓끝은 리그(실행 순서 1000)가 놓은 뒤에 읽는다(1050). 시간은 Time.deltaTime(일시정지 중 멈춤). 정적 가변 필드 없음.
    /// </summary>
    [DefaultExecutionOrder(1050), DisallowMultipleComponent]
    public sealed class VehicleCallStroke308 : MonoBehaviour
    {
        const int TrailStencil = 250, FlowStencil = 251;   // the parry strokes use 253–255, glyph strokes cycle 1–255 (never during a call)

        public VehicleUx308ProfileSO Profile;
        public WorldMacroPlayerGestureRig Gesture;

        /// <summary>The trail is recording the brush tip (between Begin and Release).</summary>
        public bool Recording { get; private set; }
        public bool Flying => flying && !arrived;
        /// <summary>Anything of the call stroke still visible (trail or flow).</summary>
        public bool Visible => Recording || melting || flying;
        public int TrailPoints => points.Count;
        public int MaxTrailPoints { get; private set; }
        public int Strokes { get; private set; }
        public int Flights { get; private set; }
        public int Arrivals { get; private set; }
        public int Melts { get; private set; }
        public float LastFlightSeconds { get; private set; }
        public float LastTrailLength { get; private set; }
        public Vector3 FlowTarget => flowTo;
        /// <summary>Largest RGB channel handed to the ink material (LDR check, ART-INK ≤ .85).</summary>
        public float MaxInkChannel { get; private set; }
        public int TrailDrawsLastFrame { get; private set; }
        public int FlowDrawsLastFrame { get; private set; }
        public string LastIssue { get; private set; } = "";

        readonly List<Vector3> points = new List<Vector3>(64);
        readonly List<float> births = new List<float>(64);   // scaled birth time per trail point: the InkStroke bleed ages from it (_InkNow - birth)
        Transform root;
        AirRibbon trail;
        InkRibbon306 flow;
        bool melting, flying, arrived;
        float melt, flyProgress, flowMelt, flowTailAtArrival, flightClock;
        Vector3 flowFrom, flowTo;

        /// <summary>Starts a new trail for the stroke the rig is presenting now (any previous trail and flow are cleared).</summary>
        public void Begin(WorldMacroPlayerGestureRig gesture)
        {
            Gesture = gesture;
            Clear();
            Recording = true; Strokes++; MaxTrailPoints = 0; LastTrailLength = 0f; LastIssue = "";
            if (Profile == null) LastIssue = "no VehicleUx308 profile";
        }

        /// <summary>The call moment: recording stops and the trail dries. With a target the ink flies from the stroke end to it
        /// (the car spot) and lands after FlowSeconds.</summary>
        public void Release(Vector3? target)
        {
            if (!Recording) return;
            Recording = false;
            // the flow leaves from the stroke end (or the brush tip when the trail caught no travel)
            bool hasStart = points.Count > 0 || (Gesture != null && Gesture.WorldTip != null);
            Vector3 start = points.Count > 0 ? points[points.Count - 1] : hasStart ? Gesture.WorldTip.position : Vector3.zero;
            melting = points.Count > 1; melt = 0f;
            if (!melting) { points.Clear(); births.Clear(); }
            if (target.HasValue && hasStart && Profile != null)
            {
                flowFrom = start; flowTo = target.Value;
                flying = true; arrived = false; flyProgress = 0f; flowMelt = 0f; flightClock = 0f; Flights++;
            }
        }

        /// <summary>Cancelled before the call moment (damage, menu, seat): the trail dries where it is, nothing flies.</summary>
        public void Cancel() => Release(null);

        /// <summary>Hides everything at once (no callback exists to skip).</summary>
        public void Clear()
        {
            Recording = melting = flying = arrived = false;
            points.Clear(); births.Clear(); melt = flyProgress = flowMelt = 0f;
            trail?.Hide(); flow?.Hide();
        }

        void LateUpdate()
        {
            TrailDrawsLastFrame = FlowDrawsLastFrame = 0;
            if (Profile == null) { if (Visible) Clear(); return; }
            if (!Visible) return;   // idle between calls: both ribbons are already hidden (every path that ends them hides them)
            float dt = Time.timeScale <= 0f ? 0f : Mathf.Max(0f, Time.deltaTime);
            if (Recording)
            {
                if (Gesture == null || !Gesture.AirStrokeActive308) Release(null);   // the gesture ended without a call moment
                else if (Gesture.AirStrokeContact308 && Gesture.WorldTip != null) Record(Gesture.WorldTip.position);
            }
            if (melting)
            {
                melt += dt / Mathf.Max(.05f, Profile.TrailMeltSeconds);
                if (melt >= 1f) { melting = false; points.Clear(); births.Clear(); trail?.Hide(); Melts++; }
            }
            if (flying)
            {
                flightClock += dt;
                if (!arrived)
                {
                    flyProgress += dt / Mathf.Max(.05f, Profile.FlowSeconds);
                    if (flyProgress >= 1f)
                    {
                        flyProgress = 1f; arrived = true; Arrivals++; LastFlightSeconds = flightClock;
                        flowTailAtArrival = Mathf.Max(0f, 1f - Profile.FlowTailLength);
                    }
                }
                else
                {
                    flowMelt += dt / Mathf.Max(.05f, Profile.FlowMeltSeconds);
                    if (flowMelt >= 1f) { flying = false; flow?.Hide(); }
                }
            }
            var cam = Camera.main;
            if (cam == null) { trail?.Hide(); flow?.Hide(); return; }
            Color ink = Ldr(Profile.InkColour);
            MaxInkChannel = Mathf.Max(MaxInkChannel, Mathf.Max(ink.r, Mathf.Max(ink.g, ink.b)));
            DrawTrail(cam, ink);
            DrawFlow(cam, ink);
        }

        void Record(Vector3 tip)
        {
            if (!float.IsFinite(tip.x + tip.y + tip.z)) return;
            int max = Mathf.Max(2, Profile.TrailMaxPoints);
            if (points.Count > 0)
            {
                Vector3 last = points[points.Count - 1];
                if (Vector3.Distance(last, tip) < Mathf.Max(.001f, Profile.TrailMinSpacing)) return;
                LastTrailLength += Vector3.Distance(last, tip);
                if (points.Count >= max) { points[points.Count - 1] = tip; births[births.Count - 1] = Time.time; return; }   // full: the head keeps following the tip
            }
            points.Add(tip); births.Add(Time.time);
            MaxTrailPoints = Mathf.Max(MaxTrailPoints, points.Count);
        }

        void DrawTrail(Camera cam, Color ink)
        {
            if (points.Count < 2 || (!Recording && !melting)) { trail?.Hide(); return; }
            if (trail == null) trail = new AirRibbon("VehicleCallStroke308_Trail", Root(), Profile.CallStrokeMaterial, TrailStencil);
            float m = Mathf.Clamp01(melt);
            float fade = melting ? 1f - m * m : 1f, dissolve = melting ? m * .85f : 0f;
            if (trail.Draw(points, births, Profile.StrokeWidth, Profile.StrokeEndWidthScale, Profile.StrokeEndInk, fade, dissolve, ink, cam)) TrailDrawsLastFrame++;
        }

        void DrawFlow(Camera cam, Color ink)
        {
            if (!flying) { flow?.Hide(); return; }
            if (flow == null) flow = new InkRibbon306("VehicleCallStroke308_Flow", Root(), Profile.CallStrokeMaterial, FlowStencil);
            // as the parry stroke: leaves fast and slows into the target; the tail follows, then dries into the car after landing
            float p = Mathf.Clamp01(flyProgress), fly = 1f - (1f - p) * (1f - p);
            float head = fly;
            float tail = arrived ? Mathf.Lerp(flowTailAtArrival, head, Mathf.Clamp01(flowMelt)) : Mathf.Max(0f, head - Profile.FlowTailLength);
            float melted = arrived ? Mathf.Clamp01(flowMelt) : 0f;
            // tint 1 with the trail's own LDR ink (≤ .85) as the "flash" colour: the flow is the same ink as the trail it leaves
            // (InkRibbon306's fixed ink would be lighter); no element colour, and the HDR add term is 0 (InkRibbon306 sets _FlashAdd 0)
            flow.Draw(flowFrom, flowTo, tail, head, Profile.FlowWidth, Profile.FlowEndWidthScale, Profile.FlowArc, .55f,
                ink, 1f, 1f - melted * melted, melted * .85f, cam, 16);
            FlowDrawsLastFrame++;
        }

        Transform Root()
        {
            if (root != null) return root;
            var go = new GameObject("VehicleCallStroke308") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(transform, false); root = go.transform; return root;
        }

        static Color Ldr(Color c) => new Color(Mathf.Clamp(c.r, 0f, .85f), Mathf.Clamp(c.g, 0f, .85f), Mathf.Clamp(c.b, 0f, .85f), 1f);

        void OnDisable() { Clear(); }
        void OnDestroy()
        {
            trail?.Dispose(); flow?.Dispose(); trail = null; flow = null;
            if (root != null) Destroy(root.gameObject);
        }

        /// <summary>A camera-facing ink ribbon through recorded world points (the real brush-tip path). The points are projected onto
        /// the plane through the stroke that faces the camera (RibbonMeshBuilder builds in local XY). Same material contract as
        /// InkRibbon306: a per-ribbon copy of Oheangbu/InkStroke, stencil per ribbon, x-ray pass off, no flash or HDR add.</summary>
        sealed class AirRibbon
        {
            static readonly int FlashColorId = Shader.PropertyToID("_FlashColor"), FlashStrengthId = Shader.PropertyToID("_FlashStrength"),
                FlashTintId = Shader.PropertyToID("_FlashTint"), FlashAddId = Shader.PropertyToID("_FlashAdd"), FadeId = Shader.PropertyToID("_FadeMul"),
                DissolveId = Shader.PropertyToID("_DissolveT"), StencilId = Shader.PropertyToID("_StencilRef"), SeedId = Shader.PropertyToID("_NoiseSeed");
            readonly GameObject go;
            readonly Transform t;
            readonly MeshRenderer renderer;
            readonly Mesh mesh;
            readonly Material material;
            readonly BrushStrokeData data = new BrushStrokeData();
            readonly RibbonMeshBuilder builder = new RibbonMeshBuilder();

            public AirRibbon(string name, Transform parent, Material source, int stencilRef)
            {
                go = new GameObject(name) { hideFlags = HideFlags.DontSave };
                t = go.transform; t.SetParent(parent, false);
                mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave }; mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var shader = source == null ? Shader.Find("Oheangbu/InkStroke") : null;
                material = source != null ? new Material(source) : shader != null ? new Material(shader) : null;
                if (material != null)
                {
                    material.hideFlags = HideFlags.DontSave;
                    material.SetFloat(StencilId, Mathf.Clamp(stencilRef, 1, 255));
                    material.SetFloat(FlashAddId, 0f); material.SetFloat(FlashStrengthId, 0f); material.SetFloat(FlashTintId, 0f);
                    material.SetFloat(SeedId, stencilRef * 3.17f); material.SetShaderPassEnabled("SRPDefaultUnlit", false);
                }
                renderer.sharedMaterial = material; renderer.enabled = false;
            }

            public bool Draw(List<Vector3> points, List<float> births, float width, float endScale, float endInk, float fade, float dissolve, Color ink, Camera cam)
            {
                int n = points.Count;
                if (material == null || n < 2 || fade <= .002f) { Hide(); return false; }
                Vector3 first = points[0], centre = Vector3.zero;
                for (int i = 0; i < n; i++) centre += points[i];
                centre /= n;
                Vector3 x = points[n - 1] - first;
                if (x.sqrMagnitude < 1e-6f) x = cam.transform.right;
                x.Normalize();
                Vector3 toCam = cam.transform.position - centre;
                Vector3 z = toCam - x * Vector3.Dot(toCam, x);
                if (z.sqrMagnitude < 1e-6f) z = Vector3.Cross(x, Mathf.Abs(x.y) < .9f ? Vector3.up : Vector3.right);
                z.Normalize();
                Vector3 y = Vector3.Cross(z, x);
                t.SetPositionAndRotation(first, Quaternion.LookRotation(z, y)); t.localScale = Vector3.one;
                if (t.parent != null) { var ls = t.parent.lossyScale; t.localScale = new Vector3(1f / Mathf.Max(1e-4f, ls.x), 1f / Mathf.Max(1e-4f, ls.y), 1f / Mathf.Max(1e-4f, ls.z)); }
                data.Clear();
                for (int i = 0; i < n; i++)
                {
                    float k = i / (float)(n - 1);
                    Vector3 d = points[i] - first;
                    // 기필: the brush enters a little thinner, swells, then lifts off thin and drier at the end (수필)
                    float entry = Mathf.SmoothStep(.45f, 1f, Mathf.Clamp01(k * 5f));
                    float w = width * entry * Mathf.Lerp(1f, endScale, k * k);
                    data.Add(new BrushStrokePoint(new Vector3(Vector3.Dot(d, x), Vector3.Dot(d, y), 0f), w, Mathf.Lerp(1f, endInk, k), births[i]));
                }
                builder.Build(data, ink, 1f, 1, 1, 3, mesh);
                material.SetColor(FlashColorId, ink); material.SetFloat(FlashStrengthId, 0f); material.SetFloat(FlashTintId, 0f); material.SetFloat(FlashAddId, 0f);
                material.SetFloat(FadeId, Mathf.Clamp01(fade)); material.SetFloat(DissolveId, Mathf.Clamp01(dissolve));
                renderer.enabled = true;
                return true;
            }

            public void Hide() { if (renderer != null) renderer.enabled = false; }

            public void Dispose()
            {
                if (go != null) Object.Destroy(go);
                if (mesh != null) Object.Destroy(mesh);
                if (material != null) Object.Destroy(material);
            }
        }
    }
}
