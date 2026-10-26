using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>The ㅓ 가른다 base of batch 6 (A4 것 넛 멋 섯 엇): the element becomes a blade ON the great brush for the attachment's 6 s (the emitter's EndUs:
    /// <see cref="Vol2FsCut.LineEndRel"/>; it grows at the launch, flickers in the template's warning, goes with its end). Every paid contact of a swing (Instant /
    /// Second) = the swing's arc in front of the caster (a fast thin-to-wide crescent at chest height + its phantom, Phantom Blade principles 2 / 3 / 5) and a
    /// diagonal cut across that body (sparks: principle 4); 섯's Rear contact = the mirrored arc BEHIND the caster (the back cut). The lab's swing is the scripted
    /// touch (no brush motion): the arc is drawn at the contact, sized by the spec's reach. The shared book (Vol2FsCut) parts the materials the caster passes /
    /// 섯's swing capsules split, opens the stripped bodies' seams, and the body action lands on the first body. The glyph draws its blade (<see cref="Blade"/>).</summary>
    public abstract class Vol2FsCutWear : Vol2FsCut
    {
        protected ParticleSystem PCharge, PSparks, PInk;
        Mesh _slashMesh, _phantomMesh, _inkMesh, _bodyMesh, _bodyTraceMesh; readonly Vol2FsStrips _slash = new Vol2FsStrips(), _phantom = new Vol2FsStrips(), _ink = new Vol2FsStrips(), _body = new Vol2FsStrips(), _bodyTrace = new Vol2FsStrips();
        public sealed class Slash { public Vector3 A, B, Bow; public float At, Width; public bool Rear, Arc, Body; public int Index; public Vector3 Centre; public float R, Y, A0, A1; }
        protected readonly List<Slash> Slashes = new List<Slash>();
        float _lastArcAt = -10f, _lastRearAt = -10f;
        /// <summary>The slash colour (the body's accent by default) and how wide / long it reads.</summary>
        protected virtual Color SlashTint => Acc;
        protected virtual float SlashWidth => 0.36f;
        /// <summary>r2: the bright stroke lasts this long; the slow afterimages (two accent crescents, Phantom Blade principle 3: fast cut -> slow trace) linger <see cref="TraceSeconds"/>.</summary>
        protected virtual float SlashSeconds => 0.45f;
        protected virtual float TraceSeconds => 0.95f;
        /// <summary>The swing arc's radius and height. r2: ~2 m round the caster, from high on the right (the top of the picture) down to the left -
        /// r1's 1.75 m chest-high arc was hidden behind the caster's body with the close bear.</summary>
        protected virtual float ArcRadius => Mathf.Max(1.9f, Inst.Log.Spec.BrushReach + 0.35f);
        protected virtual float ArcY => 1.35f;
        /// <summary>r2: how much higher the arc starts than it ends (a diagonal down-swing).</summary>
        protected virtual float ArcDrop => 1.5f;
        /// <summary>r3: the colour of the cut across a body (the accent, never burning to white).</summary>
        protected virtual Color BodyCutTint => Acc;
        /// <summary>The ink under the slash (a dark stroke behind the bright one).</summary>
        protected virtual bool InkUnder => true;

        protected abstract void OnSetup();
        /// <summary>Every frame while the blade stands on the brush: life 0..1 (grows at the launch, gone after the end), warn 0..1 (the template's last window).</summary>
        protected abstract void Blade(float t, float life, float warn);
        /// <summary>A slash was laid (by = the contact; null for a rear arc).</summary>
        protected virtual void OnSlash(Slash s, Struck by) { }
        protected abstract void Live(float t, float h);

        protected override void OnBegin()
        {
            var root = Root.transform;
            PCharge = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Acc, 3f, 1f)), Life0 = 0.18f, Life1 = 0.3f, Speed0 = 0f, Speed1 = 0f, Size0 = 0.05f, Size1 = 0.12f, Max = 120 }, root, "Charge");
            PSparks = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(M("fs_dot", Color.Lerp(SlashTint, Color.white, 0.5f), 5f, 1f)), Life0 = 0.2f, Life1 = 0.55f, Speed0 = 4f, Speed1 = 12f, Size0 = 0.02f, Size1 = 0.05f, Gravity = 1.3f, Drag = 0.9f, Stretch = true, StretchVel = 0.05f, StretchLen = 1.4f, Max = 900 }, root, "Sparks");
            var ink = M("fs_ink", InkC, 1f, 0f); ink.Edge = Acc * 1.4f; ink.EdgeWidth = 0.15f; ink.NoiseAmt = 0.3f; ink.Soft = 0.1f;
            PInk = Vol2Fs.Build(new Vol2FsPs { Mat = Kit.Mat(ink), Life0 = 0.4f, Life1 = 0.9f, Speed0 = 0.5f, Speed1 = 3f, Size0 = 0.06f, Size1 = 0.2f, Gravity = 1.2f, Drag = 0.6f, Tiles = 2, AlphaHoldTo = 0.6f, Max = 400 }, root, "Ink");
            foreach (var ps in new[] { PCharge, PSparks, PInk }) ps.Play(false);
            var sl = M("fs_streak", SlashTint, 3.2f, 1f); sl.NoiseAmt = 0.15f; sl.Soft = 0.02f; sl.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "Slash", Kit.Mat(sl), out _slashMesh);
            Vol2Fs.MeshObject(root, "SlashPhantom", Vol2FsB4A.PhantomMat(Kit, SlashTint, 1.4f), out _phantomMesh);
            // r3: the cut across the body in its own softer accent material (r2: a fat yellow-white lens in mid-picture on all five)
            var bc = M("fs_streak", BodyCutTint, 1.5f, 0.55f); bc.NoiseAmt = 0.18f; bc.Soft = 0.02f; bc.Erode = 0.1f;
            Vol2Fs.MeshObject(root, "BodyCut", Kit.Mat(bc), out _bodyMesh);
            Vol2Fs.MeshObject(root, "BodyCutTrace", Vol2FsB4A.PhantomMat(Kit, BodyCutTint, 0.9f), out _bodyTraceMesh);
            Vol2Fs.MeshObject(root, "SlashInk", Kit.InkSolid("fs_stroke2", InkC, SlashTint * 1.3f, 0.12f, 0.45f, 0.3f, 1.3f), out _inkMesh);
            OnSetup();
        }

        protected override void OnCharge(float t, float k)
        {
            if (Time.frameCount % 2 == 0) Vol2FsB1.EmitV(PCharge, Shaft(Random.Range(0.4f, 1.05f)) + Random.insideUnitSphere * 0.12f * (1f - k), Vector3.zero, 0.05f + 0.1f * k, 0.2f);
        }
        protected override void OnLaunch(long nowUs) { }
        protected override void OnHit(long nowUs) { }

        /// <summary>The blade standing on the brush (0..1).</summary>
        protected float BladeLife(float t) => LineLife(t, 0.18f, 0.45f);

        Slash Arc(float t, bool rear)
        {
            var s = new Slash { At = t, Rear = rear, Arc = true, Index = Slashes.Count, Centre = G(Inst.Origin), R = rear ? ArcRadius * 0.85f : ArcRadius, Y = rear ? ArcY * 0.8f : ArcY };
            // front: right -> left over the front (radians from the forward, + = right); rear: the mirror behind (left-back -> right-back)
            if (!rear) { s.A0 = 1.35f; s.A1 = -1.35f; } else { s.A0 = Mathf.PI - 1.15f; s.A1 = Mathf.PI + 1.15f; }
            s.Width = SlashWidth * (rear ? 1.3f : 1.7f);
            Slashes.Add(s);
            return s;
        }

        protected override void OnStruck(Struck s)
        {
            float t = Now(NowUs);
            if (s.Kind == "Rear")
            {
                if (t - _lastRearAt > 0.2f) { _lastRearAt = t; var ra = Arc(t, true); try { OnSlash(ra, s); } catch (System.Exception e) { Debug.LogException(e); } }
                return;
            }
            if (s.Kind != "Instant" && s.Kind != "Second") return;
            if (t - _lastArcAt > 0.2f) { _lastArcAt = t; var fa = Arc(t, false); try { OnSlash(fa, s); } catch (System.Exception e) { Debug.LogException(e); } }
            var b = s.T != null ? BodyBounds(s.T, s.T.position, 0.5f) : s.B;
            Vector3 toCam = FlatN(CamPos - b.center), side = Vector3.Cross(Vector3.up, toCam);
            Vector3 c = b.center + toCam * (Mathf.Max(b.extents.x, b.extents.z) + 0.22f);
            float dir = s.Kind == "Second" ? -1f : 1f;      // the second swing cuts the other way
            // r2: bigger (the close bear is mostly behind the caster): from high above its far shoulder down past its near side
            var cut = new Slash { At = t, Body = true, Index = Slashes.Count, Width = SlashWidth * 0.7f, A = c + side * 1.5f * dir + Vector3.up * 1.6f, B = c - side * 1.4f * dir - Vector3.up * 0.7f, Bow = toCam * 0.35f + Vector3.up * 0.15f };
            Slashes.Add(cut);
            for (int k = 0; k < 70; k++) Vol2FsB1.EmitV(PSparks, c, (side * dir * (k % 2 == 0 ? 1f : -0.4f) + Random.insideUnitSphere * 0.7f + Vector3.up * 0.3f).normalized * Random.Range(4f, 12f), -1f, -1f);
            for (int k = 0; k < 8; k++) Vol2FsB1.EmitV(PInk, c, (Random.onUnitSphere + toCam * 0.4f).normalized * Random.Range(2f, 5f), -1f, -1f);
            try { OnSlash(cut, s); } catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>r3: a razor stroke a -> b bowed by <paramref name="bow"/>: needle-sharp at both ends, only a little thicker in the middle (not a lens).</summary>
        static void Edge(Vol2FsStrips st, Vector3 a, Vector3 b, Vector3 bow, float width, float reveal, float life, float rnd, Vector3 cam, int n = 16)
        {
            if (life <= 0.01f || reveal <= 0.01f) return;
            var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = v * reveal;
                pts[k] = Vector3.Lerp(a, b, u) + bow * 4f * u * (1f - u);
                float x = Mathf.Max(0f, Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI));      // sin(pi) is -8.7e-8: Pow(negative, 2.4) = NaN (the lab's "invalid MinMaxAABB")
                w[k] = width * (0.08f + 0.92f * Mathf.Pow(x, 2.4f)); al[k] = life * (0.55f + 0.45f * x);
            }
            st.Strip(pts, w, null, Color.white, al, rnd, cam, 0f, 1f);
        }

        /// <summary>The arc of a swing as points (for a glyph that draws its own along it).</summary>
        protected Vector3 ArcPoint(Slash s, float u)
        {
            float a = Mathf.Lerp(s.A0, s.A1, u); var p = s.Centre + Dir(a) * s.R;
            float drop = s.Rear ? 0.5f : ArcDrop;
            p.y = Ground + s.Y + drop * (0.5f - u) + 0.3f * Mathf.Sin(u * Mathf.PI);
            return p;
        }

        void DrawArc(Vol2FsStrips st, Slash s, float reveal, float life, float widthK, float lag, float rnd, Vector3 cam, Vector3 offset = default)
        {
            int n = 24; var pts = new Vector3[n]; var w = new float[n]; var al = new float[n];
            for (int k = 0; k < n; k++)
            {
                float v = k / (float)(n - 1), u = Mathf.Clamp01(v * reveal - lag);
                pts[k] = ArcPoint(s, u) + offset;
                float lead = Mathf.Clamp01(u / Mathf.Max(0.05f, reveal));
                w[k] = s.Width * widthK * Mathf.Sin(Mathf.Pow(lead, 1.35f) * Mathf.PI * 0.92f + 0.05f);
                al[k] = life * (0.3f + 0.7f * lead);
            }
            st.Strip(pts, w, null, Color.white, al, rnd, cam, 0f, 1f);
        }

        protected override void OnLive(float t, float h)
        {
            var cam = CamPos;
            float life = BladeLife(t), warn = LineWarn(t);
            float flick = warn > 0f ? (Mathf.Sin(t * 40f) > 0f ? 1f : 0.45f) : 1f;
            try { Blade(t, life * flick, warn); } catch (System.Exception e) { Debug.LogException(e); }
            _slash.Clear(); _phantom.Clear(); _ink.Clear(); _body.Clear(); _bodyTrace.Clear();
            foreach (var s in Slashes)
            {
                float a = t - s.At; if (a < 0f || a > Mathf.Max(SlashSeconds, TraceSeconds) + 0.1f) continue;
                // r2 (Phantom Blade principle 3): the stroke is drawn in ~0.07 s and goes bright -> gone in SlashSeconds; two accent afterimages stay, drift a
                // little and fade slowly over TraceSeconds
                float reveal = Vol2Fs.Smooth(0f, s.Arc ? 0.07f : 0.05f, a), lf = 1f - Vol2Fs.Smooth(SlashSeconds * 0.25f, SlashSeconds, a);
                float tr = (1f - Vol2Fs.Smooth(0.15f, TraceSeconds, a)) * Vol2Fs.Smooth(0.02f, 0.08f, a);
                Vector3 drift = Vector3.up * 0.18f * Vol2Fs.Smooth(0f, TraceSeconds, a);
                if (s.Arc)
                {
                    if (lf > 0.01f) DrawArc(_slash, s, reveal, lf, 1f, 0f, 0.31f + s.Index * 0.1f, cam);
                    if (InkUnder && lf > 0.01f) DrawArc(_ink, s, reveal, lf * 0.8f, 1.45f, 0.02f, 0.47f + s.Index * 0.1f, cam);
                    if (tr > 0.01f) { DrawArc(_phantom, s, reveal, tr * 0.55f, 1.2f, 0.05f, 0.61f, cam, drift); DrawArc(_phantom, s, reveal, tr * 0.32f, 1.4f, 0.11f, 0.83f, cam, drift * 2f); }
                }
                else
                {
                    if (lf > 0.01f) Edge(_body, s.A, s.B, s.Bow, s.Width, reveal, lf, 0.33f + s.Index * 0.1f, cam);
                    if (InkUnder && lf > 0.01f) Edge(_ink, s.A - s.Bow * 0.2f, s.B - s.Bow * 0.2f, s.Bow, s.Width * 1.6f, reveal, lf * 0.7f, 0.52f, cam);
                    if (tr > 0.01f)
                    {
                        Edge(_bodyTrace, s.A + s.Bow * 0.4f + drift, s.B + s.Bow * 0.4f + drift, s.Bow, s.Width * 1.2f, reveal, tr * 0.5f, 0.66f, cam);
                        Edge(_bodyTrace, s.A + s.Bow * 0.8f + drift * 2f, s.B + s.Bow * 0.8f + drift * 2f, s.Bow, s.Width * 1.35f, reveal, tr * 0.28f, 0.79f, cam);
                    }
                }
            }
            _slash.Write(_slashMesh); _phantom.Write(_phantomMesh); _ink.Write(_inkMesh); _body.Write(_bodyMesh); _bodyTrace.Write(_bodyTraceMesh);
            try { Live(t, h); } catch (System.Exception e) { Debug.LogException(e); }
        }

        // ---- the blade on the brush: helpers ----

        /// <summary>A rotation that stands a +Y mesh along the shaft (from the grip to the tip), its flat face (local Z) toward the camera.</summary>
        protected Quaternion AlongShaft(float roll = 0f)
        {
            Vector3 ax = ShaftAxis, view = (CamPos - Shaft(0.8f)).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(view, ax); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, ax);
            return Quaternion.AngleAxis(roll, ax) * Quaternion.LookRotation(fwd.normalized, ax);
        }
        /// <summary>The camera-side "sideways" of the shaft (perpendicular to it, in the picture plane).</summary>
        protected Vector3 ShaftSide { get { Vector3 ax = ShaftAxis, view = (CamPos - Shaft(0.8f)).normalized; var s = Vector3.Cross(ax, view); return s.sqrMagnitude > 1e-4f ? s.normalized : Right; } }
    }
}
