using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEditor;
using Oheangbu.App.World;
using Oheangbu.BrushRender;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #300 teaser stills (editor only, Play mode). Stages extra actors (a scaled folklore body in a sampled clip pose), and
    // renders a third-person still of a frozen drawing take (film pauseAt) with the body the first-person camera keeps
    // shadow-only shown and the near arm/brush hidden. The glyph can be scaled about its live stroke end, re-anchored on the
    // world brush tip, so the ink reads at a wide framing. Nothing is saved; every change is undone after the render.
    //   teaser-stage:<spec.json>                 — {actors:[{prefab, position[3], yaw, faceTarget[3]?, scale, clip, take, time, ground}]}
    //   teaser-pose:<actor>:<take>:<seconds>     — resample a staged actor's clip pose
    //   teaser-clear
    //   teaser-shot:<name>:<eye>:<target>[:fov=][:w=][:h=][:glyph=<scale>][:glyphyaw=<deg>][:sky=<material>][:body=0]
    //   teaser-info                              — player, eye, brush tip, strokes, staged actors, player renderer states
    public static partial class Presentation297
    {
        [Serializable] sealed class TeaserActor { public string prefab = ""; public float[] position; public float yaw; public float[] faceTarget; public float scale = 1f; public string clip = ""; public string take = ""; public float time; public bool ground = true; }
        [Serializable] sealed class TeaserSpec { public TeaserActor[] actors = new TeaserActor[0]; }

        sealed class Staged { public GameObject Root; public string Clip; public PlayableGraph Graph; }
        static readonly List<Staged> staged = new List<Staged>();

        static string TeaserStage(string specPath)
        {
            if (!EditorApplication.isPlaying) throw new Exception("teaser-stage: Play mode only");
            TeaserClear();
            var spec = JsonUtility.FromJson<TeaserSpec>(File.ReadAllText(specPath));
            var sb = new StringBuilder();
            foreach (var a in spec.actors)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(a.prefab);
                if (prefab == null) throw new Exception("teaser-stage: no prefab " + a.prefab);
                Vector3 p = new Vector3(a.position[0], a.position[1], a.position[2]);
                if (a.ground && Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
                float yaw = a.yaw;
                if (a.faceTarget != null && a.faceTarget.Length == 3)
                    yaw = Mathf.Atan2(a.faceTarget[0] - p.x, a.faceTarget[2] - p.z) * Mathf.Rad2Deg;
                var go = Object.Instantiate(prefab, p, Quaternion.Euler(0f, yaw, 0f));
                go.name = "Teaser300_" + prefab.name;
                go.transform.localScale = Vector3.one * a.scale;
                var entry = new Staged { Root = go, Clip = a.clip };
                staged.Add(entry);
                if (!string.IsNullOrEmpty(a.clip)) Pose(entry, a.take, a.time);
                sb.AppendLine(go.name + " at " + p.ToString("F2") + " yaw " + yaw.ToString("F1", CultureInfo.InvariantCulture) + " scale " + a.scale.ToString(CultureInfo.InvariantCulture) + " " + BoundsText(go));
            }
            return sb.ToString();
        }

        static string BoundsText(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0) return "no renderers";
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return "bounds " + b.center.ToString("F2") + " size " + b.size.ToString("F2");
        }

        // Clip pose through a manual PlayableGraph: no controller, no root motion, evaluated once and held
        static void Pose(Staged entry, string take, float seconds)
        {
            var animator = entry.Root.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new Exception("teaser pose: no Animator under " + entry.Root.name);
            var clip = AssetDatabase.LoadAllAssetsAtPath(entry.Clip).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == take && !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) throw new Exception("teaser pose: no clip " + take + " in " + entry.Clip);
            if (entry.Graph.IsValid()) entry.Graph.Destroy();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Teaser300_" + entry.Root.name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "pose", animator);
            var play = AnimationClipPlayable.Create(graph, clip);
            play.SetApplyFootIK(true);
            play.SetTime(seconds); play.SetTime(seconds);
            play.SetSpeed(0);
            output.SetSourcePlayable(play);
            graph.Evaluate(0f);
            entry.Graph = graph;
        }

        static string TeaserPose(string[] a)
        {
            int index = int.Parse(a[1], CultureInfo.InvariantCulture);
            if (index < 0 || index >= staged.Count) throw new Exception("teaser-pose: no staged actor " + index);
            Pose(staged[index], a[2], float.Parse(a[3], CultureInfo.InvariantCulture));
            return staged[index].Root.name + " " + a[2] + " @" + a[3] + " " + BoundsText(staged[index].Root);
        }

        static string TeaserClear()
        {
            int n = 0;
            foreach (var s in staged)
            {
                if (s.Graph.IsValid()) s.Graph.Destroy();
                if (s.Root != null) { Object.Destroy(s.Root); n++; }
            }
            staged.Clear();
            return "cleared " + n;
        }

        static Transform PlayerRoot()
        {
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Transform player = gesture != null ? gesture.transform : null;
            while (player != null && player.parent != null && !player.name.StartsWith("Macro_CombatPlayerRig", StringComparison.Ordinal)) player = player.parent;
            return player;
        }

        static bool Under(Transform t, string name)
        {
            for (; t != null; t = t.parent) if (t.name == name) return true;
            return false;
        }

        // The first-person camera keeps the body shadow-only; the near arm/brush are first-person props at the eye.
        static void ShowBody(List<Action> undo)
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !(Under(r.transform, "C02_NearArm") || Under(r.transform, "C02_NearBrush"))) continue;
                var rr = r; rr.enabled = false; undo.Add(() => rr.enabled = true);
            }
            var player = PlayerRoot();
            if (player == null) return;
            foreach (var r in player.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
                var rr = r; rr.shadowCastingMode = ShadowCastingMode.On; undo.Add(() => rr.shadowCastingMode = ShadowCastingMode.ShadowsOnly);
            }
        }

        static BrushStrokeRenderer[] LiveStrokes() => Object.FindObjectsByType<BrushStrokeRenderer>(FindObjectsSortMode.None)
            .Where(s => s.isActiveAndEnabled && s.Data.Points.Count > 1).ToArray();

        static bool StrokeEnd(BrushStrokeRenderer[] strokes, out Vector3 end)
        {
            end = default;
            if (strokes.Length == 0) return false;
            var current = strokes.OrderByDescending(s => s.Data.Points[s.Data.Points.Count - 1].Time).ThenByDescending(s => s.name, StringComparer.Ordinal).First();
            end = current.transform.TransformPoint(current.Data.Points[current.Data.Points.Count - 1].Position);
            return true;
        }

        // Scale every live stroke about the current stroke end and move that end onto the world brush tip.
        static void ScaleGlyph(float k, float yawDegrees, List<Action> undo)
        {
            var strokes = LiveStrokes();
            if (!StrokeEnd(strokes, out Vector3 end)) return;
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            Vector3 dest = gesture != null && gesture.WorldTip != null ? gesture.WorldTip.position : end;
            Quaternion turn = Quaternion.AngleAxis(yawDegrees, Vector3.up);
            foreach (var s in strokes)
            {
                var t = s.transform; Vector3 p0 = t.position; Quaternion r0 = t.rotation; Vector3 s0 = t.localScale;
                t.SetPositionAndRotation(dest + turn * ((p0 - end) * k), turn * r0);
                t.localScale = s0 * k;
                undo.Add(() => { t.SetPositionAndRotation(p0, r0); t.localScale = s0; });
            }
        }

        static string TeaserShot(string[] a)
        {
            string name = a[1];
            Vector3 eye = Vec(a[2]), target = Vec(a[3]);
            float fov = Opt(a, "fov", 40f);
            int w = Mathf.RoundToInt(Opt(a, "w", 1920f)), h = Mathf.RoundToInt(Opt(a, "h", 1080f));
            float glyph = Opt(a, "glyph", 1f), glyphYaw = Opt(a, "glyphyaw", 0f);
            bool body = Opt(a, "body", 1f) > 0f;
            string sky = a.FirstOrDefault(x => x.StartsWith("sky=", StringComparison.Ordinal))?.Substring(4);
            string file = Path.Combine(Root, "Teaser300", name + ".png");
            var undo = new List<Action>();
            var rig = Open(w, h, fov, false);
            try
            {
                if (body) ShowBody(undo);
                if (Mathf.Abs(glyph - 1f) > 1e-4f || Mathf.Abs(glyphYaw) > 1e-4f) ScaleGlyph(glyph, glyphYaw, undo);
                if (!string.IsNullOrEmpty(sky))
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(sky);
                    if (material == null) throw new Exception("teaser-shot: no sky material " + sky);
                    // skyprops=_Name=v,_Colour=r/g/b — tuning on a transient copy, the asset is untouched
                    string props = a.FirstOrDefault(x => x.StartsWith("skyprops=", StringComparison.Ordinal))?.Substring(9);
                    if (!string.IsNullOrEmpty(props))
                    {
                        var copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                        foreach (var kv in props.Split(','))
                        {
                            var pair = kv.Split('='); var v = pair[1].Split('/').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                            int index = copy.shader.FindPropertyIndex(pair[0]);
                            if (index < 0) throw new Exception("teaser-shot: sky has no " + pair[0]);
                            var type = copy.shader.GetPropertyType(index);
                            if (type == ShaderPropertyType.Color) copy.SetColor(pair[0], new Color(v[0], v[1], v[2], v.Length > 3 ? v[3] : 1f));
                            else if (type == ShaderPropertyType.Vector) copy.SetVector(pair[0], new Vector4(v[0], v.Length > 1 ? v[1] : 0f, v.Length > 2 ? v[2] : 0f, v.Length > 3 ? v[3] : 0f));
                            else copy.SetFloat(pair[0], v[0]);
                        }
                        material = copy; undo.Add(() => Object.DestroyImmediate(copy));
                    }
                    var prior = RenderSettings.skybox; RenderSettings.skybox = material; undo.Add(() => RenderSettings.skybox = prior);
                }
                Render(rig, eye, target, fov, file, 3);
            }
            finally
            {
                for (int i = undo.Count - 1; i >= 0; i--) { try { undo[i](); } catch (Exception e) { Debug.LogException(e); } }
                Close(rig);
            }
            return file;
        }

        static string TeaserInfo()
        {
            var sb = new StringBuilder();
            var s = Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if (s != null && s.Walker != null)
                sb.AppendLine("player " + s.Walker.Body.transform.position.ToString("F2") + " yaw " + s.Walker.Body.transform.eulerAngles.y.ToString("F1", CultureInfo.InvariantCulture));
            var cam = MainCamera();
            if (cam != null) sb.AppendLine("camera " + cam.transform.position.ToString("F2") + " fwd " + cam.transform.forward.ToString("F3") + " fov " + cam.fieldOfView.ToString("F1", CultureInfo.InvariantCulture));
            var gesture = Object.FindFirstObjectByType<WorldMacroPlayerGestureRig>();
            if (gesture != null && gesture.WorldTip != null) sb.AppendLine("world tip " + gesture.WorldTip.position.ToString("F3"));
            var strokes = LiveStrokes();
            sb.AppendLine("strokes " + strokes.Length + (StrokeEnd(strokes, out Vector3 end) ? " end " + end.ToString("F3") : ""));
            foreach (var st in strokes)
            {
                var b = st.GetComponent<Renderer>().bounds;
                sb.AppendLine("  " + st.name + " pts " + st.Data.Points.Count + " bounds " + b.center.ToString("F2") + " size " + b.size.ToString("F2"));
            }
            for (int i = 0; i < staged.Count; i++) if (staged[i].Root != null) sb.AppendLine("staged[" + i + "] " + staged[i].Root.name + " " + BoundsText(staged[i].Root));
            var player = PlayerRoot();
            if (player != null)
            {
                sb.AppendLine("player root " + player.name);
                foreach (var g in player.GetComponentsInChildren<Renderer>(true).GroupBy(r => (r.gameObject.activeInHierarchy, r.enabled, r.shadowCastingMode)))
                    sb.AppendLine("  active=" + g.Key.Item1 + " enabled=" + g.Key.Item2 + " " + g.Key.Item3 + ": " + g.Count() + " e.g. " + string.Join(", ", g.Take(4).Select(r => r.name)));
            }
            foreach (var n in new[] { "C02_NearArm", "C02_NearBrush", "C02_WorldBrush" })
            {
                var t = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(x => x.name == n);
                if (t != null) sb.AppendLine(n + " @ " + t.position.ToString("F2") + " renderers " + t.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled));
            }
            return sb.ToString();
        }
    }
}
