using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // 2026-10-26 fog occlusion probe (user report: "실험실이든 실제 메인씬이든, 화면 위쪽 안개에 글자 및 vfx가 가려지더라"). Read-only diagnostic, Play only:
    //   census                         every enabled renderer that draws in the transparent queues (> 2500): root name, layer, shader -> which spell pieces sit on Default
    //   shoot[:label=x][:pitch=-6][:yaw=0][:cam=capture|main]
    //                                  renders the camera four ways into Art/SpellVFX308/Vol2Vfx/fog_fix/diag/<label>_*.png:
    //                                  a_now (as the game draws it) | b_nomist (CompactMist238 off for this one render) | c_routed (every transparent renderer on Default
    //                                  moved to VfxAfterFog for this one render) | d_noafterfog (the two VfxAfterFog300 passes off: what the after-fog layer would look like if it
    //                                  were not drawn at all - shows which pieces are already on the layer)
    //                                  cam=capture: the Vol2VfxCapture shoulder camera posed on its current job (pitch / yaw offset); cam=main: Camera.main as it stands.
    // Everything switched for a render is put back in the same call (features: SetActive + ClearDirty when they were clean; layers: restored per object). Never saves.
    public static class FogDiag308
    {
        const string Out = "Art/SpellVFX308/Vol2Vfx/fog_fix/diag";
        static readonly string[] FogFeatures = { "CompactMist238" }, AfterFogFeatures = { "VfxAfterFog300_Opaque", "VfxAfterFog300_Transparent" };

        public static string Run(string command)
        {
            if (!EditorApplication.isPlaying) return "REFUSED fogdiag: not in Play";
            var parts = (command ?? "").Trim().Split(':');
            var opt = new Dictionary<string, string>();
            foreach (var p in parts.Skip(1)) { int e = p.IndexOf('='); if (e > 0) opt[p.Substring(0, e)] = p.Substring(e + 1); else if (p.Length > 0) opt[p] = ""; }
            try
            {
                switch (parts[0])
                {
                    case "census": return Census();
                    case "shoot": return Shoot(opt);
                    case "target": return Target(opt);
                }
                return "REFUSED fogdiag: census | shoot[:label=..][:pitch=..][:yaw=..][:cam=capture|main]";
            }
            catch (Exception e) { return "REFUSED fogdiag: " + e.GetType().Name + " " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault(); }
        }

        static bool Transparent(Renderer r)
        {
            foreach (var m in r.sharedMaterials) if (m != null && m.renderQueue > (int)RenderQueue.GeometryLast) return true;
            return false;
        }

        static IEnumerable<Renderer> Live()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.gameObject.scene.IsValid()) yield return r;
        }

        static string Census()
        {
            var sb = new StringBuilder("fogdiag census (transparent renderers, queue > 2500): root | layer | count | shaders\n");
            foreach (var g in Live().Where(Transparent).GroupBy(r => r.transform.root.name + " | " + LayerMask.LayerToName(r.gameObject.layer) + "(" + r.gameObject.layer + ")").OrderBy(g => g.Key))
                sb.AppendLine("  " + g.Key + " | " + g.Count() + " | " + string.Join(", ", g.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name + "@" + m.renderQueue).Distinct().Take(4)));
            int after = LayerMask.NameToLayer("VfxAfterFog");
            sb.AppendLine("opaque / alpha-test renderers on VfxAfterFog: " + Live().Count(r => r.gameObject.layer == after && !Transparent(r)));
            return sb.ToString();
        }

        static List<ScriptableRendererFeature> Features(string[] names)
        {
            var list = new List<ScriptableRendererFeature>();
            foreach (var asset in GraphicsSettings.allConfiguredRenderPipelines.Concat(new[] { GraphicsSettings.currentRenderPipeline }).OfType<UniversalRenderPipelineAsset>().Distinct())
            {
                var data = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(asset) as ScriptableRendererData[];
                foreach (var d in data ?? Array.Empty<ScriptableRendererData>())
                    if (d != null) list.AddRange(d.rendererFeatures.Where(f => f != null && names.Contains(f.name) && f.isActive));
            }
            return list.Distinct().ToList();
        }

        static string Shoot(Dictionary<string, string> opt)
        {
            string label = opt.TryGetValue("label", out var l) && l.Length > 0 ? l : "diag";
            float pitch = opt.TryGetValue("pitch", out var ps) ? float.Parse(ps, System.Globalization.CultureInfo.InvariantCulture) : -6f;
            float yawOff = opt.TryGetValue("yaw", out var ys) ? float.Parse(ys, System.Globalization.CultureInfo.InvariantCulture) : 0f;
            bool useMain = opt.TryGetValue("cam", out var cs) && cs == "main";
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", Out));
            Directory.CreateDirectory(folder);

            Camera cam; RenderTexture rt = null; GameObject temp = null; string pose;
            var cap = Type.GetType("Oheangbu.EditorTools.Vol2.Vol2VfxCapture, Oheangbu.Vol2.Editor");
            const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
            object look = null; Vector3? realmEye = null;
            if (!useMain)
            {
                var capCam = cap?.GetField("_cam", S)?.GetValue(null) as Camera;
                var ctx = cap?.GetField("_ctx", S)?.GetValue(null);
                if (capCam == null || ctx == null) return "REFUSED fogdiag shoot: no Vol2VfxCapture camera / job (start a vfx9a / free run first, or cam=main)";
                var origin = (Vector3)ctx.GetType().GetField("Origin").GetValue(ctx); var fwd = (Vector3)ctx.GetType().GetField("Forward").GetValue(ctx);
                look = cap.GetField("_lookDriver", S)?.GetValue(null); realmEye = cap.GetField("_realmEye", S)?.GetValue(null) as Vector3?;
                temp = new GameObject("FogDiag308_Cam") { hideFlags = HideFlags.HideAndDontSave };
                cam = temp.AddComponent<Camera>(); cam.CopyFrom(capCam); cam.enabled = false;
                var src = capCam.GetComponent<UniversalAdditionalCameraData>(); var dst = temp.GetComponent<UniversalAdditionalCameraData>() ?? temp.AddComponent<UniversalAdditionalCameraData>();
                if (src != null) EditorUtility.CopySerialized(src, dst);
                dst.cameraStack?.Clear();
                float yaw = Quaternion.LookRotation(fwd.sqrMagnitude > 1e-6f ? fwd : Vector3.forward, Vector3.up).eulerAngles.y + yawOff;
                var rot = Quaternion.Euler(pitch, yaw, 0f); var pivot = origin + Vector3.up * 1.7f;
                cam.transform.SetPositionAndRotation(pivot + rot * new Vector3(0.7f, 0.55f, -2.8f), rot); cam.fieldOfView = 60f;
                pose = "capture shoulder pitch " + pitch + " yaw " + yaw.ToString("0") + " at " + cam.transform.position.ToString("F1");
            }
            else
            {
                cam = Camera.main; if (cam == null) return "REFUSED fogdiag shoot: no Camera.main";
                pose = "Camera.main at " + cam.transform.position.ToString("F1") + " rot " + cam.transform.eulerAngles.ToString("F0");
            }
            rt = new RenderTexture(1600, 900, 24);
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var prevTarget = cam.targetTexture;
            var sb = new StringBuilder("fogdiag shoot " + label + " | " + pose + "\n");
            int afterLayer = LayerMask.NameToLayer("VfxAfterFog");
            try
            {
                void Render(string tag)
                {
                    if (realmEye.HasValue && look is Oheangbu.App.World.LookDriver308 ld) ld.Refresh(realmEye.Value);
                    cam.targetTexture = rt; cam.Render();
                    var was = RenderTexture.active;
                    try { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); } finally { RenderTexture.active = was; }
                    string file = Path.Combine(folder, label + "_" + tag + ".png");
                    File.WriteAllBytes(file, tex.EncodeToPNG()); sb.AppendLine("  " + file);
                }
                Render("a_now");
                WithFeaturesOff(FogFeatures, () => Render("b_nomist"), sb);
                // c: every transparent Default-layer renderer onto the after-fog layer for this render only
                var moved = new List<(GameObject go, int layer)>();
                foreach (var r in Live()) if (r.gameObject.layer == 0 && Transparent(r)) { moved.Add((r.gameObject, 0)); r.gameObject.layer = afterLayer; }
                try { Render("c_routed"); } finally { foreach (var m in moved) if (m.go != null) m.go.layer = m.layer; }
                sb.AppendLine("  c_routed moved " + moved.Count + " renderer objects: " + string.Join(", ", moved.Select(m => m.go != null ? m.go.transform.root.name + "/" + m.go.name : "?").Distinct().Take(30)));
                WithFeaturesOff(AfterFogFeatures, () => Render("d_noafterfog"), sb);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                if (temp != null) Object.DestroyImmediate(temp);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            return sb.ToString();
        }

        /// <summary>target:name=Dummy_10m[:dist=6][:eye=0.3][:yaw=0][:label=x][:variants=now,InkWash297,CompactMist238,SSAO297,nopp,aa]: a copy of Camera.main placed
        /// dist metres from the named object (eye = height above the object's bounds centre, so the horizon cuts it), looking at it; one render per variant
        /// (now = as drawn, a feature name = that feature off, nopp = post-processing off, aa = antialiasing off). 4x crops of the object are written too.</summary>
        static string Target(Dictionary<string, string> opt)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string name = opt.TryGetValue("name", out var n) ? n : "Dummy_10m";
            string label = opt.TryGetValue("label", out var l) && l.Length > 0 ? l : "target_" + name;
            float dist = opt.TryGetValue("dist", out var ds) ? float.Parse(ds, inv) : 6f, eye = opt.TryGetValue("eye", out var es) ? float.Parse(es, inv) : 0.3f, yaw = opt.TryGetValue("yaw", out var ys) ? float.Parse(ys, inv) : 0f;
            var variants = (opt.TryGetValue("variants", out var vs) ? vs : "now,InkWash297,CompactMist238,SSAO297,nopp").Split(',').Where(s => s.Length > 0).ToArray();
            var go = GameObject.Find(name); if (go == null) return "REFUSED fogdiag target: no object " + name;
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return "REFUSED fogdiag target: no renderer under " + name;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            var main = Camera.main; if (main == null) return "REFUSED fogdiag target: no Camera.main";
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", Out)); Directory.CreateDirectory(folder);
            var temp = new GameObject("FogDiag308_TargetCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = temp.AddComponent<Camera>(); cam.CopyFrom(main); cam.enabled = false;
            var src = main.GetComponent<UniversalAdditionalCameraData>(); var dst = temp.GetComponent<UniversalAdditionalCameraData>() ?? temp.AddComponent<UniversalAdditionalCameraData>();
            if (src != null) EditorUtility.CopySerialized(src, dst); dst.cameraStack?.Clear();
            Vector3 toCam = Quaternion.Euler(0f, yaw, 0f) * (main.transform.position - b.center); toCam.y = 0f; if (toCam.sqrMagnitude < 1e-4f) toCam = -Vector3.forward;
            Vector3 pos = b.center + toCam.normalized * dist + Vector3.up * eye;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(b.center.x - pos.x, 0f, b.center.z - pos.z), Vector3.up)); cam.fieldOfView = 40f;
            var rt = new RenderTexture(1600, 900, 24); var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            var sb = new StringBuilder("fogdiag target " + name + " bounds " + b.center.ToString("F2") + " size " + b.size.ToString("F2") + " | cam " + pos.ToString("F2") + " (level, horizon at eye height)\n");
            Vector3 sp = cam.WorldToScreenPoint(b.center); // crop around the object
            try
            {
                foreach (var v in variants)
                {
                    void Render()
                    {
                        cam.targetTexture = rt; cam.Render();
                        var was = RenderTexture.active;
                        try { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); } finally { RenderTexture.active = was; }
                        string file = Path.Combine(folder, label + "_" + v + ".png"); File.WriteAllBytes(file, tex.EncodeToPNG()); sb.AppendLine("  " + file);
                    }
                    if (v == "now") Render();
                    else if (v == "nopp") { bool pp = dst.renderPostProcessing; dst.renderPostProcessing = false; try { Render(); } finally { dst.renderPostProcessing = pp; } }
                    else if (v == "nodry")
                    {   // InkWash297 v5 갈필 (silhouettes against the sky break up): the look driver's global, w = DryBrush; zero for this render only
                        int id = Shader.PropertyToID("_OhLook308Paper"); var was = Shader.GetGlobalVector(id);
                        sb.AppendLine("  _OhLook308Paper = " + was.ToString("F3") + " _OhLook308Dry = " + Shader.GetGlobalVector("_OhLook308Dry").ToString("F3"));
                        Shader.SetGlobalVector(id, new Vector4(was.x, was.y, was.z, 0f)); try { Render(); } finally { Shader.SetGlobalVector(id, was); }
                    }
                    else if (v.StartsWith("farbox", StringComparison.Ordinal))
                    {   // farbox<metres>: a temporary opaque "ridge" (lit box, 120 x 40 m) that far along the view, half above the horizon: is the 갈필 still on far silhouettes?
                        float far = v.Length > 6 ? float.Parse(v.Substring(6), inv) : 600f;
                        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.hideFlags = HideFlags.HideAndDontSave; Object.DestroyImmediate(box.GetComponent<Collider>());
                        Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
                        box.transform.SetPositionAndRotation(cam.transform.position + fwd * far + Vector3.right * Vector3.Dot(Vector3.right, Vector3.zero) + Vector3.down * 5f, Quaternion.LookRotation(fwd, Vector3.up));
                        box.transform.localScale = new Vector3(far * .2f, far * .07f, 10f);
                        int id = Shader.PropertyToID("_OhLook308Paper"); var was = Shader.GetGlobalVector(id);
                        try
                        {
                            var shots = new List<int[]>();
                            foreach (var dry in new[] { true, false })
                            {
                                if (!dry) Shader.SetGlobalVector(id, new Vector4(was.x, was.y, was.z, 0f));
                                cam.targetTexture = rt; cam.Render();
                                var keep = RenderTexture.active; try { RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); } finally { RenderTexture.active = keep; }
                                File.WriteAllBytes(Path.Combine(folder, label + "_" + v + (dry ? "_now" : "_nodry") + ".png"), tex.EncodeToPNG());
                                shots.Add(tex.GetPixels32().Select(c => c.r + c.g * 256 + c.b * 65536).ToArray());
                                Shader.SetGlobalVector(id, was);
                            }
                            int diff = 0; for (int k = 0; k < shots[0].Length; k++) if (shots[0][k] != shots[1][k]) diff++;
                            sb.AppendLine("  " + v + ": box at " + far + " m | pixels where 갈필 changed the picture (now vs DryBrush 0): " + diff + " -> " + label + "_" + v + "_now/_nodry.png");
                        }
                        finally { Shader.SetGlobalVector(id, was); Object.DestroyImmediate(box); }
                    }
                    else if (v == "occl20" || v == "occl0")
                    {   // occlusion test: a bright transparent quad 1 m BEHIND the object (on the after-fog layer / on Default); if the object's centre turns magenta, the pass does not depth-test
                        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.hideFlags = HideFlags.HideAndDontSave; Object.DestroyImmediate(q.GetComponent<Collider>());
                        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
                        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent"); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000; m.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
                        q.GetComponent<MeshRenderer>().sharedMaterial = m; q.layer = v == "occl20" ? LayerMask.NameToLayer("VfxAfterFog") : 0;
                        Vector3 away = (b.center - cam.transform.position); away.y = 0f; away.Normalize();
                        q.transform.SetPositionAndRotation(b.center + away * (b.extents.magnitude + 1f), Quaternion.LookRotation(away, Vector3.up)); q.transform.localScale = new Vector3(4f, 4f, 1f);
                        try
                        {
                            Render();
                            var vp = cam.WorldToViewportPoint(b.center); int cx = (int)(vp.x * tex.width), cy = (int)(vp.y * tex.height);
                            int magenta = 0, n2 = 0;
                            for (int dy = -20; dy <= 20; dy += 4) for (int dx = -10; dx <= 10; dx += 2) { var c = tex.GetPixel(cx + dx, cy + dy); n2++; if (c.r > .6f && c.b > .6f && c.g < .35f) magenta++; }
                            var edge = tex.GetPixel(cx + (int)(tex.width * .08f), cy);
                            sb.AppendLine("  " + v + ": quad layer " + q.layer + " | object centre samples magenta " + magenta + "/" + n2 + " | a sample beside the object " + edge);
                        }
                        finally { Object.DestroyImmediate(q); Object.DestroyImmediate(m); }
                    }
                    else if (v == "aa") { var aa = dst.antialiasing; dst.antialiasing = AntialiasingMode.None; try { Render(); } finally { dst.antialiasing = aa; } }
                    else WithFeaturesOff(new[] { v }, Render, sb);
                }
                sb.AppendLine("  object centre on screen (px from bottom-left of 1600x900): " + sp.ToString("F0") + "; aa=" + dst.antialiasing + " post=" + dst.renderPostProcessing + " msaa=" + cam.allowMSAA + " renderScale=" + (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u ? u.renderScale.ToString(inv) + " msaaSamples " + u.msaaSampleCount + " hdr " + u.supportsHDR : "?"));
            }
            finally { Object.DestroyImmediate(temp); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            return sb.ToString();
        }

        static void WithFeaturesOff(string[] names, Action render, StringBuilder sb)
        {
            var feats = Features(names); var dirty = feats.Select(f => EditorUtility.IsDirty(f)).ToArray();
            try { foreach (var f in feats) f.SetActive(false); render(); }
            finally { for (int i = 0; i < feats.Count; i++) { feats[i].SetActive(true); if (!dirty[i]) EditorUtility.ClearDirty(feats[i]); } }
            sb.AppendLine("    (off: " + string.Join(",", feats.Select(f => f.name)) + ")");
        }
    }
}
