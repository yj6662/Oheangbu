using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Compact Hydrology")]
    public sealed class CompactHydrologySO : ScriptableObject
    {
        [Serializable] public sealed class WaterRow
        {
            public Vector3 Position;
            public float LeftWidth, RightWidth, BedY, Distance, Flow;
        }
        [Serializable] public sealed class Reach
        {
            public string Id, ParentId;
            public float JoinDistance;
            public WaterRow[] Rows=Array.Empty<WaterRow>();
        }
        [Serializable] public sealed class LakeData
        {
            public float Level;
            public Vector2[] Polygon=Array.Empty<Vector2>();
            public Vector3[] ShorePoints=Array.Empty<Vector3>();
        }
        public int Version=295, Width, Height;
        public float Cell;
        public LakeData Lake=new LakeData();
        public Reach[] Reaches=Array.Empty<Reach>();
        public string[] ChunkFiles=Array.Empty<string>();
        public string SourceHash;
        public TextAsset WaterLevels, ProtectedMask, GenerationReport;
    }
}
