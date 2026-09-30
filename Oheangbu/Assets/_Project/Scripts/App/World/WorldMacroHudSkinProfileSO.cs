using UnityEngine;

namespace Oheangbu.App.World
{
    /// <summary>Optional, playtest-owned skin. New resource bars remain opt-in for legacy scenes.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World Macro/Playtest HUD Skin", fileName = "WorldMacroHudSkinProfile")]
    public sealed class WorldMacroHudSkinProfileSO : ScriptableObject
    {
        [Header("Recraft derivatives")]
        public Sprite HpStroke;
        public Sprite InkBottle;
        public Sprite LockRing;
        public Sprite PromptPaper;
        public Font KoreanFont;
        public bool UseInkBar;
        public Vector2 InkBarPosition = new Vector2(86f, 65f);
        public Vector2 InkBarSize = new Vector2(236f, 16f);

        [Header("ART-COLOR")]
        public Color Ink = new Color(0.165f, 0.149f, 0.133f, 0.96f);
        public Color Paper = new Color(0.969f, 0.945f, 0.894f, 0.90f);
        [Range(0f, 0.45f)] public float MaximumDangerAlpha = 0.24f;
        [Range(0.1f, 0.7f)] public float DangerBeginsAtHp = 0.35f;

        [Header("World-space interaction letter")]
        [Min(.05f)] public float InteractionLetterHeight = .22f;
        public float InteractionLetterOffset = .16f;
        [Range(1,5)] public float InteractionOutlinePixels = 2.2f;
        public Color InteractionOutlineColor = new Color(.83f,.75f,.53f,1);
        public Color InteractionLetterColor = new Color(.97f, .95f, .89f, 1f);

        [Header("Reference-resolution layout")]
        public Vector2 HpPosition = new Vector2(48f, 62f);
        public Vector2 HpSize = new Vector2(260f, 20f);
        public Vector2 BottlePosition = new Vector2(58f, 136f);
        public Vector2 BottleSize = new Vector2(60f, 108f);
        public Vector2 ReticleSize = new Vector2(44f, 44f);
        public Vector2 PromptSize = new Vector2(780f, 112f);

        [Header("#304 UI style (same asset as PlaytestUiThemeSO.Style304)")]
        public UI.UiStyle304SO Style304;
    }
}
