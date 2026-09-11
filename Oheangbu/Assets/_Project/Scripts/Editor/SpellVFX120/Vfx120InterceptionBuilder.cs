using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    public static class Vfx120InterceptionBuilder
    {
        [Serializable] sealed class Report
        {
            public string status = "AUTHORED_AWAITING_USER_VISUAL_REVIEW", glyph = "송", beforeJson, afterJson, snapshot;
            public string needleSource, cordSource, cueCheck, art = "AWAITING_USER_REVIEW", gameplay = "UNCONNECTED";
            public int targets = 6, needleTris, cordTris, totalMaximumBodyTris;
        }
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
            string root = Vfx120Editor.AssetRoot;
            var p = AssetDatabase.LoadAssetAtPath<Vfx120Profile>(root + "/Profiles/090_C1A1.asset");
            var r = new Report { needleSource = root + "/Meshes/MetalDetails/VFX120_FrostNeedle_078.asset",
                cordSource = root + "/Meshes/MetalDetails/VFX120_FrostCord_078.asset" };
            var needle = AssetDatabase.LoadAssetAtPath<Mesh>(r.needleSource); var cord = AssetDatabase.LoadAssetAtPath<Mesh>(r.cordSource);
            if (p == null || p.Glyph != "송" || needle == null || cord == null) throw new InvalidOperationException("Missing 송 or authored 상 meshes");
            r.beforeJson = JsonUtility.ToJson(p);
            string folder = Path.Combine(Vfx120Editor.Output, "MetalDetailOriginals"); Directory.CreateDirectory(folder);
            r.snapshot = Path.Combine(folder, "090_C1A1.asset.txt");
            if (!File.Exists(r.snapshot)) File.Copy(AssetDatabase.GetAssetPath(p), r.snapshot);
            p.BodyMesh = needle; p.AccentMesh = cord; p.PartScale = Vector3.one;
            p.Count = Vfx120InterceptionMotion.MaxTargets; p.RibbonCount = 0; p.NativeImpactScale = .45f;
            EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
            r.afterJson = JsonUtility.ToJson(p); r.needleTris = needle.triangles.Length / 3; r.cordTris = cord.triangles.Length / 3;
            r.totalMaximumBodyTris = r.targets * (r.needleTris + 3 * r.cordTris);
            r.cueCheck = Check();
            File.WriteAllText(Path.Combine(Vfx120Editor.Output, "interception_090_build.json"), JsonUtility.ToJson(r, true)); return r.status + "; " + r.cueCheck;
        }
        static string Check()
        {
            var c = Vfx120CueMotion.Context.Create("송"); c.InterceptionFan = true; c.Duration = 2.7f; c.Age = .8f;
            for (int i = 0; i < 18; i++)
                if (Vfx120InterceptionMotion.Sample(c, Vfx120CueMotion.PartRole.Accent, i).Visible)
                    throw new InvalidOperationException("Invented target without input");
            c.Interceptions = Vfx120InterceptionReviewFixture.CreatePlans(-1);
            // A missing/invalid slot cannot borrow a neighbouring hit.
            c.Interceptions[2].HitConfirmed = false;
            c.Interceptions[4].Point = new Vector3(float.NaN,0,0);
            int samples = 0;
            foreach (int fps in new[] {30,60,120}) for (int frame = 0; frame <= 3*fps; frame++)
            {
                c.Age = frame/(float)fps;
                for (int i = 0; i < 18; i++)
                {
                    var pose = Vfx120InterceptionMotion.Sample(c,Vfx120CueMotion.PartRole.Accent,i);
                    int slot=i/3;
                    if (pose.Visible && (slot==2 || slot==4 || c.Age<c.Interceptions[slot].HitAt || c.Age>=2.7f))
                        throw new InvalidOperationException("Wrong interception slot/timing");
                    if (!Vfx120InterceptionMotion.Finite(pose.Position) || !Vfx120InterceptionMotion.Finite(pose.Scale))
                        throw new InvalidOperationException("Nonfinite interception pose");
                    samples++;
                }
            }
            c.Age=.65f;
            for(int slot=0;slot<6;slot++)
            {
                bool visible=Vfx120InterceptionMotion.Sample(c,Vfx120CueMotion.PartRole.Accent,slot*3).Visible;
                if(visible!=(slot!=2&&slot!=4)) throw new InvalidOperationException("Confirmed knot missing or unconfirmed knot present");
            }
            return "PASS_INDEPENDENT_TARGET_AND_HIT_TIMING_ONLY samples="+samples;
        }
    }
}
