using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    // 2026-10-30 hex-cell kit: the pieces the ㅗ fan (SpellFxHexFanPart) and the ㅜ ring (SpellFxHexRingPart) share, taken out of the fan part. Nothing here is a singleton and nothing
    // here holds state between casts: the static members are immutable tables and pure helpers; everything that changes lives in instances a part owns.
    // Shader contract (Oheangbu/SpellFx/HexDome, unchanged): uv0 = the pattern uv in the cell, uv1 = (cell hash, 0 centre .. 1 border), uv2 = (pattern 0..7, height 0..1), uv3 = the cell's
    // centre in object space. The shader reveals by the VERTEX object-space y (`_RiseTime`), draws a bright "base ring" where that y is ~0 (`exp(-yOS / 0.03)`, a dome's foot) and
    // discards y < 0, so every piece of geometry must sit at an object-space y well above 0: see <see cref="HexFrame"/>.
    //
    // Plan coordinates everywhere: x right of the caster, y up from the cast origin, z ahead (the aim).

    /// <summary>Plan -> object space of one renderer. <see cref="Lying"/> is the fan's sheet (the object is laid on its side so the shader's reveal runs ahead: object y = metres ahead / sy,
    /// object z = down); <see cref="UprightSunk"/> is a world-aligned object whose origin is <c>sink</c> metres BELOW the cast origin, so every vertex has object y >= sink (the shader's base
    /// ring term is 0 and nothing is discarded): the air layer (brush-thrown cells) and the whole ring.</summary>
    public sealed class HexFrame
    {
        public readonly Vector3 Origin, Right, Fwd;
        readonly bool _lying;
        readonly float _sy, _sink, _zOff;

        HexFrame(Vector3 origin, Vector3 right, Vector3 fwd, bool lying, float sy, float sink, float zOff)
        { Origin = origin; Right = right; Fwd = fwd; _lying = lying; _sy = sy; _sink = sink; _zOff = zOff; }

        /// <summary>The fan sheet (the part's old build): local x = right, local y = (ahead + <paramref name="zOff"/>) / <paramref name="sy"/>, local z = down; the object is scaled (1, sy, 1) and sits
        /// <paramref name="zOff"/> metres behind the caster, so nothing moves in the world. The offset keeps the object-space y of anything near the caster (a body standing at its side, whose hold cells
        /// sit at plan z ~ 0) above the shader's base-ring term (exp(-y / 0.03) is 0.003 at y = 0.17); the fan's reveal times shift by at most 0.01 s.</summary>
        public static HexFrame Lying(Vector3 origin, Vector3 right, Vector3 fwd, float sy, float zOff = 1f) => new HexFrame(origin, right, fwd, true, sy, 0f, zOff);
        public static HexFrame UprightSunk(Vector3 origin, Vector3 right, Vector3 fwd, float sink = 1f) => new HexFrame(origin, right, fwd, false, 1f, sink, 0f);

        public Vector3 ObjectPosition => _lying ? Origin - Fwd * _zOff : Origin - Vector3.up * _sink;
        public Quaternion ObjectRotation => _lying ? Quaternion.LookRotation(Vector3.down, Fwd) : Quaternion.identity;
        public Vector3 ObjectScale => _lying ? new Vector3(1f, _sy, 1f) : Vector3.one;

        public Vector3 PlanW(Vector3 p) => Origin + Right * p.x + Vector3.up * p.y + Fwd * p.z;
        public Vector3 ToPlan(Vector3 w) { Vector3 r = w - Origin; return new Vector3(Vector3.Dot(r, Right), r.y, Vector3.Dot(r, Fwd)); }

        public Vector3 ToLocal(Vector3 p)
        {
            if (_lying) return new Vector3(p.x, Mathf.Max(p.z + _zOff, 0.001f) / _sy, -p.y);
            return Right * p.x + Fwd * p.z + Vector3.up * (p.y + _sink);
        }

        /// <summary>The object-space normal that the object's inverse-transpose turns into the plan normal <paramref name="w"/> (a non-uniform scale needs it pre-multiplied).</summary>
        public Vector3 NormalLocal(Vector3 w)
        {
            if (_lying) return new Vector3(w.x, w.z * _sy, -w.y).normalized;
            return (Right * w.x + Vector3.up * w.y + Fwd * w.z).normalized;
        }
    }

    /// <summary>One renderer: a dynamic mesh of cells rebuilt every tick, its own material instance and its own clock.</summary>
    public sealed class HexLayer
    {
        static readonly int AgeId = Shader.PropertyToID("_Age"), FadeId = Shader.PropertyToID("_Fade"), RiseId = Shader.PropertyToID("_RiseTime"),
            HitPosId = Shader.PropertyToID("_HitPos"), HitAgeId = Shader.PropertyToID("_HitAge"), HitCellId = Shader.PropertyToID("_HitCell"),
            LineId = Shader.PropertyToID("_Line"), PatternId = Shader.PropertyToID("_Pattern"), GlassId = Shader.PropertyToID("_Glass");

        public readonly GameObject Go;
        public readonly HexFrame Frame;
        public readonly Material Mat;
        /// <summary>Scratch for the six corners of a cell (plan space).</summary>
        public readonly Vector3[] Cs = new Vector3[6];

        readonly Mesh _mesh;
        readonly List<Vector3> _v = new List<Vector3>(), _n = new List<Vector3>(), _c = new List<Vector3>();
        readonly List<Vector2> _uv0 = new List<Vector2>(), _uv1 = new List<Vector2>(), _uv2 = new List<Vector2>();
        readonly List<int> _t = new List<int>();
        Color _lineBase, _patBase, _glassBase;
        readonly Color _rim;

        public HexLayer(Transform parent, string name, Material source, HexFrame frame, float riseTime)
        {
            Frame = frame;
            Go = new GameObject(name);
            Go.transform.SetParent(parent, false);
            Go.transform.SetPositionAndRotation(frame.ObjectPosition, frame.ObjectRotation);
            Go.transform.localScale = frame.ObjectScale;
            int layerId = VfxAfterFog308.Layer;
            if (layerId >= 0) Go.layer = layerId;
            _mesh = new Mesh { name = name }; _mesh.MarkDynamic();
            Go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = Go.AddComponent<MeshRenderer>();
            Mat = new Material(source);
            r.sharedMaterial = Mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            Mat.SetFloat(RiseId, riseTime);
            Mat.SetFloat(AgeId, -1f); Mat.SetFloat(FadeId, 0f); Mat.SetFloat(HitAgeId, -1f);
            _lineBase = Mat.HasProperty(LineId) ? Mat.GetColor(LineId) : Color.white;
            _patBase = Mat.HasProperty(PatternId) ? Mat.GetColor(PatternId) : Color.white;
            _glassBase = Mat.HasProperty(GlassId) ? Mat.GetColor(GlassId) : Color.clear;
            _rim = Mat.HasProperty("_Rim") ? Mat.GetColor("_Rim") : new Color(0.2f, 0.42f, 0.32f, 1f);
            Go.SetActive(false);
        }

        public void Destroy()
        {
            if (Mat != null) UnityEngine.Object.Destroy(Mat);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
        }

        // ------------------------------------------------------------ the material's clock

        public void SetClock(float age, float fade) { Mat.SetFloat(AgeId, age); Mat.SetFloat(FadeId, fade); }

        /// <summary>The breath while it stands: 2 Hz, +- 10 % on the seam, the pattern and the fill.</summary>
        public void Breathe(float a)
        {
            float k = 1f + 0.1f * Mathf.Sin(a * Mathf.PI * 2f * 2f);
            Mat.SetColor(LineId, new Color(_lineBase.r * k, _lineBase.g * k, _lineBase.b * k, _lineBase.a));
            Mat.SetColor(PatternId, new Color(_patBase.r * k, _patBase.g * k, _patBase.b * k, _patBase.a));
            if (Mat.HasProperty(GlassId)) Mat.SetColor(GlassId, new Color(_glassBase.r, _glassBase.g, _glassBase.b, Mathf.Clamp01(_glassBase.a * k)));
        }

        public void SetHit(Vector3 world, float hitAge)
        {
            Mat.SetVector(HitPosId, world);
            Mat.SetVector(HitCellId, new Vector4(world.x, world.y, world.z, 1f));
            Mat.SetFloat(HitAgeId, hitAge);
        }

        public void ClearHit() { Mat.SetFloat(HitAgeId, -1f); }

        public Color LineColor => _lineBase;

        /// <summary>The body's own edge colour (the material's _Rim: wood jade, fire orange, earth ochre, metal silver, water blue; every body's _Line is the same cream): what torn plates are tinted with.</summary>
        public Color RimColor => new Color(Mathf.Min(1f, _rim.r), Mathf.Min(1f, _rim.g), Mathf.Min(1f, _rim.b), 1f);

        // ------------------------------------------------------------ mesh

        public void Begin() { _v.Clear(); _n.Clear(); _c.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _t.Clear(); }

        public void Flush()
        {
            _mesh.Clear();
            _mesh.SetVertices(_v); _mesh.SetNormals(_n);
            _mesh.SetUVs(0, _uv0); _mesh.SetUVs(1, _uv1); _mesh.SetUVs(2, _uv2); _mesh.SetUVs(3, _c);
            _mesh.SetTriangles(_t, 0);
            _mesh.RecalculateBounds();
        }

        /// <summary>One hexagon cell as a fan of six triangles: centre vertex edge 0, corner vertices edge 1 (the dome's own build). <paramref name="faceN"/> picks the winding
        /// (the front face is the one it looks at); <paramref name="shadeN"/> is the shading normal (plan space). Drawn as TWO layers (<paramref name="dbl"/>): the second has hash + 1/13.7 - the
        /// same bloom period, half a period out - so a pattern is lit in one of them most of the time (the shader blooms each pattern only sporadically).</summary>
        public void EmitCell(Vector3 ctr, Vector3[] cs, float hash, int pat, float d01, Vector3 faceN, Vector3 shadeN, Vector3 ctrPlan, bool dbl = true)
        {
            Vector3 sn = Frame.NormalLocal(shadeN), cl = Frame.ToLocal(ctrPlan);
            bool flip = Vector3.Dot(Vector3.Cross(cs[1] - ctr, cs[0] - ctr), faceN) < 0f;
            for (int layer = 0; layer < (dbl ? 2 : 1); layer++)
            {
                float h = layer == 0 ? hash : hash + SpellFxHexKit.HashStep;
                int b = _v.Count;
                _v.Add(Frame.ToLocal(ctr)); _n.Add(sn); _uv0.Add(new Vector2(0.5f, 0.5f)); _uv1.Add(new Vector2(h, 0f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
                for (int k = 0; k < 6; k++)
                {
                    _v.Add(Frame.ToLocal(cs[k])); _n.Add(sn); _uv0.Add(SpellFxHexKit.Uv6[k]); _uv1.Add(new Vector2(h, 1f)); _uv2.Add(new Vector2(pat, d01)); _c.Add(cl);
                }
                for (int k = 0; k < 6; k++)
                {
                    int p = b + 1 + k, q = b + 1 + (k + 1) % 6;
                    if (!flip) { _t.Add(b); _t.Add(q); _t.Add(p); } else { _t.Add(b); _t.Add(p); _t.Add(q); }
                }
            }
        }

        /// <summary>The ribbon: a strip along <paramref name="pts"/> (plan x, z) with the outward direction <paramref name="nrm"/> at each point, <paramref name="w"/> wide, lying on the ground
        /// (<paramref name="heightAt"/> = plan y at a plan point). Its vertices carry edge = 1, so the shader draws the seam's colour (cream, a jade bleed) with no fill pattern.
        /// <paramref name="reach"/> only feeds the uv2 height of the vertices (plan z / reach, 0.3 .. 1).</summary>
        public void Strip(Vector2[] pts, Vector2[] nrm, float w, Func<Vector2, float> heightAt, Vector3 shadeN, float reach)
        {
            Vector3 sh = Frame.NormalLocal(shadeN);
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector3 a0 = RibbonPt(pts[i], nrm[i], -w * 0.5f, heightAt), b0 = RibbonPt(pts[i], nrm[i], w * 0.5f, heightAt);
                Vector3 a1 = RibbonPt(pts[i + 1], nrm[i + 1], -w * 0.5f, heightAt), b1 = RibbonPt(pts[i + 1], nrm[i + 1], w * 0.5f, heightAt);
                QuadVerts(a0, b0, a1, b1, sh, reach, false);
            }
        }

        /// <summary>One quad of a strip: corners (a0, b0) at the start and (a1, b1) at the end, plan space. <paramref name="ink"/> draws it as plain dark glass (edge 0, no pattern) instead of the seam.
        /// <paramref name="faceN"/> = the side that should draw as the front (default up: a strip on the ground; a standing thread passes the camera's side).</summary>
        public void QuadVerts(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, Vector3 shadeLocal, float reach, bool ink, Vector3? faceN = null)
        {
            int b = _v.Count;
            Vert(a0, shadeLocal, reach, ink); Vert(b0, shadeLocal, reach, ink); Vert(a1, shadeLocal, reach, ink); Vert(b1, shadeLocal, reach, ink);
            bool up = Vector3.Dot(Vector3.Cross(b0 - a0, a1 - a0), faceN ?? Vector3.up) > 0f;          // the triangle (a0, b0, a1) faces the front side when its cross product points that way
            if (up) { _t.Add(b); _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 1); _t.Add(b + 3); _t.Add(b + 2); }
            else { _t.Add(b); _t.Add(b + 2); _t.Add(b + 1); _t.Add(b + 1); _t.Add(b + 2); _t.Add(b + 3); }
        }

        /// <summary>Plan point <paramref name="p"/> pushed <paramref name="off"/> along <paramref name="nrm"/>, lying on the ground.</summary>
        static Vector3 RibbonPt(Vector2 p, Vector2 nrm, float off, Func<Vector2, float> heightAt)
        {
            Vector2 q = p + nrm * off;
            return new Vector3(q.x, heightAt(p), q.y);
        }

        void Vert(Vector3 plan, Vector3 shadeLocal, float reach, bool ink)
        {
            _v.Add(Frame.ToLocal(plan)); _n.Add(shadeLocal);
            _uv0.Add(ink ? new Vector2(0f, 0f) : new Vector2(0.5f, 0.5f));          // uv0 in a corner: the pattern's circle clip is 0 there - plain glass
            _uv1.Add(ink ? new Vector2(0.3f, 0f) : new Vector2(0.3f, 1f));
            _uv2.Add(new Vector2(SpellFxHexKit.PatChoices[0], Mathf.Clamp(plan.z / Mathf.Max(reach, 0.1f), 0.3f, 1f))); _c.Add(Frame.ToLocal(plan));
        }
    }

    /// <summary>Ground height under a plan point: one ray down (once), bodies and steep faces ignored; a 0.2 m lattice cache for the ribbons.</summary>
    public sealed class HexGround
    {
        readonly HexFrame _f;
        readonly bool _follow;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly Dictionary<long, float> _cache = new Dictionary<long, float>();

        public HexGround(HexFrame frame, bool follow) { _f = frame; _follow = follow; }

        public float At(Vector2 p)
        {
            if (!_follow) return 0f;
            Vector3 from = _f.Origin + _f.Right * p.x + _f.Fwd * p.y + Vector3.up * 1.2f;
            int n = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.collider.attachedRigidbody != null || h.collider is CharacterController || h.normal.y < 0.5f) continue;       // bodies are not ground
                best = Mathf.Max(best, h.point.y - _f.Origin.y);
            }
            return best > float.NegativeInfinity ? Mathf.Clamp(best, -1f, 1f) : 0f;
        }

        /// <summary>The ribbon's ground, sampled on a 0.2 m lattice and kept.</summary>
        public float Q(Vector2 p)
        {
            if (!_follow) return 0f;
            int qx = Mathf.RoundToInt(p.x / 0.2f), qz = Mathf.RoundToInt(p.y / 0.2f);
            long key = SpellFxHexKit.Key(qx, qz);
            if (!_cache.TryGetValue(key, out float g)) { g = At(new Vector2(qx * 0.2f, qz * 0.2f)); _cache[key] = g; }
            return g;
        }
    }

    /// <summary>A standing hexagon: a 곡 stele (grows up from the ground), a hold cell on a body (grows from its centre) or a ring pillar's wall. Dir = the horizontal direction it faces (plan x, z).</summary>
    public sealed class HexPlate
    {
        public Vector3 Ctr;                 // plan x, y (relative to the cast origin), z; a sprout's Ctr.y is its foot
        public Vector2 Dir;
        public float Rx, Rv;                // horizontal / vertical corner radius at full size
        public float Start, Grow = 0.2f, LeaveAt = float.MaxValue;
        public float Hash, D01; public int Pat; public bool Sprout;
    }

    /// <summary>The struck body as the hold needs it: its axis and height range in plan coordinates (x right, y up from the origin, z ahead), its radius, and the colliders to ray.</summary>
    public sealed class HexBody
    {
        public Vector2 Feet, Centre;
        public float Ground, Y0, Y1, Radius;
        public Collider[] Cols;
    }

    /// <summary>A cell thrown from the brush tip (or sucked into a cell that is made there): drawn on the air layer. <see cref="Quick"/> = no standing at the tip: it leaves at once (ease-out) and lies
    /// down in the first half of the flight (the seeds, thrown one after another).</summary>
    public sealed class HexFlight
    {
        public Vector3 From, To; public float T0, T1, Rc, Arc, Hash; public bool Sampled, TipInk, Quick;
        /// <summary>It stays standing facing the camera all the way (a cell that is to float, or to stand, where it lands): no tipping flat.</summary>
        public bool Standing;
    }

    /// <summary>The cast's cells as one drawn thing: a frame, a main layer (the sheet itself), an air layer (brush-thrown cells and the Once preview, with a clock of its own), the ground, the
    /// camera lean. The part owns it; nothing is shared between casts.</summary>
    public sealed class HexSheet
    {
        public readonly SpellFxCast Cast;
        public readonly HexFrame Frame;
        public readonly HexLayer Main, Air;
        public readonly HexGround Ground;
        /// <summary>Shading normal leaned toward the camera so the shader's |n.v| gates (noNew 0.2 - 0.35, facing 0.45 - 0.95) stay open at a low view.</summary>
        public readonly Vector3 Tilt;
        /// <summary>The side a standing plate's front face is wound to (the camera's), so it always draws as the strong front pass.</summary>
        public readonly Vector3 FaceCam;
        /// <summary>Plan x, z of the horizontal direction toward the camera.</summary>
        public readonly Vector2 ToCamPlan;
        /// <summary>The brush tip in plan coordinates (x right, y up from the origin, z ahead), sampled again at every throw's first frame.</summary>
        public Vector3 TipPlan;
        /// <summary>Time driving the sheet's slow breathing wave (the part sets it every tick).</summary>
        public float Age;
        public float Breath = 0.06f;
        public float Lift = 0.03f;

        readonly Camera _cam;

        public HexSheet(Transform parent, SpellFxCast cast, Material material, HexFrame mainFrame, float mainRise, bool followGround, string name)
        {
            Cast = cast;
            Frame = mainFrame;
            // the camera sits behind the caster: lean the cells' shading normal toward it (only the shading; the geometry stays flat)
            var cam = Camera.main;
            _cam = cam;
            Vector3 toCam = cam != null ? cam.transform.position - Frame.Origin : -Frame.Fwd; toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f) toCam = -Frame.Fwd;
            toCam.Normalize();
            Vector2 tc = new Vector2(Vector3.Dot(toCam, Frame.Right), Vector3.Dot(toCam, Frame.Fwd));
            ToCamPlan = tc;
            Tilt = new Vector3(tc.x * 0.35f, 0.937f, tc.y * 0.35f).normalized;      // lean 0.35 (was 0.5): |n.v| ~0.55 - 0.77 at a 13 - 30 deg view keeps the pattern gates open but leaves the fill (sil) some body
            FaceCam = new Vector3(tc.x, 0.15f, tc.y);
            Ground = new HexGround(Frame, followGround);
            Main = new HexLayer(parent, name, material, Frame, mainRise);
            // the air layer: a world-aligned object 1 m below the caster (object y >= 1: no base ring term, nothing discarded) with a material of its own and _RiseTime 0 (the shader's own reveal is a plain 0.16 s fade-in)
            Air = new HexLayer(parent, name + "Air", material, HexFrame.UprightSunk(Frame.Origin, Frame.Right, Frame.Fwd, 1f), 0f);
            TipPlan = SampleTipPlan();
        }

        public void Destroy() { Main.Destroy(); Air.Destroy(); }
        public void SetActive(bool on) { Main.Go.SetActive(on); Air.Go.SetActive(on); }
        public Vector3 PlanW(Vector3 p) => Frame.PlanW(p);
        public Vector3 ToPlan(Vector3 w) => Frame.ToPlan(w);

        /// <summary>A plan point in world space the way the main layer's transform places it (the same as <see cref="PlanW"/> except for the lying frame's clamp at the caster).</summary>
        public Vector3 WorldOf(Vector3 plan) => Main.Go.transform.TransformPoint(Frame.ToLocal(plan));

        /// <summary>The brush tip now, in plan coordinates: the rig's real tip when there is one and it is plausible (within 3 m of the caster, above the knees),
        /// else the caster + 0.32 m right + 1.25 m up + 0.6 m ahead (where the swing's brush hand is); never above 1.75 m over the feet.</summary>
        public Vector3 SampleTipPlan()
        {
            Vector3 origin = Frame.Origin;
            Vector3 fallback = origin + Frame.Right * 0.32f + Vector3.up * 1.25f + Frame.Fwd * 0.6f;
            Vector3? tip = Cast != null && Cast.BrushTip != null ? Cast.BrushTip() : null;
            Vector3 w = tip ?? fallback;
            Vector3 d = w - origin;
            if (d.sqrMagnitude > 9f || d.y < 0.3f) w = fallback;
            w.y = Mathf.Min(w.y, origin.y + 1.75f);
            return Frame.ToPlan(w);
        }

        public void Begin() { Main.Begin(); Air.Begin(); }
        public void Flush() { Main.Flush(); Air.Flush(); }

        /// <summary>The sheet's slow wave: a function of position (shared corners stay shared), a ramp in over the first second.</summary>
        public float Wave(float x, float z)
        {
            float ramp = SpellFxHexKit.Smooth((Age - 0.25f) / 0.7f);
            float r = Mathf.Sqrt(x * x + z * z);
            float w = 0.5f + 0.5f * Mathf.Sin(r * 2.4f - Age * 2.4f + Mathf.Sin(x * 1.9f) * 0.9f);
            return Breath * ramp * w * w;
        }

        /// <summary>A hexagon lying on the ground at plan point <paramref name="p"/> (x, z), base height <paramref name="baseY"/>, circumradius <paramref name="rad"/>, turned <paramref name="yaw"/> radians.</summary>
        public void EmitFlat(Vector2 p, float baseY, float rad, float hash, int pat, float d01, bool wave = true, float yaw = 0f)
        {
            var cs = Main.Cs;
            float w0 = wave ? Wave(p.x, p.y) : 0f;
            var ctr = new Vector3(p.x, baseY + w0, p.y);
            for (int k = 0; k < 6; k++)
            {
                float c = yaw == 0f ? SpellFxHexKit.Cos6[k] : Mathf.Cos(k * Mathf.PI / 3f + yaw), s = yaw == 0f ? SpellFxHexKit.Sin6[k] : Mathf.Sin(k * Mathf.PI / 3f + yaw);
                float x = p.x + c * rad, z = p.y + s * rad;
                cs[k] = new Vector3(x, baseY + (wave ? Wave(x, z) : 0f), z);
            }
            Main.EmitCell(ctr, cs, hash, pat, d01, Vector3.up, Tilt, new Vector3(p.x, baseY, p.y));
        }

        /// <summary>A standing hexagon facing <see cref="HexPlate.Dir"/>: a stele sprouts up from its foot, a hold cell grows from its centre. Wound to the camera's side unless
        /// <paramref name="outward"/> (then to its own outward direction: a ring pillar's wall seen from behind draws as the dim back pass).</summary>
        public void EmitPlate(HexPlate p, float g, bool outward = false)
        {
            float rx, rv; Vector3 ctr = p.Ctr;
            if (p.Sprout) { rx = p.Rx; rv = p.Rv * g; ctr.y += 0.8660254f * rv; }
            else { rx = p.Rx * g; rv = p.Rv * g; }
            var u = new Vector3(p.Dir.y, 0f, -p.Dir.x);
            var cs = Main.Cs;
            for (int k = 0; k < 6; k++) cs[k] = ctr + u * (SpellFxHexKit.Cos6[k] * rx) + Vector3.up * (SpellFxHexKit.Sin6[k] * rv);
            Vector3 faceN = outward ? new Vector3(p.Dir.x, 0f, p.Dir.y) : FaceCam;
            Main.EmitCell(ctr, cs, p.Hash, p.Pat, p.D01, faceN, Tilt, ctr);
        }

        /// <summary>The shading normal of one standing cell (2026-10-30, the ring wall): the dome shader dims the fill of a cell that faces the camera (sil = lerp(0.25, 1, (1 - |n.v|)^1.5), glassK =
        /// lerp(1, 0.5, facing)), and a cell face-on to the shoulder camera has |n.v| ~ 0.9+ - a fill of ~0.1 (the F1 wire cage). So the shading normal is the geometric one turned about the view
        /// vector until |n.v| lies in 0.5 .. 0.75 (a normal already there is kept), whichever way the cell faces; the geometry is not touched. <paramref name="faceN"/> = the cell's geometric normal
        /// (plan space), <paramref name="ctrPlan"/> = its centre. (The fan's own plates keep the sheet-wide <see cref="Tilt"/>.)</summary>
        public Vector3 ShadeFor(Vector3 faceN, Vector3 ctrPlan)
        {
            Vector3 v = _cam != null ? Frame.ToPlan(_cam.transform.position) - ctrPlan : new Vector3(ToCamPlan.x, 0.3f, ToCamPlan.y);
            if (v.sqrMagnitude < 1e-6f) return Tilt;
            v.Normalize();
            Vector3 g = faceN.normalized;
            float d = Vector3.Dot(g, v), ad = Mathf.Abs(d);
            float c = Mathf.Clamp(ad, 0.5f, 0.75f);
            if (Mathf.Abs(c - ad) < 1e-4f) return g;
            Vector3 perp = g - d * v;
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(v, Vector3.up);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.right;
            perp.Normalize();
            return ((d < 0f ? -1f : 1f) * c * v + Mathf.Sqrt(1f - c * c) * perp).normalized;
        }

        /// <summary>A standing hexagon (double layer) with its centre at plan point <paramref name="ctr"/>, facing the horizontal direction <paramref name="dir"/> (plan x, z), leaning <paramref name="lean"/>
        /// radians (its top turned toward dir), horizontal / vertical corner radius <paramref name="rx"/> / <paramref name="rv"/> (flat top and bottom, corners left and right; rv = rx is a regular
        /// hexagon, 1.73 rv tall). Wound to the camera's side (it always draws as the strong front pass) with the per-cell shading normal of <see cref="ShadeFor"/>.</summary>
        public void EmitStanding(Vector3 ctr, Vector2 dir, float rx, float rv, float lean, float hash, int pat, float d01)
        {
            float cl = Mathf.Cos(lean), sl = Mathf.Sin(lean);
            Vector3 n3 = new Vector3(dir.x, 0f, dir.y), u = new Vector3(dir.y, 0f, -dir.x), vp = Vector3.up * cl + n3 * sl;
            var cs = Main.Cs;
            for (int k = 0; k < 6; k++) cs[k] = ctr + u * (SpellFxHexKit.Cos6[k] * rx) + vp * (SpellFxHexKit.Sin6[k] * rv);
            Vector3 faceN = n3 * cl - Vector3.up * sl;
            Vector3 camDir = new Vector3(ToCamPlan.x, 0.15f, ToCamPlan.y);
            Vector3 wind = Vector3.Dot(faceN, camDir) >= 0f ? faceN : -faceN;
            Main.EmitCell(ctr, cs, hash, pat, d01, wind, ShadeFor(faceN, ctr), ctr);
        }

        /// <summary>The throw's first frame: the tip where it is NOW becomes this flight's start, and a small ink spit leaves there.</summary>
        public void SampleFlight(HexFlight f)
        {
            f.Sampled = true;
            f.From = SampleTipPlan();
            TipPlan = f.From;
            if (f.TipInk) SpellFxHexKit.Spit(PlanW(f.From), (Frame.Fwd * 0.6f + Vector3.up).normalized);
        }

        /// <summary>A thrown cell in flight (one layer, on the air layer): from the brush tip along a low arc to its place. It leaves the hand standing, facing the camera, and tips flat as it lands.
        /// A <see cref="HexFlight.Quick"/> one has no pause in the hand: ease-out, flat by the middle of the flight.</summary>
        public void EmitFlight(HexFlight f, float a)
        {
            float u = Mathf.Clamp01((a - f.T0) / Mathf.Max(f.T1 - f.T0, 1e-3f));
            float ue = f.Quick ? 1f - (1f - u) * (1f - u) : SpellFxHexKit.Smooth(u);
            Vector3 p = Vector3.Lerp(f.From, f.To, ue) + Vector3.up * (f.Arc * Mathf.Sin(Mathf.PI * u));
            float rc = f.Rc * (0.8f + 0.2f * SpellFxHexKit.Smooth(u * 3f));
            float tilt = f.Standing ? 1f : 1f - SpellFxHexKit.Smooth(Mathf.Clamp01(u * (f.Quick ? 2.6f : 1.15f)));          // 1 = standing facing the camera, 0 = flat
            EmitPose(p, rc, tilt, f.Hash);
        }

        /// <summary>One cell on the air layer at plan point <paramref name="p"/>, radius <paramref name="rc"/>, <paramref name="tilt"/> 1 = standing facing the camera .. 0 = flat.</summary>
        public void EmitPose(Vector3 p, float rc, float tilt, float hash)
        {
            var across = new Vector3(ToCamPlan.y, 0f, -ToCamPlan.x);                   // horizontal, square to the camera direction
            var cs = Air.Cs;
            for (int k = 0; k < 6; k++)
            {
                Vector3 flat = new Vector3(SpellFxHexKit.Cos6[k] * rc, 0f, SpellFxHexKit.Sin6[k] * rc), stand = across * (SpellFxHexKit.Cos6[k] * rc) + Vector3.up * (SpellFxHexKit.Sin6[k] * rc);
                cs[k] = p + Vector3.Lerp(flat, stand, tilt);
            }
            Vector3 faceN = Vector3.Lerp(Vector3.up, new Vector3(ToCamPlan.x, 0f, ToCamPlan.y), tilt);
            Air.EmitCell(p, cs, hash, SpellFxHexKit.PatChoices[0], 0.5f, faceN, Tilt, p, false);
        }
    }

    /// <summary>Queue of ink spits in time order. <see cref="Spit"/> = the small ink of the brush, a throw, a landing, a contact, a line breaking (always the same, whatever the body);
    /// <see cref="End"/> = the body's own burst as a piece breaks up at the end (none for wood); <see cref="Plates"/> = torn plates (가른다).</summary>
    public sealed class HexInkQueue
    {
        enum Kind { Spit, End, Plates }
        struct Item { public float At; public Vector3 Point, Normal; public Kind K; public SpellFxBurstBody Body; public Color Edge; }
        readonly List<Item> _q = new List<Item>();

        void Add(Item e) { int k = 0; while (k < _q.Count && _q[k].At <= e.At) k++; _q.Insert(k, e); }
        public void Spit(float at, Vector3 point, Vector3 normal) { Add(new Item { At = at, Point = point, Normal = normal, K = Kind.Spit }); }
        public void End(float at, Vector3 point, Vector3 normal, SpellFxBurstBody body) { if (body == SpellFxBurstBody.Wood) return; Add(new Item { At = at, Point = point, Normal = normal, K = Kind.End, Body = body }); }
        public void Plates(float at, Vector3 point, Vector3 normal, Color edge) { Add(new Item { At = at, Point = point, Normal = normal, K = Kind.Plates, Edge = edge }); }

        public void Drain(float a)
        {
            while (_q.Count > 0 && _q[0].At <= a)
            {
                var e = _q[0]; _q.RemoveAt(0);
                switch (e.K)
                {
                    case Kind.Spit: SpellFxHexKit.Spit(e.Point, e.Normal); break;
                    case Kind.End: SpellFxLabShot.InkBurst(e.Point, e.Normal, e.Body); break;
                    default: SpellFxLabShot.InkBurst(e.Point, e.Normal, SpellFxBurstBody.Armour, e.Edge); break;
                }
            }
        }
    }

    /// <summary>The shader has ONE latest-hit slot: flashes are kept 0.1 s apart so each one reads, and the one in force is driven into the main layer (its plan position is asked every
    /// frame while it lives, so a cell that moves keeps its flash). Off once the flash has died (1.2 s): nothing of a hit may linger on a standing sheet.</summary>
    public sealed class HexFlashQueue
    {
        struct Flash { public float At; public Func<Vector3> Plan; }
        readonly List<Flash> _flashes = new List<Flash>();
        Func<Vector3> _hit; float _hitAt = -1f;

        public void Schedule(Func<Vector3> plan, float at)
        {
            for (int guard = 0; guard < 40; guard++)
            {
                bool clash = false;
                foreach (var f in _flashes) if (Mathf.Abs(f.At - at) < 0.09f) { clash = true; break; }
                if (!clash) break;
                at += 0.1f;
            }
            var fl = new Flash { At = at, Plan = plan };
            int k = 0; while (k < _flashes.Count && _flashes[k].At <= at) k++;
            _flashes.Insert(k, fl);
        }

        public void Drive(HexSheet sheet, float a)
        {
            while (_flashes.Count > 0 && _flashes[0].At <= a)
            {
                _hit = _flashes[0].Plan; _hitAt = Mathf.Max(a, 0f);
                _flashes.RemoveAt(0);
            }
            if (_hit == null || a - _hitAt > 1.2f) { sheet.Main.ClearHit(); return; }
            sheet.Main.SetHit(sheet.WorldOf(_hit()), a - _hitAt);
        }
    }

    /// <summary>Hold cells on a struck body: the body's real surface by a ray per cell against its colliders.</summary>
    public static class HexHolds
    {
        // the hold cells, round the body from the side that faces the caster: never within 62 deg of it (that side is the face the camera sees), and at 46 - 70 % of the body's height
        // (each at its own height, so they never read as one ring)
        static readonly float[] HoldAng = { 62f, -62f, 95f, -95f, 130f };
        static readonly float[] HoldFrac = { 0.50f, 0.62f, 0.70f, 0.46f, 0.58f };
        public static int MaxCells => HoldAng.Length;

        public static float PlateG(HexPlate p, float a, float leaveLen)
        {
            float g = SpellFxHexKit.Smooth((a - p.Start) / p.Grow);
            if (a > p.LeaveAt) g *= 1f - SpellFxHexKit.Smooth((a - p.LeaveAt) / leaveLen);
            return g;
        }

        /// <summary>The body a hit struck at plan point <paramref name="feet"/>: hit.Feet is not the ground (the vine's old pitfall) and twice the centre's height overshot a four-legged body - the
        /// ground under the feet, and the body's height as 1.3 x its centre above that ground; then <see cref="Build"/>.</summary>
        public static HexBody BodyOf(HexSheet s, SpellFxHit hit, Vector2 feet)
        {
            float R = hit.TargetRadius > 0.05f ? Mathf.Clamp(hit.TargetRadius, 0.3f, 0.9f) : 0.5f;
            float gy = Mathf.Clamp(s.Ground.At(feet), -1f, 1f);
            float dy = hit.Position.y - (s.Frame.Origin.y + gy);
            float H = dy > 0.2f ? Mathf.Clamp(dy * 1.3f, 0.8f, 2.4f) : 1.2f;
            return Build(s, hit, feet, gy, R, H);
        }

        /// <summary>The struck body as the hold needs it. With a target: the bounds of its enabled, non-trigger colliders within 2.5 m of the contact (else of its mesh renderers) give the axis, the
        /// height range [Y0, Y1] and the radius; the colliders themselves are kept for the per-cell rays. Without one (a synthetic body) or with absurd bounds: the estimate from the contact.</summary>
        public static HexBody Build(HexSheet s, SpellFxHit hit, Vector2 feet, float gy, float R, float H)
        {
            var h = new HexBody { Feet = feet, Centre = feet, Ground = gy, Y0 = gy, Y1 = gy + H, Radius = R * 0.85f };
            if (hit.Target == null) return h;
            var list = new List<Collider>();
            Bounds b = default; bool have = false;
            foreach (var c in hit.Target.GetComponentsInChildren<Collider>(false))
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                Bounds cb = c.bounds;
                if ((cb.center - hit.Position).sqrMagnitude > 6.25f) continue;
                list.Add(c);
                if (!have) { b = cb; have = true; } else b.Encapsulate(cb);
            }
            if (!have)
            {
                int vl = VfxAfterFog308.Layer;
                foreach (var r in hit.Target.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (vl >= 0 && r.gameObject.layer == vl) continue;
                    if ((r.bounds.center - hit.Position).sqrMagnitude > 6.25f) continue;
                    if (!have) { b = r.bounds; have = true; } else b.Encapsulate(r.bounds);
                }
            }
            if (!have) return h;
            float height = b.size.y, rad = Mathf.Max(b.extents.x, b.extents.z);
            if (height < 0.3f || height > 3.5f || rad < 0.1f) return h;
            Vector3 cr = b.center - s.Frame.Origin;
            h.Centre = new Vector2(Vector3.Dot(cr, s.Frame.Right), Vector3.Dot(cr, s.Frame.Fwd));
            h.Y0 = b.min.y - s.Frame.Origin.y; h.Y1 = b.max.y - s.Frame.Origin.y;
            h.Radius = Mathf.Clamp(rad, 0.2f, 1.5f);
            h.Cols = list.ToArray();
            return h;
        }

        /// <summary>The body's surface along a horizontal ray toward its axis at plan height <paramref name="y"/>, from the direction <paramref name="dir"/> (plan x, z): the point (plan) and the
        /// outward horizontal normal (plan x, z). False when no collider is hit or the face is not a side (the top of a bust).</summary>
        static bool SurfaceAt(HexSheet s, HexBody h, Vector2 dir, float y, out Vector3 point, out Vector2 normal)
        {
            point = default; normal = dir;
            if (h.Cols == null || h.Cols.Length == 0) return false;
            Vector3 axis = s.PlanW(new Vector3(h.Centre.x, y, h.Centre.y));
            Vector3 dw = s.Frame.Right * dir.x + s.Frame.Fwd * dir.y;
            float far = h.Radius + 0.9f;
            var ray = new Ray(axis + dw * far, -dw);
            float best = float.MaxValue; RaycastHit bh = default; bool got = false;
            foreach (var c in h.Cols)
            {
                if (c == null || !c.enabled) continue;
                if (c.Raycast(ray, out RaycastHit rh, far + 0.4f) && rh.distance < best) { best = rh.distance; bh = rh; got = true; }
            }
            if (!got) return false;
            Vector2 n = new Vector2(Vector3.Dot(bh.normal, s.Frame.Right), Vector3.Dot(bh.normal, s.Frame.Fwd));
            if (n.sqrMagnitude < 0.09f) return false;
            normal = n.normalized;
            point = s.ToPlan(bh.point);
            return true;
        }

        /// <summary>The hold (round 3): <paramref name="n"/> (2 - 5) cells sit ON the body's surface - found by a ray per cell against its colliders, 3 cm out (plus the sag of a flat cell on a
        /// round body) - each at its own height between 46 and 70 % of the body's height and at 62 - 130 deg round from the side that faces the caster, so the camera-facing face stays clear and
        /// the cells never line up as a ring. Each grows in 0.2 s, 0.045 s apart. The plates are added to <paramref name="into"/>; returns the first (the contact) cell. <paramref name="reach"/> only
        /// feeds the uv height (distance / reach).</summary>
        public static HexPlate Add(HexSheet s, System.Random rng, List<HexPlate> into, HexBody h, float t0, int n, float leaveAt, float reach)
        {
            Vector2 toC = h.Centre.sqrMagnitude > 1e-4f ? -h.Centre.normalized : -Vector2.up;
            float baseAng = Mathf.Atan2(toC.y, toC.x), rc = Mathf.Clamp(0.22f * h.Radius + 0.08f, 0.12f, 0.24f);
            float d01 = Mathf.Clamp(h.Centre.magnitude / reach, 0.3f, 1f);
            float sag = Mathf.Min(0.08f, h.Radius - Mathf.Sqrt(Mathf.Max(0f, h.Radius * h.Radius - rc * rc)));
            HexPlate front = null;
            for (int k = 0; k < n && k < HoldAng.Length; k++)
            {
                float ang = baseAng + HoldAng[k] * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float y = h.Y0 + HoldFrac[k] * (h.Y1 - h.Y0);
                Vector2 outDir = dir, pt = h.Centre + dir * (h.Radius + 0.03f);
                if (SurfaceAt(s, h, dir, y, out Vector3 sp, out Vector2 sn)) { outDir = sn; pt = new Vector2(sp.x, sp.z) + sn * (0.03f + sag); y = sp.y; }
                var p = new HexPlate
                {
                    Ctr = new Vector3(pt.x, y, pt.y), Dir = outDir, Rx = rc, Rv = rc,
                    Start = t0 + 0.05f + 0.045f * k, Grow = 0.2f, LeaveAt = leaveAt + 0.02f * k, Hash = SpellFxHexKit.PickHash(rng, SpellFxHexKit.Rand(rng)), D01 = d01,
                    Pat = SpellFxHexKit.PatChoices[(k + rng.Next(SpellFxHexKit.PatChoices.Length)) % SpellFxHexKit.PatChoices.Length],
                };
                into.Add(p);
                if (front == null) front = p;
            }
            return front;
        }

        /// <summary>World point and normal a spit at a plate's face uses (the face's own direction, 4 cm out).</summary>
        public static Vector3 ContactPoint(HexSheet s, HexPlate p) => s.PlanW(p.Ctr) + FaceDirW(s, p) * 0.04f;
        public static Vector3 ContactNormal(HexSheet s, HexPlate p) => (FaceDirW(s, p) * 0.8f + Vector3.up * 0.4f).normalized;
        static Vector3 FaceDirW(HexSheet s, HexPlate p) => s.Frame.Right * p.Dir.x + s.Frame.Fwd * p.Dir.y;
    }

    /// <summary>Once preview (2026-10-30, D308-158 1): while the great brush swings toward the apex a small hexagon cell takes shape at the brush tip and ink motes are drawn into it; at the
    /// launch that cell becomes the first thrown one (same place, size, hash, pattern). Drawn on the sheet's air layer. One per cast, owned by the part.</summary>
    public sealed class HexTipPreview
    {
        ParticleSystem _ps;
        GameObject _go;
        bool _live;

        public bool Live => _live;

        /// <param name="since">seconds since the commit; <paramref name="delay"/> = seconds from the commit to the launch; <paramref name="finalRc"/> = the radius the first thrown cell starts at.</param>
        public void Show(HexSheet sheet, Transform parent, float since, float delay, float finalRc, float hash)
        {
            float p = Mathf.Clamp01(since / Mathf.Max(delay, 0.05f));
            Vector3 tip = sheet.SampleTipPlan();
            sheet.TipPlan = tip;
            float grow = SpellFxHexKit.Smooth(p);
            // a point first, then a cell that fills out; at p = 1 it is exactly the first flight's starting cell
            if (grow > 0.02f) sheet.EmitPose(tip, finalRc * (0.12f + 0.88f * grow), 1f, hash);
            Suck(sheet, parent, tip, p);
        }

        void Suck(HexSheet sheet, Transform parent, Vector3 tipPlan, float p)
        {
            if (!Application.isPlaying) return;
            if (_ps == null)
            {
                _go = new GameObject("HexTipInk");
                _go.transform.SetParent(parent, false);
                int layer = VfxAfterFog308.Layer; if (layer >= 0) _go.layer = layer;
                _ps = _go.AddComponent<ParticleSystem>();
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = _ps.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = 0.22f; main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
                main.startColor = new Color(0.03f, 0.03f, 0.035f, 0.9f);
                main.maxParticles = 64;
                var shape = _ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.42f; shape.radiusThickness = 0f;
                var em = _ps.emission; em.rateOverTime = 0f;
                var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
                vel.radial = new ParticleSystem.MinMaxCurve(-1.9f);       // 0.42 m in 0.22 s
                var sol = _ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
                var col = _ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 1f) });
                col.color = g;
                var r = _go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = SpellFxLabShot.InkBlotMaterial(); r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            _go.transform.position = sheet.PlanW(tipPlan);
            var e = _ps.emission; e.rateOverTime = 70f * p;
            if (!_ps.isPlaying) _ps.Play();
            _live = true;
        }

        /// <summary>The cell is handed to the first flight (or the cast was cut): no more motes.</summary>
        public void Stop()
        {
            _live = false;
            if (_ps != null) { var e = _ps.emission; e.rateOverTime = 0f; _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }
        }
    }

    /// <summary>Immutable tables and pure helpers of the hex-cell look.</summary>
    public static class SpellFxHexKit
    {
        public const float Sqrt3 = 1.7320508f, HashStep = 1f / 13.7f;
        public static readonly float[] Cos6 = new float[6], Sin6 = new float[6];
        public static readonly Vector2[] Uv6 = new Vector2[6];
        /// <summary>As the dome: not 0 / 6 (they read as filled stamps).</summary>
        public static readonly int[] PatChoices = { 1, 2, 3, 4, 5, 7 };
        /// <summary>(hash, bloom phase) of every hash whose bloom period is short (0.9 - 1.34 s, the shader's h3 &lt; 0.4) and below 0.8 (so hash + 1/13.7 stays under the 0.88 flicker step).</summary>
        static readonly List<Vector2> HashTable = new List<Vector2>(1800);

        /// <summary>The scale of the small ink spit (count and size of <see cref="SpellFxLabShot.InkBurst"/>).</summary>
        public const float SpitScale = 0.35f;

        static SpellFxHexKit()
        {
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f; Cos6[k] = Mathf.Cos(a); Sin6[k] = Mathf.Sin(a);
                Uv6[k] = new Vector2(0.5f + 0.54f * Cos6[k], 0.5f + 0.54f * Sin6[k]);      // the dome's 1.08 / (2 R)
            }
            for (int i = 0; i < 4096; i++)
            {
                float h = i / 4096f * 0.8f;
                if (Frac(h * 13.7f + 0.11f) > 0.4f) continue;
                HashTable.Add(new Vector2(h, Frac(h * 7.13f + 0.37f)));
            }
        }

        public static float Frac(float x) => x - Mathf.Floor(x);
        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        public static long Key(int i, int j) => ((long)i << 32) | (uint)j;
        public static float Rand(System.Random rng) => (float)rng.NextDouble();

        /// <summary>A hash whose bloom phase is near <paramref name="phase"/> and whose period is short: the shader blooms each cell's pattern at its own moment, a few tenths of a
        /// second in about a second, so a short period keeps the most cells lit (the second layer's hash + 1/13.7 shares the period and is half a period out).</summary>
        public static float PickHash(System.Random rng, float phase)
        {
            var near = new List<float>();
            float best = float.MaxValue, bh = 0.1f;
            foreach (var t in HashTable)
            {
                float d = Mathf.Abs(t.y - phase); d = Mathf.Min(d, 1f - d);
                if (d < best) { best = d; bh = t.x; }
                if (d < 0.03f) near.Add(t.x);
            }
            return near.Count > 0 ? near[rng.Next(near.Count)] : bh;
        }

        /// <summary>Centre of lattice cell (i, j) for circumradius <paramref name="r"/>: columns 1.5 r apart, every odd column half a cell up (plan x, z).</summary>
        public static Vector2 CenterOf(int i, int j, float r) => new Vector2(i * 1.5f * r, (j + 0.5f * (i & 1)) * Sqrt3 * r);

        public static void SnapTo(Vector2 p, float r, out int bi, out int bj)
        {
            float dx = 1.5f * r, dz = Sqrt3 * r, best = float.MaxValue;
            int i0 = Mathf.RoundToInt(p.x / dx); bi = i0; bj = 0;
            for (int di = -1; di <= 1; di++)
            {
                int i = i0 + di, j0 = Mathf.RoundToInt(p.y / dz - 0.5f * (i & 1));
                for (int dj = -1; dj <= 1; dj++)
                {
                    int j = j0 + dj; float d = (CenterOf(i, j, r) - p).sqrMagnitude;
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            }
        }

        /// <summary>The small ink spit of the brush, a throw, a landing, a contact, a breaking line: always plain ink, small, whatever the body (the body's own burst is only for the end).</summary>
        public static void Spit(Vector3 point, Vector3 normal) { SpellFxLabShot.InkBurst(point, normal, SpellFxBurstBody.Water, null, SpitScale); }
    }
}
