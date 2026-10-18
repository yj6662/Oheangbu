using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.61: the seal the install letters (감 남 맘 삼 암) leave on the marked enemy. The cue body
    // is cut to 3 s, but a mark lives for flight + life; this puts a Vfx308ZoneBody on the enemy at the Anchor cue and keeps
    // the mark's clock (dark for 3 s, faint after - D308-110). Detonate breaks it (Splash), Expire / TargetDefeated / End
    // dry it. The seal sits at the middle of the enemy's renderers, sized by their bounds (clamped), parented to the enemy so
    // it follows; before drying it is let go into the scene so a despawning enemy does not take it along abruptly.
    // Side table only - the seals are not Vfx120Effects and never enter _live. The cue is never swallowed. A profile without
    // Mark308Prefab never enters the table (old behaviour).
    public sealed partial class Vfx120SpellPresenter308
    {
        sealed class MarkSeal
        {
            public GameObject Prefab;
            public float Life, Born, Until;
            public Vfx308ZoneBody Body;
        }

        const float MarkRemember = 5f, MarkMinHeight = .5f, MarkMaxHeight = 2.5f, MarkMinScale = .4f, MarkMaxScale = 1.5f;

        readonly Dictionary<int, MarkSeal> _marks = new Dictionary<int, MarkSeal>();
        readonly List<int> _staleMarks = new List<int>();

        void NoteMark308(int id, Live live)
        {
            var profile = live.Fx != null ? live.Fx.Profile : null;
            if (profile == null) return;
            var request = live.Request;
            var source = live.ProfileSource != null ? live.ProfileSource : profile;
            if (source.Mark308Prefab == null || request.Role != SpellFxRole.Mark) return;
            float now = Time.time, life = Mathf.Max(0f, request.Duration - request.ImpactClock);
            if (life <= 0f) return;
            _staleMarks.Clear();
            foreach (var pair in _marks) if (pair.Value.Until < now) _staleMarks.Add(pair.Key);
            foreach (int stale in _staleMarks) { LetGo(_marks[stale], false); _marks.Remove(stale); }
            _marks[id] = new MarkSeal { Prefab = source.Mark308Prefab, Life = life, Born = now + request.ImpactClock, Until = now + request.Duration + MarkRemember };
        }

        // never swallows the cue
        void Mark308(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!_marks.TryGetValue(id, out var seal)) return;
            switch (cue)
            {
                case SpellFxCue.Anchor: PutMark308(seal, args.Target); break;
                case SpellFxCue.Detonate: LetGo(seal, true); break;
                case SpellFxCue.Expire:
                case SpellFxCue.TargetDefeated:
                case SpellFxCue.Miss: LetGo(seal, false); break;
            }
        }

        void PutMark308(MarkSeal seal, Transform target)
        {
            if (seal.Prefab == null || target == null || seal.Body != null) return;
            // the mark's life runs from the landing; an anchor that comes late gets what is left
            float life = seal.Life - Mathf.Max(0f, Time.time - seal.Born);
            if (life <= 0f) return;
            Vector3 centre = target.position + Vector3.up * 1f; float radius = .8f;
            bool found = false; var bounds = new Bounds();
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
            }
            if (found)
            {
                float height = Mathf.Clamp(bounds.center.y - target.position.y, MarkMinHeight, MarkMaxHeight);
                centre = new Vector3(bounds.center.x, target.position.y + height, bounds.center.z);
                radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            }
            var eye = Camera.main;
            if (eye != null && (centre - eye.transform.position).sqrMagnitude < EyeClearMeters * EyeClearMeters) return;
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(seal.Prefab, centre, Quaternion.identity);
                instance.name = "Spell308_Mark_" + seal.Prefab.name;
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                instance.transform.localScale = seal.Prefab.transform.localScale * Mathf.Clamp(radius, MarkMinScale, MarkMaxScale);
                // follows the enemy; world scale kept whatever the enemy's own scale is
                instance.transform.SetParent(target, true);
                var body = instance.GetComponent<Vfx308ZoneBody>();
                if (body == null) { Destroy(instance); return; }
                body.Delay = 0f; body.Life = life;
                seal.Body = body;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                if (instance != null) Destroy(instance);
            }
        }

        // the seal leaves the enemy (stays where it was) and dries, or breaks first
        void LetGo(MarkSeal seal, bool splash)
        {
            var body = seal.Body;
            seal.Body = null;
            if (body == null) return;
            var go = body.gameObject;
            if (go.transform.parent != null)
            {
                go.transform.SetParent(null, true);
                if (_owner != null && go.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(go, _owner.gameObject.scene);
            }
            if (splash) body.Splash(); else body.Dry();
        }

        void ForgetMark308(int id)
        {
            if (!_marks.TryGetValue(id, out var seal)) return;
            LetGo(seal, false);
            _marks.Remove(id);
        }

        void ClearMark308()
        {
            foreach (var seal in _marks.Values) if (seal.Body != null) seal.Body.Kill();
            _marks.Clear();
        }
    }
}
