using System;
using UnityEngine;
namespace Oheangbu.Data.World
{
    // SPEC-NATURE-COLLISION-306 / D306: proximity stand-in colliders for instanced sheet trees, rocks and props. Every value TEST.
    [CreateAssetMenu(menuName="Oheangbu/World/Natural Solid Profile")]
    public sealed class NaturalSolidProfileSO : ScriptableObject
    {
        public enum RockShape { Box, Sphere }
        [Tooltip("TagManager layer for the pooled colliders; missing -> Default (counted as LayerMissing).")]
        public string LayerName="NatureSolid";
        [Header("Reach (flat metres; hysteresis)")]
        [Min(1)] public float ActivateRadius=32;
        [Min(1)] public float ReleaseRadius=40;
        [Min(.5f)] public float VerticalReach=12;
        [Min(0)] public float VerticalHysteresis=4;
        [Header("Vehicle look-ahead corridor")]
        [Min(0)] public float VehicleLookAheadSeconds=3;
        [Min(0)] public float VehicleLookAheadMax=100;
        [Header("Cadence and budget")]
        [Min(.1f)] public float RequeryDistance=2;
        [Min(.02f)] public float RequeryInterval=.2f;
        [Tooltip("Within this flat distance every solid is enabled in the same tick regardless of the budget.")]
        [Min(0)] public float UrgentRadius=12;
        [Tooltip("Activations per tick beyond UrgentRadius; the rest carry to the next frame (never dropped).")]
        [Min(1)] public int ActivationsPerTick=48;
        [Min(0)] public int InitialPoolPerShape=256;
        [Min(2)] public float CellSize=8;
        [Header("Shapes")]
        [Min(.01f)] public float TreeRadiusMin=.12f;
        [Min(.01f)] public float TreeRadiusMax=.7f;
        [Min(.2f)] public float TreeHeightMax=5;
        [Tooltip("Place the trunk capsule at the source mesh pivot (LOD0 part translation) instead of the bounds centre.")]
        public bool TrunkAtMeshOrigin=true;
        [Range(.1f,1)] public float BoxShrink=.85f;
        [Tooltip("Solids whose world height is below this are skipped (character step height).")]
        [Min(0)] public float MinSolidHeight=.3f;
        public RockShape Rocks=RockShape.Box;
        public bool IncludeProps=true;
        [Header("Filters")]
        [Tooltip("Placement id prefixes that already own fixed colliders.")]
        public string[] SkipPlacementPrefixes={"detail261_"};
        [Tooltip("Prototype id substrings never given a solid (ordinal, case-insensitive).")]
        public string[] SkipPrototypeTokens=Array.Empty<string>();
        [Tooltip("Prop prototype id substrings that sound as wood (trees always do).")]
        public string[] WoodPropTokens={"Wood","Log","Timber","Plank","Stump"};
        // PLAN 2-6 change 7: route-blocking placements that get no collider (protected/owned sheets are never edited; an
        // unprotected one is moved instead where it can be). Written by the editor command NaturalSolids306 apply/revert.
        [Serializable] public sealed class SkipPlacement
        {
            public WorldMacroDressingSheetSO Sheet;
            [Tooltip("FixedPlacement.Id (stable across rebakes); Index is a hint, re-resolved by Id when the sheet order changed.")]
            public string PlacementId;
            public int Index=-1;
            public string RouteId,Reason;
        }
        [Tooltip("Placements on or beside a walking route that stay visual only (resolved once in Prepare; no per-frame cost).")]
        public SkipPlacement[] SkipPlacements=Array.Empty<SkipPlacement>();
    }
}
