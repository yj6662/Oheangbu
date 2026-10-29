using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    // 2026-10-27 weak-points pass of the free showcase (the user, 2026-10-27: "묶음 6–8에서 반복된 공통 약점(강철 회색 · ㅏ 경로 원근 · 작용 글자 룩 겹침 · 막힘 반 잔액
    // 약함)부터 다듬어 줘."). Two common layers, read only from the executor's facts (presentation only: nothing here is read by a rule, decides or delays anything):
    //  * Vol2FsPathRead - every ㅏ (path) look: the head's REAL run (the path emitter's HeadAlong / Finished, the cast's accepted contacts) drawn so it reads from the
    //    shoulder camera A, where a straight path is seen end-on (a short vertical line for ~7 frames): a ground furrow along the whole run that stays 1.5 - 2 s
    //    (fast run -> slow afterimage, Phantom Blade principle 3), a bow-wave V of two ground streaks opening sideways behind the head, ink and accent spray
    //    flung sideways where the head passes, a burst flung to both sides at every accepted contact (at the body's real place), an X glint + light at the
    //    launch and where the head stopped (with a short hit-stop of the look's particles).
    //  * Vol2FsRxHalf - every 막힘 (Block) reaction: the contacted material survives at the engine's remaining amount (x0.5): its LOST share (the struck near side,
    //    the area the engine's remaining / initial leaves out) tears off along a glowing cut, tilts, crumbles and sinks; the KEPT share stays, wounded (a glowing
    //    torn edge, cracks, ink dripping from the wound, its pieces leaning away) to the material's own end.

    /// <summary>The ㅏ path read (one per path cast; built and ticked by <see cref="Vol2FsRun.KitTick"/>).</summary>
    public sealed class Vol2FsPathRead
    {
        static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        readonly Vol2VfxInstance _inst; readonly Vol2FsKit _kit;
        readonly Color _acc; readonly int _seed;
        readonly Vector3 _o, _f, _r; readonly float _len, _speed, _ground, _launch;
        readonly Vol2FsStrips _furrow = new Vol2FsStrips(), _core = new Vol2FsStrips(), _wake = new Vol2FsStrips(), _flash = new Vol2FsStrips();
        Mesh _furrowM, _coreM, _wakeM, _flashM;
        ParticleSystem _spray, _sparks;
        Light _light;
        float _reach, _stopAt = -1f, _sprayNext = 2.0f, _turn = -1f;
        bool _built;
        readonly List<(float Along, float At, Vector3 At3)> _contacts = new List<(float, float, Vector3)>();
        readonly HashSet<long> _seen = new HashSet<long>();
        public string Note = "";

        public Vol2FsPathRead(Vol2VfxInstance inst, Vol2FsKit kit, float launchRel)
        {
            _inst = inst; _kit = kit; _launch = launchRel;
            var spec = inst.Log.Spec;
            _o = spec.Origin; _f = spec.Forward; _f.y = 0f; _f = _f.sqrMagnitude > 1e-6f ? _f.normalized : inst.Forward;
            _r = Vector3.Cross(Vector3.up, _f);
            _len = Mathf.Max(0.5f, spec.PathLength); _speed = Mathf.Max(0.1f, spec.PathSpeed); _ground = inst.Origin.y;
            _acc = Vol2FsRun.Accent(inst.Log.Glyph.Body);
            _seed = (int)(inst.Cast.Value & 0x7fff);
        }

        void Build(Transform root)
        {
            _built = true;
            Vol2Fs.MeshObject(root, "PathFurrow", _kit.InkSolid("fs_stroke2", InkC, _acc * 1.5f, 0.16f, 0.55f, 0.33f, 1.4f), out _furrowM);
            var core = Vol2FsMat.Make("fs_streak", _acc, 2.6f, 0.9f); core.NoiseAmt = 0.3f; core.Soft = 0.05f; core.Erode = 0.2f; core.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "PathCore", _kit.Mat(core), out _coreM);
            Vol2Fs.MeshObject(root, "PathWake", _kit.InkSolid("fs_stroke2", InkC, _acc * 1.2f, 0.12f, 0.6f, 0.36f, 1.7f), out _wakeM);
            var fl = Vol2FsMat.Make("fs_streak", Color.Lerp(_acc, Color.white, 0.3f), 3.4f, 1f); fl.NoiseAmt = 0.15f; fl.Soft = 0.02f; fl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "PathFlash", _kit.Mat(fl), out _flashM);
            var drop = Vol2FsMat.Make("fs_ink", InkC, 1f, 0f); drop.Edge = _acc * 1.6f; drop.EdgeWidth = 0.18f; drop.NoiseAmt = 0.3f; drop.Soft = 0.1f;
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(drop), Life0 = 0.4f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.04f, Size1 = 0.1f, Gravity = 2.4f, Drag = 0.5f, Tiles = 2, AlphaHoldTo = 0.4f, Max = 500, NoFlip = true }, root, "PathSpray");      // r2: small drops (r1 big black chips lay on the ground)
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(Vol2FsMat.Make("fs_dot", _acc, 4.5f, 1f)), Life0 = 0.3f, Life1 = 0.8f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 1.2f, Drag = 1.0f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 600, NoFlip = true }, root, "PathSparks");
            _spray.Play(false); _sparks.Play(false);
            _light = Vol2Fs.PointLight(root, "PathLight", _acc, 7f);
        }

        Vector3 On(float along, float side = 0f, float up = 0f) { var p = _o + _f * along + _r * side; p.y = _ground + up; return p; }

        /// <summary>The cast's accepted contact (the recipe relays every contact record): a lateral burst where the head met the body.</summary>
        public void Contact(Vol2ContactRecord rec, float t, Vector3? body = null)
        {
            if (rec == null || !rec.BudgetAccepted || float.IsNaN(rec.Along) || float.IsInfinity(rec.Along)) return;
            if (!_seen.Add(rec.TargetId * 31 + (rec.Kind ?? "").GetHashCode())) return;
            Vector3 at = body ?? On(rec.Along, 0f, 1.05f);
            float along = body.HasValue ? Mathf.Clamp(Vector3.Dot(body.Value - _o, _f), 0f, _len) : Mathf.Clamp(rec.Along, 0f, _len);
            _contacts.Add((along, t, at));
        }

        /// <summary>Every frame (t = the look clock). Returns true on a frame that should hit-stop the look (the head stopped / a contact burst).</summary>
        public bool Tick(Transform root, float t, Vector3 cam)
        {
            if (root == null || t < _launch) return false;
            if (!_built) Build(root);
            bool stop = false;
            var p = _inst.Path;
            float along = p != null ? p.HeadAlong : Mathf.Min(_len, _speed * (t - _launch));
            if (float.IsNaN(along) || float.IsInfinity(along)) along = _reach;
            along = Mathf.Clamp(along, 0f, _len);
            if (along < _reach - 0.05f && _turn < 0f) _turn = t;          // a returning head (돌아온다): the furrow keeps its farthest reach
            float prevReach = _reach;
            _reach = Mathf.Max(_reach, along);
            if (_stopAt < 0f && p != null && p.Finished)
            {
                _stopAt = t; stop = true; Note = "head " + _reach.ToString("0.0") + " m " + p.EndReason;
                Vector3 e = On(_reach, 0f, 1.0f);
                for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(_sparks, e + Random.insideUnitSphere * 0.3f, (_r * Random.Range(-1f, 1f) * 2.2f + Vector3.up * Random.Range(-0.2f, 1f) - _f * Random.Range(0f, 0.6f)).normalized * Random.Range(4f, 11f), -1f, -1f);
            }
            // spray flung sideways each metre the head passes (both sides, low)
            while (_sprayNext <= _reach && _sprayNext <= prevReach + 6f)
            {
                float a = _sprayNext; _sprayNext += 1.1f;
                float wide = 1f + a * 0.08f;
                for (int k = 0; k < 6; k++) { float s = k % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_spray, On(a, s * 0.2f, 0.25f), (_r * s * Random.Range(2.5f, 5.5f) * wide + Vector3.up * Random.Range(1.2f, 3f) + _f * Random.Range(0.5f, 2.5f)), Random.Range(0.05f, 0.12f) * wide, -1f); }
                for (int k = 0; k < 8; k++) { float s = k % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_sparks, On(a, s * 0.15f, 0.5f), (_r * s * Random.Range(3f, 7f) + Vector3.up * Random.Range(0.5f, 2.5f) + _f * Random.Range(1f, 4f)), -1f, -1f); }
            }
            // contacts: the head meets the body - a lateral slash across it and a burst flung to both sides (once the head got there)
            for (int i = 0; i < _contacts.Count; i++)
            {
                var c = _contacts[i];
                if (c.At < 0f || _reach + 0.2f < c.Along) continue;
                _contacts[i] = (c.Along, -(t + 1f), c.At3);          // fired (stored as -(time + 1))
                Vector3 hp = c.At3;
                for (int k = 0; k < 50; k++) { float s = k % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_sparks, hp + Random.insideUnitSphere * 0.25f, (_r * s * Random.Range(4f, 12f) + Vector3.up * Random.Range(-0.5f, 3f) + _f * Random.Range(-1f, 2f)), -1f, -1f); }
                for (int k = 0; k < 16; k++) { float s = k % 2 == 0 ? 1f : -1f; Vol2FsB1.EmitV(_spray, hp + Random.insideUnitSphere * 0.2f, (_r * s * Random.Range(3f, 7f) + Vector3.up * Random.Range(1f, 3.5f)), Random.Range(0.06f, 0.14f), -1f); }
                stop = true;
            }
            Draw(t, cam);
            if (_light != null)
            {
                float flying = _stopAt < 0f ? 1f : 0f;
                float end = _stopAt >= 0f ? Mathf.Exp(-(t - _stopAt) * 5f) : 0f;
                _light.transform.position = On(Mathf.Max(0.5f, along), 0f, 1.0f);
                _light.intensity = 2.5f * flying + 9f * end;
            }
            return stop;
        }

        void Draw(float t, Vector3 cam)
        {
            _furrow.Clear(); _core.Clear(); _wake.Clear(); _flash.Clear();
            float a0 = 0.9f;
            if (_reach > a0 + 0.2f)
            {
                int n = Mathf.Clamp(Mathf.CeilToInt((_reach - a0) / 0.3f) + 1, 2, 64);
                var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                var cp = new Vector3[n]; var cw = new float[n]; var ca = new float[n];
                float coreLife = _stopAt < 0f ? 1f : 1f - Vol2Fs.Smooth(_stopAt + 0.5f, _stopAt + 1.8f, t);
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1), s = Mathf.Lerp(a0, _reach, u);
                    float wob = (Mathf.PerlinNoise(s * 0.6f, _seed * 0.01f) - 0.5f) * 0.35f;
                    pts[k] = On(s, wob, 0.03f); sd[k] = _r;
                    // the furrow widens with distance (the shoulder camera foreshortens it) and its far end tapers to the head
                    w[k] = (0.32f + 0.09f * s) * Mathf.Clamp01((_reach - s) / 0.6f + 0.25f);
                    // fast run -> slow afterimage: stays ~1.4 s after the head stopped, then erodes from the caster's end
                    float fade0 = (_stopAt < 0f ? 1e6f : _stopAt) + 1.5f + 0.05f * s;
                    al[k] = 1f - Vol2Fs.Smooth(fade0, fade0 + 0.9f, t);
                    // the hot core: bright where the head just passed, a thin glowing line after
                    float passed = _launch + s / _speed;
                    cp[k] = On(s, wob, 0.05f); cw[k] = (0.06f + 0.02f * s) * (1f + 1.6f * Mathf.Exp(-Mathf.Max(0f, t - passed) * 6f));
                    ca[k] = coreLife * (0.35f + 0.65f * Mathf.Exp(-Mathf.Max(0f, t - passed) * 2.5f));
                }
                _furrow.Strip(pts, w, sd, Color.white, al, 0.21f, cam);
                _core.Strip(cp, cw, sd, Color.white, ca, 0.47f, cam);
                // the bow wave: two ground streaks opening sideways behind the head (fixed once the head stopped), out to 1.9 m each side
                float head = _turn > 0f ? _reach : _reach;
                float wakeLife = _stopAt < 0f ? 1f : 1f - Vol2Fs.Smooth(_stopAt + 0.6f, _stopAt + 1.8f, t);
                if (wakeLife > 0.01f && head > 1.6f)
                    for (int sgn = -1; sgn <= 1; sgn += 2)
                    {
                        int m = 14; var wp = new Vector3[m]; var ww = new float[m]; var wa = new float[m]; var wsd = new Vector3[m];
                        for (int k = 0; k < m; k++)
                        {
                            float u = k / (float)(m - 1), s = Mathf.Lerp(head - 0.2f, Mathf.Max(a0, head - 9f), u);
                            float off = Mathf.Min(1.9f, (head - s) * 0.21f) * sgn;
                            wp[k] = On(s, off, 0.025f);
                            Vector3 tan = (_f + _r * 0.21f * sgn).normalized; wsd[k] = Vector3.Cross(Vector3.up, tan);
                            ww[k] = (0.06f + 0.035f * s) * Mathf.Sin(Mathf.Clamp01(u * 1.1f + 0.05f) * Mathf.PI * 0.95f + 0.1f);
                            wa[k] = wakeLife * (1f - 0.6f * u);
                        }
                        _wake.Strip(wp, ww, wsd, Color.white, wa, sgn > 0 ? 0.33f : 0.71f, cam);
                    }
            }
            // flashes: the launch at the brush tip, the stop at the head (camera-facing rings that open fast)
            float la = t - _launch;
            // r2: a short X glint (r1 rings read as big white hoops over the hit)
            if (la < 0.3f) Glint(_flash, On(0.6f, 0.2f, 1.25f), 0.35f, Vol2Fs.Smooth(0f, 0.06f, la), 1f - Vol2Fs.Smooth(0.08f, 0.3f, la), cam);
            if (_stopAt >= 0f)
            {
                float sa = t - _stopAt;
                if (sa < 0.45f)
                {
                    Glint(_flash, On(_reach, 0f, 1.0f), 0.55f + 0.05f * _reach, Vol2Fs.Smooth(0f, 0.06f, sa), 1f - Vol2Fs.Smooth(0.1f, 0.45f, sa), cam);
                }
            }
            // (r3: no contact slash of its own - the glyph draws its hit; r1 / r2 slashes read as stacked white streaks / beaded ink. The contact keeps only
            // the lateral burst at the body's real place, in Tick.)
            _furrow.Write(_furrowM); _core.Write(_coreM); _wake.Write(_wakeM); _flash.Write(_flashM);
        }

        /// <summary>An X glint facing the camera at c (two crossing blade-edged slashes, half size s), opening with <paramref name="open"/>.</summary>
        public static void Glint(Vol2FsStrips s, Vector3 c, float size, float open, float life, Vector3 cam)
        {
            if (life <= 0.01f || open <= 0.01f) return;
            Vector3 view = cam - c; if (view.sqrMagnitude < 1e-6f) return; view.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, view); if (right.sqrMagnitude < 1e-6f) right = Vector3.right; right.Normalize();
            Vector3 up = Vector3.Cross(view, right);
            Vector3 d1 = (right * 1.6f + up * 0.55f).normalized * size, d2 = (right * 1.6f - up * 0.55f).normalized * size;
            Vol2FsB6.Slash(s, c - d1, c + d1, Vector3.zero, size * 0.16f, open, life, 0.3f, cam, 9);
            Vol2FsB6.Slash(s, c - d2, c + d2, Vector3.zero, size * 0.12f, open, life * 0.8f, 0.7f, cam, 9);
            Vol2FsB6.Slash(s, c - up * size * 0.45f, c + up * size * 0.45f, Vector3.zero, size * 0.08f, open, life * 0.6f, 0.5f, cam, 7);
        }

        /// <summary>A ring facing the camera at c (radius r, width w).</summary>
        public static void Ring(Vol2FsStrips s, Vector3 c, float r, float w, float life, Vector3 cam, float rnd, int n = 28)
        {
            if (life <= 0.01f || r <= 0.01f) return;
            Vector3 view = (cam - c); if (view.sqrMagnitude < 1e-6f) return; view.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, view); if (right.sqrMagnitude < 1e-6f) right = Vector3.right; right.Normalize();
            Vector3 up = Vector3.Cross(view, right);
            var pts = new Vector3[n + 1]; var wd = new float[n + 1]; var al = new float[n + 1]; var sd = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float a = k / (float)n * Mathf.PI * 2f; Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                pts[k] = c + d * r; sd[k] = d; wd[k] = w; al[k] = life;
            }
            s.Strip(pts, wd, sd, Color.white, al, rnd, cam);
        }
    }

    /// <summary>The 막힘 surviving half (one per Block reaction look; built and ticked by <see cref="Vol2FsReact"/>).</summary>
    public sealed class Vol2FsRxHalf
    {
        static readonly Color Violet = new Color(0.72f, 0.36f, 1f);
        readonly Vol2FsKit _kit; readonly Vol2FsPatch _patch; readonly Transform _root;
        readonly Vector3 _p, _dir, _side; readonly float _ground, _r, _cutAt;
        readonly Color _glow, _acc;
        readonly int _seed;
        /// <summary>The lost share (0..1 of the area) and the chord's place along Dir (in radii, -1 .. 1: lost where dot / r &lt; this).</summary>
        public readonly float Lost, Chord;
        Transform _lostPivot;
        bool _cut;
        readonly List<(Transform T, Quaternion Rot0, float Lean)> _kept = new List<(Transform, Quaternion, float)>();
        readonly Vol2FsStrips _wound = new Vol2FsStrips(), _torn = new Vol2FsStrips();
        Mesh _woundM, _tornM;
        ParticleSystem _crumbs, _dust, _drip, _sparks, _smoke;
        Light _woundLight;
        public string Note = "";

        public Vol2FsRxHalf(Vol2FsKit kit, Vol2FsPatch patch, Transform root, Vector3 p, float ground, float radius, Vector3 dir, Body contacted, float lost, float cutAt, int seed)
        {
            _kit = kit; _patch = patch; _root = root; _p = p; _ground = ground; _r = Mathf.Max(0.3f, radius);
            // r3: the cut runs ALONG the incoming direction and the lost share is the one on its right (r2 cut across it: the lost share was the near side and
            // the kept share stood right behind the body on the patch - from the shoulder camera nothing of "half" was left to see). Which half goes is the
            // look's choice (the engine halves the amount, it has no side); lost and kept now stand side by side on the screen.
            _dir = dir; _dir.y = 0f; _dir = _dir.sqrMagnitude > 1e-6f ? _dir.normalized : Vector3.forward;
            _dir = LostAxis(_dir); _side = Vector3.Cross(Vector3.up, _dir);
            Lost = Mathf.Clamp(lost, 0.15f, 0.85f); Chord = ChordFor(Lost); _cutAt = cutAt; _seed = seed;
            _acc = Vol2FsRun.Accent(contacted); _glow = Color.Lerp(Violet, _acc, 0.35f);
            Note = "half lost " + Lost.ToString("0.00") + " chord " + Chord.ToString("0.00");
        }

        /// <summary>r3: the axis whose NEGATIVE side is the lost share - the right of the incoming direction <paramref name="incoming"/> (flat, unit).</summary>
        public static Vector3 LostAxis(Vector3 incoming) => Vector3.Cross(incoming, Vector3.up).normalized;

        /// <summary>The chord h (in radii along Dir) whose near segment {x &lt; h} holds <paramref name="share"/> of a disc's area.</summary>
        public static float ChordFor(float share)
        {
            float lo = -1f, hi = 1f;
            for (int i = 0; i < 30; i++)
            {
                float h = (lo + hi) * 0.5f;
                float seg = (Mathf.PI - Mathf.Acos(Mathf.Clamp(h, -1f, 1f)) + h * Mathf.Sqrt(Mathf.Max(0f, 1f - h * h))) / Mathf.PI;      // area of {x < h} / pi
                if (seg < share) lo = h; else hi = h;
            }
            return (lo + hi) * 0.5f;
        }

        bool IsLost(Vector3 world) => Vector3.Dot(world - _p, _dir) / _r < Chord;

        /// <summary>Every frame (t = the reaction clock; endAt = the contact material's end on that clock, -1 alive; claimed = parts a cell animates itself).</summary>
        public void Tick(float t, float endAt, Vector3 cam)
        {
            if (_patch == null || _patch.Go == null || _root == null) return;
            if (!_cut && t >= _cutAt) Cut();
            if (!_cut) return;
            float a = t - _cutAt;
            float end = endAt >= 0f ? Vol2Fs.Smooth(endAt, endAt + 0.7f, t) : 0f;
            // the lost share: hinged on the chord it tilts down, slides back toward the struck side and sinks, crumbling (0.1 .. 1.7 s)
            if (_lostPivot != null)
            {
                float tilt = Vol2Fs.Smooth(0.06f, 0.75f, a), sink = Vol2Fs.Smooth(0.45f, 2.0f, a);      // r2: a bigger fall (r1 read as a small dip behind the bear)
                _lostPivot.rotation = Quaternion.AngleAxis(-78f * tilt, _side);      // r4: it flips off (r3 still mostly under the body)
                _lostPivot.position = H() - _dir * (1.7f * tilt + 0.4f * sink) + Vector3.up * 0.6f * Mathf.Sin(tilt * Mathf.PI) + Vector3.down * (0.1f * tilt + 0.95f * sink);
                if (a > 0.1f && a < 1.6f && Time.frameCount % 2 == 0) { Crumble(4); Vol2FsB1.EmitV(_smoke, _lostPivot.TransformPoint(-_dir * _r * 0.5f) + Vector3.up * 0.1f, Vector3.up * Random.Range(0.6f, 1.6f) - _dir * Random.Range(0.2f, 0.8f), -1f, -1f); }
                if (sink >= 0.999f && _lostPivot.gameObject.activeSelf) _lostPivot.gameObject.SetActive(false);
            }
            // the kept share leans away from the cut (wounded, still standing)
            float lean = Vol2Fs.Smooth(0.05f, 0.5f, a);
            foreach (var k in _kept) if (k.T != null && !_patch.Claimed.Contains(k.T)) k.T.localRotation = Quaternion.AngleAxis(k.Lean * lean, k.T.parent != null ? k.T.parent.InverseTransformDirection(_side) : _side) * k.Rot0;
            // the wound: a glowing torn edge along the chord + short cracks into the kept share, pulsing; it dims to the end and goes with the material
            _wound.Clear(); _torn.Clear();
            float open = Vol2Fs.Smooth(0f, 0.18f, a);
            float pulse = 0.7f + 0.3f * Mathf.Sin(t * 5.3f + _seed) * Mathf.Sin(t * 2.1f);
            float life = open * (1f - end) * Mathf.Lerp(1f, 0.6f, Vol2Fs.Smooth(0.6f, 2.5f, a));
            float half = Mathf.Sqrt(Mathf.Max(0f, 1f - Chord * Chord)) * _r * 1.05f;
            Vector3 c0 = H();
            if (life > 0.01f)
            {
                int n = 16; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n]; var sd = new Vector3[n];
                var tp = new Vector3[n]; var tw = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float u = k / (float)(n - 1);
                    float j = (Vol2Fs.Hash(_seed, k) - 0.5f) * 0.16f * _r;
                    pts[k] = c0 + _side * Mathf.Lerp(-half * 1.55f, half * 1.55f, u) + _dir * (j + 0.02f) + Vector3.up * 0.035f; sd[k] = _dir;
                    w[k] = (0.09f + 0.06f * Vol2Fs.Hash(_seed, k + 40)) * _r * Mathf.Sin(Mathf.Lerp(0.15f, 0.85f, u) * Mathf.PI) * 2.3f;      // r2: wider
                    al[k] = life * pulse;
                    tp[k] = pts[k] + _dir * 0.07f * _r; tw[k] = w[k] * 2.4f;
                }
                _torn.Strip(tp, tw, sd, Color.white, Fill(n, life), 0.4f, cam);
                _wound.Strip(pts, w, sd, Color.white, al, 0.17f, cam);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 from = c0 + _side * Mathf.Lerp(-half * 0.6f, half * 0.6f, (k + 0.5f) / 3f) + _dir * 0.03f;
                    Vector3 to = from + (_dir * (0.45f + 0.3f * Vol2Fs.Hash(_seed, k + 70)) + _side * (Vol2Fs.Hash(_seed, k + 80) - 0.5f) * 0.6f) * _r;
                    Vol2FsB6.Crack(_wound, from, to, _ground, 0.05f * _r + 0.02f, 0f, Vol2Fs.Smooth(0.05f, 0.4f, a), life * pulse * 0.8f, 0.3f + 0.1f * k, _seed + k, 0.06f, 0.035f);
                }
            }
            _wound.Write(_woundM); _torn.Write(_tornM);
            if (_woundLight != null) { _woundLight.transform.position = c0 + Vector3.up * 0.3f + _dir * 0.1f; _woundLight.intensity = (a < 0.15f ? 9f : 3.6f * pulse) * open * (1f - end); }
            // r2: the wound smokes (thin violet-black wisps rise from the torn edge while the patch lives)
            if (end < 0.5f && a > 0.3f && Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_smoke, c0 + _side * Random.Range(-half, half) * 0.85f + Vector3.up * 0.1f, Vector3.up * Random.Range(0.3f, 0.8f), Random.Range(0.18f, 0.35f), -1f);
            // ink drips from the wound (the patch keeps bleeding while it lives)
            if (end < 0.5f && a > 0.2f && Time.frameCount % 3 == 0)
                Vol2FsB1.EmitV(_drip, c0 + _side * Random.Range(-half, half) * 0.9f + Vector3.up * 0.06f, Vector3.up * Random.Range(0.3f, 1.1f) - _dir * Random.Range(0.1f, 0.5f), -1f, -1f);
        }

        static float[] Fill(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        Vector3 H() { var h = _p + _dir * Chord * _r; h.y = _ground; return h; }

        void Cut()
        {
            _cut = true;
            // materials / meshes of the wound
            var wm = Vol2FsMat.Make("fs_streak", _glow, 3.2f, 0.95f); wm.NoiseAmt = 0.35f; wm.Soft = 0.04f; wm.Erode = 0.18f; wm.Scroll = new Vector2(-0.6f, 0f);
            Vol2Fs.MeshObject(_root, "HalfWound", _kit.Mat(wm), out _woundM);
            Vol2Fs.MeshObject(_root, "HalfTorn", _kit.InkSolid("fs_stroke2", new Color(0.04f, 0.02f, 0.05f, 1f), _glow * 1.2f, 0.2f, 0.6f, 0.3f, 1.8f), out _tornM);
            _woundLight = Vol2Fs.PointLight(_root, "HalfWoundLight", _glow, 3.5f);
            var rock = _kit.SharedMesh("rxHalfCrumb", () => Vol2FsB1.Rock(931, 0.5f, 0.7f, 8, 6));
            _crumbs = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB8.PoolMat(_kit, _patch.Body, _patch.Enemy), Mesh = rock, Life0 = 0.7f, Life1 = 1.3f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.1f, Size1 = 0.28f, Gravity = 2.6f, Drag = 0.3f, Spin = 420f, Max = 320 }, _root, "HalfCrumbs");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(Vol2FsB3.Dust(new Color(0.3f, 0.2f, 0.34f, 1f), 0.55f)), Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.35f, Size1 = 0.8f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.7f, Drag = 1.6f, Max = 120, NoFlip = true }, _root, "HalfDust");
            var drip = Vol2FsMat.Make("fs_ink", new Color(0.05f, 0.02f, 0.06f, 1f), 1f, 0f); drip.Edge = _glow * 1.4f; drip.EdgeWidth = 0.2f; drip.NoiseAmt = 0.3f; drip.Soft = 0.1f;
            _drip = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(drip), Life0 = 0.5f, Life1 = 0.9f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.05f, Size1 = 0.11f, Gravity = 2.2f, Tiles = 2, Max = 120, NoFlip = true }, _root, "HalfDrip");
            _sparks = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(Vol2FsMat.Make("fs_dot", _glow, 4f, 1f)), Life0 = 0.25f, Life1 = 0.6f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.4f, Drag = 0.8f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.3f, Max = 300, NoFlip = true }, _root, "HalfSparks");
            _smoke = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(Vol2FsB3.Dust(new Color(0.12f, 0.06f, 0.15f, 1f), 0.6f)), Life0 = 1.0f, Life1 = 1.8f, Speed0 = 0f, Speed1 = 0.1f, Size0 = 0.3f, Size1 = 0.6f, SizeStart = 0.5f, SizePeak = 1.1f, SizeEnd = 1.8f, Drag = 1.2f, Noise = 0.6f, NoiseFreq = 1.2f, Max = 160, NoFlip = true }, _root, "HalfSmoke");
            foreach (var ps in new[] { _crumbs, _dust, _drip, _sparks, _smoke }) ps.Play(false);
            // the lost share's hinge
            var piv = new GameObject("HalfLost").transform; piv.SetParent(_patch.Go.transform, true); piv.SetPositionAndRotation(H(), Quaternion.identity); _lostPivot = piv;
            int moved = 0, split = 0, kept = 0;
            // every drawn piece of the patch: a small piece goes whole with its side, a wide one (the pool, a vine tangle, ripples) is cut along the chord
            var filters = new List<MeshFilter>(_patch.Go.GetComponentsInChildren<MeshFilter>(true));
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null || mf.transform.IsChildOf(piv)) continue;
                var tr = mf.transform;
                if (_patch.Claimed.Contains(tr)) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (mr == null) continue;
                var b = mr.bounds;
                bool wide = Mathf.Max(b.extents.x, b.extents.z) > _r * 0.45f;
                if (!wide)
                {
                    if (IsLost(tr.position)) { tr.SetParent(piv, true); moved++; }
                    else { _kept.Add((tr, tr.localRotation, 10f + 8f * Vol2Fs.Hash(_seed, kept + 11))); kept++; }
                    continue;
                }
                if (!mf.sharedMesh.isReadable) continue;
                if (SplitMesh(mf, mr, piv)) split++;
            }
            // the patch's own emitters (flames, embers, drips) keep to the kept share
            foreach (var ps in _patch.Ps)
            {
                if (ps == null) continue;
                var sh = ps.shape;
                Vector3 off = _dir * (Chord + 1f) * 0.5f * _r;      // the middle of the kept share
                sh.position = ps.transform.InverseTransformVector(off) + sh.position;
                sh.radius *= Mathf.Sqrt(1f - Lost) * 0.85f;
            }
            Note += " | cut: whole pieces moved " + moved + " kept " + kept + " split " + split;
            // the tear: sparks and dust along the chord, crumbs off the lost edge
            float half = Mathf.Sqrt(Mathf.Max(0f, 1f - Chord * Chord)) * _r;
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_sparks, H() + _side * Random.Range(-half, half) + Vector3.up * 0.08f, (Vector3.up * Random.Range(0.6f, 2f) + _dir * Random.Range(-1f, 1f) + _side * Random.Range(-0.6f, 0.6f)).normalized * Random.Range(3f, 8f), -1f, -1f);
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_dust, H() + _side * Random.Range(-half, half) + Vector3.up * 0.15f, Vector3.up * Random.Range(0.4f, 1.4f) - _dir * Random.Range(0.2f, 1.2f), -1f, -1f);
            Crumble(60);
        }

        void Crumble(int count)
        {
            float half = Mathf.Sqrt(Mathf.Max(0f, 1f - Chord * Chord)) * _r;
            for (int k = 0; k < count; k++)
            {
                // from the lost share's far rim and along the tear
                bool rim = k % 2 == 0;
                float ang = Random.Range(-1.2f, 1.2f);
                Vector3 at = rim ? _p + (-_dir * Mathf.Cos(ang) + _side * Mathf.Sin(ang)) * _r * 0.95f : H() + _side * Random.Range(-half, half);
                if (_lostPivot != null && rim) at = _lostPivot.TransformPoint(_lostPivot.InverseTransformPoint(at));
                at.y = Mathf.Max(at.y, _ground) + 0.05f;
                Vol2FsB1.EmitV(_crumbs, at, -_dir * Random.Range(0.3f, 1.8f) + Vector3.up * Random.Range(0.6f, 2.4f) + _side * Random.Range(-1f, 1f), Random.Range(0.08f, 0.2f), -1f);
            }
        }

        /// <summary>Cuts a wide piece along the chord: the kept triangles stay in its filter (a new mesh), the lost ones go to a twin under the hinge.</summary>
        bool SplitMesh(MeshFilter mf, MeshRenderer mr, Transform piv)
        {
            var src = mf.sharedMesh;
            var verts = src.vertices; var tris = src.triangles;
            if (verts == null || tris == null || tris.Length < 3) return false;
            var tr = mf.transform;
            var keep = new List<int>(tris.Length); var lose = new List<int>(tris.Length);
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 c = tr.TransformPoint((verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f);
                var list = IsLost(c) ? lose : keep;
                list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
            }
            if (lose.Count == 0) return false;
            Mesh Copy(List<int> idx, string name)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                if (verts.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.vertices = verts; var nr = src.normals; if (nr != null && nr.Length == verts.Length) m.normals = nr;
                var uv = new List<Vector3>(); src.GetUVs(0, uv); if (uv.Count == verts.Length) m.SetUVs(0, uv);
                var col = src.colors; if (col != null && col.Length == verts.Length) m.colors = col;
                m.SetTriangles(idx, 0); m.RecalculateBounds();
                return m;
            }
            mf.sharedMesh = Copy(keep, src.name + "_kept");
            var twin = new GameObject(tr.name + "_lost");
            twin.transform.SetParent(tr.parent, false);
            twin.transform.SetPositionAndRotation(tr.position, tr.rotation); twin.transform.localScale = tr.localScale;
            twin.transform.SetParent(piv, true);
            twin.AddComponent<MeshFilter>().sharedMesh = Copy(lose, src.name + "_lost");
            var r2 = twin.AddComponent<MeshRenderer>(); r2.sharedMaterials = mr.sharedMaterials; r2.shadowCastingMode = mr.shadowCastingMode; r2.receiveShadows = mr.receiveShadows;
            r2.lightProbeUsage = mr.lightProbeUsage; r2.reflectionProbeUsage = mr.reflectionProbeUsage;
            twin.layer = tr.gameObject.layer;
            return true;
        }
    }
}
