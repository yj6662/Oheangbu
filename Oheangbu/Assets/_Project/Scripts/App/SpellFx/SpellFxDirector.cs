using System;
using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App.SpellFx
{
    /// <summary>D308-153: the scene side of the restart plumbing - ONE per vol.2 scope (installed by Vol2ShapeRuntime.CreateForScope; a failure there never stops a cast).
    /// It listens to the executors (cast begun / ended, contacts, materials, reactions - all on the scope clock) and, for every cast the catalogue covers
    /// (the variant chosen now for the letter has a playable prefab), builds that prefab at the cast point and relays the events to its <see cref="SpellFxPlayer"/>.
    /// A cast the catalogue does not cover, or no catalogue at all, does nothing here: the old looks stay (see <see cref="SpellFxSilencer"/> for the covered ones).
    /// It decides nothing of a rule and holds no timer: lifetimes come from the executors' events and the clock. Not a singleton: nothing static points at it
    /// (the silencer holds only this director's "covers" question while it lives).</summary>
    [DefaultExecutionOrder(210)]
    public sealed class SpellFxDirector : MonoBehaviour
    {
        sealed class Active { public SpellFxCast Cast; public SpellFxPlayer Player; public GameObject Root; }

        Vol2CastCoordinator _c;
        IVol2Clock _clock;
        SpellFxCatalog _catalog;
        bool _pinned;
        int _loadFrame = -1;
        Transform _holder;
        HideFlags _flags;
        bool _released;
        Func<char, bool> _covers;
        readonly Dictionary<long, Active> _active = new Dictionary<long, Active>();
        readonly List<long> _scratch = new List<long>();

        public Vol2CastCoordinator Coordinator => _c;
        /// <summary>Casts the director built a prefab for / skipped (no playable prefab for the chosen variant) / retired, and the hits it relayed (bookkeeping).</summary>
        public int Spawned { get; private set; }
        public int Skipped { get; private set; }
        public int Retired { get; private set; }
        public int HitsRelayed { get; private set; }
        public int ActiveCount => _active.Count;
        public string LastNote { get; private set; } = "";

        /// <summary>The catalogue in use: the one given to <see cref="Attach"/>, else the asset at the fixed path, looked for again (at most once a frame) while there is none -
        /// a catalogue made after Play started is found at the next cast.</summary>
        public SpellFxCatalog Catalog
        {
            get
            {
                if (_catalog == null && !_pinned && _loadFrame != Time.frameCount) { _loadFrame = Time.frameCount; _catalog = SpellFxCatalog.Load(); }
                return _catalog;
            }
        }

        /// <summary>Builds the director under <paramref name="parent"/>. Never null in practice; a missing catalogue is tolerated (it then covers nothing).</summary>
        public static SpellFxDirector Attach(Vol2CastCoordinator coordinator, Transform parent, SpellFxCatalog catalog = null, HideFlags flags = HideFlags.None)
        {
            if (coordinator == null) throw new ArgumentNullException(nameof(coordinator));
            var go = new GameObject("SpellFxDirector") { hideFlags = flags };
            if (parent != null) go.transform.SetParent(parent, false);
            var d = go.AddComponent<SpellFxDirector>();
            d.Bind(coordinator, catalog, flags);
            return d;
        }

        void Bind(Vol2CastCoordinator coordinator, SpellFxCatalog catalog, HideFlags flags)
        {
            _c = coordinator; _clock = coordinator.Services.Clock; _catalog = catalog; _pinned = catalog != null; _flags = flags;
            // prefabs are built under this inactive holder so nothing of them wakes before the first Begin (a VFX Graph would start by itself)
            var holder = new GameObject("SpellFxHolder") { hideFlags = flags };
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);
            _holder = holder.transform;
            _c.CastBegan += OnBegan; _c.CastEnded += OnEnded; _c.ContactResolved += OnContact;
            _c.Materials.Created += OnMaterialCreated; _c.Materials.Ended += OnMaterialEnded;
            _c.Reactions.ReactionResolved += OnReaction;
            _covers = Covers;
            SpellFxSilencer.Register(_covers);
        }

        /// <summary>Does the catalogue cover this letter with the variant chosen now? (What the silencer asks at cast time.)</summary>
        public bool Covers(char letter) { var cat = Catalog; return cat != null && cat.Covers(letter); }

        public SpellFxPlayer PlayerOf(long castId) => _active.TryGetValue(castId, out var a) ? a.Player : null;

        public List<SpellFxPlayer> Players()
        {
            var l = new List<SpellFxPlayer>();
            foreach (var a in _active.Values) if (a.Player != null) l.Add(a.Player);
            return l;
        }

        void OnDestroy() { Release(); }

        /// <summary>Lets go of the executor events, the silencer and every prefab (what OnDestroy does; public because OnDestroy does not run for an edit-mode fixture).</summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            if (_c != null)
            {
                _c.CastBegan -= OnBegan; _c.CastEnded -= OnEnded; _c.ContactResolved -= OnContact;
                _c.Materials.Created -= OnMaterialCreated; _c.Materials.Ended -= OnMaterialEnded;
                _c.Reactions.ReactionResolved -= OnReaction;
            }
            if (_covers != null) SpellFxSilencer.Unregister(_covers);
            _covers = null;
            ClearAll();
            if (_holder != null) { if (Application.isPlaying) Destroy(_holder.gameObject); else DestroyImmediate(_holder.gameObject); }
            _holder = null;
        }

        void Update() { Step(); }

        /// <summary>One frame on the scope clock: a reset (status Aborted) removes a cast's prefab at once; every live prefab ticks; the ones past their expiry go.
        /// Public so an edit-mode fixture can drive it.</summary>
        public void Step()
        {
            if (_released || _clock == null || _active.Count == 0) return;
            long now = _clock.NowUs;
            _scratch.Clear();
            foreach (var k in _active.Keys) _scratch.Add(k);
            for (int i = 0; i < _scratch.Count; i++)
            {
                if (!_active.TryGetValue(_scratch[i], out var a)) continue;
                try
                {
                    if (a.Root == null || a.Player == null) { Remove(a); continue; }
                    var log = a.Cast.Log;
                    // AbortAll (the lab reset) stops a cast without raising CastEnded: its prefab goes at once, no tail
                    if (log != null && log.Status == "Aborted") { Retire(a, SpellFxEndReason.Aborted); continue; }
                    if (!a.Cast.Ended && log != null && log.Finished) { a.Cast.EndUs = log.EndUs > 0 ? log.EndUs : now; a.Player.End(SpellFxEndReason.Done); }
                    a.Player.Tick(now);
                    if (now >= a.Player.ExpireUs()) Retire(a, a.Cast.Ended ? SpellFxEndReason.Done : SpellFxEndReason.TimedOut);
                }
                catch (Exception e) { Debug.LogException(e); Retire(a, SpellFxEndReason.Aborted); }
            }
        }

        /// <summary>Removes every prefab now (a tool between two shots; the lab reset does not tell the director about casts that already ended).</summary>
        public void ClearAll()
        {
            _scratch.Clear();
            foreach (var k in _active.Keys) _scratch.Add(k);
            for (int i = 0; i < _scratch.Count; i++) if (_active.TryGetValue(_scratch[i], out var a)) Retire(a, SpellFxEndReason.Aborted);
        }

        void Retire(Active a, SpellFxEndReason reason)
        {
            try { if (a.Player != null && !a.Player.Ended) a.Player.End(reason); }
            catch (Exception e) { Debug.LogException(e); }
            Remove(a);
        }

        void Remove(Active a)
        {
            _active.Remove(a.Cast.CastId);
            Retired++;
            if (a.Root != null) { if (Application.isPlaying) Destroy(a.Root); else DestroyImmediate(a.Root); }
        }

        static void Safe(Action a) { try { a(); } catch (Exception e) { Debug.LogException(e); } }

        // ---------------------------------------------------------------- executor events

        void OnBegan(Vol2CastLog log)
        {
            Safe(() =>
            {
                if (_released || log == null || log.Spec == null || log.Emitter == null) return;
                var cat = Catalog;
                if (cat == null) return;
                char letter = log.Glyph.Letter;
                string variant = SpellFxSwitch.Get(letter);
                var prefab = cat.Find(letter, variant);
                if (prefab == null) { Skipped++; LastNote = "no prefab for " + letter + "/" + variant + " (old look stays)"; return; }
                if (_active.ContainsKey(log.Cast.Value)) return;
                var cast = BuildCast(log, variant);
                var go = Instantiate(prefab, _holder);
                go.name = "SpellFx_" + letter + "_" + variant + "_" + log.Cast.Value;
                if (_flags != HideFlags.None) SetFlags(go.transform, _flags);
                var player = go.GetComponent<SpellFxPlayer>();
                if (player == null) { Debug.LogWarning("[SpellFx] prefab " + prefab.name + " has no SpellFxPlayer"); if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); return; }
                var a = new Active { Cast = cast, Player = player, Root = go };
                _active[cast.CastId] = a;
                Spawned++;
                player.Begin(cast);                                  // still inactive: parts hold whatever must not start by itself
                go.transform.SetParent(transform, true);             // wakes it, in place (Begin put it at the cast point)
                player.Tick(_clock.NowUs);                           // a cast that leaves at its commit launches in this very frame
                LastNote = "built " + cast.Id;
            });
        }

        SpellFxCast BuildCast(Vol2CastLog log, string variant)
        {
            var spec = log.Spec; var g = log.Glyph;
            return new SpellFxCast
            {
                CastId = log.Cast.Value, Letter = g.Letter, Body = g.Body, Frame = g.Frame, Verb = g.Verb, Variant = variant, Spec = spec,
                Origin = spec.Origin, Forward = spec.Forward, Right = Vector3.Cross(Vector3.up, spec.Forward), Radius = SpellFxCast.RadiusOf(spec, g.Frame),
                StartUs = log.StartUs, LaunchUs = Math.Max(log.StartUs, log.Emitter.StartUs), Log = log, Clock = _clock, BrushTip = Tip, BrushGrip = Grip,
            };
        }

        void OnEnded(Vol2CastLog log)
        {
            Safe(() =>
            {
                if (log == null || !_active.TryGetValue(log.Cast.Value, out var a)) return;
                a.Cast.EndUs = log.EndUs > 0 ? log.EndUs : _clock.NowUs;
                a.Player.End(SpellFxEndReason.Done);
            });
        }

        void OnContact(Vol2CastLog log, Vol2ContactRecord rec)
        {
            Safe(() =>
            {
                if (log == null || rec == null || !_active.TryGetValue(log.Cast.Value, out var a)) return;
                if (!rec.BudgetAccepted) return;                     // a contact the rules refused draws no hit (the same rule as the old model)
                a.Player.Hit(MakeHit(a.Cast, rec));
                HitsRelayed++;
            });
        }

        SpellFxHit MakeHit(SpellFxCast cast, Vol2ContactRecord rec)
        {
            // the record has no position: the target registry resolves the body (the way the free showcase did)
            var t = _c.Registry.Find(rec.TargetId);
            Vector3 feet = t != null ? t.Position : cast.Origin + cast.Forward * rec.Along;
            Vector3 centre = feet + Vector3.up * 0.9f;
            Transform tr = null;
            if (t is Vol2EnemyTarget e && e.Vitals != null)
            {
                tr = e.Vitals.transform;
                var col = e.Vitals.GetComponentInChildren<Collider>();
                if (col != null && col.enabled && col.bounds.size.y > 0.3f) centre = col.bounds.center;
            }
            return new SpellFxHit { Position = centre, Feet = feet, NowUs = rec.NowUs, TargetId = rec.TargetId, Kind = rec.Kind, Target = tr, TargetRadius = t != null ? t.Radius : 0.4f, Along = rec.Along };
        }

        void OnMaterialCreated(Vol2Material m)
        {
            Safe(() =>
            {
                if (m == null || !_active.TryGetValue(m.Cast.Value, out var a)) return;
                a.Player.Material(MakeMaterial(m, SpellFxMaterialPhase.Created));
            });
        }

        void OnMaterialEnded(Vol2Material m, Vol2MaterialEnd why)
        {
            Safe(() =>
            {
                if (m == null || !_active.TryGetValue(m.Cast.Value, out var a)) return;
                if (why == Vol2MaterialEnd.Aborted) { Retire(a, SpellFxEndReason.Aborted); return; }       // the lab reset: everything of the cast goes at once
                a.Player.Material(MakeMaterial(m, SpellFxMaterialPhase.Ended));
            });
        }

        SpellFxMaterial MakeMaterial(Vol2Material m, SpellFxMaterialPhase phase)
        {
            var f = m.Footprint;
            float radius = 0f;
            if (f != null)
                switch (f.Kind)
                {
                    case Vol2FootprintKind.Disc: radius = f.Radius; break;
                    case Vol2FootprintKind.Fan: radius = f.FanDepth; break;
                    case Vol2FootprintKind.Ring: radius = f.RingOuter; break;
                }
            return new SpellFxMaterial
            {
                Phase = phase, Id = m.Id.Value, NowUs = _clock.NowUs, Kind = m.Kind, Body = m.Body, Letter = m.Letter, Position = f != null ? f.Centre : Vector3.zero,
                Radius = radius, End = m.End, Attached = m.Attached,
            };
        }

        void OnReaction(Vol2ReactionVisual v)
        {
            Safe(() =>
            {
                if (v == null) return;
                var r = new SpellFxReaction { EventId = v.EventId, NowUs = v.AtUs, ActingBody = v.ActingBody, ContactedBody = v.ContactedBody, Relation = v.Relation.ToString(), Product = v.Product.ToString(), Point = v.Point };
                if (_active.TryGetValue(v.ActingCast.Value, out var acting)) { r.ActingSide = true; acting.Player.Reaction(r); }
                if (v.ContactCast.Value != v.ActingCast.Value && _active.TryGetValue(v.ContactCast.Value, out var contacted)) { r.ActingSide = false; contacted.Player.Reaction(r); }
            });
        }

        // ---------------------------------------------------------------- the great brush (presentation only, looked up lazily a bounded number of times)

        Oheangbu.App.World.WorldMacroPlayerGestureRig _rig;
        int _rigSearches;

        Oheangbu.App.World.WorldMacroPlayerGestureRig Rig()
        {
            if (_rig != null) return _rig;
            if (_rigSearches >= 40 || !Application.isPlaying) return null;
            _rigSearches++;
            _rig = FindFirstObjectByType<Oheangbu.App.World.WorldMacroPlayerGestureRig>();
            return _rig;
        }

        Vector3? Tip() { var r = Rig(); return r != null && r.WorldTip != null ? r.WorldTip.position : (Vector3?)null; }
        Vector3? Grip() { var r = Rig(); return r != null && r.WorldBrushRoot308 != null ? r.WorldBrushRoot308.position : (Vector3?)null; }

        static void SetFlags(Transform t, HideFlags flags)
        {
            t.gameObject.hideFlags = flags;
            for (int i = 0; i < t.childCount; i++) SetFlags(t.GetChild(i), flags);
        }
    }
}
