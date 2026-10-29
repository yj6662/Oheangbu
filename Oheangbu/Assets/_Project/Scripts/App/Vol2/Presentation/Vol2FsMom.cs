using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>몸 (earth, ㅗ fan, 심는다 - the PEAK, the one summon: the earth giant) - free showcase batch 5, concept mom_a (in the fan an earth giant has risen
    /// from where it crouched dormant like a mound - a hulking figure of rock and soil with glowing cracks - and brings a huge fist down on the two bears, the
    /// ground exploding in rock and dust under the blow). Readable over detailed: kit rock meshes (chipped stone, ember seams) as torso / head / legs / two arms.
    /// Executor facts only: the start = the mound heaves up out of the far fan (dust, chunks) -> the wait (the giant crouches as a mound, its ember eyes and seams
    /// breathe on the seed's heartbeat, dust trickles off it; the fan's edges and the far edge's burning-down ink arc are the countdown; the last 0.4 s it shudders)
    /// -> Consumed (a body came near: the executor strikes every body in the fan at once, 1.8, and collapses the giant into the rubble material) = it rises (0.3 s),
    /// the right arm sweeps up and the fist slams down on the struck bodies (impact 0.42 s after the fact - the presentation of that one strike), a hit-stop, a dust
    /// blast, rock chunks, low heavy ink, a crack ring, every struck body shoved -> the giant breaks apart and falls into the rubble field (the executor's Residue
    /// giant-rubble, 10 s: from its creation to its end) that lies there, dust wisping; its last 0.4 s it trembles; its natural end = the rubble sinks while the
    /// ground round it lifts as plates and settles (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림(떨어지는 것 없음)). Expired (no body came) = the crouching giant sinks
    /// back into the ground with the same plates; Replaced (a newer giant stood up) = it crumbles at once.</summary>
    public sealed class Vol2FsMom : Vol2FsSeedFan
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Seam = new Color(1f, 0.5f, 0.18f);
        const float Rise = 0.3f, SlamAt = 0.42f, BreakAt = 1.0f, FallSeconds = 0.6f;
        ParticleSystem _chunks, _grit, _cloud, _ring, _eyes, _ink;
        Material _stone, _rubble, _plateMat;
        Mesh _crackMesh, _inkMesh; readonly Vol2FsStrips _crackS = new Vol2FsStrips(), _inkS = new Vol2FsStrips();
        Light _eyeLight, _seamLight;
        sealed class Part { public Transform T; public Vector3 Crouch, Stand, Scale, RubbleAt; public Quaternion RubbleRot; public float Rnd; public bool Arm; }
        readonly List<Part> _parts = new List<Part>();
        readonly List<(Transform T, Vector3 Base, Quaternion Rot)> _plates = new List<(Transform, Vector3, Quaternion)>();
        readonly List<Transform> _extra = new List<Transform>();
        readonly List<Struck> _hit = new List<Struck>();
        Vector3 _base, _fc, _rt, _target; bool _built, _impacted, _broken, _platesBuilt; float _platesAt = -1f; List<Vol2FsB1.Stroke> _burst;
        Transform _upper, _fist, _upperL, _fistL;
        protected override int SpotCount => 0;
        protected override float FireSeconds => BreakAt + FallSeconds + 0.4f;
        protected override float ExpireSeconds => 1.6f;
        protected override float AfterExitSeconds => 1.6f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.66f, 0.55f, 0.46f), 0f, 0.1f, Seam * 0.9f, "fs_vein");
            _rubble = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.58f, 0.48f, 0.4f), 0f, 0.1f, Seam * 0.25f, "fs_vein");
            _plateMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.9f, Life1 = 1.7f, Speed0 = 3f, Speed1 = 8f, Size0 = 0.12f, Size1 = 0.38f, Radius = 0.5f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 400 }, root, "Chunks");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.6f, Size0 = 0.03f, Size1 = 0.07f, Radius = 0.3f, Gravity = 2f, Spin = 400f, Max = 400 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.6f)), Life0 = 1.3f, Life1 = 2.4f, Speed0 = 1f, Speed1 = 4f, Size0 = 1.2f, Size1 = 2.6f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.8f, Drag = 2f, Tiles = 2, SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 1.8f, Max = 220, SortFudge = 10 }, root, "Cloud");
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.5f)), Life0 = 0.7f, Life1 = 1.3f, Speed0 = 6f, Speed1 = 11f, Size0 = 0.7f, Size1 = 1.4f, Shape = ParticleSystemShapeType.Circle, Radius = 0.8f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 3f, Tiles = 2, SizeEnd = 2f, Max = 240, SortFudge = 8 }, root, "DustRing");
            _eyes = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Seam, 3.5f, 1f)), Life0 = 0.08f, Life1 = 0.12f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.18f, Size1 = 0.24f, Max = 60 }, root, "Eyes");
            var ink = M("fs_ink", new Color(0.04f, 0.03f, 0.025f, 1f), 1f, 0f); ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            _ink = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 3f, Speed1 = 7f, Size0 = 0.15f, Size1 = 0.45f, Radius = 0.4f, Gravity = 2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 300 }, root, "InkDrops");
            foreach (var ps in new[] { _chunks, _grit, _cloud, _ring, _eyes, _ink }) ps.Play(false);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkV", Kit.InkSolid("fs_stroke2", InkC, new Color(0.9f, 0.55f, 0.25f, 1f), 0.08f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            _eyeLight = Vol2Fs.PointLight(root, "EyeLight", Seam, 3.5f);
            _seamLight = Vol2Fs.PointLight(root, "SeamLight", Seam, 5f);
        }

        Vector3 W(Vector3 l) => _base + _rt * l.x + Vector3.up * l.y + _fc * l.z;

        Part Add(string name, Vector3 crouch, Vector3 stand, Vector3 scale, int seed, bool arm = false)
        {
            var mesh = Kit.SharedMesh("momRock" + (seed % 4), () => Vol2FsB1.Rock(700 + seed % 4, 0.5f, 0.85f, 12, 8));
            var r = Vol2FsB1.Prop(Root.transform, name, mesh, _stone);
            var p = new Part { T = r.transform, Crouch = crouch, Stand = stand, Scale = scale, Rnd = Vol2Fs.Hash(Seed, seed + 300), Arm = arm };
            // where it lies in the rubble: dropped round the base, spread out and tipped
            float a = p.Rnd * Mathf.PI * 2f, d = 0.4f + 1.4f * Vol2Fs.Hash(Seed, seed + 310);
            p.RubbleAt = new Vector3(Mathf.Cos(a) * d, scale.y * 0.25f, Mathf.Sin(a) * d);
            p.RubbleRot = Quaternion.Euler(60f * Vol2Fs.Hash(Seed, seed + 320), 360f * p.Rnd, 70f * Vol2Fs.Hash(Seed, seed + 330));
            _parts.Add(p);
            return p;
        }

        void Build()
        {
            _built = true;
            _fc = -F; _rt = Vector3.Cross(Vector3.up, _fc);
            _base = G(InFan(0.96f, 0.05f));
            // crouch = a mound (torso low, head tucked, arms folded at its sides, legs under the ground); stand = 3.4 m
            Add("Torso", new Vector3(0f, 0.55f, 0f), new Vector3(0f, 2.0f, 0f), new Vector3(1.9f, 1.6f, 1.4f), 0);
            Add("Head", new Vector3(0f, 1.05f, 0.45f), new Vector3(0f, 3.05f, 0.25f), new Vector3(0.7f, 0.65f, 0.7f), 1);
            Add("Hips", new Vector3(0f, 0.2f, -0.2f), new Vector3(0f, 1.15f, 0f), new Vector3(1.4f, 0.8f, 1.1f), 2);
            Add("LegL", new Vector3(-0.5f, -0.6f, 0f), new Vector3(-0.55f, 0.45f, 0f), new Vector3(0.7f, 1.1f, 0.7f), 3);
            Add("LegR", new Vector3(0.5f, -0.6f, 0f), new Vector3(0.55f, 0.45f, 0f), new Vector3(0.7f, 1.1f, 0.7f), 4);
            Add("Shoulder", new Vector3(0f, 0.75f, 0.1f), new Vector3(0f, 2.55f, 0f), new Vector3(2.4f, 0.8f, 1.1f), 5);
            _upperL = Add("UpperL", Vector3.zero, Vector3.zero, new Vector3(0.6f, 0.6f, 1.2f), 6, true).T;
            _fistL = Add("FistL", new Vector3(-1.1f, 0.25f, 0.5f), new Vector3(-1.25f, 0.9f, 0.45f), new Vector3(0.75f, 0.75f, 0.75f), 7).T;
            _upper = Add("UpperR", Vector3.zero, Vector3.zero, new Vector3(0.65f, 0.65f, 1.3f), 8, true).T;
            _fist = Add("FistR", new Vector3(1.1f, 0.25f, 0.5f), new Vector3(1.25f, 0.9f, 0.45f), new Vector3(0.95f, 0.9f, 0.95f), 9).T;
            for (int k = 0; k < 10; k++)
            {
                var r = Vol2FsB1.Prop(Root.transform, "Rubble" + k, Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5)), _rubble);
                float a = Vol2Fs.Hash(Seed, k + 400) * Mathf.PI * 2f, d = 0.8f + 1.8f * Vol2Fs.Hash(Seed, k + 410), s = 0.35f + 0.4f * Vol2Fs.Hash(Seed, k + 420);
                r.transform.SetPositionAndRotation(_base + new Vector3(Mathf.Cos(a) * d, s * 0.2f, Mathf.Sin(a) * d), Quaternion.Euler(0f, a * 57f, 20f));
                r.transform.localScale = Vector3.one * s; r.gameObject.SetActive(false); _extra.Add(r.transform);
            }
        }

        protected override void Planted(Pod p)
        {
            if (!_built) Build();
            Vol2FsB1.Emit(_cloud, _base, 6); Vol2FsB1.Emit(_chunks, _base + Vector3.up * 0.3f, 30);
            _seamLight.transform.position = _base + Vector3.up * 1.2f + _fc * 0.8f;
        }

        protected override void SpotBurst(Spot s) { }

        protected override void OnStruckBody(Pod p, Struck s) { _hit.Add(s); }

        protected override void Fired(Pod p)
        {
            if (!_built) Build();
            Vol2FsB1.Emit(_cloud, _base, 8); Vol2FsB1.Emit(_grit, _base + Vector3.up, 60);
        }

        void BuildPlates(float r0, float r1)
        {
            if (_platesBuilt) return;
            _platesBuilt = true;
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f + Vol2Fs.Hash(Seed, k + 500);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Vector3.Dot(o, _fc) > 0.85f) continue;                                    // none right in front of the camera
                var pr = Vol2FsB1.Prop(Root.transform, "Plate" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _plateMat);
                pr.transform.localScale = new Vector3(Mathf.Lerp(0.7f, 1.1f, Vol2Fs.Hash(Seed, k + 530)), 1f, 0.8f);
                _plates.Add((pr.transform, _base + o * Mathf.Lerp(r0, r1, Vol2Fs.Hash(Seed, k + 560)) + Vector3.down * 0.2f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        /// <summary>Places an arm: upper segment from the shoulder to the elbow, the fist at <paramref name="fist"/>.</summary>
        void Arm(Transform upper, Transform fistT, Vector3 shoulder, Vector3 fist, float stand)
        {
            Vector3 elbow = Vector3.Lerp(shoulder, fist, 0.5f) + (Vector3.up * 0.3f + (shoulder - _base).normalized * 0.25f) * stand;
            Vector3 seg = fist - shoulder;
            upper.SetPositionAndRotation(Vector3.Lerp(shoulder, elbow, 0.6f), Quaternion.LookRotation(seg.sqrMagnitude > 1e-4f ? seg.normalized : _fc, Vector3.up));
            upper.localScale = new Vector3(upper.localScale.x, upper.localScale.y, Mathf.Max(0.4f, seg.magnitude * 0.85f));
            fistT.position = fist;
        }

        protected override void Live(float t, float h)
        {
            if (!_built) return;
            var cam = CamPos; var p = The;
            _crackS.Clear(); _inkS.Clear();
            if (p == null || !p.Started) { foreach (var pt in _parts) pt.T.gameObject.SetActive(false); _crackS.Write(_crackMesh); _inkS.Write(_inkMesh); return; }
            bool waiting = !p.Ended;
            float beat = Beat(p), warn = PodWarn(p, t), age = PodAge(p, t);
            float heave = Vol2Fs.Smooth(0f, 0.45f, age);
            float stand = 0f, sink = 0f, fall = 0f; Vector3 rFist;
            float s = SinceFire(p, t);
            if (Worked(p))
            {
                stand = Vol2Fs.Smooth(0f, Rise, s);
                fall = Vol2Fs.Smooth(BreakAt, BreakAt + FallSeconds, s);
                if (!_impacted && s >= SlamAt)
                {
                    _impacted = true; HitStop(t, 0.1f);
                    _target = _hit.Count > 0 ? Vector3.zero : EntryAt;
                    if (_hit.Count > 0) { foreach (var b in _hit) _target += FeetOf(b); _target /= _hit.Count; }
                    _target = G(_target);
                    Vol2FsB1.Emit(_cloud, _target, 14); Vol2FsB1.Emit(_ring, _target + Vector3.up * 0.1f, 90); Vol2FsB1.Emit(_chunks, _target + Vector3.up * 0.3f, 110); Vol2FsB1.Emit(_ink, _target + Vector3.up * 0.4f, 60);
                    foreach (var b in _hit) { Vector3 c = CentreNow(b); Vol2FsB1.Emit(_cloud, c, 3); Vol2FsB1.Emit(_chunks, c, 20); }
                    Vector3 side = Vector3.Cross(Vector3.up, FlatN(cam - _target));
                    _burst = Vol2FsB1.Burst(Seed + 77, 14, side, -FlatN(cam - _target), 1.8f, 3.8f, 0.3f, 0.6f, 0.2f, 0.2f);
                }
                if (!_broken && s >= BreakAt) { _broken = true; Vol2FsB1.Emit(_cloud, _base + Vector3.up, 10); Vol2FsB1.Emit(_chunks, _base + Vector3.up * 1.5f, 80); }
            }
            else if (p.Ended)
            {
                float ex = PodExit(p, t);
                if (p.Why == Vol2MaterialEnd.Replaced) fall = Vol2Fs.Smooth(0f, 0.6f, ex);
                else { sink = Vol2Fs.Smooth(0f, 0.8f, ex); if (!_platesBuilt) { BuildPlates(1.6f, 2.6f); _platesAt = p.EndAt; } }
            }
            // the right arm: hangs at the crouch, sweeps up over the head while it rises, slams onto the target, stays planted until the break
            Vector3 shR = W(Vector3.Lerp(new Vector3(0.95f, 0.75f, 0.1f), new Vector3(1.1f, 2.6f, 0f), stand)), shL = W(Vector3.Lerp(new Vector3(-0.95f, 0.75f, 0.1f), new Vector3(-1.1f, 2.6f, 0f), stand));
            Vector3 up = W(new Vector3(1.0f, 4.3f, -0.4f)), rest = W(Vector3.Lerp(new Vector3(1.1f, 0.25f, 0.5f), new Vector3(1.25f, 0.9f, 0.45f), stand));
            if (!Worked(p)) rFist = rest;
            else if (s < Rise) rFist = Vector3.Lerp(rest, up, Vol2Fs.Smooth(0f, Rise, s));
            else if (s < SlamAt) { float k = (s - Rise) / (SlamAt - Rise); Vector3 tgt = _hit.Count > 0 ? G(CentreNow(_hit[0])) : EntryAt; rFist = Vector3.Lerp(up, tgt + Vector3.up * 0.45f, k * k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.6f; }
            else rFist = _target + Vector3.up * 0.45f + (s < BreakAt ? Random.insideUnitSphere * 0.02f : Vector3.zero);
            Vector3 lFist = W(Vector3.Lerp(new Vector3(-1.1f, 0.25f, 0.5f), new Vector3(-1.35f, 1.0f, 0.55f), stand));
            Vector3 shake = (waiting && warn > 0f ? Random.insideUnitSphere * 0.05f * warn : Vector3.zero) + Vector3.down * 1.4f * sink;
            float breathe = waiting ? 1f + 0.035f * beat : 1f;
            foreach (var pt in _parts)
            {
                bool on = (waiting || Worked(p) || sink < 0.99f && fall < 0.99f || fall > 0f && fall < 1f) && heave > 0.01f;
                if (pt.T == _upper || pt.T == _upperL)
                {
                    bool r = pt.T == _upper; Arm(pt.T, r ? _fist : _fistL, r ? shR : shL, r ? rFist : lFist, stand);
                    pt.T.localScale = new Vector3(pt.Scale.x, pt.Scale.y, pt.T.localScale.z);
                }
                else if (pt.T != _fist && pt.T != _fistL)
                {
                    pt.T.position = W(Vector3.Lerp(pt.Crouch, pt.Stand, stand)) + Vector3.down * 1.2f * (1f - heave);
                    pt.T.rotation = Quaternion.LookRotation(_fc, Vector3.up) * Quaternion.Euler(8f * (1f - stand), pt.Rnd * 40f - 20f, 0f);
                    pt.T.localScale = pt.Scale * breathe;
                }
                else pt.T.rotation = Quaternion.LookRotation(_fc, Vector3.up);
                if (pt.T == _fist || pt.T == _fistL) pt.T.localScale = pt.Scale;
                pt.T.position += shake;
                // the break: every part falls into the rubble (its own spot round the base, tipped)
                if (fall > 0f)
                {
                    Vector3 lie = _base + _rt * pt.RubbleAt.x + _fc * pt.RubbleAt.z + Vector3.up * pt.RubbleAt.y;
                    float f2 = fall * fall;
                    pt.T.position = Vector3.Lerp(pt.T.position, lie, f2) + Vector3.up * Mathf.Sin(fall * Mathf.PI) * 0.3f;
                    pt.T.rotation = Quaternion.Slerp(pt.T.rotation, pt.RubbleRot, fall);
                    pt.T.localScale = Vector3.Lerp(pt.T.localScale, pt.Scale * 0.8f, fall);
                    if (pt.T.GetComponent<MeshRenderer>().sharedMaterial != _rubble && fall > 0.5f) pt.T.GetComponent<MeshRenderer>().sharedMaterial = _rubble;
                }
                pt.T.gameObject.SetActive(on || (Worked(p) && After != null));
            }
            // the rubble field (the executor's giant-rubble residue): lies there, dust wisping, trembles in its warning, sinks at its end while plates lift
            if (After != null)
            {
                float aw = AfterWarn(t), aex = AfterExit(t, AfterExitSeconds), natural = AfterWhy == Vol2MaterialEnd.Expired || AfterWhy == Vol2MaterialEnd.None ? 1f : 0f;
                float rs = AfterEndAt >= 0f ? Vol2Fs.Smooth(0f, 0.85f, aex) : 0f;
                foreach (var e in _extra) e.gameObject.SetActive(fall > 0.6f && rs < 0.99f);
                Vector3 tr = aw > 0f ? Random.insideUnitSphere * 0.03f * aw : Vector3.zero;
                if (fall >= 1f)
                    foreach (var pt in _parts) { pt.T.position += tr + Vector3.down * 0.9f * rs; pt.T.gameObject.SetActive(rs < 0.99f); }
                foreach (var e in _extra) if (rs > 0f) e.position += Vector3.down * 0.02f * rs;
                if (AfterEndAt < 0f && fall >= 1f && Time.frameCount % 7 == 0) Vol2FsB1.Emit(_cloud, _base + Random.insideUnitSphere * 1.2f, 1);
                if (AfterEndAt >= 0f && natural > 0f && !_platesBuilt) { BuildPlates(1.8f, 2.9f); _platesAt = AfterEndAt; }
            }
            // the plates (the end clause): lift and settle
            if (_platesBuilt)
            {
                float e = Mathf.Clamp01((t - _platesAt) / 1.4f), lift = Mathf.Sin(e * Mathf.PI);
                foreach (var pl in _plates)
                {
                    Vector3 o = pl.Rot * Vector3.forward;
                    pl.T.SetPositionAndRotation(pl.Base + Vector3.up * 0.2f * lift, Quaternion.AngleAxis(-28f * lift, Vector3.Cross(Vector3.up, o)) * pl.Rot);
                    pl.T.gameObject.SetActive(e < 0.98f);
                }
                if (e < 0.7f && Time.frameCount % 4 == 0) Vol2FsB1.Emit(_cloud, _base + Random.insideUnitSphere * 2.2f, 1);
            }
            // the eyes and seams: breathing on the heartbeat while it waits, blazing as it rises, out at the break
            Vector3 head = W(Vector3.Lerp(new Vector3(0f, 1.05f, 0.45f), new Vector3(0f, 3.05f, 0.25f), stand)) + _fc * 0.32f + shake;
            float eye = waiting ? heave * (0.4f + 0.6f * beat + 0.5f * warn) * (1f - 0.5f * Held01) : Worked(p) ? 1f - fall : 0f;
            if (eye > 0.02f && Time.frameCount % 2 == 0) { Vol2FsB1.EmitV(_eyes, head + _rt * 0.14f, Vector3.zero, 0.12f + 0.12f * eye, 0.1f); Vol2FsB1.EmitV(_eyes, head - _rt * 0.14f, Vector3.zero, 0.12f + 0.12f * eye, 0.1f); }
            _eyeLight.transform.position = head + _fc * 0.3f; _eyeLight.intensity = 2.5f * eye;
            _seamLight.transform.position = W(new Vector3(0f, Mathf.Lerp(0.8f, 2f, stand), 0.9f)); _seamLight.intensity = (waiting ? heave * (0.5f + 1.2f * beat) : Worked(p) ? 3f * (1f - fall) : 0f) + (_impacted && s < SlamAt + 0.5f ? 5f * (1f - (s - SlamAt) / 0.5f) : 0f);
            if (waiting && Time.frameCount % 5 == 0) Vol2FsB1.Emit(_grit, W(new Vector3(Random.Range(-0.9f, 0.9f), 1.0f, 0.3f)), 1);
            if (waiting && heave < 0.95f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_cloud, _base, 1);
            // the impact: low heavy ink, the crack ring
            if (_impacted)
            {
                float a = s - SlamAt;
                if (_burst != null) Vol2FsB1.DrawBurst(_inkS, _burst, _target + Vector3.up * 0.4f, a, cam, 0.2f, 0.08f);
                float cl = Vol2Fs.Smooth(0f, 0.15f, a) * (After != null && AfterEndAt >= 0f ? 1f - AfterExit(t, AfterExitSeconds) : 1f);
                if (cl > 0.01f) Vol2FsB2.GroundRing(_crackS, _target, F, Ground, 0.3f, 2.6f, 48, x => cl, 0.41f, cam, 0.02f, 0f, 4f);
            }
            _crackS.Write(_crackMesh); _inkS.Write(_inkMesh);
        }
    }
}
