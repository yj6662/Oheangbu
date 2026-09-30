using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.App.World.UI;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#304 HUD area setup (Art/UI304/screens/hud.md). Queue: Oheangbu.EditorTools.WorldMacro.HudSetup304 Execute("hud304-setup").
    /// 1. runs UiOverhaul304 "ui304-import" (imports Textures/hud/*.png with Textures/hud/import304.json: Sprite, mips, Clamp);
    /// 2. creates / updates Assets/_Project/Resources/UI304/hud/HudTokens304.asset and fills its sprite slots from Textures/hud.
    /// Token values already in the asset are kept (edit them there); "hud304-setup:reset" rewrites them to the code defaults.
    /// "hud304-report" prints what the asset holds. Idempotent, Edit Mode only, no dialogs, no scene edits.
    /// QA1: "hud304-prompt" (Play Mode, read-only) prints why the interaction prompt / world F is or is not showing: editor focus,
    /// the gameplay UI gate, the session focus and its text, the prompt filter, the HUD prompt's drawn state, the world-F state
    /// and the nearest interaction points (distance, radius, CanInteract). Nothing is changed.</summary>
    public static class HudSetup304
    {
        const string Textures = "Assets/_Project/Art/UI/UI304/Textures/hud";
        const string Folder = "Assets/_Project/Resources/UI304/hud";
        const string AssetPath = Folder + "/HudTokens304.asset";

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            if (a == "hud304-report") return Report();
            if (a == "hud304-prompt") return PromptReport();
            if (a == "hud304-setup" || a == "hud304-setup:reset") return Setup(a.EndsWith(":reset", StringComparison.Ordinal));
            throw new ArgumentException("Expected hud304-setup, hud304-setup:reset, hud304-report or hud304-prompt");
        }

        static string Setup(bool reset)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("hud304-setup is Edit Mode only.");
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Scripts are compiling; call hud304-setup again when done.");
            var log = new List<string>();
            try { log.Add(UiOverhaul304.Execute("ui304-import")); }
            catch (Exception e) { log.Add("IMPORT skipped: " + e.Message); }
            EnsureFolder(Folder);
            var tokens = AssetDatabase.LoadAssetAtPath<HudTokens304>(AssetPath);
            bool created = tokens == null;
            if (created) { tokens = ScriptableObject.CreateInstance<HudTokens304>(); AssetDatabase.CreateAsset(tokens, AssetPath); }
            else if (reset)
            {
                var fresh = ScriptableObject.CreateInstance<HudTokens304>();
                EditorUtility.CopySerialized(fresh, tokens);
                UnityEngine.Object.DestroyImmediate(fresh);
            }
            int missing = 0;
            Sprite Get(string name)
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Textures + "/" + name + ".png");
                if (sprite == null) { missing++; log.Add("MISSING sprite " + Textures + "/" + name + ".png (run ui304-import / check the texture type)"); }
                return sprite;
            }
            tokens.EnsoClosed = Get("enso_closed"); tokens.EnsoClosedRim = Get("enso_closed_rim");
            tokens.SpineAcross = Get("spine_across");
            tokens.PictoRest = Get("picto_rest"); tokens.PictoRestRim = Get("picto_rest_rim");
            tokens.PictoCave = Get("picto_cave"); tokens.PictoCaveRim = Get("picto_cave_rim");
            tokens.PictoMountain = Get("picto_mountain"); tokens.PictoMountainRim = Get("picto_mountain_rim");
            tokens.PictoPlace = Get("picto_place"); tokens.PictoPlaceRim = Get("picto_place_rim");
            tokens.PictoCoin = Get("picto_coin"); tokens.PictoCoinRim = Get("picto_coin_rim");
            EditorUtility.SetDirty(tokens);
            AssetDatabase.SaveAssets();
            log.Add((created ? "CREATED " : reset ? "RESET " : "UPDATED ") + AssetPath + " sprites missing=" + missing);
            return string.Join("\n", log);
        }

        static string Report()
        {
            var t = AssetDatabase.LoadAssetAtPath<HudTokens304>(AssetPath);
            if (t == null) return "HUDTOKENS missing " + AssetPath + " (run hud304-setup)";
            string S(Sprite s) => s != null ? s.name : "<none>";
            string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            Material rim = MeterMaterial("InkMeter_Rim"), body = MeterMaterial("InkMeter_Body");
            return "HUDTOKENS " + AssetPath
                + "\nMETERS hp=" + t.HpHeight + " ink=" + t.InkHeight + " gap=" + t.MeterGap + " valueRimEdgeOnly=" + t.MeterValueRimEdgeOnly
                + "\nLINEAR inkGamma=" + t.LinearInkGamma + " colorSpace=" + QualitySettings.activeColorSpace
                + " (ghost .27 -> " + t.InkAlphaFor(body, .27f).ToString("0.00", CultureInfo.InvariantCulture) + ")"
                + "\nQA2 paperK=" + t.PaperOverWorldExponent + " valueAlpha=" + t.MeterValueAlpha + " lagUndo=" + t.LagUndoRemap
                + " shaderGamma rim=" + F(HudTokens304.ShaderPaperGamma(rim)) + " body=" + F(HudTokens304.ShaderInkGamma(body))
                + " (ghostEdge .30 -> " + F(t.PaperAlpha(rim, .3f)) + ", valueRim .72 -> " + F(t.PaperAlpha(rim, .72f))
                + ", lag .34 -> " + F(t.UndoInkRemap(body, .34f)) + "; danger edges + groggy disc on UI/InkReveal)"
                + "\nLOWHP onset=" + t.DangerOnset + " band=" + t.DangerOnsetBand
                + "\nLOCKON idle=" + t.IdleEnsoSize + " alpha=" + t.IdleAlpha
                + "\nPROMPT centre=" + t.PromptCentreX + " lead=" + t.PromptKeyLead + " gap=" + t.PromptKeyGap
                + "\nBEARING emphasis=" + t.EmphasisSeconds + " moving=" + t.MovingAlpha + " range=" + t.PlaceRangeMeters
                + "\nSPRITES enso=" + S(t.EnsoClosed) + "/" + S(t.EnsoClosedRim) + " spine=" + S(t.SpineAcross)
                + " rest=" + S(t.PictoRest) + " cave=" + S(t.PictoCave) + " mountain=" + S(t.PictoMountain) + " place=" + S(t.PictoPlace) + " coin=" + S(t.PictoCoin);
        }

        /// <summary>QA2 report: the foundation meter material by name (UI304/Materials, made by the foundation setup); null if absent.</summary>
        static Material MeterMaterial(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == name) return m;
            }
            return null;
        }

        // ------------------------------------------------------------------ QA1 prompt diagnostics (Play Mode, read-only)
        static string PromptReport()
        {
            if (!EditorApplication.isPlaying) return "PROMPT needs Play Mode (read-only diagnostics of the running HUD)";
            var ci = CultureInfo.InvariantCulture;
            string Q(string v) => v == null ? "<null>" : "'" + v.Replace("\n", "\\n") + "'";
            var presenter = UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestHudPresenter>();
            var session = presenter != null && presenter.Session != null ? presenter.Session : UnityEngine.Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            var hud = presenter != null && presenter.Hud != null ? presenter.Hud : UnityEngine.Object.FindFirstObjectByType<HudController>();
            var root = PlaytestUiRoot.Instance;
            var sb = new StringBuilder();
            sb.Append("PROMPT frame=").Append(Time.frameCount).Append(" appFocused=").Append(Application.isFocused)
              .Append(" timeScale=").Append(Time.timeScale.ToString("0.###", ci));
            if (root != null)
            {
                sb.Append(" page=").Append(Q(root.Page)).Append(" menuOpen=").Append(root.IsMenuOpen);
                var g = root.Gate;
                if (g != null) sb.Append(" gateBlocked=").Append(g.InputBlocked).Append(" releasePending=").Append(g.ReleasePending)
                    .Append(" focusOwnsBlock=").Append(g.FocusOwnsBlock).Append(" neutralFrames=").Append(g.NeutralFrames);
            }
            else sb.Append(" root=<none>");
            if (session != null)
            {
                bool hasPos = session.TryGetFocusedInteractionPosition(out var focusPos);
                bool hasBounds = session.TryGetFocusedInteractionBounds(out _);
                var renderers = session.FocusedRenderers;
                sb.Append("\nSESSION inputBlocked=").Append(session.GameplayInputBlocked).Append(" focusedId=").Append(Q(session.FocusedId))
                  .Append(" bundle=").Append(Q(session.FocusedCollectionBundleId)).Append(" hasPosition=").Append(hasPos)
                  .Append(" hasBounds=").Append(hasBounds).Append(" renderers=").Append(renderers != null ? renderers.Length : 0);
                sb.Append("\n  currentHudText=").Append(Q(session.CurrentHudText)).Append("\n  lastFeedback=").Append(Q(session.LastFeedback))
                  .Append(" saveError=").Append(Q(session.SaveError))
                  .Append("\n  promptText=").Append(Q(WorldMacroPlaytestHudPresenter.PromptText(session)));
                try
                {
                    // private InteractionPoints / FindInteractionPoint (live NPC positions), read by reflection: editor diagnostics only
                    var type = session.GetType();
                    const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                    var pointsProp = type.GetProperty("InteractionPoints", any);
                    var find = type.GetMethod("FindInteractionPoint", any, null, new[] { typeof(string) }, null);
                    var points = pointsProp != null ? pointsProp.GetValue(session) as PrologueContentSO.Point[] : null;
                    var feet = session.Walker != null && session.Walker.Body != null ? session.Walker.Body.transform.position : Vector3.zero;
                    if (points != null)
                    {
                        var near = points.Where(p => p != null)
                            .Select(p => find != null ? find.Invoke(session, new object[] { p.Id }) as PrologueContentSO.Point ?? p : p)
                            .OrderBy(p => Vector3.Distance(p.Position, feet)).Take(4);
                        foreach (var p in near)
                            sb.Append("\n  point ").Append(p.Id).Append(" d=").Append(Vector3.Distance(p.Position, feet).ToString("0.00", ci))
                              .Append(" r=").Append(p.Radius.ToString("0.00", ci)).Append(" canInteract=").Append(session.CanInteract(p.Id))
                              .Append(" prompt=").Append(Q(p.Prompt));
                    }
                    else sb.Append("\n  points=<unreadable>");
                }
                catch (Exception e) { sb.Append("\n  points failed: ").Append(e.GetType().Name).Append(": ").Append(e.Message); }
                if (hasPos) sb.Append("\n  focusPosition=").Append(focusPos.ToString("F2"));
            }
            else sb.Append("\nSESSION <none>");
            if (hud != null)
            {
                var p = hud.Prompt304;
                var canvas = hud.Canvas;
                sb.Append("\nHUD interactionVisible=").Append(hud.InteractionVisible).Append(" promptOnScreen=").Append(hud.PromptOnScreen304)
                  .Append(" canvasOn=").Append(canvas != null && canvas.enabled && canvas.gameObject.activeInHierarchy).Append(" hideText=").Append(hud.HideText);
                if (p != null)
                    sb.Append("\n  prompt frameActive=").Append(p.gameObject.activeInHierarchy).Append(" text=").Append(Q(p.Text))
                      .Append(" key=").Append(Q(p.KeyLabel)).Append(" label=").Append(p.Label != null ? Q(p.Label.text) : "-")
                      .Append(" drawnAlpha=").Append(p.DrawnAlpha.ToString("0.00", ci)).Append(" underlayReveal=").Append(p.UnderlayReveal.ToString("0.00", ci))
                      .Append(" underlayAlpha=").Append(p.Underlay != null ? p.Underlay.color.a.ToString("0.00", ci) : "-");
                else sb.Append(" prompt=<none: prototype HUD (no Skin)>");
            }
            else sb.Append("\nHUD <none>");
            if (presenter != null)
            {
                var letter = presenter.transform.Find("WorldInteractionPrompt");
                var letterCanvas = letter != null ? letter.GetComponent<Canvas>() : null;
                sb.Append("\nPRESENTER enabled=").Append(presenter.isActiveAndEnabled).Append(" letter=").Append(presenter.LastLetterState304)
                  .Append(" worldF=").Append(letterCanvas != null && letterCanvas.enabled && letterCanvas.gameObject.activeInHierarchy)
                  .Append(" outline=").Append(presenter.OutlineCount).Append(" lastText=").Append(Q(presenter.LastPromptText304))
                  .Append(" lastFrame=").Append(presenter.LastPresentFrame304);
            }
            else sb.Append("\nPRESENTER <none>");
            return sb.ToString();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
