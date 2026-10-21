using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-129 (토론 17) fixtures: 숨 against the first force that moves the player, the main world's 적로 불승냥이 charge.
    // The real profile (JeokroWolfCharge308, loaded, never written) on a real EnemyController hits through the real wiring
    // (AttackDamaged -> CombatLoopWiring.Shove308 -> PlayerMotor.Shove308). The pushed body is a real PlayerMotor on a
    // CharacterController: the motor asks the default physics scene (as in the game), so its body and floor stand in the open
    // scene, hidden and unsaved, far below the world, and are destroyed at the end (the open scene must stay clean). Edit mode
    // has one Time.time: the motor is stepped by hand with an explicit dt (StepLocomotion, as WorldMacroLocomotionMotorQa does).
    // Left to Play: the push read in first person, motion sickness, the armed places' drops and water, the look of "held".
    public static partial class Spell120Checks308
    {
        static readonly Vector3 PushBase = new Vector3(7000f, -2000f, 7000f);

        static partial void FixturesP129(Report r)
        {
            // grid cell 94 = metal u + mieum (no glyph is named)
            char anchor = SpellGrammar308.LetterAt(94);
            if (!r.Wants(anchor.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var anchorFx = effects.OfType<DomainAnchorEffect308>().FirstOrDefault();
            if (!r.Check("P129", anchorFx != null, "the anchor domain handler is registered")) return;
            var charge = AssetDatabase.LoadAssetAtPath<EnemyAttackProfileSO>(JeokroCharge);
            var bite = charge != null ? charge.Fallback : null;
            if (!r.Check("P129", charge != null && charge.TryValidate(out _) && charge.Delivery == EnemyAttackDelivery.Charge && bite != null,
                "the main world's 불승냥이 charge loads with its bite Fallback: " + JeokroCharge)) return;
            r.Check("P129", Near(charge.ShoveDistance, 1.8f) && Near(charge.ShoveSeconds, .35f) && Near(bite.ShoveDistance, 0f) && Near(bite.ShoveSeconds, 0f),
                "the charge pushes 1.8 m over .35 s (D308-129 TEST), its bite does not: " + charge.ShoveDistance + " m / " + charge.ShoveSeconds + " s");
            string world = Path.Combine(Path.GetDirectoryName(Application.dataPath), MainWorld);
            string guid = AssetDatabase.AssetPathToGUID(JeokroCharge);
            r.Check("P129", !string.IsNullOrEmpty(guid) && File.Exists(world) && File.ReadAllText(world).Contains("guid: " + guid),
                "the main world arms the push: " + MainWorld + " references the charge profile");
            string chargeJson = JsonUtility.ToJson(charge), biteJson = JsonUtility.ToJson(bite);
            bool chargeDirty = EditorUtility.IsDirty(charge), biteDirty = EditorUtility.IsDirty(bite);
            var open = SceneManager.GetActiveScene(); bool openDirty = open.isDirty;

            var made = new List<GameObject>();
            GameObject Hidden(string name, Vector3 at)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
                made.Add(go); go.transform.position = at; return go;
            }
            using (var f = NewFixture(r, effects, new[] { "Rules308_Base", "Rules308_WP00", "Rules308_WP17" }))
            {
                var host = (ISpellCastHost)f.Wiring;
                object motorBefore = SpellFixture308.Get(f.Wiring, "_motor"), dodgeBefore = SpellFixture308.Get(f.Player, "_dodge");
                EnemyController controller = null;
                try
                {
                    // ---- the pushed body: a real motor on a floor 40 m square (its +z edge is the ledge), and a wall ----
                    Hidden("P129_Floor", PushBase + Vector3.down * .25f).AddComponent<BoxCollider>().size = new Vector3(40f, .5f, 40f);
                    var wallAt = PushBase + new Vector3(10f, 0f, 0f);
                    // its near face 1.1 m ahead (no ground sample falls on the face itself)
                    Hidden("P129_Wall", wallAt + new Vector3(0f, 1.5f, 1.35f)).AddComponent<BoxCollider>().size = new Vector3(4f, 3f, .5f);
                    var body = Hidden("P129_Player", PushBase); body.SetActive(false);
                    var cc = body.AddComponent<CharacterController>();
                    cc.height = 1.75f; cc.radius = .28f; cc.center = Vector3.up * .875f; cc.skinWidth = .03f; cc.stepOffset = .3f; cc.slopeLimit = 45f;
                    var dodge = body.AddComponent<DodgeAction>(); SpellFixture308.Set(dodge, "_config", f.Config);
                    var profile = f.Own(ScriptableObject.CreateInstance<PlayerLocomotionProfileSO>());
                    var motor = body.AddComponent<PlayerMotor>();
                    SpellFixture308.Set(motor, "_config", f.Config); SpellFixture308.Set(motor, "_dodge", dodge); SpellFixture308.Set(motor, "_locomotion", profile);
                    body.SetActive(true);
                    SpellFixture308.Call(motor, "Awake"); motor.ConfigureLocomotion(profile);
                    SpellFixture308.Set(f.Player, "_dodge", dodge);
                    SpellFixture308.Set(f.Wiring, "_motor", motor); SpellFixture308.Call(f.Wiring, "HookShove308");

                    void Step(float dt, float seconds) { int n = Mathf.CeilToInt(seconds / dt - 1e-4f); for (int i = 0; i < n; i++) SpellFixture308.Call(motor, "StepLocomotion", Vector2.zero, dt); }
                    void Place(Vector3 at)
                    {
                        cc.enabled = false; body.transform.position = at; body.transform.rotation = Quaternion.identity; cc.enabled = true;
                        motor.ResetMotion(); Physics.SyncTransforms(); Step(1f / 60f, 8f / 60f);
                    }
                    float Moved(Vector3 from) => Vector3.ProjectOnPlane(body.transform.position - from, Vector3.up).magnitude;
                    void Sync()
                    {
                        Physics.SyncTransforms();
                        var physics = f.Scene.GetPhysicsScene();
                        if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("The fixture scene needs its own physics scene.");
                        physics.Simulate(.001f);
                    }
                    void AnchorTick() => anchorFx.Tick(new SpellTickContext(Time.time, 1f / 60f, host, f.Fx));

                    var wolf = f.Inside[0];
                    controller = wolf.gameObject.AddComponent<EnemyController>();
                    SpellFixture308.Set(controller, "_config", f.Config); SpellFixture308.Set(controller, "_vitals", wolf);
                    SpellFixture308.Set(controller, "_player", f.Player.transform); SpellFixture308.Set(controller, "_playerVitals", f.Player);
                    controller.Init(new ParryJudge(f.Config)); controller.Configure(charge);
                    Vector3 origin = SpellFixture308.Origin, forward = Vector3.forward;
                    // a charge hit through the attack's own gate (EnemyStrike308.Deliver), the wolf facing +z
                    bool Hit(EnemyAttackProfileSO with)
                    {
                        f.Player.Restore(); controller.Configure(with);
                        wolf.transform.SetPositionAndRotation(origin - forward * 2f, Quaternion.LookRotation(forward)); Sync();
                        return EnemyStrike308.Deliver(f.Player, wolf, 18f, IncomingDamageKind.Melee);
                    }
                    float Hp() => f.Player.Hp01 * f.Player.MaxHp;
                    f.Player.transform.position = origin; Sync();

                    // ---- the real run: the charge sweeps the player, lands, and the hit pushes along its locked line ----
                    Place(PushBase);
                    f.Player.Restore();
                    wolf.transform.SetPositionAndRotation(origin - forward * 5f, Quaternion.LookRotation(forward)); Sync();
                    controller.AttackEnabled = true; controller.Configure(charge); SpellFixture308.Call(controller, "BeginAuthoredTelegraph");
                    SpellFixture308.Set(controller, "_stateUntil", Time.time - 1f); SpellFixture308.Call(controller, "TickAuthoredAttack");
                    bool running = controller.IsCharging;
                    wolf.transform.position = origin - forward * .3f; Sync();
                    int serial = motor.ShoveSerial308; float hp0 = Hp();
                    SpellFixture308.Call(controller, "TickAuthoredAttack");
                    bool pushed = motor.ShoveSerial308 == serial + 1 && motor.IsShoved308;
                    Step(1f / 60f, .5f);
                    float run = Moved(PushBase); Vector3 runEnd = body.transform.position;
                    r.Check("숨-1", running && Hp() < hp0 - 1f && pushed && Mathf.Abs(run - 1.8f) < .05f && (runEnd - PushBase).z > 1.7f && !motor.IsShoved308 && motor.IsLocomotionGrounded,
                        "a charge that lands on open ground pushes the player 1.8 m along its run and the damage stands: moved " + run.ToString("0.000") + " m");
                    controller.StopAttack();

                    // ---- the same distance at 30 · 60 · 120 Hz ----
                    foreach (int hz in new[] { 30, 60, 120 })
                    {
                        Place(PushBase); Hit(charge); Step(1f / hz, .5f);
                        float d = Moved(PushBase);
                        r.Check("숨-1", Mathf.Abs(d - 1.8f) < .05f, "the push covers the same 1.8 m at " + hz + " Hz: " + d.ToString("0.000") + " m");
                    }

                    // ---- no second push within .8 s of the first; one after it ----
                    Place(PushBase); Hit(charge); Step(1f / 60f, .5f);
                    Vector3 at = body.transform.position; serial = motor.ShoveSerial308;
                    f.Player.Restore(); hp0 = Hp();
                    EnemyStrike308.Deliver(f.Player, wolf, 18f, IncomingDamageKind.Melee);
                    bool locked = motor.ShoveSerial308 == serial && Hp() < hp0 - 1f;
                    Step(1f / 60f, .4f);
                    r.Check("숨-1", locked && Moved(at) < .01f, "a second hit within .8 s of the push still hurts but does not push again");
                    Hit(charge);
                    r.Check("숨-1", motor.ShoveSerial308 == serial + 1, "after .8 s the next hit pushes again");
                    Step(1f / 60f, .5f);

                    // ---- the bite (the charge's Fallback) does not push ----
                    Place(PushBase); serial = motor.ShoveSerial308;
                    bool bit = Hit(bite); Step(1f / 60f, .5f);
                    r.Check("숨-1", bit && motor.ShoveSerial308 == serial && Moved(PushBase) < .01f, "the Fallback bite hurts but does not push (the attacker's current profile decides)");

                    // ---- the ledge: the push stops before walkable ground ends ----
                    float edge = PushBase.z + 20f; Vector3 ledge = new Vector3(PushBase.x, PushBase.y, edge - 1.2f);
                    Place(ledge); Hit(charge); float planned = motor.ShovePlanned308; Step(1f / 60f, .5f);
                    float toEdge = edge - body.transform.position.z;
                    r.Check("숨-2", planned < 1.8f - .1f && planned > .2f && toEdge >= .3f && motor.IsLocomotionGrounded,
                        "a push toward a ledge is cut short of it (" + ShoveMargin + " m before the first sample without support): planned " + planned.ToString("0.00") + " m, " + toEdge.ToString("0.00") + " m left to the edge");

                    // ---- the wall: the controller stops the body and the push ends ----
                    Place(wallAt); Hit(charge); Step(1f / 60f, .5f);
                    float toWall = Moved(wallAt);
                    r.Check("숨-2", toWall < 1.1f - cc.radius + .03f && toWall > .5f && !motor.IsShoved308,
                        "a wall behind the player stops the push, which then ends (it does not keep pressing): moved " + toWall.ToString("0.00") + " m");

                    // ---- 숨: cast at the player's feet; inside, no push, the same damage ----
                    f.Finals.Open.Add(SpellFinal.Mieum);
                    f.Player.transform.position = origin; Sync();
                    f.Resolver.TryRow(anchor, out var row);
                    f.Cast(anchor); AnchorTick();
                    var domain = f.Wiring.PlayerDomain308;
                    bool cast = f.Accepted.Count >= 1 && f.Accepted.Last().Letter == anchor && domain.Anchored(Time.time) && row != null;
                    Place(PushBase); serial = motor.ShoveSerial308; int held = f.Wiring.ShovesHeld308;
                    f.Player.Restore(); hp0 = Hp();
                    controller.Configure(charge); wolf.transform.SetPositionAndRotation(origin - forward * 2f, Quaternion.LookRotation(forward)); Sync();
                    EnemyStrike308.Deliver(f.Player, wolf, 18f, IncomingDamageKind.Melee);
                    float lossInside = hp0 - Hp();
                    Step(1f / 60f, .5f);
                    r.Check("숨-3", cast && motor.ShoveSerial308 == serial && f.Wiring.ShovesHeld308 == held + 1 && Moved(PushBase) < .01f && Near(lossInside, 18f, .01f),
                        "inside 숨's domain the charge's hit does not move the player and the damage is the same 18");

                    // ---- a push under way stops the step the player is inside ----
                    f.Player.transform.position = origin + Vector3.right * 30f; Sync(); AnchorTick();
                    bool outside = !domain.Anchored(Time.time);
                    Place(PushBase); Hit(charge); Step(1f / 60f, 3f / 60f);
                    float before = Moved(PushBase); bool under = motor.IsShoved308;
                    f.Player.transform.position = origin; Sync(); AnchorTick();
                    Step(1f / 60f, .5f);
                    float after = Moved(PushBase);
                    r.Check("숨-3", outside && under && before > .2f && before < 1.8f && !motor.IsShoved308 && after - before < .01f,
                        "a push under way stops on the step the player stands inside the domain: " + before.ToString("0.00") + " m, then no more");

                    // ---- the domain lets go: outside it and at its end the push is back ----
                    f.Player.transform.position = origin + Vector3.right * 30f; Sync(); AnchorTick();
                    Place(PushBase); serial = motor.ShoveSerial308; Hit(charge); Step(1f / 60f, .5f);
                    r.Check("숨-3", !domain.Anchored(Time.time) && motor.ShoveSerial308 == serial + 1 && Mathf.Abs(Moved(PushBase) - 1.8f) < .05f,
                        "stepping out of the domain gives the push back");
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);

                    // ---- a dodge: it ends a push under way, and its invulnerability means no hit, so no push ----
                    Place(PushBase); Hit(charge); Step(1f / 60f, 3f / 60f);
                    bool dodged = dodge.TryDodge(Vector3.right);
                    Step(1f / 60f, 1f / 60f);
                    bool ended = !motor.IsShoved308;
                    serial = motor.ShoveSerial308; f.Player.Restore(); hp0 = Hp();
                    bool landed = EnemyStrike308.Deliver(f.Player, wolf, 18f, IncomingDamageKind.Melee);
                    r.Check("숨-1", dodged && ended && !landed && Near(Hp(), hp0) && motor.ShoveSerial308 == serial,
                        "a dodge ends a push under way; a hit during its invulnerability lands nothing and pushes nothing");
                    dodge.Cancel();

                    // ---- the gate: the main game's proof alone ----
                    f.Finals.Open.Clear(); f.Finals.Proven.Add(SpellFinal.Mieum);
                    f.Player.transform.position = origin; Sync(); int accepted = f.Accepted.Count;
                    f.Cast(anchor);
                    r.Check("숨-3", row.Gate == SpellGateMode.Final && f.Accepted.Count == accepted + 1 && f.Misfires.Count == 0,
                        "숨 is Gate final: the main game's mieum proof casts it with no TEST unlock");
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                }
                finally
                {
                    try { SpellFixture308.Call(f.Wiring, "UnhookShove308"); } catch (Exception e) { Debug.LogException(e); }
                    SpellFixture308.Set(f.Wiring, "_motor", motorBefore); SpellFixture308.Set(f.Player, "_dodge", dodgeBefore);
                    if (controller != null) Object.DestroyImmediate(controller);
                    f.Wiring.ClearSpells308(SpellClearReason.Disabled);
                    f.Finals.Proven.Clear(); f.Finals.Open.Clear();
                    for (int i = made.Count - 1; i >= 0; i--) if (made[i] != null) Object.DestroyImmediate(made[i]);
                }
            }
            r.Check("P129", made.All(go => go == null) && open.isDirty == openDirty,
                "the pushed body, its floor and wall are gone and the open scene is not marked dirty");
            r.Check("P129", JsonUtility.ToJson(charge) == chargeJson && JsonUtility.ToJson(bite) == biteJson &&
                EditorUtility.IsDirty(charge) == chargeDirty && EditorUtility.IsDirty(bite) == biteDirty,
                "the real profiles are read, never written (same values, not marked dirty)");
        }

        const float ShoveMargin = PlayerMotor.ShoveMargin308;
    }
}
