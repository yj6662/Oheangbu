using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class RealmStream308
    {
        const string LoadingProfilePath = "Assets/_Project/Art/UI/Loading270/RegionLoading.asset";   // the lobby's profile (W_Compact_Lobby)

        static Manifest ReadManifest() => File.Exists(ManifestFile) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestFile)) : null;

        static string Backup(Rules r, string stamp, string what)
        {
            string dir = Path.Combine(Stage, "Backup", stamp + "_" + what); Directory.CreateDirectory(dir);
            string project = Path.Combine(Repo, "Oheangbu");
            var files = new List<string> { r.mainScene, r.mainScene + ".meta", "ProjectSettings/EditorBuildSettings.asset", LoadingProfilePath, SheetPath(r) };
            string folder = Path.Combine(project, r.streamFolder);
            if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder).Select(f => r.streamFolder + "/" + Path.GetFileName(f)));
            foreach (string f in files.Distinct())
            {
                string from = Path.Combine(project, f); if (!File.Exists(from)) continue;
                File.Copy(from, Path.Combine(dir, f.Replace('/', '_')), true);
            }
            if (File.Exists(ManifestFile)) File.Copy(ManifestFile, Path.Combine(dir, "manifest.json"), true);
            return dir;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/'); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static float Drift(Matrix4x4 a, Matrix4x4 b) { float d = 0; for (int i = 0; i < 16; i++) d = Mathf.Max(d, Mathf.Abs(a[i] - b[i])); return d; }

        static GameObject RealmRoot(Scene scene, string id, bool create)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Realm_" + id);
            if (root != null || !create) return root;
            root = new GameObject("Realm_" + id); SceneManager.MoveGameObjectToScene(root, scene); return root;
        }

        static RealmStreamLoader308 FindLoader(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<RealmStreamLoader308>(true)).FirstOrDefault();

        // ---------------------------------------------------------------- split

        static string Split()
        {
            var r = LoadRules(); var main = MainScene(r, true);
            var old = ReadManifest();
            if (old != null && old.units.Count > 0) throw new Refuse("already split (" + old.units.Count + " units in the manifest) - merge first");
            if (FindLoader(main) != null) throw new Refuse("the play scene already holds a loader - merge first");
            var plan = Analyse(r, main); var moving = plan.Moving.ToList();
            if (moving.Count == 0) return "RealmStream308 split: nothing can move (see plan)";
            string report = Report(plan, r, true);
            string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss"); string backup = Backup(r, stamp, "split");
            EnsureFolder(r.streamFolder);
            SceneManager.SetActiveScene(main);

            var manifest = new Manifest { mainScene = r.mainScene, stamp = stamp };
            // everything about the former place is read before the first object moves
            var rows = moving.Select(u => new
            {
                unit = u, parent = u.Root.parent, sibling = u.Root.GetSiblingIndex(), before = u.Root.localToWorldMatrix,
                gid = u.Root.parent != null ? GlobalObjectId.GetGlobalObjectIdSlow(u.Root.parent.gameObject).ToString() : "",
                parentPath = u.Root.parent != null ? PathOf(u.Root.parent) : ""
            }).ToList();
            var scenes = new Scene[plan.Realms.Length]; float drift = 0; string failure = null;
            try
            {
                for (int i = 0; i < plan.Realms.Length; i++)
                {
                    string path = ScenePath(r, plan.Realms[i].Id);
                    if (File.Exists(Path.Combine(Repo, "Oheangbu", path))) scenes[i] = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    else
                    {
                        // the editor refuses a second untitled scene: each new realm scene gets its file at once (still empty)
                        scenes[i] = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                        if (!EditorSceneManager.SaveScene(scenes[i], path)) throw new Refuse("SaveScene returned false for the new " + path);
                    }
                    SceneManager.SetActiveScene(main);
                    var root = RealmRoot(scenes[i], plan.Realms[i].Id, true);
                    if (root.transform.childCount > 0) throw new Refuse(path + " already holds " + root.transform.childCount + " objects - merge first");
                    foreach (var row in rows.Where(x => x.unit.Realm == i))
                    {
                        var t = row.unit.Root;
                        t.SetParent(null, true); SceneManager.MoveGameObjectToScene(t.gameObject, scenes[i]); t.SetParent(root.transform, true);
                        drift = Mathf.Max(drift, Drift(row.before, t.localToWorldMatrix));
                        manifest.units.Add(new ManifestUnit { realm = plan.Realms[i].Id, name = t.name, parentGid = row.gid, parentPath = row.parentPath, sibling = row.sibling, renderers = row.unit.Renderers, vertices = row.unit.Vertices });
                    }
                }
                if (drift > 1e-3f) throw new Refuse("an object did not keep its world pose (largest matrix difference " + F(drift, "F5") + ")");

                // sheet (its own asset) and the loader object in the play scene
                var sheet = AssetDatabase.LoadAssetAtPath<RealmStreamSheet308>(SheetPath(r));
                if (sheet == null) { sheet = ScriptableObject.CreateInstance<RealmStreamSheet308>(); AssetDatabase.CreateAsset(sheet, SheetPath(r)); }
                sheet.PlayScene = Path.GetFileNameWithoutExtension(r.mainScene);
                sheet.LoadDistance = r.loadDistance; sheet.UnloadDistance = r.unloadDistance; sheet.AnchorDistance = r.anchorDistance; sheet.TickSeconds = r.tickSeconds; sheet.PurgeSeconds = r.purgeSeconds;
                sheet.Realms = plan.Realms.Select((realm, i) =>
                {
                    var mine = moving.Where(u => u.Realm == i).ToList();
                    return new RealmStreamSheet308.Realm { Id = realm.Id, ScenePath = ScenePath(r, realm.Id), Outline = Hull(mine), Units = mine.Count, Renderers = mine.Sum(u => u.Renderers) };
                }).ToArray();
                EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet);

                var host = new GameObject(LoaderName); SceneManager.MoveGameObjectToScene(host, main);
                var loader = host.AddComponent<RealmStreamLoader308>(); loader.Sheet = sheet;
                loader.Session = main.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<WorldMacroPlaytestSession>(true)).FirstOrDefault();

                for (int i = 0; i < scenes.Length; i++)
                    if (!EditorSceneManager.SaveScene(scenes[i], ScenePath(r, plan.Realms[i].Id))) throw new Refuse("SaveScene returned false for " + ScenePath(r, plan.Realms[i].Id));
                EditorSceneManager.MarkSceneDirty(main);
                if (!EditorSceneManager.SaveScene(main)) throw new Refuse("SaveScene returned false for " + r.mainScene);
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
            if (failure != null)
            {
                // nothing of the play scene was saved: take it back from disk (the realm scene files may exist; the manifest is not written)
                EditorSceneManager.OpenScene(r.mainScene, OpenSceneMode.Single);
                return "FAILED split: " + failure + " - the play scene was reloaded from disk, no manifest written; backup " + backup;
            }
            manifest.units.TrimExcess();
            Directory.CreateDirectory(Stage); File.WriteAllText(ManifestFile, JsonUtility.ToJson(new ManifestFile308 { mainScene = manifest.mainScene, stamp = manifest.stamp, brokenBefore = plan.Broken, units = manifest.units }, true));
            File.Copy(ManifestFile, Path.Combine(backup, "manifest_after.json"), true);
            for (int i = 0; i < scenes.Length; i++) EditorSceneManager.CloseScene(scenes[i], true);

            string build = BuildSettings(r, plan.Realms.Select(x => ScenePath(r, x.Id)).ToArray(), true);
            string profile = Profile(AssetDatabase.LoadAssetAtPath<RealmStreamSheet308>(SheetPath(r)));
            return "RealmStream308 split: saved " + r.mainScene + " and " + scenes.Length + " realm scenes; moved " + manifest.units.Count + " units; largest pose difference " + F(drift, "F6")
                + "\n" + build + "\n" + profile + "\nbackup " + backup + "\n" + report;
        }

        [Serializable] sealed class ManifestFile308 { public string mainScene, stamp; public int brokenBefore; public List<ManifestUnit> units = new List<ManifestUnit>(); }
        static ManifestFile308 ReadManifestFull() => File.Exists(ManifestFile) ? JsonUtility.FromJson<ManifestFile308>(File.ReadAllText(ManifestFile)) : null;

        static string BuildSettings(Rules r, string[] paths, bool add)
        {
            var list = EditorBuildSettings.scenes.ToList(); int before = list.Count;
            list.RemoveAll(s => paths.Contains(s.path));
            if (add) list.AddRange(paths.Select(p => new EditorBuildSettingsScene(p, true)));
            EditorBuildSettings.scenes = list.ToArray();
            return "build settings: " + before + " -> " + list.Count + " scenes (" + (add ? "realm scenes listed" : "realm scenes taken out") + "; the settings file is written when the editor next saves the project)";
        }

        static string Profile(RealmStreamSheet308 sheet)
        {
            var profile = AssetDatabase.LoadAssetAtPath<WorldLoadingProfile270>(LoadingProfilePath);
            if (profile == null) return "WARNING loading profile not found at " + LoadingProfilePath + " - the lobby flow will not bring the start realm in";
            if (profile.RealmStream == sheet) return "loading profile already " + (sheet == null ? "without a sheet" : "points at the sheet");
            profile.RealmStream = sheet; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
            return "loading profile " + LoadingProfilePath + (sheet == null ? ": sheet taken out" : ": sheet set");
        }

        // ---------------------------------------------------------------- merge

        static string Merge()
        {
            var r = LoadRules(); var main = MainScene(r, true);
            var manifest = ReadManifestFull();
            if (manifest == null || manifest.units.Count == 0) throw new Refuse("no manifest with units - nothing to merge");
            if (FarRoot(main, false) != null) throw new Refuse("the far band is in the play scene - far:clear first (it is built from the realm scenes)");
            var realms = LoadRealms(r); string stamp = DateTime.Now.ToString("yyyyMMddTHHmmss"); string backup = Backup(r, stamp, "merge");
            var scenes = new Scene[realms.Length]; string failure = null; int moved = 0;
            try
            {
                var rows = new List<(Transform t, ManifestUnit unit, Transform parent)>();
                for (int i = 0; i < realms.Length; i++)
                {
                    string path = ScenePath(r, realms[i].Id);
                    var mine = manifest.units.Where(u => u.realm == realms[i].Id).ToList();
                    if (!File.Exists(Path.Combine(Repo, "Oheangbu", path))) { if (mine.Count > 0) throw new Refuse("scene missing: " + path); continue; }
                    scenes[i] = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    var root = RealmRoot(scenes[i], realms[i].Id, false);
                    int have = root != null ? root.transform.childCount : 0;
                    if (have != mine.Count) throw new Refuse(path + " holds " + have + " units, the manifest says " + mine.Count);
                    for (int k = 0; k < mine.Count; k++)
                    {
                        var t = root.transform.GetChild(k);
                        if (t.name != mine[k].name) throw new Refuse(path + " unit " + k + " is " + t.name + ", the manifest says " + mine[k].name);
                        Transform parent = null;
                        if (!string.IsNullOrEmpty(mine[k].parentGid))
                        {
                            if (GlobalObjectId.TryParse(mine[k].parentGid, out var gid) && GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid) is GameObject go && go.scene == main) parent = go.transform;
                            if (parent == null || PathOf(parent) != mine[k].parentPath) throw new Refuse("former parent not found: " + mine[k].parentPath + " (unit " + mine[k].name + ")");
                        }
                        rows.Add((t, mine[k], parent));
                    }
                }
                foreach (var row in rows.OrderBy(x => x.unit.sibling))
                {
                    var before = row.t.localToWorldMatrix;
                    row.t.SetParent(null, true); SceneManager.MoveGameObjectToScene(row.t.gameObject, main);
                    if (row.parent != null) row.t.SetParent(row.parent, true);
                    row.t.SetSiblingIndex(row.unit.sibling);
                    if (Drift(before, row.t.localToWorldMatrix) > 1e-3f) throw new Refuse("an object did not keep its world pose: " + row.unit.name);
                    moved++;
                }
                var loader = FindLoader(main); if (loader != null) Object.DestroyImmediate(loader.gameObject);
                EditorSceneManager.MarkSceneDirty(main);
                if (!EditorSceneManager.SaveScene(main)) throw new Refuse("SaveScene returned false for " + r.mainScene);
                for (int i = 0; i < scenes.Length; i++) if (scenes[i].IsValid() && !EditorSceneManager.SaveScene(scenes[i])) throw new Refuse("SaveScene returned false for " + scenes[i].path);
            }
            catch (Exception e) { failure = e.Message; }
            if (failure != null)
            {
                EditorSceneManager.OpenScene(r.mainScene, OpenSceneMode.Single);
                return "FAILED merge: " + failure + " - the play scene was reloaded from disk; backup " + backup;
            }
            for (int i = 0; i < scenes.Length; i++) if (scenes[i].IsValid()) EditorSceneManager.CloseScene(scenes[i], true);
            File.Move(ManifestFile, Path.Combine(Stage, "manifest_merged_" + stamp + ".json"));
            var sheet = AssetDatabase.LoadAssetAtPath<RealmStreamSheet308>(SheetPath(r));
            if (sheet != null) { foreach (var realm in sheet.Realms) { realm.Units = 0; realm.Renderers = 0; realm.Outline = Array.Empty<Vector2>(); } EditorUtility.SetDirty(sheet); AssetDatabase.SaveAssetIfDirty(sheet); }
            return "RealmStream308 merge: " + moved + " units back in " + r.mainScene + " (saved), loader taken out, realm scenes saved empty\n"
                + BuildSettings(r, realms.Select(x => ScenePath(r, x.Id)).ToArray(), false) + "\n" + Profile(null) + "\nbackup " + backup;
        }

        // ---------------------------------------------------------------- open / close

        static string OpenAll()
        {
            var r = LoadRules(); MainScene(r, false); int n = 0;
            foreach (var realm in LoadRealms(r))
            {
                string path = ScenePath(r, realm.Id);
                if (!File.Exists(Path.Combine(Repo, "Oheangbu", path)) || SceneManager.GetSceneByPath(path).isLoaded) continue;
                EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); n++;
            }
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(r.mainScene));
            return "RealmStream308 open-all: opened " + n + "; open scenes " + SceneManager.sceneCount + FarShow(r, SceneManager.GetSceneByPath(r.mainScene));
        }

        static string CloseAll()
        {
            var r = LoadRules(); if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Refuse("not in Play"); int n = 0; var kept = new List<string>();
            foreach (var realm in LoadRealms(r))
            {
                var scene = SceneManager.GetSceneByPath(ScenePath(r, realm.Id));
                if (!scene.IsValid() || !scene.isLoaded) continue;
                if (scene.isDirty) { kept.Add(scene.path); continue; }
                if (SceneManager.sceneCount == 1) { kept.Add(scene.path + " (the only open scene)"); continue; }
                EditorSceneManager.CloseScene(scene, true); n++;
            }
            EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect();
            var play = SceneManager.GetSceneByPath(r.mainScene); string far = play.isLoaded ? FarShow(r, play) : "";
            return "RealmStream308 close-all: closed " + n + far + (kept.Count > 0 ? "; REFUSED to close (unsaved changes) " + string.Join(", ", kept) : "") + "; open scenes " + SceneManager.sceneCount;
        }

        // ---------------------------------------------------------------- check

        static string Check()
        {
            var r = LoadRules(); var main = MainScene(r, false); var realms = LoadRealms(r); var manifest = ReadManifestFull();
            var sb = new StringBuilder("RealmStream308 check " + r.mainScene + "\n"); int pass = 0, fail = 0;
            void Line(bool ok, string id, string text) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + id + " " + text); }
            if (manifest == null) { Line(false, "AC-S1", "no manifest (not split)"); return sb + "checks, " + pass + " pass " + fail + " fail"; }
            var opened = new List<Scene>(); var scenes = new Scene[realms.Length];
            for (int i = 0; i < realms.Length; i++)
            {
                string path = ScenePath(r, realms[i].Id); scenes[i] = SceneManager.GetSceneByPath(path);
                if (scenes[i].isLoaded || !File.Exists(Path.Combine(Repo, "Oheangbu", path))) continue;
                scenes[i] = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened.Add(scenes[i]);
            }
            SceneManager.SetActiveScene(main);
            int units = 0, mismatched = 0, foreign = 0, scripts = 0; long vertices = 0, wantVertices = manifest.units.Sum(u => u.vertices); int renderers = 0, wantRenderers = manifest.units.Sum(u => u.renderers);
            var foreignSample = "";
            for (int i = 0; i < realms.Length; i++)
            {
                var mine = manifest.units.Where(u => u.realm == realms[i].Id).ToList();
                if (!scenes[i].IsValid() || !scenes[i].isLoaded) { if (mine.Count > 0) mismatched += mine.Count; continue; }
                var root = RealmRoot(scenes[i], realms[i].Id, false); int have = root != null ? root.transform.childCount : 0; units += have;
                if (have != mine.Count) mismatched += Math.Abs(have - mine.Count); else for (int k = 0; k < have; k++) if (root.transform.GetChild(k).name != mine[k].name) mismatched++;
                foreach (var g in scenes[i].GetRootGameObjects())
                {
                    if (g != root) { foreign++; foreignSample = g.name; }
                    foreach (var c in g.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null || c is MonoBehaviour) { scripts++; continue; }
                        if (!Allowed(c)) { foreign++; foreignSample = c.GetType().Name + " on " + PathOf(c.transform); }
                        if (c is MeshRenderer) renderers++;
                        if (c is MeshFilter f && f.sharedMesh != null) vertices += f.sharedMesh.vertexCount;
                    }
                }
            }
            Line(units == manifest.units.Count && mismatched == 0, "AC-S1", "units in the realm scenes " + units + " = manifest " + manifest.units.Count + "; mismatched " + mismatched);
            Line(scripts == 0, "AC-S2", "scripts (MonoBehaviour or missing) in the realm scenes " + scripts);
            Line(foreign == 0, "AC-S3", "anything but static parts in the realm scenes " + foreign + (foreign > 0 ? " e.g. " + foreignSample : ""));
            Line(renderers == wantRenderers && vertices == wantVertices, "AC-S4", "renderers " + N(renderers) + " = " + N(wantRenderers) + ", vertices " + N(vertices) + " = " + N(wantVertices));

            // references: broken ones in the play scene, and any reference from one scene into another
            int broken = 0, cross = 0; string crossSample = "";
            var sceneOf = new Dictionary<int, int>();
            var every = new List<(Component c, int scene)>();
            void Collect(Scene s, int index) { if (!s.IsValid() || !s.isLoaded) return; foreach (var g in s.GetRootGameObjects()) foreach (var c in g.GetComponentsInChildren<Component>(true)) { if (c == null) continue; sceneOf[c.GetInstanceID()] = index; sceneOf[c.gameObject.GetInstanceID()] = index; every.Add((c, index)); } }
            Collect(main, -1); for (int i = 0; i < scenes.Length; i++) Collect(scenes[i], i);
            foreach (var (c, index) in every)
            {
                if (c is Transform) continue;
                var it = new SerializedObject(c).GetIterator(); bool enter = true;
                while (it.Next(enter))
                {
                    enter = true;
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        enter = false; int id = it.objectReferenceInstanceIDValue; if (id == 0) continue;
                        if (it.objectReferenceValue == null) { if (index == -1) broken++; continue; }
                        if (sceneOf.TryGetValue(id, out int other) && other != index) { cross++; crossSample = c.GetType().Name + " on " + PathOf(c.transform) + " -> " + it.objectReferenceValue.name; }
                    }
                    else if (it.propertyType == SerializedPropertyType.String) enter = false;
                    else if (it.isArray)
                    {
                        if (it.arraySize == 0) enter = false;
                        else { var kind = it.GetArrayElementAtIndex(0).propertyType; if (kind != SerializedPropertyType.Generic && kind != SerializedPropertyType.ObjectReference && kind != SerializedPropertyType.ManagedReference) enter = false; }
                    }
                }
            }
            Line(broken <= manifest.brokenBefore, "AC-S5", "broken references in the play scene " + broken + " (before the split " + manifest.brokenBefore + ")");
            Line(cross == 0, "AC-S6", "references from one scene into another " + cross + (cross > 0 ? " e.g. " + crossSample : ""));

            var loader = FindLoader(main); var sheet = AssetDatabase.LoadAssetAtPath<RealmStreamSheet308>(SheetPath(r));
            Line(loader != null && sheet != null && loader.Sheet == sheet && loader.Session != null, "AC-S7", "loader in the play scene with the sheet and the session: " + (loader == null ? "no loader" : loader.Sheet == sheet ? (loader.Session != null ? "yes" : "no session") : "other sheet"));
            bool sheetOk = sheet != null && sheet.Realms.Length == realms.Length && sheet.PlayScene == Path.GetFileNameWithoutExtension(r.mainScene) && sheet.UnloadDistance > sheet.LoadDistance;
            if (sheetOk) for (int i = 0; i < realms.Length; i++) sheetOk &= sheet.Realms[i].Id == realms[i].Id && sheet.Realms[i].ScenePath == ScenePath(r, realms[i].Id) && sheet.Realms[i].Units == manifest.units.Count(u => u.realm == realms[i].Id) && (sheet.Realms[i].Units == 0 || sheet.Realms[i].Outline.Length >= 3);
            Line(sheetOk, "AC-S8", "sheet: five realms, scene paths, unit counts, outlines, load " + (sheet != null ? F(sheet.LoadDistance) + " < unload " + F(sheet.UnloadDistance) : "?"));
            var listed = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            Line(realms.All(x => listed.Contains(ScenePath(r, x.Id))), "AC-S9", "the realm scenes are in the build settings");
            var profile = AssetDatabase.LoadAssetAtPath<WorldLoadingProfile270>(LoadingProfilePath);
            Line(profile != null && profile.RealmStream == sheet && sheet != null, "AC-S10", "the lobby's loading profile points at the sheet");
            Line(!main.isDirty && scenes.All(s => !s.IsValid() || !s.isDirty), "AC-S11", "no scene left with unsaved changes");
            foreach (var s in opened) EditorSceneManager.CloseScene(s, true);
            return sb + "checks, " + pass + " pass " + fail + " fail";
        }

        // ---------------------------------------------------------------- status

        static string Status()
        {
            var sb = new StringBuilder("RealmStream308 status | playing " + EditorApplication.isPlaying + " | open scenes " + SceneManager.sceneCount + "\n");
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); sb.AppendLine("  scene " + s.path + (s.isLoaded ? "" : " (not loaded)") + " roots " + (s.isLoaded ? s.rootCount : 0)); }
            var loader = Object.FindFirstObjectByType<RealmStreamLoader308>(FindObjectsInactive.Include);
            sb.Append(loader == null ? "no loader in the open scenes\n" : loader.Describe());
            return sb.ToString();
        }
    }

    // Editor Play straight from the play scene (every editor harness does this): the session checks its spawn point against colliders in
    // its Start, so the realm scenes around the possible start points are opened next to the play scene before Play starts, and closed
    // again afterwards. The loader adopts them and lets the far ones go.
    [InitializeOnLoad]
    static class RealmStream308PlayHook
    {
        const string Key = "RealmStream308.OpenedForPlay";
        static RealmStream308PlayHook() { EditorApplication.playModeStateChanged -= Changed; EditorApplication.playModeStateChanged += Changed; }

        static void Changed(PlayModeStateChange change)
        {
            try
            {
                if (change == PlayModeStateChange.ExitingEditMode) Before();
                else if (change == PlayModeStateChange.EnteredEditMode) After();
            }
            catch (Exception e) { Debug.LogWarning("[RealmStream308] play hook: " + e.Message); }
        }

        static void Before()
        {
            if (EditorSceneManager.playModeStartScene != null) return;   // Play starts in another scene (the lobby flow brings the realms in itself)
            var loader = Object.FindFirstObjectByType<RealmStreamLoader308>(FindObjectsInactive.Exclude);
            if (loader == null || loader.Sheet == null) return;
            var sheet = loader.Sheet; var wanted = new List<int>(); bool known = false;
            try
            {
                var session = loader.Session;
                if (session != null && session.Content != null)
                {
                    sheet.Around(session.Content.StartFeet, wanted); known = true;
                    if (session.Content.Opening != null) sheet.Around(session.Content.Opening.StartFeet, wanted);
                    var saved = WorldMacroSaveSlot.Inspect(Application.persistentDataPath, session.Content.SaveSlot + session.TestSaveSuffix).Progress;
                    if (saved != null && saved.ledger != null && saved.ledger.hasPosition) sheet.Around(saved.ledger.position, wanted);
                }
            }
            catch (Exception) { known = false; }
            if (!known) { wanted.Clear(); for (int i = 0; i < sheet.Realms.Length; i++) wanted.Add(i); }
            var opened = new List<string>();
            foreach (int i in wanted)
            {
                var realm = sheet.Realms[i];
                if (realm == null || realm.Units == 0 || string.IsNullOrEmpty(realm.ScenePath) || RealmStreamSheet308.IsLoaded(realm)) continue;
                EditorSceneManager.OpenScene(realm.ScenePath, OpenSceneMode.Additive); opened.Add(realm.ScenePath);
            }
            SceneManager.SetActiveScene(loader.gameObject.scene);
            SessionState.SetString(Key, string.Join("|", opened));
            if (opened.Count > 0) Debug.Log("[RealmStream308] opened for Play: " + string.Join(", ", opened));
        }

        static void After()
        {
            string value = SessionState.GetString(Key, ""); SessionState.EraseString(Key);
            foreach (string path in value.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (scene.IsValid() && scene.isLoaded && !scene.isDirty && SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
