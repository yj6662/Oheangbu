using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>앙 (water, ㅏ path, 돌아온다 - the PEAK) - free showcase batch 7, concept ang_b (a water serpent with ink swirling inside races out along the path, coils
    /// round the caster's own standing stone on its way back and carries it home in its coils toward the caster; a looping wake of spray traces its trip).
    /// Executor facts only: the head = a water serpent's head (a clear blue-green bulb with a foam crest and an ink eye) at the executor's head; its body = a clear
    /// water ribbon with a foam rim and two ink threads along the trail the head flew (out lane, the U at the end, the back lane), spray fanning off its belly;
    /// the carry = the report's CarriedId / From / To: the stand-in stone goes the frame the registry moved it, the look lifts the same stone where the returning
    /// head met it, rides it home inside two coils of water, and sets it down 2 m ahead of the caster with a splash ring and a hit-stop (seen where the head passes
    /// the destination); it stands there wet (drips) until its own material ends (moved, never remade: same id, life, amount); the first touch soaks (the shared
    /// action, 4 s), the return touch (x0.75) = a heavier splash crown; a shot taken = a pop of spray. Home: the serpent pours down into the brush (CSV 끝: 아래로 흘러내림).</summary>
    public sealed class Vol2FsAng : Vol2FsBackPath
    {
        static readonly Color Teal = new Color(0.35f, 0.85f, 1f);
        Mesh _waterMesh, _foamMesh, _inkMesh, _coilMesh;
        readonly Vol2FsStrips _water = new Vol2FsStrips(), _foam = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _coil = new Vol2FsStrips();
        ParticleSystem _spray, _drops, _mist;
        readonly List<(Vector3 P, float T)> _flown = new List<(Vector3, float)>(256);
        Transform _stone; bool _landed; float _landAt = -1f; Vector3 _stoneFrom; Quaternion _stoneRot;
        protected override Color LoopGlow => new Color(0.55f, 1.3f, 1.6f);
        protected override float LoopTail => 1.2f;

        protected override void OnSetup()
        {
            var root = Root.transform;
            Vol2Fs.MeshObject(root, "Serpent", Vol2FsB1.Water(Kit, 1.7f, 0.4f), out _waterMesh);
            var foam = M("fs_streak", new Color(0.9f, 1f, 1f), 2.6f, 0.9f); foam.NoiseAmt = 0.3f; foam.Soft = 0.05f; foam.Scroll = new Vector2(-2f, 0f);
            Vol2Fs.MeshObject(root, "Foam", Kit.Mat(foam), out _foamMesh);
            Vol2Fs.MeshObject(root, "InkThreads", Kit.InkSolid("fs_stroke2", InkC, Teal * 1.2f, 0.08f, 0.45f, 0.32f, 1.3f), out _inkMesh);
            Vol2Fs.MeshObject(root, "Coils", Vol2FsB1.Water(Kit, 1.9f, 0.45f), out _coilMesh);
            _spray = Vol2Fs.Build(new Vol2FsPs { Mat = Vol2FsB1.Water(Kit, 1.5f, 0.45f, "fs_splash"), Life0 = 0.35f, Life1 = 0.8f, Speed0 = 2f, Speed1 = 6f, Size0 = 0.12f, Size1 = 0.32f, Gravity = 1.8f, Drag = 0.6f, Max = 1200 }, root, "Spray");
            _drops = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", new Color(0.7f, 0.95f, 1f), 2.6f, 0.75f)), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 1f, Speed1 = 4f, Size0 = 0.025f, Size1 = 0.06f, Gravity = 2.4f, Drag = 0.3f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.2f, Max = 1200 }, root, "Drops");
            var mist = M("fs_puff", new Color(0.75f, 0.9f, 1f, 1f), 1f, 0.3f); mist.Opacity = 0.35f; mist.NoiseAmt = 0.5f; mist.Erode = 0.3f;
            _mist = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(mist), Life0 = 0.6f, Life1 = 1.2f, Speed0 = 0.3f, Speed1 = 1.2f, Size0 = 0.4f, Size1 = 0.9f, SizeStart = 0.6f, SizePeak = 1.1f, SizeEnd = 1.5f, Drag = 1.2f, Max = 200 }, root, "Mist");
            foreach (var ps in new[] { _spray, _drops, _mist }) ps.Play(false);
        }

        protected override void Run(float t, float along, float prev, bool back)
        {
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(along - prev) * 2f), 1, 12);
            for (int k = 0; k < n; k++)
            {
                Vector3 p = TripPoint(Mathf.Lerp(prev, along, (k + 0.5f) / n), back);
                for (int i = 0; i < 3; i++) Vol2FsB1.EmitV(_spray, p + Vector3.down * 0.2f, Side * (i % 2 == 0 ? 1f : -1f) * Random.Range(1.5f, 4f) + Vector3.up * Random.Range(0.5f, 2.5f), -1f, -1f);
                Vol2FsB1.EmitV(_drops, p, Random.insideUnitSphere * 2f, -1f, -1f);
            }
        }

        protected override void OnTurn()
        {
            Vector3 p = HeadPos(Now(NowUs));
            for (int k = 0; k < 80; k++) Vol2FsB1.EmitV(_spray, p, (Random.onUnitSphere + Vector3.up * 0.5f).normalized * Random.Range(2f, 6f), -1f, -1f);
        }

        protected override void OnStruck(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 90; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 0.9f).normalized * Random.Range(2f, 6f), -1f, -1f);
            for (int k = 0; k < 6; k++) Vol2FsB1.EmitV(_mist, c + Random.insideUnitSphere * 0.5f, Random.onUnitSphere * 0.8f, -1f, -1f);
        }

        protected override void OnReturnHit(Struck s)
        {
            var c = CentreNow(s);
            for (int k = 0; k < 120; k++) Vol2FsB1.EmitV(_spray, c + Random.insideUnitSphere * 0.4f, (Random.onUnitSphere + Vector3.up * 1.1f).normalized * Random.Range(2f, 6.5f), -1f, -1f);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_drops, c, (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 5f), -1f, -1f);
        }

        protected override void OnShot(Shot s) { for (int k = 0; k < 50; k++) Vol2FsB1.EmitV(_spray, s.Pos, Random.onUnitSphere * Random.Range(1.5f, 5f), -1f, -1f); }

        protected override void OnCarry(Carry c)
        {
            var dims = Vol2FsB7.StoneDims(c.Radius);
            _stone = Vol2FsB1.Prop(Root.transform, "CarriedStone", Vol2FsB7.StoneMesh(Kit, c.Id, dims), Vol2FsB6.WallMat(Kit)).transform;
            Vector3 face = Apex - c.From; face.y = 0f; if (face.sqrMagnitude < 1e-6f) face = -F;
            _stoneRot = Quaternion.LookRotation(face.normalized, Vector3.up) * Quaternion.Euler(0f, 18f + c.Id % 23, 0f);
            _stoneFrom = c.From;
            _stone.SetPositionAndRotation(c.From, _stoneRot);
            for (int k = 0; k < 60; k++) Vol2FsB1.EmitV(_spray, c.From + Vector3.up * Random.Range(0f, dims.y), (Random.onUnitSphere + Vector3.up * 0.6f).normalized * Random.Range(2f, 5f), -1f, -1f);
            Note += " | 앙 carry seen " + c.At.ToString("0.00") + " -> set " + c.SetAt.ToString("0.00");
        }

        protected override void OnHome()
        {
            Vector3 p = Tip();
            for (int k = 0; k < 40; k++) Vol2FsB1.EmitV(_drops, p + Random.insideUnitSphere * 0.2f, Vector3.down * Random.Range(0.5f, 2f) + Random.insideUnitSphere, -1f, -1f);
        }

        void DrawStone(float t, Vector3 head, Vector3 cam)
        {
            var c = Carried; if (c == null || _stone == null) return;
            var dims = Vol2FsB7.StoneDims(c.Radius);
            if (t < c.SetAt)
            {
                // lifted where the head met it, riding home inside two coils of water
                float u = Mathf.Clamp01((t - c.At) / Mathf.Max(0.05f, c.SetAt - c.At));
                Vector3 p = Vector3.Lerp(_stoneFrom, c.To, u) + Vector3.up * (0.5f * Mathf.Sin(u * Mathf.PI) + 0.25f * Vol2Fs.Smooth(0f, 0.15f, u) * (1f - u));
                _stone.SetPositionAndRotation(p, _stoneRot * Quaternion.Euler(8f * Mathf.Sin(t * 30f), 0f, 6f * Mathf.Cos(t * 25f)));
                for (int ring = 0; ring < 2; ring++)
                {
                    int n = 26; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
                    for (int k = 0; k < n; k++)
                    {
                        float a = k / (float)(n - 1) * Mathf.PI * 2.4f + t * 14f + ring * 2f;
                        float y = dims.y * (0.25f + 0.45f * ring + 0.1f * Mathf.Sin(a * 0.5f));
                        pts[k] = p + Vector3.up * y + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (dims.x * 0.75f + 0.15f);
                        w[k] = 0.18f; al[k] = 0.95f;
                    }
                    _coil.Strip(pts, w, null, Color.white, al, 0.3f + ring * 0.3f, cam);
                }
                if (Time.frameCount % 2 == 0) for (int k = 0; k < 4; k++) Vol2FsB1.EmitV(_drops, p + Vector3.up * Random.Range(0f, dims.y), Random.insideUnitSphere * 1.5f + Vector3.down, -1f, -1f);
                return;
            }
            if (!_landed)
            {
                _landed = true; _landAt = t; HitStop(t, 0.09f);
                _stone.SetPositionAndRotation(c.To, _stoneRot);
                for (int k = 0; k < 140; k++) Vol2FsB1.EmitV(_spray, c.To + Random.insideUnitSphere * 0.3f, (new Vector3(Random.Range(-1f, 1f), 0.35f, Random.Range(-1f, 1f)).normalized * Random.Range(2.5f, 7f)), -1f, -1f);
                for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(_mist, c.To + Random.insideUnitSphere * 0.6f, Random.onUnitSphere, -1f, -1f);
                FlashLight(c.To + Vector3.up * 0.8f, 4f, t + 0.3f);
            }
            float since = t - _landAt;
            float end = c.EndAt >= 0f ? 1f - Mathf.Clamp01((t - c.EndAt) / 0.6f) : 1f;
            _stone.gameObject.SetActive(end > 0.01f);
            _stone.position = c.To + Vector3.down * dims.y * (1f - end);
            // the splash ring spreading where it landed, then it stands wet, dripping
            if (since < 0.7f) Vol2FsB4A.GroundArc(_coil, c.To, Ground, 0.4f + 2.2f * Vol2Fs.Smooth(0f, 0.7f, since), 0.12f, 0f, Mathf.PI * 2f, 1f - Vol2Fs.Smooth(0.2f, 0.7f, since), 0.5f, cam, 40);
            if (end > 0.01f && Time.frameCount % 3 == 0) Vol2FsB1.EmitV(_drops, c.To + Vector3.up * Random.Range(0.1f, dims.y) + Random.insideUnitSphere * dims.x * 0.4f, Vector3.down * 0.4f, -1f, -1f);
        }

        protected override void DrawHead(float t, Vector3 pos, Vector3 dir, bool back, float life)
        {
            var cam = CamPos;
            if (life > 0.01f && (_flown.Count == 0 || (_flown[_flown.Count - 1].P - pos).sqrMagnitude > 1e-6f)) _flown.Add((pos, t));
            _water.Clear(); _foam.Clear(); _ink.Clear(); _coil.Clear();
            float loop = LoopLife(t);
            if (_flown.Count >= 2 && loop > 0.01f)
            {
                // the serpent's body along where it flew; after home it pours down from its tail
                float pour = IsHome ? Mathf.Clamp01((t - HomeAt) / LoopTail) : 0f;
                int from = Mathf.Min(_flown.Count - 2, Mathf.FloorToInt(pour * (_flown.Count - 1)));
                var pts = new List<Vector3>(); var ww = new List<float>(); var wf = new List<float>(); var al = new List<float>();
                for (int k = from; k < _flown.Count; k++)
                {
                    float age = t - _flown[k].T, fresh = 1f - Vol2Fs.Smooth(0.1f, 1.1f, age), u = (k - from) / Mathf.Max(1f, _flown.Count - 1 - from);
                    pts.Add(_flown[k].P + Vector3.up * 0.08f * Mathf.Sin(k * 0.8f + t * 10f) + Vector3.down * 0.25f * pour);
                    ww.Add((0.45f + 0.5f * u) * (0.5f + 0.5f * fresh)); wf.Add(0.05f + 0.03f * u); al.Add(loop * (0.45f + 0.55f * fresh));
                }
                if (pts.Count >= 2)
                {
                    _water.Strip(pts, ww, null, Color.white, al, 0.37f, cam, 0f, 3f);
                    var top = new List<Vector3>(); foreach (var p in pts) top.Add(p + Vector3.up * 0.16f);
                    _foam.Strip(top, wf, null, Color.white, al, 0.71f, cam);
                    for (int j = 0; j < 2; j++)
                    {
                        var ip = new List<Vector3>(); var iw = new List<float>();
                        for (int k = 0; k < pts.Count; k++) { ip.Add(pts[k] + Vector3.up * 0.08f * Mathf.Sin(k * 1.3f + t * 8f + j * 2f) + Vector3.Cross(Vector3.up, F) * 0.06f * Mathf.Cos(k * 1.1f + j)); iw.Add(0.035f); }
                        _ink.Strip(ip, iw, null, Color.white, al, 0.2f + 0.4f * j, cam);
                    }
                }
                if (IsHome && from > 0 && Time.frameCount % 2 == 0) for (int k = 0; k < 3; k++) Vol2FsB1.EmitV(_drops, _flown[Mathf.Min(_flown.Count - 1, from + k)].P, Vector3.down * 1.5f, -1f, -1f);
            }
            if (life > 0.01f)
            {
                // the head: a bulb of water with a foam crest, open toward its travel
                Vol2FsB4A.Wrap(_water, pos, 0.45f, 0.45f, pos.y, 0f, Mathf.PI * 2f, 0.45f, life, 0.5f, cam, 0f, 20);
                Vol2FsB6.Slash(_foam, pos - Vector3.Cross(dir, Vector3.up) * 0.4f + Vector3.up * 0.25f, pos + Vector3.Cross(dir, Vector3.up) * 0.4f + Vector3.up * 0.25f, dir * 0.3f + Vector3.up * 0.2f, 0.16f, 1f, life, 0.9f, cam);
                if (Time.frameCount % 1 == 0) Vol2FsB1.EmitV(_spray, pos, -dir * 2f + Random.insideUnitSphere * 2f, -1f, -1f);
            }
            DrawStone(t, pos, cam);
            _water.Write(_waterMesh); _foam.Write(_foamMesh); _ink.Write(_inkMesh); _coil.Write(_coilMesh);
        }
    }
}
