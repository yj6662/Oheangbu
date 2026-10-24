using System;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>Shared fakes of the slice 3 edit-mode fixtures (Vol2ShapeFixture, Vol2BodyFixture): bodies, a caster, a wall, recording sinks and a
    /// rig that builds a Vol2 scope with a hand-driven clock and the real coordinator. No scene object, no Play. Coordinates: the caster stands at
    /// the origin looking along +z; At(ahead, side) is (side, 0, ahead).</summary>
    internal static class Vol2ShapeFixtureKit
    {
        internal sealed class FakeTarget : IVol2Target
        {
            public long Id { get; set; }
            public long LifeRevision { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Previous { get; set; }
            public float Radius { get; set; } = 0.4f;
            public bool Alive { get; set; } = true;
            public string Name { get; set; }
            public FakeTarget(long id, string name, float ahead, float side) { Id = id; Name = name; Position = Previous = At(ahead, side); }
            public void MoveTo(Vector3 p) { Previous = Position; Position = p; }
        }

        internal static Vector3 At(float ahead, float side) => new Vector3(side, 0f, ahead);

        internal sealed class FakeCaster : IVol2Caster
        {
            public Vector3 Position { get; set; } = Vector3.zero;
            public Vector3 AimDirection { get; set; } = Vector3.forward;
            public Transform Anchor => null;
        }

        /// <summary>A wall plane across the aim axis at z = At (the caster looks along +z).</summary>
        internal sealed class FakeWall : IVol2Obstruction
        {
            public float? At;
            public bool TryBlock(Vector3 from, Vector3 to, out float distance)
            {
                distance = 0f;
                if (!At.HasValue || !(from.z <= At.Value && At.Value <= to.z)) return false;
                distance = At.Value - from.z; return true;
            }
        }

        internal sealed class RecDamage : IVol2DamageSink
        {
            public readonly List<Vol2DamageRequest> Requests = new List<Vol2DamageRequest>();
            public Vol2DamageResult Apply(in Vol2DamageRequest r) { Requests.Add(r); return new Vol2DamageResult(true, (float)r.Units * 10f, false, ""); }
        }

        internal sealed class RecStatus : IVol2StatusSink
        {
            public readonly List<Vol2BodyEffectRequest> Requests = new List<Vol2BodyEffectRequest>();
            public string Apply(in Vol2BodyEffectRequest r) { Requests.Add(r); return "rec"; }
            public void Tick(float dt) { }
        }

        internal sealed class RecParry : IVol2ParryPort
        {
            public readonly List<Vol2ParryWindow> Windows = new List<Vol2ParryWindow>();
            public void WindowOpened(in Vol2ParryWindow w) { Windows.Add(w); }
        }

        internal sealed class Rig
        {
            public Vol2Services Services;
            public Vol2ShapeRuntime Runtime;
            public Vol2CastCoordinator Coord;
            public readonly List<FakeTarget> Targets = new List<FakeTarget>();
            public FakeCaster Caster = new FakeCaster();
            public FakeWall Wall = new FakeWall();
            public RecDamage Damage = new RecDamage();
            public RecStatus Status = new RecStatus();
            public RecParry Parry = new RecParry();
            public Vol2RecordingInkSpender Ink = new Vol2RecordingInkSpender();
            public double Now;
            public NumbersV3Dto Numbers => Services.Rulebook.Numbers;

            public static Rig Create(Vol2Rulebook book)
            {
                var rig = new Rig();
                rig.Services = Vol2Installer.Build(SpellRuntimeMode.Vol2, book, _ => true, () => rig.Now);
                var registry = new Vol2TargetRegistry { Synthetic = () => rig.Targets };
                rig.Runtime = Vol2ShapeRuntime.Create(rig.Services, registry, rig.Caster, rig.Wall, rig.Damage, rig.Status, rig.Ink);
                rig.Coord = rig.Runtime.Coordinator;
                rig.Coord.ParryPort = rig.Parry;
                return rig;
            }

            public FakeTarget Add(string name, float ahead, float side) { var t = new FakeTarget(Targets.Count + 1, name, ahead, side); Targets.Add(t); return t; }
            public void Advance(double seconds, int steps = 1)
            {
                for (int i = 0; i < steps; i++) { Now += seconds / steps; Coord.Tick((float)(seconds / steps)); }
            }
            /// <summary>Injects the letter the way the route does (adapter.Receive -> CastAccepted) and returns the cast log.</summary>
            public Vol2CastLog Cast(char letter, out Vol2CastDecision decision)
            {
                decision = Services.Route.Adapter.Receive(letter);
                Vol2CastLog log = null;
                if (decision.Accepted) Coord.TryGetLog(decision.CastId, out log);
                return log;
            }
            public static List<string> Names(Vol2CastLog log)
            {
                var l = new List<string>(); foreach (var c in log.Contacts) l.Add(c.TargetName); return l;
            }
        }

        /// <summary>Counts error logs while a fixture runs (an executor must never log an error to refuse something).</summary>
        internal sealed class ErrorCounter : IDisposable
        {
            public int Errors; public string First = "";
            public ErrorCounter() { Application.logMessageReceived += On; }
            void On(string c, string s, LogType t) { if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { Errors++; if (First.Length == 0) First = c; } }
            public void Dispose() { Application.logMessageReceived -= On; }
        }
    }
}
