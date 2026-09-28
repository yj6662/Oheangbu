using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Oheangbu.App.World;
using Oheangbu.App.Prologue;
using Oheangbu.App.Demo;
using Oheangbu.Combat;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #297 presentation-only reskin of the first four encounters (first cave, Cheongrim road), which still wear
    // the player's own summon tiger (Enemy_TEMP_Visual → MetalTiger_Body) as a placeholder. Approved #298 folklore
    // visual + motion + SFX are attached; every gameplay component (Id, EnemyVitals, EnemyController/attack
    // profile and timing, patrol, NavMeshAgent, capsule, encounter spec, campaign ties) must digest identically.
    //   dry | apply | revert           — active scene only (apply/revert: saved, clean, Edit mode)
    //   dry:<actorId>=<model>,...      — explicit override of the TEST mapping (apply accepts the same)
    public static partial class CompactFolklore298
    {
        const string Placeholder297 = "Enemy_TEMP_Visual", Generated297 = "Folklore298_Visual";
        // TEST mapping: actor Id → preferred model. A model that does not fit the actor's unchanged
        // NavMeshAgent is substituted by the best-fitting ordinary humanoid (reported, never silent).
        static readonly (string actor, string model)[] ReskinMap297 =
            { ("mine_beast/0", "changgui"), ("mine_beast/1", "agwi"), ("village_road_raider_0", "dokkaebi"), ("village_road_raider_1", "dokkaebi") };
        // TEST fit limits: measured visual height and the #298 authored body radius against the actor's nav capsule.
        const float MinHeightRatio297 = .8f, MaxHeightRatio297 = 1.25f, MaxRadiusRatio297 = 1.75f;
        static string ReskinRecordPath297 => Path.Combine(RepositoryRoot, "Art/World/Compact/Rebuild/Finish297/Reskin/original.json");

        [Serializable] sealed class ReskinRef297 { public string path, gid; }
        [Serializable] sealed class ReskinRow297
        {
            public string actorId, model, preferred, reason;
            public string placeholderName, placeholderGid; public bool placeholderActive;
            public bool hadRig273; public string rig273Json; public ReskinRef297[] rig273Refs = Array.Empty<ReskinRef297>();
            public string rendererGid; public float deathVisualSeconds, newDeathVisualSeconds, attackPeak01;
            public float navRadius, navHeight, navBaseOffset, capsuleRadius, capsuleHeight; public Vector3 capsuleCenter;
            public float heightRatio, radiusRatio; public string readability, gameplayHash;
        }
        [Serializable] sealed class ReskinRecord297 { public string utc, revertedUtc, scene, status; public ReskinRow297[] rows = Array.Empty<ReskinRow297>(); }
        sealed class ModelFit297 { public ModelRow row; public float height, radius, halfWidth, deathLength, attackPeak; public string error; }
        sealed class ReskinPlan297
        {
            public string actorId, preferred, reason, state; public PrologueEncounter actor; public ModelFit297 model;
            public float heightRatio, radiusRatio; public readonly List<string> errors = new List<string>();
        }

        public static string Reskin297(string argument)
        {
            RequireEdit();
            string command = (argument ?? "").Trim(), tail = null;
            int colon = command.IndexOf(':'); if (colon >= 0) { tail = command.Substring(colon + 1); command = command.Substring(0, colon).Trim(); }
            var overrides = new Dictionary<string, string>();
            foreach (var pair in (tail ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('='); if (kv.Length != 2) throw new ArgumentException("Override must be actorId=model: " + pair);
                string key = kv[0].Trim(), value = kv[1].Trim();
                if (!ReskinMap297.Any(m => m.actor == key)) throw new ArgumentException("Override for unknown actor " + key);
                overrides[key] = value;
            }
            if (command == "dry") return ReskinApply297(overrides, false);
            if (command == "apply") return ReskinApply297(overrides, true);
            if (command == "revert") { if (overrides.Count > 0) throw new ArgumentException("revert takes no overrides"); return ReskinRevert297(); }
            throw new ArgumentException("Expected dry, apply or revert (dry|apply may add :actorId=model,...): " + argument);
        }

        static WorldMacroPlaytestSession ReskinSession297()
        {
            var sessions = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).ToArray();
            if (sessions.Length != 1) throw new Exception("Active scene must hold exactly one WorldMacroPlaytestSession (found " + sessions.Length + ")");
            return sessions[0];
        }
        static void RequireCleanScene297()
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) || scene.isDirty) throw new Exception("Saved, clean Edit-mode scene required (active=" + scene.path + " dirty=" + scene.isDirty + "); nothing changed");
        }

        static ModelFit297 MeasureModel297(ModelRow row)
        {
            var fit = new ModelFit297 { row = row }; var errors = new List<string>();
            SpeciesDefaults(row); fit.radius = row.capsuleRadius; fit.height = row.targetHeight;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(row.id));
            if (prefab == null) errors.Add("prefab missing " + PrefabPath(row.id));
            else
            {
                try { var b = PhysicalBounds(prefab); fit.height = b.size.y; fit.halfWidth = new[] { b.min.x, b.max.x, b.min.z, b.max.z }.Max(v => Mathf.Abs(v)); }
                catch (Exception e) { errors.Add("physical bounds: " + e.Message); }
                var receipt = Inspect(row, prefab, row.model, true);
                if (receipt.status != "PASS") errors.Add("not ready: " + string.Join(";", receipt.errors));
            }
            if (AssetDatabase.LoadAssetAtPath<EnemyAudioProfile298>(AssetRoot + "/Data/Audio_" + row.id + ".asset") == null) errors.Add("audio profile Audio_" + row.id + " missing");
            try { fit.deathLength = Clip(row, "death").length; fit.attackPeak = row.clips.Single(c => c.role == "attack").peak01; }
            catch (Exception e) { errors.Add("clips: " + e.Message); }
            fit.error = errors.Count == 0 ? null : string.Join("; ", errors);
            return fit;
        }
        static string F297(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string DescribeFit297(ModelFit297 m, NavMeshAgent nav) =>
            m.row.id + " H" + F297(m.height) + "(x" + F297(m.height / nav.height) + ") R" + F297(m.radius) + "(x" + F297(m.radius / nav.radius) + ") halfW" + F297(m.halfWidth);
        static bool Fits297(ModelFit297 m, NavMeshAgent nav)
        {
            float h = m.height / nav.height, r = m.radius / nav.radius;
            return m.error == null && h >= MinHeightRatio297 && h <= MaxHeightRatio297 && r <= MaxRadiusRatio297;
        }
        static float FitScore297(ModelFit297 m, NavMeshAgent nav) => Mathf.Max(Mathf.Abs(Mathf.Log(m.height / nav.height)), Mathf.Abs(Mathf.Log(m.radius / nav.radius)));

        static List<ReskinPlan297> PlanReskin297(WorldMacroPlaytestSession session, Dictionary<string, string> overrides)
        {
            var manifest = ReadManifest();
            var humanoids = NewSpecies.Where(id => manifest.rows.Any(r => r.id == id && r.rigType == "Humanoid")).ToArray();
            var models = new Dictionary<string, ModelFit297>();
            ModelFit297 Model(string id)
            {
                if (!models.TryGetValue(id, out var m)) models[id] = m = MeasureModel297(manifest.rows.SingleOrDefault(r => r.id == id) ?? throw new Exception("Manifest lacks " + id));
                return m;
            }
            var plans = new List<ReskinPlan297>();
            foreach (var (actorId, preferred) in ReskinMap297)
            {
                var plan = new ReskinPlan297 { actorId = actorId, preferred = preferred }; plans.Add(plan);
                var matches = (session.Actors ?? Array.Empty<PrologueEncounter>()).Where(a => a != null && a.Id == actorId).ToArray();
                if (matches.Length != 1) { plan.errors.Add("session actors with this Id: " + matches.Length); continue; }
                var actor = plan.actor = matches[0]; var nav = actor.GetComponent<NavMeshAgent>();
                if (nav == null || nav.radius <= 0 || nav.height <= 0) { plan.errors.Add("no usable NavMeshAgent"); continue; }
                if (actor.GetComponent<EnemyController>() == null || actor.GetComponent<EnemyVitals>() == null) plan.errors.Add("EnemyController/EnemyVitals missing");
                if (actor.GetComponent<SouthGateGeneralController>() != null || actor.GetComponent<CheongryongCombatController>() != null) plan.errors.Add("boss controller present; ordinary reskin only");
                if (Vector3.Distance(actor.transform.lossyScale, Vector3.one) > .01f) plan.errors.Add("root scale " + actor.transform.lossyScale + " would rescale the visual");
                var placeholders = actor.transform.Cast<Transform>().Where(t => t.name == Placeholder297).ToArray();
                var generated = actor.transform.Cast<Transform>().Where(t => t.name == Generated297).ToArray();
                bool rig298 = actor.GetComponent<EnemyRigMotion298>() != null, audio298 = actor.GetComponent<EnemyAudioEmitter298>() != null;
                plan.state = generated.Length > 0 ? "APPLIED(" + string.Join(",", generated.Select(g => Path.GetFileNameWithoutExtension(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g.gameObject)))) + ")" :
                    placeholders.Length == 1 ? "ORIGINAL(placeholder active=" + placeholders[0].gameObject.activeSelf + ", rig273=" + (actor.GetComponent<EnemyRigMotion273>() != null) + ")" : "UNKNOWN";
                if (placeholders.Length != 1) plan.errors.Add(Placeholder297 + " children: " + placeholders.Length);
                if (generated.Length > 0 || rig298 || audio298) plan.errors.Add("already carries " + Generated297 + "/EnemyRigMotion298/EnemyAudioEmitter298 (revert first)");
                // Choice: explicit override > preferred when it fits > best-fitting other humanoid.
                if (overrides.TryGetValue(actorId, out var forced))
                {
                    if (!humanoids.Contains(forced)) { plan.errors.Add("override " + forced + " is not an ordinary humanoid (" + string.Join("/", humanoids) + ")"); continue; }
                    plan.model = Model(forced); plan.reason = "explicit override; fit " + (Fits297(plan.model, nav) ? "OK " : "OUTSIDE limits ") + DescribeFit297(plan.model, nav);
                }
                else
                {
                    var wanted = Model(preferred);
                    if (Fits297(wanted, nav)) { plan.model = wanted; plan.reason = "preferred fits: " + DescribeFit297(wanted, nav); }
                    else
                    {
                        var best = humanoids.Where(id => id != preferred).Select(id => Model(id)).Where(m => Fits297(m, nav)).OrderBy(m => FitScore297(m, nav)).FirstOrDefault();
                        string why = wanted.error ?? DescribeFit297(wanted, nav) + " outside limits";
                        if (best == null) { plan.errors.Add("preferred " + preferred + " unsuitable (" + why + ") and no humanoid fits"); continue; }
                        plan.model = best; plan.reason = "preferred " + why + " → best-fitting humanoid " + DescribeFit297(best, nav);
                    }
                }
                if (plan.model.error != null) plan.errors.Add(plan.model.row.id + ": " + plan.model.error);
                plan.heightRatio = plan.model.height / nav.height; plan.radiusRatio = plan.model.radius / nav.radius;
            }
            return plans;
        }

        static string ReskinApply297(Dictionary<string, string> overrides, bool write)
        {
            var scene = SceneManager.GetActiveScene();
            if (write) RequireCleanScene297();
            var session = ReskinSession297(); var plans = PlanReskin297(session, overrides);
            var lines = new List<string> { "Reskin297 " + (write ? "apply" : "dry") + " scene=" + scene.path + " dirty=" + scene.isDirty +
                " limits: height x" + F297(MinHeightRatio297) + "–" + F297(MaxHeightRatio297) + ", radius ≤x" + F297(MaxRadiusRatio297) + " (TEST)" };
            foreach (var p in plans)
            {
                var nav = p.actor != null ? p.actor.GetComponent<NavMeshAgent>() : null;
                lines.Add(p.actorId + " [" + (p.state ?? "missing") + "] nav r" + (nav != null ? F297(nav.radius) + " h" + F297(nav.height) + " off" + F297(nav.baseOffset) : "?") +
                    " → " + (p.model != null ? p.model.row.id + " peak" + F297(p.model.attackPeak) + " death" + F297(p.model.deathLength) + "s (DeathVisual " + F297(p.actor.DeathVisualSeconds) + "→" + F297(Mathf.Max(p.actor.DeathVisualSeconds, p.model.deathLength + .1f)) + ")" : "none") +
                    " | " + p.reason + (p.errors.Count > 0 ? " | BLOCKED: " + string.Join("; ", p.errors) : ""));
            }
            if (File.Exists(ReskinRecordPath297)) lines.Add("record exists: " + ReskinRecordPath297);
            if (!write) return string.Join("\n", lines);
            if (plans.Any(p => p.errors.Count > 0)) throw new Exception("Reskin297 apply refused; nothing changed.\n" + string.Join("\n", lines));

            var rows = new List<ReskinRow297>(); var profiles = new List<Object>();
            try
            {
                foreach (var p in plans)
                {
                    var actor = p.actor; var row = p.model.row; var nav = actor.GetComponent<NavMeshAgent>();
                    var capsule = actor.GetComponent<CapsuleCollider>(); var enemy = actor.GetComponent<EnemyController>();
                    var placeholder = actor.transform.Cast<Transform>().Single(t => t.name == Placeholder297).gameObject;
                    string before = GameplayDigest297(actor, session, false);
                    var rec = new ReskinRow297 { actorId = actor.Id, model = row.id, preferred = p.preferred, reason = p.reason, heightRatio = p.heightRatio, radiusRatio = p.radiusRatio,
                        placeholderName = placeholder.name, placeholderActive = placeholder.activeSelf, placeholderGid = Gid297(placeholder),
                        rendererGid = Gid297(GetRef<Renderer>(enemy, "_renderer")), deathVisualSeconds = actor.DeathVisualSeconds,
                        navRadius = nav.radius, navHeight = nav.height, navBaseOffset = nav.baseOffset,
                        capsuleRadius = capsule != null ? capsule.radius : 0, capsuleHeight = capsule != null ? capsule.height : 0, capsuleCenter = capsule != null ? capsule.center : Vector3.zero,
                        gameplayHash = Sha297(GameplayDigest297(actor, session, true)) };
                    var old = actor.GetComponent<EnemyRigMotion273>();
                    if (old != null) { rec.hadRig273 = true; rec.rig273Json = EditorJsonUtility.ToJson(old); rec.rig273Refs = Refs297(old); }
                    // Presentation only: placeholder is deactivated (kept), approved visual/motion/SFX attached.
                    placeholder.SetActive(false);
                    var visual = AddVisual(actor, row); BindMotion(actor, row, visual); BindAudio(actor, row.id, session);
                    try { var r = SetReadability298(visual, row.id); rec.readability = "fill " + F297(r.diffuseFill) + " on " + r.renderers + " skins"; }
                    catch (Exception e) { rec.readability = "skipped: " + e.Message; }
                    var motion = actor.GetComponent<EnemyRigMotion298>();
                    if (motion == null || !motion.IsConfigured || motion.General != null) throw new Exception(actor.Id + " EnemyRigMotion298 not configured as ordinary melee presentation");
                    rec.attackPeak01 = motion.AttackPeak01; rec.newDeathVisualSeconds = actor.DeathVisualSeconds;
                    var audio = actor.GetComponent<EnemyAudioEmitter298>(); if (audio != null && audio.Profile != null) profiles.Add(audio.Profile);
                    string after = GameplayDigest297(actor, session, false);
                    if (after != before) throw new Exception(actor.Id + " gameplay digest changed: " + Diff297(before, after));
                    rows.Add(rec);
                }
            }
            catch (Exception e)
            {
                throw new Exception("Reskin297 apply failed mid-way; the active scene holds UNSAVED partial edits — reload it without saving. " + e.Message, e);
            }
            string file = ReskinRecordPath297; Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file)) File.Copy(file, Path.Combine(Path.GetDirectoryName(file), "original." + DateTime.UtcNow.ToString("yyyyMMddTHHmmss") + ".json"), true);
            File.WriteAllText(file, JsonUtility.ToJson(new ReskinRecord297 { utc = DateTime.UtcNow.ToString("O"), scene = scene.path, status = "APPLIED", rows = rows.ToArray() }, true));
            // Only the touched audio profiles (content unchanged); no global SaveAssets over other sessions' edits.
            foreach (var profile in profiles.Distinct()) AssetDatabase.SaveAssetIfDirty(profile);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed after reskin; record written to " + file);
            return "Reskin297 applied + saved " + scene.path + ": " + string.Join(", ", rows.Select(r => r.actorId + "→" + r.model + (r.model != r.preferred ? " (for " + r.preferred + ")" : "") + " peak" + F297(r.attackPeak01) + " readability " + r.readability)) +
                ". Gameplay digests identical (Id/vitals/controller+profile/nav/capsule/patrol/spec); placeholders inactive, EnemyRigMotion273 recorded. Record " + file;
        }

        static string ReskinRevert297()
        {
            RequireCleanScene297();
            var scene = SceneManager.GetActiveScene(); string file = ReskinRecordPath297;
            if (!File.Exists(file)) throw new Exception("No reskin record " + file);
            var record = JsonUtility.FromJson<ReskinRecord297>(File.ReadAllText(file));
            if (record?.rows == null || record.rows.Length == 0) throw new Exception("Empty reskin record " + file);
            if (record.scene != scene.path) throw new Exception("Record belongs to " + record.scene + ", active scene is " + scene.path);
            var session = ReskinSession297(); var errors = new List<string>();
            var work = new List<(ReskinRow297 rec, PrologueEncounter actor, GameObject visual, GameObject placeholder, Renderer renderer, Object[] refs)>();
            // Resolve everything first; nothing is touched unless every actor is in the recorded applied state.
            foreach (var rec in record.rows)
            {
                var matches = session.Actors.Where(a => a != null && a.Id == rec.actorId).ToArray();
                if (matches.Length != 1) { errors.Add(rec.actorId + ": session actors " + matches.Length); continue; }
                var actor = matches[0];
                var visuals = actor.transform.Cast<Transform>().Where(t => t.name == Generated297).ToArray();
                if (visuals.Length != 1 || PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visuals[0].gameObject) != PrefabPath(rec.model)) { errors.Add(rec.actorId + ": expected one " + Generated297 + " of " + PrefabPath(rec.model)); continue; }
                var placeholder = Resolve297(rec.placeholderGid) as GameObject;
                if (placeholder == null || placeholder.transform.parent != actor.transform) placeholder = actor.transform.Cast<Transform>().FirstOrDefault(t => t.name == rec.placeholderName)?.gameObject;
                if (placeholder == null) { errors.Add(rec.actorId + ": placeholder " + rec.placeholderName + " not found"); continue; }
                var renderer = Resolve297(rec.rendererGid) as Renderer;
                if (!string.IsNullOrEmpty(rec.rendererGid) && renderer == null) errors.Add(rec.actorId + ": original EnemyController renderer unresolved");
                var refs = (rec.rig273Refs ?? Array.Empty<ReskinRef297>()).Select(r => Resolve297(r.gid)).ToArray();
                for (int i = 0; i < refs.Length; i++) if (!string.IsNullOrEmpty(rec.rig273Refs[i].gid) && refs[i] == null) errors.Add(rec.actorId + ": EnemyRigMotion273." + rec.rig273Refs[i].path + " unresolved");
                if (rec.hadRig273 && actor.GetComponent<EnemyRigMotion273>() != null) errors.Add(rec.actorId + ": EnemyRigMotion273 already present");
                work.Add((rec, actor, visuals[0].gameObject, placeholder, renderer, refs));
            }
            if (errors.Count > 0) throw new Exception("Reskin297 revert refused; nothing changed.\n" + string.Join("\n", errors));
            var warnings = new List<string>();
            foreach (var w in work)
            {
                var motion = w.actor.GetComponent<EnemyRigMotion298>(); if (motion != null) Object.DestroyImmediate(motion);
                var audio = w.actor.GetComponent<EnemyAudioEmitter298>(); if (audio != null) Object.DestroyImmediate(audio);
                Object.DestroyImmediate(w.visual);
                w.placeholder.SetActive(w.rec.placeholderActive);
                if (w.rec.hadRig273)
                {
                    var rig = w.actor.gameObject.AddComponent<EnemyRigMotion273>();
                    EditorJsonUtility.FromJsonOverwrite(SanitizeJson297(w.rec.rig273Json), rig);
                    // JSON object references are session instance IDs; rebind each from its GlobalObjectId.
                    var so = new SerializedObject(rig);
                    for (int i = 0; i < w.refs.Length; i++) { var property = so.FindProperty(w.rec.rig273Refs[i].path); if (property != null) property.objectReferenceValue = w.refs[i]; }
                    so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rig);
                }
                SetRef(w.actor.GetComponent<EnemyController>(), "_renderer", w.renderer);
                w.actor.DeathVisualSeconds = w.rec.deathVisualSeconds; EditorUtility.SetDirty(w.actor);
                if (Sha297(GameplayDigest297(w.actor, session, true)) != w.rec.gameplayHash) warnings.Add(w.actor.Id + " gameplay digest differs from apply-time record (changed outside Reskin297?)");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed after revert");
            record.status = "REVERTED"; record.revertedUtc = DateTime.UtcNow.ToString("O"); File.WriteAllText(file, JsonUtility.ToJson(record, true));
            return "Reskin297 reverted + saved " + scene.path + ": " + string.Join(", ", work.Select(w => w.rec.actorId + " placeholder restored" + (w.rec.hadRig273 ? " +EnemyRigMotion273" : ""))) +
                (warnings.Count > 0 ? ". WARN: " + string.Join("; ", warnings) : ". Gameplay digests match apply-time record.");
        }

        // Gameplay surface that a presentation reskin must leave untouched. Excluded: PrologueEncounter.DeathVisualSeconds
        // (renderer hide delay after death) and EnemyController._renderer (tint target) — both recorded and reverted.
        static string GameplayDigest297(PrologueEncounter actor, WorldMacroPlaytestSession session, bool stable)
        {
            var sb = new StringBuilder();
            void Add(string label, Object target, params string[] exclude) { sb.Append('[').Append(label).Append("]\n"); if (target == null) sb.Append("absent\n"); else Serialized297(sb, target, stable, exclude); }
            Add("encounter", actor, "DeathVisualSeconds");
            Add("controller", actor.GetComponent<EnemyController>(), "_renderer");
            Add("vitals", actor.GetComponent<EnemyVitals>());
            Add("nav", actor.GetComponent<NavMeshAgent>());
            Add("capsule", actor.GetComponent<CapsuleCollider>());
            var t = actor.transform;
            sb.Append("[root]").Append(t.position.ToString("F4")).Append(t.rotation.ToString("F5")).Append(t.lossyScale.ToString("F4")).Append(actor.gameObject.layer).Append(actor.gameObject.activeSelf).Append('\n');
            foreach (var c in actor.GetComponentsInChildren<Collider>(true)) sb.Append("[collider]").Append(c.name).Append(c.GetType().Name).Append(c.enabled).Append(c.isTrigger).Append('\n');
            var spec = session.Content != null && session.Content.Encounters != null ? session.Content.Encounters.FirstOrDefault(e => e != null && e.Id == actor.Id) : null;
            sb.Append("[spec]").Append(spec == null ? "none" : JsonUtility.ToJson(spec)).Append('\n');
            return sb.ToString();
        }
        static void Serialized297(StringBuilder sb, Object target, bool stable, string[] exclude)
        {
            var so = new SerializedObject(target); var p = so.GetIterator(); bool enter = true;
            while (p.Next(enter))
            {
                enter = true; string path = p.propertyPath;
                if (exclude.Any(e => path == e || path.StartsWith(e + ".", StringComparison.Ordinal))) { enter = false; continue; }
                string value;
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Integer: value = p.longValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.LayerMask: case SerializedPropertyType.ArraySize: case SerializedPropertyType.Character: case SerializedPropertyType.Enum: value = p.intValue.ToString(CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.Boolean: value = p.boolValue ? "1" : "0"; break;
                    case SerializedPropertyType.Float: value = p.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                    case SerializedPropertyType.String: value = p.stringValue; break;
                    case SerializedPropertyType.ObjectReference: value = stable ? Gid297(p.objectReferenceValue) : p.objectReferenceValue != null ? p.objectReferenceValue.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "0"; break;
                    default: continue;
                }
                sb.Append(path).Append('=').Append(value).Append('\n');
            }
        }
        static ReskinRef297[] Refs297(Object target)
        {
            var list = new List<ReskinRef297>(); var p = new SerializedObject(target).GetIterator();
            while (p.Next(true))
                if (p.propertyType == SerializedPropertyType.ObjectReference && !p.propertyPath.StartsWith("m_", StringComparison.Ordinal))
                    list.Add(new ReskinRef297 { path = p.propertyPath, gid = Gid297(p.objectReferenceValue) });
            return list.ToArray();
        }
        // Keep the new component's own script/owner; neutralise stale instance IDs (rebound from GlobalObjectIds after).
        static string SanitizeJson297(string json)
        {
            foreach (var key in new[] { "m_Script", "m_GameObject" })
            {
                json = Regex.Replace(json, "\"" + key + "\"\\s*:\\s*\\{[^}]*\\}\\s*,", "");
                json = Regex.Replace(json, ",\\s*\"" + key + "\"\\s*:\\s*\\{[^}]*\\}", "");
            }
            return Regex.Replace(json, "\\{\\s*\"instanceID\"\\s*:\\s*-?\\d+\\s*\\}", "{\"instanceID\":0}");
        }
        static string Gid297(Object value) => value == null ? "" : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static Object Resolve297(string gid)
        {
            if (string.IsNullOrEmpty(gid)) return null;
            if (!GlobalObjectId.TryParse(gid, out var id)) throw new Exception("Unparseable GlobalObjectId " + gid);
            return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
        }
        static string Sha297(string text) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", ""); }
        static string Diff297(string before, string after)
        {
            var b = before.Split('\n'); var a = after.Split('\n');
            return "removed{" + string.Join(" | ", b.Except(a).Take(6)) + "} added{" + string.Join(" | ", a.Except(b).Take(6)) + "}";
        }
    }
}
