using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.App
{
    // path.purge (SPEC-SPELL-120-308 WP-13, opened by D308-13 Q5): the base glyph's wave, which also wipes enemy hazards
    // and remnants as it runs.
    //  - first hit = the host's path judgement, unchanged, presented by the stroke adapter like the base glyph;
    //  - while the wave front runs its corridor, every hazard (ISpellPurgeable308, found through its collider in the
    //    caster's own physics scene) whose circle the front has reached is wiped (PurgeRule308). A hazard laid on the
    //    path while the wave is still running is wiped too when the front gets there; one behind the front is not.
    //    Physics is asked only for the strip of the corridor the front crossed since the last frame (one half width
    //    of room on either side of it), never for the whole corridor every frame.
    // Only enemy hazards answer to the seam: the player's own zones are not touched.
    public sealed class PurgeEffect308 : ISpellEffect
    {
        public const string HandlerId = "path.purge";
        static readonly SpellParamSpec[] Specs = Array.Empty<SpellParamSpec>();

        struct Wave
        {
            public PathFront308 Front; public long Owner;
            public float Washed;        // how far along the corridor axis the strips asked so far reach
            public bool Began;
        }
        readonly List<Wave> _waves = new List<Wave>();
        readonly List<ISpellPurgeable308> _found = new List<ISpellPurgeable308>();
        Collider[] _overlaps = new Collider[SpellHazardTestTarget308.QueryCapacity];

        public string Id => HandlerId;
        public IReadOnlyList<SpellParamSpec> Params => Specs;
        public int WavesRunning => _waves.Count;
        public int Wiped { get; private set; }             // hazards wiped so far (checks read this)

        public bool Prepare(in SpellCastContext ctx) => ctx.Host != null && ctx.Row != null;

        public bool Commit(in SpellCastContext ctx)
        {
            var host = ctx.Host;
            var plan = host.PlanPath(ctx.Cast, true);
            if (plan == null) return false;
            var area = plan.Area;
            if (area == null) return true;
            var front = new PathFront308(area.Point, area.Direction, area.Radius, area.Length, area.Speed, ctx.Now + area.Delay, false);
            if (!front.Valid) return true;
            Object caster = host.PlayerVitals != null ? (Object)host.PlayerVitals : host.Player;
            _waves.Add(new Wave { Front = front, Owner = AttackProvenance.Create(caster, DamageSource.PersistentSpell, ctx.Cast.Element).AttackId });
            return true;
        }

        public void Tick(in SpellTickContext ctx)
        {
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var wave = _waves[i];
                Wash(ctx.Host, ref wave, ctx.Now);
                if (wave.Front.Finished(ctx.Now)) _waves.RemoveAt(i);
                else _waves[i] = wave;
            }
        }

        // Death, rest or scene leave: the waves are dropped; what they already wiped stays wiped.
        public void Clear(SpellClearReason reason) { _waves.Clear(); _found.Clear(); }

        void Wash(ISpellCastHost host, ref Wave wave, float now)
        {
            var front = wave.Front;
            if (host == null || host.Player == null || !front.Started(now)) return;
            var scene = host.Player.gameObject.scene;
            PhysicsScene physics = scene.IsValid() ? scene.GetPhysicsScene() : Physics.defaultPhysicsScene;
            if (!physics.IsValid()) return;
            // The strip of the corridor the front crossed since the last look, with half a corridor width of room before and
            // after it (a hazard is found by its collider a little before the front reaches it). The rule decides what is
            // really on the path and whether the front has reached it. The box is as tall as the old corridor sphere was.
            float room = front.HalfWidth, reached = front.Travelled(now);
            float from = wave.Began ? wave.Washed : 0f;
            wave.Began = true; wave.Washed = reached;
            PurgeRule308.Strip(front, from, reached, room, out float near, out float far);
            Vector3 middle = front.Start + front.Direction * ((near + far) * .5f);
            var half = new Vector3(front.HalfWidth + room, front.Length * .5f + front.HalfWidth, (far - near) * .5f);
            Quaternion facing = Quaternion.LookRotation(front.Direction, Vector3.up);
            int count = physics.OverlapBox(middle, half, _overlaps, facing, ~0, QueryTriggerInteraction.Collide);
            while (count >= _overlaps.Length)
            {
                Array.Resize(ref _overlaps, _overlaps.Length * 2);
                count = physics.OverlapBox(middle, half, _overlaps, facing, ~0, QueryTriggerInteraction.Collide);
            }
            _found.Clear();
            for (int i = 0; i < count; i++)
            {
                var hazard = _overlaps[i] != null ? _overlaps[i].GetComponentInParent<ISpellPurgeable308>() : null;
                if (hazard != null && !_found.Contains(hazard)) _found.Add(hazard);
            }
            Array.Clear(_overlaps, 0, count);
            for (int i = 0; i < _found.Count; i++)
            {
                var hazard = _found[i];
                var snap = new SpellHazardSnap(i, hazard.Position, hazard.Radius, hazard.Purged);
                if (PurgeRule308.Reached(front, snap, now) && hazard.Purge(wave.Owner)) Wiped++;
            }
            _found.Clear();
        }
    }
}
