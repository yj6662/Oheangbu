using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Optional exact final TERRAIN world height. Highest absolute Y, never a vegetation delta.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/World/Macro Final Surface")]
    public sealed class WorldMacroFinalSurfaceSO : ScriptableObject
    {
        [Tooltip("Final world-space terrain triangles, including zero-height surfaces. No bridges, roofs or vegetation.")]
        public WorldMacroSurfaceDeformationSO.TriangleSurfaceData AbsoluteHeightTriangles;

        /// <summary>Author once from final world XYZ vertices, three per triangle; takes array ownership without copying.</summary>
        public void SetTriangles(Vector3[] finalWorldVertices, float binSize = 16)
        {
            AbsoluteHeightTriangles = BuildAbsoluteTriangles(finalWorldVertices, binSize);
        }

        public bool TrySample(float x, float z, out float absoluteWorldY) =>
            TrySample(AbsoluteHeightTriangles, x, z, out absoluteWorldY);

        // Pure entry points allow validation without constructing a Unity object.
        public static WorldMacroSurfaceDeformationSO.TriangleSurfaceData BuildAbsoluteTriangles(Vector3[] finalWorldVertices, float binSize = 16) =>
            WorldMacroSurfaceDeformationSO.BuildTriangleSurface(finalWorldVertices, binSize, true);

        public static bool TrySample(WorldMacroSurfaceDeformationSO.TriangleSurfaceData triangles, float x, float z, out float absoluteWorldY) =>
            WorldMacroSurfaceDeformationSO.TrySampleTriangleValue(triangles, x, z, out absoluteWorldY, true);
    }
}
