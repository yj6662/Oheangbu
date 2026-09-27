using System;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App.World
{
    [CreateAssetMenu(menuName = "Oheangbu/Playtest/Audio Feedback Profile")]
    public sealed class WorldMacroPlaytestAudioProfileSO : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            public AudioClip Clip;
            [Range(0f, 1f)] public float Volume = .7f;
            [Range(0f, 1f)] public float SpatialBlend;
            [Min(0f)] public float Cooldown;
            [Min(1)] public int MaxConcurrent = 3;
            [Min(.005f)] public float AttackSeconds = .012f;
            [Min(.005f)] public float ReleaseSeconds = .04f;
        }

        public WorldMacroAudioMixProfileSO Mix;
        public CompactSoundPalette255 ExtendedPalette;
        public Cue HarvestStart, HarvestLoop, HarvestEnd;
        public Cue Waiting, SaveFailed;
        public Cue BrushStroke = new Cue { Volume = .42f };
        public Cue CastWood = new Cue { Volume = .64f };
        public Cue CastFire = new Cue { Volume = .64f };
        public Cue CastEarth = new Cue { Volume = .64f };
        public Cue CastMetal = new Cue { Volume = .64f };
        public Cue CastWater = new Cue { Volume = .64f };
        public Cue Impact = new Cue { Volume = .72f, SpatialBlend = 1f, Cooldown = .055f };
        public Cue PlayerHit = new Cue { Volume = .78f, Cooldown = .08f };
        public Cue Parry = new Cue { Volume = .82f, SpatialBlend = 1f, Cooldown = .08f };
        public Cue Harvest = new Cue { Volume = .48f, SpatialBlend = 1f, Cooldown = 1.2f };
        public Cue Interact = new Cue { Volume = .5f, SpatialBlend = 1f, Cooldown = .1f };
        public Cue Rest = new Cue { Volume = .58f, SpatialBlend = 1f, Cooldown = .25f };
        public Cue SummonAppear = new Cue { Volume = .78f, SpatialBlend = 1f, Cooldown = .05f };
        public Cue SummonRelease = new Cue { Volume = .62f, SpatialBlend = 1f, Cooldown = .05f };

        public Cue CastFor(Element element)
        {
            switch (element)
            {
                case Element.Fire: return CastFire;
                case Element.Earth: return CastEarth;
                case Element.Metal: return CastMetal;
                case Element.Water: return CastWater;
                default: return CastWood;
            }
        }
    }
}
