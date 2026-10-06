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
        // #308 collide (D308-29 answer 2 / D308-31, SPEC-NATURE-COLLISION-306 §308): stand-ins measured from the drawn LOD0 mesh instead
        // of the prototype's Radius / 85 % bounds box. Prototype space (pivot = origin, +Y up, Scale 1); the placement's Euler and Scale
        // apply. Written by Tools/Unity/Stage308_collide (collide_measure.py -> collide_build.py); never typed by hand.
        [Serializable] public struct Capsule308
        {
            [Tooltip("Sphere centres of the two ends (a leaning stem leans with them).")] public Vector3 Base,Top;
            public float Radius;
        }
        [Serializable] public struct Box308
        {
            public Vector3 Centre,Size;
            [Tooltip("Degrees about +Y, applied before the placement's rotation.")] public float Yaw;
        }
        [Serializable] public sealed class Fit308
        {
            [Tooltip("Sheet prototype Ids that draw this mesh (exact, ordinal).")]
            public string[] PrototypeIds=Array.Empty<string>();
            [Tooltip("Prototype.Size the fit was measured on: a prototype whose Size differs (rebaked mesh) falls back to the code rule.")]
            public Vector3 Size;
            [Tooltip("Tree stems: one capsule per straight stretch of a stem. The canopy has none.")]
            public Capsule308[] Capsules=Array.Empty<Capsule308>();
            [Tooltip("Rock: boxes inside the drawn rock. Empty (with no capsule) = this prototype gets no stand-in.")]
            public Box308[] Boxes=Array.Empty<Box308>();
        }
        [Header("#308 measured fits")]
        [Tooltip("Off = every prototype uses the code rule (Radius capsule, BoxShrink box) as before #308.")]
        public bool UseFits308=true;
        [Tooltip("Largest Size difference (metres, any axis) at which a fit still belongs to its prototype.")]
        [Min(0)] public float FitSizeTolerance308=.005f;
        public Fit308[] Fits308=Array.Empty<Fit308>();
        // The fit for a prototype, or null (not listed, switched off, or measured on another mesh size).
        public Fit308 FitFor308(string prototypeId,Vector3 size)
        {
            if(!UseFits308||Fits308==null||string.IsNullOrEmpty(prototypeId))return null;
            foreach(var f in Fits308)
            {
                if(f?.PrototypeIds==null)continue;
                foreach(var id in f.PrototypeIds)
                {
                    if(!string.Equals(id,prototypeId,StringComparison.Ordinal))continue;
                    var d=f.Size-size;
                    return Mathf.Abs(d.x)<=FitSizeTolerance308&&Mathf.Abs(d.y)<=FitSizeTolerance308&&Mathf.Abs(d.z)<=FitSizeTolerance308?f:null;
                }
            }
            return null;
        }
    }
}
