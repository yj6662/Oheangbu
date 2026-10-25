using System;
using System.Linq;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.Vol2
{
    /// <summary>D308-150 V10-V11 free showcase (나 / 고 / 각) through the editor queue:
    ///   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.Vol2.Vol2FreeShowcaseCommands Run "free:status"
    ///   ... "free:on" | "free:off" | "free:auto"      the switch (Auto = on in the spell lab only; reset to Auto when Play starts)
    ///   ... "free:play"                                 = vfx9a:play (the lab in Play with a Vol2 scope; the showcase installs itself there)
    ///   ... "free:run:P0,F0,P1:bodies=..."             = vfx9a:run with the showcase on (refused if the installer is missing)
    /// Never saves a scene, never opens a dialog.</summary>
    public static class Vol2FreeShowcaseCommands
    {
        public static string Run(string command)
        {
            string c = (command ?? "").Trim();
            if (c.StartsWith("free:", StringComparison.Ordinal)) c = c.Substring(5);
            try
            {
                switch (c)
                {
                    case "on": Vol2FreeShowcaseInstaller.Mode = Vol2FreeShowcaseMode.On; Ensure(); return "free showcase: On " + Status();
                    case "off": Vol2FreeShowcaseInstaller.Mode = Vol2FreeShowcaseMode.Off; return "free showcase: Off " + Status();
                    case "auto": Vol2FreeShowcaseInstaller.Mode = Vol2FreeShowcaseMode.Auto; Ensure(); return "free showcase: Auto " + Status();
                    case "status": return Status();
                    case "play": return Vol2VfxCommands.Run("play");
                }
                if (c.StartsWith("run:", StringComparison.Ordinal))
                {
                    if (!EditorApplication.isPlaying) return "REFUSED free:run: not in Play (free:play first)";
                    var inst = Object.FindFirstObjectByType<Vol2FreeShowcaseInstaller>();
                    if (inst == null) return "REFUSED free:run: no showcase installer in this Play (" + Status() + ")";
                    if (inst.Kit == null || !inst.Kit.Ok) return "REFUSED free:run: kit not ok: " + (inst.Kit != null ? inst.Kit.Problem : "null");
                    return Vol2VfxCommands.Run("run:" + c.Substring(4)) + " | " + Status();
                }
                return "REFUSED free: on | off | auto | status | play | run:<vfx9a spec>";
            }
            catch (Exception e) { return "REFUSED free:" + c + ": " + e.GetType().Name + " " + e.Message; }
        }

        static void Ensure()
        {
            if (!EditorApplication.isPlaying || Vol2FreeShowcaseInstaller.Mode == Vol2FreeShowcaseMode.Off) return;
            if (Object.FindFirstObjectByType<Vol2FreeShowcaseInstaller>() == null && (Vol2FreeShowcaseInstaller.Mode == Vol2FreeShowcaseMode.On || Vol2FreeShowcaseInstaller.LabLoaded()))
                new GameObject("Vol2FreeShowcaseInstaller").AddComponent<Vol2FreeShowcaseInstaller>();
        }

        static string Status()
        {
            var inst = EditorApplication.isPlaying ? Object.FindFirstObjectByType<Vol2FreeShowcaseInstaller>() : null;
            string s = "mode=" + Vol2FreeShowcaseInstaller.Mode + " playing=" + EditorApplication.isPlaying + " lab=" + Vol2FreeShowcaseInstaller.LabLoaded();
            if (inst == null) return s + " installer=none";
            s += " installer on=" + inst.On + " views=" + inst.Installed + " kit=" + (inst.Kit != null && inst.Kit.Ok ? "ok" : inst.Kit != null ? inst.Kit.Problem : "null");
            s += " recipes=[" + string.Join(", ", inst.Recipes.Select(r => r.Glyph + ":" + r.Kind + " shown " + r.Shown + (r.LastNote != "" ? " (" + r.LastNote + ")" : ""))) + "]";
            return s;
        }
    }
}
