using System;
using System.Collections.Generic;
using System.Reflection;
using Oheangbu.App;
using Oheangbu.App.Demo;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools
{
    public static class DemoFieldSpellChecks
    {
        const string VisualPath = "Assets/_Project/Art/SpellVFX120/Profiles/020_AD6D.asset";
        const string BookPath = "Assets/_Project/Data/Configs/SpellBook_Proto.asset";
        [Serializable] public sealed class Report
        {
            public string status;
            public List<string> passed = new List<string>(), failed = new List<string>();
            public string[] unverified = { "Actual manual drawing, saved HasDemoGuk hookup and brush visual suppression",
                "PlayerMotor movement/input during lift and actual 2.1-2.3 m authored ledge traversal",
                "Natural scene unload/vehicle events and Play-mode deferred destruction",
                "Visual acceptance, accessibility, sound and performance" };
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Field checks require Edit mode.");
            var report = new Report();
            void Check(bool pass, string message) => (pass ? report.passed : report.failed).Add(message);
            void Group(string label, Action action) { try { action(); } catch (Exception e) { report.failed.Add(label + ": " + e); } }
            var profile = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(VisualPath);
            var book = AssetDatabase.LoadAssetAtPath<SpellBookSO>(BookPath);
            if (profile == null || book == null) { report.failed.Add("Missing actual WoodLift or spell-book source"); report.status = "FAIL"; return JsonUtility.ToJson(report, true); }
            string profileBefore = EditorJsonUtility.ToJson(profile), bookBefore = EditorJsonUtility.ToJson(book);
            Group("Resolver", () =>
            {
                var resolver = new SpellResolver(book);
                Check(!resolver.TryResolve(Guk(), out _) && !book.TryGet('국', out _), "Shared prototype book remains unchanged and legacy resolution does not enable 국");
                Check(resolver.TryResolve(Guk(), out var cast, true) && cast.Kind == SpellKind.Field && cast.Letter == '국' && cast.Element == Element.Wood && cast.Power == 0,
                    "Explicit demo unlock resolves 국 once as a non-damaging Field cast");
            });
            foreach (int fps in new[] { 30, 60, 120 })
            {
                int rate = fps; Group("Lift " + fps, () => Lift(profile, book, rate, Check));
            }
            Group("Payment and invalid placement", () => Payment(profile, book, Check));
            Group("Dynamic ceiling", () => Ceiling(profile, book, Check));
            Group("Disable safety hold", () => Safety(profile, book, Check));
            Group("Death and vehicle", () => Cleanup(profile, book, Check));
            Check(EditorJsonUtility.ToJson(profile) == profileBefore && EditorJsonUtility.ToJson(book) == bookBefore,
                "Actual WoodLift profile and shared spell-book assets remain unchanged in memory");
            report.status = report.failed.Count == 0 ? "PASS" : "FAIL";
            return JsonUtility.ToJson(report, true);
        }

        public static string RunMountains()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Detached fixture only");
            var report=new Report();void Check(bool pass,string name)=>(pass?report.passed:report.failed).Add(name);
            var profile=AssetDatabase.LoadAssetAtPath<Vfx120Profile>(VisualPath);var book=AssetDatabase.LoadAssetAtPath<SpellBookSO>(BookPath);
            foreach(float height in new[]{8f,16f,24f})foreach(int fps in new[]{30,60,120})
            using(var f=new Fixture(profile,book))
            {
                var obj=new GameObject("Authored mountain site");SceneManager.MoveGameObjectToScene(obj,f.Scene);var site=obj.AddComponent<GukLiftSite>();site.Id="fixture-mountain";
                site.Lower=new GameObject("Lower").transform;site.Lower.SetParent(obj.transform);site.Lower.position=Vector3.zero;
                site.Upper=new GameObject("Upper").transform;site.Upper.SetParent(obj.transform);site.Upper.position=new Vector3(1.1f,height,0);site.RiseSeconds=5;
                f.Box("Permanent upper ledge",new Vector3(1.55f,height-.1f,0),new Vector3(1.2f,.2f,2));f.Sync();Call(site,"OnEnable");
                try
                {
                    f.Service.CombatBlocked=()=>true;f.Cast();Check(!f.Service.HasPlatform&&Near(f.Ink.Value,1),height+"m/"+fps+": combat rejects before ink");
                    f.Service.CombatBlocked=()=>false;f.Cast();Check(f.Accepted==1&&f.Service.ActiveSiteId==site.Id,height+"m/"+fps+": authored node selected, single charge ("+f.Service.LastFailure+")");
                    float top=0;for(int i=0;i<fps*6;i++){f.Tick(1f/fps);top=Mathf.Max(top,f.Service.CurrentHeight);}
                    Check(Near(top,height)&&f.Service.State==FieldLiftState.Holding&&f.Service.PassengerSupported,height+"m/"+fps+": actual controller reaches amplified support");
                    f.Service.RequestRelease();for(int i=0;i<fps*7&&f.Service.HasPlatform;i++)f.Tick(1f/fps);
                    Check(!f.Service.HasPlatform&&Near(f.FeetY,.05f),height+"m/"+fps+": safe descent to permanent ground");
                }
                catch(Exception e){report.failed.Add(height+"/"+fps+": "+e);}
                finally{Call(site,"OnDisable");}
            }
            report.status=report.failed.Count==0?"PASS":"FAIL";
            string json=JsonUtility.ToJson(report,true);System.IO.File.WriteAllText("../Art/World/Compact/Rebuild/Mountain290/guk-fixtures.json",json);return json;
        }

        static void Lift(Vfx120Profile profile, SpellBookSO book, int fps, Action<bool, string> check)
        {
            using (var f = new Fixture(profile, book))
            {
                float start = f.FeetY; f.Cast();
                check(f.Service.State == FieldLiftState.Rising && f.Accepted == 1 && Near(f.Ink.Value, 1f - f.Config.SpellInkCost),
                    fps + " fps: real central cast pays exactly one normal spell cost after valid preparation");
                float maxStep = 0;
                for (int i = 0; i < fps * 3; i++) { float before = f.FeetY; f.Tick(1f / fps); maxStep = Mathf.Max(maxStep, Mathf.Abs(f.FeetY - before)); }
                check(f.Service.State == FieldLiftState.Holding && Near(f.Service.CurrentHeight, FieldSpellService.MaximumHeight) &&
                    Near(f.FeetY - start, FieldSpellService.MaximumHeight) && maxStep <= FieldSpellService.RiseSpeed / fps + .02f,
                    fps + " fps: actual CharacterController rises 2.4 m in bounded collision movement (height=" + f.Service.CurrentHeight + ", feet delta=" + (f.FeetY - start) + ")");
                var effect = f.Service.PlatformObject.GetComponentInChildren<Vfx120Effect>(true);
                check(effect != null && effect.WoodLiftHasPlan && Near(effect.WoodLiftHeight, f.Service.CurrentHeight) && effect.Profile == profile,
                    fps + " fps: existing approved WoodLift samples authoritative height without a replacement model");
                float height = f.Service.CurrentHeight, feet = f.FeetY, age = effect.Age;
                f.Paused = true; for (int i = 0; i < 30; i++) f.Tick(.1f);
                f.Paused = false; for (int i = 0; i < 10; i++) f.Tick(0);
                check(Near(height, f.Service.CurrentHeight) && Near(feet, f.FeetY) && Near(age, effect.Age),
                    fps + " fps: pause and zero scaled time preserve platform, passenger and visual clock");
                f.Service.RequestRelease();
                for (int i = 0; i < fps * 4 && f.Service.HasPlatform; i++) f.Tick(1f / fps);
                check(!f.Service.HasPlatform && Near(f.FeetY, start) && f.OwnedRoots == 0,
                    fps + " fps: release lowers passenger to the original real floor before removing support");
            }
        }

        static void Payment(Vfx120Profile profile, SpellBookSO book, Action<bool, string> check)
        {
            using (var f = new Fixture(profile, book))
            {
                f.Ink.Restore(.01f); f.Cast();
                check(!f.Service.HasPlatform && f.OwnedRoots == 0 && Near(f.Ink.Value, .01f) && f.Accepted == 0,
                    "Insufficient real InkPool balance removes the prepared candidate without a charge or accepted cast");
            }
            using (var f = new Fixture(profile, book))
            {
                f.Box("Low ceiling", new Vector3(0, 2.6f, 0), new Vector3(3, .2f, 3)); f.Cast();
                check(!f.Service.HasPlatform && Near(f.Ink.Value, 1) && f.Accepted == 0,
                    "Static low ceiling rejects full-height capsule clearance before spending ink");
            }
            using (var f = new Fixture(profile, book))
            {
                f.Ground.GetComponent<BoxCollider>().size = new Vector3(.5f, 1, .5f); f.Sync(); f.Cast();
                check(!f.Service.HasPlatform && Near(f.Ink.Value, 1), "Unsupported deck edges reject placement before spending ink");
            }
            using (var f = new Fixture(profile, book))
            {
                f.Cast(); var original = f.Service.PlatformObject; float ink = f.Ink.Value; f.Cast();
                check(f.Service.PlatformObject == original && Near(f.Ink.Value, ink) && f.Accepted == 1 && f.OwnedRoots == 1,
                    "Repeated cast cannot replace occupied support, pay twice or create another lift");
            }
            using (var f = new Fixture(profile, book))
            {
                f.Unlocked = false;
                var field = new SpellCast('국', SpellKind.Field, Element.Wood, 0, default, 1);
                check(!f.Service.TryPrepare(field, out _) && !f.Service.HasPlatform, "Unconfirmed demo unlock cannot be bypassed with a constructed Field cast");
            }
        }

        static void Ceiling(Vfx120Profile profile, SpellBookSO book, Action<bool, string> check)
        {
            using (var f = new Fixture(profile, book))
            {
                f.Cast(); for (int i = 0; i < 20; i++) f.Tick(.025f);
                const float ceilingBottom = 2.8f;
                f.Box("Inserted ceiling", new Vector3(0, ceilingBottom + .1f, 0), new Vector3(3, .2f, 3));
                float highestTop = 0; bool descended = false;
                f.Service.StateChanged += state => { if (state == FieldLiftState.Descending) descended = true; };
                for (int i = 0; i < 220 && f.Service.HasPlatform; i++) { f.Tick(.025f); highestTop = Mathf.Max(highestTop, f.FeetY + f.Body.height); }
                check(descended && highestTop <= ceilingBottom + .04f && !f.Service.HasPlatform && Near(f.FeetY, .05f),
                    "New ceiling triggers real CharacterController collision, stops ascent and returns to safe floor (top=" + highestTop + ")");
            }
        }

        static void Safety(Vfx120Profile profile, SpellBookSO book, Action<bool, string> check)
        {
            using (var f = new Fixture(profile, book))
            {
                f.Cast(); for (int i = 0; i < 90; i++) f.Tick(1f / 30f);
                float feet = f.FeetY; var platform = f.Service.PlatformObject;
                f.Service.enabled = false; Call(f.Service, "OnDisable");
                var hold = platform.GetComponent<FieldSpellSafetyHold>();
                for (int i = 0; i < 10; i++) Call(hold, "LateUpdate");
                check(hold != null && platform != null && Near(f.FeetY, feet) && f.OwnedRoots == 1,
                    "Disabled service preserves stationary support under the living passenger instead of forcing a fall");
                f.Box("Safe adjacent ledge", new Vector3(1.25f, 2.2f, 0), new Vector3(1.7f, .4f, 2));
                f.Body.Move(Vector3.right * 1.25f); f.Sync(); Call(hold, "LateUpdate");
                check(platform == null && f.OwnedRoots == 0 && Near(f.FeetY, feet),
                    "Safety holder removes actual owned objects only after the player walks onto adjacent stable support");
            }
            using (var f = new Fixture(profile, book))
            {
                f.Cast(); for (int i = 0; i < 1000 && f.Service.HasPlatform; i++) f.Tick(.03f);
                check(!f.Service.HasPlatform && Near(f.FeetY, .05f), "Natural TEST hold expiry descends to ground instead of deleting occupied support");
            }
        }

        static void Cleanup(Vfx120Profile profile, SpellBookSO book, Action<bool, string> check)
        {
            foreach (bool vehicle in new[] { false, true })
                using (var f = new Fixture(profile, book))
                {
                    f.Cast(); f.Tick(.3f);
                    if (vehicle) { f.Seated = true; f.Body.enabled = false; f.Tick(.01f); }
                    else f.Player.TakeDamage(float.MaxValue);
                    check(!f.Service.HasPlatform && f.OwnedRoots == 0,
                        vehicle ? "Vehicle ownership disables the body and clears lift objects" : "Real player death immediately clears lift objects");
                }
        }

        static DrawnLetter Guk() => new DrawnLetter('국', default, default, null, .8f, .7f, 2f, 2f, 4);
        sealed class Fixture : IDisposable
        {
            public readonly Scene Scene;
            public readonly CharacterController Body;
            public readonly PlayerVitals Player;
            public readonly GameObject Ground;
            public readonly CombatConfigSO Config;
            public readonly InkPool Ink;
            public readonly CombatLoopWiring Wiring;
            public readonly FieldSpellService Service;
            public bool Unlocked = true, Seated, Paused;
            public int Accepted;
            public float FeetY => Body.transform.TransformPoint(Body.center).y - Body.height * .5f;
            public int OwnedRoots { get { int count = 0; foreach (var root in Scene.GetRootGameObjects()) if (root.name == "Demo_GukLift") count++; return count; } }
            public Fixture(Vfx120Profile profile, SpellBookSO book)
            {
                Scene = EditorSceneManager.NewPreviewScene(); Config = ScriptableObject.CreateInstance<CombatConfigSO>();
                Ground = Box("Ground", new Vector3(0, -.5f, 0), new Vector3(30, 1, 30));
                var player = Object("Real field player", new Vector3(0, .05f, 0));
                Body = player.AddComponent<CharacterController>(); Body.height = 1.75f; Body.center = Vector3.up * .875f; Body.radius = .27f; Body.skinWidth = .015f; Body.minMoveDistance = 0;
                Player = player.AddComponent<PlayerVitals>(); Set(Player, "_config", Config); Player.Restore();
                var host = Object("Field runtime wiring", Vector3.zero); Service = host.AddComponent<FieldSpellService>();
                Service.Configure(Body, null, Player, profile, () => Unlocked, () => Seated, () => Paused);
                Wiring = host.AddComponent<CombatLoopWiring>(); Set(Wiring, "_config", Config); Set(Wiring, "_playerTransform", player.transform);
                Ink = new InkPool(Config, null); Wiring.Construct(new SpellResolver(book), new ParryJudge(Config), new GroggyMeter(Config), Ink);
                Wiring.FieldSpells = Service; Wiring.CastAccepted += (_, __, ___) => Accepted++; Sync();
            }
            GameObject Object(string name, Vector3 position)
            { var obj = new GameObject(name); SceneManager.MoveGameObjectToScene(obj, Scene); obj.transform.position = position; return obj; }
            public GameObject Box(string name, Vector3 position, Vector3 size)
            { var obj = Object(name, position); obj.AddComponent<BoxCollider>().size = size; Sync(); return obj; }
            public void Cast() { Call(Wiring, "OnLetterDrawn", Guk()); }
            public void Tick(float dt) { Service.Tick(dt); Sync(); }
            public void Sync() { Physics.SyncTransforms(); var physics = Scene.GetPhysicsScene(); if (!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("Local physics required"); physics.Simulate(.001f); }
            public void Dispose()
            {
                if (Player != null && Player.Hp01 > 0f) Player.TakeDamage(float.MaxValue);
                if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene); UnityEngine.Object.DestroyImmediate(Config);
            }
        }
        static bool Near(float a, float b) => Mathf.Abs(a - b) < .025f;
        static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
