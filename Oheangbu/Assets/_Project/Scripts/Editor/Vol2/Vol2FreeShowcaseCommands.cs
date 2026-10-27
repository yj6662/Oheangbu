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
                    // 2026-10-27 ink-world pass: the shared ink layer and the world colour grade (default on; reset to on when Play starts) - before / after in one session
                    case "ink:on": Vol2FsWorld.InkOn = true; return "free ink: On (grade " + (Vol2FsWorld.GradeOn ? "On" : "Off") + ")";
                    case "ink:off": Vol2FsWorld.InkOn = false; return "free ink: Off (grade " + (Vol2FsWorld.GradeOn ? "On" : "Off") + ")";
                    case "grade:on": Vol2FsWorld.GradeOn = true; return "free grade: On (ink " + (Vol2FsWorld.InkOn ? "On" : "Off") + ")";
                    case "grade:off": Vol2FsWorld.GradeOn = false; return "free grade: Off (ink " + (Vol2FsWorld.InkOn ? "On" : "Off") + ")";
                    case "play": return Vol2VfxCommands.Run("play");
                    // batch 2 (LAB-ONLY): the scripted enemy blow on the ㅓ guard, judged by the real vol.2 parry rules (Vol2FsEnemyStrike)
                    case "strike:on": return Vol2FsEnemyStrike.Arm(true);
                    case "strike:off": return Vol2FsEnemyStrike.Arm(false);
                    case "strike:status": return Vol2FsEnemyStrike.Status();
                    // batch 7: the editor's quality level for a capture (in memory only, never saved): quality:PC before, quality:<the prior name> after
                    case "quality": return "quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + " (" + string.Join(",", QualitySettings.names) + ")";
                }
                if (c.StartsWith("quality:", StringComparison.Ordinal))
                {
                    string want = c.Substring(8).Trim();
                    int q = Array.IndexOf(QualitySettings.names, want);
                    if (q < 0 && int.TryParse(want, out int qi)) q = qi;
                    if (q < 0 || q >= QualitySettings.names.Length) return "REFUSED free:quality: " + want + " (" + string.Join(",", QualitySettings.names) + ")";
                    string before = QualitySettings.names[QualitySettings.GetQualityLevel()];
                    if (QualitySettings.GetQualityLevel() != q) QualitySettings.SetQualityLevel(q, true);
                    return "quality " + before + " -> " + QualitySettings.names[QualitySettings.GetQualityLevel()];
                }
                // 2026-10-27 weak-points pass: the showcase steel's diagnostic switch (FsMetal _FsMetalDiag, a shader global; 0 = normal) - for the cause stills only
                if (c.StartsWith("metaldiag:", StringComparison.Ordinal))
                {
                    if (!float.TryParse(c.Substring(10).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d)) return "REFUSED free:metaldiag: <0..5>";
                    Shader.SetGlobalFloat("_FsMetalDiag", Mathf.Clamp(Mathf.Round(d), 0f, 5f));
                    return "metaldiag " + Shader.GetGlobalFloat("_FsMetalDiag").ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (c.StartsWith("run:", StringComparison.Ordinal))
                {
                    if (!EditorApplication.isPlaying) return "REFUSED free:run: not in Play (free:play first)";
                    var inst = Object.FindFirstObjectByType<Vol2FreeShowcaseInstaller>();
                    if (inst == null) return "REFUSED free:run: no showcase installer in this Play (" + Status() + ")";
                    if (inst.Kit == null || !inst.Kit.Ok) return "REFUSED free:run: kit not ok: " + (inst.Kit != null ? inst.Kit.Problem : "null");
                    return Vol2VfxCommands.Run("run:" + c.Substring(4)) + " | " + Status();
                }
                return "REFUSED free: on | off | auto | status | play | strike:on|off|status | quality[:name] | run:<vfx9a spec>";
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
            string s = "ink=" + Vol2FsWorld.InkOn + " grade=" + Vol2FsWorld.GradeOn + " mode=" + Vol2FreeShowcaseInstaller.Mode + " playing=" + EditorApplication.isPlaying + " lab=" + Vol2FreeShowcaseInstaller.LabLoaded();
            s += " | " + Vol2FsEnemyStrike.Status();
            if (inst == null) return s + " installer=none";
            s += " installer on=" + inst.On + " views=" + inst.Installed + " kit=" + (inst.Kit != null && inst.Kit.Ok ? "ok" : inst.Kit != null ? inst.Kit.Problem : "null");
            s += " geok=" + inst.GeokShown + (inst.GeokNote != "" ? " (" + inst.GeokNote + ")" : "");      // batch 3: 걱 (the recovery, no cast log)
            s += " recipes=[" + string.Join(", ", inst.Recipes.Select(r => r.Kind + "{" + string.Join(" ", r.Glyphs.Select(g => g + ":" + r.ShownOf(g))) + "} shown " + r.Shown + (r.LastNote != "" ? " (" + r.LastNote + ")" : ""))) + "]";
            // batch 4: every glyph's last note (the spread looks note their hops / marks / ends)
            // batch 5: + the 심는다 20 (plant / fire / strike / end reasons)
            // batch 6: + the 가른다 20 (contacts / splits / strips / 솟's metal)
            // batch 7: + the 돌아온다 20 (legs / return hits / shots / carry / drags / cleanse)
            var notes = inst.Recipes.SelectMany(r => r.Notes).Where(kv => "간난만산안건넌먼선언곤논몬손온군눈문순운감남맘삼암검넘멈섬엄곰놈몸솜옴굼눔뭄숨움갓낫맛삿앗것넛멋섯엇곳놋못솟옷굿눗뭇숫웃강낭망상앙겅넝멍성엉공농몽송옹궁눙뭉숭웅".IndexOf(kv.Key) >= 0).Select(kv => kv.Key + ": " + kv.Value);
            if (notes.Any()) s += "\nnotes:\n" + string.Join("\n", notes);
            // batch 8: the 25 reaction directions (no cast: the installer's own looks, by cell "목>화")
            s += "\nreactions shown " + inst.ReactShown + " live " + inst.ReactLive;
            if (inst.ReactNotes.Count > 0) s += "\n" + string.Join("\n", inst.ReactNotes.Select(kv => kv.Key + ": " + kv.Value));
            return s;
        }
    }
}
