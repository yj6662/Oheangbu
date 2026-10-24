using System;
using System.Collections.Generic;
using System.Globalization;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.App
{
    /// <summary>S5-b slice 8 (design s5b_design_v1 sections 6 and 8, "필드 5자 양쪽 용도"): the field's side of the five field glyphs, bound to the coordinator's <see cref="IVol2FieldPort"/>. Each of 국 눈 뭄 숫 웅
    /// has a COMBAT use (the executors) and a FIELD use (this adapter): 국 a platform to stand on, 눈 a burning vine door, 뭄 the hardened ground, 숫 a cut rock / iron gate, 웅 a cleansed pollution.
    /// It reuses what the old field code already has - the gate object <see cref="SpellFieldGate308"/> (Vine / Boulder / Pollution, found through physics, opened with Open()) and the
    /// rule <see cref="FieldGateRule308"/> (the nearest closed gate of the glyph's kind that can be reached, only outside combat) - and bypasses the OLD letter judgement entirely.
    /// There is NO contamination gate API besides SpellFieldGate308 (kind Pollution): 웅 opens those. One instance per scope; nothing static. Edit-mode fixtures give it fake gates and a
    /// fake combat test (no GameObject is made unless <see cref="MakePlatforms"/> is on).</summary>
    public sealed class Vol2FieldGateAdapter : IVol2FieldPort
    {
        /// <summary>A gate as the adapter sees it. The production source is SpellFieldGate308 (see <see cref="Vol2FieldGateAdapter.FromScene"/>).</summary>
        public interface IGate
        {
            string Id { get; }
            SpellFieldGateKind Kind { get; }
            Vector3 Position { get; }
            float Radius { get; }
            bool IsOpen { get; }
            bool Open();
        }

        sealed class SceneGate : IGate
        {
            readonly SpellFieldGate308 _g;
            public SceneGate(SpellFieldGate308 g) { _g = g; }
            public string Id => _g.Id;
            public SpellFieldGateKind Kind => _g.Kind;
            public Vector3 Position => _g.transform.position;
            public float Radius => _g.Radius;
            public bool IsOpen => _g.IsOpen;
            public bool Open() => _g.Open();
        }

        sealed class Platform { public GameObject Go; public long UntilUs; public CastId Cast; }

        readonly IVol2Caster _caster;
        readonly IVol2Clock _clock;
        readonly Vol2ReactionTuning _tuning;
        readonly List<Platform> _platforms = new List<Platform>();
        /// <summary>The gates in the world. Default: every active SpellFieldGate308 of the loaded scenes (a lookup per request, not per frame).</summary>
        public Func<IEnumerable<IGate>> Gates { get; set; }
        /// <summary>true = a living enemy hunts / stands near: the field uses of the glyphs are refused (the old rule: gates open only outside combat). Default: any living target within CombatRadius.</summary>
        public Func<bool> InCombat { get; set; }
        public float CombatRadius { get; set; } = 12f;
        /// <summary>Edit-mode fixtures turn this off: a platform is then only noted.</summary>
        public bool MakePlatforms { get; set; } = true;
        public Func<IReadOnlyList<IVol2Target>> Targets { get; set; }
        /// <summary>One line per request and what was done (read by the lab and the fixture).</summary>
        public readonly List<string> Log = new List<string>();
        public int Opened { get; private set; }
        public int Refused { get; private set; }
        public int PlatformsMade { get; private set; }
        public int PlatformsEnded { get; private set; }

        public Vol2FieldGateAdapter(IVol2Caster caster, IVol2Clock clock, Vol2ReactionTuning tuning = null)
        {
            _caster = caster; _clock = clock;
            _tuning = tuning ?? new Vol2ReactionTuning();
            Gates = FromScene;
            InCombat = DefaultInCombat;
        }

        public static IEnumerable<IGate> FromScene()
        {
            var list = new List<IGate>();
            foreach (var g in UnityEngine.Object.FindObjectsByType<SpellFieldGate308>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (g != null && g.isActiveAndEnabled) list.Add(new SceneGate(g));
            return list;
        }

        bool DefaultInCombat()
        {
            var targets = Targets != null ? Targets() : null;
            if (targets == null || _caster == null) return false;
            Vector3 p = _caster.Position;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.Alive) continue;
                Vector3 d = t.Position - p; d.y = 0f;
                if (d.magnitude <= CombatRadius) return true;
            }
            return false;
        }

        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        string Done(string note) { Log.Add(note); return note; }

        // ---- the gates ----

        /// <summary>Opens the nearest closed gate of <paramref name="kind"/> whose edge is within <paramref name="reach"/> of <paramref name="centre"/>; Pick is the old rule (nearest wins, a tie goes to the lower id).</summary>
        IGate Pick(SpellFieldGateKind kind, Vector3 centre, float reach, out int considered)
        {
            var found = new List<IGate>(); var snaps = new List<FieldGateSnap>();
            foreach (var g in Gates != null ? Gates() : new List<IGate>())
            {
                if (g == null) continue;
                snaps.Add(new FieldGateSnap(found.Count, (int)g.Kind, g.Position, g.Radius, g.IsOpen, false));
                found.Add(g);
            }
            considered = found.Count;
            // reach is measured to the gate's edge; the cast stands INSIDE the ring, so it needs no facing: halfAngle 180
            int pick = FieldGateRule308.Pick(snaps, (int)kind, centre, Vector3.forward, reach, 180f);
            return pick == FieldGateRule308.None ? null : found[pick];
        }

        string OpenKind(string what, SpellFieldGateKind kind, Vector3 centre, float reach)
        {
            if (InCombat != null && InCombat()) { Refused++; return Done(what + ": in combat - the gate stays shut"); }
            var gate = Pick(kind, centre, reach, out int n);
            if (gate == null) { Refused++; return Done(what + ": no closed " + kind + " gate within " + F(reach) + " m (" + n + " gates known)"); }
            bool opened = gate.Open();
            if (opened) Opened++;
            return Done(what + ": " + kind + " gate '" + gate.Id + "' " + (opened ? "opened" : "was open already"));
        }

        /// <summary>눈: a burning vine. The gate whose id is the vine material's Tag opens; a vine with no tag opens the nearest Vine gate under it.</summary>
        public string OnVineBurn(CastId cast, Vol2Material vine)
        {
            if (vine == null) return Done("vine door: no material");
            if (InCombat != null && InCombat()) { Refused++; return Done("vine door " + vine.Id + ": in combat - the gate stays shut"); }
            IGate target = null;
            if (!string.IsNullOrEmpty(vine.Tag))
                foreach (var g in Gates != null ? Gates() : new List<IGate>()) if (g != null && g.Kind == SpellFieldGateKind.Vine && g.Id == vine.Tag) { target = g; break; }
            if (target == null) target = Pick(SpellFieldGateKind.Vine, vine.Footprint != null ? vine.Footprint.Centre : Vector3.zero, _tuning.GateReachMeters, out int _);
            if (target == null) { Refused++; return Done("vine door " + vine.Id + ": no Vine gate under it"); }
            bool opened = target.Open();
            if (opened) Opened++;
            return Done("vine door " + vine.Id + ": Vine gate '" + target.Id + "' " + (opened ? "burnt open" : "was open already"));
        }

        /// <summary>숫: the ring cut the rock / iron gate. The cast stands at the ring's centre; the ring reaches <paramref name="radius"/> m, so a Boulder gate whose edge is within that (plus the usual reach) is cut.</summary>
        public string OnGateCut(CastId cast, Vector3 centre, float radius)
            => OpenKind("rock/iron gate cut r=" + F(radius), SpellFieldGateKind.Boulder, centre, radius + _tuning.GateReachMeters);

        /// <summary>웅: the returning ring cleansed the ground between <paramref name="inner"/> and <paramref name="outer"/>: EVERY closed Pollution gate the band reaches is cleansed (the old glyph opened one; a ring passes over all of them).</summary>
        public string OnCleanse(CastId cast, Vector3 centre, float inner, float outer)
        {
            if (InCombat != null && InCombat()) { Refused++; return Done("pollution: in combat - the ground stays foul"); }
            int opened = 0, seen = 0; var ids = new List<string>();
            foreach (var g in Gates != null ? Gates() : new List<IGate>())
            {
                if (g == null || g.Kind != SpellFieldGateKind.Pollution) continue;
                seen++;
                Vector3 d = g.Position - centre; d.y = 0f;
                float dist = d.magnitude;
                if (dist + g.Radius < inner || dist - g.Radius > outer + _tuning.GateReachMeters) continue;
                if (g.Open()) { opened++; Opened++; ids.Add(g.Id); }
            }
            if (opened == 0) Refused++;
            return Done("pollution cleansed " + F(inner) + " to " + F(outer) + " m: " + opened + " of " + seen + " gates" + (ids.Count > 0 ? " (" + string.Join(",", ids) + ")" : ""));
        }

        /// <summary>국 (a platform) and 뭄 (the hardened ground): outside combat a flat collider stands on the ring for the material's life, so the player can climb onto it; in combat the glyph
        /// does its combat work only (국 stretches my materials, 뭄's ground blocks enemy dashes) and no platform is made.</summary>
        public string OnPlatform(CastId cast, Vector3 centre, float radius, long lifeUs)
        {
            if (InCombat != null && InCombat()) return Done("platform r=" + F(radius) + ": in combat - no platform (combat use only)");
            long until = (_clock != null ? _clock.NowUs : 0L) + lifeUs; Platform p = null;
            if (MakePlatforms && Application.isPlaying)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Vol2Platform_" + cast.Value;
                var r = go.GetComponent<Renderer>(); if (r != null) UnityEngine.Object.Destroy(r);          // invisible: the presenter draws the ring; the collider is the platform
                float height = Mathf.Max(0.05f, _tuning.PlatformThickness);
                go.transform.position = new Vector3(centre.x, centre.y + height * 0.5f, centre.z);
                go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
                p = new Platform { Go = go, UntilUs = until, Cast = cast };
                _platforms.Add(p); PlatformsMade++;
            }
            return Done("platform r=" + F(radius) + " for " + lifeUs + " us" + (p != null ? ": collider made" : ": noted (no collider)"));
        }

        /// <summary>Called every frame with the scope clock: a platform whose life is over goes away.</summary>
        public void Tick(long nowUs)
        {
            for (int i = _platforms.Count - 1; i >= 0; i--)
            {
                var p = _platforms[i];
                if (p.Go == null) { _platforms.RemoveAt(i); continue; }
                if (nowUs >= p.UntilUs) { UnityEngine.Object.Destroy(p.Go); _platforms.RemoveAt(i); PlatformsEnded++; }
            }
        }

        public int StandingPlatforms => _platforms.Count;

        public void Clear()
        {
            foreach (var p in _platforms) if (p.Go != null) UnityEngine.Object.Destroy(p.Go);
            _platforms.Clear();
        }
    }
}
