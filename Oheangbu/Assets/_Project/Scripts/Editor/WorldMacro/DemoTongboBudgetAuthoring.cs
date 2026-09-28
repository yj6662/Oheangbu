using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.Data.Demo;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Four demo-owned reward fields only. Never enables a campaign stage or touches a user save.
    public static class DemoTongboBudgetAuthoring
    {
        static WorldMacroPlaytestSession Session => Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
        static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Demo/EncounterExpansion"));
        static readonly string[] OptionalExisting = { "demo_logging_branch_01", "demo_logging_branch_02", "demo_logging_branch_03" };
        static readonly string[] OptionalGroups = { "g08_tree_return", "e01_root_warden" };
        [Serializable] sealed class RewardRow { public string id, owner; public int current, proposed; public bool implemented; }
        [Serializable] sealed class Budget
        {
            public int stages, requiredOrdinaryKills, minimumCompletionGross, minimumBeforeGeneralGross;
            public int mainAllFirstKillsGross, mainWithoutLessonKillGross, optionalAllFirstKillsGross, allFirstKillsGross;
            public int currentlyEnabledPrefixStageReward, currentlyEnabledPrefixMinimumGross;
        }
        [Serializable] sealed class Report
        {
            public string status, scope; public RewardRow[] rewards; public Budget current, proposed;
            public int registeredOrdinary, registeredElite, registeredLesson, registeredBoss, registeredOptionalActors, shops;
            public bool finalPopulationPresent; public string[] checks, limitations;
        }
        public static string Execute(string command)
        {
            Require(); Directory.CreateDirectory(Output);
            if (command == "survey" || command == "audit") return Save(command, ReportNow(command == "audit"));
            if (command == "apply") return Apply();
            if (command == "tests") { string json = RunTransactionChecks(); File.WriteAllText(Path.Combine(Output, "tongbo_transaction_tests.json"), json); return json; }
            throw new ArgumentException("survey/apply/audit/tests only");
        }
        static void Require()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Session == null || Session.gameObject.scene.path != DemoFoundationAuthoring.Scene ||
                AssetDatabase.GetAssetPath(Session.Content) != DemoFoundationAuthoring.Folder + "/Content.asset" ||
                AssetDatabase.GetAssetPath(Session.Content.Campaign) != DemoFoundationAuthoring.Folder + "/Campaign.asset" ||
                !Session.Content.Campaign.IsValid || Session.Content.TestRules == null || Session.Content.TestRules.EnemyReward != 12)
                throw new InvalidOperationException("Dedicated demo Edit scene, original campaign/content and unchanged EnemyReward=12 required");
            foreach (string id in new[] { "worker_satchel", "demo_logging_cache", "cargo_delivery", "guk_high_reward" })
                if (Session.Content.Points.Count(p => p != null && p.Id == id) != 1) throw new InvalidOperationException("Missing/duplicate point " + id);
            foreach (string id in new[] { "delivery", "south_gate", "ending", "guk_return" })
                if (Session.Content.Campaign.Stages.Count(s => s.Id == id) != 1) throw new InvalidOperationException("Missing/duplicate stage " + id);
            if (Session.Content.Points.Single(p => p.Id == "cargo_delivery").Currency != 0 ||
                Session.Content.Points.Single(p => p.Id == "guk_high_reward").Currency != 0 ||
                Session.Content.Campaign.Stages.Single(s => s.Id == "ending").TongboReward != 0)
                throw new InvalidOperationException("Delivery/Guk point or gate reward would add a second reward owner; review before rebalance");
        }
        static int PointAmount(string id, int current, bool proposed) => proposed && (id == "worker_satchel" || id == "demo_logging_cache") ? 160 : current;
        static int StageAmount(string id, int current, bool proposed) => proposed && id == "delivery" ? 340 : proposed && id == "south_gate" ? 120 : current;
        static Budget Calculate(bool proposed)
        {
            var content = Session.Content; var profile = content.Campaign;
            var required = profile.Stages.SelectMany(s => s.RequiredDefeatedIds ?? Array.Empty<string>()).Distinct().Where(id => id != "cheongryong" && id != "south_gate_general").ToArray();
            int stages = profile.Stages.Sum(s => StageAmount(s.Id, s.TongboReward, proposed));
            var prefix = profile.Stages.TakeWhile(s => s.Implemented).ToArray();
            int prefixStages = prefix.Sum(s => StageAmount(s.Id, s.TongboReward, proposed));
            int prefixKills = prefix.SelectMany(s => s.RequiredDefeatedIds ?? Array.Empty<string>()).Distinct().Count(id => id != "cheongryong" && id != "south_gate_general");
            int optionalPoints = content.Points.Where(p => p.Id == "worker_satchel" || p.Id == "demo_logging_cache").Sum(p => PointAmount(p.Id, p.Currency, proposed));
            int finalReward = profile.Stages.Where(s => s.Id == "south_gate" || s.Id == "ending").Sum(s => StageAmount(s.Id, s.TongboReward, proposed));
            return new Budget { stages = stages, requiredOrdinaryKills = required.Length, minimumCompletionGross = stages + required.Length * 12,
                minimumBeforeGeneralGross = stages + required.Length * 12 - finalReward,
                mainAllFirstKillsGross = stages + 27 * 12 + 12, mainWithoutLessonKillGross = stages + 27 * 12,
                optionalAllFirstKillsGross = 7 * 12 + optionalPoints, allFirstKillsGross = stages + 35 * 12 + optionalPoints,
                currentlyEnabledPrefixStageReward = prefixStages, currentlyEnabledPrefixMinimumGross = prefixStages + prefixKills * 12 };
        }
        static Report ReportNow(bool audit)
        {
            var checks = new List<string>(); bool pass = true;
            void C(bool ok, string text) { checks.Add((ok ? "PASS " : "FAIL ") + text); pass &= ok; }
            var c = Session.Content; var p = c.Campaign;
            var required = p.Stages.SelectMany(s => s.RequiredDefeatedIds ?? Array.Empty<string>()).Distinct().OrderBy(s => s).ToArray();
            C(required.SequenceEqual(new[] { "demo_logging_01", "demo_logging_02", "demo_logging_03" }), "Only the existing logging three are required generic kills; no new kill gates");
            C(p.Stages.Where(s => s.Id != "delivery" && s.Id != "south_gate").Sum(s => s.TongboReward) == 400, "Existing 400 stage Tongbo includes mandatory Guk 60");
            C(c.Points.Where(v => v.Id != "worker_satchel" && v.Id != "demo_logging_cache").All(v => v.Currency == 0), "No uncounted point currency or duplicate stage-point reward");
            C(c.Economy != null && c.Economy.StoneCosts.SequenceEqual(new[] { 60, 120, 240 }) && c.Economy.CapacityCosts.SequenceEqual(new[] { 100, 200 }), "Existing stone/HP/ink prices unchanged");
            C(Session.Actors.All(a => a != null && a.Session == null), "No registered actor also pays a legacy PrologueSession ledger");
            if (audit) C(c.Points.Single(v => v.Id == "worker_satchel").Currency == 160 && c.Points.Single(v => v.Id == "demo_logging_cache").Currency == 160 &&
                p.Stages.Single(s => s.Id == "delivery").TongboReward == 340 && p.Stages.Single(s => s.Id == "south_gate").TongboReward == 120, "Four intended reward amounts saved");
            var actors = Session.Actors; int bosses = actors.Count(a => a.Id == "cheongryong" || a.Id == "south_gate_general");
            int lesson = actors.Count(a => a.Id == "demo_growth_lesson"); int elite = actors.Count(a => a.GetComponent<DemoEncounterExpansionTag>()?.IsElite == true);
            int ordinary = actors.Length - bosses - lesson - elite;
            var rows = c.Points.Where(v => v.Id == "worker_satchel" || v.Id == "demo_logging_cache").Select(v => new RewardRow { id = v.Id, owner = "Content.Points.Currency", current = v.Currency, proposed = 160, implemented = true })
                .Concat(p.Stages.Select(s => new RewardRow { id = s.Id, owner = "Campaign.Stage.TongboReward", current = s.TongboReward, proposed = StageAmount(s.Id, s.TongboReward, true), implemented = s.Implemented })).ToArray();
            var progress = WorldMacroProgress.CreateNew(c.TerrainRevision, c.StartFeet, c.StartYaw); progress.campaign.CampaignId = p.CampaignId;
            return new Report { status = pass ? audit ? "PASS_DATA_ONLY" : "PROPOSAL" : "BLOCKED", scope = "First-kill gross-income forecast for planned 32 ordinary + 2 elite. Not guaranteed, net spending, farming cap or progression activation.", rewards = rows,
                current = Calculate(false), proposed = Calculate(true), registeredOrdinary = ordinary, registeredElite = elite, registeredLesson = lesson, registeredBoss = bosses,
                finalPopulationPresent = ordinary == 32 && elite == 2 && bosses == 2 && lesson == 1,
                registeredOptionalActors = actors.Count(a => OptionalExisting.Contains(a.Id) || OptionalGroups.Contains(a.GetComponent<DemoEncounterExpansionTag>()?.GroupId)),
                shops = c.Points.Count(v => v.Kind == PrologueInteractionKind.Rest && WorldMacroCheckpointRules.TryResolve(c, progress, v.Id, out var cp) && cp.Shop), checks = checks.ToArray(),
                limitations = new[] { "Guk return is currently mandatory before escort, despite an earlier optional-branch design description", "Lesson interruption pays 40; killing its actor adds optional 12", "Boss owners pay campaign reward only, without generic EnemyReward", "Respawn-on-rest clears defeated IDs; repeated kills can pay again", "Ordinary death reward mutates live progress then saves; its existing save-failure behavior is unchanged", "Current disabled stages remain disabled; forecast completion requires separately verified activation", "Actual route access, all new encounters, transactions through live shop UI, death/loss and whole-journey balance need root play checks" } };
        }
        static string Save(string name, Report report) { string json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Output, "tongbo_" + name + ".json"), json); return json; }
        static string Disk(string asset) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", asset));
        static string Hash(Object asset) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Disk(AssetDatabase.GetAssetPath(asset))))); }
        static void SetProposed(WorldMacroPlaytestSO content, DemoCampaignProfile campaign)
        {
            content.Points.Single(v => v.Id == "worker_satchel").Currency = 160;
            content.Points.Single(v => v.Id == "demo_logging_cache").Currency = 160;
            campaign.Stages.Single(v => v.Id == "delivery").TongboReward = 340;
            campaign.Stages.Single(v => v.Id == "south_gate").TongboReward = 120;
        }
        static string Apply()
        {
            if (ReportNow(false).status == "BLOCKED") return Save("survey", ReportNow(false));
            var c = Session.Content; var p = c.Campaign;
            if (EditorUtility.IsDirty(c) || EditorUtility.IsDirty(p)) throw new InvalidOperationException("Save demo Content/Campaign before reward authoring so backup matches the loaded baseline");
            string beforeC = EditorJsonUtility.ToJson(c), beforeP = EditorJsonUtility.ToJson(p), ruleHash = Hash(c.TestRules), priceHash = Hash(c.Economy);
            int oldWorker = c.Points.Single(v => v.Id == "worker_satchel").Currency, oldCache = c.Points.Single(v => v.Id == "demo_logging_cache").Currency;
            int oldDelivery = p.Stages.Single(v => v.Id == "delivery").TongboReward, oldGeneral = p.Stages.Single(v => v.Id == "south_gate").TongboReward;
            string backup = Path.Combine(Output, "Backups", "rewards_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")); Directory.CreateDirectory(backup);
            foreach (var asset in new Object[] { c, p }) File.Copy(Disk(AssetDatabase.GetAssetPath(asset)), Path.Combine(backup, Path.GetFileName(AssetDatabase.GetAssetPath(asset))));
            try
            {
                SetProposed(c, p);
                // Restoring only the four original amounts must reproduce every original serialized field,
                // including Implemented, required kills, IDs, geometry, dialogue and asset references.
                c.Points.Single(v => v.Id == "worker_satchel").Currency = oldWorker;
                c.Points.Single(v => v.Id == "demo_logging_cache").Currency = oldCache;
                p.Stages.Single(v => v.Id == "delivery").TongboReward = oldDelivery;
                p.Stages.Single(v => v.Id == "south_gate").TongboReward = oldGeneral;
                if (EditorJsonUtility.ToJson(c) != beforeC || EditorJsonUtility.ToJson(p) != beforeP) throw new InvalidOperationException("Reward authoring changed unrelated data");
                SetProposed(c, p);
                if (Hash(c.TestRules) != ruleHash || Hash(c.Economy) != priceHash || ReportNow(true).status != "PASS_DATA_ONLY") throw new InvalidOperationException("Reward/source audit rejected");
                EditorUtility.SetDirty(c); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(c); AssetDatabase.SaveAssetIfDirty(p);
                return Save("audit", ReportNow(true));
            }
            catch
            {
                EditorJsonUtility.FromJsonOverwrite(beforeC, c); EditorJsonUtility.FromJsonOverwrite(beforeP, p);
                EditorUtility.SetDirty(c); EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(c); AssetDatabase.SaveAssetIfDirty(p); throw;
            }
        }

        [Serializable] sealed class TestReport { public int passed, failed; public string[] checks; public string scope = "Isolated real economy service + AtomicJsonStore round trips; no scene, user save, or live shop UI"; }
        sealed class TestStore : IDemoEconomyStore
        {
            public WorldMacroProgress Live; public AtomicJsonStore<WorldMacroProgress> DiskStore; public string FailurePath;
            public DemoEconomySnapshot Read() => new DemoEconomySnapshot(Live.ledger.currency, Live.economy);
            public DemoEconomyCommitStatus TryCommit(DemoEconomySnapshot expected, DemoEconomySnapshot next, out string error)
            {
                error = null; if (!Read().SameAs(expected)) return DemoEconomyCommitStatus.Conflict;
                var candidate = JsonUtility.FromJson<WorldMacroProgress>(JsonUtility.ToJson(Live)); candidate.ledger.currency = next.Tongbo; candidate.economy = next.State;
                try { (FailurePath == null ? DiskStore : new AtomicJsonStore<WorldMacroProgress>(FailurePath, WorldMacroProgress.Valid)).Save(candidate); Live = candidate; return DemoEconomyCommitStatus.Saved; }
                catch (IOException e) { error = e.Message; return DemoEconomyCommitStatus.Failed; }
            }
        }
        static string RunTransactionChecks()
        {
            var checks = new List<string>(); int passed = 0, failed = 0;
            void C(bool ok, string text) { checks.Add((ok ? "PASS " : "FAIL ") + text); if (ok) passed++; else failed++; }
            string directory = Path.Combine(Path.GetTempPath(), "OheangbuTongboBudget_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "isolated.json"), blocker = Path.Combine(directory, "not-a-directory");
            try
            {
                var c = Session.Content; string contentJson = EditorJsonUtility.ToJson(c), campaignJson = EditorJsonUtility.ToJson(c.Campaign), prices = Hash(c.Economy), rules = Hash(c.TestRules);
                var state = WorldMacroProgress.CreateNew(c.TerrainRevision, c.StartFeet, c.StartYaw); state.campaign.CampaignId = c.Campaign.CampaignId; state.ledger.currency = 776;
                var disk = new AtomicJsonStore<WorldMacroProgress>(path, WorldMacroProgress.Valid); disk.Save(state);
                var store = new TestStore { Live = state, DiskStore = disk }; var service = new DemoEconomyService(c.Economy.CreateRules(), store);
                byte[] before = File.ReadAllBytes(path); File.WriteAllText(blocker, "Intentional isolated IO failure"); store.FailurePath = Path.Combine(blocker, "rejected.json");
                C(!service.TryPurchase(DemoUpgradeTrack.Health, 0, "health-1", out var receipt) && receipt.Status == DemoPurchaseStatus.SaveFailed && store.Live.ledger.currency == 776 && store.Live.economy.Level(DemoUpgradeTrack.Health) == 0 && !store.Live.economy.purchaseIds.Contains("health-1") && before.SequenceEqual(File.ReadAllBytes(path)), "Real write failure preserves Tongbo, level, request and durable file");
                store.FailurePath = null;
                C(service.TryPurchase(DemoUpgradeTrack.Health, 0, "health-1", out receipt) && receipt.Charged == 100 && store.Live.ledger.currency == 676, "Same failed request retries once at actual HP price 100");
                C(!service.TryPurchase(DemoUpgradeTrack.Health, 0, "health-1", out receipt) && receipt.Status == DemoPurchaseStatus.DuplicateRequest && store.Live.ledger.currency == 676, "Duplicate accepted request cannot pay twice");
                int[] costs = { 60, 120, 240 };
                for (int tier = 0; tier < 3; tier++) C(service.TryPurchase(DemoUpgradeTrack.Metal, tier, "metal-" + tier, out receipt) && receipt.Charged == costs[tier], "Real stone tier price " + costs[tier]);
                C(service.TryPurchase(DemoUpgradeTrack.Health, 1, "health-2", out receipt) && receipt.Charged == 200 && store.Live.ledger.currency == 56, "776 minimum pre-boss gross buys stone 420 + HP 300, leaving 56");
                store.Live = disk.Load(); service = new DemoEconomyService(c.Economy.CreateRules(), store);
                C(store.Live.ledger.currency == 56 && store.Live.economy.Level(DemoUpgradeTrack.Metal) == 3 && store.Live.economy.Level(DemoUpgradeTrack.Health) == 2, "Reload restores exact spend and levels");
                C(!service.TryPurchase(DemoUpgradeTrack.Ink, 0, "ink-unaffordable", out receipt) && receipt.Status == DemoPurchaseStatus.InsufficientTongbo && disk.Load().ledger.currency == 56, "Insufficient 56 cannot buy first ink capacity at 100");
                C(WorldMacroCheckpointRules.TryProposeRest(c, store.Live, "geumpyo_inn", c.InnCheckpointFeet, out var rested, out _), "Real rest proposal at a configured shop");
                if (rested != null) { disk.Save(rested); store.Live = disk.Load(); }
                C(store.Live.ledger.currency == 56 && store.Live.economy.Level(DemoUpgradeTrack.Metal) == 3 && store.Live.economy.Level(DemoUpgradeTrack.Health) == 2 && service.TryQuote(DemoUpgradeTrack.Ink, out var quote, out _) && quote.Cost == 100, "Returning after rest retains spend, upgrades and unmodified next price");
                var earned = WorldMacroProgress.CreateNew(c.TerrainRevision, c.StartFeet, c.StartYaw);
                C(earned.Defeat("ordinary", 12) && !earned.Defeat("ordinary", 12) && earned.ledger.currency == 12, "Duplicate ordinary defeat before rest pays once");
                C(PrologueProgressStore.Complete(earned.ledger, "worker_satchel", 160) && !PrologueProgressStore.Complete(earned.ledger, "worker_satchel", 160) && earned.ledger.currency == 172, "One-time optional point pays once");
                var budget = Calculate(true);
                C(budget.minimumCompletionGross == 896 && budget.mainAllFirstKillsGross == 1196 && budget.mainWithoutLessonKillGross == 1184 && budget.optionalAllFirstKillsGross == 404 && budget.allFirstKillsGross == 1600, "Proposed gross budget separates required, main potential and optional");
                C(contentJson == EditorJsonUtility.ToJson(c) && campaignJson == EditorJsonUtility.ToJson(c.Campaign) && prices == Hash(c.Economy) && rules == Hash(c.TestRules), "Fixture preserves original content, Implemented flags, campaign, reward source and prices");
            }
            catch (Exception e) { C(false, e.GetType().Name + ": " + e.Message); }
            finally
            {
                foreach (string file in new[] { path, path + ".bak", path + ".tmp", blocker }) if (File.Exists(file)) File.Delete(file);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            return JsonUtility.ToJson(new TestReport { passed = passed, failed = failed, checks = checks.ToArray() }, true);
        }
    }
}
