using System;
using System.IO;
using Oheangbu.Spellcraft;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using VContainer;

namespace Oheangbu.App
{
    /// <summary>The scope-owned services of the vol.2 runtime (clock, id issuer, attack ledger, command ledger, route). One set per
    /// combat scope, owned by the VContainer scope; nothing here is static. Slices 2 to 8 take what they need from the container.</summary>
    public sealed class Vol2Services
    {
        public SpellRuntimeMode Mode { get; }
        public IVol2Clock Clock { get; }
        public Vol2IdIssuer Ids { get; }
        public Vol2AttackLedger Attacks { get; }
        public CommandLedger Commands { get; }
        public SpellRoute Route { get; }
        /// <summary>Null in Legacy308.</summary>
        public Vol2Rulebook Rulebook { get; }
        /// <summary>Slice 3: the shape / cast coordinator runtime of this scope. Null in Legacy308, and null until the scope's build callback
        /// (Vol2Installer.Install) has made it; an edit-mode fixture sets it through Vol2ShapeRuntime.Create.</summary>
        public Vol2ShapeRuntime Shapes { get; set; }
        /// <summary>Slice 4: the lab may jump the scope clock forward (to watch a 10 s residue expire without waiting 10 s). Zero in the game.</summary>
        public Vol2TimeSkew Skew { get; }
        public Vol2Services(SpellRuntimeMode mode, IVol2Clock clock, Vol2IdIssuer ids, Vol2AttackLedger attacks, CommandLedger commands, SpellRoute route, Vol2Rulebook rulebook, Vol2TimeSkew skew = null)
        { Mode = mode; Clock = clock; Ids = ids; Attacks = attacks; Commands = commands; Route = route; Rulebook = rulebook; Skew = skew ?? new Vol2TimeSkew(); }
    }

    /// <summary>Seconds added to the scope clock. 0 always, except while the spell lab fast-forwards a row: the clock stays monotonic (a forward jump is a long frame).</summary>
    public sealed class Vol2TimeSkew { public double Seconds; }

    /// <summary>S5-b slice 1: builds the Vol2Services for a mode and registers them in the combat scope. Registered in BOTH modes
    /// (clock, issuer, ledgers, route); only Vol2 gets an adapter. The runtime mode never changes after this: a switch is a
    /// scope rebuild (design section 3: no live switch with projectiles in flight).</summary>
    public static class Vol2Installer
    {
        public const int CombatScopeId = 1;
        /// <summary>TEMPORARY (slice 3), the one place the temporary damage adapter's scale is set: HP per action unit is UNDEFINED in the
        /// data. 45 is the D308-147 앞잡 hit and is only a stand-in. Remove together with Vol2TempEnemyDamageSink when slice 2's entry is bound.</summary>
        public const float TempHpPerActionUnit = Vol2TempEnemyDamageSink.UnconfirmedDefaultHpPerActionUnit;

        public static Vol2Services Build(SpellRuntimeMode mode, Vol2Rulebook rulebook, Func<Glyph, bool> isOpen, Func<double> seconds = null)
        {
            var skew = new Vol2TimeSkew();
            Func<double> source = seconds ?? (() => Time.timeAsDouble);
            var clock = new Vol2Clock(() => source() + skew.Seconds);
            var ids = new Vol2IdIssuer(CombatScopeId);
            SpellRoute route;
            if (mode == SpellRuntimeMode.Vol2)
            {
                if (rulebook == null) rulebook = Vol2Rulebook.Unavailable("no rulebook was given");
                var adapter = new Vol2GlyphAdapter(rulebook.Ok ? rulebook.Numbers : null, rulebook.Error, clock, ids, isOpen);
                route = SpellRoute.CreateVol2(adapter);
            }
            else { route = SpellRoute.CreateLegacy(); rulebook = null; }
            return new Vol2Services(mode, clock, ids, new Vol2AttackLedger(), new CommandLedger(), route, rulebook, skew);
        }

        public static void Install(IContainerBuilder builder, Vol2Services services)
        {
            builder.RegisterInstance(services);
            builder.RegisterInstance(services.Route);
            builder.RegisterInstance(services.Clock);
            builder.RegisterInstance(services.Ids);
            builder.RegisterInstance(services.Attacks);
            builder.RegisterInstance(services.Commands);
            // slice 3: the shape executors and the cast coordinator (Vol2 scopes only; Legacy308 gets nothing). Built after the container, so the
            // wiring, the ink pool and the combat scene are resolvable; the lab runner, built later, finds it as services.Shapes.
            if (services.Mode == SpellRuntimeMode.Vol2 && services.Rulebook != null && services.Rulebook.Ok)
                builder.RegisterBuildCallback(resolver => Vol2ShapeRuntime.CreateForScope(resolver, services));
            // slice 2 (main agent, 2026-10-24): the defence side - real damage entry, ink gate, parry, 갈무리, 걱. MUST stay after the shape runtime's callback
            // above: it binds kit.DamageSink / kit.PaidInk into the coordinator that callback made. Default HP per unit (Vol2Units.DefaultHpPerUnit, 10, TEST).
            Vol2DefenceInstaller.Register(builder, services);
        }
    }

    /// <summary>The request the editor command vol2lab:play leaves for the spell lab scope. A file, not a static: Enter Play Mode keeps
    /// statics between runs, and the scene must not be saved to carry a flag. One-shot: the scope that reads it renames it to
    /// consumed_&lt;runId&gt;.json, a request older than five minutes is renamed expired_*.json and ignored, and a scope whose scene
    /// is not the spell lab never touches it. The main scene has no lab unlock stand-in, so it can never reach this.</summary>
    [Serializable]
    public sealed class Vol2LabRequest
    {
        public string mode = "Vol2";
        public bool run = true;
        public string runId = "";
        public string commit = "";
        public long issuedUnixMs;
        public string scene = "";
        public SpellRuntimeMode Mode => string.Equals(mode, "Vol2", StringComparison.OrdinalIgnoreCase) ? SpellRuntimeMode.Vol2 : SpellRuntimeMode.Legacy308;
    }

    public static class Vol2LabRequestFile
    {
        public const string LabScenePath = "Assets/_Project/Scenes/Dev/SpellLab308.unity";
        public const long MaxAgeMs = 300000;
        public static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/SpellVFX308/Vol2Lab"));
        public static string RequestPath => Path.Combine(Folder, "request.json");

        public static bool TryConsume(string scenePath, out Vol2LabRequest request)
        {
            request = null;
            if (!Application.isEditor || scenePath != LabScenePath) return false;
            string path = RequestPath;
            try
            {
                if (!File.Exists(path)) return false;
                var parsed = JsonUtility.FromJson<Vol2LabRequest>(File.ReadAllText(path));
                string id = parsed != null && !string.IsNullOrEmpty(parsed.runId) ? parsed.runId : DateTime.Now.ToString("yyyyMMdd_HHmmss");
                long age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (parsed != null ? parsed.issuedUnixMs : 0);
                bool fresh = parsed != null && age >= 0 && age <= MaxAgeMs && parsed.Mode == SpellRuntimeMode.Vol2;
                string target = Path.Combine(Folder, (fresh ? "consumed_" : "expired_") + id + ".json");
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
                if (!fresh) { Debug.LogWarning("[Vol2Lab] request ignored (stale or not Vol2): " + target); return false; }
                request = parsed;
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Vol2Lab] request unreadable: " + e.Message); return false; }
        }
    }
}
