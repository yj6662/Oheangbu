using UnityEngine;
using Oheangbu.Drawing;

namespace Oheangbu.App.World.UI
{
    [CreateAssetMenu(menuName="Oheangbu/UI/Playtest Theme")]
    public sealed class PlaytestUiThemeSO : ScriptableObject
    {
        public CompactUiProfileSO Icons;
        public Font Font;
        public JamoTemplateLibrarySO StrokeTemplates;
        public Texture2D PaperTexture, TitleBackdrop;
        public Sprite BrushStroke, PromptPaper, LockRing;
        public Sprite FragmentIcon;
        public AudioClip PaperSound, ConfirmSound, BackSound;
        public CompactSoundPalette255 SoundPalette;
        public WorldMacroAudioMixProfileSO AudioMix;
        public Color Ink = new Color(.17f,.15f,.12f,1);
        public Color Paper = new Color(.969f,.945f,.894f,1);
        public Color Muted = new Color(.43f,.41f,.36f,1);
        public Color Seal = new Color(.49f,.19f,.13f,1);
    }
}
