using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.Demo
{
    // #308 기관 표면 덧칠(SPEC-TELEGRAPH-ORGAN-308, D308-4) — EnemyElementTelegraph가 소유하는 일반 클래스(MonoBehaviour 아님).
    // 새 GameObject·Renderer를 만들지 않는다: 기관이 가리키는 렌더러(TargetRenderer)의 재질 배열 끝에 덧칠 재질 사본을 붙여
    // 하나뿐인 서브메시를 Oheangbu/InkOrganSurface로 한 번 더 그린다(투명 큐, 깊이·그림자 패스 없음).
    //   · 렌더러마다 덧칠 재질 사본 하나(런타임, DontSave) — 첫 예고가 만들고 그 뒤에는 재사용(AC-T2 Material 수 증가 0)
    //   · MPB를 쓰지 않는다: FolkloreReadability298의 SH MPB 재적용·Restore()와 부딪치지 않는다(AC-T4)
    //   · 붙이기 전 GetSharedMaterials 배열(EnemyController 슬롯 0 사본 포함)을 잡아 두고, 뗄 때는 우리 재질만 뺀다 —
    //     그사이 아무도 배열을 바꾸지 않았으면 결과는 붙이기 전 배열과 참조 단위로 같다(다르면 RestoreMismatches로 보고)
    //   · 서브메시가 1개가 아닌 렌더러는 대상이 될 수 없다(덧붙인 재질은 마지막 서브메시만 다시 그린다) — Bind가 거부하고 보고
    //   · 매 프레임 SetVectorArray로 기관 값을 쓴다. 버퍼는 미리 잡아 둔다(프레임마다 할당 없음)
    // D308-4d 마석 오염: 오염(ContamRenderer = 몸)을 가진 기관은 그 렌더러를 '상시 표면'으로 묶는다. 예고와 무관하게 보는 이(카메라)가
    // AttachDistance 안이면 붙이고 AttachDistance + DetachMargin 밖이면 뗀다(히스테리시스). 붙은 동안 그리기 1회가 더해진다(같은 스키닝
    // 결과를 마지막 서브메시로 다시 그림 — 스키닝 재계산·그림자·깊이 패스 없음). 예고 창 안에서는 같은 재질에 번짐 앞머리·물듦 값만 쓴다.
    // 평소 어둡힘의 세기는 FadeStartDistance부터 풀려 AttachDistance에서 0이다(붙고 떨어지는 순간이 보이지 않는다).
    // 정적 가변 필드 없음(Shader.PropertyToID 상수만) — 도메인 리로드 꺼짐에서도 초기화할 것이 없다.
    public sealed class OrganSurfaceOverlay
    {
        public const int MaxOrgansPerRenderer = 16;

        private static readonly int SphereId = Shader.PropertyToID("_OrganSphere"), StateId = Shader.PropertyToID("_OrganState"),
            ParamId = Shader.PropertyToID("_OrganParam"), CountId = Shader.PropertyToID("_OrganCount"), TintId = Shader.PropertyToID("_Tint"),
            PulseId = Shader.PropertyToID("_Pulse"), BreakId = Shader.PropertyToID("_Break"), SeedId = Shader.PropertyToID("_Seed"),
            BrightId = Shader.PropertyToID("_Brightness"), PaperId = Shader.PropertyToID("_PaperBrightness"), MaskTexId = Shader.PropertyToID("_MaskTex"),
            MaskModeId = Shader.PropertyToID("_MaskMode"), MaskThresholdId = Shader.PropertyToID("_MaskThreshold"), RingScaleId = Shader.PropertyToID("_RingScale"),
            RingWidthId = Shader.PropertyToID("_RingWidth"), FeatherId = Shader.PropertyToID("_Feather"), ZBiasId = Shader.PropertyToID("_ZBias"),
            DebugId = Shader.PropertyToID("_DebugView"),
            ContamModeId = Shader.PropertyToID("_ContamMode"), ContamTexId = Shader.PropertyToID("_ContamTex"),
            ContamSphereId = Shader.PropertyToID("_ContamSphere"), ContamStateId = Shader.PropertyToID("_ContamState"),
            ContamColorId = Shader.PropertyToID("_ContamColor"), ContamDarkId = Shader.PropertyToID("_ContamDark"),
            ContamFlowSoftId = Shader.PropertyToID("_ContamFlowSoft"), ContamMaxLodId = Shader.PropertyToID("_ContamMaxLod");

        private sealed class Surface
        {
            public Renderer Renderer;
            public Material Material;
            public Texture Mask;
            public bool Attached, Wanted, OrganWanted;
            public int Count;
            public readonly Vector4[] Spheres = new Vector4[MaxOrgansPerRenderer];
            public readonly Vector4[] States = new Vector4[MaxOrgansPerRenderer];
            public readonly Vector4[] Params = new Vector4[MaxOrgansPerRenderer];
            public readonly List<Material> Saved = new List<Material>(4);
            // D308-4d contamination (one per surface: one crystal per body)
            public int ContamOrgan = -1;
            public EnemyContaminationMode ContamMode;
            public Texture ContamTex;
            public bool InRange;
            public Vector4 ContamSphere, ContamState;
        }

        private readonly List<Surface> _surfaces = new List<Surface>(4);
        private readonly List<Material> _scratch = new List<Material>(8);
        private int[] _organSurface = new int[0], _organSlot = new int[0];
        private Material _template, _ownedTemplate;
        private bool _templateLooked;

        // 읽기 전용 진단
        public int BoundOrganCount { get; private set; }
        public int UnboundOrganCount { get; private set; }
        public int AttachedCount { get; private set; }
        // 예고(기관 덧칠)로 붙은 렌더러 수 — 오염만으로 붙은 상시 표면은 세지 않는다(AC-12b·T2의 '예고 창 밖 덧칠 0'은 이 값)
        public int OrganAttachedCount { get; private set; }
        // D308-4d: 오염 표면 수 · 지금 붙은 오염 표면 수(거리 예산 안)
        public int ContaminationSurfaceCount { get; private set; }
        public int ContaminationAttachedCount { get; private set; }
        // 기관 없이 오염만 가진 표면 수(몸 렌더러) — 이 표면의 덧칠 사본은 기관 대상 렌더러 수에 들지 않는다(AC-T2 집계에서 따로 더한다)
        public int ContaminationOnlySurfaceCount { get; private set; }
        public int MaterialCount { get; private set; }
        public int RestoreMismatches { get; private set; }
        public string Refusals { get; private set; } = "";
        public bool ShaderMissing { get; private set; }
        public float LastBrightness { get; private set; }
        public float LastPaperBrightness { get; private set; }

        // 기관 → 덧칠 표면 지도(한 번). 렌더러가 없거나 서브메시가 1개가 아니거나 렌더러당 16을 넘는 기관은 빛나지 않는다(보고).
        // 오염을 가진 기관은 그 몸 렌더러를 상시 표면으로 더한다(렌더러당 오염 하나 — 둘째는 보고하고 버린다)
        public void Bind(EnemyOrganSet organs)
        {
            DetachAll();
            foreach (var old in _surfaces) if (old.Material != null) { Release(old.Material); old.Material = null; MaterialCount--; }
            _surfaces.Clear(); BoundOrganCount = UnboundOrganCount = ContaminationSurfaceCount = 0; Refusals = "";
            int n = organs != null ? organs.Count : 0;
            _organSurface = new int[n]; _organSlot = new int[n];
            var refusals = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                _organSurface[i] = -1; _organSlot[i] = -1;
                var organ = organs.Get(i);
                if (organ == null || organ.TargetRenderer == null) { UnboundOrganCount++; continue; }
                int subMeshes = SubMeshCount(organ.TargetRenderer);
                if (subMeshes != 1)
                {
                    UnboundOrganCount++;
                    refusals.Append(organ.Id).Append(": ").Append(organ.TargetRenderer.name).Append(" has ").Append(subMeshes).Append(" submeshes; ");
                    continue;
                }
                int s = SurfaceIndex(organ.TargetRenderer);
                var surface = _surfaces[s];
                if (surface.Count >= MaxOrgansPerRenderer)
                {
                    UnboundOrganCount++; refusals.Append(organ.Id).Append(": over ").Append(MaxOrgansPerRenderer).Append(" organs on ").Append(organ.TargetRenderer.name).Append("; ");
                    continue;
                }
                if (organ.Mode == EnemyOrganSurfaceMode.Mask && organ.Mask != null)
                {
                    if (surface.Mask == null) surface.Mask = organ.Mask;
                    else if (surface.Mask != organ.Mask) refusals.Append(organ.Id).Append(": second mask on ").Append(organ.TargetRenderer.name).Append(" (first kept); ");
                }
                _organSurface[i] = s; _organSlot[i] = surface.Count;
                surface.Params[surface.Count] = new Vector4(0f, i * 1.37f, 0f, 0f);
                surface.Count++; BoundOrganCount++;
            }
            // mask use per organ: only a Mask organ whose texture is the surface texture samples it (Part / Sphere = 1)
            for (int i = 0; i < n; i++)
            {
                int s = _organSurface[i]; if (s < 0) continue;
                var organ = organs.Get(i); var surface = _surfaces[s];
                bool useMask = organ.Mode == EnemyOrganSurfaceMode.Mask && organ.Mask != null && organ.Mask == surface.Mask;
                surface.Params[_organSlot[i]].x = useMask ? 1f : 0f;
            }
            // D308-4d contamination: the body renderer becomes a persistent surface (attached by distance, not by the telegraph window)
            for (int i = 0; i < n; i++)
            {
                var organ = organs.Get(i);
                if (organ == null || !organ.HasContamination) continue;
                int subMeshes = SubMeshCount(organ.ContamRenderer);
                if (subMeshes != 1)
                {
                    refusals.Append(organ.Id).Append(": contamination on ").Append(organ.ContamRenderer.name).Append(" refused (").Append(subMeshes).Append(" submeshes); ");
                    continue;
                }
                var surface = _surfaces[SurfaceIndex(organ.ContamRenderer)];
                if (surface.ContamOrgan >= 0) { refusals.Append(organ.Id).Append(": second contamination on ").Append(organ.ContamRenderer.name).Append(" (first kept); "); continue; }
                surface.ContamOrgan = i; surface.ContamMode = organ.ContamMode;
                surface.ContamTex = organ.ContamMode == EnemyContaminationMode.Texture ? organ.ContamMask : null;
                ContaminationSurfaceCount++;
            }
            ContaminationOnlySurfaceCount = 0;
            for (int k = 0; k < _surfaces.Count; k++) if (_surfaces[k].ContamOrgan >= 0 && _surfaces[k].Count == 0) ContaminationOnlySurfaceCount++;
            Refusals = refusals.ToString();
        }

        private int SurfaceIndex(Renderer renderer)
        {
            for (int k = 0; k < _surfaces.Count; k++) if (_surfaces[k].Renderer == renderer) return k;
            _surfaces.Add(new Surface { Renderer = renderer });
            return _surfaces.Count - 1;
        }

        public bool IsBound(int organ) => organ >= 0 && organ < _organSurface.Length && _organSurface[organ] >= 0;

        public void BeginFrame()
        {
            for (int k = 0; k < _surfaces.Count; k++)
            {
                var s = _surfaces[k]; s.Wanted = s.OrganWanted = false;
                for (int i = 0; i < s.Count; i++) s.States[i] = Vector4.zero;
            }
        }

        // 한 기관의 이번 프레임 값(월드 중심·반지름 m · level = 알파 × 사슬 · open · blot · burst). 묶이지 않은 기관이면 false
        public bool SetOrgan(int organ, Vector3 centre, float radius, float level, float open, float blot, float burst)
        {
            if (!IsBound(organ)) return false;
            var s = _surfaces[_organSurface[organ]]; int slot = _organSlot[organ];
            s.Spheres[slot] = new Vector4(centre.x, centre.y, centre.z, Mathf.Max(.01f, radius));
            s.States[slot] = new Vector4(Mathf.Clamp01(level), Mathf.Clamp01(open), Mathf.Clamp01(blot), Mathf.Clamp01(burst));
            if (level > 0f) s.Wanted = s.OrganWanted = true;
            return true;
        }

        // 디버그 보기(AC-T8 _DebugView 1 캡처): 영역만 흰색 — 레벨과 무관하게 붙인다
        public void SetDebugOrgan(int organ, Vector3 centre, float radius)
        {
            if (!IsBound(organ)) return;
            var s = _surfaces[_organSurface[organ]]; int slot = _organSlot[organ];
            s.Spheres[slot] = new Vector4(centre.x, centre.y, centre.z, Mathf.Max(.01f, radius));
            s.Wanted = s.OrganWanted = true;
        }

        // D308-4d: 상시 오염 표면 — viewer(카메라)와 오염 중심의 거리로 붙임/뗌(히스테리시스), 붙어 있으면 이번 프레임 값을 잡아 둔다.
        // flow = 번짐 앞머리(측지 거리 비, 0 = 평소), tint = 예고 알파(0 = 평소 무채), blot = 가라앉음·취소·방어 성공 먹 얼룩.
        // 뷰어가 없으면(카메라 없음) 붙인다 — 보이는 이가 없으면 그려지지도 않는다
        // fadeStart: 이 거리부터 평소 어둡힘의 세기가 풀려 attach에서 0이 된다(붙고 떨어지는 순간이 보이지 않는다). 속성 물듦은 세기와 무관
        public void SetContamination(EnemyOrganSet organs, bool hasViewer, Vector3 viewer, float attach, float detach, float fadeStart, float flow, float tint, float blot)
        {
            for (int k = 0; k < _surfaces.Count; k++)
            {
                var s = _surfaces[k];
                if (s.ContamOrgan < 0 || s.Renderer == null || organs == null || s.ContamOrgan >= organs.Count) continue;
                var organ = organs.Get(s.ContamOrgan);
                Vector3 c = organs.ContamPoint(s.ContamOrgan);
                float d2 = hasViewer ? (viewer - c).sqrMagnitude : 0f;
                if (s.InRange) { if (d2 > detach * detach) s.InRange = false; }
                else if (d2 <= attach * attach) s.InRange = true;
                if (!s.InRange) continue;
                s.Wanted = true;
                float strength = 1f;
                if (hasViewer && fadeStart < attach) strength = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, attach, Mathf.Sqrt(d2)));
                s.ContamSphere = new Vector4(c.x, c.y, c.z, Mathf.Max(.01f, organ.ContamRadius));
                s.ContamState = new Vector4(Mathf.Max(0f, flow), Mathf.Clamp01(tint), Mathf.Clamp01(blot), strength);
            }
        }

        // D308-4d 측정용(오염 켬/끔 비교, AC-C3·C5): 이번 프레임 오염을 쓰지 않는다 — 상시 표면은 예고가 없으면 떼어지고,
        // 예고로 붙어 있어도 _ContamMode 0이다. 다시 SetContamination을 부르면 거리 판정부터 새로 한다
        public void ClearContamination()
        {
            for (int k = 0; k < _surfaces.Count; k++) _surfaces[k].InRange = false;
        }

        // 이번 프레임을 렌더러에 반영한다: 원하는 표면은 붙이고 값을 쓰고, 아닌 표면은 뗀다. 반환 = 예고(기관)로 붙은 렌더러 수.
        // debugMode: 0 보통 / 1 기관 영역 흰색(AC-T8) / 2 오염 영역 흰색(AC-C). contamInk = 팔레트 오염 먹(sRGB)
        public int EndFrame(EnemyTelegraphTimingSO t, Color tint, float pulse, float brk, float seed, float debugMode, Color contamInk)
        {
            AttachedCount = OrganAttachedCount = ContaminationAttachedCount = 0;
            float bright = Mathf.Min(1f, t != null ? t.MaxBrightness : .85f);
            float paper = Mathf.Min(bright, t != null ? t.PaperBrightness : .78f);   // AC-12d: both ≤ MaxBrightness
            for (int k = 0; k < _surfaces.Count; k++)
            {
                var s = _surfaces[k];
                if (s.Renderer == null) { s.Attached = false; continue; }
                if (!s.Wanted) { if (s.Attached) Detach(s); continue; }
                if (s.Material == null && !MakeMaterial(s, t)) continue;
                var m = s.Material;
                if (s.Count > 0)
                {
                    m.SetVectorArray(SphereId, s.Spheres); m.SetVectorArray(StateId, s.States); m.SetVectorArray(ParamId, s.Params);
                }
                m.SetFloat(CountId, s.OrganWanted || debugMode > .5f ? s.Count : 0);
                m.SetColor(TintId, tint); m.SetFloat(PulseId, pulse); m.SetFloat(BreakId, brk);
                m.SetFloat(SeedId, seed); m.SetFloat(BrightId, bright); m.SetFloat(PaperId, paper); m.SetFloat(DebugId, debugMode);
                if (t != null)
                {
                    m.SetFloat(MaskThresholdId, t.MaskThreshold); m.SetFloat(RingScaleId, t.RingScale); m.SetFloat(RingWidthId, t.RingWidth);
                    m.SetFloat(FeatherId, t.Feather); m.SetFloat(ZBiasId, t.ZBias);
                }
                if (s.ContamOrgan >= 0)
                {
                    bool live = s.InRange;
                    m.SetFloat(ContamModeId, live ? (float)s.ContamMode : 0f);
                    m.SetVector(ContamSphereId, s.ContamSphere); m.SetVector(ContamStateId, live ? s.ContamState : Vector4.zero);
                    m.SetColor(ContamColorId, contamInk);
                    if (t != null)
                    {
                        m.SetVector(ContamDarkId, new Vector4(t.ContaminationDarkNear, t.ContaminationDarkFar, t.ContaminationInkMix, t.ContaminationTintAlpha));
                        m.SetFloat(ContamFlowSoftId, t.ContaminationFlowSoftness); m.SetFloat(ContamMaxLodId, t.ContaminationMaxMip);
                    }
                    if (live) ContaminationAttachedCount++;
                }
                if (!s.Attached) Attach(s);
                AttachedCount++;
                if (s.OrganWanted) OrganAttachedCount++;
            }
            LastBrightness = bright; LastPaperBrightness = paper;
            return OrganAttachedCount;
        }

        public void DetachAll()
        {
            for (int k = 0; k < _surfaces.Count; k++) { var s = _surfaces[k]; if (s.Attached) Detach(s); s.InRange = false; }
            AttachedCount = OrganAttachedCount = ContaminationAttachedCount = 0;
        }

        public void Dispose()
        {
            DetachAll();
            foreach (var s in _surfaces) if (s.Material != null) { Release(s.Material); s.Material = null; }
            if (_ownedTemplate != null) Release(_ownedTemplate);
            _ownedTemplate = null; _template = null; _surfaces.Clear(); MaterialCount = 0;
        }

        // 렌더러에 붙어 있는 덧칠 재질(검사용) — 없으면 null
        public Material MaterialOn(Renderer renderer)
        {
            for (int k = 0; k < _surfaces.Count; k++) if (_surfaces[k].Renderer == renderer) return _surfaces[k].Material;
            return null;
        }

        public Renderer RendererOf(int organ) => IsBound(organ) ? _surfaces[_organSurface[organ]].Renderer : null;
        public bool IsAttached(Renderer renderer)
        {
            for (int k = 0; k < _surfaces.Count; k++) if (_surfaces[k].Renderer == renderer) return _surfaces[k].Attached;
            return false;
        }
        // D308-4d 검사용: 오염 표면 렌더러(index < ContaminationSurfaceCount 순서 아님 — 표면 순서), 없으면 null
        public Renderer ContaminationRenderer(int nth)
        {
            for (int k = 0, seen = 0; k < _surfaces.Count; k++) if (_surfaces[k].ContamOrgan >= 0 && seen++ == nth) return _surfaces[k].Renderer;
            return null;
        }

        private void Attach(Surface s)
        {
            s.Renderer.GetSharedMaterials(s.Saved);   // 붙이기 전 배열(슬롯 0 사본 포함)
            _scratch.Clear();
            for (int i = 0; i < s.Saved.Count; i++) _scratch.Add(s.Saved[i]);
            _scratch.Add(s.Material);
            s.Renderer.SetSharedMaterials(_scratch);
            s.Attached = true;
        }

        private void Detach(Surface s)
        {
            s.Attached = false;
            if (s.Renderer == null) return;
            s.Renderer.GetSharedMaterials(_scratch);
            for (int i = _scratch.Count - 1; i >= 0; i--) if (_scratch[i] == s.Material) _scratch.RemoveAt(i);
            bool same = _scratch.Count == s.Saved.Count;
            for (int i = 0; same && i < _scratch.Count; i++) same = _scratch[i] == s.Saved[i];
            if (!same) RestoreMismatches++;   // someone else changed the array meanwhile: keep their change, drop only ours
            s.Renderer.SetSharedMaterials(_scratch);
        }

        // no material = no overlay (a null material would draw the magenta error shader — never a fake glow)
        private bool MakeMaterial(Surface s, EnemyTelegraphTimingSO t)
        {
            if (_template == null)
            {
                _template = t != null ? t.SurfaceMaterial : null;
                if (_template == null && !_templateLooked)
                {
                    _templateLooked = true;
                    var shader = Shader.Find(EnemyTelegraphTimingSO.SurfaceShaderName);
                    if (shader != null) _template = _ownedTemplate = new Material(shader) { hideFlags = HideFlags.DontSave, name = "InkOrganSurface308 (runtime)" };
                }
                if (_template == null) { ShaderMissing = true; return false; }
            }
            s.Material = new Material(_template) { hideFlags = HideFlags.DontSave, name = "InkOrganSurface308 " + s.Renderer.name };
            s.Material.SetFloat(MaskModeId, s.Mask != null ? 1f : 0f);
            if (s.Mask != null) s.Material.SetTexture(MaskTexId, s.Mask);
            s.Material.SetFloat(ContamModeId, 0f);
            if (s.ContamTex != null) s.Material.SetTexture(ContamTexId, s.ContamTex);
            MaterialCount++;
            return true;
        }

        private static void Release(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        public static int SubMeshCount(Renderer r)
        {
            if (r == null) return 0;
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh != null ? smr.sharedMesh.subMeshCount : 0;
            var filter = r.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.subMeshCount : 0;
        }
    }
}
