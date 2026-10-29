using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-127 step 2 (TEST): the weapon head a sword-form glyph puts on the great brush. Presentation only: rules never
    // read it, and the judgement (reach, line, cone) never follows the drawn head.
    //   where     rigid on the shaft: a child of GripSocket (+Y runs along the shaft) at the ferrule, where the bristles start
    //             (Bristle_01). Not ShaftEndSocket: that is the butt end, where the tassel hangs (build_v5.py).
    //   bristles  they are the brush mesh's own material slot. While the head is on, that slot draws the hidden material
    //             (no depth, no shadow: nothing for the ink post to outline); Detach puts back the very same materials.
    //   shown     the close-up brush is switched off by renderer.enabled, not by its object: the head copies its brush
    //             renderer's enabled state and shadow mode every frame.
    // ExecuteAlways: an edit-mode head (checks, stills) gets the same OnDestroy as a Play one - destroyed without Detach,
    // it still gives the bristles back.
    [ExecuteAlways]
    public sealed class GreatBrushWeapon308 : MonoBehaviour
    {
        public const string GripName = "GripSocket", FerruleName = "Bristle_01";

        [SerializeField] private Material _hiddenBristle;
        [Tooltip("Turn of the head around the shaft (deg): which way the blade faces against the grip.")]
        [SerializeField, Range(-180f, 180f)] private float _roll;
        [Header("debate 13: the strike's swing (SpellSwing308 state) and its afterimage")]
        [SerializeField] private string _strikeState = "SwingSweep";
        [Tooltip("s: the clip starts this long before its apex (contact vs damage <= 2 frames; the wind-up is not shown).")]
        [SerializeField, Min(0f)] private float _strikeToApex = .033f;
        [SerializeField, Min(0f)] private float _strikeLock = .3f;
        [Header("debate 15: the finisher of a combo (empty state = the plain swing)")]
        [SerializeField] private string _finishState = "";
        [SerializeField, Min(0f)] private float _finishLock = .5f;
        [SerializeField, Min(.1f)] private float _finishWidth = 1.5f;
        [Header("D308-127 step 3: the head grows out of the ferrule, dries before the ink is gone, goes back into the bristles")]
        [Tooltip("s: the ink hardens from the ferrule outward (criterion 1: the outline within 10 % of its final size in <= .5 s).")]
        [SerializeField, Min(.01f)] private float _growSeconds = .3f;
        [Tooltip("s before the form's last moment the head starts to dry (criterion 8: 3 s; the only clock the player has - no HUD).")]
        [SerializeField, Min(.01f)] private float _drySeconds = 3f;
        [Tooltip("s: the head sinks back into the ferrule, then the bristles return (criterion 8: <= .5 s).")]
        [SerializeField, Min(.01f)] private float _retractSeconds = .3f;
        [Tooltip("sRGB: dried ink - pale bone, not gold (concept e), no brighter than the paper.")]
        [SerializeField, ColorUsage(false, false)] private Color _dried = new Color(.62f, .59f, .54f, 1f);
        [Header("D308-127 debate 16: the bow is the whole brush bent (every renderer of the brush draws the hidden material)")]
        [SerializeField] private bool _wholeBrush;

        private Renderer _brush;
        // the form's clock (presentation): grown from, last moment of the form, sinking since (NaN = not)
        private float _bornAt = float.NegativeInfinity, _lifeEnd = float.PositiveInfinity, _retractFrom = float.NaN;
        private MaterialPropertyBlock _block;
        private Color[][] _baseColours;   // per own renderer, per material slot: the shared material's _BaseColor
        private float _dryShown = -1f;
        private WeaponTrail308[] _trails = Array.Empty<WeaponTrail308>();
        private int _slot = -1;
        private Material[] _restore;
        private Renderer[] _own = Array.Empty<Renderer>();
        // whole-brush heads: every other renderer of the brush and the materials it had
        private Renderer[] _hiddenOthers = Array.Empty<Renderer>();
        private Material[][] _othersRestore = Array.Empty<Material[]>();

        public Renderer BrushRenderer => _brush;
        public int BristleSlot => _slot;
        public Material HiddenBristle => _hiddenBristle;
        public bool WholeBrush => _wholeBrush;
        public Color DriedColour => _dried;
        public string StrikeState => _strikeState;
        public float StrikeToApex => _strikeToApex;
        public float StrikeLock => _strikeLock;
        public string FinishState => _finishState;
        public float FinishLock => _finishLock;
        public float FinishWidth => _finishWidth;
        public WeaponTrail308[] Trails => _trails;
        public float GrowSeconds => _growSeconds;
        public float DrySeconds => _drySeconds;
        public float RetractSeconds => _retractSeconds;
        public bool Retracting => !float.IsNaN(_retractFrom);
        /// <summary>0 = fresh ink, 1 = dried (read by checks; what the head shows after the last Step).</summary>
        public float Dryness => Mathf.Max(0f, _dryShown);
        /// <summary>The head's size along the shaft now, 0..1 (grow, then retract).</summary>
        public float Grown => transform.localScale.y;

        // The form's clock: the head grows from now, and dries over the last DrySeconds of `seconds` (0 / infinity = no end known).
        public void SetLife(float now, float seconds)
        {
            _bornAt = now;
            _lifeEnd = seconds > 0f && !float.IsInfinity(seconds) && !float.IsNaN(seconds) ? now + seconds : float.PositiveInfinity;
            Step(now);
        }

        // The form's end moved (the bow pays ink per shot, which the first life did not know): the grow is left as it is.
        public void SetLifeEnd(float now, float seconds)
        {
            _lifeEnd = seconds > 0f && !float.IsInfinity(seconds) && !float.IsNaN(seconds) ? now + seconds : float.PositiveInfinity;
        }

        // The editor build fills these in (the prefab is made by a tool, not by hand).
        public void Configure(Material hiddenBristle, float roll, bool wholeBrush = false) { _hiddenBristle = hiddenBristle; _roll = roll; _wholeBrush = wholeBrush; }
        public void ConfigureStrike(string state, float toApex, float lockSeconds) { _strikeState = state; _strikeToApex = toApex; _strikeLock = lockSeconds; }
        public void ConfigureFinish(string state, float lockSeconds, float width) { _finishState = state ?? ""; _finishLock = lockSeconds; _finishWidth = width; }

        // One strike of the form: every afterimage of this head starts fresh (the body's swing is SpellSwing308's).
        // finish = the last strike of a combo: a wider stroke (only when this head has a finisher).
        public void Strike(bool finish = false)
        {
            float width = finish && !string.IsNullOrEmpty(_finishState) ? _finishWidth : 1f;
            foreach (var trail in _trails) if (trail != null) trail.Strike(width);
            foreach (var chain in GetComponentsInChildren<FlailChain308>(true)) chain.Strike(Time.time);
        }

        // Puts the head on the brush under brushRoot. false = no grip or ferrule there (nothing changed).
        public bool Attach(Transform brushRoot)
        {
            if (brushRoot == null) return false;
            Transform grip = Find(brushRoot, GripName), ferrule = Find(brushRoot, FerruleName);
            if (grip == null || ferrule == null) return false;
            Restore();
            // a head still sinking back on this brush (the form was cast again at once) ends now: its bristle swap must be
            // undone before this head takes the brush's materials as the ones to give back
            foreach (var other in grip.GetComponentsInChildren<GreatBrushWeapon308>(true))
                if (other != this) other.Detach(true);
            // the brush renderer whose material slot holds the bristles (found by the material's name, never by index)
            Renderer brush = null; int slot = -1;
            foreach (var renderer in brushRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform.IsChildOf(transform)) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length && slot < 0; i++)
                    if (materials[i] != null && materials[i].name.IndexOf("Bristle", StringComparison.OrdinalIgnoreCase) >= 0) slot = i;
                if (slot >= 0) { brush = renderer; break; }
            }
            transform.SetParent(grip, false);
            transform.localPosition = grip.InverseTransformPoint(ferrule.position);
            transform.localRotation = Quaternion.AngleAxis(_roll, Vector3.up);
            transform.localScale = Vector3.one;
            int layer = brush != null ? brush.gameObject.layer : grip.gameObject.layer;
            foreach (var child in GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            _own = GetComponentsInChildren<Renderer>(true);
            _baseColours = new Color[_own.Length][];
            for (int r = 0; r < _own.Length; r++)
            {
                var shared = _own[r].sharedMaterials; _baseColours[r] = new Color[shared.Length];
                for (int m = 0; m < shared.Length; m++) _baseColours[r][m] = shared[m] != null && shared[m].HasProperty("_BaseColor") ? shared[m].GetColor("_BaseColor") : Color.black;
            }
            _dryShown = -1f; _retractFrom = float.NaN;
            _trails = GetComponentsInChildren<WeaponTrail308>(true);
            foreach (var trail in _trails) trail.ShownBy(brush);
            if (brush != null && _hiddenBristle != null)
            {
                _restore = brush.sharedMaterials;
                var swapped = (Material[])_restore.Clone();
                if (_wholeBrush) for (int i = 0; i < swapped.Length; i++) swapped[i] = _hiddenBristle; else swapped[slot] = _hiddenBristle;
                brush.sharedMaterials = swapped;
            }
            if (_wholeBrush && _hiddenBristle != null) HideOthers(brushRoot, brush);
            _brush = brush; _slot = slot;
            Mirror();
            return true;
        }

        // Takes the head off: the bristles come back with the exact materials they had, then the head is destroyed.
        // immediate false (the form ended): the head first sinks back into the ferrule over RetractSeconds (Step does the rest).
        public void Detach(bool immediate = true)
        {
            if (this == null) return;
            if (!immediate && _brush != null) { if (!Retracting) _retractFrom = Time.time; return; }
            Restore();
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        private void LateUpdate() => Step(Time.time);

        // Advance the head's presentation to `now` (checks call this with their own clock).
        public void Step(float now)
        {
            if (this == null) return;
            Mirror();
            // grow out of the ferrule: along the shaft first, the width a little behind (ink hardening outward)
            float grown = float.IsNegativeInfinity(_bornAt) ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - _bornAt) / _growSeconds));
            if (Retracting)
            {
                float back = Mathf.Clamp01((now - _retractFrom) / _retractSeconds);
                grown = Mathf.Min(grown, 1f - Mathf.SmoothStep(0f, 1f, back));
                if (back >= 1f) { Detach(true); return; }
            }
            float along = Mathf.Max(.02f, grown), across = Mathf.Lerp(.55f, 1f, Mathf.Clamp01(grown * 1.25f - .25f));
            transform.localScale = new Vector3(across, along, across);
            // dry over the last DrySeconds of the form: ink and pigment both go to pale bone
            float dry = float.IsPositiveInfinity(_lifeEnd) ? 0f : Mathf.Clamp01((now - (_lifeEnd - _drySeconds)) / _drySeconds);
            if (Mathf.Abs(dry - _dryShown) > .004f) Dry(dry);
        }

        private void Dry(float dry)
        {
            _dryShown = dry;
            if (_baseColours == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            float k = dry * dry * (3f - 2f * dry);
            for (int r = 0; r < _own.Length; r++)
            {
                var renderer = _own[r]; if (renderer == null || r >= _baseColours.Length) continue;
                for (int m = 0; m < _baseColours[r].Length; m++)
                {
                    if (k <= 0f) { renderer.SetPropertyBlock(null, m); continue; }
                    renderer.GetPropertyBlock(_block, m);
                    _block.SetColor("_BaseColor", Color.Lerp(_baseColours[r][m], _dried, k));
                    renderer.SetPropertyBlock(_block, m);
                }
            }
        }

        // a head destroyed with its scene (or by anyone else) still gives the bristles back
        private void OnDestroy() => Restore();

        private void Mirror()
        {
            if (_brush == null) return;
            bool on = _brush.enabled && _brush.gameObject.activeInHierarchy;
            var shadows = _brush.shadowCastingMode;
            foreach (var renderer in _own)
                if (renderer != null) { renderer.enabled = on; renderer.shadowCastingMode = shadows; }
        }

        // A whole-brush head (the bow) also hides the brush's other renderers (shaft pieces, the tassel): every slot draws the
        // hidden material until Restore.
        private void HideOthers(Transform brushRoot, Renderer bristles)
        {
            var others = new System.Collections.Generic.List<Renderer>(); var restores = new System.Collections.Generic.List<Material[]>();
            foreach (var renderer in brushRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer == bristles || renderer.transform.IsChildOf(transform)) continue;
                var materials = renderer.sharedMaterials; var swapped = new Material[materials.Length];
                for (int i = 0; i < swapped.Length; i++) swapped[i] = _hiddenBristle;
                others.Add(renderer); restores.Add(materials);
                renderer.sharedMaterials = swapped;
            }
            _hiddenOthers = others.ToArray(); _othersRestore = restores.ToArray();
        }

        // only this head's own swap is undone: a brush whose materials were changed since by someone else is left alone
        private void Restore()
        {
            var others = _hiddenOthers; var othersRestore = _othersRestore;
            _hiddenOthers = Array.Empty<Renderer>(); _othersRestore = Array.Empty<Material[]>();
            for (int i = 0; i < others.Length && i < othersRestore.Length; i++)
            {
                var renderer = others[i]; if (renderer == null) continue;
                var now = renderer.sharedMaterials;
                if (now.Length == othersRestore[i].Length && (now.Length == 0 || now[0] == _hiddenBristle)) renderer.sharedMaterials = othersRestore[i];
            }
            var brush = _brush; var restore = _restore; int slot = _slot;
            _brush = null; _restore = null; _slot = -1;
            if (brush == null || restore == null || slot < 0) return;
            var current = brush.sharedMaterials;
            if (slot < current.Length && current[slot] == _hiddenBristle && current.Length == restore.Length) brush.sharedMaterials = restore;
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
