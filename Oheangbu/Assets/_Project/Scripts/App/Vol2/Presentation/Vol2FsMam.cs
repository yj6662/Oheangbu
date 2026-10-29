using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>맘 (earth, ㅏ path, 심는다 - the PEAK: pressed into the ground) - free showcase batch 5, concept mam_c (a small rough stone hangs over every bear the
    /// path passed, slowly turning, dust trickling UP from it; over the first bear it grows in an instant into a huge flat slab that slams down and presses the bear
    /// into the ground, the ground round it cracking into a ring of tilted plates, dust blasting out). Executor facts only: "Plant" = the stone appears over the
    /// body -> the wait (it turns, its ember seams breathe on the heartbeat, the ink dial burns down; the last 0.4 s it shudders) -> Consumed = the slab slams down
    /// and holds the bear pressed for the executor's own Press time (its "Press" fact: 1.5 s, a boss half - the move lock is the status sink's, mirrored) ->
    /// then it sinks back into the ground while the earth round it lifts as plates and settles (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림(떨어지는 것 없음));
    /// Expired = the little stone drops to the ground and crumbles.</summary>
    public sealed class Vol2FsMam : Vol2FsSeedPath
    {
        static readonly Color DustC = new Color(0.55f, 0.42f, 0.34f, 1f), Seam = new Color(1f, 0.5f, 0.18f);
        ParticleSystem _chunks, _grit, _cloud, _ring, _upDust;
        Material _stone, _slabMat, _plateMat;
        Mesh _crackMesh, _inkMesh; readonly Vol2FsStrips _crackS = new Vol2FsStrips(), _inkS = new Vol2FsStrips();
        protected override float MarkLift => 0.75f;
        protected override float DialR => 0.4f;
        protected override float FireSeconds => PressSeconds + 1.6f;
        protected override float ExpireSeconds => 1.0f;
        float PressSeconds
        {
            get
            {
                if (Hub != null) foreach (var r in Hub.RecordsOf(Inst.Cast)) if (r.Kind == "Press" && r.Value > 0m) return Mathf.Clamp((float)r.Value / 1e6f, 0.3f, 3f);
                return 1.5f;
            }
        }

        sealed class Look
        {
            public Transform Stone, Slab; public Light Seam; public Vector3 Fall, Top0, Feet; public float Spin;
            public readonly List<(Transform T, Vector3 Base, Quaternion Rot)> Plates = new List<(Transform, Vector3, Quaternion)>();
            public List<Vol2FsB1.Stroke> Ink;
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.52f, 0.44f), 0f, 0.1f, Seam * 0.9f, "fs_vein");
            _slabMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.78f, 0.66f, 0.55f), 0f, 0.1f, Seam * 0.5f, "fs_vein");
            _plateMat = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.62f, 0.5f, 0.42f), 0f, 0.1f);
            var chunk = Kit.SharedMesh("chunk0", () => Vol2FsB1.Rock(23, 0.5f, 0.7f, 7, 5));
            _chunks = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.9f, Life1 = 1.6f, Speed0 = 2.5f, Speed1 = 7f, Size0 = 0.1f, Size1 = 0.32f, Radius = 0.4f, Gravity = 2.2f, Drag = 0.3f, Spin = 500f, Max = 300 }, root, "Chunks");
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _plateMat, Mesh = chunk, Life0 = 0.5f, Life1 = 1f, Speed0 = 0.1f, Speed1 = 0.5f, Size0 = 0.02f, Size1 = 0.05f, Radius = 0.08f, Gravity = 1.6f, Spin = 400f, Max = 300 }, root, "Grit");
            _cloud = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.6f)), Life0 = 1.2f, Life1 = 2.2f, Speed0 = 1f, Speed1 = 4f, Size0 = 1f, Size1 = 2.2f, Shape = ParticleSystemShapeType.Hemisphere, Radius = 0.6f, Drag = 2f, Tiles = 2, SizeStart = 0.4f, SizePeak = 1.2f, SizeEnd = 1.7f, Max = 160, SortFudge = 10 }, root, "Cloud");
            _ring = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.5f)), Life0 = 0.7f, Life1 = 1.2f, Speed0 = 5f, Speed1 = 9f, Size0 = 0.6f, Size1 = 1.2f, Shape = ParticleSystemShapeType.Circle, Radius = 0.6f, ShapeRot = new Vector3(-90f, 0f, 0f), Drag = 3f, Tiles = 2, SizeEnd = 2f, Max = 200, SortFudge = 8 }, root, "DustRing");
            _upDust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(DustC, 0.35f)), Life0 = 0.8f, Life1 = 1.4f, Speed0 = 0.2f, Speed1 = 0.5f, Size0 = 0.08f, Size1 = 0.2f, Radius = 0.12f, Tiles = 2, VelOverLife = new Vector3(0f, 0.5f, 0f), SizeEnd = 2f, Max = 200 }, root, "UpDust");
            foreach (var ps in new[] { _chunks, _grit, _cloud, _ring, _upDust }) ps.Play(false);
            var cr = M("fs_crack", new Color(0.08f, 0.05f, 0.04f, 1f), 1f, 0f); cr.Soft = 0.001f; cr.NoiseAmt = 0.4f; cr.Erode = 0.3f; cr.Opacity = 0.9f;
            Vol2Fs.MeshObject(root, "Cracks", Kit.Mat(cr), out _crackMesh);
            Vol2Fs.MeshObject(root, "InkV", Kit.InkSolid("fs_stroke2", InkC, new Color(0.9f, 0.55f, 0.25f, 1f), 0.08f, 0.45f, 0.3f, 1.3f), out _inkMesh);
        }

        Look L(Pod p) => p.Data as Look;

        protected override void Planted(Pod p)
        {
            var l = new Look(); p.Data = l;
            var r = Vol2FsB1.Prop(Root.transform, "Stone" + p.Index, Kit.SharedMesh("mamStone", () => Vol2FsB1.Rock(77, 0.5f, 0.8f, 12, 8)), _stone);
            l.Stone = r.transform; l.Stone.localScale = Vector3.one * 0.01f; l.Spin = Vol2Fs.Hash(Seed, p.Index) * 360f;
            l.Seam = Vol2Fs.PointLight(Root.transform, "Seam" + p.Index, Seam, 2.2f);
            Vol2FsB1.Emit(_grit, MarkPoint(p), 12);
        }

        protected override void OnFire(Pod p)
        {
            var l = L(p); if (l == null) return;
            var root = Root.transform;
            l.Top0 = HostTop(p); l.Feet = G(HostFeet(p));
            var slab = Vol2FsB1.Prop(root, "Slab" + p.Index, Kit.SharedMesh("mamBoulder", () => Vol2FsB1.Rock(912, 0.5f, 0.5f, 16, 10)), _slabMat);
            l.Slab = slab.transform;
            l.Stone.gameObject.SetActive(false); l.Seam.intensity = 4f;
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - l.Feet));
            l.Ink = Vol2FsB1.Burst(Seed + p.Index * 3, 12, side, -FlatN(CamPos - l.Feet), 1.6f, 3.4f, 0.28f, 0.55f, 0.15f, 0.2f);
            int seed = Seed + p.Index * 19;
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * Mathf.PI * 2f + Vol2Fs.Hash(seed, k);
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var pr = Vol2FsB1.Prop(root, "Plate" + p.Index + "_" + k, Kit.SharedMesh("plate" + (k % 3), () => Vol2FsB1.Slab(400 + k % 3, new Vector3(1f, 0.22f, 0.8f), 0.9f)), _plateMat);
                pr.transform.localScale = new Vector3(Mathf.Lerp(0.8f, 1.3f, Vol2Fs.Hash(seed, k + 30)), 1.4f, 1f);
                l.Plates.Add((pr.transform, l.Feet + o * (1.25f + 0.25f * Vol2Fs.Hash(seed, k + 60)) + Vector3.down * 0.18f, Quaternion.LookRotation(o, Vector3.up)));
            }
        }

        protected override void OnPodEnd(Pod p)
        {
            var l = L(p); if (l == null || Worked(p)) return;
            l.Fall = l.Stone.position;
            if (!Withered(p)) { l.Stone.gameObject.SetActive(false); Vol2FsB1.Emit(_grit, l.Fall, 10); }
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _crackS.Clear(); _inkS.Clear();
            foreach (var p in Pods)
            {
                var l = L(p); if (l == null) continue;
                if (!Worked(p))
                {
                    float life = PodLife(p, t, 0.3f), beat = Beat(p), warn = PodWarn(p, t), ex = PodExit(p, t);
                    Vector3 at = MarkPoint(p) + Vector3.up * 0.05f * Mathf.Sin(t * 1.7f + p.Index);
                    if (Withered(p))
                    {
                        float fall = Vol2Fs.Smooth(0f, 0.35f, ex); at = Vector3.Lerp(l.Fall, G(l.Fall) + Vector3.up * 0.15f, fall * fall);
                        if (ex > 0.33f && ex < 0.4f) { Vol2FsB1.Emit(_grit, at, 30); Vol2FsB1.Emit(_cloud, at, 2); }
                        l.Stone.gameObject.SetActive(ex < 0.4f);
                    }
                    else l.Stone.gameObject.SetActive(p.Started && !p.Ended);
                    l.Spin += Time.deltaTime * (35f + 90f * warn) * (1f - Held01);
                    l.Stone.position = at + (warn > 0f ? Random.insideUnitSphere * 0.04f * warn : Vector3.zero);
                    l.Stone.rotation = Quaternion.Euler(12f, l.Spin, 8f);
                    l.Stone.localScale = Vector3.one * Mathf.Max(0.01f, Mathf.Max(life, Withered(p) ? 1f : 0f) * (0.85f + 0.06f * beat));
                    l.Seam.transform.position = at; l.Seam.intensity = p.Ended ? 0f : life * (0.5f + 1.5f * beat + warn) * (1f - 0.6f * Held01);
                    if (!p.Ended && Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_upDust, at + Random.insideUnitSphere * 0.15f, Vector3.up * 0.3f, -1f, -1f);
                    continue;
                }
                float s = SinceFire(p, t), press = PressSeconds;
                // the slab: grows in 0.06 s over the body, slams down 0.1 s, holds the press, then sinks
                float grow = Vol2Fs.Smooth(0f, 0.06f, s), slam = Vol2Fs.Smooth(0.05f, 0.15f, s), sink = Vol2Fs.Smooth(press, press + 0.9f, s);
                Vector3 hi = l.Top0 + Vector3.up * 2.2f, lo = new Vector3(l.Top0.x, Mathf.Lerp(Ground, l.Top0.y, 0.55f) + 0.35f, l.Top0.z);
                Vector3 pos = Vector3.Lerp(hi, lo, slam * slam) + Vector3.down * 0.9f * sink;
                if (s > 0.15f && s < press) pos += Random.insideUnitSphere * 0.015f;
                l.Slab.SetPositionAndRotation(pos, Quaternion.LookRotation(FlatN(cam - l.Feet), Vector3.up) * Quaternion.Euler(0f, 12f, 4f));
                l.Slab.localScale = new Vector3(3.2f, 1.9f, 2.6f) * Mathf.Max(0.02f, grow) * (1f - 0.3f * sink);
                l.Slab.gameObject.SetActive(sink < 0.99f);
                bool impact = s >= 0.15f && s - Time.deltaTime < 0.15f;
                if (impact)
                {
                    HitStop(t, 0.09f);
                    Vol2FsB1.Emit(_cloud, l.Feet + FlatN(CamPos - l.Feet) * 1.2f, 22); Vol2FsB1.Emit(_ring, l.Feet + Vector3.up * 0.1f, 90); Vol2FsB1.Emit(_chunks, l.Feet + Vector3.up * 0.3f, 110);
                }
                if (s > 0.15f && l.Ink != null) Vol2FsB1.DrawBurst(_inkS, l.Ink, l.Feet + Vector3.up * 0.35f, s - 0.15f, cam, 0.15f, 0.08f);
                // the ring of plates: kicked up at the slam, settled, lifted again as it sinks (the end clause), settled
                float kick = Mathf.Sin(Mathf.Clamp01((s - 0.15f) / 0.5f) * Mathf.PI) * 0.9f, lift = Mathf.Sin(Mathf.Clamp01((s - press) / 1.2f) * Mathf.PI);
                float k = Mathf.Max(kick, lift), on = s >= 0.15f && s < press + 1.4f ? 1f : 0f;
                foreach (var pl in l.Plates)
                {
                    Vector3 o = pl.Rot * Vector3.forward;
                    pl.T.SetPositionAndRotation(pl.Base + Vector3.up * 0.2f * k, Quaternion.AngleAxis(-30f * k, Vector3.Cross(Vector3.up, o)) * pl.Rot);
                    pl.T.gameObject.SetActive(on > 0f);
                }
                if (s > press && s < press + 1.1f && Time.frameCount % 3 == 0) Vol2FsB1.Emit(_cloud, l.Feet + Random.insideUnitSphere * 1.2f, 1);
                float cl = Vol2Fs.Smooth(0.15f, 0.3f, s) * (1f - Vol2Fs.Smooth(press + 1f, press + 1.5f, s));
                if (cl > 0.01f) Vol2FsB2.GroundRing(_crackS, l.Feet, F, Ground, 0.2f, 1.9f, 48, a => cl, 0.37f + p.Index * 0.1f, cam, 0.02f, 0f, 4f);
                l.Seam.transform.position = pos + Vector3.up * 0.4f; l.Seam.intensity = 4f * (1f - Vol2Fs.Smooth(0.15f, 0.8f, s)) + 0.6f * (1f - sink) * (s < press ? 1f : 0f);
            }
            _crackS.Write(_crackMesh); _inkS.Write(_inkMesh);
        }
    }
}
