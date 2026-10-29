using System.Collections.Generic;
using Oheangbu.Combat;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>Where the parts of one enemy's body are (resolved once per enemy). A null part falls back to a share of the body's
    /// height over its root.</summary>
    public sealed class RegionAnchors308
    {
        public Transform Root, Weapon, Chest, Head, Hips;
        public float Height = 1.8f, Radius = .4f;
        /// <summary>m from the root to the feet along the world's up (a body whose pivot is not at its feet: the Creatures308 bodies sit at mid-body).</summary>
        public float Feet;
        public string WeaponFrom = "fallback", HeadFrom = "fallback", ChestFrom = "fallback";
    }

    // #308 D308-127 (debate 14): the region marks of every enemy the stage watches. The stage hands it each enemy's reading (at the
    // stage's watch rate); it keeps at most RegionMarks308SO.MaxRegions marks per enemy, the first ones of the priority, and fades
    // out the ones that came off. Anchors, by Fable's chain: the enemy's own organ roles (EnemyOrganSet: Weapon, else Mouth;
    // Eye; Chest) -> a Humanoid's bones (the hand that holds a Part308_ piece, else the right hand; Head; UpperChest / Chest) ->
    // bone names (head, jaw) -> a share of the body's height. Legs and ankles are drawn from the root (they reach the ground).
    public sealed class EnemyRegionMarks308
    {
        private readonly Transform _parent;
        private readonly RegionMarks308SO _data;
        private readonly InkRegionMark308[] _marks;
        private readonly Dictionary<int, RegionAnchors308> _anchors = new Dictionary<int, RegionAnchors308>();
        private readonly BodyRegion308[] _picked = new BodyRegion308[3];
        private readonly int _layer;

        public int Shown { get; private set; }
        public int Refused { get; private set; }
        public RegionMarks308SO Data => _data;

        public EnemyRegionMarks308(Transform parent, RegionMarks308SO data, int layer)
        {
            _parent = parent; _data = data; _layer = layer;
            _marks = new InkRegionMark308[Mathf.Clamp(data != null ? data.Capacity : 0, 1, 64)];
        }

        public int ActiveCount { get { int n = 0; foreach (var m in _marks) if (m != null && m.Busy && !m.Releasing) n++; return n; } }
        public IEnumerable<InkRegionMark308> Marks { get { foreach (var m in _marks) if (m != null && m.Busy) yield return m; } }

        /// <summary>One enemy's reading: the parts it should show now. `standOff` = how far its root-carried marks stand toward the eye.</summary>
        public void Watch(Transform enemy, PresentState308 state, float standOff, float now)
        {
            if (enemy == null || _data == null || _data.Material == null) return;
            int count = RegionMarks308SO.Pick(state, _data.Priority, _data.MaxRegions, _picked);
            // the parts that came off fade out
            foreach (var mark in _marks)
            {
                if (mark == null || !mark.Busy || mark.Releasing || mark.Enemy != enemy) continue;
                bool keep = false;
                for (int i = 0; i < count; i++) if (_picked[i] == mark.Region) keep = true;
                if (!keep) mark.Release(now);
            }
            if (count == 0) return;
            var anchors = AnchorsOf(enemy);
            for (int i = 0; i < count; i++)
            {
                var region = _picked[i];
                if (Find(enemy, region) != null) continue;
                var mark = Rent();
                if (mark == null) { Refused++; continue; }
                Place(region, anchors, standOff, out var carrier, out float lift, out float stand);
                float scale = anchors.Height / _data.ReferenceHeight;
                if (mark.Show(region, enemy, carrier, lift, stand, scale, _data.InkTint, _data.FadeIn, _data.FadeOut, now)) Shown++;
            }
        }

        public void Sample(float now, Vector3 eye) { foreach (var m in _marks) if (m != null && m.Busy) m.Sample(now, eye); }

        public void EndAll() { foreach (var m in _marks) if (m != null) m.Hide(); _anchors.Clear(); }

        public void Destroy()
        {
            for (int i = 0; i < _marks.Length; i++)
            {
                if (_marks[i] == null) continue;
                _marks[i].ReleaseResources();
                if (Application.isPlaying) Object.Destroy(_marks[i].gameObject); else Object.DestroyImmediate(_marks[i].gameObject);
                _marks[i] = null;
            }
            _anchors.Clear();
        }

        public InkRegionMark308 Find(Transform enemy, BodyRegion308 region)
        {
            foreach (var m in _marks) if (m != null && m.Busy && !m.Releasing && m.Enemy == enemy && m.Region == region) return m;
            return null;
        }

        private InkRegionMark308 Rent()
        {
            for (int i = 0; i < _marks.Length; i++)
            {
                if (_marks[i] == null)
                {
                    var holder = new GameObject("RegionMark308_" + i) { hideFlags = HideFlags.DontSave };
                    holder.transform.SetParent(_parent, false);
                    _marks[i] = holder.AddComponent<InkRegionMark308>();
                    _marks[i].Prepare(_data.Material, _layer);
                    return _marks[i];
                }
                if (!_marks[i].Busy) return _marks[i];
            }
            return null;
        }

        private RegionAnchors308 AnchorsOf(Transform enemy)
        {
            int key = enemy.GetInstanceID();
            if (_anchors.TryGetValue(key, out var a) && a.Root == enemy) return a;
            a = Resolve(enemy);
            _anchors[key] = a;
            return a;
        }

        /// <summary>Where a region's mark is carried: the carrier, its lift along the world's up and its stand-off toward the eye.</summary>
        public static void Place(BodyRegion308 region, RegionAnchors308 a, float rootStandOff, out Transform carrier, out float lift, out float standOff)
        {
            float s = a.Height / 1.8f;
            carrier = a.Root; lift = a.Feet; standOff = Mathf.Max(rootStandOff, a.Radius + .05f);
            switch (region)
            {
                case BodyRegion308.Legs:
                case BodyRegion308.Ankles:
                    // still 1: a body's pivot need not stand over its legs - a Humanoid's legs hang under its hips (the feet's
                    // height kept as it was found)
                    if (a.Hips != null) { carrier = a.Hips; lift = a.Root.position.y + a.Feet - a.Hips.position.y; }
                    break;
                case BodyRegion308.WeaponHand:
                    if (a.Weapon != null) { carrier = a.Weapon; lift = 0f; standOff = .1f * s; } else lift = a.Feet + a.Height * .55f;
                    break;
                case BodyRegion308.Chest:
                case BodyRegion308.Collar:
                    if (a.Chest != null) { carrier = a.Chest; lift = 0f; standOff = .16f * s; } else lift = a.Feet + a.Height * .62f;
                    // the seal sits above the chest's install mark (InkPresentForms308 draws that at the chest's middle)
                    if (region == BodyRegion308.Collar) lift += .18f * s;
                    break;
                case BodyRegion308.Head:
                    // (still 1: .14 hid the band behind a talisman hung over the bandit's face)
                    if (a.Head != null) { carrier = a.Head; standOff = .24f * s; lift = .06f * s; } else lift = a.Feet + a.Height * .93f;
                    break;
            }
        }

        /// <summary>The anchors of an enemy's body (also used by the checks).</summary>
        public static RegionAnchors308 Resolve(Transform enemy)
        {
            var a = new RegionAnchors308 { Root = enemy };
            // height: the highest bone the skin follows + 10 cm (check 1: a bandit's colliders reach .86 m of its body, and skinned
            // bounds are loose - a wolf read 2.1 m); a body with no skin: its colliders. Radius: the colliders.
            var bounds = new Bounds(enemy.position, Vector3.zero); bool any = false;
            foreach (var c in enemy.GetComponentsInChildren<Collider>(true)) { if (c.isTrigger) continue; if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds); }
            if (any) a.Radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), .15f, 3f);
            // (from the lowest bone, not the root: check 2 found the body prefabs' pivots at mid-body)
            float top = float.NegativeInfinity, low = float.PositiveInfinity;
            foreach (var skin in enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.bones != null) foreach (var bone in skin.bones) if (bone != null) { top = Mathf.Max(top, bone.position.y); low = Mathf.Min(low, bone.position.y); }
            if (!float.IsNegativeInfinity(top) && top - low > .2f) { a.Feet = Mathf.Min(0f, low - .05f - enemy.position.y); a.Height = top + .1f - (enemy.position.y + a.Feet); }
            else if (any && bounds.max.y - enemy.position.y > .3f) { a.Feet = Mathf.Min(0f, bounds.min.y - enemy.position.y); a.Height = bounds.max.y - bounds.min.y; }
            // 1. the enemy's own organs (presentation data authored per body)
            var organs = enemy.GetComponentInChildren<EnemyOrganSet>(true);
            if (organs != null)
            {
                Transform mouth = null;
                for (int i = 0; i < organs.Count; i++)
                {
                    var organ = organs.Get(i); if (organ == null) continue;
                    var anchor = organs.AnchorOf(i);
                    if (organ.Role == EnemyOrganRole.Weapon && a.Weapon == null) { a.Weapon = anchor; a.WeaponFrom = "organ Weapon"; }
                    else if (organ.Role == EnemyOrganRole.Mouth && mouth == null) mouth = anchor;
                    else if (organ.Role == EnemyOrganRole.Eye && a.Head == null) { a.Head = anchor; a.HeadFrom = "organ Eye"; }
                    else if (organ.Role == EnemyOrganRole.Chest && a.Chest == null) { a.Chest = anchor; a.ChestFrom = "organ Chest"; }
                }
                if (a.Weapon == null && mouth != null) { a.Weapon = mouth; a.WeaponFrom = "organ Mouth"; }
            }
            // 2. a Humanoid's bones
            Animator human = null;
            foreach (var animator in enemy.GetComponentsInChildren<Animator>(true)) if (animator.avatar != null && animator.isHuman) { human = animator; break; }
            if (human != null)
            {
                if (a.Weapon == null)
                {
                    Transform right = human.GetBoneTransform(HumanBodyBones.RightHand), left = human.GetBoneTransform(HumanBodyBones.LeftHand);
                    if (Holds(right)) { a.Weapon = right; a.WeaponFrom = "right hand (holds a part)"; }
                    else if (Holds(left)) { a.Weapon = left; a.WeaponFrom = "left hand (holds a part)"; }
                    else if (right != null) { a.Weapon = right; a.WeaponFrom = "right hand"; }
                }
                if (a.Head == null) { a.Head = human.GetBoneTransform(HumanBodyBones.Head); if (a.Head != null) a.HeadFrom = "Head bone"; }
                a.Hips = human.GetBoneTransform(HumanBodyBones.Hips);
                var skull = human.GetBoneTransform(HumanBodyBones.Head);
                if (skull != null) a.Height = Mathf.Max(a.Height, skull.position.y + .15f - (enemy.position.y + a.Feet));
                if (a.Chest == null)
                {
                    a.Chest = human.GetBoneTransform(HumanBodyBones.UpperChest);
                    if (a.Chest == null) a.Chest = human.GetBoneTransform(HumanBodyBones.Chest);
                    if (a.Chest != null) a.ChestFrom = "chest bone";
                }
            }
            // 3. bone names (generic rigs: four-legged bodies have a head and a jaw, no hands)
            if (a.Head == null || a.Weapon == null)
                foreach (var t in enemy.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.StartsWith("legacy298_")) continue;
                    if (a.Head == null && (n == "head" || n.StartsWith("head_") || n.EndsWith(":head") || n == "head_m")) { a.Head = t; a.HeadFrom = "bone " + t.name; }
                    if (a.Weapon == null && n.Contains("jaw")) { a.Weapon = t; a.WeaponFrom = "bone " + t.name; }
                }
            if (a.Weapon == null && a.Head != null && human == null) { a.Weapon = a.Head; a.WeaponFrom = "head (no hand, no jaw)"; }
            return a;
        }

        // a hand that holds a model part (Creatures308 hangs weapons as Part308_ children of the hand bone)
        private static bool Holds(Transform hand)
        {
            if (hand == null) return false;
            foreach (var child in hand.GetComponentsInChildren<Transform>(true)) if (child.name.StartsWith("Part308_")) return true;
            return false;
        }
    }
}
