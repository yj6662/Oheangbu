using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    [CreateAssetMenu(menuName = "Oheangbu/VFX/KTP Contact Profile")]
    public sealed class KtpContactProfile : ScriptableObject
    {
        public const string ResourcePath = "KTP_ContactProfile";
        [Serializable]
        public struct Entry
        {
            public Element Element;
            public GameObject Source;
        }

        public Entry[] Parries = Array.Empty<Entry>();
        public GameObject PlayerHit;
        public Vfx120Profile[] SpellProfiles = Array.Empty<Vfx120Profile>();
        public Vfx120Profile ForSpell(char letter)
        {
            foreach (var profile in SpellProfiles)
                if (profile != null && profile.KtpEmphasis && profile.Glyph == letter.ToString()) return profile;
            return null;
        }
        public Vector3 SourceEuler = new Vector3(0, 90, 0);
        [Min(.001f)] public float ParryScale = .22f;
        [Range(.01f, 1f)] public float HalfScale = .5f;
        [Min(.001f)] public float PlayerHitScale = .09f;
        [Min(.1f)] public float PlayerHitDistance = 1.6f;
        public Vector2 PlayerHitOffset = new Vector2(0, -.12f);

        public GameObject ParrySource(Element element)
        {
            foreach (var entry in Parries) if (entry.Element == element) return entry.Source;
            return null;
        }
    }
}
