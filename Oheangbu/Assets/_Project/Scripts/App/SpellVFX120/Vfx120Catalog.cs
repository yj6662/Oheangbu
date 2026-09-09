using System;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    [CreateAssetMenu(menuName = "Oheangbu/VFX120/Catalog")]
    public sealed class Vfx120Catalog : ScriptableObject
    {
        [Serializable] public struct Entry
        {
            public string Glyph;
            public Vfx120Profile Profile;
            public GameObject Prefab;
            public bool GameplayConnected;
        }
        public Entry[] Entries = Array.Empty<Entry>();
    }
}
