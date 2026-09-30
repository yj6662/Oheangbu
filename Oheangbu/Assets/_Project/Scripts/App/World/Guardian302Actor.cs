using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Oheangbu.App.World
{
    // #302 trailer giant (금강역사 기계 불상, SPEC-PLAYER-FEEL-300 G): presentation only — no combat, no AI. Plays a timed
    // sequence of Mixamo clips on its own clock ("Roaring@0,Smash@2.4,Idle@5") with short crossfades, so a film take can
    // stage the confrontation shot by shot. Heavy impacts (clip time marks) shake the view and throw ink.
    [DisallowMultipleComponent]
    public sealed class Guardian302Actor : MonoBehaviour
    {
        [Serializable]
        public sealed class Move
        {
            public string Name; public AnimationClip Clip; public bool Loop;
            [Tooltip("Clip seconds where a blow lands.")] public float[] Impacts = Array.Empty<float>();
            [Tooltip("Body centre of mass XZ over clip time (humanoid RootT, avatar-normalised): the ground travel the object follows.")]
            public AnimationCurve RootX, RootZ;
        }
        public Move[] Moves = Array.Empty<Move>();
        [Min(0)] public float Crossfade = .25f;
        public float ImpactShake = .12f;
        public GameObject ImpactPrefab;
        [Tooltip("Ink splash sprite thrown along the ground where a blow lands (ink, not light: no emission).")]
        public Material ImpactMaterial;
        [Min(0)] public int ImpactSplashes = 16;
        [Tooltip("Outward speed of the ground ink at scale 1 (m/s).")] public float ImpactSpread = 1.6f;
        [Tooltip("Splash sprite size at scale 1 (m).")] public float ImpactSize = .32f;
        public float ImpactLife = 1.1f;
        public Color ImpactInk = new Color(.07f, .065f, .06f, .85f);

        public static float Shake { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Shake = 0f; _quad = null; }   // Enter Play Mode keeps statics

        struct Cue { public int Move; public float At; }
        readonly List<Cue> _cues = new List<Cue>();
        PlayableGraph _graph; AnimationMixerPlayable _mixer; AnimationClipPlayable[] _nodes; Animator _animator;
        float _clock; int _current = -1, _previous = -1, _currentCue = -1, _previousCue = -1; float _changedAt; readonly HashSet<long> _fired = new HashSet<long>();
        float[] _sampled;   // clip time each move was last sampled at: the ground travel is its RootT difference since then

        // feet height to stand on. The clips keep their height in the pose (measured from the feet at each clip start), so the
        // object stays at this height and leaps, crouches and the fall come from the animation itself
        [NonSerialized] public float GroundY = float.NaN;
        [Tooltip("Hold the clock at 0 until a fixed-rate capture (a film take) starts, so cues line up with the take.")]
        public bool WaitForCapture = true;
        [Tooltip("Carry the object along the clips' ground travel (a lunge or leap moves the whole body; crossfades never slide it back).")]
        public bool RootMotion = true;
        float _weakAt = float.NaN, _riposteAt = float.NaN; bool _weakDone, _riposteDone;
        bool _grounded;

        public string Info()
        {
            float low = LowestFoot();
            return $"root {transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2} feet {low:F2} ground {GroundY:F2} clock {_clock:F2} move {(_current >= 0 ? Moves[_current].Name : "-")} grounded {_grounded}";
        }

        static readonly HumanBodyBones[] Feet = { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        float LowestFoot()
        {
            var anim = _animator != null ? _animator : GetComponentInChildren<Animator>(); float low = float.MaxValue;
            foreach (var b in Feet) { var t = anim.GetBoneTransform(b); if (t != null) low = Mathf.Min(low, t.position.y); }
            return low;
        }

        // once, on the first evaluated pose: the lowest foot joint onto the ground. Afterwards the height is the clips' own
        // (no per-frame chasing of the lowest bone: that lifted the body whenever a hand or a tucked leg went lower)
        void Ground()
        {
            float low = LowestFoot();
            if (low < float.MaxValue) transform.position += Vector3.up * (GroundY - low);
            // a presentation lock-on target rides at chest height (a child, so it follows the ground travel)
            var v = GetComponentInChildren<Oheangbu.Combat.EnemyVitals>();
            if (v != null) v.transform.position = new Vector3(transform.position.x, GroundY + 4.0f * transform.lossyScale.y / 4.7f, transform.position.z);
            _grounded = true;
        }

        public void Play(string sequence)
        {
            _cues.Clear(); _clock = 0f; _current = _previous = _currentCue = _previousCue = -1; _fired.Clear(); _grounded = false;
            _weakAt = float.NaN; _weakDone = false; _riposteAt = float.NaN; _riposteDone = false;
            foreach (var part in (sequence ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('@');
                if (string.Equals(kv[0].Trim(), "Riposte", StringComparison.OrdinalIgnoreCase))
                { _riposteAt = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture); continue; }
                if (string.Equals(kv[0].Trim(), "Weak", StringComparison.OrdinalIgnoreCase))
                { _weakAt = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture); continue; }
                int m = Array.FindIndex(Moves, x => string.Equals(x.Name, kv[0].Trim(), StringComparison.OrdinalIgnoreCase));
                if (m < 0) throw new ArgumentException("Guardian302Actor: no move " + kv[0]);
                _cues.Add(new Cue { Move = m, At = kv.Length > 1 ? float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture) : 0f });
            }
            _cues.Sort((a, b) => a.At.CompareTo(b.At));
            Build();
        }

        void Build()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _animator = GetComponentInChildren<Animator>();
            _animator.applyRootMotion = false; _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; _animator.runtimeAnimatorController = null;
            _graph = PlayableGraph.Create("Guardian302"); _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, Moves.Length); _nodes = new AnimationClipPlayable[Moves.Length]; _sampled = new float[Moves.Length];
            for (int i = 0; i < Moves.Length; i++)
            {
                _nodes[i] = AnimationClipPlayable.Create(_graph, Moves[i].Clip); _nodes[i].SetApplyFootIK(true);
                _graph.Connect(_nodes[i], 0, _mixer, i);
            }
            AnimationPlayableOutput.Create(_graph, "Guardian302", _animator).SetSourcePlayable(_mixer);
            _graph.Play(); Evaluate(0f);
        }

        void Update()
        {
            Shake = Mathf.Max(0f, Shake - Time.deltaTime * ImpactShake * 4f);
            bool hold = WaitForCapture && Time.captureFramerate <= 0;
            if (_graph.IsValid()) Evaluate(hold ? 0f : Time.deltaTime);
            if (!_weakDone && !float.IsNaN(_weakAt) && _clock >= _weakAt)
            {
                var v = GetComponentInChildren<Oheangbu.Combat.EnemyVitals>();
                if (v != null) { v.Restore(); v.OpenWeakPoint(); }
                _weakDone = true;
            }
            if (!_riposteDone && !float.IsNaN(_riposteAt) && _clock >= _riposteAt)
            {
                var r = FindFirstObjectByType<Riposte301>();
                _riposteDone = true;
                if (r != null && !r.Fire()) Debug.LogWarning("Guardian302Actor: riposte cue did not fire");
            }
            if (!_grounded && !float.IsNaN(GroundY) && _graph.IsValid()) Ground();
        }

        float CueStart(int cue) => cue >= 0 && cue < _cues.Count ? _cues[cue].At : 0f;
        float ClipTime(int move, float t)
        {
            var m = Moves[move]; float len = Mathf.Max(.01f, m.Clip.length); t = Mathf.Max(0f, t);
            return m.Loop ? Mathf.Repeat(t, len) : Mathf.Min(t, len);
        }
        static Vector2 RootAt(Move m, float t) => new Vector2(m.RootX != null ? m.RootX.Evaluate(t) : 0f, m.RootZ != null ? m.RootZ.Evaluate(t) : 0f);
        Vector2 Travel(int move, float from, float to)
        {
            var m = Moves[move];
            if (m.Loop && to < from) return RootAt(m, m.Clip.length) - RootAt(m, from) + RootAt(m, to) - RootAt(m, 0f);
            return RootAt(m, to) - RootAt(m, from);
        }

        void Evaluate(float dt)
        {
            _clock += dt;
            int cue = -1;
            for (int i = 0; i < _cues.Count; i++) if (_cues[i].At <= _clock) cue = i;
            if (cue < 0) cue = 0;
            int move = _cues.Count > 0 ? _cues[cue].Move : 0;
            if (cue != _currentCue)
            {
                if (move != _current) { _previous = _current; _previousCue = _currentCue; _changedAt = _clock; }
                _current = move; _currentCue = cue;
                _sampled[move] = ClipTime(move, _clock - CueStart(cue));   // the move starts here: no travel from an earlier use
            }
            float u = Crossfade <= 0 ? 1 : Mathf.Clamp01((_clock - _changedAt) / Crossfade);
            bool fading = _previous >= 0 && _previous != _current && u < 1;
            for (int i = 0; i < Moves.Length; i++) _mixer.SetInputWeight(i, i == _current ? (fading ? u : 1) : fading && i == _previous ? 1 - u : 0);

            // ground travel: each playing clip's centre-of-mass XZ since it was last sampled, blended like the poses
            Vector2 travel = Vector2.zero;
            float tc = ClipTime(_current, _clock - CueStart(_currentCue));
            travel += (fading ? u : 1f) * Travel(_current, _sampled[_current], tc); _sampled[_current] = tc;
            SetTime(_current, tc, _clock - CueStart(_currentCue), _currentCue);
            if (fading)
            {
                float tp = ClipTime(_previous, _clock - CueStart(_previousCue));
                travel += (1 - u) * Travel(_previous, _sampled[_previous], tp); _sampled[_previous] = tp;
                SetTime(_previous, tp, 0f, -1);
            }
            if (RootMotion && travel != Vector2.zero)
                transform.position += transform.rotation * new Vector3(travel.x, 0f, travel.y) * (_animator.humanScale * transform.lossyScale.y);
            _graph.Evaluate(0f);
        }

        void SetTime(int move, float ct, float t, int cue)
        {
            var m = Moves[move];
            _nodes[move].SetTime(ct);
            if (cue < 0) return;
            for (int k = 0; k < m.Impacts.Length; k++)
            {
                long key = ((long)cue << 8) | (uint)k;
                if (!m.Loop && t >= m.Impacts[k] && !_fired.Contains(key)) { _fired.Add(key); Impact(); }
            }
        }

        // a blow lands: shake the view and throw ink along the ground at the contact (between the hands, or under the body
        // when it falls)
        void Impact()
        {
            Shake = Mathf.Max(Shake, ImpactShake);
            var anim = _animator != null ? _animator : GetComponentInChildren<Animator>();
            bool body = _current >= 0 && string.Equals(Moves[_current].Name, "Die", StringComparison.OrdinalIgnoreCase);
            Vector3 at = transform.position;
            if (body) { var h = anim.GetBoneTransform(HumanBodyBones.Hips); if (h != null) at = h.position; }
            else
            {
                var l = anim.GetBoneTransform(HumanBodyBones.LeftHand); var r = anim.GetBoneTransform(HumanBodyBones.RightHand);
                if (l != null && r != null) at = (l.position + r.position) * .5f;
            }
            at.y = (float.IsNaN(GroundY) ? transform.position.y : GroundY) + .03f * transform.lossyScale.y;
            Splash(at);
            if (ImpactPrefab != null) { var fx = Instantiate(ImpactPrefab, at, Quaternion.identity); Destroy(fx, 4f); }
        }

        struct Mote { public Transform T; public MeshRenderer R; public MaterialPropertyBlock Block; public float Born, Life, Size; public Vector3 V; }
        readonly List<Mote> _motes = new List<Mote>();
        float _moteClock;
        static Mesh _quad;
        static Mesh Quad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh { name = "Guardian302Splash" };
            _quad.SetVertices(new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) });
            _quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            _quad.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0); _quad.RecalculateBounds();
            return _quad;
        }

        void Splash(Vector3 at)
        {
            if (ImpactMaterial == null) return;
            float s = transform.lossyScale.y;
            for (int i = 0; i < ImpactSplashes; i++)
            {
                var go = new GameObject("Guardian302Splash"); go.transform.position = at;
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = ImpactMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                float a = UnityEngine.Random.value * Mathf.PI * 2f, sp = ImpactSpread * s * UnityEngine.Random.Range(.35f, 1.1f);
                var v = new Vector3(Mathf.Cos(a) * sp, sp * UnityEngine.Random.Range(.15f, .7f), Mathf.Sin(a) * sp);
                _motes.Add(new Mote { T = go.transform, R = r, Block = new MaterialPropertyBlock(), Born = _moteClock, Life = ImpactLife * UnityEngine.Random.Range(.7f, 1.2f),
                    Size = ImpactSize * s * UnityEngine.Random.Range(.55f, 1.25f), V = v });
            }
        }

        void LateUpdate()
        {
            if (_motes.Count == 0) return;
            float dt = Time.deltaTime; _moteClock += dt;
            var cam = Camera.main; Transform view = Riposte301.BillboardView != null ? Riposte301.BillboardView : cam != null ? cam.transform : null;
            float floor = float.IsNaN(GroundY) ? float.MinValue : GroundY + .02f * transform.lossyScale.y;
            for (int i = _motes.Count - 1; i >= 0; i--)
            {
                var m = _motes[i]; float age = (_moteClock - m.Born) / Mathf.Max(.01f, m.Life);
                if (m.T == null || age >= 1f) { if (m.T != null) Destroy(m.T.gameObject); _motes.RemoveAt(i); continue; }
                m.V += Physics.gravity * .3f * dt; m.V.x *= 1f - 2.2f * dt; m.V.z *= 1f - 2.2f * dt;
                var p = m.T.position + m.V * dt; if (p.y < floor) { p.y = floor; m.V.y = 0f; }
                m.T.position = p; m.T.localScale = Vector3.one * m.Size * (1f + age * .9f);
                if (view != null) m.T.rotation = Quaternion.LookRotation(m.T.position - view.position, view.up);
                m.Block.SetColor("_BaseColor", new Color(ImpactInk.r, ImpactInk.g, ImpactInk.b, ImpactInk.a * Mathf.Pow(1f - age, 1.4f)));
                m.R.SetPropertyBlock(m.Block);
                _motes[i] = m;
            }
        }

        // view shake on heavy blows: offset each game camera for its render only (the rig never sees it)
        readonly Dictionary<Camera, Vector3> _shaken = new Dictionary<Camera, Vector3>();
        void OnEnable() { RenderPipelineManager.beginCameraRendering += ShakeBegin; RenderPipelineManager.endCameraRendering += ShakeEnd; }
        void ShakeBegin(ScriptableRenderContext _, Camera camera)
        {
            if (Shake <= .001f || camera.cameraType != CameraType.Game) return;
            float t = Time.time * 41f;
            _shaken[camera] = camera.transform.position;
            camera.transform.position += camera.transform.rotation * new Vector3(Mathf.PerlinNoise(t, .7f) - .5f, Mathf.PerlinNoise(1.9f, t) - .5f, 0f) * 2f * Shake;
        }
        void ShakeEnd(ScriptableRenderContext _, Camera camera) { if (_shaken.TryGetValue(camera, out var p)) { camera.transform.position = p; _shaken.Remove(camera); } }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= ShakeBegin; RenderPipelineManager.endCameraRendering -= ShakeEnd;
            foreach (var kv in _shaken) if (kv.Key != null) kv.Key.transform.position = kv.Value;
            _shaken.Clear();
            foreach (var m in _motes) if (m.T != null) Destroy(m.T.gameObject);
            _motes.Clear();
            if (_graph.IsValid()) _graph.Destroy();
        }
    }
}
