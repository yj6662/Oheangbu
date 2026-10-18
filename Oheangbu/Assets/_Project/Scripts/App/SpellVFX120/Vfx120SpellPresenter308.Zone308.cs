using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.57: the floor of the lingering zones (곡 곳 녹 농 몽). The area body is still the cast
    // cue cut to 3 s; this puts down a Vfx308ZoneBody that keeps the zone's own clock (start at the impact, faint after
    // 3 s, dry at the rule's end). Role Zone: one body at the zone centre on Begin. Role Cast (녹): one body per Secondary
    // (a heal zone opened where a hit landed). 11.59: Role Ward (막's rooted rock) puts one body like Zone, at the
    // landing with the cover radius, for the rock's life. The bodies dry by themselves; Expire / Break / End only bring that forward.
    // Side table only - the bodies are not Vfx120Effects and never enter _live. The cue is never swallowed. A profile
    // without Zone308Prefab never enters the table (old behaviour).
    public sealed partial class Vfx120SpellPresenter308
    {
        sealed class ZoneFloor
        {
            public GameObject Prefab;
            public float Life, Until;
            public readonly List<Vfx308ZoneBody> Bodies = new List<Vfx308ZoneBody>();
        }

        const float ZoneRemember = 5f, ZoneMinRadius = .25f;

        readonly Dictionary<int, ZoneFloor> _zones = new Dictionary<int, ZoneFloor>();
        readonly List<int> _staleZones = new List<int>();

        void NoteZone308(int id, Live live)
        {
            var profile = live.Fx != null ? live.Fx.Profile : null;
            if (profile == null) return;
            var request = live.Request;
            // the private 3 s copy keeps the source's fields; read the source when there is one
            var source = live.ProfileSource != null ? live.ProfileSource : profile;
            if (source.Zone308Prefab == null || (request.Role != SpellFxRole.Zone && request.Role != SpellFxRole.Cast && request.Role != SpellFxRole.Ward)) return;
            float now = Time.time, life = Mathf.Max(0f, request.Duration - request.ImpactClock);
            if (life <= 0f) return;
            _staleZones.Clear();
            foreach (var pair in _zones) if (pair.Value.Until < now) _staleZones.Add(pair.Key);
            foreach (int stale in _staleZones) { DryAll(_zones[stale]); _zones.Remove(stale); }
            var floor = new ZoneFloor { Prefab = source.Zone308Prefab, Life = life, Until = now + request.Duration + ZoneRemember };
            _zones[id] = floor;
            if (request.Role == SpellFxRole.Zone || request.Role == SpellFxRole.Ward) PutZone308(floor, request.FallbackPoint, request.Radius, request.ImpactClock, request.Letter);
        }

        // never swallows the cue
        void Zone308(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!_zones.TryGetValue(id, out var floor)) return;
            switch (cue)
            {
                case SpellFxCue.Secondary: PutZone308(floor, args.Point, args.Value, Mathf.Max(0f, args.At - Time.time), '\0'); break;
                case SpellFxCue.Expire: DryAll(floor); break;
                case SpellFxCue.Break:
                    foreach (var body in floor.Bodies) if (body != null) body.Splash();
                    break;
            }
        }

        void PutZone308(ZoneFloor floor, Vector3 point, float radius, float delay, char letter)
        {
            if (floor.Prefab == null || radius < ZoneMinRadius) return;
            var eye = Camera.main;
            if (eye != null && (point - eye.transform.position).sqrMagnitude < EyeClearMeters * EyeClearMeters) return;
            GameObject instance = null;
            try
            {
                instance = Object.Instantiate(floor.Prefab, point, Quaternion.identity);
                instance.name = "Spell308_Zone_" + (letter != '\0' ? letter.ToString() : floor.Prefab.name);
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                instance.transform.localScale = floor.Prefab.transform.localScale * radius;
                var body = instance.GetComponent<Vfx308ZoneBody>();
                if (body == null) { Destroy(instance); return; }
                body.Delay = delay; body.Life = floor.Life;
                floor.Bodies.RemoveAll(b => b == null);
                floor.Bodies.Add(body);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                if (instance != null) Destroy(instance);
            }
        }

        static void DryAll(ZoneFloor floor)
        {
            foreach (var body in floor.Bodies) if (body != null) body.Dry();
        }

        // replaced / dropped / refused: the floor dries (one that has not started yet just goes)
        void ForgetZone308(int id)
        {
            if (!_zones.TryGetValue(id, out var floor)) return;
            DryAll(floor);
            _zones.Remove(id);
        }

        void ClearZone308()
        {
            foreach (var floor in _zones.Values)
                foreach (var body in floor.Bodies) if (body != null) body.Kill();
            _zones.Clear();
        }
    }
}
