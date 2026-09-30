using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Oheangbu.App.World.UI
{
    /// <summary>TMP helpers for #304: apply a TypeRole, read a label from legacy Text OR TMP_Text (harnesses), text-scale
    /// rule for TMP, hanja prewarm, vertical (세로) setting, rubbing face mapping.</summary>
    public static class UiText304
    {
        static bool prewarmed;
        static int scaleFrame = -1;
        static float scaleValue = 1f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { prewarmed = false; scaleFrame = -1; scaleValue = 1f; }

        /// <summary>Settings text scale (UserSettingsData.TextScale, read once per frame; 1 outside the playtest root).
        /// V.Label applies it at creation so widths measured while building (underlays, rail slots) are already final.</summary>
        public static float TextScale
        {
            get
            {
                if (scaleFrame == Time.frameCount) return scaleValue;
                scaleFrame = Time.frameCount;
                var root = PlaytestUiRoot.Instance;
                scaleValue = root != null && root.Settings != null ? Mathf.Clamp(root.Settings.Current.TextScale, .75f, 2f) : 1f;
                return scaleValue;
            }
        }

        // ------------------------------------------------------------------ harness-facing read / write
        /// <summary>Text of the label on `go` or its children: TMP_Text first, then legacy Text (self before children). null if none.</summary>
        public static string Get(GameObject go)
        {
            if (go == null) return null;
            var tmp = go.GetComponent<TMP_Text>(); if (tmp != null) return tmp.text;
            var legacy = go.GetComponent<Text>(); if (legacy != null) return legacy.text;
            tmp = go.GetComponentInChildren<TMP_Text>(true); if (tmp != null) return tmp.text;
            legacy = go.GetComponentInChildren<Text>(true); return legacy != null ? legacy.text : null;
        }

        public static string Get(Component c) => c != null ? Get(c.gameObject) : null;

        /// <summary>Sets the text of the first TMP_Text / Text found like Get. Returns false when there is none.</summary>
        public static bool Set(GameObject go, string value)
        {
            if (go == null) return false;
            var tmp = go.GetComponent<TMP_Text>(); if (tmp == null) { var legacy = go.GetComponent<Text>(); if (legacy != null) { legacy.text = value; return true; } tmp = go.GetComponentInChildren<TMP_Text>(true); }
            if (tmp != null) { tmp.text = value; return true; }
            var child = go.GetComponentInChildren<Text>(true); if (child != null) { child.text = value; return true; }
            return false;
        }

        /// <summary>Label text at `path` under root (Transform.Find semantics), TMP or legacy.</summary>
        public static string Find(Transform root, string path)
        {
            if (root == null) return null;
            var t = string.IsNullOrEmpty(path) ? root : root.Find(path);
            return t != null ? Get(t.gameObject) : null;
        }

        // ------------------------------------------------------------------ roles
        /// <summary>Font + size + TMP line spacing + tracking + preset material of `role`. applyTextScale: roles of 28 px or less
        /// get the current TextScale (same rule as ApplyTextScale; UiTextNoScale304 labels never scale).</summary>
        public static void ApplyRole(TMP_Text t, TypeRole role, UiStyle304SO style = null, bool applyTextScale = true)
        {
            if (t == null || role == null) return;
            var font = style != null ? style.FontOf(role) : role.Font;
            if (font != null && t.font != font) t.font = font;
            bool scaled = applyTextScale && role.Size <= 28f && t.GetComponent<UiTextNoScale304>() == null;
            t.fontSize = role.Size * (scaled ? TextScale : 1f);
            t.fontStyle = FontStyles.Normal;
            t.characterSpacing = role.Tracking;
            t.lineSpacing = role.TmpLineSpacing(t.font);
            var material = style != null ? style.MaterialOf(role, t.font)
                : role.Material != null && UiStyle304SO.MatchesAtlas(role.Material, t.font) ? role.Material : null;
            if (material != null) t.fontSharedMaterial = material;
            else if (t.font != null && t.fontSharedMaterial != t.font.material) t.fontSharedMaterial = t.font.material;
            if (role.Preset == TmpPreset304.Rubbing || role.Preset == TmpPreset304.Rubbing_Lite) ApplyRubbingMapping(t);
            var baseSize = t.GetComponent<UiTextBaseSize>(); if (baseSize == null) baseSize = t.gameObject.AddComponent<UiTextBaseSize>();
            baseSize.Size = Mathf.RoundToInt(role.Size);
        }

        /// <summary>Rubbing face texture mapping (IMPLEMENTATION §3.3 "UV Line"): horizontal Line, vertical MatchAspect,
        /// a per-line offset so stacked vertical glyphs do not repeat the same wear.</summary>
        public static void ApplyRubbingMapping(TMP_Text t)
        {
            if (t == null) return;
            t.horizontalMapping = TextureMappingOptions.Line;
            t.verticalMapping = TextureMappingOptions.MatchAspect;
            t.mappingUvLineOffset = .37f;
        }

        /// <summary>TMP version of PlaytestUiRoot.ApplyTextScale: labels whose base size is &lt;= 28 scale with the text setting.</summary>
        public static void ApplyTextScale(Component root, float scale)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.GetComponent<UiTextNoScale304>() != null) continue;
                var baseSize = t.GetComponent<UiTextBaseSize>();
                if (baseSize == null) { baseSize = t.gameObject.AddComponent<UiTextBaseSize>(); baseSize.Size = Mathf.RoundToInt(t.fontSize); }
                float size = baseSize.Size * (baseSize.Size <= 28 ? scale : 1f);
                if (!Mathf.Approximately(t.fontSize, size)) t.fontSize = size;
            }
        }

        /// <summary>Adds PrewarmHanja (+ common symbols) to every family's font or its fallbacks once per Play session.</summary>
        public static void Prewarm(UiStyle304SO style)
        {
            if (prewarmed || style == null || UiStyle304SO.IsFallback(style)) return;
            prewarmed = true;
            string chars = (style.PrewarmHanja ?? "") + "·→×‹›";
            var done = new System.Collections.Generic.HashSet<TMP_FontAsset>();
            foreach (var family in style.Fonts)
            {
                var font = family != null ? family.Font : null; if (font == null || !done.Add(font)) continue;
                font.TryAddCharacters(chars, out string missing);
                if (!string.IsNullOrEmpty(missing) && font.fallbackFontAssetTable != null)
                    foreach (var fb in font.fallbackFontAssetTable) if (fb != null && done.Add(fb)) fb.TryAddCharacters(missing, out _);
            }
        }

        // ------------------------------------------------------------------ measuring / setting
        /// <summary>Preferred size of `value` in this label's current font / size (maxWidth 0 = unbounded, single line).</summary>
        public static Vector2 Preferred(TMP_Text t, string value, float maxWidth = 0f)
        {
            if (t == null) return Vector2.zero;
            return maxWidth > 0f ? t.GetPreferredValues(value ?? "", maxWidth, 0f) : t.GetPreferredValues(value ?? "");
        }

        /// <summary>세로 조판: one character per line; a space becomes an empty line (지명 사이 빈 줄).</summary>
        public static string Vertical(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new StringBuilder(value.Length * 2);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0) sb.Append('\n');
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
