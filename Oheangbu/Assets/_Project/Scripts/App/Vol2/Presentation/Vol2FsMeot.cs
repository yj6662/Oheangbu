using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>멋 (earth, ㅓ attachment, 가른다) - free showcase batch 6, concept meot_a (rock crusts the brush into a massive jagged stone cleaver with a glowing
    /// amber edge; the swing smashes through the sill in a burst of grit - two halves toppling apart - and strikes the bear, its armour plates blasting off
    /// as it is shoved back; a dark ink-and-dust crescent). Executor facts only: the attachment's 6 s = the stone cleaver on the brush (a broad chipped slab
    /// past the bristles, crusted rocks round the bristles, an amber cutting edge, grit trickling off it), every swing contact = an amber crescent with a dark
    /// dust-ink wake (the base) + grit and rocks flung along it, the struck body's cut throws rock chips, the strip book opens the seam, the body action shoves
    /// it (the game's push: dust, rocks, skid). The attachment's end = the cleaver crumbles off the brush into falling rocks and dust (CSV 끝: 가라앉으며).</summary>
    public sealed class Vol2FsMeot : Vol2FsCutWear
    {
        static readonly Color Amber = new Color(0.95f, 0.66f, 0.3f);
        Transform _slab; readonly List<Transform> _crust = new List<Transform>();
        Material _stone;
        Mesh _edgeMesh; readonly Vol2FsStrips _edge = new Vol2FsStrips();
        ParticleSystem _grit, _rocks, _dust;
        float _endAt = -1f; bool _crumbled;
        protected override Color SlashTint => new Color(1f, 0.7f, 0.32f);
        protected override Color CutGlow => new Color(1.5f, 0.95f, 0.45f);
        protected override float SlashWidth => 0.5f;
        protected override float SlashSeconds => 0.65f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            _stone = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.46f, 0.43f, 0.41f), 0f, 0.15f, Amber * 0.6f, "fs_rock_crack");
            _slab = Vol2FsB1.Prop(root, "Cleaver", Kit.SharedMesh("meotCleaver", () => Vol2FsB1.Slab(41, new Vector3(0.44f, 1f, 0.13f), 0.55f)), _stone, false).transform;
            for (int k = 0; k < 4; k++) _crust.Add(Vol2FsB1.Prop(root, "Crust" + k, Kit.SharedMesh("meotCrust" + k, () => Vol2FsB1.Rock(60 + k, 1f)), _stone, false).transform);
            var edge = M("fs_streak", Amber, 3f, 1f); edge.NoiseAmt = 0.25f; edge.Soft = 0.03f; edge.Erode = 0.12f;
            Vol2Fs.MeshObject(root, "CleaverEdge", Kit.Mat(edge), out _edgeMesh);
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("meotGrit", () => Vol2FsB1.Rock(7, 0.5f)), Life0 = 0.5f, Life1 = 1f, Speed0 = 1f, Speed1 = 5f, Size0 = 0.03f, Size1 = 0.09f, Gravity = 2.4f, Drag = 0.3f, Spin = 600f, Max = 500 }, root, "Grit");
            _rocks = Vol2Fs.Build(new Vol2FsPs { Mat = _stone, Mesh = Kit.SharedMesh("meotRock", () => Vol2FsB1.Rock(13, 0.5f)), Life0 = 0.8f, Life1 = 1.3f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.1f, Size1 = 0.24f, Gravity = 2.6f, Drag = 0.2f, Spin = 500f, Max = 120 }, root, "Rocks");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.36f, 0.26f, 1f), 0.5f)), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.25f, Size1 = 0.6f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 300 }, root, "Dust");
            foreach (var ps in new[] { _grit, _rocks, _dust }) ps.Play(false);
            SetVisible(false);
        }

        void SetVisible(bool on) { _slab.gameObject.SetActive(on); foreach (var c in _crust) c.gameObject.SetActive(on); }

        protected override void OnLineEnd() { _endAt = Now(NowUs); }

        protected override void Blade(float t, float life, float warn)
        {
            _edge.Clear();
            if (_endAt >= 0f && !_crumbled && t >= _endAt)
            {
                _crumbled = true; SetVisible(false);
                for (int k = 0; k < 18; k++) Vol2FsB1.EmitV(_rocks, Shaft(Random.Range(0.6f, 1.9f)), Random.insideUnitSphere * 1.2f + Vector3.down * 0.5f, -1f, -1f);
                for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_dust, Shaft(Random.Range(0.6f, 1.9f)), Random.insideUnitSphere * 0.6f, -1f, -1f);
            }
            if (life <= 0.01f || _crumbled) { SetVisible(false); _edge.Write(_edgeMesh); return; }
            SetVisible(true);
            Vector3 ax = ShaftAxis, side = ShaftSide, base0 = Shaft(0.7f);
            float len = (Tip() - base0).magnitude + 1.55f, grow = Vol2Fs.Smooth(0f, 1f, life);
            var shake = warn > 0f ? Random.insideUnitSphere * 0.02f * warn : Vector3.zero;
            _slab.SetPositionAndRotation(base0 + shake, AlongShaft(4f));
            _slab.localScale = new Vector3(Mathf.Lerp(0.5f, 1.5f, grow), len * grow, 1.7f);      // r2: ~0.66 m broad, 0.22 m thick, 1.55 m past the bristles (r1: small)
            for (int k = 0; k < _crust.Count; k++)
            {
                float u = 0.82f + 0.08f * k; float a = k * 1.9f;
                Vector3 off = (side * Mathf.Cos(a) + Vector3.Cross(ax, side) * Mathf.Sin(a)) * 0.07f;
                _crust[k].SetPositionAndRotation(Shaft(u) + off + shake, Quaternion.Euler(k * 47f, k * 83f, 0f));
                _crust[k].localScale = Vector3.one * (0.19f + 0.04f * k) * grow;
            }
            // the amber cutting edge down the leading side of the cleaver
            Vector3 e0 = base0 + side * 0.32f * grow, e1 = base0 + ax * len * grow * 0.98f + side * 0.02f;
            float pulse = 0.7f + 0.3f * Mathf.Sin(t * 7f);
            _edge.Strip(new[] { e0, Vector3.Lerp(e0, e1, 0.5f) + side * 0.12f, e1 }, new[] { 0.08f, 0.11f, 0.03f }, null, Color.white, new[] { life * pulse, life * pulse, life * 0.6f }, 0.31f, CamPos);
            if (Time.frameCount % 4 == 0) Vol2FsB1.EmitV(_grit, base0 + ax * len * grow * Random.value, Vector3.down * 0.3f + Random.insideUnitSphere * 0.2f, -1f, -1f);
            _edge.Write(_edgeMesh);
        }

        protected override void OnSlash(Slash s, Struck by)
        {
            if (s.Arc)
            {
                for (int k = 0; k < 20; k++)
                {
                    float u = k / 19f; Vector3 p = ArcPoint(s, u), d = (ArcPoint(s, Mathf.Min(1f, u + 0.05f)) - p).normalized;
                    Vol2FsB1.EmitV(_grit, p, d * Random.Range(3f, 6f) + Vector3.up * 1.2f, -1f, -1f);
                    if (k % 3 == 0) Vol2FsB1.EmitV(_dust, p, d * 1.2f, -1f, -1f);
                }
                return;
            }
            Vector3 c = (s.A + s.B) * 0.5f;
            for (int k = 0; k < 14; k++) Vol2FsB1.EmitV(_rocks, c, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(3f, 7f), -1f, -1f);
            for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_dust, c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * 1.4f, -1f, -1f);
            HitStop(Now(NowUs), 0.09f);
        }

        protected override void Live(float t, float h) { }
    }
}
