using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>수>토 막힘 (막힘) - free showcase batch 8, concept ae_a (a hooked wave of clear blue-green water with ink curling inside rears up against the enemy's
    /// ring of stone slabs, breaks over it and FOLDS SIDEWAYS - short and spent - in a burst of spray and mist; half of the slabs topple and sink).
    /// Facts only: the acting shape is spent at the commit (the wave runs in, rears, curls and folds sideways along the dyke, falling flat and draining away); the
    /// enemy's earth patch survives at x0.5 (Blocked(half)), its life not renewed - three of its six slabs topple outward and sink in dust, the other three stand,
    /// wet, (the enemy's stone) to the material's own end (Expired: they sink into the ground). Nothing is damaged, nothing is born.</summary>
    public sealed class Vol2FsRxWaterEarth : Vol2FsReact
    {
        static readonly Color Teal = new Color(0.45f, 0.85f, 1f);
        Mesh _waveMesh, _foamMesh, _inkMesh, _wetMesh, _scarMesh; Light _keep;
        readonly Vol2FsStrips _wave = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _wet = new Vol2FsStrips(), _scar = new Vol2FsStrips();
        ParticleSystem _spray, _drops, _mist, _dust;
        Vector3 _front;                                  // the wave's front line centre at the dyke (the patch's near edge), on the ground
        bool _hit, _toppled;
        readonly List<(Transform T, Quaternion R0, Vector3 P0, Vector3 Out)> _fall = new List<(Transform, Quaternion, Vector3, Vector3)>();
        const float HitAt = 0.08f;
        /// <summary>2026-10-27 weak-points pass: the common half tears as the slabs start to topple (HitAt + 0.12).</summary>
        protected override float HalfAt => 0.2f;

        protected override void OnBuild()
        {
            var root = Root.transform;
            var water = Vol2FsB1.M("fs_water", new Color(0.5f, 0.92f, 1f), 1.25f, 0.3f); water.Ramp = "fs_ramp_water"; water.RampScale = 1.6f; water.NoiseAmt = 0.3f; water.NoiseScale = 1.4f; water.Erode = 0.28f; water.Soft = 0.15f;
            water.Scroll = new Vector2(0f, -1.6f); water.Edge = new Color(0.9f, 1f, 1f, 1f) * 0.6f; water.EdgeWidth = 0.12f;
            Vol2Fs.MeshObject(root, "Wave", Kit.Mat(water), out _waveMesh);
            var foam = M("fs_splash", new Color(0.55f, 0.9f, 1f), 1.2f, 0.45f); foam.NoiseAmt = 0.4f; foam.Erode = 0.3f; foam.Soft = 0.1f; foam.Scroll = new Vector2(-1.5f, 0f);
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "InkCurl", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.14f, 0.55f, 0.35f, 1.3f), out _inkMesh);
            var wet = M("fs_wet", new Color(0.1f, 0.16f, 0.2f, 1f), 1f, 0f); wet.Opacity = 0.6f; wet.NoiseAmt = 0.3f; wet.Soft = 0.2f;
            Vol2Fs.MeshObject(root, "Wet", Kit.Mat(wet), out _wetMesh);
            Vol2Fs.MeshObject(root, "HalfScar", Vol2FsRxBlockKit.ScarMat(Kit), out _scarMesh);
            _keep = Vol2FsRxBlockKit.KeepLight(root, Contacted);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.15f, 0.3f, "fs_splash"), Life0 = 0.5f, Life1 = 1.0f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.3f, Size1 = 0.6f, Gravity = 1.5f, Drag = 0.6f, Max = 500 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.35f, 0.78f, 1f), 2.2f, 0.7f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.2f, Size0 = 0.025f, Size1 = 0.05f, Gravity = 2.6f, Stretch = true, StretchVel = 0.06f, StretchLen = 1.2f, Max = 600 }, root, "Drops");
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.75f, 0.85f, 1f), 0.22f)), Life0 = 0.9f, Life1 = 1.6f, Speed0 = 0.3f, Speed1 = 1.4f, Size0 = 0.5f, Size1 = 1.0f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.6f, Drag = 1.5f, VelOverLife = new Vector3(0f, 0.4f, 0f), Max = 160 }, root, "Mist");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.4f, 0.32f, 1f), 0.5f)), Life0 = 0.8f, Life1 = 1.5f, Speed0 = 0.4f, Speed1 = 2f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.4f, Max = 160 }, root, "Dust");
            foreach (var ps in new[] { _spray, _drops, _mist, _dust }) ps.Play(false);
            _front = G(P - Dir * Radius * 0.85f);
            // three of the six slabs (every other) will topple outward and sink
            if (Patch != null)
                for (int i = 0; i < Mathf.Min(6, Patch.Props.Count); i += 2)
                {
                    var tr = Patch.Props[i]; if (tr == null) continue;
                    Vector3 o = tr.position - P; o.y = 0f; o = o.sqrMagnitude > 1e-4f ? o.normalized : Dir;
                    _fall.Add((tr, tr.rotation, tr.position, o)); Patch.Claim(tr);
                }
            Note += " | dammed ae_a topple " + _fall.Count;
        }

        /// <summary>The hook's profile at s (0 = the wave's foot, 1 = its curling lip) for a wave that has run to <paramref name="run"/> (m before the dyke) and
        /// reared <paramref name="rear"/> (0..1): forward along Dir, up.</summary>
        Vector3 Hook(float s, float rear, float run)
        {
            float h = Mathf.Lerp(0.8f, 2.3f, rear);
            float fwd = -run + 0.8f * Mathf.Sin(s * Mathf.PI * 0.95f) * rear + 0.35f * s;
            float up = h * Mathf.Sin(Mathf.Min(1f, s * 1.15f) * Mathf.PI * 0.5f) - 1.2f * rear * Mathf.Max(0f, s - 0.7f) * 4f * Mathf.Max(0f, s - 0.7f);
            return Dir * fwd + Vector3.up * up;
        }

        protected override void OnLive(float t)
        {
            var cam = CamPos;
            _wave.Clear(); _foam.Clear(); _ink.Clear(); _wet.Clear();
            if (!_hit && t >= HitAt)
            {
                _hit = true; HitStop(t, 0.08f);
                for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_spray, _front + Side * Random.Range(-1.6f, 1.6f) + Vector3.up * Random.Range(0.4f, 1.4f), (Vector3.up * 1.4f + Random.insideUnitSphere + Dir * 0.3f).normalized * Random.Range(2f, 6f), -1f, -1f);
                for (int k = 0; k < 160; k++) Vol2FsB1.EmitV(_drops, _front + Side * Random.Range(-1.2f, 1.2f) + Vector3.up * Random.Range(0.3f, 1.5f), (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1.5f, 5f), -1f, -1f);
                for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_mist, _front + Side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(0.3f, 1.2f), Random.insideUnitSphere * 1.2f + Vector3.up * 0.6f, -1f, -1f);
            }
            // the wave: runs in (0 .. HitAt), rears and curls (.. +0.15), then folds sideways, falls flat and drains (.. +0.9)
            float run = Mathf.Lerp(2.6f, 0f, Vol2Fs.Smooth(0f, HitAt, t));
            float a = Mathf.Max(0f, t - HitAt);
            float rear = Mathf.Lerp(0.55f, 1f, Vol2Fs.Smooth(0f, 0.1f, t)) * (1f - 0.75f * Vol2Fs.Smooth(0.12f, 0.6f, a));
            float fold = Vol2Fs.Smooth(0.08f, 0.55f, a);
            float life = 1f - Vol2Fs.Smooth(0.45f, 1.0f, a);
            if (life > 0.01f)
            {
                const int Rows = 7, Cols = 13;
                for (int r = 0; r < Rows; r++)
                {
                    float s0 = r / (float)Rows, s1 = (r + 1f) / Rows, sm = (s0 + s1) * 0.5f;
                    var pts = new Vector3[Cols]; var w = new float[Cols]; var al = new float[Cols]; var sd = new Vector3[Cols];
                    for (int c = 0; c < Cols; c++)
                    {
                        float f = c / (float)(Cols - 1) * 2f - 1f;                      // -1 .. 1 along the front
                        float bend = 1f - 0.35f * f * f;                                  // the front bows forward in the middle
                        float sideOff = f * 1.8f + Mathf.Sign(f) * fold * (0.9f + 0.5f * Mathf.Abs(f)) * Mathf.Min(1f, Mathf.Abs(f) * 3f);
                        float squash = 1f - 0.8f * fold * (1f - 0.4f * Mathf.Abs(f));
                        float ripple = 1f + 0.18f * Mathf.Sin(f * 7f + t * 9f + r * 0.6f);         // r2: an uneven crest (r1 read as a box)
                        Vector3 h0 = Hook(s0, rear * bend * ripple, run), h1 = Hook(s1, rear * bend * ripple, run);
                        h0.y *= squash; h1.y *= squash;
                        Vector3 baseP = _front + Side * sideOff - Dir * fold * 0.25f * Mathf.Abs(f);
                        pts[c] = baseP + (h0 + h1) * 0.5f;
                        sd[c] = (h1 - h0).sqrMagnitude > 1e-6f ? (h1 - h0).normalized : Vector3.up;
                        w[c] = (h1 - h0).magnitude * 1.15f + 0.02f;
                        al[c] = life * (1f - 0.8f * Mathf.Abs(f) * Mathf.Abs(f)) * (0.45f + 0.55f * sm);
                    }
                    _wave.Strip(pts, w, sd, Color.white, al, 0.13f * r, cam);
                    // the foam lip on the last row, the ink curl just inside it
                    if (r == Rows - 1) _foam.Strip(pts, MulArr(w, 1.4f), sd, Color.white, al, 0.5f, cam);
                    if (r == Rows - 3) { var ip = new Vector3[Cols]; for (int c = 0; c < Cols; c++) ip[c] = pts[c] - Dir * 0.08f; _ink.Strip(ip, MulArr(w, 0.35f), sd, Color.white, al, 0.3f, cam); }
                }
                if (a > 0f && a < 0.6f && Time.frameCount % 2 == 0)
                    for (int k = 0; k < 6; k++) { float f = Random.Range(-1f, 1f); Vol2FsB1.EmitV(_drops, _front + Side * (f * 1.3f + Mathf.Sign(f) * fold) + Vector3.up * Random.Range(0.2f, 1.1f) * (1f - fold * 0.6f), Side * Mathf.Sign(f) * Random.Range(1f, 3f) + Vector3.up * Random.Range(0.5f, 2f), -1f, -1f); }
            }
            // the wet sheen the wave leaves along the dyke (drying over ~2.5 s)
            float wetLife = Vol2Fs.Smooth(0.15f, 0.4f, a) * (1f - Vol2Fs.Smooth(1.2f, 2.8f, a));
            if (wetLife > 0.01f) Vol2FsB4A.GroundArc(_wet, P, Ground + 0.01f, Radius * 1.05f, 0.55f, Mathf.Atan2(-Dir.z, -Dir.x) - 1.3f, 2.6f, wetLife, 0.4f, cam, 24);
            _scar.Clear(); if (Patch != null) { Vol2FsRxBlockKit.Survivor(_scar, P, Ground, Radius, Dir, t, ContactEndAt, ContactExpireRel, cam); Vol2FsRxBlockKit.KeepLightTick(_keep, P, t, ContactEndAt); }
            _wave.Write(_waveMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh); _wet.Write(_wetMesh); _scar.Write(_scarMesh);
            // half of the enemy's slabs topple outward and sink in dust; the rest stand to the end
            if (_hit && !_toppled && a >= 0.12f)
            {
                _toppled = true;
                foreach (var f in _fall) if (f.T != null) for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_dust, f.P0 + f.Out * 0.2f + Vector3.up * 0.2f, f.Out * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(0.3f, 1.2f), -1f, -1f);
            }
            foreach (var f in _fall)
            {
                if (f.T == null) continue;
                float k = Vol2Fs.Smooth(0.12f, 0.55f, a), sink = Vol2Fs.Smooth(1.0f, 2.4f, a);
                Vector3 axis = Vector3.Cross(Vector3.up, f.Out);
                f.T.SetPositionAndRotation(f.P0 + f.Out * 0.25f * k + Vector3.down * 0.7f * sink, Quaternion.AngleAxis(78f * k, axis) * f.R0);
                if (sink >= 0.999f && f.T.gameObject.activeSelf) f.T.gameObject.SetActive(false);
            }
            if (ContactEndAt >= 0f && Patch != null)
            {
                float e = Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.9f, t);
                Patch.Pose(1f - 0.6f * e, e * Patch.Height);
            }
        }

        static float[] MulArr(float[] a, float k) { var r = new float[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i] * k; return r; }

        protected override void OnContactEnd(Vol2MaterialEnd why, float t)
        {
            if (Patch == null) return;
            if (why == Vol2MaterialEnd.Expired) for (int k = 0; k < 20; k++) Vol2FsB1.EmitV(_dust, G(P + Random.insideUnitSphere * Radius) + Vector3.up * 0.2f, Random.insideUnitSphere + Vector3.up * 0.5f, -1f, -1f);
            else Patch.SetActive(false);
        }
    }
}
