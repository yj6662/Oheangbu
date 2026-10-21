using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 D308-125 적용 5 fixtures (debate decision Codex · Fable · Claude, TEST): the ember patch on the real wiring. A fire
    // ground eruption of a real EnemyController lays a patch where it burst (hit or miss; a parried one lays none) and keeps two
    // per enemy; the wiring burns the player standing in it on EmberExposure308's shared clock (the frame rule itself is the
    // pure runner's: Cases_Ember125.cs) and never through Damaged; a dodge's frames pass the patch ticks but not the scorch;
    // 운's ward washes the scorch off; the lethal guard and death put it out; a rest of the owner drops its patches; 온's wave
    // wipes a real patch. The clock is handed in (edit mode has one Time.time). Left to Play: the look and sound, the main
    // world's armed enemy, a real dodge's frames.
    public static partial class Spell120Checks308
    {
        static partial void FixturesE125(Report r)
        {
            char purge = SpellGrammar308.LetterAt(111);   // 111 water o + nieun
            if (!r.Wants(purge.ToString())) return;
            var effects = SpellEffectInstaller308.CreateAll();
            var purgeFx = effects.OfType<PurgeEffect308>().FirstOrDefault();
            if (!r.Check("E125", purgeFx != null, "the cleansing wave is registered")) return;

            using (var f = NewFixture(r, effects, WP13Sheets))
            {
                var host = (ISpellCastHost)f.Wiring;
                foreach (SpellFinal final in new[] { SpellFinal.Giyeok, SpellFinal.Nieun, SpellFinal.Siot, SpellFinal.Ieung }) f.Finals.Open.Add(final);
                Vector3 origin = SpellFixture308.Origin, forward = Vector3.forward, right = Vector3.right;
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
                void Tick(ISpellEffect effect, float clock) => effect.Tick(new SpellTickContext(clock, 1f / 60f, host, f.Fx));

                // The wiring's own subscriptions (OnEnable, TryHookServices) never run in edit mode: the fixture hands the player's
                // events to the same handlers and hooks the controllers by hand.
                const float frame = .0625f;   // 1/16 s: every clock below is exact in float
                var owner = f.Inside[0];
                var patches = f.Wiring.Patches308;
                EnemyController controller = null; DodgeAction dodge = null; Func<float, bool> guard = null;
                float lingering = 0f; int damaged = 0, died = 0, spared = 0, started = 0, washed = 0, ended = 0, impacts = 0;
                EnemyAttackImpact impact = default;
                Action<float> onLingering = amount => lingering += amount, onDamaged = _ => damaged++;
                Action onDied = () => { died++; SpellFixture308.Call(f.Wiring, "OnPlayerDied"); };
                Action onSpared = () => { spared++; SpellFixture308.Call(f.Wiring, "OnSpared308"); };
                Action onStarted = () => started++, onWashed = () => washed++, onEnded = () => ended++;
                Action<EnemyAttackImpact> onImpact = e => { impacts++; impact = e; };
                f.Player.LingeringDamaged += onLingering; f.Player.Damaged += onDamaged; f.Player.Died += onDied; f.Player.Spared308 += onSpared;
                f.Wiring.ScorchStarted308 += onStarted; f.Wiring.ScorchWashed308 += onWashed; f.Wiring.ScorchEnded308 += onEnded;
                try
                {
                    SpellFixture308.Set(f.Wiring, "_patchHost308", (Func<string, GameObject>)(name => f.Obj(name, origin)));
                    var floor = f.Obj("Spell308_E125Floor", origin - Vector3.up * .5f).AddComponent<BoxCollider>();
                    floor.size = new Vector3(30f, 1f, 30f);
                    Reset();
                    controller = owner.gameObject.AddComponent<EnemyController>();
                    SpellFixture308.Set(controller, "_config", f.Config); SpellFixture308.Set(controller, "_vitals", owner);
                    SpellFixture308.Set(controller, "_player", f.Player.transform); SpellFixture308.Set(controller, "_playerVitals", f.Player);
                    var judge = new ParryJudge(f.Config); controller.Init(judge);
                    controller.AttackImpactResolved += onImpact;
                    var patchSo = f.Own(ScriptableObject.CreateInstance<EnemyGroundPatchSO>());
                    var profile = f.Own(ScriptableObject.CreateInstance<EnemyAttackProfileSO>());
                    profile.ApplyDefaults(EnemyArchetype.WoodVine); profile.Element = Element.Fire; profile.Patch = patchSo;
                    controller.Configure(profile);
                    SpellFixture308.Call(f.Wiring, "CollectControllers"); SpellFixture308.Call(f.Wiring, "HookPatches308");

                    // frames calls of the wiring's frame, the first at from, the feet where it says; returns the time after the last
                    float Run(float from, int frames, Vector3 feet)
                    {
                        f.Player.transform.position = feet;
                        for (int k = 0; k < frames; k++) SpellFixture308.Call(f.Wiring, "TickPatches308", from + k * frame);
                        return from + frames * frame;
                    }
                    void Clear() { SpellFixture308.Call(f.Wiring, "ClearPatches308"); f.Player.Restore(); }
                    EnemyGroundPatch308 Lay(EnemyGroundPatchSO so, EnemyVitals who, Vector3 at, float now) =>
                        (EnemyGroundPatch308)SpellFixture308.Call(f.Wiring, "LayPatch308", so, who, at, now);
                    void Begin()
                    {
                        controller.AttackEnabled = true; f.Player.transform.position = origin;
                        controller.Configure(profile); judge.ClearGuard(); Sync();
                        SpellFixture308.Call(controller, "BeginAuthoredTelegraph");
                    }
                    void Resolve() { SpellFixture308.Set(controller, "_stateUntil", Time.time - 1f); SpellFixture308.Call(controller, "TickAuthoredAttack"); }

                    // ---- the eruption lays the patch (one clock: every patch below is born at the same Time.time) ----
                    int impacts0 = impacts, damaged0 = damaged; float lingering0 = lingering;
                    Begin(); Vector3 warned = controller.AuthoredTargetPoint; Resolve();
                    var first = patches.Count == 1 ? patches[0] : null;
                    r.Check("불씨-9", impacts == impacts0 + 1 && impact.InShape && damaged == damaged0 + 1 && lingering == lingering0 && first != null &&
                        (first.Position - warned).magnitude < .01f && Mathf.Abs(first.Position.y - origin.y) < .01f && first.Owner == owner,
                        "a fire eruption that hits lays one ember patch where it burst; the hit is the eruption's own (Damaged) and the patch adds nothing then");
                    var box = first != null ? first.GetComponent<BoxCollider>() : null;
                    r.Check("불씨-9", first != null && first.gameObject.scene == f.Scene && first.gameObject.layer == 2 && box != null && box.isTrigger &&
                        first.GetComponentInChildren<Light>() == null,
                        "the patch lies in the player's scene as a thin trigger on Ignore Raycast and gives no light (ART-INK)");

                    Begin(); f.Player.transform.position = origin + right * 3f; Sync(); damaged0 = damaged; Resolve();
                    r.Check("불씨-9", !impact.InShape && damaged == damaged0 && patches.Count == 2 && patches[1] != null && (patches[1].Position - origin).magnitude < .01f,
                        "a miss lays a patch too, at the warned point the player left");

                    Begin(); judge.RaiseGuard(Element.Water, Time.time); Resolve();
                    r.Check("불씨-9", impact.Outcome == ParryOutcome.Success && patches.Count == 2, "a parried eruption lays no patch");

                    var oldest = patches[0]; var second = patches[1];
                    Begin(); Resolve();
                    r.Check("불씨-9", impact.InShape && patches.Count == 2 && oldest == null && second != null && patches.Contains(second),
                        "one enemy keeps two patches: a third puts out its oldest");

                    // ---- the profile ----
                    var plain = f.Own(ScriptableObject.CreateInstance<EnemyAttackProfileSO>()); plain.ApplyDefaults(EnemyArchetype.WoodVine);
                    bool noPatch = plain.Patch == null;
                    plain.ApplyDefaults(EnemyArchetype.NeutralMelee); plain.Patch = patchSo;
                    bool melee = !plain.TryValidate(out string meleeError) && meleeError == "A ground patch belongs to a ground eruption";
                    var broken = f.Own(ScriptableObject.CreateInstance<EnemyGroundPatchSO>()); broken.Tick = 0f;
                    plain.ApplyDefaults(EnemyArchetype.WoodVine); plain.Patch = broken;
                    bool brokenRefused = !plain.TryValidate(out _) && !broken.TryValidate(out _);
                    int before = patches.Count;
                    bool noneLaid = Lay(broken, owner, origin, Time.time) == null && patches.Count == before;
                    r.Check("불씨-10", noPatch && melee && brokenRefused && noneLaid,
                        "no archetype lays a patch by default; a patch on anything but a ground eruption and a broken patch are refused, and the wiring lays none from one");

                    // ---- the burn on the wiring (the pure runner's 불씨-1 and 불씨-2 through HP) ----
                    Clear();
                    var bystander = f.Inside[2]; bystander.transform.position = origin; Sync(); float bystanderHp = bystander.Hp;
                    lingering0 = lingering; damaged0 = damaged; int started0 = started;
                    var burning = Lay(patchSo, owner, origin, 1000f);
                    float t = Run(1000f, 49, origin);
                    r.Check("불씨-1", burning != null && Near(lingering - lingering0, 21f) && started == started0 + 1 && damaged == damaged0 && f.Wiring.Ember308.Scorched(1003f),
                        "three seconds in a patch on the wiring: six patch ticks of 2 and six scorch ticks of 1.5 from the one it caught on, all lingering (no Damaged)");
                    r.Check("불씨-1", Near(bystander.Hp, bystanderHp), "an enemy standing in the patch is never burnt");
                    lingering0 = lingering; int ended0 = ended;
                    Run(t, 71, origin + right * 5f);
                    r.Check("불씨-2", Near(lingering - lingering0, 10.5f) && ended == ended0 + 1 && patches.Count == 0 && burning == null && !f.Wiring.Ember308.Scorched(1007f),
                        "out of the fire the scorch keeps its own ticks and ends four seconds after the last frame in it; the patch goes out at its life's end");

                    // ---- a dodge's frames ----
                    Clear();
                    dodge = f.Player.gameObject.AddComponent<DodgeAction>();
                    SpellFixture308.Set(dodge, "_invulnerableUntil", Time.time + 1000f); SpellFixture308.Set(f.Player, "_dodge", dodge);
                    lingering0 = lingering; started0 = started;
                    Lay(patchSo, owner, origin, 2000f); Run(2000f, 49, origin);
                    r.Check("불씨-11", dodge.IsInvulnerable && Near(lingering - lingering0, 9f) && started == started0 + 1,
                        "dodge frames pass through the patch's ticks but not through the scorch (it burns on the player, not on the ground)");
                    SpellFixture308.Set(f.Player, "_dodge", null); Object.DestroyImmediate(dodge); dodge = null;

                    // ---- 운's ward ----
                    Clear(); started0 = started; int washed0 = washed;
                    Lay(patchSo, owner, origin, 3000f);
                    t = Run(3000f, 17, origin);
                    bool caught = started == started0 + 1;
                    f.Wiring.PlayerDomain308.Ward(1, 3100f);
                    lingering0 = lingering;
                    t = Run(t, 1, origin);
                    bool washedOff = washed == washed0 + 1 && !f.Wiring.Ember308.Scorched(t - frame);
                    started0 = started;
                    Run(t, 32, origin);
                    r.Check("불씨-4", caught && washedOff, "운's ward on the wiring washes the scorch off the frame it reaches the player");
                    r.Check("불씨-4", started == started0 && Near(lingering - lingering0, 8f), "inside the ward no scorch catches, but the patch still burns (four ticks of 2 in two seconds)");
                    f.Wiring.PlayerDomain308.End(1);

                    // ---- the lethal guard ----
                    Clear(); Lay(patchSo, owner, origin, 4000f);
                    t = Run(4000f, 17, origin);
                    guard = _ => true; f.Player.BindLethalDamageGuard(guard);
                    f.Player.Restore(1.5f / f.Player.MaxHp);
                    int spared0 = spared, died0 = died; ended0 = ended;
                    Run(t, 8, origin);
                    r.Check("불씨-6", spared == spared0 + 1 && ended == ended0 + 1 && !f.Wiring.Ember308.Scorched(4001.5f) && Near(f.Player.Hp01 * f.Player.MaxHp, 1f) && died == died0,
                        "a patch tick the lethal guard survives puts the scorch out, and the scorch's tick of that frame does not land");
                    f.Player.UnbindLethalDamageGuard(guard); guard = null;

                    // ---- death, a rest ----
                    Clear(); Lay(patchSo, owner, origin, 5000f);
                    f.Player.Restore(1.5f / f.Player.MaxHp); died0 = died;
                    Run(5000f, 9, origin);
                    r.Check("불씨-12", died == died0 + 1 && patches.Count == 0 && !f.Wiring.Ember308.Scorched(5000.5f) && float.IsNaN(f.Wiring.Ember308.NextTick),
                        "death puts every patch and the scorch out (the wiring's death path)");
                    f.Player.Restore();

                    Clear(); f.RestoreAll();
                    var rested = Lay(patchSo, f.Inside[0], origin + right * 8f, 6000f); var fallen = Lay(patchSo, f.Inside[1], origin - right * 8f, 6000f);
                    f.Inside[1].TakeDamage(1e6f); f.Inside[0].Restore();
                    f.Player.transform.position = origin;
                    SpellFixture308.Call(f.Wiring, "TickPatches308", 6000.1f);
                    r.Check("불씨-12", rested == null && fallen != null && patches.Count == 1 && patches.Contains(fallen),
                        "a rest of the enemy that laid a patch puts it out; a dead enemy's patch burns its time out");
                    f.RestoreAll(); Clear();

                    // ---- 온's wave against a real patch ----
                    Reset(); Clear(); SendAway(); f.Player.transform.position = origin; Sync();
                    float laidAt = Time.time;
                    var front = Lay(patchSo, f.Inside[0], origin + forward * 5f, laidAt); var back = Lay(patchSo, f.Inside[1], origin - forward * 4f, laidAt);
                    int wiped = 0; if (front != null) front.Wiped += _ => wiped++;
                    Sync();
                    float castAt = Time.time; f.Cast(purge);
                    bool planned = f.Plans.Count == 1 && f.Plans[0].Area != null && f.Plans[0].Area.Shape == AreaShape.Path && purgeFx.WavesRunning == 1;
                    r.Check("온-2", front != null && back != null && planned && !front.Purged, "a cleansing wave is cast down a corridor with a real ember patch on it");
                    if (planned)
                    {
                        var area = f.Plans[0].Area;
                        Tick(purgeFx, castAt + area.Delay + area.Length / area.Speed + .1f);
                        r.Check("온-2", front.Purged && front.PurgedBy > 0 && wiped == 1 && purgeFx.Wiped == 1 && !back.Purged,
                            "온's front wipes the ember patch on its path (found by its thin trigger); the one behind the caster stays");
                        SpellFixture308.Call(f.Wiring, "TickPatches308", Time.time);
                        r.Check("온-2", front == null && back != null && patches.Count == 1, "the wiring drops the wiped patch on its next frame");
                    }
                    Clear();
                }
                finally
                {
                    f.Player.LingeringDamaged -= onLingering; f.Player.Damaged -= onDamaged; f.Player.Died -= onDied; f.Player.Spared308 -= onSpared;
                    f.Wiring.ScorchStarted308 -= onStarted; f.Wiring.ScorchWashed308 -= onWashed; f.Wiring.ScorchEnded308 -= onEnded;
                    if (controller != null) controller.AttackImpactResolved -= onImpact;
                    SpellFixture308.Call(f.Wiring, "ClearPatches308"); SpellFixture308.Call(f.Wiring, "UnhookPatches308");
                    if (guard != null) f.Player.UnbindLethalDamageGuard(guard);
                    SpellFixture308.Set(f.Player, "_dodge", null);
                    if (dodge != null) Object.DestroyImmediate(dodge);
                    if (controller != null) Object.DestroyImmediate(controller);
                    SpellFixture308.Set(f.Wiring, "_patchHost308", null);
                    f.Wiring.PlayerDomain308.End(1);
                }
            }
        }
    }
}
