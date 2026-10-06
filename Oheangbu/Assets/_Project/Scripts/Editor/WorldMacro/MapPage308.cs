using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using Oheangbu.App.World.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 map 5 (D308-25, SPEC-MAP-OVERHAUL-308 5b): the 지도 page is the map only. Two users of one rule set:
    //   Inspect(...)   what may stand on the page - no legend / list / card / hint object (also inactive ones), no ScrollRect /
    //                  RectMask2D / list row / stroke swatch component, the key line has exactly its three rows, every visible
    //                  text is on the allow-list BY VALUE, at most one name tag and it hangs on a mark the sheet draws (#214).
    //                  Called by this file's `page` (Edit mode) and by PlaytestMenuReview layout-check / layout-large (Play).
    //   page[...]      Queue: python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.MapOverhaul308 Run "page[:size=1080|1440]
    //                  [:aspect=16x9|16x10|21x9][:bundle=on|off][:at=x,z][:mutant=legend|tags]"
    //                  Edit mode, the real WorldMapPresenter in a preview scene (isolated session, every marker known, every
    //                  cell walked), nothing saved, the open scene only read -> Art/UI308/Map/checks-page.txt, head `page ok N/N`.
    //                  mutant=... plants a fault (a legend object with a text / two name tags): the answer must be `page FAILED`.
    // The lists are data: Tools/Unity/Stage308_map5/Data/map5_page_texts.json. A missing or unreadable file is a FAILED line,
    // never a pass. No dialog, no static state, no asset write.
    public static class MapPage308
    {
        public const string DataFile = "Tools/Unity/Stage308_map5/Data/map5_page_texts.json";
        const string StylePath = "Assets/_Project/Resources/UI304/map/MapStyle304.asset";
        const string NotationPath = "Assets/_Project/Resources/UI308/Map/MapNotation308.asset";
        const string Usage = "page[:size=1080|1440][:aspect=16x9|16x10|21x9][:bundle=on|off][:at=x,z][:mutant=legend|tags]";

        [Serializable] public sealed class Limits { public int tag = 1, regionNames = 5, realmLine = 1; }
        [Serializable]
        public sealed class Texts
        {
            public string[] @static = Array.Empty<string>(), railTabs = Array.Empty<string>(), forbiddenObjects = Array.Empty<string>(),
                forbiddenComponents = Array.Empty<string>(), controls = Array.Empty<string>();
            public string checkpointTag = "", objectiveSuffix = "";
            public Limits max = new Limits();
        }

        /// <summary>The rule data, or null with the reason.</summary>
        public static Texts Load(out string why)
        {
            why = "";
            string file = Path.Combine(Harness303.RepoRoot, DataFile.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) { why = DataFile + " is missing"; return null; }
            Texts t;
            try { t = JsonUtility.FromJson<Texts>(File.ReadAllText(file, Encoding.UTF8)); }
            catch (Exception e) { why = DataFile + " does not parse (" + e.Message + ")"; return null; }
            if (t == null || t.@static == null || t.@static.Length == 0 || t.forbiddenObjects == null || t.forbiddenObjects.Length == 0 || t.controls == null || t.controls.Length == 0
                || string.IsNullOrEmpty(t.checkpointTag) || string.IsNullOrEmpty(t.objectiveSuffix))
            { why = DataFile + " carries no static / forbiddenObjects / controls / checkpointTag / objectiveSuffix"; return null; }
            return t;
        }

        public sealed class Census
        {
            public readonly List<string> Objects = new List<string>(), Components = new List<string>(), Controls = new List<string>(), Texts = new List<string>(),
                Tags = new List<string>(), Seen = new List<string>();
            public int TagCount, PlateCount, RegionCount, RealmLines, TextCount;
            public string TagText = "";
            public IEnumerable<string> Failures => Objects.Concat(Components).Concat(Controls).Concat(Texts).Concat(Tags);
            public int FailureCount => Objects.Count + Components.Count + Controls.Count + Texts.Count + Tags.Count;
        }

        static readonly Regex Markup = new Regex("<[^>]+>", RegexOptions.Compiled);
        static string Clean(string s) => new string(Markup.Replace(s ?? "", "").Where(ch => !char.IsWhiteSpace(ch)).ToArray());

        static string PathOf(Transform t, Transform stop)
        {
            var parts = new List<string>();
            for (var x = t; x != null && x != stop; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // pattern: "A/B" = the path ends with it; "Name_*" = the name starts with Name_; otherwise the exact name
        static bool Match(string pattern, string name, string path)
        {
            if (pattern.IndexOf('/') >= 0) return path == pattern || path.EndsWith("/" + pattern, StringComparison.Ordinal);
            if (pattern.EndsWith("*", StringComparison.Ordinal)) return name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.Ordinal);
            return name == pattern;
        }

        static string Short(string label)
        {
            if (string.IsNullOrEmpty(label)) return label ?? "";
            int i = label.LastIndexOf(" · ", StringComparison.Ordinal);
            return i >= 0 && i + 3 < label.Length ? label.Substring(i + 3) : label;
        }

        static bool Under(Transform t, string ancestor, Transform stop)
        {
            for (var x = t.parent; x != null && x != stop; x = x.parent) if (x.name == ancestor) return true;
            return false;
        }

        /// <summary>What stands on the page right now. mapScope = the map layer (or the presenter's ExpandedWorldMap in the Edit
        /// fixture); railScope = the menu rail layer, null when the fixture has none.</summary>
        public static Census Inspect(Transform mapScope, Transform railScope, WorldMapBakedDataSO data, MapStyle304SO style, Texts t)
        {
            var c = new Census();
            if (mapScope == null || t == null) { c.Objects.Add("no map scope / no rule data"); return c; }
            // AC-M5.1: objects (active or not) and components
            foreach (Transform tr in mapScope.GetComponentsInChildren<Transform>(true))
            {
                if (tr == mapScope) continue;
                string path = PathOf(tr, mapScope);
                foreach (string pattern in t.forbiddenObjects) if (Match(pattern, tr.name, path)) { c.Objects.Add("object " + path + " (" + pattern + ")"); break; }
            }
            foreach (Component comp in mapScope.GetComponentsInChildren<Component>(true))
                if (comp != null && Array.IndexOf(t.forbiddenComponents, comp.GetType().Name) >= 0) c.Components.Add("component " + comp.GetType().Name + " on " + PathOf(comp.transform, mapScope));
            var controls = mapScope.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "PaperMapControls");
            if (controls == null) c.Controls.Add("PaperMapControls is missing (the key line stays)");
            else
            {
                var rows = new List<string>(); foreach (Transform row in controls) rows.Add(row.name);
                if (!rows.SequenceEqual(t.controls)) c.Controls.Add("PaperMapControls rows [" + string.Join(", ", rows) + "] != [" + string.Join(", ", t.controls) + "]");
            }

            // AC-M5.2 / M5.5: every visible text, by value
            var regions = new HashSet<string>(StringComparer.Ordinal); var places = new HashSet<string>(StringComparer.Ordinal);
            if (data != null && data.Locations != null) foreach (var e in data.Locations.Entries) if (e != null && e.Priority == 0 && !string.IsNullOrEmpty(e.Name)) regions.Add(Clean(e.Name));
            if (data != null && data.Zones != null) foreach (var z in data.Zones) if (z != null && !string.IsNullOrEmpty(z.Label)) places.Add(Clean(z.Label));
            var fixedTexts = new HashSet<string>(t.@static.Select(Clean), StringComparer.Ordinal);
            var railTexts = new HashSet<string>((t.railTabs ?? Array.Empty<string>()).Select(Clean), StringComparer.Ordinal);
            var shownRegions = new List<string>(); string realmLine = null;
            foreach (Transform scope in new[] { mapScope, railScope })
            {
                if (scope == null) continue;
                foreach (Graphic g in scope.GetComponentsInChildren<Graphic>(false))
                {
                    if (!(g is TMP_Text) && !(g is Text)) continue;
                    if (!HarnessUiRules304.IsVisible(g)) continue;
                    string raw = HarnessUiRules304.TextOf(g), text = Clean(raw), path = PathOf(g.transform, scope);
                    c.TextCount++; c.Seen.Add(path + " = " + text);
                    bool onLabels = Under(g.transform, "MapLabels", scope);
                    if (onLabels && g.name.StartsWith("Label_Region_", StringComparison.Ordinal))
                    {
                        c.RegionCount++; shownRegions.Add(text);
                        if (!regions.Contains(text)) c.Texts.Add("text \"" + raw + "\" at " + path + " is not a realm name of the location catalogue");
                    }
                    else if (onLabels && g.name.StartsWith("Label_", StringComparison.Ordinal))
                    {
                        c.TagCount++; c.TagText = raw;
                        string id = g.name.Substring("Label_".Length);
                        var markers = mapScope.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "MapMarkers");
                        Transform mark = markers != null ? markers.Find(id == "KnownDestination" ? "KnownDestinationArea" : "Marker_" + id) : null;
                        if (mark == null || !mark.gameObject.activeInHierarchy) c.Tags.Add("name tag \"" + raw + "\" (" + g.name + ") hangs on no drawn mark (#214: a name only for a mark the sheet draws)");
                        var spec = data != null && data.Markers != null ? data.Markers.FirstOrDefault(m => m != null && m.Id == id) : null;
                        string want = spec != null ? Short(spec.Label) : id == "Checkpoint" ? t.checkpointTag : id == "Drop" && style != null ? style.Kind(MapMarkerKind304.Coin).Name : null;
                        if (want != null && Clean(want) != text) c.Tags.Add("name tag " + g.name + " says \"" + raw + "\", its mark's name is \"" + want + "\"");
                        if (id == "KnownDestination" && !raw.EndsWith(t.objectiveSuffix, StringComparison.Ordinal) && (style == null || raw != style.Kind(MapMarkerKind304.Objective).Name))
                            c.Tags.Add("objective tag \"" + raw + "\" is neither '<destination>" + t.objectiveSuffix + "' nor the objective kind's name");
                    }
                    else if (g.name == "Realm" && Under(g.transform, "MapTitle", scope))
                    {
                        c.RealmLines++; realmLine = text;
                        if (!regions.Contains(text) && !places.Contains(text)) c.Texts.Add("slip realm line \"" + raw + "\" is neither a realm nor a zone name");
                    }
                    else if (!fixedTexts.Contains(text) && !(scope == railScope && railTexts.Contains(text)))
                        c.Texts.Add("text \"" + raw + "\" at " + path + " is not on the allow-list");
                }
            }
            if (realmLine != null && style != null && style.SlipRealm == MapSlipRealm304.WhenNotOnSheet && shownRegions.Contains(realmLine))
                c.Texts.Add("the slip's realm line \"" + realmLine + "\" stands while the sheet shows the same realm name (the word twice on the paper)");
            foreach (Transform tr in mapScope.GetComponentsInChildren<Transform>(false))
                if (tr.name.StartsWith("Plate_", StringComparison.Ordinal) && Under(tr, "MapLabels", mapScope)) c.PlateCount++;
            if (c.TagCount > t.max.tag) c.Tags.Add(c.TagCount + " name tags visible (at most " + t.max.tag + ")");
            if (c.PlateCount > t.max.tag) c.Tags.Add(c.PlateCount + " name plates visible (at most " + t.max.tag + ")");
            if (c.RegionCount > t.max.regionNames) c.Texts.Add(c.RegionCount + " realm names visible (at most " + t.max.regionNames + ")");
            if (c.RealmLines > t.max.realmLine) c.Texts.Add(c.RealmLines + " slip realm lines visible (at most " + t.max.realmLine + ")");
            return c;
        }

        // ------------------------------------------------------------------ page (Edit mode)
        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Failed, Total;
            public void Add(bool ok, string label) { Total++; if (!ok) Failed++; Lines.Add((ok ? "ok      " : "FAILED  ") + label); }
            public void Info(string label) => Lines.Add("INFO    " + label);
        }

        static string F(float v, string f = "0.###") => v.ToString(f, CultureInfo.InvariantCulture);
        static string R(Rect r) => "(" + F(r.x, "0.#") + ", " + F(r.y, "0.#") + ") " + F(r.width, "0.#") + " x " + F(r.height, "0.#");
        static bool Near(Rect a, Rect b, float tolerance) => Mathf.Abs(a.x - b.x) <= tolerance && Mathf.Abs(a.y - b.y) <= tolerance && Mathf.Abs(a.width - b.width) <= tolerance && Mathf.Abs(a.height - b.height) <= tolerance;

        public static string Run(string command)
        {
            int height = 1080; float aspect = 16f / 9f; string aspectName = "16x9", mutant = ""; bool bundleOn = true; Vector2? at = null;
            foreach (string part in (command ?? "").Trim().Split(':').Skip(1))
            {
                int eq = part.IndexOf('='); if (eq <= 0) return "REFUSED page option '" + part + "' (" + Usage + ")";
                string key = part.Substring(0, eq).Trim(), value = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "size": if (value == "1440") height = 1440; else if (value == "1080") height = 1080; else return "REFUSED size=1080|1440"; break;
                    case "aspect":
                        if (value == "16x9") aspect = 16f / 9f; else if (value == "16x10") aspect = 16f / 10f; else if (value == "21x9") aspect = 64f / 27f; else return "REFUSED aspect=16x9|16x10|21x9";
                        aspectName = value; break;
                    case "bundle": bundleOn = value != "off"; break;
                    case "mutant": if (value != "legend" && value != "tags") return "REFUSED mutant=legend|tags"; mutant = value; break;
                    case "at":
                    {
                        var xy = value.Split(',');
                        if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return "REFUSED at=x,z";
                        at = new Vector2(x, z); break;
                    }
                    default: return "REFUSED page option '" + key + "' (" + Usage + ")";
                }
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED page is Edit Mode only (Play: PlaytestMenuReview layout-check reads the same rules)";
            var active = SceneManager.GetActiveScene(); bool dirtyBefore = active.isDirty;
            var ui = Object.FindObjectsByType<PlaytestUiRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(u => u.gameObject.scene == active);
            if (ui == null || ui.MapData == null || ui.Content == null || ui.WorldSheet == null || ui.Theme == null)
                return "REFUSED the active scene has no PlaytestUiRoot with MapData / Content / WorldSheet / Theme (open W_Demo_Main; the check only reads it)";
            var styleAsset = AssetDatabase.LoadAssetAtPath<MapStyle304SO>(StylePath);
            if (styleAsset == null) return "REFUSED " + StylePath + " missing";
            var notation = AssetDatabase.LoadAssetAtPath<MapNotation308SO>(NotationPath);
            if (bundleOn && notation == null) return "REFUSED " + NotationPath + " missing (page:bundle=off for the map without a bundle)";

            var sheet = new Sheet();
            var texts = Load(out string why);
            sheet.Add(texts != null, "rule data " + DataFile + (texts != null ? " (" + texts.@static.Length + " fixed texts, " + texts.forbiddenObjects.Length + " object names, " + texts.forbiddenComponents.Length + " component types)" : ": " + why));
            if (texts == null) return Finish(sheet, dirtyBefore);

            int width = Mathf.RoundToInt(height * aspect);
            // the game's CanvasScaler: reference 1920 x 1080, match .5 (UI scale 1) -> scaleFactor = sqrt(W / 1920 * H / 1080)
            float scale = Mathf.Sqrt(width / 1920f * (height / 1080f));
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject New(string name, params Type[] types) { var g = new GameObject(name, types); SceneManager.MoveGameObjectToScene(g, preview); return g; }
            var instance = typeof(PlaytestUiRoot).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var oldInstance = PlaytestUiRoot.Instance;
            RenderTexture rt = null; WorldMapPresenter presenter = null; MapStyle304SO style = null; WorldMacroPlaytestSO content = null; GameObject panel = null;
            try
            {
                var cam = New("Map5 page camera", typeof(Camera)).GetComponent<Camera>();
                cam.enabled = false; cam.scene = preview; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.06f, .06f, .055f); cam.cullingMask = 1 << 31;
                rt = new RenderTexture(width, height, 24) { name = "Map5Page" }; cam.targetTexture = rt; cam.aspect = aspect;
                panel = New("Map5 page canvas", typeof(RectTransform), typeof(Canvas));
                var canvas = panel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 1; canvas.scaleFactor = scale;
                var sessionHost = New("Map5 page session"); sessionHost.SetActive(false);
                var session = sessionHost.AddComponent<WorldMacroPlaytestSession>();
                content = Object.Instantiate(ui.Content);
                Vector2 here = at ?? new Vector2(content.StartFeet.x, content.StartFeet.z);
                content.StartFeet = new Vector3(here.x, content.StartFeet.y, here.y); session.Content = content;
                var progress = WorldMacroProgress.CreateNew("map5-page", content.StartFeet, 0);
                typeof(WorldMacroPlaytestSession).GetProperty("Progress").SetValue(session, progress);
                style = Object.Instantiate(styleAsset); style.hideFlags = HideFlags.HideAndDontSave; style.Notation308 = bundleOn ? notation : null;
                if (instance != null && instance.CanWrite) instance.SetValue(null, ui);
                var theme = ui.Theme; var data = ui.MapData;
                presenter = panel.AddComponent<WorldMapPresenter>(); presenter.enabled = false;
                presenter.Initialize((RectTransform)panel.transform, session, ui.WorldSheet, new WorldMapUiDependencies
                { BakedData = data, MapStyle = style, Theme = theme, Icons = theme.Icons, Font = theme.Font, PaperTexture = theme.PaperTexture, Ink = theme.Ink, Paper = theme.Paper, Muted = theme.Muted, Seal = theme.Seal, ReducedMotion = true });
                presenter.PreviewReveal308(true, here, 0f);
                progress.ui.discoveredMarkers = data.Markers.Where(m => m != null).Select(m => m.Id).ToList();
                foreach (var tr in panel.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 31;
                Canvas.ForceUpdateCanvases(); cam.Render();
                presenter.OpenedFromPage304 = true;
                presenter.SetExpanded(true);
                void Refresh() { presenter.PreviewRefresh308(); Canvas.ForceUpdateCanvases(); }
                Refresh();

                bool bundle = presenter.Notation308 != null;
                var root = presenter.FullRoot;
                var paper = root.Find("TwiceFoldedHanji") as RectTransform;
                var window = paper != null ? paper.Find("PrintedMapWindow") as RectTransform : null;
                var input = window != null ? window.Find("MapInput") as RectTransform : null;
                var labels = window != null ? window.Find("MapLabels") as RectTransform : null;
                var page = root.Find("MapPage304") as RectTransform;
                var title = page != null ? page.Find("MapTitle") as RectTransform : null;
                var north = page != null ? page.Find("North") as RectTransform : null;
                var controlsRow = page != null ? page.Find("PaperMapControls") as RectTransform : null;
                var board = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(x => x.name == "MapBoard308");
                sheet.Add(paper != null && window != null && input != null && labels != null && page != null && title != null && north != null && controlsRow != null,
                    "AC-E2 names: TwiceFoldedHanji / PrintedMapWindow / MapInput / MapLabels / MapPage304 / MapTitle / North / PaperMapControls" + (bundle ? "" : " (no bundle: no board)"));
                if (paper == null || window == null || input == null || labels == null || page == null || title == null || north == null || controlsRow == null) return Finish(sheet, dirtyBefore);

                float k = presenter.PreviewPageScale308;
                Vector2 worldMin = data.BoundsMin, worldSize = data.BoundsMax - data.BoundsMin;
                // page px (1920 x 1080, top-left origin) of a rect: the page root's local space is centred and scaled by k
                Rect PagePx(RectTransform r)
                {
                    Rect a = HarnessUiRules304.RectIn(root, r);
                    return new Rect(a.xMin / k + 960f, 540f - a.yMax / k, a.width / k, a.height / k);
                }
                Vector2 ToWindow(Vector2 worldXZ)
                {
                    Rect uv = presenter.PreviewView308, w = window.rect;
                    var n = new Vector2((worldXZ.x - worldMin.x) / worldSize.x, (worldXZ.y - worldMin.y) / worldSize.y);
                    return new Vector2(Mathf.LerpUnclamped(w.xMin, w.xMax, (n.x - uv.x) / uv.width), Mathf.LerpUnclamped(w.yMin, w.yMax, (n.y - uv.y) / uv.height));
                }
                Vector2 ToWorld(Vector2 windowPoint)
                {
                    Rect uv = presenter.PreviewView308, w = window.rect;
                    var v = new Vector2(Mathf.InverseLerp(w.xMin, w.xMax, windowPoint.x), Mathf.InverseLerp(w.yMin, w.yMax, windowPoint.y));
                    return new Vector2(worldMin.x + (uv.x + v.x * uv.width) * worldSize.x, worldMin.y + (uv.y + v.y * uv.height) * worldSize.y);
                }
                // metres per PAGE px along x and z
                Vector2 Scale() { Rect uv = presenter.PreviewView308, w = window.rect; return new Vector2(uv.width * worldSize.x / (w.width / k), uv.height * worldSize.y / (w.height / k)); }
                bool Isotropic(out float worst) { Vector2 s = Scale(); worst = Mathf.Abs(s.x - s.y) / Mathf.Max(1e-6f, s.x); return worst <= 1e-4f; }
                Census Now() => Inspect(root, null, data, style, texts);
                int maxTags = 0;
                Census Tagged() { var c = Now(); maxTags = Mathf.Max(maxTags, c.TagCount); return c; }

                // ---- mutants (the check must be able to fail)
                if (mutant == "legend")
                {
                    var planted = new GameObject("LegendPanel", typeof(RectTransform)); planted.transform.SetParent(page, false); planted.layer = 31;
                    var word = new GameObject("LegendCaption", typeof(RectTransform)).AddComponent<Text>(); word.transform.SetParent(planted.transform, false); word.text = "mutant";
                    word.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    sheet.Info("MUTANT legend: a LegendPanel object with a text was planted on the page - the lines below must fail");
                }

                // ---- AC-M5.1 / M5.2 / M5.5 at rest (default view, no pointer)
                presenter.PreviewPointerOff308(); Refresh();
                if (mutant == "tags")
                {
                    int forced = 0;
                    foreach (Transform child in labels)
                        if (forced < 2 && child.name.StartsWith("Label_", StringComparison.Ordinal) && !child.name.StartsWith("Label_Region_", StringComparison.Ordinal) && child.GetComponent<TMP_Text>() is TMP_Text forcedLabel && !string.IsNullOrWhiteSpace(forcedLabel.text))
                        { child.gameObject.SetActive(true); forcedLabel.alpha = 1f; forced++; }
                    sheet.Info("MUTANT tags: " + forced + " name labels were switched on without a pointer - the tag lines below must fail");
                }
                var rest = Tagged();
                sheet.Add(rest.Objects.Count == 0, "AC-M5.1 no legend / list / card / hint / tooltip object on the page, active or not" + (rest.Objects.Count > 0 ? ": " + string.Join("; ", rest.Objects.Take(6)) : ""));
                sheet.Add(rest.Components.Count == 0, "AC-M5.1 no ScrollRect / RectMask2D / list row / stroke swatch component" + (rest.Components.Count > 0 ? ": " + string.Join("; ", rest.Components.Take(6)) : ""));
                sheet.Add(rest.Controls.Count == 0, "AC-M5.1 the key line has exactly [" + string.Join(", ", texts.controls) + "]" + (rest.Controls.Count > 0 ? ": " + string.Join("; ", rest.Controls) : ""));
                sheet.Add(rest.Texts.Count == 0 && rest.TextCount > 0, "AC-M5.2 every visible text is on the allow-list by value (" + rest.TextCount + " texts; the rail is not in this fixture - Play's layout-check reads it)"
                    + (rest.Texts.Count > 0 ? ": " + string.Join("; ", rest.Texts.Take(6)) : ""));
                sheet.Add(rest.TagCount == 0 && rest.PlateCount == 0 && rest.Tags.Count == 0, "AC-M5.5 no pointer: name tags " + rest.TagCount + ", plates " + rest.PlateCount + (rest.Tags.Count > 0 ? ": " + string.Join("; ", rest.Tags.Take(4)) : ""));
                sheet.Add(rest.RegionCount <= texts.max.regionNames, "AC-M5.5 realm names in the default view: " + rest.RegionCount + " (a realm's name stands when its centre is in view; at most " + texts.max.regionNames + ")");
                sheet.Info("default view texts: " + string.Join(" | ", rest.Seen));

                // ---- AC-M5.3 rects (page px)
                Rect want = style.SheetRect; float inset = style.PrintInset; Vector4 grow = style.BoardGrow;
                Rect wantWindow = new Rect(want.x + inset, want.y + inset, want.width - 2f * inset, want.height - 2f * inset);
                Rect wantBoard = new Rect(want.x - grow.x, want.y - grow.y, want.width + grow.x + grow.z, want.height + grow.y + grow.w);
                Rect gotSheet = PagePx(paper), gotWindow = PagePx(window), gotInput = PagePx(input), gotTitle = PagePx(title), gotNorth = PagePx(north), gotControls = PagePx(controlsRow);
                sheet.Add(Near(gotSheet, want, .5f), "AC-M5.3 sheet " + R(gotSheet) + " = MapStyle304SO.SheetRect " + R(want));
                sheet.Add(Near(gotWindow, wantWindow, .5f) && Near(gotInput, wantWindow, .5f), "AC-M5.3 print window " + R(gotWindow) + " and input surface " + R(gotInput) + " = sheet - PrintInset " + R(wantWindow));
                sheet.Add(Mathf.Abs(gotTitle.x - (want.x + style.TitleSlipOffset.x)) <= .5f && Mathf.Abs(gotTitle.y - (want.y + style.TitleSlipOffset.y)) <= .5f,
                    "AC-M5.3 title slip top-left (" + F(gotTitle.x, "0.#") + ", " + F(gotTitle.y, "0.#") + ") = sheet + TitleSlipOffset (" + F(want.x + style.TitleSlipOffset.x, "0.#") + ", " + F(want.y + style.TitleSlipOffset.y, "0.#") + ")");
                sheet.Add(Mathf.Abs(gotNorth.x - (want.xMax - style.NorthOffset.x)) <= .5f && Mathf.Abs(gotNorth.y - (want.y + style.NorthOffset.y)) <= .5f,
                    "AC-M5.3 north mark top-left (" + F(gotNorth.x, "0.#") + ", " + F(gotNorth.y, "0.#") + ") = sheet right - NorthOffset.x, top + NorthOffset.y (" + F(want.xMax - style.NorthOffset.x, "0.#") + ", " + F(want.y + style.NorthOffset.y, "0.#") + ")");
                if (bundle)
                {
                    sheet.Add(board != null && Near(PagePx(board), wantBoard, .5f), "AC-M5.3 board " + (board != null ? R(PagePx(board)) : "MISSING") + " = sheet + BoardGrow " + R(wantBoard));
                    sheet.Add(Mathf.Abs((wantBoard.width - 80f) % 9f) < .01f && Mathf.Abs((wantBoard.height - 82f) % 9f) < .01f, "AC-M5.3 kit rule: board " + F(wantBoard.width) + " = 80 + 9 x " + F((wantBoard.width - 80f) / 9f) + ", " + F(wantBoard.height) + " = 82 + 9 x " + F((wantBoard.height - 82f) / 9f));
                }
                else sheet.Info("no bundle: no lacquer board (AC-M5.3 board lines not counted)");
                float rowsMin = float.MaxValue, rowsMax = float.MinValue;
                foreach (Transform row in controlsRow) { Rect rr = PagePx((RectTransform)row); rowsMin = Mathf.Min(rowsMin, rr.xMin); rowsMax = Mathf.Max(rowsMax, rr.xMax); }
                sheet.Add(Mathf.Abs(gotSheet.center.x - 960f) <= .5f && Mathf.Abs((rowsMin + rowsMax) * .5f - 960f) <= .5f && (!bundle || board == null || Mathf.Abs(PagePx(board).center.x - 960f) <= .5f),
                    "AC-M5.3 one axis: sheet centre x " + F(gotSheet.center.x, "0.#") + ", key line x " + F(rowsMin, "0.#") + " - " + F(rowsMax, "0.#") + " (centre " + F((rowsMin + rowsMax) * .5f, "0.#") + ")" + (bundle && board != null ? ", board centre x " + F(PagePx(board).center.x, "0.#") : ""));
                sheet.Add(rowsMin >= want.xMin && rowsMax <= want.xMax, "AC-M5.10 the key line's rows stay over the sheet's span x " + F(want.xMin, "0.#") + " - " + F(want.xMax, "0.#"));
                sheet.Add(gotTitle.xMin >= HarnessUiRules304.TitleSafeLeft && gotNorth.xMax <= HarnessUiRules304.TitleSafeRight && gotTitle.yMin >= HarnessUiRules304.TitleSafeTop && gotNorth.yMin >= HarnessUiRules304.TitleSafeTop,
                    "title slip and north mark inside the title-safe box (x " + F(HarnessUiRules304.TitleSafeLeft) + " - " + F(HarnessUiRules304.TitleSafeRight) + ")");

                // ---- AC-M5.11 page scale and canvas
                float wantK = Mathf.Min(1f, width / scale / 1920f, height / scale / 1080f);
                Rect canvasRect = ((RectTransform)panel.transform).rect; bool inside = true;
                foreach (var r in new[] { paper, controlsRow, title, north, board })
                {
                    if (r == null) continue;
                    Rect a = HarnessUiRules304.RectIn((RectTransform)panel.transform, r);
                    inside &= a.xMin >= canvasRect.xMin - .5f && a.xMax <= canvasRect.xMax + .5f && a.yMin >= canvasRect.yMin - .5f && a.yMax <= canvasRect.yMax + .5f;
                }
                sheet.Add(Mathf.Abs(k - wantK) <= .002f && inside, "AC-M5.11 " + aspectName + " " + width + " x " + height + ": page scale " + F(k, "0.####") + " (rule " + F(wantK, "0.####") + "), sheet / board / key line / slip / north inside the canvas " + inside);

                // ---- AC-M5.4 default view: the whole east-west span, same scale both ways
                Rect uv0 = presenter.PreviewView308; Vector2 s0 = Scale();
                bool illustrated = data.HasIllustration && data.DisplayMap != null && data.DisplayMap.width > 0;
                float fitScale = worldSize.x / wantWindow.width;
                if (illustrated && data.ZoneAt(new Vector3(here.x, 0f, here.y)) == null)
                    sheet.Add(Mathf.Abs(uv0.width - 1f) < 1e-5f && Mathf.Abs(s0.x - fitScale) <= .005f, "AC-M5.4 default view: uv width " + F(uv0.width, "0.#####") + " (whole east-west span), " + F(s0.x, "0.####") + " m per page px (world width / window width = " + F(fitScale, "0.####") + ")");
                else sheet.Info("default view not the outdoor illustrated one here (interior, or a map without an illustration): uv " + uv0 + ", " + F(s0.x, "0.####") + " m per page px");
                sheet.Add(Isotropic(out float worst0), "AC-M5.4 default view isotropic: east-west " + F(s0.x, "0.#####") + " / north-south " + F(s0.y, "0.#####") + " m per px (difference " + F(worst0, "0.#######") + ")");

                // ---- AC-M5.4 / M5.12 wheel: every step isotropic, the closest zoom is a scale, a step out at the default view changes nothing
                presenter.PreviewZoom308(-1f, Vector2.zero); Refresh();
                sheet.Add(presenter.PreviewView308 == uv0, "AC-M5.12 wheel out at the default view changes nothing (view " + presenter.PreviewView308 + ")");
                int steps = 0; float worstStep = 0f, lastWidth = presenter.PreviewView308.width;
                for (int i = 0; i < 40; i++)
                {
                    presenter.PreviewZoom308(1f, Vector2.zero); Refresh();
                    Isotropic(out float w); worstStep = Mathf.Max(worstStep, w);
                    if (Mathf.Approximately(presenter.PreviewView308.width, lastWidth)) break;
                    lastWidth = presenter.PreviewView308.width; steps++;
                }
                float closest = Scale().x, wantClosest = style.MinViewWidth * worldSize.x / Mathf.Max(1f, style.ViewReferenceWidthPx);
                sheet.Add(worstStep <= 1e-4f, "AC-M5.4 every wheel step isotropic (" + steps + " steps in, worst difference " + F(worstStep, "0.#######") + ")");
                if (data.ZoneAt(new Vector3(here.x, 0f, here.y)) == null)
                    sheet.Add(Mathf.Abs(closest - wantClosest) <= .002f, "AC-M5.12 closest zoom " + F(closest, "0.####") + " m per page px = MinViewWidth x world width / ViewReferenceWidthPx " + F(wantClosest, "0.####") + " (the scale of the 740 px window)");
                else sheet.Info("interior: closest zoom " + F(closest, "0.####") + " m per page px");
                var closeCensus = Tagged();
                sheet.Add(closeCensus.FailureCount == 0, "AC-M5.2 closest zoom: texts and objects still on the allow-list (realm names " + closeCensus.RegionCount + ", slip realm lines " + closeCensus.RealmLines + ")"
                    + (closeCensus.FailureCount > 0 ? ": " + string.Join("; ", closeCensus.Failures.Take(4)) : ""));

                // ---- AC-M5.7: the pin as a probe mark at nine places of the window, at the closest zoom and four steps in from the default
                bool outdoors = data.ZoneAt(new Vector3(here.x, 0f, here.y)) == null;
                Rect titleIn = HarnessUiRules304.RectIn(window, title), northIn = HarnessUiRules304.RectIn(window, north);
                if (outdoors)
                {
                    int probes = 0, shown = 0, covered = 0, bad = 0; float worstShift = 0f; var notes = new List<string>();
                    for (int view = 0; view < 2; view++)
                    {
                        if (view == 1) { presenter.FocusCurrent(); Refresh(); for (int i = 0; i < 4; i++) presenter.PreviewZoom308(1f, Vector2.zero); Refresh(); }
                        Rect w = window.rect; float edge = 14f * k;
                        for (int iy = 0; iy < 3; iy++) for (int ix = 0; ix < 3; ix++)
                        {
                            var p = new Vector2(ix == 0 ? w.xMin + edge : ix == 1 ? w.center.x : w.xMax - edge, iy == 0 ? w.yMin + edge : iy == 1 ? w.center.y : w.yMax - edge);
                            progress.ui.pin.active = true; progress.ui.pin.worldXZ = ToWorld(p); progress.ui.pin.label = "";
                            presenter.PreviewPointerOff308(); Refresh();
                            if (!presenter.PreviewMarkPoint308("Pin", out Vector2 pinAt)) continue;   // outside the outline's view: no pin mark
                            probes++;
                            titleIn = HarnessUiRules304.RectIn(window, title); northIn = HarnessUiRules304.RectIn(window, north);
                            bool under = titleIn.Contains(pinAt) || northIn.Contains(pinAt);
                            presenter.PreviewPointer308(pinAt); Refresh();
                            var c = Tagged();
                            var plate = labels.Find("Plate_Pin") as RectTransform; var label = labels.Find("Label_Pin") as RectTransform;
                            bool pinTag = label != null && label.gameObject.activeSelf && label.GetComponent<TMP_Text>().alpha > 0f;
                            if (under) { covered++; if (pinTag) { bad++; notes.Add("the probe under the slip / north mark got its tag at " + p); } continue; }
                            // a nearer baked mark may win the pick: then the tag is that mark's, not the pin's - not a placement probe
                            if (!pinTag && c.TagCount == 1) continue;
                            if (!pinTag || c.TagCount != 1) { bad++; notes.Add("no tag for the probe at window " + p + " (view " + view + ", tags " + c.TagCount + ")"); continue; }
                            shown++;
                            Rect box = HarnessUiRules304.RectIn(window, bundle && plate != null && plate.gameObject.activeSelf ? plate : label);
                            bool fits = box.xMin >= w.xMin + 2f - .5f && box.xMax <= w.xMax - 2f + .5f && box.yMin >= w.yMin + 2f - .5f && box.yMax <= w.yMax - 2f + .5f;
                            bool clear = !box.Overlaps(titleIn) && !box.Overlaps(northIn);
                            float away = Mathf.Max(0f, Mathf.Max(Mathf.Max(box.xMin - pinAt.x, pinAt.x - box.xMax), Mathf.Max(box.yMin - pinAt.y, pinAt.y - box.yMax))) / k;
                            worstShift = Mathf.Max(worstShift, away);
                            if (bundle && (!fits || !clear)) { bad++; notes.Add("tag " + R(box) + " at window " + p + (fits ? "" : " leaves the window") + (clear ? "" : " covers the slip / north mark")); }
                        }
                    }
                    progress.ui.pin.active = false; presenter.PreviewPointerOff308(); Refresh();
                    if (bundle) sheet.Add(bad == 0 && shown > 0, "AC-M5.7 probe mark at nine window places x two zooms: " + shown + " tags shown whole inside the window (2 px) and clear of the slip / north mark, " + covered
                        + " probes under the slip / north mark got none, of " + probes + "; farthest plate edge from its mark " + F(worstShift, "0.#") + " page px" + (notes.Count > 0 ? ": " + string.Join("; ", notes.Take(4)) : ""));
                    else sheet.Add(bad == 0 && shown > 0, "AC-M5.7 (no bundle: the plain label, no window fit - eye check on the capture) probe tags shown " + shown + " of " + probes + (notes.Count > 0 ? ": " + string.Join("; ", notes.Take(4)) : ""));
                }
                else sheet.Info("interior: no pin, the placement probe is an outdoor check");

                // ---- AC-M5.6 picking, at the default view
                presenter.FocusCurrent(); Refresh();
                if (outdoors)
                {
                    int marks = 0, named = 0, hidden = 0; var wrong = new List<string>(); float radius = style.HoverRadiusPx * k;
                    var drawn = new List<(WorldMapMarkerSpec spec, Vector2 at)>();
                    foreach (var m in data.Markers) if (m != null && presenter.PreviewMarkPoint308(m.Id, out Vector2 p)) drawn.Add((m, p));
                    titleIn = HarnessUiRules304.RectIn(window, title); northIn = HarnessUiRules304.RectIn(window, north);
                    foreach (var (spec, p) in drawn)
                    {
                        marks++;
                        presenter.PreviewPointer308(p); Refresh();
                        var c = Tagged();
                        if (titleIn.Contains(p) || northIn.Contains(p)) { hidden++; if (c.TagCount == 1 && Clean(c.TagText) == Clean(Short(spec.Label))) wrong.Add(spec.Id + " (under the slip / north mark) got its tag"); continue; }
                        // another mark at the same spot with a higher name priority may rightly win
                        bool rival = drawn.Any(o => o.spec != spec && (o.at - p).magnitude <= style.HoverTiePx * k);
                        if (c.TagCount == 1 && c.Tags.Count == 0 && (Clean(c.TagText) == Clean(Short(spec.Label)) || rival)) named++;
                        else wrong.Add(spec.Id + ": tags " + c.TagCount + " \"" + c.TagText + "\"" + (c.Tags.Count > 0 ? " " + c.Tags[0] : ""));
                    }
                    sheet.Add(marks > 0 && wrong.Count == 0, "AC-M5.6 pointer on each drawn mark's centre -> that mark's short name, one tag: " + named + " of " + marks + " (" + hidden + " under the slip / north mark: none)"
                        + (wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(5)) : ""));
                    // just outside the radius, in a direction with no other mark near
                    int far = 0, farOk = 0;
                    foreach (var (spec, p) in drawn)
                    {
                        for (int d = 0; d < 8; d++)
                        {
                            float a = d * Mathf.PI / 4f; var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + 3f * k);
                            if (!window.rect.Contains(q) || drawn.Any(o => (o.at - q).magnitude <= radius + k)) continue;
                            far++; presenter.PreviewPointer308(q); Refresh();
                            if (Tagged().TagCount == 0) farOk++;
                            break;
                        }
                    }
                    sheet.Add(far > 0 && farOk == far, "AC-M5.6 pointer " + F(style.HoverRadiusPx + 3f) + " px from a mark, no other mark near -> no tag: " + farOk + " of " + far);
                    presenter.PreviewPointerOff308(); Refresh();
                    sheet.Add(Tagged().TagCount == 0, "AC-M5.6 pointer out / button down -> no tag");
                    // #214: an undiscovered mark's place gives no name
                    if (drawn.Count > 0)
                    {
                        var (spec, p) = drawn.FirstOrDefault(o => !titleIn.Contains(o.at) && !northIn.Contains(o.at) && !drawn.Any(x => x.spec != o.spec && (x.at - o.at).magnitude <= radius * 2f));
                        if (spec != null)
                        {
                            progress.ui.discoveredMarkers.Remove(spec.Id); Refresh();
                            presenter.PreviewPointer308(p); Refresh();
                            var c = Tagged();
                            sheet.Add(!presenter.PreviewMarkPoint308(spec.Id, out _) && c.TagCount == 0, "AC-M5.6 / #214 the place of an undiscovered mark (" + spec.Id + ") gives no icon and no tag (tags " + c.TagCount + " \"" + c.TagText + "\")");
                            progress.ui.discoveredMarkers.Add(spec.Id); presenter.PreviewPointerOff308(); Refresh();
                        }
                        else sheet.Info("no drawn mark stands alone enough for the undiscovered-mark probe in this view");
                    }
                    // tie: the pin on a mark's own spot -> the mark's name (pin = the lowest name priority); the wake place on a non-rest mark -> the wake place
                    var host = drawn.FirstOrDefault(o => !titleIn.Contains(o.at) && !northIn.Contains(o.at) && o.spec.Kind != WorldMapMarkerKind.Rest && (o.spec.WorldXZ - here).sqrMagnitude > 400f
                        && !drawn.Any(x => x.spec != o.spec && (x.at - o.at).magnitude <= radius));
                    if (host.spec != null)
                    {
                        progress.ui.pin.active = true; progress.ui.pin.worldXZ = host.spec.WorldXZ; progress.ui.pin.label = "";
                        presenter.PreviewPointerOff308(); Refresh(); presenter.PreviewPointer308(host.at); Refresh();
                        var c = Tagged();
                        sheet.Add(c.TagCount == 1 && Clean(c.TagText) == Clean(Short(host.spec.Label)), "AC-M5.6 tie (the pin on " + host.spec.Id + "'s spot): the place's name wins - one tag \"" + c.TagText + "\"");
                        progress.ui.pin.active = false;
                        Vector3 keep = progress.ledger.checkpointPosition;
                        progress.ledger.checkpointPosition = new Vector3(host.spec.WorldXZ.x, keep.y, host.spec.WorldXZ.y);
                        presenter.PreviewPointerOff308(); Refresh(); presenter.PreviewPointer308(host.at); Refresh();
                        c = Tagged();
                        sheet.Add(c.TagCount == 1 && Clean(c.TagText) == Clean(texts.checkpointTag), "AC-M5.6 tie (the wake place on " + host.spec.Id + "'s spot): the wake place wins - one tag \"" + c.TagText + "\"");
                        progress.ledger.checkpointPosition = keep; presenter.PreviewPointerOff308(); Refresh();
                    }
                    else sheet.Info("no lone non-rest mark in the default view for the tie probes");
                    // the current position has no name
                    Vector2 me = ToWindow(here);
                    if (!drawn.Any(o => (o.at - me).magnitude <= radius + k))
                    {
                        presenter.PreviewPointer308(me); Refresh();
                        sheet.Add(Tagged().TagCount == 0, "AC-M5.6 pointer on the current position (no mark within the radius) -> no tag");
                        presenter.PreviewPointerOff308(); Refresh();
                    }
                    else sheet.Info("a mark stands within the pick radius of the current position: the 'no name for the position' probe is skipped here (try page:at=x,z)");
                }

                // ---- AC-M5.12 whole world: the strip, the input surface beside it, the wheel
                presenter.ShowWholeWorld(); Refresh();
                if (outdoors)
                {
                    Rect whole = PagePx(window), wholeInput = PagePx(input); Rect uvW = presenter.PreviewView308;
                    float wantWidth = wantWindow.height * worldSize.x / worldSize.y;
                    sheet.Add(uvW == new Rect(0, 0, 1, 1) && Mathf.Abs(whole.width - Mathf.Min(wantWidth, wantWindow.width)) <= .5f && Mathf.Abs(whole.center.x - want.center.x) <= .5f && presenter.PreviewWholeLayout308,
                        "AC-M5.12 T: the whole world, window " + R(whole) + " in the world's shape in the middle of the sheet (" + F((wantWindow.width - whole.width) * .5f, "0.#") + " px of bare paper each side)");
                    sheet.Add(Near(wholeInput, wantWindow, .5f), "AC-M5.12 T: the input surface still covers the whole printed area " + R(wholeInput) + " (wheel and drag work beside the strip)");
                    sheet.Add(Isotropic(out float worstWhole), "AC-M5.4 whole world isotropic (difference " + F(worstWhole, "0.#######") + ")");
                    var wholeCensus = Tagged();
                    sheet.Add(wholeCensus.FailureCount == 0 && wholeCensus.TagCount == 0, "AC-M5.2 whole world: allow-list holds, name tags " + wholeCensus.TagCount + ", realm names " + wholeCensus.RegionCount
                        + (wholeCensus.FailureCount > 0 ? ": " + string.Join("; ", wholeCensus.Failures.Take(4)) : ""));
                    presenter.PreviewZoom308(-1f, Vector2.zero); Refresh();
                    sheet.Add(presenter.PreviewWholeLayout308 && presenter.PreviewView308 == new Rect(0, 0, 1, 1), "AC-M5.12 wheel out in the whole-world view changes nothing");
                    // one notch in, with the pointer on the bare paper beside the strip
                    var beside = new Vector2(window.rect.xMax + 120f * k, window.rect.height * .25f);
                    presenter.PreviewZoom308(1f, beside); Refresh();
                    Vector2 after = Scale(); Rect back = PagePx(window);
                    sheet.Add(!presenter.PreviewWholeLayout308 && Near(back, wantWindow, .5f) && Mathf.Abs(after.x - s0.x) <= .005f,
                        "AC-M5.12 one wheel notch in from the whole-world view (pointer beside the strip): back to the wide window " + R(back) + " at the default scale " + F(after.x, "0.####") + " m per px");
                }
                else sheet.Info("interior: T stays on the local plan");
                presenter.FocusCurrent(); Refresh();
                sheet.Add(presenter.PreviewView308 == uv0 && !presenter.PreviewWholeLayout308, "AC-M5.12 R: back to the default view " + presenter.PreviewView308);

                // ---- AC-M5.5 over the whole run, and the L key
                sheet.Add(maxTags <= texts.max.tag, "AC-M5.5 most name tags seen at once during this run: " + maxTags + " (at most " + texts.max.tag + ")");
                var keys = new List<string>();
                foreach (var field in typeof(WorldMapPresenter).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (field.FieldType == typeof(InputAction) && field.GetValue(presenter) is InputAction action)
                        foreach (var binding in action.bindings) keys.Add(binding.path);
                sheet.Add(keys.Count == 3 && !keys.Any(p => p.EndsWith("/l", StringComparison.OrdinalIgnoreCase)), "AC-M5.12 the page's own keys: [" + string.Join(", ", keys) + "] (three, no L)");
                sheet.Add(typeof(WorldMapPresenter).GetProperty("LegendVisible") == null && typeof(WorldMapPresenter).GetMethod("ToggleLegend", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) == null,
                    "AC-M5.1 WorldMapPresenter has no LegendVisible / ToggleLegend member");
                sheet.Info("fixture: " + (bundle ? "bundle ON (stage '" + presenter.Notation308.Stage + "')" : "no bundle") + ", at " + here + ", every marker known (" + progress.ui.discoveredMarkers.Count + "), every cell walked, " + width + " x " + height
                    + ", strip vertices " + presenter.FullStripVertices308 + (mutant.Length > 0 ? ", MUTANT " + mutant : ""));
                sheet.Info("not in this check (Play / captures): the rail's texts, the fold, a real pointer, the click that puts the pin (it saves), the minimap's pixels (AC-M5.8), text scale 1.25 (layout-large)");
            }
            catch (Exception e) { sheet.Add(false, "the check threw: " + e.GetType().Name + " " + e.Message); }
            finally
            {
                if (instance != null && instance.CanWrite) instance.SetValue(null, oldInstance);
                if (presenter != null) presenter.ReleasePreview308();
                if (panel != null) Object.DestroyImmediate(panel);
                if (content != null) Object.DestroyImmediate(content);
                if (style != null) Object.DestroyImmediate(style);
                if (rt != null) { if (RenderTexture.active == rt) RenderTexture.active = null; rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return Finish(sheet, dirtyBefore);
        }

        static string Finish(Sheet sheet, bool dirtyBefore)
        {
            sheet.Info("sceneDirty " + dirtyBefore + " -> " + SceneManager.GetActiveScene().isDirty + " (preview scene closed, nothing saved)");
            string head = "page " + (sheet.Failed == 0 ? "ok" : "FAILED") + " " + (sheet.Total - sheet.Failed) + "/" + sheet.Total;
            string folder = Path.Combine(Harness303.RepoRoot, "Art", "UI308", "Map");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "checks-page.txt"), head + "\n" + string.Join("\n", sheet.Lines) + "\n", new UTF8Encoding(false));
            return head + "\n" + string.Join("\n", sheet.Lines);
        }
    }
}
