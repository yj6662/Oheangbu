using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>섯 (metal, ㅓ attachment, 가른다 - the PEAK) - free showcase batch 6, concept seot_a (mirror-steel blades jut from BOTH ends of the great brush; one
    /// swing slashes two crescents at once, one ahead - the sill and the block ahead-right cut in two, the bear's armour flying off - and one behind - the block
    /// behind cut in two - sparks everywhere). Executor facts only: the attachment's 6 s = two mirror blades on the brush (a long one past the bristles, a shorter
    /// one out of the butt behind the grip, white-gold edge glints running along both), every swing contact = the front white-gold crescent (the base) and each
    /// Rear contact (the back cut x0.5, TE-S6-4) = the mirrored crescent BEHIND the caster at the same instant, with a spray of sparks and steel shards off both
    /// ends (Phantom Blade principle 4); the walls the swing capsules split part on both sides (the shared book), the strip book opens the seam, the body
    /// action is the extra cut. The attachment's end = both blades snap with a ring, shards flung sideways (CSV 끝: 쨍하게 끊기며 파편이 옆으로 튐).</summary>
    public sealed class Vol2FsSeot : Vol2FsCutWear
    {
        static readonly Color Gold = new Color(1f, 0.88f, 0.6f);
        Transform _front, _back; Material _steel;
        Mesh _glintMesh; readonly Vol2FsStrips _glint = new Vol2FsStrips();
        ParticleSystem _shards, _flash;
        Light _light;
        float _endAt = -1f; bool _snapped;
        protected override Color SlashTint => new Color(1f, 0.9f, 0.68f);
        protected override Color CutGlow => new Color(1.7f, 1.45f, 0.95f);
        protected override float SlashWidth => 0.34f;
        protected override Color BodyCutTint => new Color(0.82f, 0.85f, 0.92f);      // r3: platinum
        protected override bool InkUnder => true;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _steel = Kit.Metal(1.3f, 0f, new Color(0.04f, 0.035f, 0.02f)) ?? Vol2FsB1.Lit(Kit, "fs_steel", Color.white, 1f, 0.9f, new Color(1.2f, 1.2f, 1.25f));      // r2: brighter mirror
            var mesh = Kit.SharedMesh("seotBlade", () => Vol2FsB6.Spike(0.16f, 0.035f, 0.18f, 0f));
            _front = Vol2FsB1.Prop(root, "FrontBlade", mesh, _steel, false).transform;
            _back = Vol2FsB1.Prop(root, "BackBlade", mesh, _steel, false).transform;
            var g = M("fs_streak", Gold, 4f, 1f); g.NoiseAmt = 0.1f; g.Soft = 0.02f; g.Erode = 0.08f;
            Vol2Fs.MeshObject(root, "EdgeGlints", Kit.Mat(g), out _glintMesh);
            _shards = Vol2Fs.Build(new Vol2FsPs { Mat = _steel, Mesh = Kit.SharedMesh("shard0", () => Vol2FsB1.Shard(5)), Life0 = 0.6f, Life1 = 1.1f, Speed0 = 4f, Speed1 = 9f, Size0 = 0.05f, Size1 = 0.14f, Gravity = 2f, Drag = 0.4f, Spin = 900f, Max = 200 }, root, "Shards");
            _flash = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(Gold, Color.white, 0.3f), 2.4f, 1f)), Life0 = 0.06f, Life1 = 0.1f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.18f, Size1 = 0.28f, Max = 60 }, root, "Flash");      // r2: no white discs (<= 0.3, gold, 2.4)
            foreach (var ps in new[] { _shards, _flash }) ps.Play(false);
            _light = Vol2Fs.PointLight(root, "SteelLight", Gold, 3.5f);
            _front.gameObject.SetActive(false); _back.gameObject.SetActive(false);
        }

        protected override void OnLineEnd() { _endAt = Now(NowUs); }

        void Snap(Vector3 at, int n)
        {
            Vector3 side = Vector3.Cross(Vector3.up, FlatN(CamPos - at));
            for (int k = 0; k < n; k++) Vol2FsB1.EmitV(_shards, at + Random.insideUnitSphere * 0.15f, side * (k % 2 == 0 ? 1f : -1f) * Random.Range(3f, 8f) + Vector3.up * Random.Range(0.5f, 3f) + Random.insideUnitSphere, -1f, -1f);
            for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(PSparks, at, (side * (k % 2 == 0 ? 1f : -1f) + Random.insideUnitSphere * 0.6f).normalized * Random.Range(4f, 11f), -1f, -1f);
            Vol2FsB1.EmitV(_flash, at, Vector3.zero, 0.28f, 0.09f);
        }

        protected override void Blade(float t, float life, float warn)
        {
            _glint.Clear();
            if (_endAt >= 0f && !_snapped && t >= _endAt)
            {
                _snapped = true;
                Snap(Shaft(1.5f), 14); Snap(Shaft(-0.35f), 10);
                _front.gameObject.SetActive(false); _back.gameObject.SetActive(false);
            }
            if (life <= 0.01f || _snapped) { _front.gameObject.SetActive(false); _back.gameObject.SetActive(false); _glint.Write(_glintMesh); _light.intensity = 0f; return; }
            _front.gameObject.SetActive(true); _back.gameObject.SetActive(true);
            Vector3 ax = ShaftAxis; float grow = Vol2Fs.Smooth(0f, 1f, life);
            Vector3 tip = Tip(), butt = Grip() - ax * 0.05f;
            var shake = warn > 0f ? Random.insideUnitSphere * 0.015f * warn : Vector3.zero;
            float lf = 1.45f * grow, lb = 1.25f * grow;      // r2: both >= 1.2 m (r1: the back one hardly read)
            _front.SetPositionAndRotation(tip - ax * 0.08f + shake, AlongShaft());
            _front.localScale = new Vector3(1.45f, Mathf.Max(0.01f, lf), 1.3f);
            var view = Vector3.ProjectOnPlane(CamPos - butt, -ax); if (view.sqrMagnitude < 1e-4f) view = Vector3.forward;
            _back.SetPositionAndRotation(butt + shake, Quaternion.LookRotation(view.normalized, -ax));
            _back.localScale = new Vector3(1.45f, Mathf.Max(0.01f, lb), 1.3f);
            // a glint running along each edge (one cycle per ~0.7 s), the light breathing on them
            float ph = Mathf.Repeat(t * 1.4f, 1f), side = 0.05f;
            Vector3 ss = ShaftSide;
            Vector3 g0 = tip + ax * lf * Mathf.Max(0f, ph - 0.15f) + ss * side, g1 = tip + ax * lf * ph + ss * side * 0.6f;
            _glint.Strip(new[] { g0, g1 }, new[] { 0.015f, 0.08f }, null, Color.white, new[] { life * 0.3f, life }, 0.3f, CamPos);
            Vector3 b0 = butt - ax * lb * Mathf.Max(0f, ph - 0.15f) - ss * side, b1 = butt - ax * lb * ph - ss * side * 0.6f;
            _glint.Strip(new[] { b0, b1 }, new[] { 0.015f, 0.08f }, null, Color.white, new[] { life * 0.3f, life }, 0.6f, CamPos);
            _glint.Write(_glintMesh);
            _light.transform.position = tip + ax * 0.5f; _light.intensity = Mathf.Max(_light.intensity * 0.9f, life * (1.2f + warn));
        }

        protected override void OnSlash(Slash s, Struck by)
        {
            Vector3 at = s.Arc ? ArcPoint(s, 0.85f) : (s.A + s.B) * 0.5f;
            if (s.Arc)
            {
                for (int k = 0; k < 16; k++) { float u = k / 15f; Vector3 p = ArcPoint(s, u); Vol2FsB1.EmitV(PSparks, p, ((ArcPoint(s, Mathf.Min(1f, u + 0.05f)) - p).normalized + Random.insideUnitSphere * 0.4f).normalized * Random.Range(5f, 11f), -1f, -1f); }
                Vol2FsB1.EmitV(_flash, at, Vector3.zero, s.Rear ? 0.24f : 0.3f, 0.09f);
                _light.transform.position = at; _light.intensity = 4f;
                return;
            }
            Snap(at, 10);
        }

        protected override void Live(float t, float h) { }
    }
}
