using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #300 before/after takes: flip one serialized field of a data asset (bool, int, float) and read it back.
    //   so-get:<asset path>:<field>
    //   so-set:<asset path>:<field>:<value>   — the caller restores the prior value after the take
    public static partial class Presentation297
    {
        static SerializedProperty Field(string path, string field, out SerializedObject so)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null) throw new Exception("so: no ScriptableObject at " + path);
            so = new SerializedObject(asset);
            var prop = so.FindProperty(field);
            if (prop == null) throw new Exception("so: no field " + field + " on " + path);
            return prop;
        }

        static string Value(SerializedProperty p) => p.propertyType switch
        {
            SerializedPropertyType.Boolean => p.boolValue.ToString(),
            SerializedPropertyType.Integer => p.intValue.ToString(CultureInfo.InvariantCulture),
            SerializedPropertyType.Float => p.floatValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new Exception("so: unsupported field type " + p.propertyType)
        };

        static string SoGet(string[] a) => Value(Field(a[1], a[2], out _));

        // renderers:<name substring> — every renderer under matching active objects: enabled, layer, material/shader,
        // visibility and bounds, plus the gameplay camera's culling mask (debugging presentation objects in Play)
        static string Renderers(string[] a)
        {
            string needle = a[1].ToLowerInvariant();
            var sb = new System.Text.StringBuilder();
            var cam = Camera.main;
            if (cam != null) sb.AppendLine("main camera mask " + cam.cullingMask + " at " + cam.transform.position.ToString("F2"));
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(false))
                {
                    if (!t.name.ToLowerInvariant().Contains(needle)) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    {
                        var m = r.sharedMaterial;
                        string extra = r is ParticleSystemRenderer ? " particles=" + r.GetComponent<ParticleSystem>().particleCount : r is TrailRenderer tr ? " points=" + tr.positionCount : "";
                        sb.AppendLine(r.name + " enabled=" + r.enabled + " active=" + r.gameObject.activeInHierarchy + " layer=" + r.gameObject.layer + " visible=" + r.isVisible
                            + " mat=" + (m != null ? m.name + "/" + m.shader.name : "null") + " bounds " + r.bounds.center.ToString("F2") + " " + r.bounds.size.ToString("F2") + extra);
                    }
                    break;
                }
            return sb.ToString();
        }

        // cameras — every camera: enabled, type, stack, mask. maincap:<name> — render the live gameplay camera itself
        static string Cameras()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var d = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(c);
                sb.AppendLine(c.name + " enabled=" + c.isActiveAndEnabled + " type=" + d.renderType + " stack=" + (d.cameraStack != null ? string.Join(",", d.cameraStack.ConvertAll(x => x != null ? x.name : "null")) : "-")
                    + " mask=" + c.cullingMask + " depth=" + c.depth + " renderer=" + d.scriptableRenderer?.GetType().Name + " tag=" + c.tag + " scene=" + c.scene.name);
            }
            return sb.ToString();
        }

        static string MainCapture(string[] a)
        {
            var cam = MainCamera();
            var rt = new RenderTexture(Screen.width > 0 ? 1280 : 1280, 720, 24);
            var prior = cam.targetTexture;
            try
            {
                cam.targetTexture = rt; cam.Render();
                var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                var active = RenderTexture.active; RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); img.Apply(); RenderTexture.active = active;
                string file = System.IO.Path.Combine(Root, "Teaser300", a[1] + ".png");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                System.IO.File.WriteAllBytes(file, img.EncodeToPNG()); Object.DestroyImmediate(img);
                return file;
            }
            finally { cam.targetTexture = prior; rt.Release(); Object.DestroyImmediate(rt); }
        }

        // weakpoint:<actor id> — Play test hook: open that actor's weak point (as a full groggy meter would)
        // guardian-lock — diagnose/force the lock on the giant target
        static string GuardianLock()
        {
            var v = Object.FindFirstObjectByType<Oheangbu.App.World.Guardian302Actor>()?.GetComponentInChildren<Oheangbu.Combat.EnemyVitals>();
            var sb = new System.Text.StringBuilder(); var cam = Camera.main;
            sb.Append("vitals=" + (v != null) + " alive=" + (v != null && v.IsAlive) + " cam=" + (cam != null ? cam.name + " " + cam.transform.position.ToString("F1") : "none"));
            if (v != null && cam != null) sb.Append(" dist=" + Vector3.Distance(cam.transform.position, v.transform.position).ToString("F1") + " vp=" + cam.WorldToViewportPoint(v.transform.position).ToString("F2"));
            foreach (var l in Object.FindObjectsByType<Oheangbu.Combat.LockOn>(FindObjectsSortMode.None))
            {
                if (v != null) l.SetCandidates(new[] { v });
                if (!l.IsLocked) l.Toggle();
                sb.Append(" | lockOn " + l.name + " locked=" + l.IsLocked);
            }
            return sb.ToString();
        }

        // guardian-weak — open the giant's weak point (the 앞잡 window) for a take
        static string GuardianWeak()
        {
            var v = Object.FindFirstObjectByType<Oheangbu.App.World.Guardian302Actor>()?.GetComponentInChildren<Oheangbu.Combat.EnemyVitals>();
            if (v == null) throw new Exception("guardian-weak: spawn with :target first");
            v.Restore(); v.OpenWeakPoint(); return "guardian weakPoint=" + v.WeakPointActive + " hp=" + v.Hp.ToString("F0");
        }

        // floaters:<x,z>:<radius>[:fix] — tall renderers whose base hangs more than 1 m above what lies under it (e.g. trees
        // left at the old height after a pad levelled the terrain). "fix" drops them onto the ground for this Play session only.
        static string Floaters(string[] a)
        {
            var c = Vec2(a[1]); float r = float.Parse(a[2], CultureInfo.InvariantCulture); bool fix = a.Length > 3 && a[3] == "fix";
            var sb = new System.Text.StringBuilder(); int n = 0; var seen = new System.Collections.Generic.HashSet<Transform>();
            foreach (var rend in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!(rend is MeshRenderer) || !rend.enabled || !rend.gameObject.activeInHierarchy) continue;
                var b = rend.bounds; if (b.size.y < 2.5f || b.size.x > 40f || b.size.z > 40f) continue;
                if (new Vector2(b.center.x - c.x, b.center.z - c.y).magnitude > r) continue;
                var root = rend.transform; while (root.parent != null && root.parent.GetComponent<Renderer>() == null && root.parent.childCount < 8) root = root.parent;
                if (!seen.Add(root)) continue;
                var from = new Vector3(b.center.x, b.min.y - .05f, b.center.z);
                if (!Physics.Raycast(from, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) continue;
                float gap = from.y - hit.point.y;
                if (gap < 1f) continue;
                n++; if (n <= 40) sb.AppendLine($"{gap:F1} m  {root.name}  at {b.center.x:F0},{b.min.y:F1},{b.center.z:F0} over {hit.collider.name}");
            }
            int inst = 0;
            if (fix) foreach (var art in Object.FindObjectsByType<Oheangbu.App.World.CompactRebuildArtRenderer>(FindObjectsSortMode.None)) inst += art.DropFloaters(new Vector3(c.x, 0, c.y), r, 1f);
            return n + " floating" + (fix ? " (dropped for this session), instanced dropped " + inst : "") + "\n" + sb;
        }

        static Vector2 Vec2(string s) { var p = s.Split(','); return new Vector2(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture)); }

        // guardian:<x,y,z feet>:<yaw>:<scale>:<Move@t,Move@t...> — the #302 trailer giant, Play mode (replaces an earlier one)
        static string Guardian(string[] a)
        {
            if (!EditorApplication.isPlaying) throw new Exception("guardian: Play mode only");
            foreach (var old in Object.FindObjectsByType<Oheangbu.App.World.Guardian302Actor>(FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/Trailer302/PF_GeumgangGuardian302.prefab");
            if (prefab == null) throw new Exception("guardian: run Guardian302Build build first");
            var feet = Vec(a[1]); float yaw = float.Parse(a[2], CultureInfo.InvariantCulture), scale = float.Parse(a[3], CultureInfo.InvariantCulture);
            var go = Object.Instantiate(prefab, feet, Quaternion.Euler(0, yaw, 0)); go.transform.localScale = Vector3.one * scale;
            var actor = go.GetComponent<Oheangbu.App.World.Guardian302Actor>(); actor.Play(a.Length > 4 ? a[4] : "Idle@0");
            actor.GroundY = feet.y;   // grounded by the actor after its first evaluated frame
            if (a.Length > 5 && a[5] == "target")
            {
                // a lock-on / 앞잡 target at chest height (Riposte aims 1.2 m above the vitals); presentation only
                var t = new GameObject("Guardian302Target"); t.transform.SetParent(go.transform, false);
                t.transform.position = new Vector3(feet.x, feet.y + 5.2f * scale / 4.7f, feet.z);
                var vitals = t.AddComponent<Oheangbu.Combat.EnemyVitals>();
                foreach (var lockOn in Object.FindObjectsByType<Oheangbu.Combat.LockOn>(FindObjectsSortMode.None)) lockOn.SetCandidates(new[] { vitals });
            }
            var anim = go.GetComponentInChildren<Animator>(); var head = anim.GetBoneTransform(HumanBodyBones.Head);
            return "guardian at " + go.transform.position.ToString("F1") + " head " + (head != null ? head.position.y - feet.y : 0f).ToString("F1");
        }

        static string WeakPoint(string[] a)
        {
            var s = Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroPlaytestSession>();
            var actor = s != null ? System.Linq.Enumerable.FirstOrDefault(s.Actors, x => x != null && x.Id == a[1]) : null;
            if (actor == null) throw new Exception("no actor " + a[1]);
            var v = actor.GetComponentInChildren<Oheangbu.Combat.EnemyVitals>(true) ?? actor.GetComponentInParent<Oheangbu.Combat.EnemyVitals>();
            if (v == null) throw new Exception("no EnemyVitals on " + a[1]);
            v.OpenWeakPoint();
            return a[1] + " weakPoint=" + v.WeakPointActive + " hp=" + v.Hp.ToString("F1") + " at " + v.transform.position.ToString("F1");
        }

        static string RiposteStatus()
        {
            var r = Object.FindFirstObjectByType<Oheangbu.App.World.Riposte301>();
            if (r == null) return "no Riposte301 component";
            var lockOn = r.Walker != null && r.Walker.Motor != null ? r.Walker.Motor.GetComponent<Oheangbu.Combat.LockOn>() : null;
            var target = lockOn != null ? lockOn.Target : null;
            return "profile=" + (r.Profile != null) + " volleys=" + r.Volleys + " active=" + r.Active + " suppress=" + r.SuppressDraw
                + " lock=" + (target != null ? target.name + " weak=" + target.WeakPointActive + " hp=" + target.Hp.ToString("F1") : "none")
                + " can=" + r.CanRiposte(out _)
                + (target != null && r.Walker != null ? " bodyDist=" + Vector3.Distance(target.transform.position, r.Walker.Body.transform.position).ToString("F1") + " canDraw=" + r.Walker.Motor.CanBeginDrawing + " seated=" + r.Walker.Seated : "");
        }

        static string SoSet(string[] a)
        {
            var p = Field(a[1], a[2], out var so);
            string prior = Value(p);
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: p.boolValue = a[3] == "1" || a[3].Equals("true", StringComparison.OrdinalIgnoreCase); break;
                case SerializedPropertyType.Integer: p.intValue = int.Parse(a[3], CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Float: p.floatValue = float.Parse(a[3], CultureInfo.InvariantCulture); break;
                default: throw new Exception("so: unsupported field type " + p.propertyType);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return a[2] + " " + prior + " -> " + Value(p);
        }
    }
}
