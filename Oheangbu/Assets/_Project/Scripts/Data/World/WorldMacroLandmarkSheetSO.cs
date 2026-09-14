using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Landmarks")]
    public sealed class WorldMacroLandmarkSheetSO : ScriptableObject
    {
        [Serializable] public sealed class Landmark
        {
            public string Id, SiteId;
            public Vector3 Position;
            public float Yaw;
            public bool PlacementResolved;
            public Vector2 CourtyardSize;
            public float HallWidth;
        }
        public Landmark[] Landmarks=Array.Empty<Landmark>();
    }
}
