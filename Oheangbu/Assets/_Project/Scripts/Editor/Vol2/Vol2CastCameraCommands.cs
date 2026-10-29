using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.Core.Domain;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V12 / V13 cast camera treatments through the editor queue (V13: A softened is ON in every vol.2 scope; these switch it per Play):
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2CastCameraCommands Run "castcam:status"
    ///   ... "castcam:A" (the default) | "castcam:A0" or "castcam:off" (A off, for comparison) | "castcam:B" | "castcam:C" | "castcam:D" | "castcam:AB" ...   (A when Play starts)
    ///   ... "castcam:A12"                    A with the V12 numbers and shape (age envelope, 0.9 / 0.9 / -0.6 m, +20 deg, no boom) on the runtime copy - comparison only
    ///   ... "castcam:mode:auto|on|off"      where the director may exist (Auto = with every vol.2 scope)
    ///   ... "castcam:set:FrameStrength=0.8" | "castcam:set:FrameOffset=0.45,0.45,-0.3" | "castcam:set:FrameShape=Spring"   a field of the RUNTIME profile copy
    ///   ... "castcam:reload"                 read the profile asset again
    ///   ... "castcam:make-profile"           write the profile asset with the class defaults (edit mode, only if missing; never a scene)
    ///   ... "castcam:probe"                  the lab's camera rig and the caster renderers the fade would use (Play)
    ///   ... "castcam:cast:나" | "castcam:burst:나:5:0.65"   raise a letter on the scope's channel (LAB-SIMULATED, no drawing) - once / N times every s seconds
    ///   ... "castcam:wall:right|back|up|off"  a TEMPORARY box collider (runtime object, never saved) where the full A eye would go, for the boom test
    ///   ... "castcam:aim[:out.csv]"          the aim probe summary, and its samples written to Art/SpellVFX308/Vol2Vfx/camera_treatments/A_soft/aim_probe.csv
    ///   ... "castcam:diag:reset"             zero the director's counters
    /// Never saves a scene, never opens a dialog.</summary>
    public static class Vol2CastCameraCommands
    {
        const string ProfileAsset = "Assets/_Project/Art/SpellVFX308/Vol2Vfx/CastCamera/Resources/Vol2CastCamera/Vol2CastCameraProfile.asset";
        const string AimDefault = "Art/SpellVFX308/Vol2Vfx/camera_treatments/A_soft/aim_probe.csv";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static GameObject _wall;

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
                if (c == "diag:reset") { var d = Director(); if (d == null) return "REFUSED castcam:diag:reset: no director"; d.ResetDiagnostics(); return "reset " + Status(); }
                if (c.StartsWith("mode:", StringComparison.Ordinal))
                {
                    Vol2CastCameraDirector.Mode = (Vol2CastCameraMode)Enum.Parse(typeof(Vol2CastCameraMode), c.Substring(5), true);
                    if (EditorApplication.isPlaying) Vol2CastCameraDirector.Ensure();
                    return "mode " + Vol2CastCameraDirector.Mode + " " + Status();
                }
                if (c.StartsWith("set:", StringComparison.Ordinal)) return Set(c.Substring(4));
                if (c.StartsWith("cast:", StringComparison.Ordinal)) return Cast(c.Substring(5));
                if (c.StartsWith("burst:", StringComparison.Ordinal)) return Burst(c.Substring(6));
                if (c.StartsWith("wall:", StringComparison.Ordinal)) return Wall(c.Substring(5));
                if (c.StartsWith("drawmode:", StringComparison.Ordinal)) return DrawMode(c.Substring(9) == "1");
                if (c == "aim" || c.StartsWith("aim:", StringComparison.Ordinal)) return Aim(c.Length > 4 ? c.Substring(4) : AimDefault);
                if (c.Equals("A12", StringComparison.OrdinalIgnoreCase)) return V12();
                Vol2CastCameraDirector.Treatment = Vol2CastCameraMath.Parse(c);
                if (EditorApplication.isPlaying && Vol2CastCameraDirector.Treatment != Vol2CastCamTreatment.None) Vol2CastCameraDirector.Ensure();
                var dir = Director();
                if (dir != null && dir.Profile != null && dir.Profile.FrameShape == Vol2FrameShape.AgeEnvelope) dir.Reload();   // leaving A12: the soft numbers come back
                return "treatment " + Vol2CastCameraMath.Letters(Vol2CastCameraDirector.Treatment) + " " + Status();
            }
            catch (Exception e) { return "REFUSED castcam:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        static Vol2CastCameraDirector Director() => EditorApplication.isPlaying ? Object.FindFirstObjectByType<Vol2CastCameraDirector>() : null;

        static string F(float v, string fmt = "0.00") => v.ToString(fmt, Inv);

        static string Status()
        {
            string s = "treatment=" + Vol2CastCameraMath.Letters(Vol2CastCameraDirector.Treatment) + " mode=" + Vol2CastCameraDirector.Mode + " playing=" + EditorApplication.isPlaying
                + " lab=" + Vol2FreeShowcaseInstaller.LabLoaded() + " asset=" + (File.Exists(Path.GetFullPath(ProfileAsset)) ? "yes" : "no");
            var d = Director();
            if (d == null) return s + " director=none";
            var p = d.Profile;
            return s + " director coords=" + d.Coordinators + " casts=" + d.CastsSeen + " gamePosed=" + d.GamePosed + " capturePosed=" + d.CapturePosed + " faded=" + d.Faded
                + " restoreMismatch=" + d.RestoreMismatch + " restoredAtFrameStart=" + d.RestoredAtFrameStart + " restoredInUpdate=" + d.RestoredInUpdate
                + " refusedDrawing=" + d.RefusedDrawing + (d.LastRefusal != "" ? " (" + d.LastRefusal + ")" : "")
                + " | A shape=" + (p != null ? p.FrameShape.ToString() : "?") + " strength=" + (p != null ? F(p.FrameStrength) : "?") + " w=" + F(d.FrameWeightNow, "0.000") + " rate=" + F(d.FrameRate, "0.000")
                + " peakW=" + F(d.PeakWeight) + " peakRate=" + F(d.PeakRate) + "/s chained=" + d.ChainedCasts + " fresh=" + d.FreshSwings + " reversals=" + d.Reversals + " envelopeRestarts=" + d.EnvelopeRestarts
                + " peakEye=" + F(d.PeakEyeSpeed) + " m/s peakTurn=" + F(d.PeakTurnRate, "0.0") + " deg/s"
                + " | boom clamped=" + d.BoomClamped + " min=" + F(d.MinBoom, "0.000") + " last=" + F(d.LastBoom, "0.000") + " eyeInside=" + d.EyeInside + " wall=" + (_wall != null ? _wall.name : "none")
                + " | lastAge=" + F(d.LastAge) + " lastWeight=" + F(d.LastWeight) + " lastCover=" + F(d.LastCover) + " lastVis=" + F(d.LastVisibility) + " focus=" + d.LastFocus + " body=" + d.BodyRenderers
                + (d.GhostProblem != "" ? " GHOST " + d.GhostProblem : "") + " profile=" + (p != null ? p.name : "null");
        }

        static string Set(string kv)
        {
            var d = Director();
            if (d == null || d.Profile == null) return "REFUSED castcam:set: no director in Play (" + Status() + ")";
            int eq = kv.IndexOf('=');
            if (eq < 0) return "REFUSED castcam:set: Field=value";
            return SetField(d.Profile, kv.Substring(0, eq).Trim(), kv.Substring(eq + 1).Trim());
        }

        static string SetField(Vol2CastCameraProfile profile, string name, string v)
        {
            var f = typeof(Vol2CastCameraProfile).GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return "REFUSED castcam:set: no field " + name;
            object val;
            if (f.FieldType == typeof(float)) val = float.Parse(v, Inv);
            else if (f.FieldType == typeof(bool)) val = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            else if (f.FieldType == typeof(Vector3)) { var p = v.Split(','); val = new Vector3(float.Parse(p[0], Inv), float.Parse(p[1], Inv), float.Parse(p[2], Inv)); }
            else if (f.FieldType.IsEnum) val = Enum.Parse(f.FieldType, v, true);
            else return "REFUSED castcam:set: type " + f.FieldType.Name;
            f.SetValue(profile, val);
            return "set " + name + "=" + val + " (runtime copy only)";
        }

        /// <summary>A as V12 had it (the numbers of cast_camera_ledger.md section 2 and its age envelope, no boom) on the runtime copy: the "before" of the V13 comparison.</summary>
        static string V12()
        {
            if (!EditorApplication.isPlaying) return "REFUSED castcam:A12: Play only";
            Vol2CastCameraDirector.Treatment = Vol2CastCamTreatment.Frame;
            var d = Vol2CastCameraDirector.Ensure() ?? Director();
            if (d == null || d.Profile == null) return "REFUSED castcam:A12: no director (" + Status() + ")";
            d.Reload();
            string[] v12 = { "FrameShape=AgeEnvelope", "FrameStrength=1", "FrameIn=0.28", "FrameHoldUntil=1.1", "FrameOut=0.4", "FrameOffset=0.9,0.9,-0.6", "FramePitchAdd=20", "FramePitchMax=28",
                "FrameYawAdd=-10", "FrameFovAdd=0", "FrameBoom=0" };
            foreach (var kv in v12) { int eq = kv.IndexOf('='); string r = SetField(d.Profile, kv.Substring(0, eq), kv.Substring(eq + 1)); if (r.StartsWith("REFUSED", StringComparison.Ordinal)) return r; }
            return "treatment A12 (V12 numbers, age envelope, no boom) " + Status();
        }

        // ---- LAB-SIMULATED casts (the letter on the scope's channel, as Vol2VfxCapture.RaiseLetter does) ----

        static bool Raise(char letter, out string problem)
        {
            problem = "";
            var wiring = Object.FindFirstObjectByType<CombatLoopWiring>();
            if (wiring == null || wiring.LetterChannel == null) { problem = "no CombatLoopWiring / letter channel"; return false; }
            wiring.LetterChannel.Raise(new DrawnLetter(letter, default, default, null, 0.6f, 0.6f, 1f, 1f, 3));
            return true;
        }

        static string Cast(string s)
        {
            if (!EditorApplication.isPlaying || string.IsNullOrEmpty(s)) return "REFUSED castcam:cast: Play and a letter";
            if (!Raise(s[0], out var why)) return "REFUSED castcam:cast: " + why;
            return "raised " + s[0] + " " + Status();
        }

        static char _burstLetter; static int _burstLeft; static double _burstEvery, _burstNext;

        static string Burst(string s)
        {
            if (!EditorApplication.isPlaying) return "REFUSED castcam:burst: Play only";
            var p = s.Split(':');
            if (p.Length < 1 || p[0].Length == 0) return "REFUSED castcam:burst:<letter>[:n[:seconds]]";
            _burstLetter = p[0][0];
            _burstLeft = p.Length > 1 ? int.Parse(p[1], Inv) : 5;
            _burstEvery = p.Length > 2 ? double.Parse(p[2], Inv) : 0.65;
            _burstNext = EditorApplication.timeSinceStartup;
            EditorApplication.update -= BurstTick; EditorApplication.update += BurstTick;
            int n = _burstLeft;
            BurstTick();
            return "burst " + _burstLetter + " x" + n + " every " + _burstEvery.ToString("0.00", Inv) + " s started";
        }

        static void BurstTick()
        {
            if (!EditorApplication.isPlaying || _burstLeft <= 0) { EditorApplication.update -= BurstTick; _burstLeft = 0; return; }
            if (EditorApplication.timeSinceStartup < _burstNext) return;
            _burstNext += _burstEvery;
            _burstLeft--;
            Raise(_burstLetter, out _);
        }

        // ---- the boom test wall (a runtime object in the playing scene: never saved, gone when Play ends) ----

        static string Wall(string where)
        {
            if (!EditorApplication.isPlaying) return "REFUSED castcam:wall: Play only";
            if (_wall != null) { Object.Destroy(_wall); _wall = null; }
            if (where == "off") return "wall removed " + Status();
            var cam = Camera.main;
            if (cam == null) return "REFUSED castcam:wall: no main camera";
            var d = Director();
            var p = d != null ? d.Profile : null;
            Vector3 full = p != null ? p.FrameOffset * Mathf.Max(0f, p.FrameStrength) : new Vector3(.45f, .45f, -.3f);
            Quaternion yaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            Vector3 eye = cam.transform.position;
            // the face of the wall sits halfway along the A offset on that axis: an unbounded eye would end inside the 0.25 m sphere
            Vector3 center, size;
            switch (where)
            {
                case "right": center = eye + yaw * new Vector3(full.x * .5f + .25f + .1f, 0f, 0f); size = new Vector3(.2f, 4f, 6f); break;
                case "back": center = eye + yaw * new Vector3(0f, 0f, full.z * .5f - .25f - .1f); size = new Vector3(6f, 4f, .2f); break;
                case "up": center = eye + yaw * new Vector3(0f, full.y * .5f + .25f + .1f, 0f); size = new Vector3(6f, .2f, 6f); break;
                default: return "REFUSED castcam:wall: right | back | up | off";
            }
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Vol2CastCam_TestWall_" + where;
            _wall.hideFlags = HideFlags.DontSave;
            _wall.transform.SetPositionAndRotation(center, yaw);
            _wall.transform.localScale = size;
            Physics.SyncTransforms();
            return "wall " + where + " at " + center.ToString("F2") + " size " + size.ToString("F2") + " (camera " + eye.ToString("F2") + ", full A offset " + full.ToString("F2") + ") " + Status();
        }

        /// <summary>LAB-SIMULATED drawing: the rig's own draw-mode channel raised (the rig enters / leaves its draw close-up, as the drawing input makes it) -
        /// the director must refuse while it is on.</summary>
        static string DrawMode(bool on)
        {
            if (!EditorApplication.isPlaying) return "REFUSED castcam:drawmode: Play only";
            var rig = Object.FindFirstObjectByType<Oheangbu.Combat.CameraRigController>();
            var ch = rig != null ? typeof(Oheangbu.Combat.CameraRigController).GetField("_drawModeChanged", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rig) as Oheangbu.Core.Events.BoolEventChannelSO : null;
            if (ch == null) return "REFUSED castcam:drawmode: no rig / draw-mode channel";
            ch.Raise(on);
            return "draw mode " + (on ? "ON" : "off") + " (closeup=" + rig.IsDrawingCloseup + ") " + Status();
        }

        static string Aim(string rel)
        {
            var d = Director();
            if (d == null) return "REFUSED castcam:aim: no director";
            var samples = d.AimSamples();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../..", rel.Replace("..", "")));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder("frame,site,weight,sawPosed,atRigPose,rayX,rayY,rayZ,dirX,dirY,dirZ,posedX,posedY,posedZ,baseX,baseY,baseZ,sepFromPosed,devFromBase\n");
            string[] site = { "FixedUpdate", "Update", "LateUpdate" };
            foreach (var a in samples)
                sb.Append(string.Join(",", a.Frame.ToString(Inv), site[a.Site], F(a.Weight, "0.000"), a.SawPosed ? "1" : "0", a.AtRigPose ? "1" : "0",
                    F(a.RayOrigin.x, "0.0000"), F(a.RayOrigin.y, "0.0000"), F(a.RayOrigin.z, "0.0000"), F(a.RayDir.x, "0.0000"), F(a.RayDir.y, "0.0000"), F(a.RayDir.z, "0.0000"),
                    F(a.PosedOrigin.x, "0.0000"), F(a.PosedOrigin.y, "0.0000"), F(a.PosedOrigin.z, "0.0000"), F(a.BaseOrigin.x, "0.0000"), F(a.BaseOrigin.y, "0.0000"), F(a.BaseOrigin.z, "0.0000"),
                    F((a.RayOrigin - a.PosedOrigin).magnitude, "0.0000"), F((a.RayOrigin - a.BaseOrigin).magnitude, "0.0000"))).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return "aim " + d.AimSummary() + " | " + samples.Length + " samples -> " + path;
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
            var sb = new StringBuilder();
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
            var d = Director();
            if (d != null) sb.AppendLine("director parent=" + (d.transform.parent != null ? d.transform.parent.name : "-") + " aim " + d.AimSummary());
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
