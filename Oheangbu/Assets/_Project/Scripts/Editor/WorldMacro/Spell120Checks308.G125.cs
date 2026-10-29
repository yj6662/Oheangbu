using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.World;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 G125 fixtures: 낭 · 순 against the main world's own guarding field enemies (the S125 chain: WorldContent_Main's
    // GuardBrains, the session's brain clone, PrologueEncounter.Guard308 on the body's own stance), 눗 · 숭 against its own
    // stone-throwers (the R125 profile, MineRangedNeutral308). Content, brain and profile are loaded and never written.
    //  낭: the hit goes past the actor's raised guard whole and breaks it; the actor's own Guard308 cannot raise it again
    //  before guard.lock runs out (a plain shot lands whole), and raises it again after (a plain shot loses the share).
    //  순: inside the circle the actor's Guard308 raises no new guard (a plain shot lands whole); a guard raised before the
    //  circle reached it stays up; out of the circle or at its end the guard rises again.
    //  눗 · 숭: the real stone in the air over the circle is taken (the throw ends as a cancel, no damage); a stone over open
    //  ground, a stone thrown from inside the circle and a stone after the cap or the circle's end fly on and hurt. 숭 sends
    //  the taken stone's power x domain.reflect back on its thrower (Retaliation, no weak point).
    // A fixture never calls Awake, so a thrower is handed a stone object of the fixture's own (the stone a Play controller
    // makes in Awake); Edit Mode's Time.time does not move, so a flight is set to a fraction of its way by its impact time.
    // Every cast is on the main game's proof alone (Gate final, no TEST unlock). Left to Play: the brain reaching its Wait
    // phase in the circle, the flight and the look of the guard, the circle and the burnt or turned stone.
    public static partial class Spell120Checks308
    {
        static readonly string[] G125Sheets = { "Rules308_Base", "Rules308_WP00", "Rules308_WP11", "Rules308_WP15", "Rules308_WP17" };

        static partial void FixturesG125(Report r)
        {
            // grid cells by index (no glyph is named): 30 fire a + ieung, 93 metal u + nieun, 47 fire u + siot, 96 metal u + ieung,
            // 73 = the metal single shot without a final
            char breaker = SpellGrammar308.LetterAt(30), unguard = SpellGrammar308.LetterAt(93), burn = SpellGrammar308.LetterAt(47),
                turn = SpellGrammar308.LetterAt(96), plain = SpellGrammar308.LetterAt(73);
            if (!r.Wants(new string(new[] { breaker, unguard, burn, turn }))) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var breakFx = effects.OfType<GuardBreakEffect308>().FirstOrDefault(); var unguardFx = effects.OfType<DomainUnguardEffect308>().FirstOrDefault();
            var burnFx = effects.OfType<DomainBurnShotEffect308>().FirstOrDefault(); var turnFx = effects.OfType<DomainReflectShotEffect308>().FirstOrDefault();
            if (!r.Check("G125", breakFx != null && unguardFx != null && burnFx != null && turnFx != null, "the four handlers are registered")) return;
            var content = AssetDatabase.LoadAssetAtPath<WorldMacroPlaytestSO>(MainContent);
            var brain = content != null ? content.EnemyBrain : null;
            if (!r.Check("G125", content != null && brain != null && brain.Usable && brain.GuardShare == 0f && content.Encounters != null && content.GuardBrains != null,
                "the main world's content loads and hands out a usable brain that does not guard on its own: " + MainContent)) return;
            var stone = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(StoneThrow);
            if (!r.Check("G125", stone != null && stone.TryValidate(out _) && stone.Delivery == EnemyAttackDelivery.HomingProjectile && !stone.Elemental && stone.Damage > 0f && stone.Range > 0f,
                "the main world's stone-throw loads (a plain projectile): " + StoneThrow)) return;

            // the guarded kinds (an entry with a share and a front, and at least one encounter of that kind), as in S125
            var kinds = content.GuardBrains.Where(e => e != null && e.GuardShare > 0f && e.GuardFacingDegrees >= 1f)
                .Select(e => (entry: e, encounter: Array.Find(content.Encounters, x => x != null && x.ContentId == e.ContentId)))
                .Where(k => k.encounter != null).ToArray();
            var guardedIds = new HashSet<string>(kinds.Select(k => k.entry.ContentId));
            var guardedEncounters = content.Encounters.Where(x => x != null && guardedIds.Contains(x.ContentId)).ToArray();
            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string text = File.Exists(world) ? File.ReadAllText(world) : "";
            string contentGuid = AssetDatabase.AssetPathToGUID(MainContent), stoneGuid = AssetDatabase.AssetPathToGUID(StoneThrow);
            bool read = text.Length > 0 && !string.IsNullOrEmpty(contentGuid) && text.Contains("Content: {fileID: 11400000, guid: " + contentGuid);
            int standing = guardedEncounters.Count(x => Regex.IsMatch(text, "\\n  Id: " + Regex.Escape(x.Id) + "\\r?\\n"));
            int armed = text.Length > 0 && !string.IsNullOrEmpty(stoneGuid) ? Regex.Matches(text, "_attackProfile: \\{fileID: 11400000, guid: " + stoneGuid).Count : 0;
            text = null;
            r.Check("G125", kinds.Length >= 3 && read && standing >= 3,
                "낭 · 순's consumers: " + kinds.Length + " guarded kinds (" + string.Join(", ", kinds.Select(k => k.entry.ContentId)) + "), " + standing + " of their "
                + guardedEncounters.Length + " encounters stand as actors in " + MainWorld + ", whose session reads " + MainContent);
            r.Check("G125", armed >= 3, "눗 · 숭's consumers: the main world arms the stone-throw on " + armed + " bodies in " + MainWorld);
            string contentJson = JsonUtility.ToJson(content), brainJson = JsonUtility.ToJson(brain), stoneJson = JsonUtility.ToJson(stone);
            bool contentDirty = EditorUtility.IsDirty(content), brainDirty = EditorUtility.IsDirty(brain), stoneDirty = EditorUtility.IsDirty(stone);
            EnemyBrainProfile308[] made = Array.Empty<EnemyBrainProfile308>();

            using (var f = NewFixture(r, effects, G125Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                f.Finals.Proven.Add(SpellFinal.Ieung); f.Finals.Proven.Add(SpellFinal.Nieun); f.Finals.Proven.Add(SpellFinal.Siot);   // the main-game proof, no TEST unlock
                float brush = f.Brush();
                Vector3 origin = SpellFixture308.Origin, forward = Vector3.forward;
                var everyone = f.Inside.Concat(f.Outside).Concat(new[] { f.Boss }).ToArray();
                var home = everyone.Select(e => e.transform.position).ToArray();
                void Sync()
                {
                    Physics.SyncTransforms();
                    var physics = f.Scene.GetPhysicsScene();
                    if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                    physics.Simulate(.001f);
                }
                void Reset()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = home[i];
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.RestoreAll(); f.Aim(null);
                    f.Player.transform.position = origin;
                    Sync();
                }
                void SendAway()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = origin + new Vector3(300f + i * 10f, 0f, 300f);
                    Sync();
                }
                void Tick(ISpellEffect effect, float clock) => effect.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));
                T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

                var body = f.Inside[0];
                PrologueEncounter actor = null; EnemyController controller = null, thrower = null; NavMeshAgent agent = null;
                List<EnemyBrainProfile308> clones = null;
                GameObject stoneBody = null;
                int impacts = 0, ended = 0; bool cancelled = false; EnemyAttackImpact impact = default;
                Action<EnemyAttackImpact> onImpact = e => { impacts++; impact = e; };
                Action<AttackProvenance, bool> onEnded = (_, off) => { ended++; cancelled = off; };
                try
                {
                    var rows = new[] { breaker, unguard, burn, turn }.Select(x => f.Resolver.TryRow(x, out var row) ? row : null).ToArray();
                    if (!r.Check("G125", rows.All(x => x != null && x.Gate == SpellGateMode.Final),
                        "the four rows are Gate final (D308-125 G125: the main world's guarding field enemies and stone-throwers are their target)")) return;
                    Reset();

                    // the real actor on the fixture's own body (S125); Awake never runs in Edit Mode, so its parts are handed over by hand
                    body.gameObject.SetActive(false);
                    actor = body.gameObject.AddComponent<PrologueEncounter>();             // brings EnemyController and NavMeshAgent
                    agent = body.GetComponent<NavMeshAgent>(); if (agent != null) agent.enabled = false;
                    controller = body.GetComponent<EnemyController>();
                    body.gameObject.SetActive(true);
                    Physics.SyncTransforms();
                    SpellFixture308.Set(controller, "_config", f.Config); SpellFixture308.Set(controller, "_vitals", body);
                    SpellFixture308.Set(controller, "_player", f.Player.transform); SpellFixture308.Set(controller, "_playerVitals", f.Player);
                    controller.Init(new ParryJudge(f.Config));
                    SpellFixture308.Set(actor, "vitals", body);
                    // the real session on an inactive host (its OnDisable saves): only its guard step is called
                    var sessionHost = f.Obj("Spell308_G125_Session", origin + new Vector3(0f, -50f, -300f));
                    sessionHost.SetActive(false);
                    var session = sessionHost.AddComponent<WorldMacroPlaytestSession>();
                    session.Content = content; session.Actors = new[] { actor };
                    clones = (List<EnemyBrainProfile308>)typeof(WorldMacroPlaytestSession).GetField("guardBrainClones308", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);

                    // the real stone-thrower on another body, with a stone object of the fixture's own
                    thrower = f.Inside[1].gameObject.AddComponent<EnemyController>();
                    SpellFixture308.Set(thrower, "_config", f.Config); SpellFixture308.Set(thrower, "_vitals", f.Inside[1]);
                    SpellFixture308.Set(thrower, "_player", f.Player.transform); SpellFixture308.Set(thrower, "_playerVitals", f.Player);
                    thrower.Init(new ParryJudge(f.Config));
                    thrower.AttackImpactResolved += onImpact; thrower.AttackPresentationEnded += onEnded;
                    thrower.Configure(stone);
                    stoneBody = f.Obj("Spell308_G125_Stone", origin + new Vector3(0f, -50f, -320f));
                    stoneBody.SetActive(false);
                    SpellFixture308.Set(thrower, "_projectile", stoneBody.transform);

                    float planned = 0f;
                    float Shot(char letter)                                                 // HP taken by one cast at the actor's body
                    {
                        f.ClearLog(); f.Ink.Restore(); f.Aim(body); float hp = body.Hp; f.Cast(letter);
                        planned = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 ? f.Plans[0].Hits[0].Power : -1f;
                        f.Land();
                        return f.Hits.Count == 1 ? hp - body.Hp : -1f;
                    }
                    bool Guard(bool up, float now) { SpellFixture308.Call(actor, "Guard308", up, now); return body.Modifiers.Stance.Raised; }
                    float Arm((WorldMacroPlaytestSO.GuardBrain308 entry, WorldMacroPlaytestSO.Encounter encounter) k)  // the session's clone for this kind; its share (-1 = none)
                    {
                        actor.Id = k.encounter.Id; actor.Brain = brain;
                        int guarded = (int)SpellFixture308.Call(session, "ApplyGuardBrains308");
                        var clone = actor.Brain;
                        bool cloned = guarded == 1 && clone != null && clone != brain && clones.Contains(clone) && Near(clone.GuardShare, Mathf.Clamp01(k.entry.GuardShare));
                        return cloned ? clone.GuardShare : -1f;
                    }
                    void Put(EnemyController c, Vector3 at)
                    {
                        c.transform.position = at;
                        Vector3 to = Vector3.ProjectOnPlane(f.Player.transform.position - at, Vector3.up);
                        if (to.sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(to.normalized);
                        Sync();
                    }
                    // the telegraph runs out and the stone leaves the hand (R125)
                    bool Throw(EnemyController c)
                    {
                        f.Player.Restore();
                        c.AttackEnabled = true;
                        SpellFixture308.Call(c, "BeginAuthoredTelegraph");
                        SpellFixture308.Set(c, "_stateUntil", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                        return c.IsProjectileFlying && stoneBody.activeSelf;
                    }
                    // the stone this fraction of its way to the player (its impact time set so that the controller's own flight step puts it there)
                    void Advance(EnemyController c, float part)
                    {
                        SpellFixture308.Set(c, "_impactTime", Time.time + (1f - part) * Get<float>(c, "_authoredFlightDuration"));
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                    }
                    void Lands(EnemyController c)
                    {
                        SpellFixture308.Set(c, "_impactTime", Time.time - 1f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                    }
                    bool Hurt(int impacts0) => impacts == impacts0 + 1 && impact.InShape && impact.AppliedDamage > 0f && impact.AppliedDamage <= stone.Damage + .001f && f.Player.Hp01 < 1f;
                    SpellZone308 Zone(string owner) => unguardFx.Zones.Zones.FirstOrDefault(z => z.Owner == owner);
                    bool Placed(DomainEffect308 fx, string owner, SpellRow row, out SpellZone308 zone)
                    {
                        zone = Zone(owner);
                        return f.Accepted.Count == 1 && f.Misfires.Count == 0 && fx.Zones.CountOf(owner) == 1 && (zone.Centre - origin).magnitude < .01f &&
                            Near(zone.Radius, row.F("domain.radius", -1f)) && Near(zone.Until - zone.From, row.F("domain.life", -1f)) && zone.From <= Time.time && Time.time < zone.Until;
                    }

                    // ---- 낭: the actor's raised guard is passed and broken ----
                    if (r.Wants(breaker.ToString()))
                    {
                        var row = rows[0]; float lockFor = row.F("guard.lock", -1f);
                        foreach (var k in kinds)
                        {
                            string name = k.entry.ContentId + " (" + k.encounter.Id + ")";
                            float share = Arm(k);
                            body.Restore();
                            bool up = share > 0f && Guard(true, Time.time) && Near(body.Modifiers.Stance.Share, share);
                            f.ClearLog(); f.Ink.Restore(); f.Aim(body); float hp = body.Hp; f.Cast(breaker);
                            bool told = f.Plans.Count == 1 && f.Plans[0].Hits.Count == 1 && body.Modifiers.Stance.Passes(f.Plans[0].Hits[0].AttackId);
                            float whole = told ? f.Plans[0].Hits[0].Power : -1f;
                            f.Land();
                            float until = body.Modifiers.Stance.BreakUntil;
                            r.Check("낭-1", up && told && whole > 0f && Near(whole, row.BasePower * brush) && Near(hp - body.Hp, whole) && f.Hits.Count == 1 && breakFx.HitsOnTheWay == 0,
                                name + ": the hit goes past the actor's raised guard (" + share + ") whole [" + (hp - body.Hp) + " of " + whole + "]");
                            r.Check("낭-2", !body.Modifiers.Stance.Raised && body.Modifiers.Stance.Broken && Near(until - Time.time, lockFor, .05f),
                                name + ": the guard drops and is locked down for guard.lock (" + lockFor + " s)");
                            bool held = !Guard(true, Time.time) && body.Modifiers.Stance.Broken;
                            float open = Shot(plain);
                            r.Check("낭-2", held && planned > 0f && Near(open, planned),
                                name + ": the actor's own Guard308 cannot raise it again while the lock lasts: a plain shot lands whole [" + open + " of " + planned + "]");
                            bool again = Guard(true, until + .01f) && Near(body.Modifiers.Stance.Share, share) && !body.Modifiers.Stance.Broken;
                            float halved = Shot(plain);
                            r.Check("낭-2", again && planned > 0f && Near(halved, planned * (1f - share)),
                                name + ": after the lock the actor raises it again: a plain shot loses the share [" + halved + " of " + planned + "]");
                            r.Check("낭-3", !body.WeakPointActive, name + ": the break opens no weak point (no groggy)");
                            Guard(false, Time.time);
                            f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                        }
                    }

                    // ---- 순: no new guard goes up inside the circle ----
                    if (r.Wants(unguard.ToString()))
                    {
                        var row = rows[1];
                        Vector3 inside = origin + forward * 2f, outside = origin + forward * 8f;
                        foreach (var k in kinds)
                        {
                            string name = k.entry.ContentId + " (" + k.encounter.Id + ")";
                            Reset(); SendAway();
                            float share = Arm(k);
                            body.Restore(); Guard(false, Time.time);
                            body.transform.position = inside; Sync();
                            f.ClearLog(); f.Ink.Restore(); f.Cast(unguard);
                            bool placed = Placed(unguardFx, DomainUnguardEffect308.HandlerId, row, out var zone);
                            if (!r.Check("G125", share > 0f && placed,
                                name + ": 순 lays its circle at the caster's feet with the sheet's radius and life")) continue;
                            Tick(unguardFx, Time.time);
                            bool sealedIn = body.Modifiers.Domain.GuardBlocked(Time.time) && unguardFx.HeldEnemies == 1;
                            bool none = !Guard(true, Time.time);
                            float open = Shot(plain);
                            r.Check("G125", sealedIn && none && planned > 0f && Near(open, planned),
                                name + ": inside the circle the actor's Guard308 raises no guard: a plain shot lands whole [" + open + " of " + planned + "]");

                            body.transform.position = outside; Sync(); Tick(unguardFx, Time.time);
                            bool free = !body.Modifiers.Domain.GuardBlocked(Time.time) && unguardFx.HeldEnemies == 0;
                            bool raised = Guard(true, Time.time);
                            body.transform.position = inside; Sync(); Tick(unguardFx, Time.time);
                            bool kept = body.Modifiers.Domain.GuardBlocked(Time.time) && Guard(true, Time.time) && Near(body.Modifiers.Stance.Share, share);
                            float halved = Shot(plain);
                            r.Check("G125", free && raised && kept && planned > 0f && Near(halved, planned * (1f - share)),
                                name + ": a guard raised outside stays up when the circle reaches it: a plain shot loses the share [" + halved + " of " + planned + "]");

                            Guard(false, Time.time);
                            Tick(unguardFx, zone.Until + .1f);
                            bool gone = unguardFx.Zones.CountOf(DomainUnguardEffect308.HandlerId) == 0 && unguardFx.HeldEnemies == 0 && !body.Modifiers.Domain.GuardBlocked(Time.time);
                            r.Check("G125", gone && Guard(true, Time.time) && Near(body.Modifiers.Stance.Share, share),
                                name + ": at the circle's end nothing stays on the body and the guard rises again");
                            Guard(false, Time.time);
                            f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                        }
                    }

                    // ---- 눗: the fire circle burns the stone over it ----
                    if (r.Wants(burn.ToString()))
                    {
                        var row = rows[2]; int cap = Mathf.RoundToInt(row.F("domain.cap", 0f));
                        Reset(); SendAway();
                        f.ClearLog(); f.Ink.Restore(); f.Cast(burn);
                        bool placed = Placed(burnFx, DomainBurnShotEffect308.HandlerId, row, out var zone);
                        if (r.Check("G125", cap == 4 && placed,
                            "눗 lays its circle at the caster's feet with the sheet's radius, life and cap (" + cap + ")"))
                        {
                            Put(thrower, origin + forward * 8f);
                            bool flying = Throw(thrower);
                            int downed0 = burnFx.Downed, ended0 = ended, impacts0 = impacts;
                            Advance(thrower, .3f); Tick(burnFx, Time.time);
                            bool open = thrower.IsProjectileFlying && burnFx.Downed == downed0 && !SpellZoneRule308.Inside(zone, stoneBody.transform.position);
                            Advance(thrower, .85f);
                            bool over = SpellZoneRule308.Inside(zone, stoneBody.transform.position);
                            Tick(burnFx, Time.time);
                            r.Check("G125", flying && open && over && !thrower.IsProjectileFlying && !stoneBody.activeSelf && burnFx.Downed == downed0 + 1 &&
                                ended == ended0 + 1 && cancelled && impacts == impacts0 && Near(f.Player.Hp01, 1f),
                                "a real thrower's stone flies on over open ground and burns over the circle: the throw ends as a cancel (the stone is hidden), no impact, the player takes nothing");

                            Put(thrower, origin + forward * 2f);
                            flying = Throw(thrower); downed0 = burnFx.Downed;
                            Advance(thrower, .5f); Tick(burnFx, Time.time);
                            bool past = thrower.IsProjectileFlying && burnFx.Downed == downed0;
                            impacts0 = impacts; Lands(thrower);
                            r.Check("G125", flying && past && Hurt(impacts0), "a stone thrown from inside the circle flies on and hurts (the circle shuts the outside out, not the inside in)");

                            Put(thrower, origin + forward * 8f);
                            int guard = 0; bool taken = true;
                            while (burnFx.Downed < cap && guard++ < 8)
                            {
                                taken &= Throw(thrower); Advance(thrower, .85f); Tick(burnFx, Time.time);
                                taken &= !thrower.IsProjectileFlying;
                            }
                            flying = Throw(thrower); Advance(thrower, .85f); Tick(burnFx, Time.time);
                            bool spent = burnFx.Downed == cap && thrower.IsProjectileFlying;
                            impacts0 = impacts; Lands(thrower);
                            r.Check("G125", taken && flying && spent && Hurt(impacts0),
                                "the circle burns " + cap + " stones (domain.cap); the next stone over it flies on and hurts");

                            f.ClearLog(); f.Ink.Restore(); f.Cast(burn);
                            var fresh = Zone(DomainBurnShotEffect308.HandlerId);
                            bool recast = f.Accepted.Count == 1 && burnFx.Zones.CountOf(DomainBurnShotEffect308.HandlerId) == 1 && burnFx.Downed == 0;
                            Tick(burnFx, fresh.Until + .1f);
                            flying = Throw(thrower); Advance(thrower, .85f); Tick(burnFx, Time.time);
                            bool ended1 = burnFx.Zones.CountOf(DomainBurnShotEffect308.HandlerId) == 0 && thrower.IsProjectileFlying;
                            impacts0 = impacts; Lands(thrower);
                            r.Check("G125", recast && flying && ended1 && Hurt(impacts0),
                                "a recast starts a fresh circle (the count back at 0); at the circle's end the stone over the spot flies on and hurts");
                        }
                        f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    }

                    // ---- 숭: the metal circle turns the stone back on its thrower ----
                    if (r.Wants(turn.ToString()))
                    {
                        var row = rows[3]; float reflect = row.F("domain.reflect", -1f);
                        Reset(); SendAway();
                        f.ClearLog(); f.Ink.Restore(); f.Cast(turn);
                        bool placed = Placed(turnFx, DomainReflectShotEffect308.HandlerId, row, out var zone);
                        if (r.Check("G125", Near(row.F("domain.cap", -1f), 3f) && Near(reflect, .5f) && placed,
                            "숭 lays its circle at the caster's feet with the sheet's radius, life, cap and reflection (" + reflect + ")"))
                        {
                            var shooter = f.Inside[1];
                            Put(thrower, origin + forward * 8f);
                            bool flying = Throw(thrower);
                            float power = thrower.AttackDamage * reflect, expected = power * host.DamageScale(zone.Element), hp = shooter.Hp;
                            int downed0 = turnFx.Downed, ended0 = ended, impacts0 = impacts;
                            Advance(thrower, .85f);
                            f.ClearLog(); Tick(turnFx, Time.time);
                            r.Check("G125", flying && !thrower.IsProjectileFlying && !stoneBody.activeSelf && turnFx.Downed == downed0 + 1 && ended == ended0 + 1 && cancelled &&
                                impacts == impacts0 && Near(f.Player.Hp01, 1f) && turnFx.PendingCount == 0,
                                "a real thrower's stone over the circle is taken: the throw ends as a cancel, no impact, the player takes nothing");
                            var hit = f.Hits.Count == 1 ? f.Hits[0] : default;
                            r.Check("G125", f.Hits.Count == 1 && hit.Target == shooter && hit.Attack.Source == DamageSource.Retaliation && power > 0f &&
                                Near(hit.AppliedDamage, expected, .01f) && Near(hp - shooter.Hp, hit.AppliedDamage) && shooter.IsAlive && !shooter.WeakPointActive,
                                "the stone goes back on its thrower the same frame: " + hit.AppliedDamage + " (the stone's " + thrower.AttackDamage + " x " + reflect
                                + "), Retaliation, no weak point");
                            Tick(turnFx, zone.Until + .1f);
                            r.Check("G125", turnFx.Zones.CountOf(DomainReflectShotEffect308.HandlerId) == 0 && turnFx.PendingCount == 0, "at the circle's end nothing is left to send back");
                        }
                        f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    }
                }
                finally
                {
                    if (clones != null)
                    {
                        made = clones.ToArray();
                        foreach (var clone in made) if (clone != null) Object.DestroyImmediate(clone);
                        clones.Clear();
                    }
                    if (actor != null) { actor.Brain = null; Object.DestroyImmediate(actor); }
                    if (controller != null) Object.DestroyImmediate(controller);
                    if (agent != null) Object.DestroyImmediate(agent);
                    if (thrower != null)
                    {
                        thrower.AttackImpactResolved -= onImpact; thrower.AttackPresentationEnded -= onEnded;
                        SpellFixture308.Set(thrower, "_projectile", null);                  // its OnDestroy would Destroy the stone (an error in Edit Mode)
                        Object.DestroyImmediate(thrower);
                    }
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("G125", made.Length > 0 && made.All(c => c == null), "the guarded brain clones (" + made.Length + ") die with the fixture");
            r.Check("G125", JsonUtility.ToJson(content) == contentJson && JsonUtility.ToJson(brain) == brainJson && JsonUtility.ToJson(stone) == stoneJson
                && EditorUtility.IsDirty(content) == contentDirty && EditorUtility.IsDirty(brain) == brainDirty && EditorUtility.IsDirty(stone) == stoneDirty,
                "the real content, brain and stone-throw are read, never written (same values, not marked dirty)");
        }
    }
}
