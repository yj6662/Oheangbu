using System;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public enum Vfx120Behavior { Projectile, Bind, Heal, Shield, Zone, Summon, Weapon, Buff, Burst, Wave, Reserve }
    public enum Vfx120Layout { Spear, Spiral, Fan, Rain, Dome, Field, Orbit, Wave, Cage, Petals, Pillar, Helix }

    [CreateAssetMenu(menuName = "Oheangbu/VFX120/Spell profile")]
    public sealed class Vfx120Profile : ScriptableObject
    {
        public string Glyph;
        public string Title;
        public string Intent;
        public string Family;
        public bool Assigned;
        public Vfx120Behavior Behavior;
        public Vfx120Layout Layout;
        public Mesh BodyMesh;
        public Mesh AccentMesh;
        public Material BodyMaterial;
        public Material InkMaterial;
        public Material PatternMaterial;
        public Material MistMaterial;
        public Color Pigment = new Color(.18f, .3f, .23f);
        public Color Accent = new Color(.56f, .47f, .3f);
        public Color Ink = new Color(.075f, .065f, .058f);
        [Range(1, 32)] public int Count = 10;
        [Range(0, 3)] public int RibbonCount = 2;
        [Range(.2f, 8f)] public float Size = 2f;
        public Vector3 PartScale = new Vector3(.14f, .14f, .85f);
        [Range(.5f, 12f)] public float Duration = 3f;
        [Range(.1f, 3f)] public float Flight = .6f;
        [Range(0f, 8f)] public float Turns = 1f;
        [Range(0f, 3f)] public float Lift = 1f;
        [Range(0f, 1f)] public float Stagger = .15f;
        public bool UseMist = true;
        public bool Grounded = true;
        public string SourcePattern;
        public string RecipeJson;
    }

}
