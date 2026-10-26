using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    /// <summary>걱 (wood, ㅓ attachment, 남긴다 PEAK = the self-recovery exception: HP 30 over 3 s, a hit ends it) - free showcase batch 3, concept geok_a (out of the
    /// ground at the caster's feet a young tree and vines sprout and spiral up AROUND the caster like a gentle embrace, leaves unfurling, small white blossoms opening,
    /// jade life-light and pollen motes flowing INTO the chest). It harms nothing and leaves nothing in the world. 걱 never reaches the vol.2 coordinator (the defence
    /// kit's Vol2RecoveryExecutor runs it), so this look is driven by the kit's own records (Begin / Tick / Damage / Cancel), read on the scope clock by the showcase
    /// installer, and lives exactly as long as the recovery runs: Begin = it sprouts; every Tick that really healed = a pulse of light into the body; the last 0.4 s of
    /// the 3 s = the blossoms close; the record that says it no longer runs = the exit of its reason (ran its course = the vines loosen and unravel into rootlets
    /// reaching forward over the ground, CSV 목 끝; a hit / cancel = the growth snaps and scatters at once). Not a Vol2FsRun (no cast instance); owned by the installer.</summary>
    public sealed class Vol2FsGeok
    {
        static readonly Color Jade = new Color(0.5f, 1f, 0.45f), Ink = new Color(0.03f, 0.035f, 0.025f);
        readonly Vol2FsKit _kit; readonly GameObject _root; readonly Func<Vector3> _pos, _fwd; readonly Func<Vector3?> _tip, _grip;
        public string Note = "";
        long _startUs, _lifeUs, _endUs = -1; string _endKind = ""; float _reserved;
        float _pulseAt = -10f; int _ticks;
        ParticleSystem _motes, _pollen, _petals, _leafFall, _blossoms, _glow; Light _light;
        readonly Vol2FsTubes _vines = new Vol2FsTubes(), _trunk = new Vol2FsTubes(), _roots = new Vol2FsTubes();
        Mesh _vineMesh, _trunkMesh, _rootMesh, _leafMesh, _mossMesh, _ribbonMesh;
        readonly Vol2FsLeaves _leaves = new Vol2FsLeaves(); readonly Vol2FsStrips _moss = new Vol2FsStrips(), _ribbon = new Vol2FsStrips();
        readonly List<float> _leafAt = new List<float>();
        readonly List<Vector3> _blossomPts = new List<Vector3>();
        Vector3 _feet, _f, _r; bool _built, _blossomed, _scattered;
        long _nowUs;

        public Vol2FsGeok(Vol2FsKit kit, Transform parent, Func<Vector3> casterPos, Func<Vector3> casterFwd, Func<Vector3?> tip, Func<Vector3?> grip)
        {
            _kit = kit; _pos = casterPos; _fwd = casterFwd; _tip = tip; _grip = grip;
            _root = new GameObject("Vol2FreeShowcase_걱");
            if (parent != null) _root.transform.SetParent(parent, false);
            _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        float Rel(long us) => (float)((us - _startUs) / 1e6);
        Vector3 Chest => _pos() + Vector3.up * 1.2f;
        static Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsMat.Make(tex, tint, i, a);
        Material Bark => _kit.Bark(new Color(0.55f, 0.68f, 0.38f), new Color(0.2f, 0.5f, 0.1f), 0.18f, "fs_bark", "fs_bark");      // r4: the whole vine faintly lit green (r3: still dark spikes against the sky)      // r2: green living vine (r1 near-black from behind)

        /// <summary>The recovery began (its own StartedUs and life from the kit's ledger).</summary>
        public void Begin(long startUs, long lifeUs, long nowUs, float reserved)
        {
            _startUs = startUs; _lifeUs = Math.Max(500000, lifeUs); _reserved = reserved; _nowUs = nowUs;
            _feet = _pos(); var f = _fwd(); f.y = 0f; _f = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward; _r = Vector3.Cross(Vector3.up, _f);
            Note = "begin life " + (_lifeUs / 1e6).ToString("0.0") + " s reserved " + reserved.ToString("0");
            Build();
        }

        /// <summary>A later record of the same recovery: a heal tick (a pulse), or the end (Active = false).</summary>
        public void Record(Vol2RecoveryRecord rec, long nowUs)
        {
            if (_endUs >= 0) return;
            if (rec.Kind == "Tick" && rec.Accepted && rec.HpHealed > 0m) { _pulseAt = Rel(nowUs); _ticks++; }
            if (!rec.Active && rec.Kind != "Rest" && rec.Kind != "Begin") End(rec.Kind == "Tick" ? "Complete" : rec.Kind == "Damage" ? "Interrupted" : rec.Kind, nowUs);
        }

        public void End(string kind, long nowUs)
        {
            if (_endUs >= 0) return;
            _endUs = nowUs; _endKind = kind;
            // r2: a Cancel (death, a scene leave, the lab reset) is a hard stop - the look goes at once (r1: its leaves drifted into the next capture job)
            if (kind == "Cancel" && _root != null) _root.SetActive(false);
            Note += " | end " + kind + " at " + Rel(nowUs).ToString("0.00") + " ticks " + _ticks;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            var root = _root.transform;
            var motes = M("fs_dot", Jade, 3.5f, 1f);
            _motes = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(motes), Rate = 70f, Loop = true, Life0 = 0.5f, Life1 = 0.8f, Speed0 = -1.6f, Speed1 = -2.6f, Size0 = 0.04f, Size1 = 0.1f, Radius = 1.4f, World = false, Max = 160 }, root, "Motes");
            var pollen = M("fs_dot", new Color(1f, 0.95f, 0.7f), 2.5f, 1f);
            _pollen = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(pollen), Rate = 30f, Loop = true, Life0 = 1.2f, Life1 = 2f, Speed0 = 0.1f, Speed1 = 0.4f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.9f, Noise = 0.8f, NoiseFreq = 0.8f, VelOverLife = new Vector3(0f, 0.25f, 0f), Max = 120 }, root, "Pollen");
            var petal = M("fs_petal", new Color(1f, 0.97f, 0.93f), 1.3f, 0.15f); petal.NoiseAmt = 0.2f; petal.Erode = 0.15f; petal.Soft = 0.05f;
            _blossoms = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(petal), Life0 = 1f, Life1 = 1f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.18f, Size1 = 0.28f, Radius = 0.01f, Tiles = 1, SizeStart = 0.1f, SizePeak = 1f, SizePeakAt = 0.25f, SizeEnd = 0.9f, AlphaHoldTo = 0.85f, Max = 80 }, root, "Blossoms");
            _petals = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(petal), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.05f, Size1 = 0.1f, Radius = 0.3f, Gravity = 0.15f, Drag = 1.2f, Noise = 1f, NoiseFreq = 0.7f, Spin = 300f, AlphaHoldTo = 0.7f, Max = 200 }, root, "Petals");
            var leaf = M("fs_leaf", new Color(0.9f, 1f, 0.75f, 1f), 1f, 0f); leaf.NoiseAmt = 0.15f; leaf.Erode = 0.1f; leaf.Soft = 0.05f;
            _leafFall = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(leaf), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 0.5f, Speed1 = 2.5f, Size0 = 0.1f, Size1 = 0.22f, Radius = 0.4f, Gravity = 0.3f, Drag = 1.6f, Noise = 1.2f, NoiseFreq = 0.8f, Tiles = 2, Spin = 240f, AlphaHoldTo = 0.75f, Max = 260 }, root, "LeafFall");
            var glow = M("fs_dot", Jade, 0.8f, 1f); glow.Soft = 0.6f;
            _glow = Vol2Fs.Build(new Vol2FsPs { Mat = _kit.Mat(glow), Rate = 20f, Loop = true, Life0 = 0.25f, Life1 = 0.35f, Speed0 = 0f, Speed1 = 0f, Size0 = 1.0f, Size1 = 1.3f, Radius = 0.15f, Max = 20, SortFudge = 20 }, root, "Glow");
            foreach (var ps in new[] { _motes, _pollen, _blossoms, _petals, _leafFall, _glow }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "LifeLight", Jade, 4f);
            int seed = (int)(_startUs & 0x7fff);
            // three vines spiralling up round the caster (0.45 m out), leaves along them; a young sapling behind-right of the caster with a crown of leaves and blossoms
            for (int j = 0; j < 3; j++)
            {
                var pts = new Vector3[9];
                for (int k = 0; k < 9; k++)
                {
                    float u = k / 8f, a = j * 2.09f + u * 5.2f;
                    float rad = Mathf.Lerp(0.55f, 0.36f, u);
                    pts[k] = _feet + (_r * Mathf.Cos(a) + _f * Mathf.Sin(a)) * rad + Vector3.up * (u * 1.75f - 0.05f);
                    if (k > 0)
                    {
                        Vector3 nrm = (_r * Mathf.Cos(a) + _f * Mathf.Sin(a));
                        _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = pts[k], Normal = nrm, Up = (Vector3.up + nrm * 0.6f).normalized, Size = 0.3f, Grow = 0f, Cell = (j + k) % 4 }); _leafAt.Add(0.2f + u * 0.5f);
                        if (k % 2 == 0) _blossomPts.Add(pts[k] + nrm * 0.1f + Vector3.up * 0.05f);
                    }
                }
                var tb = _vines.Add(pts, 0.075f, 0.035f, 28); tb.Knots = 0.35f; tb.Sharp = 0.06f; tb.Sides = 6;      // r2: thicker, blunt tips (r1 read as dark thorn spikes)
            }
            Vector3 sb = _feet - _f * 0.45f + _r * 0.55f;
            var trunk = _trunk.Add(new[] { sb + Vector3.down * 0.1f, sb + Vector3.up * 0.8f + _r * 0.05f, sb + Vector3.up * 1.6f - _r * 0.08f, sb + Vector3.up * 2.2f + _f * 0.15f }, 0.09f, 0.025f, 18); trunk.Knots = 0.45f; trunk.Sharp = 0.3f; trunk.Sides = 7;
            for (int k = 0; k < 5; k++)
            {
                float a = k * 1.3f; Vector3 o = _r * Mathf.Cos(a) + _f * Mathf.Sin(a);
                Vector3 s0 = sb + Vector3.up * (1.1f + 0.22f * k), s1 = s0 + o * 0.45f + Vector3.up * 0.35f;
                var br = _trunk.Add(new[] { s0, s1, s1 + o * 0.25f + Vector3.up * 0.2f }, 0.035f, 0.01f, 8); br.Sharp = 0.2f; br.Sides = 5;
                for (int j = 0; j < 4; j++)
                {
                    Vector3 nrm = Quaternion.AngleAxis(Vol2Fs.Hash(seed, k * 4 + j) * 360f, Vector3.up) * Vector3.forward;
                    _leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = Vector3.Lerp(s0, s1, 0.4f + 0.2f * j) + o * 0.1f * j, Normal = nrm, Up = (Vector3.up + nrm).normalized, Size = 0.22f, Grow = 0f, Cell = (k + j) % 4 }); _leafAt.Add(0.45f + 0.06f * k);
                }
                _blossomPts.Add(s1 + Vector3.up * 0.1f);
            }
            _vineR = Vol2Fs.MeshObject(root, "Vines", Bark, out _vineMesh);
            Vol2Fs.MeshObject(root, "Sapling", Bark, out _trunkMesh);
            Vol2Fs.MeshObject(root, "Rootlets", Bark, out _rootMesh);
            Vol2Fs.MeshObject(root, "Leaves", _kit.Leaf(new Color(0.7f, 1f, 0.45f), new Color(0.3f, 0.85f, 0.12f)), out _leafMesh);
            var moss = M("fs_moss", Color.white, 1f, 0f); moss.Soft = 0.001f; moss.NoiseAmt = 0.35f; moss.Erode = 0.25f; moss.Edge = new Color(0.4f, 1.2f, 0.25f, 1f); moss.EdgeWidth = 0.08f;
            Vol2Fs.MeshObject(root, "Moss", _kit.Mat(moss), out _mossMesh);
            var rib = M("fs_streak", Jade, 3f, 1f); rib.NoiseAmt = 0.3f;
            Vol2Fs.MeshObject(root, "LifeRibbons", _kit.Mat(rib), out _ribbonMesh);
        }
        MeshRenderer _vineR;

        public void Tick(long nowUs)
        {
            if (_root == null) return;
            _nowUs = nowUs;
            float t = Rel(nowUs), life = _lifeUs / 1e6f;
            float ex = _endUs < 0 ? 0f : Mathf.Clamp01((t - Rel(_endUs)) / ExitSeconds);
            bool complete = _endKind == "Complete";
            float warn = _endUs >= 0 ? 0f : Vol2Fs.Smooth(life - 0.4f, life, t);
            float pulse = Mathf.Clamp01(1f - (t - _pulseAt) / 0.35f);
            Vector3 feet = _pos(), chest = feet + Vector3.up * 1.2f;
            // the growth: vines spiral up in 0.6 s, the sapling in 0.8 s; complete = loosen and sink; interrupted = snap
            float g = Vol2Fs.EaseOutBack(Mathf.Clamp01(t / 0.6f)), gs = Vol2Fs.EaseOutBack(Mathf.Clamp01((t - 0.1f) / 0.8f));
            float loosen = complete ? Vol2Fs.Smooth(0f, 0.75f, ex) : Vol2Fs.Smooth(0f, 0.25f, ex);
            foreach (var v in _vines.Tubes) v.Grow = g * (1f - loosen);
            foreach (var v in _trunk.Tubes) v.Grow = gs * (1f - loosen);
            _vines.Write(_vineMesh); _trunk.Write(_trunkMesh);
            for (int k = 0; k < _leaves.Items.Count; k++) { var l = _leaves.Items[k]; l.Grow = Vol2Fs.Smooth(_leafAt[k], _leafAt[k] + 0.3f, t) * (1f - loosen) * (1f + 0.15f * pulse); _leaves.Items[k] = l; }
            _leaves.Write(_leafMesh);
            // blossoms open after 0.7 s (white petals), close in the last 0.4 s
            if (!_blossomed && t > 0.7f && _endUs < 0) { _blossomed = true; foreach (var p in _blossomPts) _blossoms.Emit(new ParticleSystem.EmitParams { position = p, startLifetime = Mathf.Max(0.5f, life - 0.7f), applyShapeToPosition = false }, 1); }
            // jade light and motes flowing INTO the chest; each real heal tick = a pulse
            var em = _motes.emission; em.rateOverTime = _endUs < 0 ? 70f * (1f - 0.6f * warn) : 0f;
            _motes.transform.position = chest;
            var gem = _glow.emission; gem.rateOverTime = _endUs < 0 ? 20f : 0f;
            _glow.transform.position = chest;
            var pem = _pollen.emission; pem.rateOverTime = _endUs < 0 ? 30f : 0f;
            _pollen.transform.position = chest + Vector3.up * 0.2f;
            _ribbon.Clear();
            if (_endUs < 0 && t > 0.3f)
                for (int j = 0; j < 4; j++)
                {
                    // ribbons of life-light spiralling in from the vines to the chest
                    var pts = new Vector3[10]; var w = new float[10]; var al = new float[10];
                    for (int k = 0; k < 10; k++)
                    {
                        float u = k / 9f, a = j * 1.57f + t * 2.2f + u * 3f;
                        float rad = Mathf.Lerp(0.75f, 0.05f, u);
                        pts[k] = chest + (_r * Mathf.Cos(a) + _f * Mathf.Sin(a)) * rad + Vector3.up * (0.5f - 0.6f * u) * (1f - u * 0.5f);
                        w[k] = 0.06f * (1f - 0.6f * u); al[k] = (0.4f + 0.6f * u) * (0.6f + 0.6f * pulse) * (1f - warn);
                    }
                    _ribbon.Strip(pts, w, null, Color.white, al, 0.2f * j, CamPos(), 0f, 1f);
                }
            _ribbon.Write(_ribbonMesh);
            // the moss ring at the feet
            _moss.Clear();
            float ml = Vol2Fs.Smooth(0f, 0.5f, t) * (1f - Vol2Fs.Smooth(0.4f, 1f, ex));
            if (ml > 0.01f) _moss.Quad(new Vector3(feet.x, feet.y + 0.03f, feet.z), _r, _f, 2.6f, new Color(1f, 1f, 1f, ml), 0.33f);
            _moss.Write(_mossMesh);
            _light.transform.position = chest;
            _light.intensity = _endUs < 0 ? (2.2f + 3.5f * pulse) * Mathf.Clamp01(t / 0.3f) * (1f - 0.5f * warn) : Mathf.Max(0f, 2.2f * (1f - ex * 2f));
            // the ends
            if (_endUs >= 0 && !_scattered)
            {
                _scattered = true;
                var bem = _blossoms; bem.Clear(false);
                if (complete)
                {
                    // ran its course: the petals drift down, rootlets unravel forward over the ground (목 끝: 잔뿌리로 풀림, 앞으로 뻗으며)
                    foreach (var p in _blossomPts) _petals.Emit(new ParticleSystem.EmitParams { position = p, applyShapeToPosition = true }, 3);
                    for (int k = 0; k < 9; k++)
                    {
                        float a = (k / 8f - 0.5f) * 1.6f; Vector3 o = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * _f;
                        var pts = new Vector3[5];
                        for (int j = 0; j < 5; j++) { float u = j / 4f; pts[j] = feet + o * (0.4f + 2.6f * u) + Vector3.Cross(Vector3.up, o) * Mathf.Sin(u * 5f + k) * 0.2f; pts[j].y = feet.y + 0.03f + 0.04f * (1f - u); }
                        var tb = _roots.Add(pts, 0.06f, 0.01f, 12); tb.Knots = 0.4f; tb.Sharp = 0.5f; tb.Sides = 5;
                    }
                }
                else
                {
                    // a hit cut it short: the growth snaps and scatters at once
                    _leafFall.Emit(new ParticleSystem.EmitParams { position = chest, applyShapeToPosition = true }, 70);
                    _petals.Emit(new ParticleSystem.EmitParams { position = chest, applyShapeToPosition = true }, 40);
                }
            }
            if (_roots.Tubes.Count > 0)
            {
                float rg = Vol2Fs.Smooth(0.05f, 0.6f, ex), rf = Vol2Fs.Smooth(0.75f, 1f, ex);
                foreach (var r in _roots.Tubes) r.Grow = rg * (1f - rf);
                _roots.Write(_rootMesh);
            }
            if (complete && ex > 0f && ex < 0.6f && Time.frameCount % 3 == 0) _leafFall.Emit(new ParticleSystem.EmitParams { position = chest + UnityEngine.Random.insideUnitSphere * 0.5f, applyShapeToPosition = true }, 1);
        }

        float ExitSeconds => _endKind == "Complete" ? 1.3f : _endKind == "Replaced" ? 0.3f : 0.35f;
        Vector3 CamPos() { var c = Camera.main; return c != null ? c.transform.position : _feet - _f * 3f + Vector3.up * 2f; }

        public bool Done(long nowUs)
        {
            if (_root == null) return true;
            if (_endKind == "Cancel") return true;
            if (_endUs < 0) return Rel(nowUs) > _lifeUs / 1e6f + 6f;          // a lost end record: never longer than the recovery's own life + 6 s
            float a = Rel(nowUs) - Rel(_endUs);
            if (a < ExitSeconds + 0.3f) return false;
            foreach (var ps in _root.GetComponentsInChildren<ParticleSystem>()) if (ps.IsAlive(false)) return a > ExitSeconds + 2.5f;
            return true;
        }

        public void Dispose()
        {
            if (_root == null) return;
            foreach (var f in _root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && (_kit == null || !_kit.Owns(f.sharedMesh))) Object.Destroy(f.sharedMesh);
            Object.Destroy(_root);
        }
    }
}
