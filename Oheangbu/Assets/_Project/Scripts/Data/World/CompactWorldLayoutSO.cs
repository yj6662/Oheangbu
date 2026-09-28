using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    public enum CompactRouteRole { Main, Exploration, ReturnShortcut, AbilityGate }
    public enum CompactMountainKind { Rounded, High, Background }
    public enum CompactTraversal { Shared, FootOnly }
    [CreateAssetMenu(menuName="Oheangbu/World/Compact Rebuild Layout")]
    public sealed class CompactWorldLayoutSO : ScriptableObject
    {
        [Serializable] public sealed class Place
        {
            public string Id, Realm, Label, Purpose;
            public Vector2 XZ;
            public string InteractionId, EncounterId;
            public float GroundRadius=18;
            public string[] SceneRoots=Array.Empty<string>();
            public string[] InteractionIds=Array.Empty<string>();
            public string[] EncounterIds=Array.Empty<string>();
            public string[] CheckpointIds=Array.Empty<string>();
            public Vector3 SourceAnchor;
            public bool HasSourceBinding;
            public float YawDelta;
            public float SurfaceBlendDistance=240;
        }
        [Serializable] public sealed class Route
        {
            public string Id, From, To;
            public CompactRouteRole Role;
            public string RequiredAbility;
            public bool OneWay;
            public bool GradeForVehicle;
            public CompactTraversal Traversal;
            public Vector2[] Bends=Array.Empty<Vector2>();
            public float Width=5;
        }
        [Serializable] public sealed class Ridge
        {
            public string Id;
            public Vector2[] Spine=Array.Empty<Vector2>();
            public float Height, Width;
            public CompactMountainKind Kind;
        }
        [Serializable] public sealed class Mountain
        {
            public string Id, Realm, Label, EntryPlaceId, SummitRewardId, TempleRewardId;
            public CompactMountainKind Kind=CompactMountainKind.High;
            public bool MainStory;
            public Vector3[] MainPath=Array.Empty<Vector3>(), TemplePath=Array.Empty<Vector3>(), ReturnPath=Array.Empty<Vector3>();
            public Vector3 Foot, Summit, Temple, LiftBase, LiftLanding;
            public float LiftSeconds=5;
            public int RouteRevision;
            public VehicleExclusion[] VehicleExclusions=Array.Empty<VehicleExclusion>();
            public MountainAction[] Actions=Array.Empty<MountainAction>();
            public string BossId, BossStageId, RequiredStageId;
            public string UnlockFact, UnlockSpell, UnlockVirtue;
        }
        [Serializable] public sealed class MountainAction
        {
            public string Id, Title, Text, RecordId;
            public int Reward;
            public string[] RequiredCompleted=Array.Empty<string>(),RequiredDefeated=Array.Empty<string>();
        }
        [Serializable] public sealed class VehicleExclusion
        {
            public Vector3 Centre;
            public Vector3 Size;
            public bool Contains(Vector3 p,float margin=0)=>
                Mathf.Abs(p.x-Centre.x)<=Size.x*.5f+margin &&
                Mathf.Abs(p.z-Centre.z)<=Size.z*.5f+margin &&
                Mathf.Abs(p.y-Centre.y)<=Size.y*.5f+margin;
        }
        public Mountain[] Mountains=Array.Empty<Mountain>();
        // Optional absolute surface. Empty preserves every incumbent layout/query path.
        public TextAsset FinalSurface;
        public Texture2D SurfaceDistribution;
        public int SurfaceWidth, SurfaceHeight;
        public float SurfaceCell=4;
        public string Revision="compact-rebuild-v2";
        public Vector2 Extent=new Vector2(4000,6000);
        public Place[] Places=Array.Empty<Place>();
        public Route[] Routes=Array.Empty<Route>();
        public Ridge[] Ridges=Array.Empty<Ridge>();
        public Vector2[] River=Array.Empty<Vector2>();
        [Serializable] public sealed class Drainage
        {
            public string Id;
            public Vector2[] Centreline=Array.Empty<Vector2>();
            public float HalfWidth;
        }
        public Drainage[] Drainages=Array.Empty<Drainage>();
        [Tooltip("Optional independently authored watershed. Terrain and rendered/query water are generated from this source.")]
        public CompactHydrologySO Hydrology;
        [Serializable] public sealed class RealmArea
        {
            public string Id,Label;
            public Vector2 Centre;
            public Vector2[] Polygon=Array.Empty<Vector2>();
            public Color Tint=Color.gray;
            public float TreeDensity=1;
        }
        public RealmArea[] Realms=Array.Empty<RealmArea>();
        public RealmArea RealmAt(Vector2 p)
        {
            foreach(var area in Realms)
            {
                bool inside=false;var v=area.Polygon;
                for(int i=0,j=v.Length-1;i<v.Length;j=i++)
                    if((v[i].y>p.y)!=(v[j].y>p.y)&&p.x<(v[j].x-v[i].x)*(p.y-v[i].y)/(v[j].y-v[i].y)+v[i].x)inside=!inside;
                if(inside)return area;
            }
            return null;
        }
        public float RiverWidth=18;
        public float BaseHeight=45, ReliefHeight=28, ReliefWavelength=780;
        public float TileSize=500;
        public int TileSegments=64;
        [Tooltip("Optional little-endian float height offsets. Derived relief; POI and route coordinates remain owned here.")]
        public TextAsset MountainRelief;
        public int MountainReliefWidth,MountainReliefHeight;
        public float MountainReliefCell=5;
        public string MountainReliefSource;
    }
}
