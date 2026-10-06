using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 edit-mode fixture (SPEC-SPELL-120-308 section 11, L3): the real CombatLoopWiring, PlayerVitals and EnemyVitals in a
    /// preview scene, three targets inside the usual shapes, three outside, one boss-profile target, an explicit ink pool, the
    /// null presenter. No Play mode, no asset is written, the open scene is not touched. A cast always goes through
    /// SpellResolver (OnLetterDrawn), never through a hand-made SpellCast: a check that injects power hides a broken data path.
    /// Everything a check needs is born hidden and moved into the fixture's own preview scene at once (Born: Obj, Child and the
    /// TEST target builders below); an object that did not arrive is destroyed and the fixture throws. Dispose destroys every
    /// object the fixture made, then closes the preview scene, then destroys what it owns outside a scene (Own). A failed
    /// Create disposes itself. The preview scene has its own physics scene: a rule that asks the wiring's scene physics
    /// (ScenePhysicsQuery) is observable here; CombatLoopWiring.TargetVisible asks the global one and sees no fixture collider.</summary>
    public sealed class SpellFixture308 : IDisposable
    {
        // Which finals the fixture's player owns (the campaign session's role in a real scene). The answer goes through the
        // real policy, as the session's does:
        //   Open    the TEST unlock of the isolated store (what the package fixtures use to reach their glyphs);
        //   Proven  the main-game proof of a final (ledger id and evidence in a normal save). It opens a final only where
        //           the rule handed in says the main game grants it, so a row the data keeps TEST-only (Gate test: the
        //           wiring hands its rule in without the grant) stays locked behind a proof alone.
        public sealed class Unlocks : ISpellUnlocks
        {
            public readonly HashSet<SpellFinal> Open = new HashSet<SpellFinal>();
            public readonly HashSet<SpellFinal> Proven = new HashSet<SpellFinal>();
            public bool FinalUnlocked(SpellUnlockRule rule)
                => SpellUnlockPolicy308.Unlocked(true, rule.GrantedInMain, Proven.Contains(rule.Final), true, Open.Contains(rule.Final), true);
        }

        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public static readonly Vector3 Origin = new Vector3(3000, 3000, 3000);

        public Scene Scene { get; private set; }
        public CombatConfigSO Config { get; private set; }
        public SpellBookSO Book { get; private set; }
        public SpellResolver Resolver { get; private set; }
        public PlayerVitals Player { get; private set; }
        public CombatLoopWiring Wiring { get; private set; }
        public LockOn Lock { get; private set; }
        public InkPool Ink { get; private set; }
        public EnemyVitals[] Inside { get; private set; }
        public EnemyVitals[] Outside { get; private set; }
        public EnemyVitals Boss { get; private set; }
        public EnemyVitalsProfileSO BossProfile { get; private set; }
        public NullSpellPresenter308 Fx { get; } = new NullSpellPresenter308();
        public SpellEffectRegistry308 Registry { get; private set; }
        public Unlocks Finals { get; } = new Unlocks();
        public readonly List<KeyValuePair<char, SpellResolveStatus>> Misfires = new List<KeyValuePair<char, SpellResolveStatus>>();
        public readonly List<SpellCast> Accepted = new List<SpellCast>();
        public readonly List<CastPlan> Plans = new List<CastPlan>();
        public readonly List<EnemyDamageResult> Hits = new List<EnemyDamageResult>();
        private readonly List<Object> _owned = new List<Object>();
        private readonly List<GameObject> _objects = new List<GameObject>();   // every object the fixture made, in birth order

        // book: a book the fixture owns (the caller clones an asset or builds one in memory). effects: the handlers under test
        // (null = no registry at all: the state of an old scene).
        public static SpellFixture308 Create(SpellBookSO book, IEnumerable<ISpellEffect> effects)
        {
            var f = new SpellFixture308 { Book = book };
            try
            {
                if (Application.isPlaying) throw new InvalidOperationException("SpellFixture308 is an Edit Mode fixture.");
                f.Build(effects);
                return f;
            }
            catch
            {
                // a field renamed under a reflection Set, a missing handler: nothing of a half-built fixture stays behind
                f.Dispose();
                throw;
            }
        }

        void Build(IEnumerable<ISpellEffect> effects)
        {
            var f = this; var book = Book;
            f.Scene = EditorSceneManager.NewPreviewScene();
            f.Config = f.Own(ScriptableObject.CreateInstance<CombatConfigSO>());
            Set(f.Config, "_enemyMaxHp", 1000f);
            var player = f.Obj("Spell308_Player", Origin);
            f.Player = player.AddComponent<PlayerVitals>(); Set(f.Player, "_config", f.Config); f.Player.Restore();
            f.Lock = player.AddComponent<LockOn>();
            // three inside the usual shapes (ahead, within every default range), three outside (behind, far, far to the side)
            f.Inside = new[]
            {
                f.Enemy("Spell308_In0", Origin + new Vector3(0f, 0f, 3f)),
                f.Enemy("Spell308_In1", Origin + new Vector3(.8f, 0f, 4.5f)),
                f.Enemy("Spell308_In2", Origin + new Vector3(-.8f, 0f, 6f)),
            };
            f.Outside = new[]
            {
                f.Enemy("Spell308_Out0", Origin + new Vector3(0f, 0f, -5f)),
                f.Enemy("Spell308_Out1", Origin + new Vector3(0f, 0f, 60f)),
                f.Enemy("Spell308_Out2", Origin + new Vector3(40f, 0f, 2f)),
            };
            f.BossProfile = f.Own(ScriptableObject.CreateInstance<EnemyVitalsProfileSO>());
            Set(f.BossProfile, "_isBoss", true); Set(f.BossProfile, "_maxHp", 1000f);
            f.Boss = f.Enemy("Spell308_Boss", Origin + new Vector3(2.5f, 0f, 6.5f));
            f.Boss.ConfigureProfile(f.BossProfile); f.Boss.Restore();
            var all = new List<EnemyVitals>(f.Inside); all.AddRange(f.Outside); all.Add(f.Boss);

            f.Wiring = f.Obj("Spell308_Wiring", Origin).AddComponent<CombatLoopWiring>();
            Set(f.Wiring, "_config", f.Config); Set(f.Wiring, "_enemies", all.ToArray()); Set(f.Wiring, "_playerTransform", player.transform);
            Set(f.Wiring, "_playerVitals", f.Player); Set(f.Wiring, "_lockOn", f.Lock);
            Call(f.Wiring, "CollectControllers");
            f.Resolver = new SpellResolver(book);
            f.Ink = new InkPool(f.Config, null);
            f.Wiring.Construct(f.Resolver, new ParryJudge(f.Config), new GroggyMeter(f.Config), f.Ink);
            if (effects != null)
            {
                f.Registry = new SpellEffectRegistry308(effects);
                f.Wiring.ConstructSpells(f.Registry, f.Finals, f.Fx);
            }
            f.Wiring.CastMisfired += (letter, status) => f.Misfires.Add(new KeyValuePair<char, SpellResolveStatus>(letter, status));
            f.Wiring.CastAccepted += (cast, _, __) => f.Accepted.Add(cast);
            f.Wiring.CastPlanned += plan => f.Plans.Add(plan);
            f.Wiring.EnemyDamageResolved += hit => f.Hits.Add(hit);
            Physics.SyncTransforms();
        }

        // Lock-on target of the next casts (null = no lock: free aim, which depends on the editor's main camera).
        public void Aim(EnemyVitals target)
        {
            Set(Lock, "_target", target); Set(Lock, "_locked", target != null);
        }

        // A drawn letter as the stroke channel would deliver it. worst / seconds are the raw form and speed measures.
        public void Cast(char letter, float worst = .6f, float seconds = 1f, float hold = 1f)
        {
            var drawn = new DrawnLetter(letter, default, default, null, worst, worst, seconds, hold, 3);
            typeof(CombatLoopWiring).GetMethod("OnLetterDrawn", Hidden).Invoke(Wiring, new object[] { drawn });
        }

        // The brush multiplier the book gives the same measures (what a resolved cast must carry).
        public float Brush(float worst = .6f, float seconds = 1f) => Book.EvaluateBrushPower(worst, seconds);

        // Every scheduled hit lands now, in schedule order (the wiring's own tick; nothing is applied by hand).
        public void Land()
        {
            var pending = (IList)typeof(CombatLoopWiring).GetField("_pendingCasts", Hidden).GetValue(Wiring);
            for (int i = 0; i < pending.Count; i++)
            {
                object item = pending[i];
                item.GetType().GetField("ImpactTime").SetValue(item, Time.time);
                pending[i] = item;
            }
            Call(Wiring, "TickPendingCasts");
        }

        public int PendingCount => ((IList)typeof(CombatLoopWiring).GetField("_pendingCasts", Hidden).GetValue(Wiring)).Count;

        public void ClearLog() { Misfires.Clear(); Accepted.Clear(); Plans.Clear(); Hits.Clear(); }

        public void RestoreAll()
        {
            foreach (var enemy in Inside) enemy.Restore();
            foreach (var enemy in Outside) enemy.Restore();
            Boss.Restore(); Player.Restore(); Ink.Restore();
            ((IList)typeof(CombatLoopWiring).GetField("_pendingCasts", Hidden).GetValue(Wiring)).Clear();
            ClearLog();
        }

        // A new object of the fixture scene. It is born hidden and unsaved (no undo entry, no open scene marked dirty), moved
        // into the preview scene at once and checked there: an object that did not arrive is destroyed and the fixture throws.
        // (ObjectFactory.CreateGameObject(scene, ...) left the object in the active scene on Unity 6000.3.9f1: 2026-10-04, A0.)
        // Every object is on the fixture's list, and Dispose destroys the list before it closes the preview scene.
        GameObject Born(string name)
        {
            if (!Scene.IsValid()) throw new InvalidOperationException("SpellFixture308: the fixture scene is not open.");
            var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave);
            _objects.Add(go);
            SceneManager.MoveGameObjectToScene(go, Scene);
            if (go.scene != Scene)
            {
                string where = go.scene.IsValid() ? go.scene.name : "no scene";
                _objects.Remove(go); Object.DestroyImmediate(go);
                throw new InvalidOperationException("SpellFixture308: '" + name + "' did not arrive in the fixture scene (it was in " + where + "); it was destroyed.");
            }
            return go;
        }

        public GameObject Obj(string name, Vector3 position)
        {
            var go = Born(name);
            go.transform.position = position;
            return go;
        }

        public GameObject Child(GameObject parent, string name)
        {
            if (parent == null || parent.scene != Scene) throw new InvalidOperationException("SpellFixture308: the parent of '" + name + "' is not in the fixture scene.");
            var go = Born(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        // The fixture destroys it in Dispose (a profile, a config: anything that lives outside the preview scene).
        public T Own<T>(T item) where T : Object
        {
            if (item != null) _owned.Add(item);
            return item;
        }

        // ---- TEST targets (Q5: the spell rule plus a TEST target), built in the fixture scene from colliders only ----
        // The same shapes the Play helpers make (SpellFieldGate308.CreateTest and its siblings), without a renderer and without
        // ever standing in an open scene: a closed block on a child for a gate, a flat trigger for a hazard patch, a small
        // trigger ball for a projectile.
        public SpellFieldGate308 Gate(SpellFieldGateKind kind, Vector3 position, float size)
        {
            var root = Obj("Spell308_TestGate_" + kind, position);
            var block = Child(root, "Closed");
            block.transform.localPosition = Vector3.up * (size * .5f);
            block.transform.localScale = Vector3.one * size;
            block.AddComponent<BoxCollider>().isTrigger = kind == SpellFieldGateKind.Pollution;
            var gate = root.AddComponent<SpellFieldGate308>();
            gate.Configure("test_" + kind.ToString().ToLowerInvariant(), kind, size * .5f, new[] { block }, null);
            return gate;
        }

        public SpellHazardTestTarget308 Hazard(Vector3 position, float radius)
        {
            var root = Obj("Spell308_TestHazard", position);
            var disc = Child(root, "Patch");
            disc.transform.localScale = new Vector3(radius * 2f, .05f, radius * 2f);
            disc.AddComponent<CapsuleCollider>().isTrigger = true;
            var hazard = root.AddComponent<SpellHazardTestTarget308>();
            hazard.Configure(radius, disc);
            return hazard;
        }

        public SpellProjectileTestTarget308 Projectile(Vector3 position, Vector3 velocity)
        {
            var ball = Obj("Spell308_TestProjectile", position);
            ball.transform.localScale = Vector3.one * .35f;
            ball.AddComponent<SphereCollider>().isTrigger = true;
            var projectile = ball.AddComponent<SpellProjectileTestTarget308>();
            projectile.Relaunch(position, velocity);
            return projectile;
        }

        EnemyVitals Enemy(string name, Vector3 position)
        {
            var vitals = Obj(name, position).AddComponent<EnemyVitals>();
            Set(vitals, "_config", Config); vitals.Restore();
            return vitals;
        }

        public static void Set(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, Hidden);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            info.SetValue(target, value);
        }

        public static object Call(object target, string method, params object[] arguments)
        {
            var info = target.GetType().GetMethod(method, Hidden);
            if (info == null) throw new MissingMethodException(target.GetType().Name, method);
            return info.Invoke(target, arguments.Length == 0 ? null : arguments);
        }

        // Safe to call twice and on a half-built fixture. One object that refuses to die does not keep the rest alive.
        // The fixture's objects go first, one by one (newest first; a child that went with its parent is already null), then
        // the preview scene is closed: nothing depends on "closing the scene destroys what is in it".
        public void Dispose()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                try { if (_objects[i] != null) Object.DestroyImmediate(_objects[i]); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _objects.Clear();
            try { if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); }
            catch (Exception exception) { Debug.LogException(exception); }
            Scene = default;
            foreach (var item in _owned)
            {
                try { if (item != null) Object.DestroyImmediate(item); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            _owned.Clear();
            try { if (Book != null) Object.DestroyImmediate(Book); }
            catch (Exception exception) { Debug.LogException(exception); }
            Book = null;
        }
    }
}
