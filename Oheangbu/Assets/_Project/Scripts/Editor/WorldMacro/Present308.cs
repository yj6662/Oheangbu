using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Data.Spell;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 present add-on: editor commands of the spell presenter on the deploy layer (SPEC-SPELL-120-308 section 9 seam,
    // drawn by SPEC-SPELL-DEPLOY-308). Queue: Oheangbu.EditorTools.WorldMacro.Present308 Run "<command>"
    //   present308-status                 what is registered, the sheet, the layer's switch; in Play the stage's counters
    //   present308-assets[:dry|:revert]   the sheet as an asset (Resources/Deploy308/SpellPresent308Sheet), made from the code
    //                                     defaults. Optional: without it the code defaults are used as they are.
    //   present308-unit                   the checks that need Unity objects but no Play (in memory, nothing is saved)
    //   preview:on:<form>[:view=4][:t=s][:at=x,y,z][:yaw=deg][:fov=60][:element=wood|fire|earth|metal|water][:grade=0|1|2][:radius=3][:states=bound,slowed,exposed,weakened][:allowdirty]
    //             [:tier=pc|mobile][:letter=<glyph>][:life=6]
    //        form = slash | slashhit | blade | install | carried | state | ring | fling | shed | spent | splash | drip | cover | combo
    //        forms3 (D9 / D11): ring = the zone's ring stroke (a body lying on the ground) over its held ground marks;
    //          cover = a cover's standing ink wall (Ward / Begin: Stand + HeldRing) `view` m ahead, :radius (default 1.2), :life (6 s), default t = 3 s
    //          combo = an installed mark's triggered burst (Mark / Detonate) on the enemy `view` m ahead, default t = its burst moment
    //          :letter picks the glyph whose map row the burst takes (default: the first Ward row for cover, the first install row for combo)
    //        An in-memory preview in the frame of the layer's own preview (DeployLook308): the same root and eye camera names, so
    //        its capture command works and its "preview:off" removes this one too. Nothing is saved.
    //   preview:off                       remove the preview (DeployLook308's teardown)
    // Everything here is Edit mode only, has no dialog, and writes no scene. A line is "ok" or "FAIL" with the measured numbers.
    public static class Present308
    {
        const string SheetPath = Deploy308.ResourcesDir + "/SpellPresent308Sheet.asset";
        const string RootName = "DeployLook308_Preview", EyeName = "DeployLook308_Eye", Prefix = "DeployLook308_";
        const HideFlags Flags = HideFlags.DontSave;
        const float EyeHeight = 1.6f;

        public static string Run(string command)
        {
            string[] a = (command ?? "").Split(':');
            try
            {
                switch (a[0])
                {
                    case "present308-status": return Status();
                    case "present308-assets": return Assets(a.Skip(1).Contains("dry"), a.Skip(1).Contains("revert"));
                    case "present308-unit": return Unit();
                    case "preview":
                        if (a.Length > 2 && a[1] == "on") return PreviewOn(a);
                        if (a.Length > 1 && a[1] == "off") return DeployLook308.Run("preview:off");
                        throw new PostLedger308.Refused("use preview:on:<form>[...] or preview:off");
                    default: throw new PostLedger308.Refused("unknown command '" + command + "'");
                }
            }
            catch (PostLedger308.Refused r) { return "refused: " + r.Message; }
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder("present308 status\n");
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            var sheet = AssetDatabase.LoadAssetAtPath<SpellPresent308SheetSO>(SheetPath);
            sb.AppendLine("presenter class: " + typeof(SpellDeployPresenter308).FullName + " (registered by SpellPresentationInstaller308; falls back to " + typeof(Vfx120SpellPresenter308).Name + " per glyph)");
            sb.AppendLine("sheet: " + (sheet != null ? SheetPath + " | routes " + (sheet.Routes != null ? sheet.Routes.Length : 0) : "no asset (the code defaults are used: " + SpellPresent308SheetSO.DefaultRoutes().Length + " routes)"));
            if (profile == null) sb.AppendLine("layer profile: missing (" + Deploy308.ProfilePath + ") - every glyph goes to the interim presenter");
            else
            {
                sb.AppendLine("layer profile: LayerEnabled=" + profile.LayerEnabled + " | EnabledLetters='" + profile.EnabledLetters + "' (" + (profile.EnabledLetters ?? "").Length + ") | map=" + (profile.Map != null));
                if (profile.Map != null && !string.IsNullOrEmpty(profile.EnabledLetters))
                {
                    var probe = sheet != null ? sheet : ScriptableObject.CreateInstance<SpellPresent308SheetSO>();
                    try
                    {
                        var by = profile.EnabledLetters.Where(c => profile.Map.TryGet(c, out _)).GroupBy(c => { profile.Map.TryGet(c, out var row); return row.Category; }).OrderBy(g => (int)g.Key);
                        sb.AppendLine("switched on by category: " + string.Join(" | ", by.Select(g => g.Key + " " + new string(g.ToArray()))));
                        sb.AppendLine("hits the layer shows itself (no KTP contact): " + new string(profile.EnabledLetters.Where(c => SpellPresentRules308.OwnsHit(profile, probe, c)).ToArray()));
                    }
                    finally { if (sheet == null) Object.DestroyImmediate(probe); }
                }
            }
            if (Application.isPlaying)
            {
                var wirings = Object.FindObjectsByType<CombatLoopWiring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var wiring in wirings)
                {
                    var director = wiring.DeployDirector308;
                    sb.AppendLine("wiring '" + wiring.name + "': " + (director == null ? "the layer is not up (profile missing or LayerEnabled off): the interim presenter shows everything"
                        : director.Present == null ? "the layer is up but has no presenter stage" : director.Present.Describe()));
                }
                if (wirings.Length == 0) sb.AppendLine("no CombatLoopWiring in the loaded scenes");
            }
            else sb.AppendLine("Edit mode: the stage's counters are shown in Play");
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- the sheet asset

        static string Assets(bool dry, bool revert)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var existing = AssetDatabase.LoadAssetAtPath<SpellPresent308SheetSO>(SheetPath);
            if (revert)
            {
                if (existing == null) return "present308-assets:revert: no " + SheetPath + " (nothing to remove)";
                if (dry) return "present308-assets:revert:dry: would delete " + SheetPath + " (the code defaults are used again)";
                return AssetDatabase.DeleteAsset(SheetPath) ? "present308-assets:revert: deleted " + SheetPath : "refused: " + SheetPath + " could not be deleted";
            }
            if (existing != null) return "present308-assets: " + SheetPath + " exists (routes " + (existing.Routes != null ? existing.Routes.Length : 0) + ") - nothing written";
            if (AssetDatabase.LoadMainAssetAtPath(SheetPath) != null) throw new PostLedger308.Refused("another asset sits at " + SheetPath);
            if (!AssetDatabase.IsValidFolder(Deploy308.ResourcesDir)) throw new PostLedger308.Refused("no folder " + Deploy308.ResourcesDir + " (run deploy308-assets first)");
            if (dry) return "present308-assets:dry: would create " + SheetPath + " from the code defaults (" + SpellPresent308SheetSO.DefaultRoutes().Length + " routes); nothing written";
            var sheet = ScriptableObject.CreateInstance<SpellPresent308SheetSO>();
            AssetDatabase.CreateAsset(sheet, SheetPath);
            AssetDatabase.SaveAssetIfDirty(sheet);
            return "present308-assets: created " + SheetPath + " (" + sheet.Routes.Length + " routes, code defaults) | saved with SaveAssetIfDirty | revert: present308-assets:revert";
        }

        // ---------------------------------------------------------------- unit checks

        sealed class Host : ISpellDeployHost308
        {
            public int Stamps, Held, Released, Air, Still, Began, Impacts, GroggyAsked, Rented;
            public float MaxAirLife, MaxStampLife;
            public bool Groggy;
            readonly InkBurstBuffer308[] _buffers = new InkBurstBuffer308[4];
            readonly Mesh[] _meshes = new Mesh[4];
            readonly InkDeployRuntime308[] _owners = new InkDeployRuntime308[4];
            public Host()
            {
                for (int i = 0; i < _buffers.Length; i++) { _buffers[i] = new InkBurstBuffer308(2048); _meshes[i] = new Mesh { name = Prefix + "PresentUnitBurst" + i, hideFlags = Flags }; }
            }
            public DeployTier308 Tier => DeployTier308.PC;
            public float CutPause => 0f;
            public Camera ViewCamera => null;
            public bool RentBurst(InkDeployRuntime308 owner, out InkBurstBuffer308 buffer, out Mesh mesh)
            {
                buffer = null; mesh = null;
                for (int i = 0; i < _owners.Length; i++)
                {
                    if (_owners[i] != null) continue;
                    _owners[i] = owner; buffer = _buffers[i]; mesh = _meshes[i]; Rented++;
                    return true;
                }
                return false;
            }
            public void ReturnBurst(InkDeployRuntime308 owner, InkBurstBuffer308 buffer, Mesh mesh)
            { for (int i = 0; i < _owners.Length; i++) if (_buffers[i] == buffer) _owners[i] = null; }
            public void StampResidue(in ResidueStamp308 stamp) { Stamps++; if (stamp.Held) Held++; MaxStampLife = Mathf.Max(MaxStampLife, stamp.Life); }
            public void ReleaseHeld(int owner) { Released++; }
            public void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY)
            { if (gravityScale <= 0f) Still++; else Air++; MaxAirLife = Mathf.Max(MaxAirLife, life); }
            public void RequestImpact(in ImpactRequest308 request) { Impacts++; }
            public void NotifyDeployBegan(char letter) { Began++; }
            public bool TargetGroggy(Transform target, AreaImpactPlan plan) { GroggyAsked++; return Groggy; }
            public void Reset() { Stamps = Held = Released = Air = Still = Began = Impacts = GroggyAsked = 0; MaxAirLife = MaxStampLife = 0f; }
            public void Dispose() { foreach (var mesh in _meshes) if (mesh != null) Object.DestroyImmediate(mesh); }
        }

        static string Unit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            var sb = new StringBuilder();
            int checks = 0, failed = 0;
            void Check(string id, bool ok, string detail) { checks++; if (!ok) failed++; sb.AppendLine(id + " " + (ok ? "ok" : "FAIL") + ": " + detail); }
            var rows = Deploy308.ReadMap(out _, out _);
            Shader burstShader = Deploy308.RequireShader(Deploy308.BurstShader);
            SpellDeploy308ProfileSO profile = null; SpellDeploy308MapSO map = null; SpellPresent308SheetSO sheet = null; Material burst = null; GameObject root = null; Host host = null;
            bool sceneDirty = SceneManager.GetActiveScene().isDirty;
            try
            {
                profile = ScriptableObject.CreateInstance<SpellDeploy308ProfileSO>(); profile.name = Prefix + "PresentUnitProfile"; profile.hideFlags = Flags;
                map = ScriptableObject.CreateInstance<SpellDeploy308MapSO>(); map.name = Prefix + "PresentUnitMap"; map.hideFlags = Flags; map.Rows = rows;
                sheet = ScriptableObject.CreateInstance<SpellPresent308SheetSO>(); sheet.name = Prefix + "PresentUnitSheet"; sheet.hideFlags = Flags;
                burst = new Material(burstShader) { name = Prefix + "PresentUnitBurstMat", hideFlags = Flags };
                profile.Map = map; profile.LayerEnabled = true; profile.BurstMaterial = burst;
                char area = rows.First(r => r.Category == DeployCategory308.AttackArea).Char, single = rows.First(r => r.Category == DeployCategory308.AttackSingle).Char;
                char buff = rows.First(r => r.Category == DeployCategory308.Buff).Char, install = rows.First(r => r.Category == DeployCategory308.ComboInstall).Char;
                char summon = rows.First(r => r.Category == DeployCategory308.Summon).Char;
                profile.EnabledLetters = new string(new[] { area, single, buff, install });
                root = new GameObject(Prefix + "PresentUnit") { hideFlags = Flags };
                host = new Host();
                var stage = root.AddComponent<SpellPresentStage308>();
                stage.Configure(profile, host, null, sheet);
                float now = 100f;
                stage.Step(now, true);
                Vector3 place = new Vector3(0f, 0f, 6f);

                // U1: the switch and the routes
                Check("U1", stage.Ready && stage.Uses(area) && stage.Uses(buff) && !stage.Uses(summon) && sheet.Routes.Length == SpellPresent308SheetSO.DefaultRoutes().Length
                    && sheet.Find(SpellFxRole.Summon, PresentMoment308.Begin, default) == null,
                    "stage ready " + stage.Ready + " | takes the enabled glyphs (" + profile.EnabledLetters + ") and not a glyph that is off (" + summon + "): " + (stage.Uses(area) && !stage.Uses(summon))
                    + " | routes " + sheet.Routes.Length + " | no route for a role no handler begins (Summon): " + (sheet.Find(SpellFxRole.Summon, PresentMoment308.Begin, default) == null));

                // U2: a zone - a burst, then held ground marks when it forms, dry on Expire
                {
                    host.Reset();
                    var plan = new AreaImpactPlan { Shape = AreaShape.Circle, Point = place, Radius = 3f, Delay = .25f };
                    stage.Begin(1, new SpellFxRequest { Letter = area, Role = SpellFxRole.Zone, Element = Element.Wood, Origin = place, FallbackPoint = place, ImpactClock = .25f, Duration = 6.25f, Radius = 3f, Grade01 = .5f, Area = plan });
                    int before = host.Stamps, bursts = stage.CastsStarted;
                    stage.Step(now += .3f, true);
                    int ring = sheet.RingCount(3f) + 1;
                    bool laid = before == 0 && host.Held == ring && stage.HeldNow == ring && host.MaxStampLife <= SpellDeploy308ProfileSO.MaxResidueLife;
                    // forms3 (D9): the zone's ring stroke - not before the zone forms, one body while it lives, ink after its birth cels, melting on Expire
                    InkPresentBody308 ringBody = null;
                    for (int i = 0; i < stage.BodySlots; i++) if (stage.BodyAt(i) != null && stage.BodyAt(i).Busy && stage.BodyAt(i).Form == PresentBody308.ZoneRing) ringBody = stage.BodyAt(i);
                    bool ringLaid = ringBody != null && stage.RingsShown == 1 && ringBody.VertexCount > 0 && ringBody.Dropped == 0 && ringBody.Stats.Bold == sheet.RingArcs(DeployTier308.PC, 3f) && ringBody.GlowEndCel <= profile.GlowCels;
                    stage.Step(now += 1f, true);
                    bool ringInk = ringBody != null && ringBody.Busy && !ringBody.Melting && ringBody.GlowNow == 0f;
                    stage.Cue(1, SpellFxCue.Expire, new SpellFxCueArgs { Point = place, Value = 3f });
                    Check("U2r", ringLaid && ringInk && ringBody.Melting,
                        "zone ring stroke: one body after the zone forms " + ringLaid + " (" + (ringBody != null ? ringBody.Stats.Bold + " arcs, " + ringBody.VertexCount + " vertices, glow ends on cel " + ringBody.GlowEndCel : "none")
                        + "; rings shown " + stage.RingsShown + ") | one second later it is ink (glow 0) and still held " + ringInk + " | Expire: it melts " + (ringBody != null && ringBody.Melting));
                    Check("U2", bursts == 1 && laid && host.Released == 1 && stage.HeldNow == 0 && stage.LiveCount == 0 && stage.Unrouted == 0,
                        "zone: bursts begun " + bursts + ", ground marks before the zone forms " + before + ", held marks after " + host.Held + " (want " + ring + "), longest drying time asked " + F(host.MaxStampLife)
                        + " s | Expire: releases " + host.Released + ", held now " + stage.HeldNow + ", handles " + stage.LiveCount + ", unrouted " + stage.Unrouted);
                }

                // U3: a self buff - one cast cue that is over long before the buff, and an end cue
                {
                    host.Reset();
                    // 2026-10-04: the burst of U2's zone (begun 0.3 s ago) is still playing when this cast begins, so "one burst
                    // begun" is counted against the bursts playing just before the cast (was: ActiveCasts == 1).
                    int bursts = stage.CastsStarted, playing = stage.ActiveCasts;
                    stage.Begin(2, new SpellFxRequest { Letter = buff, Role = SpellFxRole.Aura, Element = Element.Fire, Origin = Vector3.zero, FallbackPoint = Vector3.forward, Duration = 20f, Grade01 = .5f });
                    int started = stage.CastsStarted - bursts, playingAfter = stage.ActiveCasts, told = host.Began;
                    bool began = started == 1 && told >= 1 && playingAfter == playing + 1;
                    stage.Step(now += 3f, true);
                    bool over = stage.ActiveCasts == 0 && stage.LiveCount == 1;
                    stage.Cue(2, SpellFxCue.Expire, default);
                    int drops = host.Air;
                    stage.End(2, false);
                    Check("U3", began && over && drops == sheet.Shed.Drops && host.Held == 0 && stage.LiveCount == 0 && host.MaxAirLife <= SpellPresent308SheetSO.MaxAirSeconds,
                        "buff of 20 s: one burst begun " + began + " (started " + started + ", layer told " + told + ", playing " + playing + " -> " + playingAfter + "), gone after 3 s while the handle lives " + over + " | Expire: " + drops + " drops fall (want " + sheet.Shed.Drops + "), held marks " + host.Held
                        + ", longest air life " + F(host.MaxAirLife) + " s | End: handles " + stage.LiveCount);
                }

                // U4: the confirmed hit no live cast took
                {
                    var enemyObject = new GameObject(Prefix + "PresentUnitEnemy") { hideFlags = Flags };
                    enemyObject.transform.SetParent(root.transform, false); enemyObject.transform.position = place;
                    var enemy = enemyObject.AddComponent<EnemyVitals>();
                    EnemyDamageResult Hit(DamageSource source) => new EnemyDamageResult(enemy, AttackProvenance.Create(root, source, Element.Fire), 5f, 0f, false);
                    host.Reset();
                    stage.NoteConfirmedHit(Hit(DamageSource.PlayerDirect), true);
                    bool tookA = stage.ConfirmedHitLetter(single, true); int airA = host.Air + host.Still;
                    stage.NoteConfirmedHit(Hit(DamageSource.PlayerDirect), false);
                    bool tookB = stage.ConfirmedHitLetter(single, true); int airB = host.Air, stillB = host.Still;
                    host.Reset();
                    stage.NoteConfirmedHit(Hit(DamageSource.Companion), false);
                    bool tookC = stage.ConfirmedHitLetter(buff, false); int airC = host.Air;
                    host.Reset();
                    stage.NoteConfirmedHit(Hit(DamageSource.PlayerDirect), false);
                    bool tookD = stage.ConfirmedHitLetter(summon, false); int airD = host.Air + host.Still;
                    // a detonation inside a hit hook: the inner hit names its glyph first
                    stage.NoteConfirmedHit(Hit(DamageSource.PlayerDirect), false);
                    stage.NoteConfirmedHit(Hit(DamageSource.Harmony), true);
                    stage.ConfirmedHitLetter(install, false); int airInner = host.Air + host.Still;
                    stage.ConfirmedHitLetter(single, true); int airOuter = host.Air;
                    host.Reset();
                    int drips = stage.Drips;
                    stage.NoteConfirmedHit(Hit(DamageSource.PersistentSpell), false); stage.ConfirmedHitLetter(area, true);
                    stage.NoteConfirmedHit(Hit(DamageSource.PersistentSpell), false); stage.ConfirmedHitLetter(area, true);
                    // forms4 (P1): a zone step's drops leave one after another (Splash.DripStagger apart - forms3's fix pass), so
                    // all but the first wait in the stage's delay queue. The unit plays that stagger out before it counts
                    // (it counted at once: 1 of 3 here, and the two late drops then fell into U5's count). The behaviour is
                    // unchanged; nothing else of U4 is counted after this step, and U5 starts with host.Reset().
                    stage.Step(now += Mathf.Max(0f, sheet.Splash.DripStagger) * Mathf.Max(0, sheet.Splash.DripDrops - 1) + .02f, true);
                    Check("U4", tookA && airA == 0 && tookB && airB > 0 && stillB == 2 && tookC && airC > 0 && airC < airB && !tookD && airD == 0 && airInner == 0 && airOuter > 0
                        && stage.Drips == drips + 1 && host.Air == sheet.Splash.DripDrops && host.Still == 0,
                        "a hit a live cast took: nothing more (" + airA + ") | the same hit taken by no cast: " + airB + " drops + " + stillB + " standing (star, blot) | a companion shot of an enabled buff glyph: owned "
                        + tookC + ", " + airC + " drops (smaller grade) | a glyph that is off: owned " + tookD + ", " + airD + " | nested (outer unrouted, inner routed): inner " + airInner + ", outer " + airOuter
                        + " | two zone steps in a row: " + (stage.Drips - drips) + " drip of " + host.Air + " drops (the second waits for its cooldown)");

                    // U5: an installed mark - carried by the enemy, ink after its birth beat, off on the detonation
                    host.Reset(); host.Groggy = true;
                    int bursts = stage.CastsStarted;
                    stage.Begin(3, new SpellFxRequest { Letter = install, Role = SpellFxRole.Mark, Element = Element.Wood, Origin = Vector3.up, FallbackPoint = place, Target = enemyObject.transform, ImpactClock = .3f, Duration = 15.3f, Grade01 = .5f });
                    int thrown = host.Air;
                    stage.Step(now += .3f, true);
                    thrown = Mathf.Max(thrown, host.Air);
                    stage.Cue(3, SpellFxCue.Anchor, new SpellFxCueArgs { Target = enemyObject.transform, Point = place, At = now });
                    InkPresentBody308 mark = null;
                    for (int i = 0; i < stage.BodySlots; i++) if (stage.BodyAt(i) != null && stage.BodyAt(i).Busy) mark = stage.BodyAt(i);
                    bool shown = mark != null && mark.Form == PresentBody308.MarkInstall && mark.VertexCount > 0 && mark.Dropped == 0 && mark.GlowNow > 0f && mark.Carrier == enemyObject.transform;
                    enemyObject.transform.position = place + Vector3.right * 2f;
                    stage.Step(now += 1f, true);
                    bool ink = mark != null && mark.Busy && mark.GlowNow == 0f && (mark.transform.position - enemyObject.transform.position).sqrMagnitude < 1e-6f;
                    stage.Cue(3, SpellFxCue.Detonate, new SpellFxCueArgs { Target = enemyObject.transform, Point = enemyObject.transform.position + Vector3.up * .4f, At = now, Value = 14f });
                    bool melting = mark != null && mark.Melting;
                    stage.End(3, false);
                    stage.Step(now += .3f, true);
                    int asked = host.GroggyAsked, impacts = host.Impacts;
                    stage.Step(now += 1f, true);
                    Check("U5", thrown == sheet.FlingDrops && shown && ink && melting && stage.CastsStarted == bursts + 2 && asked == 1 && impacts == 1 && (mark == null || !mark.Busy) && stage.LiveCount == 0,
                        "install: " + thrown + " drops thrown at the enemy (want " + sheet.FlingDrops + ") | Anchor: a carried mark with glow at its birth " + shown + " | one second later: still carried, glow 0, follows the enemy " + ink
                        + " | Detonate: the mark melts " + melting + ", bursts begun " + (stage.CastsStarted - bursts) + " (the install form and the triggered burst), groggy asked " + asked + ", impact requests on a groggy target "
                        + impacts + " | afterwards the mark is gone " + (mark == null || !mark.Busy) + ", handles " + stage.LiveCount);
                }

                // U6: without a layer every glyph is the interim presenter's
                {
                    var presenter = new SpellDeployPresenter308();
                    presenter.Bind(null, null);
                    var request = new SpellFxRequest { Letter = area, Role = SpellFxRole.Zone, Origin = place, FallbackPoint = place, Radius = 3f };
                    var handle = presenter.Begin(request);
                    presenter.Cue(handle, SpellFxCue.Expire, default); presenter.End(handle); presenter.EndAll();
                    Check("U6", !handle.Shown && presenter.DeployBegun == 0 && !presenter.CanShow(request) && presenter.Fallback.LiveCount == 0,
                        "a presenter without a wiring (no layer): deploy begun " + presenter.DeployBegun + ", the interim presenter without a visual set shows nothing (handle shown " + handle.Shown + "), calls on a dead handle are ignored");
                }

                // U7: EndAll lets go of everything, and nothing is left behind
                {
                    host.Reset();
                    stage.Begin(4, new SpellFxRequest { Letter = area, Role = SpellFxRole.Zone, Origin = place, FallbackPoint = place, Radius = 2f, Duration = 5f });
                    stage.Step(now += .1f, true);
                    int held = stage.HeldNow;
                    stage.EndAll();
                    bool empty = stage.LiveCount == 0 && stage.HeldNow == 0 && stage.ActiveCasts == 0 && stage.ActiveBodies == 0 && host.Released >= 1;
                    stage.ReleaseResources();
                    Object.DestroyImmediate(root); root = null;
                    int left = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && !EditorUtility.IsPersistent(g) && (g.name.StartsWith("SpellPresent308_", StringComparison.Ordinal) || g.name == Prefix + "PresentUnit"));
                    Check("U7", held > 0 && empty && left == 0 && SceneManager.GetActiveScene().isDirty == sceneDirty,
                        "EndAll with " + held + " held marks: handles, held marks, bursts and bodies all 0 " + empty + " | objects left after the cleanup " + left + " | scene dirty flag unchanged " + (SceneManager.GetActiveScene().isDirty == sceneDirty));
                }
            }
            finally
            {
                if (root != null)
                {
                    foreach (var stage in root.GetComponents<SpellPresentStage308>()) stage.ReleaseResources();
                    Object.DestroyImmediate(root);
                }
                if (host != null) host.Dispose();
                foreach (Object o in new Object[] { burst, sheet, map, profile }) if (o != null) Object.DestroyImmediate(o);
            }
            sb.AppendLine("not covered here: the state marks (they read the wiring's own targets) and the director's hit seam - Play");
            sb.Append("present308-unit: " + checks + " checks, " + failed + " failed");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- preview

        static float Num(Dictionary<string, string> opt, string key, float fallback) =>
            opt.TryGetValue(key, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name) { hideFlags = Flags };
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static string PreviewOn(string[] a)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new PostLedger308.Refused("Edit mode only (Play is running)");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new PostLedger308.Refused("the editor is compiling or importing");
            string form = a[2].ToLowerInvariant();
            string[] forms = { "slash", "slashhit", "blade", "install", "carried", "state", "ring", "fling", "shed", "spent", "splash", "drip", "cover", "combo" };
            if (Array.IndexOf(forms, form) < 0) throw new PostLedger308.Refused("form must be one of " + string.Join(" ", forms));
            var opt = PostLedger308.Options(a.Skip(3));
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty && !opt.ContainsKey("allowdirty")) throw new PostLedger308.Refused("the scene has unsaved changes that are not from this tool (add :allowdirty to preview anyway)");
            var profile = AssetDatabase.LoadAssetAtPath<SpellDeploy308ProfileSO>(Deploy308.ProfilePath);
            if (profile == null || profile.BurstMaterial == null || profile.Atlas == null) throw new PostLedger308.Refused("the layer's profile, burst material or atlas is missing (run deploy308-assets)");
            Shader residueShader = Deploy308.RequireShader(Deploy308.ResidueShader);
            Deploy308.RequireShader(Deploy308.BurstShader);
            var sheetAsset = AssetDatabase.LoadAssetAtPath<SpellPresent308SheetSO>(SheetPath);
            string removed = DeployLook308.Run("preview:off");

            var sheet = sheetAsset != null ? Object.Instantiate(sheetAsset) : ScriptableObject.CreateInstance<SpellPresent308SheetSO>();
            sheet.name = Prefix + "PresentSheet"; sheet.hideFlags = Flags;
            var residue = new Material(residueShader) { name = Prefix + "PresentResidue", hideFlags = Flags };
            Deploy308.ApplyResidueMaterial(residue, profile.Atlas);
            residue.enableInstancing = false;      // the preview draws one renderer per mark

            var view = SceneView.lastActiveSceneView;
            Vector3 at = view != null ? view.pivot : Vector3.zero;
            if (opt.TryGetValue("at", out var atText))
            {
                var p = atText.Split(',');
                if (p.Length != 3 || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out at.x) || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out at.y)
                    || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out at.z)) throw new PostLedger308.Refused("at must be x,y,z");
            }
            Vector3 forward = Quaternion.Euler(0f, Num(opt, "yaw", view != null ? view.rotation.eulerAngles.y : 0f), 0f) * Vector3.forward;
            Vector3 ground = Physics.Raycast(at + Vector3.up * 40f, Vector3.down, out var hit, 120f, profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= profile.Residue.MinNormalY ? hit.point : at;
            Vector3 eye = ground + Vector3.up * EyeHeight;
            bool cover = form == "cover", combo = form == "combo";
            float viewDistance = Mathf.Clamp(Num(opt, "view", 4f), .8f, 40f), t = Mathf.Max(0f, Num(opt, "t", cover ? 3f : combo ? -1f : .05f));
            var tier = opt.TryGetValue("tier", out var tierText) && tierText.Equals("mobile", StringComparison.OrdinalIgnoreCase) ? DeployTier308.Mobile : DeployTier308.PC;
            // forms3 (D11): cover / combo draw a real burst of the layer: the glyph's own map row, as the stage hands it over
            SpellDeploy308MapSO.Row row = default;
            if (cover || combo)
            {
                if (profile.Map == null) throw new PostLedger308.Refused("the layer's profile has no map (run deploy308-map)");
                var want = cover ? DeployCategory308.Ward : DeployCategory308.ComboInstall;
                char glyph = opt.TryGetValue("letter", out var letterText) && letterText.Length == 1 ? letterText[0] : profile.Map.Rows.Where(r => r.Category == want).Select(r => r.Char).FirstOrDefault();
                if (!profile.Map.TryGet(glyph, out row)) throw new PostLedger308.Refused("no map row for :letter (give one of the 120 letters)");
            }
            int grade = Mathf.Clamp((int)Num(opt, "grade", 1f), 0, 2);
            Element element = cover || combo ? row.Element : Element.Fire;
            if (opt.TryGetValue("element", out var elementText) && !Enum.TryParse(elementText, true, out element)) throw new PostLedger308.Refused("element must be wood, fire, earth, metal or water");
            Color tint = sheet.Tint((int)element);

            var root = Make(RootName, null);
            var eyeObject = Make(EyeName, root.transform);
            eyeObject.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, Vector3.up));
            var camera = eyeObject.AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = Num(opt, "fov", 60f); camera.nearClipPlane = .05f; camera.farClipPlane = 2000f;
            eyeObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            // the enemy the form is shown on: an empty transform `view` metres ahead (no renderer is added to the scene)
            var enemy = Make("Present_Enemy", root.transform);
            enemy.transform.position = ground + forward * viewDistance;
            Vector3 chest = enemy.transform.position + Vector3.up * InkDeployRuntime308.ChestHeight;
            var log = new StringBuilder("preview on: " + form + " | element " + element + ", grade " + grade + ", " + F(viewDistance) + " m ahead, t = " + (t < 0f ? "the burst moment" : F(t) + " s") + " | eye " + eye + "\n");
            if (!removed.StartsWith("preview: none", StringComparison.Ordinal)) log.AppendLine("replaced an earlier preview: " + removed);
            // forms4 (P3 / P10): :dummy=<m>[:dummyh=<m>][:dummyw=<m>] - a plain box <m> ahead as a body stand-in (no collider, a child
            // of the preview root: preview:off removes it), as DeployLook308 draws its stand-in: what does the form HIDE of a body?
            float dummyAt = Num(opt, "dummy", 0f);
            if (dummyAt > 0f)
            {
                float dummyHigh = Mathf.Clamp(Num(opt, "dummyh", 1.7f), .2f, 4f), dummyWide = Mathf.Clamp(Num(opt, "dummyw", .5f), .1f, 4f);
                var standIn = GameObject.CreatePrimitive(PrimitiveType.Cube);
                standIn.name = Prefix + "Present_StandIn"; standIn.hideFlags = Flags;
                var standInCollider = standIn.GetComponent<Collider>();
                if (standInCollider != null) Object.DestroyImmediate(standInCollider);
                standIn.transform.SetParent(root.transform, false);
                standIn.transform.SetPositionAndRotation(ground + forward * dummyAt + Vector3.up * (dummyHigh * .5f), Quaternion.LookRotation(forward, Vector3.up));
                standIn.transform.localScale = new Vector3(dummyWide, dummyHigh, .35f);
                log.AppendLine("stand-in " + F(dummyWide) + " x " + F(dummyHigh) + " m at " + F(dummyAt) + " m");
            }

            int marks = 0;
            Mesh quad = null;
            void Mark(string name, Matrix4x4 pose, Vector4 stampA, Vector4 stampB)
            {
                if (quad == null) quad = InkResidueField308.BuildQuad();
                var go = Make(name, root.transform);
                go.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
                go.transform.localScale = pose.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = residue; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
                var block = new MaterialPropertyBlock();
                block.SetVector("_StampA", stampA); block.SetVector("_StampB", stampB); block.SetVector("_StampC", new Vector4(0f, 0f, 1f, 1f));
                renderer.SetPropertyBlock(block);
                marks++;
            }

            if (form == "slash" || form == "slashhit" || form == "blade" || form == "install" || form == "carried" || form == "state")
            {
                var input = new PresentBodyInput308 { Profile = profile, Sheet = sheet, Element = element, Tinted = form != "state", Grade = grade, Seed = (int)Num(opt, "seed", 308f), Reach = viewDistance,
                    Height = InkDeployRuntime308.ChestHeight, StandOff = sheet.Mark.TowardEye };
                bool carried = false;
                switch (form)
                {
                    case "slash": input.Body = PresentBody308.Slash; break;
                    case "slashhit": input.Body = PresentBody308.Slash; input.Hit = true; break;
                    case "blade": input.Body = PresentBody308.Blade; break;
                    case "install": input.Body = PresentBody308.MarkInstall; carried = true; break;
                    case "carried": input.Body = PresentBody308.MarkCarried; carried = true; break;
                    default:
                        input.Body = PresentBody308.MarkState; carried = true;
                        string names = opt.TryGetValue("states", out var statesText) ? statesText : "bound";
                        foreach (string name in names.Split(','))
                        {
                            if (!Enum.TryParse(name.Trim(), true, out PresentState308 state) || state == PresentState308.None) throw new PostLedger308.Refused("states must be a comma list of bound, slowed, exposed, weakened");
                            input.States |= state;
                        }
                        break;
                }
                var holder = Make("Present_Body", root.transform);
                var body = holder.AddComponent<InkPresentBody308>();
                body.Prepare(profile, sheet, DeployTier308.PC, Prefix + "PresentBody");
                float hold = carried ? -1f : SpellPresentRules308.BodyHold(sheet, profile, input.Body == PresentBody308.Blade ? sheet.Sword.BladeHold : sheet.Sword.SlashHold);
                Vector3 place = carried ? enemy.transform.position : eye;
                Vector3 toward = carried ? enemy.transform.position - eye : chest - eye;
                if (!body.Show(input, 0, carried ? enemy.transform : null, place, toward, hold, tint, 0f)) throw new PostLedger308.Refused("the body has nothing to draw");
                body.Sample(t, eye);
                log.AppendLine("body: " + input.Body + (input.Body == PresentBody308.MarkState ? " (" + input.States + ")" : "") + " | " + body.Stats.Primitives + " primitives (" + body.Stats.Bold + " bold, " + body.Stats.Fine
                    + " dry, " + body.Stats.Needles + " needles, " + body.Stats.Sprites + " sprites), " + body.VertexCount + " vertices | cel " + F(body.CelSeconds) + " s, glow ends on cel " + body.GlowEndCel
                    + ", glow amount at t " + F(body.GlowNow) + (body.Busy ? "" : " | at t the body has melted away"));
            }
            else
            {
                // the pattern forms: the ops of the pure rules, each air drop moved to where it is at t, each ground mark as one renderer
                var ops = new PresentOp308[SpellPresent308SheetSO.MaxOps];
                var e = new PresentEvent308
                {
                    Element = element, Tier = tier, Grade = grade, Seed = (int)Num(opt, "seed", 308f), Origin = eye + forward * 1.2f - Vector3.up * .3f, FallbackPoint = enemy.transform.position, TargetPoint = chest,
                    HasTarget = true, ImpactClock = .4f, Duration = 8f, Radius = Mathf.Max(.2f, Num(opt, "radius", cover ? 1.2f : 3f)), Point = chest, Normal = forward, CueHasTarget = true, CueTargetPoint = chest + Quaternion.Euler(0f, 60f, 0f) * forward * 3f,
                    At = .4f, Now = 0f, Value = 3f, Eye = eye, EyeForward = forward, CasterFeet = ground, GroundY = enemy.transform.position.y, HitScratch = new DeployDrop308[InkDeployForms308.MaxHitDrops],
                };
                int n = 0;
                switch (form)
                {
                    case "ring": e.Role = SpellFxRole.Zone; e.Moment = PresentMoment308.Begin; e.MapCategory = DeployCategory308.AttackArea; e.ImpactClock = 0f; break;
                    case "fling": e.Role = SpellFxRole.Projectile; e.Moment = PresentMoment308.Cue; e.Cue = SpellFxCue.Split; break;
                    case "shed": e.Role = SpellFxRole.Aura; e.Moment = PresentMoment308.Cue; e.Cue = SpellFxCue.Expire; break;
                    case "spent": e.Role = SpellFxRole.Aura; e.Moment = PresentMoment308.Cue; e.Cue = SpellFxCue.Consume; break;
                    case "splash": e.Role = SpellFxRole.Field; e.Moment = PresentMoment308.Cue; e.Cue = SpellFxCue.Hit; break;
                    // forms3 (D11): a cover's wall stands where the request puts it; a detonation's cue point is the enemy's root + .4 m (the unit check's)
                    case "cover": e.Role = SpellFxRole.Ward; e.Moment = PresentMoment308.Begin; e.MapCategory = row.Category; e.Origin = enemy.transform.position; e.ImpactClock = 0f; e.HasTarget = false;
                        e.Duration = Mathf.Max(1f, Num(opt, "life", 6f)); break;
                    case "combo": e.Role = SpellFxRole.Mark; e.Moment = PresentMoment308.Cue; e.Cue = SpellFxCue.Detonate; e.MapCategory = row.Category; e.Point = enemy.transform.position + Vector3.up * .4f;
                        e.CueTargetPoint = chest; e.Value = 14f; break;
                }
                if (form == "drip") SpellPresentRules308.DripAt(ops, ref n, sheet, profile, chest, e.GroundY, element, e.Seed);
                else n = SpellPresentRules308.Plan(sheet, profile, e, ops).Count;
                int air = 0, landed = 0, ground2 = 0, waiting = 0, gone = 0, rings = 0, bursts = 0;
                for (int i = 0; i < n; i++)
                {
                    var op = ops[i];
                    float age = t - op.Delay;
                    if (op.Kind == PresentOpKind308.Body && op.Body == PresentBody308.ZoneRing)
                    {
                        // forms3 (D9): the zone's ring stroke, laid on the ground as SpellPresentStage308.ShowRing lays it
                        if (age < 0f) { waiting++; continue; }
                        float radius = Mathf.Max(sheet.Ring.MinRadius, op.Reach);
                        float Height(Vector3 p) => Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var h, 14f, profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && h.normal.y >= profile.Residue.MinNormalY ? h.point.y : ground.y;
                        Vector3 centre = op.Point;
                        float east = Height(centre + Vector3.right * radius), west = Height(centre - Vector3.right * radius), north = Height(centre + Vector3.forward * radius), south = Height(centre - Vector3.forward * radius);
                        Vector3 up = new Vector3((west - east) / (2f * radius), 1f, (south - north) / (2f * radius)).normalized;
                        if (up.y < profile.Residue.MinNormalY) up = Vector3.up;
                        centre.y = (east + west + north + south) * .25f;
                        var ringBody = Make("Present_Ring" + i, root.transform).AddComponent<InkPresentBody308>();
                        ringBody.Prepare(profile, sheet, tier, Prefix + "PresentRing" + i);
                        var ringInput = new PresentBodyInput308 { Profile = profile, Sheet = sheet, Body = PresentBody308.ZoneRing, Element = element, Tinted = true, Grade = grade, Seed = (int)Num(opt, "seed", 308f), Radius = radius, Tier = tier };
                        if (!ringBody.Show(ringInput, 0, null, centre, Vector3.forward, sheet.RingSeconds, tint, 0f, up)) throw new PostLedger308.Refused("the ring stroke has nothing to draw");
                        ringBody.Sample(age, eye);
                        rings++;
                        log.AppendLine("ring stroke: radius " + F(radius) + " m, " + ringBody.Stats.Bold + " arcs, " + ringBody.VertexCount + " vertices (" + tier + ") | ground normal " + up + ", centre height " + F(centre.y)
                            + " | glow ends on cel " + ringBody.GlowEndCel + ", glow amount at t " + F(ringBody.GlowNow) + " | held until the handle lets go (at most " + F(sheet.RingSeconds) + " s)");
                        continue;
                    }
                    if (op.Kind == PresentOpKind308.Cast)
                    {
                        if (!cover && !combo) continue;      // a zone's own burst is the glyph's attack form (DeployLook308 shows those)
                        // forms3 (D11): the burst as SpellPresentStage308.StartCast hands it to the layer
                        var castRow = row;
                        if (castRow.Category != op.Category) { castRow.Category = op.Category; castRow.ImpactFrame = false; castRow.ImpactOnTrigger = false; }
                        Vector3 fallback = op.Point; Vector3 gap = fallback - op.Origin; gap.y = 0f;
                        Transform target = op.Target == PresentRef308.CueTarget ? enemy.transform : null;
                        if (target == null && (gap.sqrMagnitude < .01f || Vector3.Dot(gap, forward) <= 0f)) fallback = op.Origin + forward;
                        var holder = Make("Present_Cast" + i, root.transform);
                        holder.transform.SetPositionAndRotation(op.Origin, Quaternion.LookRotation(forward, Vector3.up));
                        var runtime = holder.AddComponent<InkDeployRuntime308>();
                        var cast = new DeployCast308
                        {
                            Profile = profile, Row = castRow, Origin = op.Origin, Target = target, FallbackPoint = fallback, ImpactClock = 0f, Grade01 = grade * .5f, Tint = tint, HoldSeconds = op.HoldSeconds, WardRadius = op.WardRadius,
                            Triggered = op.Triggered, Tier = tier, Seed = (int)Num(opt, "seed", 308f), CameraPosition = eye, HasCameraPosition = true,
                            Cover = op.HoldSeconds > 0f && op.Category == DeployCategory308.Ward,   // as the stage hands it over (forms3 fix pass)
                        };
                        if (!runtime.Configure(cast, null)) throw new PostLedger308.Refused("the runtime refused the cast");
                        float cel = runtime.CelSeconds;
                        // default moment of a triggered burst: the cel after its last stroke is born (DeployLook308's "burst" beat)
                        if (t < 0f) t = (Mathf.Max(runtime.Stats.LastBirthCel, runtime.ImpactCel) + .5f) * cel;
                        runtime.Sample(t);
                        int castMarks = 0;
                        for (int k = 0; k < runtime.DropCount; k++)
                        {
                            var drop = runtime.Drop(k);
                            if (drop.Air || drop.Cel > runtime.CurrentCel) continue;
                            Vector3 world = runtime.Root.TransformPoint(drop.Local); Vector3 point = new Vector3(world.x, ground.y, world.z), normal = Vector3.up;
                            if (Physics.Raycast(world + Vector3.up * 1.1f, Vector3.down, out var under, 6f, profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && under.normal.y >= profile.Residue.MinNormalY) { point = under.point; normal = under.normal; }
                            Mark("Present_CastMark" + i + "_" + k, InkResidueField308.Place(point, normal, forward, drop.Size, drop.Size, profile.Residue.Lift), new Vector4(0f, profile.SpellLife, drop.Cell, profile.Residue.Opacity), Vector4.zero);
                            castMarks++;
                        }
                        bursts++;
                        var s = runtime.Stats;
                        log.AppendLine("burst: " + castRow.Letter + " as " + castRow.Category + (op.Triggered ? " (triggered)" : "") + (op.HoldSeconds > 0f ? " standing " + F(op.HoldSeconds) + " s, radius " + F(op.WardRadius) + " m" : "")
                            + " | t = " + F(t) + " s (cel " + runtime.CurrentCel + ") | bold " + s.Bold + ", fine " + s.Fine + ", needles " + s.Needles + ", sprites " + s.Sprites + ", column strips " + s.ColumnStrips + " | vertices " + runtime.VertexCount
                            + (runtime.DroppedPrimitives > 0 ? " (" + runtime.DroppedPrimitives + " primitives did not fit)" : "") + " | est. screen cover " + F(s.ScreenShare * 100f) + " % (limit 35) | glow amount at t " + F(runtime.GlowNow)
                            + ", glow ends at " + F(runtime.GlowEnd) + " s | hold ends " + F(runtime.HoldEnd) + " s, melt ends " + F(runtime.MeltEnd) + " s | ground marks of the burst " + castMarks + " (its air drops fall only in the game)");
                        continue;
                    }
                    if (t < 0f) continue;      // (combo before its burst has named the moment: nothing else is drawn)
                    if (op.Kind == PresentOpKind308.Stamp)
                    {
                        if (age < 0f) { waiting++; continue; }
                        Vector3 point = new Vector3(op.Point.x, ground.y, op.Point.z); Vector3 normal = Vector3.up;
                        if (Physics.Raycast(op.Point + Vector3.up * .6f, Vector3.down, out var under, 6f, profile.Residue.GroundLayers, QueryTriggerInteraction.Ignore) && under.normal.y >= profile.Residue.MinNormalY) { point = under.point; normal = under.normal; }
                        Mark("Present_Mark" + i, InkResidueField308.Place(point, normal, op.Forward, op.Size, op.Size, profile.Residue.Lift), new Vector4(0f, op.Life, op.Cell, op.Opacity), Vector4.zero);
                        ground2++;
                    }
                    else if (op.Kind == PresentOpKind308.Air)
                    {
                        if (age < 0f) { waiting++; continue; }
                        if (age > op.Life) { gone++; continue; }
                        Vector3 where = op.Point + op.Velocity * age + Vector3.down * (.5f * profile.Residue.Gravity * op.Gravity * age * age);
                        if (op.Gravity > 0f && where.y <= op.LandY)
                        {
                            Mark("Present_Landed" + i, InkResidueField308.Place(new Vector3(where.x, op.LandY, where.z), Vector3.up, forward, op.Size * 1.6f, op.Size * 1.6f, profile.Residue.Lift),
                                new Vector4(0f, profile.SpellLife, InkBurstMeshBuilder308.CellDrop, profile.Residue.Opacity), Vector4.zero);
                            landed++; continue;
                        }
                        // forms3 (D10): a tailed drop lies along the way it travels, as the residue field draws it
                        var airPose = InkResidueField308.AirPose(profile.AirTail, where, op.Velocity + Vector3.down * (profile.Residue.Gravity * op.Gravity * age),
                            !op.Floor ? op.Size : sheet.AirSize(op.Size, Mathf.Max((op.Point - eye).magnitude, (op.Point + op.Velocity * op.Life - eye).magnitude), 0f, profile.AirTail.Tailed(op.Cell) ? profile.AirTail.Width : 1f), op.Cell, out bool tailed, op.TailMax);   // forms4 (P8): the stage's least on-screen size; pass 4b (Q6): the thrown head's tail ceiling
                        Mark("Present_Air" + i, airPose, new Vector4(0f, op.Life, op.Cell, 1f), new Vector4(0f, 0f, 1f, tailed ? 1f : 0f));
                        air++;
                    }
                }
                Shader.SetGlobalFloat("_OhResidueNow", Mathf.Max(0f, Num(opt, "age", 0f)));
                log.AppendLine("pattern: " + n + " ops at t = " + F(t) + " s -> ground marks " + ground2 + ", ring strokes " + rings + ", bursts " + bursts + ", drops in the air " + air + ", drops that have landed " + landed + ", not yet begun " + waiting + ", already gone " + gone
                    + " | every mark is one renderer here (the game draws them instanced); :age=<s> dries the ground marks");
            }
            Make("State|q=" + QualitySettings.GetQualityLevel() + "|present308:" + form + "|dirtyAtOn=" + scene.isDirty + "|utc=" + PostLedger308.Utc(), root.transform);
            SceneView.RepaintAll();
            log.Append("objects " + root.GetComponentsInChildren<Transform>(true).Length + " (root '" + RootName + "', eye camera '" + EyeName + "') | remove with preview:off (this command or DeployLook308's)");
            return log.ToString();
        }
    }
}
