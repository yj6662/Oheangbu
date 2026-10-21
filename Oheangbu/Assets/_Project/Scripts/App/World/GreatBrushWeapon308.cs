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

        private Renderer _brush;
        private int _slot = -1;
        private Material[] _restore;
        private Renderer[] _own = Array.Empty<Renderer>();

        public Renderer BrushRenderer => _brush;
        public int BristleSlot => _slot;
        public Material HiddenBristle => _hiddenBristle;

        // The editor build fills these in (the prefab is made by a tool, not by hand).
        public void Configure(Material hiddenBristle, float roll) { _hiddenBristle = hiddenBristle; _roll = roll; }

        // Puts the head on the brush under brushRoot. false = no grip or ferrule there (nothing changed).
        public bool Attach(Transform brushRoot)
        {
            if (brushRoot == null) return false;
            Transform grip = Find(brushRoot, GripName), ferrule = Find(brushRoot, FerruleName);
            if (grip == null || ferrule == null) return false;
            Restore();
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
            if (brush != null && _hiddenBristle != null)
            {
                _restore = brush.sharedMaterials;
                var swapped = (Material[])_restore.Clone(); swapped[slot] = _hiddenBristle;
                brush.sharedMaterials = swapped;
            }
            _brush = brush; _slot = slot;
            Mirror();
            return true;
        }

        // Takes the head off: the bristles come back with the exact materials they had, then the head is destroyed.
        public void Detach()
        {
            Restore();
            if (this == null) return;
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        private void LateUpdate() => Mirror();

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

        // only this head's own swap is undone: a brush whose materials were changed since by someone else is left alone
        private void Restore()
        {
            var brush = _brush; var restore = _restore; int slot = _slot;
            _brush = null; _restore = null; _slot = -1;
            if (brush == null || restore == null || slot < 0) return;
            var now = brush.sharedMaterials;
            if (slot < now.Length && now[slot] == _hiddenBristle && now.Length == restore.Length) brush.sharedMaterials = restore;
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
