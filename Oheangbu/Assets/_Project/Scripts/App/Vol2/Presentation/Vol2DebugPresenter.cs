using System.Collections.Generic;
using Oheangbu.Core.Domain;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>TEMPORARY (slice 3, design section 13: "the first eight slices carry the simplest visual their function needs"): draws the
    /// judged shape and the contacts as flat dark ink lines so a Play run shows WHAT was judged. It reads the same Vol2ShapeSpec the
    /// judgement used and nothing else; it never feeds anything back. It is replaced by the vol.2 VFX templates (a separate three-stage
    /// process): do not design final looks here. No emission, no light, no additive: one unlit, dark, matte line material.</summary>
    public sealed class Vol2DebugPresenter : MonoBehaviour
    {
        sealed class Line { public GameObject Go; public LineRenderer Lr; public float DieAt; public Vol2CastLog Log; public bool Follow; }

        readonly List<Line> _lines = new List<Line>();
        Vol2CastCoordinator _coordinator;
        Material _material;
        public bool Enabled = true;
        public int Drawn { get; private set; }

        public static Vol2DebugPresenter Attach(Vol2CastCoordinator coordinator, Transform parent)
        {
            var go = new GameObject("Vol2DebugPresenter");
            if (parent != null) go.transform.SetParent(parent, false);
            var p = go.AddComponent<Vol2DebugPresenter>();
            p._coordinator = coordinator;
            coordinator.CastBegan += p.OnBegan;
            coordinator.ContactResolved += p.OnContact;
            return p;
        }

        void OnDestroy()
        {
            if (_coordinator != null) { _coordinator.CastBegan -= OnBegan; _coordinator.ContactResolved -= OnContact; }
            if (_material != null) Destroy(_material);
        }

        static Color InkOf(Body b)
        {
            switch (b)
            {
                case Body.Wood: return new Color(0.20f, 0.27f, 0.20f);
                case Body.Fire: return new Color(0.42f, 0.20f, 0.14f);
                case Body.Earth: return new Color(0.42f, 0.35f, 0.20f);
                case Body.Metal: return new Color(0.42f, 0.42f, 0.44f);
                default: return new Color(0.20f, 0.27f, 0.38f);
            }
        }

        Line NewLine(Vol2CastLog log, string name, int points, float width, float life, bool loop)
        {
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                _material = new Material(shader) { name = "Vol2Debug_Ink" };
            }
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _material; lr.useWorldSpace = true; lr.loop = loop; lr.positionCount = points;
            lr.startWidth = lr.endWidth = width; lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.startColor = lr.endColor = InkOf(log.Glyph.Body);
            var line = new Line { Go = go, Lr = lr, DieAt = Time.time + life, Log = log };
            _lines.Add(line); Drawn++;
            return line;
        }

        void OnBegan(Vol2CastLog log)
        {
            if (!Enabled || log.Spec == null) return;
            var s = log.Spec; Vector3 o = s.Origin + Vector3.up * 0.06f; Vector3 f = s.Forward;
            switch (s.Kind)
            {
                case Vol2ShapeKind.Path:
                    { var l = NewLine(log, "Path", 2, s.PathWidth, 0.6f, false); l.Follow = true; l.Lr.SetPosition(0, s.Origin + Vector3.up * 1.1f); l.Lr.SetPosition(1, s.Origin + Vector3.up * 1.1f); break; }
                case Vol2ShapeKind.Fan:
                    {
                        int arc = 12; var l = NewLine(log, "Fan", arc + 3, 0.05f, 0.6f, false);
                        l.Lr.SetPosition(0, o);
                        for (int i = 0; i <= arc; i++)
                        {
                            float a = Mathf.Lerp(-s.FanHalfAngleRad, s.FanHalfAngleRad, i / (float)arc);
                            Vector3 d = Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.up) * f;
                            l.Lr.SetPosition(i + 1, o + d * s.FanDepth);
                        }
                        l.Lr.SetPosition(arc + 2, o);
                        break;
                    }
                case Vol2ShapeKind.Ring:
                    {
                        int n = 48; var l = NewLine(log, "Ring", n, s.RingThickness, 0.6f, true);
                        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; l.Lr.SetPosition(i, o + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * s.RingRadius); }
                        break;
                    }
                default:
                    {
                        var host = log.Emitter as Vol2AttachmentHost;
                        float life = host != null ? Vol2Time.ToSecondsF(host.EndUs - host.StartUs) : 0.5f;
                        var l = NewLine(log, "Attach", 2, s.SwingRadius, life, false); l.Follow = true;
                        break;
                    }
            }
        }

        void OnContact(Vol2CastLog log, Vol2ContactRecord rec)
        {
            if (!Enabled) return;
            Vector3 p = log.Spec != null ? log.Spec.Origin : Vector3.zero;
            var target = _coordinator.Registry.Find(rec.TargetId);
            if (target != null) p = target.Position;
            p += Vector3.up * 1.1f;
            var l = NewLine(log, "Contact", 2, 0.12f, 0.5f, false);
            l.Lr.SetPosition(0, p + Vector3.left * 0.35f + Vector3.up * 0.35f); l.Lr.SetPosition(1, p + Vector3.right * 0.35f - Vector3.up * 0.35f);
            var l2 = NewLine(log, "Contact", 2, 0.12f, 0.5f, false);
            l2.Lr.SetPosition(0, p + Vector3.left * 0.35f - Vector3.up * 0.35f); l2.Lr.SetPosition(1, p + Vector3.right * 0.35f + Vector3.up * 0.35f);
        }

        void Update()
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                if (Time.time >= l.DieAt || l.Go == null) { if (l.Go != null) Destroy(l.Go); _lines.RemoveAt(i); continue; }
                if (!l.Follow) continue;
                var s = l.Log.Spec;
                if (l.Log.Emitter is Vol2PathEmitter path)
                    l.Lr.SetPosition(1, s.Origin + Vector3.up * 1.1f + s.Forward * path.HeadAlong);
                else if (l.Log.Emitter is Vol2AttachmentHost)
                {
                    l.Lr.SetPosition(0, s.Origin + Vector3.up * 1.1f);
                    l.Lr.SetPosition(1, s.Origin + Vector3.up * 1.1f + s.Forward * s.BrushReach);
                }
            }
        }
    }
}
