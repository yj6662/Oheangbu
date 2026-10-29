using System;
using System.Linq;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 적용 3 (b) fixtures: 묵's circle against the main world's own charger. The real 적로 불승냥이 profile
    // (JeokroWolfCharge308 and its bite Fallback, loaded, never written) on a real EnemyController in the fixture scene: inside
    // the circle a common enemy's dash is 0, so it cannot start its run and closes in to bite once, then is the charge again;
    // a telegraph under way is called off; a run that enters the circle stops there with no hit and the plain recovery (not
    // the wall's); a boss keeps .7 of its dash and still charges; the circle lets go outside and at its end; 묵 casts on the
    // main game's proof alone (Gate final). Edit mode has one Time.time: the checks call the controller's steps by hand and
    // move the bodies, so nothing below needs a frame's deltaTime. Left to Play: the field charge itself and its look.
    public static partial class Spell120Checks308
    {
        const string JeokroCharge = "Assets/_Project/Art/World/Pacing308/Data/JeokroWolfCharge308.asset";

        static partial void FixturesM125(Report r)
        {
            char ground = SpellGrammar308.LetterAt(68);   // 68 metal u + giyeok
            if (!r.Wants(ground.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var groundFx = effects.OfType<DomainGroundEffect308>().FirstOrDefault();
            if (!r.Check("M125", groundFx != null, "묵's ground circle is registered")) return;
            var charge = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(JeokroCharge);
            var bite = charge != null ? charge.Fallback : null;
            if (!r.Check("M125", charge != null && charge.TryValidate(out _) && charge.Delivery == EnemyAttackDelivery.Charge && bite != null &&
                bite.Delivery == EnemyAttackDelivery.MeleeArc && bite.TryValidate(out _), "the main world's 불승냥이 charge loads with its bite Fallback: " + JeokroCharge)) return;
            string chargeJson = JsonUtility.ToJson(charge), biteJson = JsonUtility.ToJson(bite);
            bool chargeDirty = EditorUtility.IsDirty(charge), biteDirty = EditorUtility.IsDirty(bite);

            using (var f = NewFixture(r, effects, new[] { "Rules308_Base", "Rules308_WP00", "Rules308_WP17" }))
            {
                f.Finals.Open.Add(SpellFinal.Giyeok);
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
                    Sync();
                }
                void SendAway()
                {
                    for (int i = 0; i < everyone.Length; i++) everyone[i].transform.position = origin + new Vector3(300f + i * 10f, 0f, 300f);
                    Sync();
                }
                T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

                var wolf = f.Inside[0]; var boss = f.Boss;
                EnemyController controller = null, chief = null;
                int impacts = 0, ended = 0; bool cancelled = false; EnemyAttackImpact impact = default;
                Action<EnemyAttackImpact> onImpact = e => { impacts++; impact = e; };
                Action<AttackProvenance, bool> onEnded = (_, off) => { ended++; cancelled = off; };
                try
                {
                    Reset(); SendAway();
                    EnemyController Hook(EnemyVitals body)
                    {
                        var c = body.gameObject.AddComponent<EnemyController>();
                        SpellFixture308.Set(c, "_config", f.Config); SpellFixture308.Set(c, "_vitals", body);
                        SpellFixture308.Set(c, "_player", f.Player.transform); SpellFixture308.Set(c, "_playerVitals", f.Player);
                        c.Init(new ParryJudge(f.Config));
                        c.AttackImpactResolved += onImpact; c.AttackPresentationEnded += onEnded;
                        c.Configure(charge);
                        return c;
                    }
                    controller = Hook(wolf); chief = Hook(boss);

                    // the body at, the player at, the body turned to the player (LockCharge308 runs where it faces), the circle read
                    void Put(EnemyController c, Vector3 body, Vector3 player)
                    {
                        c.transform.position = body; f.Player.transform.position = player;
                        Vector3 to = Vector3.ProjectOnPlane(player - body, Vector3.up);
                        if (to.sqrMagnitude > 1e-4f) c.transform.rotation = Quaternion.LookRotation(to.normalized);
                        Sync(); groundFx.Tick(new SpellTickContext(Time.time, 1f / 60f, (ISpellCastHost)f.Wiring, f.Fx));
                    }
                    // one idle choice with the cooldown run out and the path read fresh
                    void Choose(EnemyController c)
                    {
                        SpellFixture308.Set(c, "_cooldown", 0f); SpellFixture308.Set(c, "_closeInCheck308", 0f);
                        SpellFixture308.Call(c, "TickAuthoredAttack");
                    }
                    void Begin(EnemyController c) { c.AttackEnabled = true; c.Configure(charge); SpellFixture308.Call(c, "BeginAuthoredTelegraph"); }
                    void Resolve(EnemyController c) { SpellFixture308.Set(c, "_stateUntil", Time.time - 1f); SpellFixture308.Call(c, "TickAuthoredAttack"); }
                    float Scale(EnemyController c) => Get<float>(c, "_recoveryScale308");
                    bool Recovering(EnemyController c, float recovery) => c.IsRecovering && Near(c.RecoveryDuration, recovery * Scale(c)) &&
                        Near(Get<float>(c, "_stateUntil") - Time.time, recovery * Scale(c), .01f);

                    // ---- the cast ----
                    f.Player.transform.position = origin; Sync();
                    f.Resolver.TryRow(ground, out var row);
                    f.Cast(ground);
                    var zone = groundFx.Zones.Zones.FirstOrDefault(z => z.Owner == DomainGroundEffect308.HandlerId);
                    bool placed = f.Accepted.Count == 1 && groundFx.Zones.CountOf(DomainGroundEffect308.HandlerId) == 1 && row != null;
                    if (!r.Check("묵-3", placed && (zone.Centre - origin).magnitude < .01f && Near(zone.Radius, row.F("domain.radius", -1f)) &&
                        Near(zone.Until - zone.From, row.F("domain.life", -1f)) && zone.From <= Time.time && Time.time < zone.Until &&
                        Near(row.F("domain.scale", -1f), 0f) && Near(row.F("domain.boss", -1f), .7f),
                        "묵 lays its circle at the caster's feet with the sheet's radius and life (dash 0, a boss .7)")) return;
                    Vector3 inside = origin + forward * 2.5f, outside = origin + forward * 8f;

                    // ---- inside: no run, a close-in and one bite ----
                    Put(controller, inside, inside + forward * 5f);
                    var domain = wolf.Modifiers.Domain;
                    bool marked = Near(domain.DashScale(Time.time), 0f) && groundFx.HeldEnemies == 1;
                    bool noRun = !(bool)SpellFixture308.Call(controller, "ChargeCanStart308");
                    Choose(controller);
                    r.Check("묵-4", marked && noRun && !controller.IsTelegraphing && controller.CloseIn308 && controller.AttackProfile == charge &&
                        Near(controller.CloseInRange308, bite.Range),
                        "inside the circle the real charger's dash is 0: no run starts five metres off, it closes in to its bite's range instead");
                    int impacts0 = impacts; float hp0 = f.Player.Hp01;
                    Put(controller, inside, inside + forward * 1.7f);
                    Choose(controller);
                    bool biting = controller.IsTelegraphing && controller.AttackProfile == bite;
                    Resolve(controller);
                    r.Check("묵-4", biting && impacts == impacts0 + 1 && impact.InShape && impact.AppliedDamage > 0f && impact.AppliedDamage <= bite.Damage + .001f &&
                        f.Player.Hp01 < hp0 && Recovering(controller, bite.Recovery),
                        "within the bite's reach it lands the Fallback bite once (melee damage, the bite's own recovery)");
                    Resolve(controller);
                    r.Check("묵-4", !controller.IsRecovering && controller.AttackProfile == charge, "after the bite's recovery it is the charge again");

                    // ---- a telegraph under way is called off ----
                    f.Player.Restore();
                    Put(controller, outside, outside - forward * 6f);
                    bool free = Near(domain.DashScale(Time.time), 1f) && groundFx.HeldEnemies == 0;
                    Begin(controller);
                    bool telling = controller.IsTelegraphing && controller.AttackProfile == charge;
                    impacts0 = impacts; int ended0 = ended;
                    Put(controller, inside, inside + forward * 6f);
                    SpellFixture308.Call(controller, "TickAuthoredAttack");
                    r.Check("묵-5", free && telling && !controller.IsTelegraphing && ended == ended0 + 1 && cancelled && impacts == impacts0 &&
                        !Get<bool>(controller, "_chargeWall308") && Recovering(controller, charge.Recovery),
                        "a charge telegraph whose body is drawn into the circle is called off: no impact, the plain recovery");

                    // ---- a run under way stops at the circle ----
                    f.Player.Restore();
                    Put(controller, outside, origin + forward);
                    Begin(controller); Resolve(controller);
                    bool running = controller.IsCharging && Near(domain.DashScale(Time.time), 1f);
                    impacts0 = impacts; ended0 = ended;
                    Put(controller, inside, origin + forward);
                    SpellFixture308.Call(controller, "TickAuthoredAttack");
                    r.Check("묵-6", running && !controller.IsCharging && impacts == impacts0 + 1 && !impact.InShape && impact.AppliedDamage == 0f &&
                        Near(f.Player.Hp01, 1f) && ended == ended0 + 1 && !cancelled,
                        "a run that enters the circle stops there: one empty impact, no damage, the run ends as an attack (not a cancel)");
                    r.Check("묵-6", !Get<bool>(controller, "_chargeWall308") && Recovering(controller, charge.Recovery) && !Near(charge.Recovery, charge.ChargeWallRecovery),
                        "the stop is no wall: the plain recovery, not ChargeWallRecovery");

                    // ---- a boss keeps .7 of its dash ----
                    Put(chief, inside - forward * .5f, origin - forward * 4f);
                    var chiefDomain = boss.Modifiers.Domain;
                    float dash = chiefDomain.DashScale(Time.time);
                    bool canRun = (bool)SpellFixture308.Call(chief, "ChargeCanStart308");
                    Begin(chief);
                    float gap = Vector3.ProjectOnPlane(f.Player.transform.position - chief.transform.position, Vector3.up).magnitude;
                    float expected = Get<float>(chief, "_stateUntil") + Mathf.Max(0f, gap - charge.ImpactRadius) / (charge.ProjectileSpeed * .7f);
                    bool slowed = chief.IsTelegraphing && Near(chief.PredictedImpactTime, expected, .01f) &&
                        chief.PredictedImpactTime > Get<float>(chief, "_stateUntil") + Mathf.Max(0f, gap - charge.ImpactRadius) / charge.ProjectileSpeed + .1f;
                    Resolve(chief);
                    bool chiefRuns = chief.IsCharging;
                    SpellFixture308.Call(chief, "TickAuthoredAttack");
                    r.Check("묵-7", Near(dash, .7f) && canRun && slowed && chiefRuns && chief.IsCharging,
                        "a boss in the circle keeps .7 of its dash: it still starts its run, its impact comes later, and the circle does not stop it");
                    chief.StopAttack();

                    // ---- the circle lets go ----
                    Put(controller, outside, origin); Put(chief, outside + Vector3.right * 3f, origin);
                    bool released = Near(domain.DashScale(Time.time), 1f) && Near(chiefDomain.DashScale(Time.time), 1f) && groundFx.HeldEnemies == 0;
                    groundFx.Tick(new SpellTickContext(zone.Until + .1f, 1f / 60f, (ISpellCastHost)f.Wiring, f.Fx));
                    r.Check("묵-8", released && groundFx.Zones.CountOf(DomainGroundEffect308.HandlerId) == 0 && groundFx.HeldEnemies == 0 && wolf.Modifiers.IsEmpty && boss.Modifiers.IsEmpty,
                        "outside the circle the dash comes back; at the circle's end it is gone and leaves nothing on either body");

                    // ---- the gate: the main game's proof alone ----
                    Reset(); SendAway(); f.Finals.Open.Clear(); f.Finals.Proven.Add(SpellFinal.Giyeok);
                    f.Player.transform.position = origin; Sync();
                    f.Cast(ground);
                    r.Check("묵-8", row.Gate == SpellGateMode.Final && f.Accepted.Count == 1 && f.Misfires.Count == 0 &&
                        groundFx.Zones.CountOf(DomainGroundEffect308.HandlerId) == 1,
                        "묵 is Gate final: the main game's giyeok proof casts it with no TEST unlock");
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                }
                finally
                {
                    foreach (var c in new[] { controller, chief })
                        if (c != null) { c.AttackImpactResolved -= onImpact; c.AttackPresentationEnded -= onEnded; Object.DestroyImmediate(c); }
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                }
            }
            r.Check("M125", JsonUtility.ToJson(charge) == chargeJson && JsonUtility.ToJson(bite) == biteJson &&
                EditorUtility.IsDirty(charge) == chargeDirty && EditorUtility.IsDirty(bite) == biteDirty,
                "the real profiles are read, never written (same values, not marked dirty)");
        }
    }
}
