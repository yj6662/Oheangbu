using System;
using System.Collections.Generic;
using Oheangbu.App;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153b variant 우/M (2026-10-29): the water ring 우 as WAVE MODELS MADE BY MESHY from the concept uks_1 (image-to-3D: the painted crests cut out of the concept
    /// became thin painted relief cards, 6.5 k triangles each, the painting as the colour map). 16 of them stand on the ring: 8 hooked crests (two models, mirrored at random) at radius R,
    /// 2 taller ones behind them (R + 0.75), 6 low mound clusters in front (R - 0.12); the arc behind the caster (toward the camera) is left empty. Each card is turned toward a point
    /// between the ring centre and the camera side (a card seen edge-on is a sliver; the side crests would be invisible if they faced the centre exactly). Motion on the scope clock
    /// (no coroutine, no Time, no Physics): a staggered rise from below the ground with ease-back (0 - 0.4 s + 0.45 s each), a forward tilt = the curl, a sink + noise erosion with a
    /// cream burn line from 1.05 s, gone by 1.8 s. A contact: one big crest (1.6 - 2 x) rears up behind the body and topples over it. Look only: never moves a gameplay object.
    /// The models are normalised at run time (height, pivot bottom-centre, the thin axis is the card's normal) so the import scale of the FBX does not matter. (r1)</summary>
    public sealed class SpellFxMeshWavesPart : MonoBehaviour, ISpellFxPart
    {
        [Serializable]
        public sealed class WaveModel
        {
            public string Name = "";
            public GameObject Model;
            public Texture2D Paint;
            [Tooltip("A low mound cluster (the front rows) instead of a hooked crest.")] public bool Low;
        }

        [SerializeField] Shader paintShader;
        [SerializeField] WaveModel[] models = new WaveModel[0];
        [SerializeField] float radius = 2.2f;
        [SerializeField] float lifeSeconds = 1.8f;
        [SerializeField, Tooltip("Cream hull outline width in metres (0 = off).")] float outlineWidth = 0.025f;
        [SerializeField, Tooltip("Extra yaw of every model about its own up axis (degrees), if an import faces the wrong way.")] float modelYaw = 0f;

        // ---- tunables (metres / seconds on the 1.8 s base timeline; lifeSeconds stretches the whole timeline) ----
        const float BaseLife = 1.8f;
        const float Sink = 0.05f;                                     // the cards stand this far below the cast point (uneven ground)
        const float RiseDur = 0.45f, StaggerMax = 0.4f;               // rise: starts at 0 .. 0.4 s, takes 0.45 s (ease-back)
        const float SinkA = 1.1f, SinkB = 1.7f, SinkDrop = 0.5f;     // the cards sink (by this fraction of their height) between these moments
        const float ErodeA = 1.05f, ErodeB = 1.78f;                   // and eat away between these (every card has its own small offset)
        const float FaceCentre = 0.4f;                                // how much a card faces the ring centre (the rest faces the camera side, -forward)
        const int CrestsA = 8, CrestsB = 2, Mounds = 6;               // 16 in all
        const float ArcA = 2.45f, ArcMound = 2.5f;                    // crests over theta = -ArcA .. ArcA (none on the camera-side arc)

        // ---- hit wave ----
        const int MaxHits = 2;
        const float HitH = 3.1f;                                      // 1.6 - 2 x the band
        const float HitRise = 0.22f, HitTopA = 0.28f, HitTopB = 0.95f, HitTopDeg = 82f;
        const float HitSinkA = 0.95f, HitSinkB = 1.4f, HitBehind = 0.75f;

        static readonly int TintId = Shader.PropertyToID("_Tint"), ErodeId = Shader.PropertyToID("_Erode"), MainTexId = Shader.PropertyToID("_MainTex"),
            OutlineWId = Shader.PropertyToID("_OutlineW"), SeedId = Shader.PropertyToID("_NoiseSeed");

        sealed class Inst
        {
            public Transform Root, Pivot;
            public Renderer[] Rends;
            public Material Mat;
            public Vector3 Foot;               // ground point (world)
            public float YawDeg, Height, Delay, TiltMax, SinkAt, ErodeAt, Seed, Wobble;
            public bool Low, Hit;
            public float T0, Dir;              // hit: start (timeline seconds), away-from-centre direction is stored in Foot / Face
            public Vector3 Face;               // horizontal facing (the card's +Z)
        }

        // ---- state ----
        Vector3 _o, _f, _r;
        float _R = 2.2f, _tScale = 1f;
        bool _dead;
        readonly List<Inst> _ring = new List<Inst>(24);
        readonly Inst[] _hitSlots = new Inst[MaxHits];
        int _hitCount;
        readonly Dictionary<int, Bounds> _fit = new Dictionary<int, Bounds>();     // model index -> local bounds of the model under an identity pivot
        readonly Dictionary<int, bool> _turn = new Dictionary<int, bool>();        // model index -> thin axis is X (the card needs a 90 deg turn)
        readonly List<Material> _mats = new List<Material>(24);

        // ---------------------------------------------------------------- small maths
        static float Sm(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float EaseBack(float t) { t = Mathf.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f); }
        static float Back(float th) => Sm(0.55f, 1f, Mathf.Abs(th) / Mathf.PI);
        Vector3 Dir(float th) => _f * Mathf.Cos(th) + _r * Mathf.Sin(th);

        // ---------------------------------------------------------------- ISpellFxPart
        public void OnBegin(SpellFxCast cast)
        {
            _o = cast.Origin; _f = cast.Forward.sqrMagnitude > 1e-6f ? cast.Forward : Vector3.forward; _f.y = 0f;
            _f = _f.sqrMagnitude > 1e-6f ? _f.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            _R = cast.Radius > 0.5f ? cast.Radius : radius;
            _tScale = BaseLife / Mathf.Max(0.3f, lifeSeconds);
            var sh = paintShader != null ? paintShader : Shader.Find("Oheangbu/SpellFx/MeshPaint");
            if (sh == null) { Debug.LogWarning("SpellFxMeshWavesPart: shader Oheangbu/SpellFx/MeshPaint not found; nothing is drawn."); _dead = true; return; }
            if (models == null || models.Length == 0 || models[0] == null || models[0].Model == null) { Debug.LogWarning("SpellFxMeshWavesPart: no wave models; nothing is drawn."); _dead = true; return; }

            var crestIdx = new List<int>(); var lowIdx = new List<int>();
            for (int i = 0; i < models.Length; i++) { var m = models[i]; if (m == null || m.Model == null) continue; (m.Low ? lowIdx : crestIdx).Add(i); }
            if (crestIdx.Count == 0) crestIdx.Add(0);
            if (lowIdx.Count == 0) lowIdx.AddRange(crestIdx);

            var rnd = new System.Random((int)(cast.CastId * 7919 + 77));
            float Rnd() => (float)rnd.NextDouble();
            float yawFlip = (cast.CastId & 1) == 0 ? 1f : -1f;

            // row A: the hooked crests at the ring
            for (int k = 0; k < CrestsA; k++)
            {
                float th = Mathf.Lerp(-ArcA, ArcA, k / (CrestsA - 1f)) + (Rnd() - 0.5f) * 0.14f;
                int mi = crestIdx[(k + (Rnd() > 0.7f ? 1 : 0)) % crestIdx.Count];
                float h = Mathf.Lerp(1.45f, 1.95f, Rnd()) * (1f - 0.35f * Back(th));
                Add(sh, mi, th, _R + (Rnd() - 0.5f) * 0.2f, h, Rnd() > 0.5f, StaggerMax * Mathf.Clamp01(0.15f + 0.85f * Rnd()), Mathf.Lerp(12f, 22f, Rnd()), false, Rnd(), yawFlip);
            }
            // row B: two taller ones behind (further out)
            float[] thB = { -0.95f, 0.75f };
            for (int k = 0; k < CrestsB; k++)
            {
                int mi = crestIdx[(k + 1) % crestIdx.Count];
                Add(sh, mi, thB[k % thB.Length] + (Rnd() - 0.5f) * 0.2f, _R + 0.75f, Mathf.Lerp(2.1f, 2.4f, Rnd()), Rnd() > 0.5f, StaggerMax * (0.5f + 0.5f * Rnd()), Mathf.Lerp(14f, 22f, Rnd()), false, Rnd(), yawFlip);
            }
            // low mounds in front of the crests' feet (the foot of the wall)
            for (int k = 0; k < Mounds; k++)
            {
                float th = Mathf.Lerp(-ArcMound, ArcMound, (k + 0.5f) / Mounds) + (Rnd() - 0.5f) * 0.12f;
                int mi = lowIdx[k % lowIdx.Count];
                Add(sh, mi, th, _R - 0.12f, Mathf.Lerp(0.55f, 0.75f, Rnd()) * (1f - 0.3f * Back(th)), Rnd() > 0.5f, StaggerMax * Mathf.Clamp01(0.05f + 0.7f * Rnd()), Mathf.Lerp(3f, 7f, Rnd()), true, Rnd(), yawFlip);
            }
            // the hit waves are built now (hidden) so a contact costs nothing
            for (int s = 0; s < MaxHits; s++)
            {
                var h = MakeInst(sh, crestIdx[s % crestIdx.Count], HitH, Rnd() > 0.5f, false, Rnd());
                h.Hit = true; h.Root.gameObject.SetActive(false); _hitSlots[s] = h;
            }
            VfxAfterFog308.Route(transform);          // transparent-range queue: the main look's fog post would otherwise paint the cards dark
        }

        public void OnTick(SpellFxCast cast, float age)
        {
            if (_dead) return;
            float t = age * _tScale;
            foreach (var it in _ring) Pose(it, t);
            for (int s = 0; s < _hitCount; s++) PoseHit(_hitSlots[s], t);
        }

        public void OnHit(SpellFxCast cast, SpellFxHit hit)
        {
            if (_dead || _hitCount >= MaxHits) return;
            var it = _hitSlots[_hitCount];
            if (it == null) return;
            Vector3 feet = hit.Feet.sqrMagnitude > 1e-6f ? hit.Feet : new Vector3(hit.Position.x, _o.y, hit.Position.z);
            Vector3 rel = feet - _o; rel.y = 0f;
            Vector3 outward = rel.sqrMagnitude > 1e-4f ? rel.normalized : _f;
            it.Face = -outward;                                                                                // the card looks at the caster; it topples toward them, over the body
            it.Foot = new Vector3(feet.x, _o.y - Sink, feet.z) + outward * (hit.TargetRadius * 0.5f + HitBehind);
            it.T0 = Mathf.Max(0f, cast.AgeAt(hit.NowUs) * _tScale);
            it.Root.gameObject.SetActive(true);
            SetShown(it, false);
            _hitCount++;
        }

        public void OnEnd(SpellFxCast cast, SpellFxEndReason reason)
        {
            if (reason != SpellFxEndReason.Aborted) return;        // Done / TimedOut: the 1.8 s animation plays out through the player's tail
            _dead = true;
            foreach (var it in _ring) SetShown(it, false);
            foreach (var it in _hitSlots) if (it != null) SetShown(it, false);
        }

        // ---------------------------------------------------------------- the cards
        void Add(Shader sh, int modelIndex, float th, float ringR, float height, bool mirror, float delay, float tiltMax, bool low, float seed, float yawFlip)
        {
            var it = MakeInst(sh, modelIndex, height, mirror, low, seed);
            Vector3 dir = Dir(th);
            it.Foot = _o + dir * ringR; it.Foot.y = _o.y - Sink;
            Vector3 face = Vector3.Lerp(-dir, -_f, 1f - FaceCentre); face.y = 0f;
            it.Face = face.sqrMagnitude > 1e-4f ? face.normalized : -_f;
            it.YawDeg = (seed - 0.5f) * 24f;
            it.Delay = delay; it.TiltMax = tiltMax;
            it.SinkAt = SinkA + 0.12f * seed; it.ErodeAt = ErodeA + 0.1f * (1f - seed);
            it.Wobble = yawFlip;
            _ring.Add(it);
            SetShown(it, false);
        }

        Inst MakeInst(Shader sh, int modelIndex, float height, bool mirror, bool low, float seed)
        {
            var m = models[modelIndex];
            var it = new Inst { Height = height, Low = low, Seed = seed };
            var root = new GameObject("Wave" + _ring.Count + (low ? "m" : "c")).transform;
            root.SetParent(transform, false);
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(root, false);
            var inst = Instantiate(m.Model, pivot, false);
            inst.name = m.Name.Length > 0 ? m.Name : m.Model.name;
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            it.Root = root; it.Pivot = pivot;
            it.Rends = inst.GetComponentsInChildren<Renderer>(true);

            // normalise once per model: local bounds under the identity pivot -> bottom-centre pivot, the thin axis is the card's normal
            if (!_fit.TryGetValue(modelIndex, out Bounds lb))
            {
                lb = LocalBounds(pivot, inst);
                _fit[modelIndex] = lb;
                _turn[modelIndex] = lb.size.x < lb.size.z;
            }
            float s = lb.size.y > 1e-4f ? height / lb.size.y : 1f;
            inst.transform.localPosition = new Vector3(-lb.center.x, -lb.min.y, -lb.center.z);
            float turn = (_turn[modelIndex] ? 90f : 0f) + modelYaw;
            pivot.localRotation = Quaternion.Euler(0f, turn, 0f);
            pivot.localScale = new Vector3(mirror ? -s : s, s, s);

            it.Mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            it.Mat.SetColor(TintId, Color.white);
            it.Mat.SetFloat(ErodeId, 0f); it.Mat.SetFloat(OutlineWId, outlineWidth);
            it.Mat.SetFloat(SeedId, seed * 31f);
            if (m.Paint != null) it.Mat.SetTexture(MainTexId, m.Paint);
            _mats.Add(it.Mat);
            foreach (var r in it.Rends)
            {
                r.sharedMaterial = it.Mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            return it;
        }

        /// <summary>The model's bounds in <paramref name="space"/>'s frame (every mesh's eight corners through its own transform chain).</summary>
        static Bounds LocalBounds(Transform space, GameObject model)
        {
            bool any = false; Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh; if (mesh == null) continue;
                Bounds mb = mesh.bounds; Vector3 c = mb.center, e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(new Vector3(0f, 0.5f, 0f), Vector3.one);
            return b;
        }

        static void SetShown(Inst it, bool on)
        {
            if (it == null || it.Rends == null) return;
            foreach (var r in it.Rends) if (r != null && r.enabled != on) r.enabled = on;
        }

        void Pose(Inst it, float t)
        {
            float a = t - it.Delay;
            float rise = a > 0f ? EaseBack(a / RiseDur) : 0f;
            float sinkP = Sm(it.SinkAt, SinkB, t);
            float erode = Sm(it.ErodeAt, ErodeB, t);
            bool show = a > 0.01f && erode < 0.995f && t < BaseLife + 0.05f;
            SetShown(it, show);
            if (!show) return;
            float hgt = it.Height;
            float y = hgt * (rise - 1f) - sinkP * SinkDrop * hgt;                                           // below the ground at first; the overshoot goes a little above the rest
            float curl = it.TiltMax * Sm(0f, 0.5f, a) + (it.Low ? 0f : 7f * Mathf.Sin(a * 6.5f + it.Seed * 6.28f) * (1f - Sm(0.5f, 1.2f, a))) + 12f * sinkP * (it.Low ? 0.3f : 1f);
            float sway = 3.5f * Mathf.Sin(t * 3f + it.Seed * 6.28f) * it.Wobble;
            Quaternion yaw = Quaternion.LookRotation(it.Face, Vector3.up) * Quaternion.Euler(0f, it.YawDeg + sway, 0f);
            it.Root.SetPositionAndRotation(it.Foot + Vector3.up * y, yaw * Quaternion.Euler(curl, 0f, 0f));      // +x tilts the top toward the card's front (the curl)
            float pop = Mathf.Lerp(0.72f, 1f, Mathf.Min(1f, rise));
            it.Root.localScale = new Vector3(pop, 1f, pop);
            it.Mat.SetFloat(ErodeId, erode);
        }

        void PoseHit(Inst it, float t)
        {
            if (it == null) return;
            float a = t - it.T0;
            float ramp = a > 0f ? Mathf.Min(1.06f, EaseBack(a / HitRise)) : 0f;
            float sinkP = Sm(HitSinkA, HitSinkB, a);
            bool show = a > 0.005f && sinkP < 0.995f;
            SetShown(it, show);
            if (!show) return;
            float y = it.Height * (ramp - 1f) - sinkP * 0.5f * it.Height;
            float tilt = 8f * Mathf.Min(1f, ramp) + HitTopDeg * Sm(HitTopA, HitTopB, a);                    // rears up, then the crest topples over the target
            Quaternion yaw = Quaternion.LookRotation(it.Face, Vector3.up);
            it.Root.SetPositionAndRotation(it.Foot + Vector3.up * y, yaw * Quaternion.Euler(tilt, 0f, 0f));
            it.Root.localScale = Vector3.one;
            it.Mat.SetFloat(ErodeId, Sm(HitSinkA - 0.05f, HitSinkB, a));
        }

        void OnDestroy()
        {
            foreach (var m in _mats) if (m != null) Destroy(m);
            _mats.Clear();
        }
    }
}
