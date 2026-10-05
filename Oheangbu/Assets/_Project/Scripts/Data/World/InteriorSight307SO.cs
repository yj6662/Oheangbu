using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>#307 perf (SPEC-PERF-120): baked "sealed" cells of one interior zone (cave). From a sealed cell level no exterior pixel
    /// can be seen: InteriorSight307BakeTool proved, per cell level and view, that skipping the exterior vegetation draws and culling
    /// every renderer beyond SightRadius gives byte-identical frames. Runtime (App InteriorSight307) applies that only while the camera
    /// stands in a sealed level's proven height band. Loaded from Resources/Perf307. Re-bake after moving cave geometry or its portal.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/Perf/Interior Sight 307")]
    public sealed class InteriorSight307SO : ScriptableObject
    {
        public const int Levels = 3;       // NavMesh floor levels per XZ cell (cave floors, ledges, the terrain surface above)
        public string ZoneId = "";
        public Vector2 Origin;             // world XZ of cell (0,0)'s lower corner
        public float Cell = 3f;
        public int Width, Height;
        public float MinY, MaxY;           // overall camera height band of every level
        public float SightRadius = 180f;   // spherical layer cull distance while sealed
        public byte[] Sealed = Array.Empty<byte>();    // [(x + z * Width) * Levels + level]: 1 = sealed (after same-level dilation)
        public float[] FloorY = Array.Empty<float>();  // same index: that level's floor height (NaN = no level)
        public float EyeLow = .6f, EyeHigh = 3.9f;     // camera height above a level's floor the proof covers (eyes 1.0 / 2.2 / 3.4 m)
        [TextArea] public string BakeInfo = "";

        public bool IsSealed(Vector3 world)
        {
            if (Sealed == null || FloorY == null || Width <= 0 || Height <= 0 || world.y < MinY || world.y > MaxY) return false;
            int x = Mathf.FloorToInt((world.x - Origin.x) / Cell), z = Mathf.FloorToInt((world.z - Origin.y) / Cell);
            if (x < 0 || z < 0 || x >= Width || z >= Height) return false;
            int b = (x + z * Width) * Levels;
            if (b + Levels > Sealed.Length || b + Levels > FloorY.Length) return false;
            // the level whose proven band holds the camera decides; an unproven height (between levels, on the terrain above) is open
            for (int l = 0; l < Levels; l++)
            {
                float h = world.y - FloorY[b + l];
                if (h >= EyeLow && h <= EyeHigh) return Sealed[b + l] == 1;
            }
            return false;
        }
    }
}
