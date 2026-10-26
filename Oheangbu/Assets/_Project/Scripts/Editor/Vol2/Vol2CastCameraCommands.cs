using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V12 cast camera treatments (LAB-ONLY prototype) through the editor queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2CastCameraCommands Run "castcam:status"
    ///   ... "castcam:off" | "castcam:A" | "castcam:B" | "castcam:C" | "castcam:D" | "castcam:AB" ...   the treatment (None when Play starts)
    ///   ... "castcam:mode:auto|on|off"      where the director may exist (Auto = the spell lab only)
    ///   ... "castcam:set:FrameOffset=0.9,0.9,-0.6" | "castcam:set:FadeMinVisibility=0.25"   a field of the director's RUNTIME profile copy
    ///   ... "castcam:reload"                 read the profile asset again
    ///   ... "castcam:make-profile"           write the profile asset with the class defaults (edit mode, only if missing; never a scene)
    ///   ... "castcam:probe"                  the lab's camera rig and the caster renderers the fade would use (Play)
    /// Never saves a scene, never opens a dialog.</summary>
    public static class Vol2CastCameraCommands
    {
        const string ProfileAsset = "Assets/_Project/Art/SpellVFX308/Vol2Vfx/CastCamera/Resources/Vol2CastCamera/Vol2CastCameraProfile.asset";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("castcam:", StringComparison.Ordinal)) c = c.Substring(8);
            try
            {
                if (c == "status") return Status();
                if (c == "probe") return Probe();
                if (c == "reload") { var d = Director(); if (d == null) return "REFUSED castcam:reload: no director (" + Status() + ")"; d.Reload(); return "reloaded " + Status(); }
                if (c == "make-profile") return MakeProfile();
                if (c.StartsWith("mode:", StringComparison.Ordinal))
                {
                    Vol2CastCameraDirector.Mode = (Vol2CastCameraMode)Enum.Parse(typeof(Vol2CastCameraMode), c.Substring(5), true);
                    if (EditorApplication.isPlaying) Vol2CastCameraDirector.Ensure();
                    return "mode " + Vol2CastCameraDirector.Mode + " " + Status();
                }
                if (c.StartsWith("set:", StringComparison.Ordinal)) return Set(c.Substring(4));
                Vol2CastCameraDirector.Treatment = Vol2CastCameraMath.Parse(c);
                if (EditorApplication.isPlaying && Vol2CastCameraDirector.Treatment != Vol2CastCamTreatment.None) Vol2CastCameraDirector.Ensure();
                return "treatment " + Vol2CastCameraMath.Letters(Vol2CastCameraDirector.Treatment) + " " + Status();
            }
            catch (Exception e) { return "REFUSED castcam:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        static Vol2CastCameraDirector Director() => EditorApplication.isPlaying ? Object.FindFirstObjectByType<Vol2CastCameraDirector>() : null;

        static string Status()
        {
            string s = "treatment=" + Vol2CastCameraMath.Letters(Vol2CastCameraDirector.Treatment) + " mode=" + Vol2CastCameraDirector.Mode + " playing=" + EditorApplication.isPlaying
                + " lab=" + Vol2FreeShowcaseInstaller.LabLoaded() + " asset=" + (File.Exists(Path.GetFullPath(ProfileAsset)) ? "yes" : "no");
            var d = Director();
            if (d == null) return s + " director=none";
            return s + " director coords=" + d.Coordinators + " casts=" + d.CastsSeen + " gamePosed=" + d.GamePosed + " capturePosed=" + d.CapturePosed + " faded=" + d.Faded
                + " restoreMismatch=" + d.RestoreMismatch + " refusedDrawing=" + d.RefusedDrawing + " lastAge=" + d.LastAge.ToString("0.00", Inv) + " lastWeight=" + d.LastWeight.ToString("0.00", Inv)
                + " lastCover=" + d.LastCover.ToString("0.00", Inv) + " lastVis=" + d.LastVisibility.ToString("0.00", Inv) + " focus=" + d.LastFocus + " body=" + d.BodyRenderers
                + (d.GhostProblem != "" ? " GHOST " + d.GhostProblem : "") + " profile=" + (d.Profile != null ? d.Profile.name : "null");
        }

        static string Set(string kv)
        {
            var d = Director();
            if (d == null || d.Profile == null) return "REFUSED castcam:set: no director in Play (" + Status() + ")";
            int eq = kv.IndexOf('=');
            if (eq < 0) return "REFUSED castcam:set: Field=value";
            string name = kv.Substring(0, eq).Trim(), v = kv.Substring(eq + 1).Trim();
            var f = typeof(Vol2CastCameraProfile).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return "REFUSED castcam:set: no field " + name;
            object val;
            if (f.FieldType == typeof(float)) val = float.Parse(v, Inv);
            else if (f.FieldType == typeof(bool)) val = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (f.FieldType == typeof(Vector3)) { var p = v.Split(','); val = new Vector3(float.Parse(p[0], Inv), float.Parse(p[1], Inv), float.Parse(p[2], Inv)); }
            else return "REFUSED castcam:set: type " + f.FieldType.Name;
            f.SetValue(d.Profile, val);
            return "set " + name + "=" + val + " (runtime copy only)";
        }

        static string MakeProfile()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED castcam:make-profile: edit mode only";
            if (AssetDatabase.LoadAssetAtPath<Vol2CastCameraProfile>(ProfileAsset) != null) return "exists " + ProfileAsset;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ProfileAsset)));
            var p = ScriptableObject.CreateInstance<Vol2CastCameraProfile>();
            AssetDatabase.CreateAsset(p, ProfileAsset);
            AssetDatabase.SaveAssetIfDirty(p);
            return "created " + ProfileAsset;
        }

        static string Probe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Status());
            var main = Camera.main;
            sb.AppendLine("Camera.main=" + (main != null ? main.name + " fov=" + main.fieldOfView.ToString("0.0", Inv) + " pos=" + main.transform.position.ToString("F2") + " local=" + main.transform.localPosition.ToString("F2") + " parent=" + (main.transform.parent != null ? main.transform.parent.name : "-") : "none"));
            var rig = Object.FindFirstObjectByType<Oheangbu.Combat.CameraRigController>();
            if (rig != null)
            {
                var cfg = typeof(Oheangbu.Combat.CameraRigController).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rig) as Oheangbu.Combat.CombatConfigSO;
                sb.AppendLine("CameraRigController on " + rig.name + " shoulder=" + rig.IsShoulder + " closeup=" + rig.IsDrawingCloseup + " config=" + (cfg != null ? cfg.name + " (" + AssetDatabase.GetAssetPath(cfg) + ") offset=" + cfg.ShoulderOffset.ToString("F2") : "null"));
            }
            else sb.AppendLine("no CameraRigController");
            var pres = Object.FindFirstObjectByType<Vol2VfxPresenter>();
            var anchor = pres != null && pres.Coordinator != null && pres.Coordinator.Caster != null ? pres.Coordinator.Caster.Anchor : null;
            sb.AppendLine("caster anchor=" + (anchor != null ? anchor.name + " at " + anchor.position.ToString("F2") : "none"));
            if (anchor != null)
                foreach (var r in anchor.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.sharedMaterial;
                    sb.AppendLine("  " + r.GetType().Name + " '" + r.name + "' en=" + r.enabled + " active=" + r.gameObject.activeInHierarchy + " shadows=" + r.shadowCastingMode + " mats=" + r.sharedMaterials.Length
                        + " shader=" + (m != null && m.shader != null ? m.shader.name : "-") + " baseMap=" + (m != null && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) + " bounds=" + r.bounds.size.ToString("F2"));
                }
            return sb.ToString();
        }
    }
}
