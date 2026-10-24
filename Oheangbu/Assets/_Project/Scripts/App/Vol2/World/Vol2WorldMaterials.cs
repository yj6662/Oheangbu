using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    public enum Vol2WorldShape { Disc, Fan, Ring }

    /// <summary>One pre-placed field material: a patch of vines, a pool, a bed of embers the level designer put in the world (LDB places them; this slice only builds the door for them). It is a
    /// real material of the registry - owned by the WORLD (faction 3), balance and life like any other - so the reaction rules read it like a cast's: fire burns a vine patch, water floods an ember
    /// bed. A vine patch can be a gate door (<see cref="GateId"/>): when fire consumes it the field port is told (the same report 눈 makes) and the SpellFieldGate308 of that id opens.</summary>
    [Serializable]
    public sealed class Vol2WorldMaterialSpec
    {
        public string Id = "";
        public Body Body = Body.Wood;
        public Vol2WorldShape Shape = Vol2WorldShape.Disc;
        public Vector3 Position;
        public Vector3 Forward = Vector3.forward;
        public float Radius = 0.8f;
        public float FanDepth = 4f, FanHalfAngleDeg = 30f;
        public float RingInner = 1.7f, RingOuter = 2.3f;
        public float Amount = 0.9f;
        /// <summary>0 = the tuning's WorldLifeSeconds.</summary>
        public float LifeSeconds;
        /// <summary>The id of the SpellFieldGate308 this vine patch is the door of ("" = not a door).</summary>
        public string GateId = "";
    }

    /// <summary>The world's materials of one scope (S5-b slice 8). Places specs into the registry; nothing static; Vol2 scopes only (the runtime creates it).</summary>
    public sealed class Vol2WorldMaterials
    {
        readonly Vol2MaterialRegistry _registry;
        readonly IVol2Clock _clock;
        readonly Vol2ReactionTuning _tuning;
        readonly List<Vol2Material> _placed = new List<Vol2Material>();
        readonly Dictionary<string, Vol2Material> _byId = new Dictionary<string, Vol2Material>();
        long _next;

        public IReadOnlyList<Vol2Material> Placed => _placed;
        public Vol2WorldMaterials(Vol2MaterialRegistry registry, IVol2Clock clock, Vol2ReactionTuning tuning = null)
        { _registry = registry ?? throw new ArgumentNullException(nameof(registry)); _clock = clock ?? throw new ArgumentNullException(nameof(clock)); _tuning = tuning ?? new Vol2ReactionTuning(); }

        public bool TryGet(string id, out Vol2Material m) => _byId.TryGetValue(id ?? "", out m) && m.Alive;

        public Vol2Footprint FootprintOf(Vol2WorldMaterialSpec s)
        {
            switch (s.Shape)
            {
                case Vol2WorldShape.Fan: return Vol2Footprint.FanAt(s.Position, s.Forward, s.FanDepth, s.FanHalfAngleDeg * Mathf.Deg2Rad);
                case Vol2WorldShape.Ring: return Vol2Footprint.RingAt(s.Position, s.RingInner, s.RingOuter);
                default: return Vol2Footprint.Disc(s.Position, s.Radius);
            }
        }

        /// <summary>Puts the material into the world. The CastId is the world's own (from WorldCastBase up: no cast of the issuer, no lab plant), so the rules never see it as the same cast as anything.</summary>
        public Vol2Material Place(Vol2WorldMaterialSpec s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var cast = new CastId(_tuning.WorldCastBase + (++_next));
            float life = s.LifeSeconds > 0f ? s.LifeSeconds : _tuning.WorldLifeSeconds;
            long lifeUs = Math.Max(1L, Vol2Time.FromSeconds(life));
            var m = _registry.Create(cast, '\0', s.Body, Vol2MaterialKind.Residue, (decimal)s.Amount, lifeUs, FootprintOf(s), Vol2Faction.World, Vol2Faction.World, false, _clock.NowUs, 0, 0,
                string.IsNullOrEmpty(s.GateId) ? (s.Id ?? "") : s.GateId, !string.IsNullOrEmpty(s.GateId), null, "world");
            _placed.Add(m);
            if (!string.IsNullOrEmpty(s.Id)) _byId[s.Id] = m;
            return m;
        }

        public int PlaceAll(IEnumerable<Vol2WorldMaterialSpec> specs) { int n = 0; foreach (var s in specs) { Place(s); n++; } return n; }

        /// <summary>Scope reset: the world's materials end Aborted (they are not part of any save).</summary>
        public void EndAll()
        {
            long now = _clock.NowUs;
            foreach (var m in _placed) if (m.Alive) _registry.TryEnd(m.Id, Vol2MaterialEnd.Aborted, now);
            _placed.Clear(); _byId.Clear();
        }
    }

    /// <summary>Authoring: put this on an object in a level to place a world material there when a Vol2 scope runs. Inert without a Vol2 runtime (nothing reads it in Legacy308 - the binder
    /// exists only in Vol2 scopes). A vine door: also give the object (or its parent) a SpellFieldGate308 of kind Vine and put its id in <see cref="GateId"/>.</summary>
    [DisallowMultipleComponent]
    public sealed class Vol2WorldMaterialMarker : MonoBehaviour
    {
        public Body Body = Body.Wood;
        public Vol2WorldShape Shape = Vol2WorldShape.Disc;
        [Min(0.1f)] public float Radius = 0.8f;
        public float FanDepth = 4f, FanHalfAngleDeg = 30f, RingInner = 1.7f, RingOuter = 2.3f;
        [Min(0.05f)] public float Amount = 0.9f;
        public float LifeSeconds;
        public string GateId = "";

        /// <summary>The runtime fills this when it has placed the material.</summary>
        [NonSerialized] public Vol2Material Placed;

        public Vol2WorldMaterialSpec ToSpec() => new Vol2WorldMaterialSpec
        {
            Id = name, Body = Body, Shape = Shape, Position = transform.position, Forward = transform.forward, Radius = Radius, FanDepth = FanDepth, FanHalfAngleDeg = FanHalfAngleDeg,
            RingInner = RingInner, RingOuter = RingOuter, Amount = Amount, LifeSeconds = LifeSeconds, GateId = GateId
        };
    }

    /// <summary>Placed by the Vol2 shape runtime: looks for markers in the loaded scenes once a second and places the ones not placed yet (streamed-in areas bring theirs).</summary>
    public sealed class Vol2WorldBinder : MonoBehaviour
    {
        Vol2WorldMaterials _world;
        float _next;
        public int Scans { get; private set; }
        public int Placed { get; private set; }
        public void Bind(Vol2WorldMaterials world) { _world = world; Scan(); }

        void Update()
        {
            if (_world == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Scan();
        }

        public void Scan()
        {
            if (_world == null) return;
            Scans++;
            foreach (var marker in FindObjectsByType<Vol2WorldMaterialMarker>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (marker == null || (marker.Placed != null && marker.Placed.Alive)) continue;
                if (marker.Placed != null) continue;                         // placed once; if it ended (burnt, cleansed, aborted) it is gone for this scope
                marker.Placed = _world.Place(marker.ToSpec()); Placed++;
            }
        }
    }

    /// <summary>둘레 1 (TE-S8-10, D308-121 "one standing domain", E5 "둘레 1개 규칙"): the player holds ONE standing ring at a time. A new ring-footprint material of another cast of mine ends the old one
    /// <see cref="Vol2MaterialEnd.Replaced"/>. Off unless the reaction engine is on.</summary>
    public sealed class Vol2RingSlot : IDisposable
    {
        readonly Vol2MaterialRegistry _registry;
        readonly IVol2Clock _clock;
        readonly Func<bool> _on;
        public int Replaced { get; private set; }
        public event Action<Vol2Material, Vol2Material> Replacing;

        public Vol2RingSlot(Vol2MaterialRegistry registry, IVol2Clock clock, Func<bool> on)
        { _registry = registry; _clock = clock; _on = on; _registry.Created += OnCreated; }

        public void Dispose() { _registry.Created -= OnCreated; }

        static bool IsMyRing(Vol2Material m)
            => m.Footprint != null && m.Footprint.Kind == Vol2FootprintKind.Ring && m.FactionId == Vol2Faction.Player && m.Origin == "cast" && m.Kind != Vol2MaterialKind.Carrier;

        void OnCreated(Vol2Material m)
        {
            if (_on == null || !_on() || !IsMyRing(m)) return;
            long now = _clock.NowUs;
            foreach (var o in _registry.AliveSnapshot())
            {
                if (o == m || !o.Alive || o.Cast == m.Cast || !IsMyRing(o)) continue;
                if (_registry.TryEnd(o.Id, Vol2MaterialEnd.Replaced, now)) { Replaced++; Replacing?.Invoke(o, m); }
            }
        }
    }

    /// <summary>뭄's enemy dash / leap block (closes the seam of slice 5): while an enemy stands on a live hardened ground (<see cref="Vol2SeedWorld.BlocksDashOrJump"/>) it carries an
    /// EnemyDomainState308 "dash x0" entry, refreshed every tick and lasting BlockHoldSeconds past the last one. The enemy movement code already asks that state before a charge starts
    /// (EnemyController.ChargeDashes308), so no change to the enemy is needed. TE-S8-13.</summary>
    public sealed class Vol2MovementBlock
    {
        /// <summary>The owner key of the entries: apart from the status sink's (0x56...) and the old domains' keys.</summary>
        public const long OwnerKey = 0x5800000000L;
        readonly Vol2MaterialRegistry _materials;
        readonly Vol2ReactionTuning _tuning;
        public int Applied { get; private set; }
        public Vol2MovementBlock(Vol2MaterialRegistry materials, Vol2ReactionTuning tuning = null) { _materials = materials; _tuning = tuning ?? new Vol2ReactionTuning(); }

        public void Tick(long nowUs, IReadOnlyList<IVol2Target> targets, float timeNow)
        {
            if (targets == null) return;
            bool any = false;
            foreach (var m in _materials.Alive) if (m.Tag == Vol2SeedExecutor.TagGround) { any = true; break; }
            if (!any) return;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = targets[i] as Vol2EnemyTarget;
                if (e == null || e.Vitals == null || !e.Alive) continue;
                var domain = e.Vitals.Modifiers != null ? e.Vitals.Modifiers.Domain : null;
                if (domain == null) continue;
                if (Vol2SeedWorld.BlocksDashOrJump(_materials, e.Position, nowUs, e.Radius) && domain.Dash(OwnerKey, timeNow + _tuning.BlockHoldSeconds, 0f)) Applied++;
            }
        }
    }
}
