using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Small, owned record of visual-only corridor dressing. It never owns routes, terrain, content, or progression.</summary>
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Visual Corridor")]
    public sealed class WorldMacroVisualCorridorSO : ScriptableObject
    {
        [Serializable] public sealed class SourceRecord
        {
            public string Id, SourcePath, SourceHash;
            public int LodCount, TriangleCount;
        }

        [Serializable] public sealed class Placement
        {
            public string Id, SourceId, Group;
            public Vector3 Position, Size;
            public float Yaw;
            public bool Solid;
        }

        [Serializable] public sealed class View
        {
            public string Id;
            public Vector3 Eye, Target;
        }

        public int Seed = 20260915;
        public float TrailWidth = 3.2f, MainPathWidth = 4.1f;
        public float ForestDensity = .55f, RockDensity = .34f;
        public string Scope = "Static visual corridor only. Existing terrain, routes, content, progression, player, and owned asset reuse are preserved.";
        public SourceRecord[] Sources = Array.Empty<SourceRecord>();
        public Placement[] Placements = Array.Empty<Placement>();
        public View[] Views = Array.Empty<View>();
    }
}
