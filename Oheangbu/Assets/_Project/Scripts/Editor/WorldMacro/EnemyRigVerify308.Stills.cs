using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 SPEC-ENEMY-RIG-VERIFY-308: studio stills of the posed prefab copy through the #298 still (CompactFolklore298.RenderPose298:
    // CPU-baked pose, fixed orthographic camera, 1024 px). The pose is the measured one: PlayableGraph sample + ApplyArmRelax.
    // This renders: the operator runs it through `python Tools/resource_guard.py --gpu-run enemyrig-stills -- ...` (one GPU job at a time).
    public static partial class EnemyRigVerify308
    {
        [Serializable] sealed class StillRow { public string file = "", role = "", clip = "", relax = ""; public float phase, yaw, coverage; }
        [Serializable] sealed class StillLog
        {
            public string id = "", utc = "", clipMode = "", relax = "", quality = "", avatar = "", ground = "", thinRoles = ""; public int warmupRenders, rerendered; public float coverageMin, coverageMedian;
            public List<StillRow> rows = new List<StillRow>();
        }

        // Share of the upper part of the picture (stills.coverageTopShare) that is not background (the top corner pixel): a first render
        // of a session can come back without the body. The lower part is left out because the ground slab is always there.
        static float Coverage(string png, StillSettings set)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(png), false)) return 0;
                var px = texture.GetPixels32(); int w = texture.width, h = texture.height; if (px.Length == 0 || w * h != px.Length) return 0;
                var bg = px[px.Length - 1]; int first = Mathf.FloorToInt(h * (1f - Mathf.Clamp01(set.coverageTopShare))) * w, n = 0, seen = 0, stride = Mathf.Max(1, set.coverageStride);
                for (int i = first; i < px.Length; i += stride) { seen++; if (Mathf.Abs(px[i].r - bg.r) + Mathf.Abs(px[i].g - bg.g) + Mathf.Abs(px[i].b - bg.b) > set.coverageColourDelta) n++; }
                return seen > 0 ? n / (float)seen : 0;
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // The prefab's ground (y = 0) as a slab under the figure plus a bright line across the body's own depth: the camera looks down
        // 8.5 degrees, so a sole below the ground is hidden by the slab and the line is the true ground height at the body's centre.
        static GameObject GroundPart(PreviewRenderUtility preview, string name, Vector3 size, Vector3 centre, Color colour, List<Object> owned)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags(Marker + name, HideFlags.HideAndDontSave, typeof(MeshFilter), typeof(MeshRenderer));
            preview.AddSingleGO(go);
            if (go.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) { Object.DestroyImmediate(go); throw new InvalidOperationException("still ground stayed in the open scene (destroyed; nothing rendered)"); }
            float x = size.x * .5f, z = size.z * .5f, y = size.y;
            var mesh = new Mesh { name = Marker + name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z), new Vector3(-x, -y, -z), new Vector3(x, -y, -z), new Vector3(x, -y, z), new Vector3(-x, -y, z) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? throw new InvalidOperationException("no unlit shader for the still ground");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, color = colour };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);   // both faces: the slab must hide what is under the ground from any yaw
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material; go.layer = 0; go.transform.position = centre;
            owned.Add(mesh); owned.Add(material); owned.Add(go);
            return go;
        }

        static string Stills(Config cfg, string id, Dictionary<string, string> options)
        {
            if (string.IsNullOrEmpty(id)) return "REFUSED stills needs one id";
            var picked = Pick(cfg, id, out string why); if (picked == null) return "REFUSED " + why;
            if (picked.Length != 1) return "REFUSED stills takes one id per call";
            var m = picked[0];
            string clipMode = options.TryGetValue("clip", out string c) ? c : "orig"; if (clipMode != "orig" && clipMode != "fixed") return "REFUSED clip takes orig or fixed";
            string relax = options.TryGetValue("relax", out string r) ? r : clipMode == "fixed" ? "profile" : "legacy";
            bool explicitRelax = ExplicitRelax(relax, out float relaxArm, out float relaxElbow);
            if (relax != "legacy" && relax != "profile" && relax != "off" && relax != "B" && !explicitRelax) return "REFUSED relax takes legacy, profile, off, B or <arm>,<elbow> (0..1 each)";
            // rev 2: the picture is labelled with the avatar that is imported NOW; asking for the other one refuses instead of mislabelling
            string avatarNow = AvatarState(cfg, m, out string avatarNote), avatarWant = options.TryGetValue("avatar", out string av) ? av : avatarNow;
            if (avatarWant != "live" && avatarWant != "A") return "REFUSED avatar takes live or A (the avatar imported now reads '" + avatarNow + "': " + avatarNote + ")";
            if (avatarWant != avatarNow) return "REFUSED avatar=" + avatarWant + " but the imported avatar of " + m.id + " is '" + avatarNow + "' (" + avatarNote + "); " + (avatarWant == "A" ? "avatar-apply:" + m.id + " first" : "avatar-revert:" + m.id + " first");
            // review F3: a clip whose own copy of the reference rows is stale is converted with the other pose - its picture would carry the wrong label
            var staleRows = StaleClipRows(m, cfg.avatar.rowTolerance, out _);
            if (staleRows.Count > 0) return "REFUSED clip rows STALE (" + string.Join("; ", staleRows) + "): avatar-sync:" + m.id + " first";
            string which = options.TryGetValue("set", out string s) ? s : "all"; if (which != "walk" && which != "idle" && which != "all" && which != "others") return "REFUSED set takes walk, idle, all or others";
            if (which == "others" && cfg.stills.otherPhases.Length == 0) return "REFUSED config stills.otherPhases is empty";
            if (cfg.stills.yaws.Length == 0 || cfg.stills.walkPhases.Length == 0 || cfg.stills.idlePhases.Length == 0) return "REFUSED config stills block is empty";
            var row = CompactFolklore298.ReadManifest().rows.FirstOrDefault(x => x.id == m.id); if (row == null) return "REFUSED no manifest row " + m.id;
            var source = OnlyClip(m.walkSource); if (source == null) return "REFUSED walk source clip not found in " + m.walkSource;
            var walk = clipMode == "fixed" ? FixedClip(m) : source; if (walk == null) return "REFUSED repaired clip not imported (clip-check)";
            var profile = Profile(relax == "B" ? m.relaxProfileB : clipMode == "fixed" ? m.relaxProfile : m.relaxProfileOrigClip); if ((relax == "profile" || relax == "B") && profile == null) return "REFUSED relax profile asset missing";
            if (relax == "B" && clipMode != "fixed") return "REFUSED relax=B is the profile of the repaired clip (clip=fixed)";
            var idle = CompactFolklore298.Clip(row, "idle");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompactFolklore298.PrefabPath(m.id)); if (prefab == null) return "REFUSED no prefab " + CompactFolklore298.PrefabPath(m.id);
            string relaxTag = explicitRelax ? "r" + Mathf.RoundToInt(relaxArm * 100).ToString("000", CultureInfo.InvariantCulture) + "-" + Mathf.RoundToInt(relaxElbow * 100).ToString("000", CultureInfo.InvariantCulture) : relax;
            string folder = Path.Combine(Out, "Stills", m.id, clipMode + "_" + relaxTag + (avatarNow == "A" ? "_avA" : "")); Directory.CreateDirectory(folder);
            var log = new StillLog { id = m.id, utc = DateTime.UtcNow.ToString("O"), clipMode = clipMode, relax = relax, quality = QualitySettings.names[QualitySettings.GetQualityLevel()], avatar = avatarNow,
                ground = "slab " + F(cfg.stills.groundSize) + " m at y = 0 (prefab ground) + line at the body's centre depth" };
            var preview = new PreviewRenderUtility(false, true); var owned = new List<Object>(); bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            var thinRoles = new List<string>(); var runForOthers = which == "others" ? RunClip(m) : null;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;   // a still must not be taken with a shader that is still compiling
                var host = EditorUtility.CreateGameObjectWithHideFlags(Marker + "still_" + m.id, HideFlags.HideAndDontSave);
                preview.AddSingleGO(host);
                if (host.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene()) { Object.DestroyImmediate(host); return "FAILED still host stayed in the open scene (destroyed; nothing rendered)"; }
                var visual = Object.Instantiate(prefab, host.transform); visual.name = "Folklore298_Visual";
                visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
                foreach (var t in host.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; t.gameObject.hideFlags = HideFlags.HideAndDontSave; }
                foreach (var skin in host.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.updateWhenOffscreen = false;
                var animator = visual.GetComponentInChildren<Animator>(true); if (animator == null || !animator.isHuman) return "FAILED prefab has no humanoid Animator";
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false; animator.runtimeAnimatorController = null; animator.fireEvents = false;
                var rig = host.AddComponent<EnemyRigMotion298>(); rig.enabled = false; rig.Animator = animator;
                CompactFolklore298.SamplePoseGraph298(animator, idle, 0);
                var frame = CompactFolklore298.MeshBoundsVerify308(host);   // one fixed frame for every tile: no automatic re-framing
                // ground AFTER the frame is taken (it must not widen the frame); the line turns with the camera so it always crosses the picture
                float ground = Mathf.Max(.5f, cfg.stills.groundSize);
                GroundPart(preview, "still_ground", new Vector3(ground, .02f, ground), Vector3.zero, new Color(.30f, .27f, .22f, 1), owned);
                var line = GroundPart(preview, "still_groundline", new Vector3(ground * 1.4f, .004f, Mathf.Max(.002f, cfg.stills.groundLineWidth)), new Vector3(0, .002f, 0), new Color(1f, .85f, .25f, 1), owned);
                var coverage = new List<float>(); var byRole = new Dictionary<string, List<float>>();
                void Render(float yaw, string file) { line.transform.rotation = Quaternion.Euler(0, yaw, 0); CompactFolklore298.RenderPoseVerify308(preview, host, frame, yaw, file); }
                // warm-up: the first render of a session is thrown away (measured: it can miss the body)
                CompactFolklore298.SamplePoseGraph298(animator, idle, 0);
                string warm = Path.Combine(folder, "_warmup.png");
                for (int i = 0; i < Mathf.Max(0, cfg.stills.warmupRenders); i++) { Render(cfg.stills.yaws[0], warm); log.warmupRenders++; }
                if (File.Exists(warm)) File.Delete(warm);
                void Shoot(string role, AnimationClip clip, float[] phases)
                {
                    if (!byRole.TryGetValue(role, out var mine)) byRole[role] = mine = new List<float>();
                    foreach (float phase in phases)
                    {
                        CompactFolklore298.SamplePoseGraph298(animator, clip, Mathf.Clamp01(phase) * clip.length);
                        if (role == "walk" || role == "idle") Relax(rig, relax, walk, profile, role == "idle");   // the game relaxes idle and walk only
                        foreach (float yaw in cfg.stills.yaws)
                        {
                            string name = role + "_p" + Mathf.RoundToInt(phase * 1000).ToString("000", CultureInfo.InvariantCulture) + "_y" + Mathf.RoundToInt(yaw).ToString("000", CultureInfo.InvariantCulture) + ".png";
                            string file = Path.Combine(folder, name); Render(yaw, file); float cover = Coverage(file, cfg.stills);
                            // a picture with far less figure than the others OF ITS OWN ROLE is rendered again, never kept silently (a body
                            // lying dead covers less than a standing one: roles are not compared with each other). The first picture of
                            // a role has nothing to be compared with; the check after the loop catches it.
                            float typical = mine.Count > 0 ? mine.OrderBy(v => v).ElementAt(mine.Count / 2) : 0;
                            for (int retry = 0; retry < Mathf.Max(0, cfg.stills.retries) && typical > 0 && cover < typical * cfg.stills.minCoverageRatio; retry++) { Render(yaw, file); cover = Coverage(file, cfg.stills); log.rerendered++; }
                            mine.Add(cover); coverage.Add(cover);
                            log.rows.Add(new StillRow { file = name, role = role, clip = clip.name, relax = relax, phase = phase, yaw = yaw, coverage = cover });
                        }
                    }
                }
                if (which == "others")
                {
                    foreach (string role in new[] { "attack", "hit", "stun" }) Shoot(role, CompactFolklore298.Clip(row, role), cfg.stills.otherPhases);
                    Shoot("death", ClipIn(m.motionSource, m.deathTake) ?? CompactFolklore298.Clip(row, "death"), cfg.stills.otherPhases);
                    // review F4: the run clip of the run-clip stage when that file is imported (candidate A re-converts it too)
                    if (runForOthers != null) Shoot("run", runForOthers, cfg.stills.walkPhases);
                }
                else
                {
                    if (which != "idle") Shoot("walk", walk, cfg.stills.walkPhases);
                    if (which != "walk") Shoot("idle", idle, cfg.stills.idlePhases);
                }
                if (coverage.Count > 0) { var sorted = coverage.OrderBy(v => v).ToList(); log.coverageMin = sorted[0]; log.coverageMedian = sorted[sorted.Count / 2]; }
                foreach (var kv in byRole)
                {
                    if (kv.Value.Count < 2) continue; var sorted = kv.Value.OrderBy(v => v).ToList();
                    if (sorted[0] < sorted[sorted.Count / 2] * cfg.stills.minCoverageRatio) thinRoles.Add(kv.Key);
                }
                log.thinRoles = string.Join(",", thinRoles);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                foreach (var o in owned) if (o != null) Object.DestroyImmediate(o);
                preview.Cleanup();
            }
            File.WriteAllText(Path.Combine(folder, "stills.json"), JsonUtility.ToJson(log, true), new UTF8Encoding(false));
            return "stills " + m.id + " clip=" + clipMode + " relax=" + relax + " avatar=" + avatarNow + ": " + log.rows.Count + " PNG -> " + folder + " | coverage min " + F1(log.coverageMin * 100) + " % median " + F1(log.coverageMedian * 100) +
                " % | warm-up " + log.warmupRenders + ", re-rendered " + log.rerendered + (thinRoles.Count > 0 ? " | WARN a picture has far less figure than the others of its role (" + string.Join(", ", thinRoles) + "): look at it before making sheets" : "") +
                (which != "others" ? "" : runForOthers != null ? " | run clip included" : string.IsNullOrEmpty(m.runClip) ? "" : " | run clip NOT imported: not photographed") +
                " (sheets: python Tools/Unity/Stage308_enemyrig2/_Tools/sheets_enemyrig2.py)";
        }
    }
}
