using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>못 (earth, ㅗ fan, 가른다) - free showcase batch 6, concept mot_b (a fan of thin slate blades is flung low across the area like a thrown hand of cards,
    /// each one gouging the ground, the wall sliced through, the bears' armour shattering off as they are shoved back). Executor facts only: the launch = the
    /// one cut: twelve slate cards fan out from the brush low over the ground, right edge to left (the sweep - a presentation of the one tick), each skimming
    /// and spinning with an amber-lit edge and an ink streak behind it, its gouge opening under it (the gouges follow the cards' own angles), then it bites
    /// edge-first into the ground at the fan's far edge and stands there tilted; the split wall parts in a burst of grit; the stripped bodies are shoved (the
    /// body action: dust, rocks, the skid of their feet); when the strips are over the cards sink and the ground plates settle (CSV 끝: 가라앉으며 둘레 땅이 판으로 들림).</summary>
    public sealed class Vol2FsMot : Vol2FsCutFan
    {
        static readonly Color Amber = new Color(0.95f, 0.66f, 0.3f);
        const int Cards = 12;
        const float Fly = 0.22f;
        ParticleSystem _grit, _dust;
        Material _slate; Mesh _cardMesh, _streakMesh; readonly Vol2FsStrips _streak = new Vol2FsStrips();
        sealed class Card { public Transform T; public float Ang, At, Spin, U1, Size, Rnd; public bool Bit; public Vector3 Rest; public Quaternion RestRot; }
        readonly List<Card> _cards = new List<Card>();
        protected override Color CutGlow => new Color(1.15f, 0.95f, 0.75f);      // r3: less saturated
        protected override float CrackWidth => 0.36f;
        protected override float CrackJag => 0.025f;
        protected override float SweepSeconds => 0.14f;

        protected override void LayGouges()
        {
            for (int k = 0; k < Cards; k++)
            {
                float v = k / (float)(Cards - 1) * 2f - 1f;
                float ang = v * Half * 1.02f + (Vol2Fs.Hash(Seed, k + 300) - 0.5f) * 0.06f;
                float u1 = 0.88f + 0.14f * Vol2Fs.Hash(Seed, k + 310);
                float at = SweepAt(ang);
                _cards.Add(new Card { Ang = ang, At = at, U1 = u1, Size = 0.95f + 0.5f * Vol2Fs.Hash(Seed, k + 320), Spin = (Vol2Fs.Hash(Seed, k + 330) - 0.5f) * 2400f, Rnd = Vol2Fs.Hash(Seed, k + 340) });
                // the gouge opens under the card as it skims (from a quarter out to where it bites) - r3: four cards gouge (r2: twelve lines read as zebra stripes)
                if (k % 3 == 1) Gouges.Add(new Gouge { A = InFan(0.27f, ang), B = InFan(u1, ang), Width = CrackWidth * (0.75f + 0.4f * Vol2Fs.Hash(Seed, k + 350)), OpenAt = at + Fly * 0.25f, Rnd = Vol2Fs.Hash(Seed, k + 360), Seed = Seed + k * 7 });
            }
        }

        protected override void OnSetup()
        {
            var root = Root.transform;
            _slate = Vol2FsB1.Lit(Kit, "fs_rock", new Color(0.42f, 0.4f, 0.4f), 0f, 0.2f, Amber * 0.9f, "fs_rock_crack");
            _cardMesh = Kit.SharedMesh("motCard", () => Vol2FsB6.Spike(0.5f, 0.05f, 0.45f, 0f));
            _grit = Vol2Fs.Build(new Vol2FsPs { Mat = _slate, Mesh = Kit.SharedMesh("matGrit", () => Vol2FsB1.Rock(7, 0.5f)), Life0 = 0.5f, Life1 = 1f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.04f, Size1 = 0.12f, Gravity = 2.4f, Drag = 0.3f, Spin = 600f, Max = 700 }, root, "Grit");
            _dust = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(Vol2FsB3.Dust(new Color(0.5f, 0.36f, 0.26f, 1f), 0.5f)), Life0 = 0.6f, Life1 = 1.3f, Speed0 = 0.3f, Speed1 = 1.6f, Size0 = 0.3f, Size1 = 0.7f, SizeStart = 0.6f, SizePeak = 1.2f, SizeEnd = 1.6f, Drag = 1.3f, Max = 300 }, root, "Dust");
            foreach (var ps in new[] { _grit, _dust }) ps.Play(false);
            Vol2Fs.MeshObject(root, "CardStreaks", Kit.InkSolid("fs_stroke2", InkC, Amber * 1.5f, 0.14f, 0.45f, 0.3f, 1.3f), out _streakMesh);
        }

        protected override void Sweep(float t, float k, float prevK)
        {
            Vector3 tip = Tip();
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_dust, tip, Dir(SweepAng(k)) * 2f, -1f, -1f);
        }

        Vector3 CardPos(Card c, float f) => InFan(Mathf.Lerp(0.12f, c.U1, f), c.Ang, Mathf.Lerp(0.55f, 0.28f, f) - 0.25f * f * f);

        protected override void OnCleave(Cleave c)
        {
            for (int k = 0; k < 30; k++) Vol2FsB1.EmitV(_grit, c.Centre + Vector3.up * Random.Range(0.1f, 0.8f), c.Side * (k % 2 == 0 ? 1f : -1f) * Random.Range(2f, 5f) + Vector3.up * Random.Range(1f, 3f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_dust, c.Centre + Vector3.up * 0.3f, c.Side * (k % 2 == 0 ? 1f : -1f) * 1.4f + Vector3.up * 0.5f, -1f, -1f);
        }

        protected override void Live(float t, float h)
        {
            var cam = CamPos; _streak.Clear();
            float close = 1f - GougeLife(t);
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                float a = t - c.At;
                if (c.T == null)
                {
                    if (a < 0f) continue;
                    var r = Vol2FsB1.Prop(Root.transform, "Card" + i, _cardMesh, _slate, false);
                    c.T = r.transform;
                }
                float f = Mathf.Clamp01(a / Fly);
                Vector3 d = Dir(c.Ang), side = Vector3.Cross(Vector3.up, d);
                if (f < 1f)
                {
                    // skimming flat, spinning about the vertical, its point leading
                    Vector3 p = CardPos(c, f);
                    c.T.SetPositionAndRotation(p, Quaternion.AngleAxis(c.Spin * a, Vector3.up) * Quaternion.LookRotation(Vector3.up, d));
                    c.T.localScale = new Vector3(c.Size * 0.9f, c.Size * 1.5f, c.Size);
                    if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(_grit, G(p), -d * Random.Range(0.5f, 2f) + Vector3.up * Random.Range(1f, 2.5f), -1f, -1f);
                    // the ink streak behind it, from where it left to where it is
                    Vol2FsB6.Slash(_streak, CardPos(c, Mathf.Max(0f, f - 0.45f)), p, Vector3.up * 0.05f + side * 0.1f, 0.28f, 1f, 0.9f, c.Rnd, cam);
                }
                else
                {
                    if (!c.Bit)
                    {
                        c.Bit = true;
                        // the point bitten into the ground (a third of it buried), leaning back toward the caster, its face toward the camera
                        Vector3 ydir = (d * 0.55f + Vector3.down * 0.83f + side * (c.Rnd - 0.5f) * 0.3f).normalized;
                        Vector3 bite = InFan(c.U1, c.Ang, 0f);
                        c.Rest = bite - ydir * c.Size * 1.5f * 0.7f;
                        c.RestRot = Quaternion.LookRotation(Vector3.Cross(side, ydir).normalized, ydir);
                        for (int k = 0; k < 10; k++) Vol2FsB1.EmitV(_grit, bite, (Random.onUnitSphere + Vector3.up).normalized * Random.Range(1.5f, 4f), -1f, -1f);
                        Vol2FsB1.EmitV(_dust, bite + Vector3.up * 0.2f, Vector3.up * 0.6f, -1f, -1f);
                    }
                    // stands bitten into the ground; sinks when the gouges close
                    float sink = close;
                    c.T.SetPositionAndRotation(c.Rest + Vector3.down * sink * c.Size * 1.2f, c.RestRot);
                    c.T.localScale = new Vector3(c.Size * 0.9f, c.Size * 1.5f, c.Size);
                    c.T.gameObject.SetActive(sink < 0.98f);
                    float st = 1f - Vol2Fs.Smooth(0.15f, 0.95f, a - Fly);      // r2: the streaks linger (Phantom Blade principle 3)
                    if (st > 0.01f) Vol2FsB6.Slash(_streak, CardPos(c, 0.55f), CardPos(c, 1f), Vector3.up * 0.05f + side * 0.1f, 0.28f, 1f, 0.9f * st, c.Rnd, cam);
                }
            }
            // r2: the throw itself as an amber-edged ink crescent across the fan, lingering with two phantoms
            SweepArc(_streak, _streak, t, Depth * 0.5f, 0.4f, 0.32f, 0.8f, 0.7f, 0.1f, 0.15f, 0.9f);
            _streak.Write(_streakMesh);
        }
    }
}
