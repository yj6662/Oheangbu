using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-127 debate 16 (TEST): one flying ink arrow of the horn bow. Presentation only: the hit is the rule's (the arrow
    // keeps the same flight time and follows the same target, so it arrives when the damage does).
    //   flight  an ink stroke `length` long with a vermilion-edged head (the head's own short stroke) and a soot tail no longer
    //           than `tail`, behind the head. Width >= .08 m (3 px at 15 m). No glow, no fire.
    //   hit     the target is still there on arrival: a small black ink splash (no red: nothing that reads as blood) and the arrow
    //           stuck in it unravels into drooping strands that fade over .5 s.
    //   miss    no target, or it is gone: the arrow reaches its point and scatters into falling strands.
    // It destroys itself when done. Edit-mode checks drive Step with their own clock.
    public sealed class BowArrow308 : MonoBehaviour
    {
        const int Strands = 3, Splash = 6, StrandPoints = 6;
        public const float UnravelSeconds = .5f, SplashSeconds = .4f;

        private LineRenderer _shaft, _head, _tailLine;
        private readonly LineRenderer[] _strands = new LineRenderer[Strands];
        private readonly LineRenderer[] _splash = new LineRenderer[Splash];
        private readonly Vector3[] _splashDirs = new Vector3[Splash];
        private Transform _target;
        private Vector3 _from, _point, _offset, _dir, _landPoint;
        private float _born, _flight, _width, _length, _headLength, _tail, _landedAt = float.NaN;
        private bool _hit;

        public bool Landed => !float.IsNaN(_landedAt);
        public bool Hit => _hit;
        public Vector3 HeadPosition { get; private set; }
        public float Width => _width;
        public float Length => _length;
        public bool Done { get; private set; }

        public static BowArrow308 Launch(Vector3 from, Transform target, Vector3 point, float flight, Material ink, Material head,
            float width, float length, float headLength, float tail, int layer, float born = float.NaN)
        {
            var go = new GameObject("BowArrow308") { layer = layer };
            var arrow = go.AddComponent<BowArrow308>();
            arrow._target = target; arrow._from = from; arrow._point = point; arrow._offset = target != null ? point - target.position : Vector3.zero;
            arrow._flight = Mathf.Max(0f, flight); arrow._width = width; arrow._length = length; arrow._headLength = headLength; arrow._tail = tail;
            arrow._born = float.IsNaN(born) ? Now : born;
            arrow._shaft = arrow.Child("Shaft", ink, 2, width);
            arrow._head = arrow.Child("Head", head, 2, width);
            arrow._tailLine = arrow.Child("Tail", ink, 2, width * .6f);
            arrow._tailLine.colorGradient = Fade(.55f, 0f);
            for (int i = 0; i < Strands; i++) { arrow._strands[i] = arrow.Child("Strand" + i, ink, StrandPoints, width * .45f); arrow._strands[i].enabled = false; }
            for (int i = 0; i < Splash; i++) { arrow._splash[i] = arrow.Child("Splash" + i, ink, 2, width * .7f); arrow._splash[i].enabled = false; }
            arrow.Step(arrow._born);
            return arrow;
        }

        private static float Now => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

        private void Update() => Step(Now);

        public void Step(float now)
        {
            if (Done || this == null) return;
            if (!Landed)
            {
                float u = _flight <= 0f ? 1f : Mathf.Clamp01((now - _born) / _flight);
                Vector3 end = _target != null && _target.gameObject.activeInHierarchy ? _target.position + _offset : _point;
                Vector3 at = Vector3.Lerp(_from, end, u);
                _dir = (end - _from).sqrMagnitude > 1e-6f ? (end - _from).normalized : Vector3.forward;
                HeadPosition = at;
                Set(_head, at, at - _dir * _headLength);
                Set(_shaft, at - _dir * _headLength, at - _dir * _length);
                Set(_tailLine, at - _dir * _length, at - _dir * (_length + _tail));
                if (u >= 1f) Land(now, end);
                return;
            }
            float t = (now - _landedAt) / UnravelSeconds;
            if (_hit)
            {
                // the stuck arrow (half of it out of the body) unravels into strands that droop and fade
                for (int s = 0; s < Strands; s++)
                {
                    var line = _strands[s]; line.enabled = t < 1f;
                    Vector3 side = Quaternion.AngleAxis(120f * s + 30f, _dir) * Vector3.Cross(_dir, Vector3.up).normalized;
                    for (int i = 0; i < StrandPoints; i++)
                    {
                        float k = i / (StrandPoints - 1f);
                        Vector3 p = _landPoint - _dir * (_length * .5f * k) + side * (.06f * t * k) + Vector3.down * (.3f * t * t * k * k);
                        line.SetPosition(i, p);
                    }
                    line.widthMultiplier = _width * .45f * (1f - .6f * Mathf.Clamp01(t));
                    line.colorGradient = Fade(1f - Mathf.Clamp01(t), 1f - Mathf.Clamp01(t));
                }
                float r = Mathf.Clamp01((now - _landedAt) / .12f);
                float fade = 1f - Mathf.Clamp01((now - _landedAt - .12f) / (SplashSeconds - .12f));
                for (int i = 0; i < Splash; i++)
                {
                    var line = _splash[i]; line.enabled = fade > 0f;
                    Set(line, _landPoint + _splashDirs[i] * .04f, _landPoint + _splashDirs[i] * (.04f + .18f * r * (1f - .35f * (i % 2))));
                    line.colorGradient = Fade(fade, fade * .6f);
                }
            }
            else
            {
                // a miss: the arrow falls apart where it ended
                for (int s = 0; s < Strands; s++)
                {
                    var line = _strands[s]; line.enabled = t < 1f;
                    Vector3 side = Quaternion.AngleAxis(120f * s, _dir) * Vector3.Cross(_dir, Vector3.up).normalized;
                    for (int i = 0; i < StrandPoints; i++)
                    {
                        float k = i / (StrandPoints - 1f);
                        line.SetPosition(i, _landPoint - _dir * (_length * k * (1f - .3f * t)) + side * (.15f * t) + Vector3.down * (.8f * t * t));
                    }
                    line.colorGradient = Fade(1f - Mathf.Clamp01(t), 1f - Mathf.Clamp01(t));
                }
            }
            if (now - _landedAt >= Mathf.Max(UnravelSeconds, SplashSeconds))
            {
                Done = true;
                if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
            }
        }

        private void Land(float now, Vector3 end)
        {
            _landedAt = now; _landPoint = end;
            _hit = _target != null && _target.gameObject.activeInHierarchy;
            _shaft.enabled = false; _head.enabled = false; _tailLine.enabled = false;
            if (_hit) transform.SetParent(_target, true);
            Vector3 face = -_dir, a = Vector3.Cross(face, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(face, Vector3.up).normalized : Vector3.right, b = Vector3.Cross(face, a);
            for (int i = 0; i < Splash; i++)
            {
                float angle = (i + .35f * (i % 2)) * Mathf.PI * 2f / Splash;
                _splashDirs[i] = (a * Mathf.Cos(angle) + b * Mathf.Sin(angle) + face * .3f).normalized;
            }
            Step(now);
        }

        private LineRenderer Child(string name, Material material, int points, float width)
        {
            var go = new GameObject(name) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            Setup(line, material, points, width);
            return line;
        }

        // the ink line every bow stroke uses: world points, facing the camera, no shadow, no light probe
        public static void Setup(LineRenderer line, Material material, int points, float width)
        {
            line.useWorldSpace = true; line.sharedMaterial = material; line.positionCount = points; line.widthMultiplier = width;
            line.numCapVertices = 2; line.alignment = LineAlignment.View; line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static void Set(LineRenderer line, Vector3 a, Vector3 b) { line.positionCount = 2; line.SetPosition(0, a); line.SetPosition(1, b); }

        private static Gradient Fade(float head, float tail)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(Mathf.Clamp01(head), 0f), new GradientAlphaKey(Mathf.Clamp01(tail), 1f) });
            return g;
        }
    }
}
