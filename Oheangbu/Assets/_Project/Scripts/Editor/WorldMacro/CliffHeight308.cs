using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 height source (SPEC-WORLD-CLIFF-BOUNDARY-308 "높이 출처", AC-B24): ONE resolver scene -> height asset for the editor tools that used
    // to read Finish297/Surface/height.bytes as a constant (CompactEnclosure305.Offline305, EventWash308.Field308). Order:
    //   0. the scene is not one of the config's #308 target scenes (W_Demo_Compact, any older scene), or there is no #308 config -> the base
    //      field, exactly the constant those tools read before #308 (whatever that scene's own layout references)
    //   1. the target scene is loaded and its session layout carries a FinalSurface -> that asset (the runtime's own source; `tiles` /
    //      `revert` re-point it together with the tile meshes, so a scene still on stage 1a never resolves to a 1b field)
    //   2. the target scene's #308 ledger says a stage is applied -> the stage height asset recorded there
    //   3. a target scene that is neither loaded nor applied -> the base field
    // Read-only. The base path below is the pre-#308 constant and is used only when the #308 config cannot be read.
    internal static class CliffHeight308
    {
        const string PreCliffBase = "Assets/_Project/Art/World/Finish297/Surface/height.bytes";

        internal static string For(string scenePath, out string source)
        {
            CliffCore308.Config cfg = null;
            try { cfg = CliffCore308.LoadConfig(); } catch (PostLedger308.Refused) { }
            string fallback = cfg != null ? cfg.baseHeight : PreCliffBase;
            if (cfg == null) { source = "base (no #308 config)"; return fallback; }
            if (CliffCore308.SceneKey(cfg, scenePath) == null) { source = "base (not a #308 target scene)"; return fallback; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path != scenePath || !scene.isLoaded) continue;
                var session = CliffCore308.Session(scene);
                var surface = session != null && session.MountainLayout != null ? session.MountainLayout.FinalSurface : null;
                string path = surface != null ? AssetDatabase.GetAssetPath(surface) : "";
                if (!string.IsNullOrEmpty(path)) { source = "layout " + AssetDatabase.GetAssetPath(session.MountainLayout); return path; }
            }
            if (cfg != null && CliffCore308.SceneKey(cfg, scenePath) != null)
            {
                try
                {
                    var ledger = CliffBoundary308.ReadLedger(cfg, scenePath);
                    if (ledger != null && ledger.state == "applied" && ledger.layoutApplied && !string.IsNullOrEmpty(ledger.layoutAppliedSurface)) { source = "ledger stage " + ledger.stage; return ledger.layoutAppliedSurface; }
                }
                catch (PostLedger308.Refused) { }
            }
            source = cfg != null ? "base" : "base (no #308 config)";
            return fallback;
        }

        /// <summary>Height asset of the active scene.</summary>
        internal static string Active(out string source) => For(SceneManager.GetActiveScene().path, out source);
        internal static string Active() => Active(out _);

        internal static string Sha(string assetPath) => string.IsNullOrEmpty(assetPath) ? "" : CliffCore308.ShaFile(PostLedger308.Abs(assetPath));

        /// <summary>One line per target scene: height asset, sha256, where the answer came from.</summary>
        internal static string Table()
        {
            var sb = new StringBuilder();
            CliffCore308.Config cfg;
            try { cfg = CliffCore308.LoadConfig(); } catch (PostLedger308.Refused r) { return "height sources: " + r.Message; }
            foreach (var sc in cfg.scenes)
            {
                string path = For(sc.path, out string source);
                sb.AppendLine(sc.key + ": " + path + " sha256 " + Sha(path) + " [" + source + "]");
            }
            sb.Append("other scenes: " + cfg.baseHeight + " sha256 " + Sha(cfg.baseHeight) + " [base]");
            return sb.ToString();
        }
    }
}
