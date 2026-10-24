using System;
using System.Collections.Generic;
using Oheangbu.Combat;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>What a posture call answered (the ㅏ+ㅁ active seed: posture 60). Plain values, so the executor does not hold a combat type.</summary>
    public readonly struct Vol2SeedPosture
    {
        public readonly bool Called, Accepted;
        public readonly string Reason;
        public readonly decimal Added, After;
        public readonly long WindowUs;
        public Vol2SeedPosture(bool called, bool accepted, string reason, decimal added, decimal after, long windowUs)
        { Called = called; Accepted = accepted; Reason = reason ?? ""; Added = added; After = after; WindowUs = windowUs; }
        public static Vol2SeedPosture None => default;
        public override string ToString() => !Called ? "no posture port" : (Accepted ? "" : "REFUSED ") + Reason + " +" + Added.ToString(System.Globalization.CultureInfo.InvariantCulture) + " -> " + After.ToString(System.Globalization.CultureInfo.InvariantCulture) + (WindowUs > 0 ? " window " + WindowUs + "us" : "");
    }

    /// <summary>One structured fact of a seed cast (the lab and the fixture read these; the cast log's Notes get the same in words).
    /// Kind: Plant | PlantRefused | Activate | ActivateRefused | Expired | HostGone | Posture | Exempt | Press | Collapse | Ground | NoTarget | Replaced | Pause.</summary>
    public sealed class Vol2SeedRecord
    {
        public string Kind = "", Trigger = "", Detail = "", TargetName = "";
        public long AtUs, TargetId, MaterialId;
        public decimal Value;
        public Vol2SeedPosture Posture;
        public override string ToString() => Kind + (Trigger.Length > 0 ? " [" + Trigger + "]" : "") + (TargetName.Length > 0 ? " " + TargetName : "") + (Detail.Length > 0 ? ": " + Detail : "");
    }

    /// <summary>A seed run, as the hub sees it.</summary>
    internal interface IVol2SeedListener
    {
        CastId CastOf { get; }
        void OnOwnerHit(long targetId, long lifeRevision, long swingId, long nowUs);
        void OnGatherContact(long castId, long materialId, long stableTargetId, long swingId, long nowUs);
        void OnPlayerHit(Vol2PlayerHit hit, Vol2SeedHitGate gate, long nowUs);
        void CollectCarries(List<NoInkSwingV3.Carry> into, long ownerId);
    }

    /// <summary>The per-scope state of the 심는다 executor and its ports to the rest of the game (S5-b slice 5). The executor itself stays stateless
    /// (a run per cast); everything that spans casts lives here: who is waiting for the owner's next hit, the 갈무리 carry, the player-hit hook, the
    /// 信 signal, the facts. One hub per cast coordinator (<see cref="Vol2SeedExecutor.HubOf"/>); nothing static.
    /// PORTS (all optional; an unset port makes the trigger it serves inert, never an error):
    ///   Drawing / DrawingSimulated  - 信: is a letter being drawn now (production: the drawing input's HasStartedStroke; the lab simulates).
    ///   ActiveSeedPosture           - the ㅏ+ㅁ active seed: posture 60 on the struck body (production: the defence kit's posture bridge).
    ///   GatherTargetMatches         - maps a 갈무리 contact's stable target id to the executor's target (production: the kit's target book).
    ///   AttachGather(kit.Gather)    - the 갈무리 swing: the hub chains the gather service's CarryProvider with the cast's un-triggered path seeds and
    ///                                 listens to Gathered for the Activate contacts.
    ///   AttachHits(gate / vitals)   - the player's real hits (ㅓ+ㅁ) and the stagger exemption of 멈.
    ///   CasterPositionOverride      - the caster's position for the 뭄 band and the ㅓ+ㅁ radius (lab / fixture; production reads Host.Caster).
    /// <see cref="Vol2SeedWiring.Bind"/> sets the production ones in one call.</summary>
    public sealed class Vol2SeedHub : IDisposable
    {
        readonly Vol2CastCoordinator _host;
        readonly List<IVol2SeedListener> _runs = new List<IVol2SeedListener>();
        readonly Dictionary<long, List<Vol2SeedRecord>> _records = new Dictionary<long, List<Vol2SeedRecord>>();
        readonly Dictionary<long, long> _paused = new Dictionary<long, long>();
        readonly Queue<long> _order = new Queue<long>();
        Vol2SeedHitGate _hits;
        Vol2GatherService _gather;
        Func<IEnumerable<NoInkSwingV3.Carry>> _previousCarry;
        bool _disposed;

        public Vol2CastCoordinator Host => _host;
        public Vol2SeedTuning Tuning { get; } = new Vol2SeedTuning();
        public Func<bool> Drawing { get; set; }
        public bool DrawingSimulated { get; set; }
        public Func<IVol2Target, char, Vol2SeedPosture> ActiveSeedPosture { get; set; }
        public Func<IVol2Target, long, bool> GatherTargetMatches { get; set; }
        public Func<Vector3> CasterPositionOverride { get; set; }
        public Vol2SeedHitGate Hits => _hits;
        public Vol2GatherService Gather => _gather;
        public int RunsListening => _runs.Count;
        public long OwnerHitsSeen { get; private set; }
        public long GatherContactsSeen { get; private set; }
        public long PlayerHitsSeen { get; private set; }

        public bool IsDrawing => DrawingSimulated || (Drawing != null && SafeDrawing());
        public Vector3 CasterPosition => CasterPositionOverride != null ? CasterPositionOverride() : _host.Caster.Position;

        public Vol2SeedHub(Vol2CastCoordinator host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _host.ContactResolved += OnContact;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _host.ContactResolved -= OnContact;
            DetachGather(); DetachHits();
            _runs.Clear();
        }

        bool SafeDrawing() { try { return Drawing(); } catch (Exception e) { Debug.LogException(e); return false; } }

        // ---- the runs ----

        internal void Register(IVol2SeedListener run) { if (!_runs.Contains(run)) _runs.Add(run); }
        internal void Unregister(IVol2SeedListener run) { _runs.Remove(run); }
        List<IVol2SeedListener> Snapshot() => new List<IVol2SeedListener>(_runs);

        // ---- facts ----

        internal Vol2SeedRecord Record(CastId cast, Vol2SeedRecord r)
        {
            if (!_records.TryGetValue(cast.Value, out var list)) { list = new List<Vol2SeedRecord>(); _records[cast.Value] = list; _order.Enqueue(cast.Value); while (_order.Count > 256) { long old = _order.Dequeue(); _records.Remove(old); _paused.Remove(old); } }
            list.Add(r);
            return r;
        }

        public IReadOnlyList<Vol2SeedRecord> RecordsOf(CastId cast) => _records.TryGetValue(cast.Value, out var l) ? (IReadOnlyList<Vol2SeedRecord>)l : new List<Vol2SeedRecord>();
        internal void SetPaused(CastId cast, long us) { _paused[cast.Value] = us; }
        /// <summary>How long 信 has held this cast's waiting seeds (microseconds, at most the tuning cap).</summary>
        public long PausedUsOf(CastId cast) => _paused.TryGetValue(cast.Value, out long us) ? us : 0;

        internal Vol2SeedPosture CallPosture(IVol2Target target, char letter)
        {
            var f = ActiveSeedPosture;
            if (f == null) return Vol2SeedPosture.None;
            try { return f(target, letter); } catch (Exception e) { Debug.LogException(e); return Vol2SeedPosture.None; }
        }

        internal bool GatherMatches(IVol2Target seedTarget, long stableId)
        {
            var f = GatherTargetMatches;
            if (f == null || seedTarget == null) return false;
            try { return f(seedTarget, stableId); } catch (Exception e) { Debug.LogException(e); return false; }
        }

        // ---- the owner's later direct hit: any OTHER cast's paid first / second contact on a body ----

        void OnContact(Vol2CastLog log, Vol2ContactRecord rec)
        {
            if (_disposed || !rec.BudgetAccepted || (rec.Kind != "Instant" && rec.Kind != "Second")) return;
            OwnerHitsSeen++;
            long now = _host.Services.Clock.NowUs;
            foreach (var run in Snapshot())
            {
                if (run.CastOf == log.Cast) continue;
                try { run.OnOwnerHit(rec.TargetId, rec.LifeRevision, rec.SwingId, now); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        // ---- 갈무리: the kit's gather service ----

        /// <summary>Hooks the 갈무리 service: its CarryProvider is chained (what was there stays) and its Gathered contacts reach the seeds.</summary>
        public void AttachGather(Vol2GatherService gather)
        {
            if (gather == null) throw new ArgumentNullException(nameof(gather));
            if (ReferenceEquals(_gather, gather)) return;
            DetachGather();
            _gather = gather;
            _previousCarry = gather.CarryProvider;
            var previous = _previousCarry;
            gather.CarryProvider = () =>
            {
                var all = new List<NoInkSwingV3.Carry>();
                if (previous != null) { var p = previous(); if (p != null) all.AddRange(p); }
                foreach (var run in Snapshot()) run.CollectCarries(all, gather.OwnerId);
                return all;
            };
            gather.Gathered += OnGathered;
        }

        public void DetachGather()
        {
            if (_gather == null) return;
            _gather.Gathered -= OnGathered;
            _gather.CarryProvider = _previousCarry;
            _gather = null; _previousCarry = null;
        }

        void OnGathered(Vol2GatherRecord record, NoInkSwingV3.Step step)
        {
            if (_disposed || step == null || step.Contacts.Count == 0) return;
            long now = _host.Services.Clock.NowUs;
            foreach (var c in step.Contacts)
            {
                if (c.Kind != ContactKind.Activate) continue;
                GatherContactsSeen++;
                foreach (var run in Snapshot())
                {
                    try { run.OnGatherContact(c.CastId, c.MaterialId, c.TargetId, c.SwingId, now); } catch (Exception e) { Debug.LogException(e); }
                }
            }
        }

        /// <summary>Test / fixture entry: the same thing a Gathered contact does (the real path is <see cref="AttachGather"/>).</summary>
        public void NotifyGatherContact(long castId, long materialId, long stableTargetId, long swingId)
        {
            long now = _host.Services.Clock.NowUs;
            GatherContactsSeen++;
            foreach (var run in Snapshot()) run.OnGatherContact(castId, materialId, stableTargetId, swingId, now);
        }

        // ---- the player's real hits (ㅓ+ㅁ) ----

        public void AttachHits(Vol2SeedHitGate gate)
        {
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            if (ReferenceEquals(_hits, gate)) return;
            DetachHits();
            _hits = gate;
            _hits.Confirmed += OnPlayerHit;
        }

        /// <summary>Makes (or reuses) the scope's hit gate and attaches it to the player.</summary>
        public Vol2SeedHitGate AttachHits(PlayerVitals vitals)
        {
            if (vitals == null) throw new ArgumentNullException(nameof(vitals));
            var gate = _hits ?? new Vol2SeedHitGate(_host.Services.Clock);
            gate.Attach(vitals);
            AttachHits(gate);
            return gate;
        }

        public void DetachHits()
        {
            if (_hits == null) return;
            _hits.Confirmed -= OnPlayerHit;
            _hits = null;
        }

        void OnPlayerHit(Vol2PlayerHit hit)
        {
            if (_disposed) return;
            PlayerHitsSeen++;
            long now = _host.Services.Clock.NowUs;
            foreach (var run in Snapshot())
            {
                try { run.OnPlayerHit(hit, _hits, now); } catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>The production binding of the 심는다 ports, in one call: the kit gives posture, 갈무리 and the stable target ids; the player gives
    /// the real hits; the drawing state comes from whoever knows it. Idempotent. Slice 5's open item: the main agent places ONE call to this where
    /// the defence kit is bound to the coordinator (Vol2DefenceInstaller's build callback has both).</summary>
    public static class Vol2SeedWiring
    {
        public static Vol2SeedHub Bind(Vol2CastCoordinator coordinator, Vol2DefenceKit kit, PlayerVitals player, Func<bool> drawing = null)
        {
            var hub = Vol2SeedExecutor.HubOf(coordinator);
            if (hub == null) return null;
            if (kit != null)
            {
                hub.ActiveSeedPosture = (target, letter) =>
                {
                    var enemy = target as Vol2EnemyTarget;
                    if (enemy == null || enemy.Vitals == null) return Vol2SeedPosture.None;
                    var r = kit.Posture.ApplyActiveSeed(enemy.Vitals, letter, true, true);
                    return new Vol2SeedPosture(true, r.Accepted, r.Reason, r.Added, r.PostureAfter, r.WindowUs);
                };
                hub.GatherTargetMatches = (target, stableId) => target is Vol2EnemyTarget e && e.Vitals != null && kit.Targets.StableId(e.Vitals) == stableId;
                hub.AttachGather(kit.Gather);
            }
            if (player != null) hub.AttachHits(player);
            if (drawing != null) hub.Drawing = drawing;
            return hub;
        }

        /// <summary>The fallback binding, tried by every seed cast until it succeeds: in Play, when the scope's wiring has its defence kit, the kit, the
        /// player and the drawing input are taken from the scene (one lookup per cast while unbound, none once bound). Edit mode and a hub that was bound
        /// by hand are left alone. An explicit <see cref="Bind"/> from the installer is the better place and makes this a no-op.</summary>
        public static bool EnsureBound(Vol2SeedHub hub)
        {
            if (hub == null) return false;
            if (hub.Gather != null) return true;
            if (!Application.isPlaying) return false;
            var wiring = UnityEngine.Object.FindFirstObjectByType<CombatLoopWiring>();
            var kit = wiring != null ? wiring.Vol2Defence : null;
            if (kit == null) return false;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerVitals>();
            var input = UnityEngine.Object.FindFirstObjectByType<Oheangbu.Drawing.DrawingInputController>();
            Func<bool> drawing = input != null ? (Func<bool>)(() => input != null && input.InDrawMode) : null;
            Bind(hub.Host, kit, player, hub.Drawing == null ? drawing : null);
            return hub.Gather != null;
        }
    }
}
