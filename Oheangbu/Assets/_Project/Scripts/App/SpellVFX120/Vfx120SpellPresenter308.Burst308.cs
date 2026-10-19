using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.58: the intercept letters (상 needle, 송 volley). Where an enemy projectile was shot
    // down, one ink burst freezes and cracks (Burst308Prefab, one shot, about 1 s). 상 begins a presentation and gets an
    // Intercept cue with the point; 송's volley begins none, so the barrage calls Burst(letter, point) through
    // ISpellBurstPresenter308. A profile without Burst308Prefab never draws (old behaviour). The cue is never swallowed.
    // Presentation only: nothing is read back by a rule.
    public sealed partial class Vfx120SpellPresenter308 : ISpellBurstPresenter308
    {
        // how long a handle is remembered (the intercept effect never Ends its handle before its cue)
        const float BurstRemember = 30f;

        readonly Dictionary<int, BurstNote> _bursts = new Dictionary<int, BurstNote>();
        readonly List<int> _staleBursts = new List<int>();

        struct BurstNote { public GameObject Prefab; public char Letter; public float Until; }

        void NoteBurst308(int id, Live live)
        {
            var profile = live.Fx != null ? live.Fx.Profile : null;
            if (profile == null || profile.Burst308Prefab == null || live.Request.Role != SpellFxRole.Projectile) return;
            float now = Time.time;
            _staleBursts.Clear();
            foreach (var pair in _bursts) if (pair.Value.Until < now) _staleBursts.Add(pair.Key);
            foreach (int stale in _staleBursts) _bursts.Remove(stale);
            _bursts[id] = new BurstNote { Prefab = profile.Burst308Prefab, Letter = live.Request.Letter, Until = now + BurstRemember };
        }

        // never swallows the cue; Miss stays silent
        void Burst308(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (cue != SpellFxCue.Intercept || !_bursts.TryGetValue(id, out var note)) return;
            PutBurst308(note.Prefab, note.Letter, args.Point);
        }

        // 송: a volley that began no presentation. The catalogue prefab's profile is read; nothing is instantiated for it.
        public void Burst(char letter, Vector3 point)
        {
            if (_visuals == null || !_visuals.TryGet(letter, out var visual) || visual.FxPrefab == null) return;
            var fx = visual.FxPrefab.GetComponent<Vfx120Effect>();
            var profile = fx != null ? fx.Profile : null;
            if (profile == null || profile.Burst308Prefab == null) return;
            PutBurst308(profile.Burst308Prefab, letter, point);
        }

        void PutBurst308(GameObject prefab, char letter, Vector3 point)
        {
            if (prefab == null) return;
            var eye = Camera.main;
            if (eye != null && (point - eye.transform.position).sqrMagnitude < EyeClearMeters * EyeClearMeters) return;
            Quaternion rotation = Quaternion.identity;
            if (eye != null)
            {
                Vector3 toEye = eye.transform.position - point; toEye.y = 0f;
                if (toEye.sqrMagnitude > 1e-4f) rotation = Quaternion.LookRotation(toEye.normalized, Vector3.up);
            }
            var go = Object.Instantiate(prefab, point, rotation);
            go.name = "Spell308_" + letter + "_Burst";
            if (_owner != null && go.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(go, _owner.gameObject.scene);
            if (!Application.isPlaying) return;
            var residue = go.GetComponent<Vfx308Residue>() ?? go.AddComponent<Vfx308Residue>();
            residue.Life = StrikeLife;
        }

        void ForgetBurst308(int id) => _bursts.Remove(id);

        void ClearBurst308() => _bursts.Clear();
    }
}
