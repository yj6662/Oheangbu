using System;
using UnityEngine;

namespace Oheangbu.Data.World
{
    /// <summary>Macro-only environment. Geography remains owned by WorldMacroSheetSO.</summary>
    [CreateAssetMenu(menuName="Oheangbu/World/Macro Dressing")]
    public sealed class WorldMacroDressingSheetSO : ScriptableObject
    {
        public enum Kind { Tree, Shrub, Grass, Rock, Prop }
        [Serializable] public sealed class Part { public Mesh Mesh; public int Submesh; public Material Material; public Matrix4x4 Local=Matrix4x4.identity; }
        [Serializable] public sealed class Level { public Part[] Parts=Array.Empty<Part>(); }
        [Serializable] public sealed class Prototype
        {
            public string Id,SourcePath,SourceHash; public RealmId Realm; public Kind Category; public float Weight=1;
            public Vector2 Scale=new Vector2(.8f,1.2f); public Vector3 Size; public float Radius=.2f;
            public bool WetBank; public Level[] Lods=Array.Empty<Level>();
            public bool GroundPatch,LowInfill; public int BillboardViews=1,PolishVersion;
            public float MaximumAltitude=10000,MaximumSlope=90;
            // Actual selected-LOD bottom vertices, normalized with the render geometry.
            public Vector3[] GroundPoints=Array.Empty<Vector3>();
        }
        [Serializable] public sealed class Cell
        {
            public int X,Z,Owner; public Vector3 Centre; public float MinHeight,MaxHeight;
            // 17x17 heights match the existing sixteen-metre triangle lattice.
            public float[] Heights; // no per-instance transforms are serialized
            // Optional compact-only 2 m lattice. Legacy source cells retain null and
            // continue using their original 16 m interpolation without modification.
            public float[] FineHeights;
            // 64x64 four-metre habitats: low bits 1 dry/2 damp/3 settlement;
            // bits8/16/32/64 independently allow tree/shrub/grass/rock.
            public byte[] Habitat;
            // Optional 4 m open-ground weights. Empty on baseline sheets.
            public byte[] OpenGround;
            // 17x17 x five normalized region weights. Boundary blend +/-150m.
            public byte[] RealmWeights;
        }
        [Serializable] public sealed class PreserveArea
        {
            public string Id; public Vector3 Centre; public Vector2 HalfSize; public float Yaw;
            public bool ExcludeProcedural=true;
            public bool LimitHeight;
            public float MinimumY,MaximumY;
            // Bit per Kind; old sheets continue to exclude all categories.
            public int AffectedKinds=31;
            public bool TypedClearance;
            public Vector4 Padding=new Vector4(1.2f,.55f,.02f,1.2f);
        }
        [Serializable] public sealed class Passage
        {
            public string Id; public Vector3 A,B; public float Width;
        }
        [Serializable] public sealed class StoryCluster
        {
            public string Id,AnchorId,Theme;public RealmId Realm;public Vector3 Centre;public float Radius=80;
            public float TreeDensity=1,ShrubDensity=1,GrassDensity=1,RockDensity=1;
            public string PlacementNote="Environment vignette reservation; no quest or reward added.";
        }
        [Serializable] public sealed class FixedPlacement
        {
            public string Id,ClusterId,PrototypeId;public Vector3 Position,Euler;public float Scale=1;
            // Authoring creates once; later region bakes preserve these exact user-editable transforms.
            public bool Preserve=true;
        }
        public WorldMacroSheetSO Geography;
        // Optional final world-Y translation. Candidate heights/slopes and habitat stay authored.
        public WorldMacroSurfaceDeformationSO SurfaceDeformation;
        public int Seed=20260912,CellSize=256;
        public int PaletteVersion;
        // Opt-in derivative. Serialized baseline sheets retain their previous appearance.
        public bool DenseVegetation;
        public bool UseOpenGround;
        [Range(0,1)] public float OpenGrassDensity=.25f,OpenShrubDensity=.35f;
        // Optional thinning of the existing accepted population, including deep forest/wet banks.
        // X tree / Y shrub / Z grass (tall, low infill and distant cover); rocks and fixed placements remain.
        public bool UseSpeciesRetention;
        public Vector3 SpeciesRetention=Vector3.one;
        public int DenseRealmMask=31;
        public float DenseTreeSpacing=7.5f,DenseShrubSpacing=4.5f,DenseGrassSpacing=1.6f;
        public float GrassMeshDistance=50,GroundCoverDistance=420,GroundCoverSpacing=8;
        // A separate seeded layer preserves every existing tall-vegetation placement.
        public bool LowGrassInfill;
        public float LowGrassSpacing=1.25f,LowGrassDensity=.9f,LowGrassMeshDistance=20,LowGrassDistance=110;
        public Vector4 DensityGain=new Vector4(1.45f,1.6f,1.8f,1);
        public Passage[] Passages=Array.Empty<Passage>();
        public float RegionBlend=300,GrassDistance=80,ShrubDistance=220,TreeDistance=800,ForestDistance=3200;
        public float TreeSpacing=11,ShrubSpacing=8,GrassSpacing=2.5f,RockSpacing=24;
        public float TreeNear=65,TreeMiddle=180,CollisionRadius=38,VehiclePredictionSeconds=3;
        public int CellsPerFrame=1,ColliderPoolSize=384;
        public float[] RealmDensity={.9f,.58f,.52f,.65f,.76f}; // RealmId order, not polygon order
        public string[] CompletedRegions=Array.Empty<string>();
        public Prototype[] Prototypes=Array.Empty<Prototype>();
        public Cell[] Cells=Array.Empty<Cell>();
        public PreserveArea[] PreservedAreas=Array.Empty<PreserveArea>();
        public StoryCluster[] StoryClusters=Array.Empty<StoryCluster>();
        public FixedPlacement[] FixedPlacements=Array.Empty<FixedPlacement>();
        public string SourceFingerprint;
        // #307 SPEC-ART-RENDERER-CULLING S8 (v2, user-approved 2026-09-30). TEST defaults: sheets saved before #307 read these
        // code values, protected sheets are never rewritten. Read by CompactRebuildArtRenderer (grass reads the bounds slack).
        // ScreenIdenticalCulling307: true = S8 screen-identical path, false = S3 output-identical path (A/B, pixel check).
        public bool ScreenIdenticalCulling307=true;
        // CasterMargin(instance) = clamp((caster top above its base + relief) / tan(sun elevation), clamp.x, clamp.y), at least
        // the punctual reach. Elevation = this assumed value, or the live RenderSettings.sun when that is lower. Shadow On/ShadowsOnly only when
        // the caster's horizontal distance <= min(pipeline shadowDistance, camera far) + CasterMargin.
        public float ShadowSunMinElevation307=25;      // degrees
        public float ShadowReliefAllowance307=12;      // m a shadow receiver may lie below the caster's base (slopes)
        public float ShadowPunctualReach307=24;        // m: point/spot (lantern) shadow casters sit within the light range of the receiver
        public Vector2 ShadowCasterMarginClamp307=new Vector2(8,240);
        public const float DefaultCullBoundsSlack307=.5f;
        public float CullBoundsSlack307=DefaultCullBoundsSlack307;  // m added to every render/chunk bound (view test, worldBounds)
        // S8 chunk merge (#307 A/B: one chunk per 64 m cell and batch = thousands of draws, 3-5 ms submit). The next cell joins the
        // current chunk while it holds fewer than ChunkMinInstances307 or the union stays within ChunkMaxSpan307 m (XZ); 1 / 0 = per cell.
        public int ChunkMinInstances307=64;
        public float ChunkMaxSpan307=192;
        // fine shadow cells (#307 GPU: coarse chunks were drawn whole into every cascade and lantern cube face): cells within this
        // XZ distance of the eye, and (when on) cells within a shadowed lantern's range + ShadowPunctualReach307, are never merged
        public float FineShadowRadius307=96;
        public bool FineShadowLanterns307=true;
        // #307 2c: the S8 lists are collected with this safety band (every box padded, frustum widened) and reused while the camera stays
        // within it; extra instances are offscreen or clipped by the shader's distance fade, so frames stay identical. 0 = collect every frame.
        // bandpixel 2026-10-01: NOT screen-identical yet (a banded list keeps LOD parts the exact centre-distance rule drops, and 80 m / limit
        // casters), so it stays off until the vertex shader applies the same per-instance rule; A/B and approval only.
        public float CollectBandMetres307=0f,CollectBandDegrees307=6f;
        // #307 per-prototype tree LOD distances (CompactRebuildArtRenderer): the first entry whose IdContains is part of the prototype
        // Id replaces TreeNear / TreeMiddle (<= 0 keeps the sheet value). Empty = every tree uses the sheet distances (unchanged look).
        [Serializable] public sealed class LodOverride307 { public string IdContains=""; public float TreeNear=-1, TreeMiddle=-1; }
        public LodOverride307[] LodOverrides307=Array.Empty<LodOverride307>();
        // #307 shadow proxy (look change, approval): for prototypes whose Id contains IdContains, LOD0 draws for the camera only and the
        // ProxyLod parts cast LOD0's shadows over LOD0's fade range (e.g. the 88 K-triangle Meshy pine shadows from its 9.7 K LOD1).
        [Serializable] public sealed class ShadowProxy307 { public string IdContains=""; public int ProxyLod=1; }
        public ShadowProxy307[] ShadowProxies307=Array.Empty<ShadowProxy307>();
        public ShadowProxy307 ShadowProxy307For(string id)
        {
            if(ShadowProxies307==null||string.IsNullOrEmpty(id))return null;
            foreach(var o in ShadowProxies307)if(o!=null&&!string.IsNullOrEmpty(o.IdContains)&&id.Contains(o.IdContains))return o;
            return null;
        }
        public LodOverride307 LodOverride307For(string id)
        {
            if(LodOverrides307==null||string.IsNullOrEmpty(id))return null;
            foreach(var o in LodOverrides307)if(o!=null&&!string.IsNullOrEmpty(o.IdContains)&&id.Contains(o.IdContains))return o;
            return null;
        }

        public float OpenGroundDensity(Cell cell,int x,int z,int category)
        {
            if(!UseOpenGround||(category!=1&&category!=2)||cell.OpenGround==null||cell.OpenGround.Length!=4096)return 1;
            float weight=cell.OpenGround[Mathf.Clamp(z,0,63)*64+Mathf.Clamp(x,0,63)]/255f;
            return Mathf.Lerp(1,category==1?OpenShrubDensity:OpenGrassDensity,weight);
        }

        public float RetentionForCategory(int category)
        {
            if(!UseSpeciesRetention||category<0||category>2)return 1;
            return Mathf.Clamp01(category==0?SpeciesRetention.x:category==1?SpeciesRetention.y:SpeciesRetention.z);
        }

        public static bool Excludes(PreserveArea area,Vector3 position,Kind kind)
        {
            if(!area.ExcludeProcedural||(area.AffectedKinds&(1<<(int)kind))==0)return false;
            if(area.LimitHeight&&(position.y<area.MinimumY||position.y>area.MaximumY))return false;
            var q=Quaternion.Euler(0,-area.Yaw,0)*(position-area.Centre);
            float pad=area.TypedClearance?(kind==Kind.Tree?area.Padding.x:kind==Kind.Shrub?area.Padding.y:kind==Kind.Grass?area.Padding.z:area.Padding.w):2;
            return Mathf.Abs(q.x)<area.HalfSize.x+pad&&Mathf.Abs(q.z)<area.HalfSize.y+pad;
        }
        public static float PassageEdgeDistance(Passage route,Vector3 position)
        {
            var a=new Vector2(route.A.x,route.A.z);var b=new Vector2(route.B.x,route.B.z);var p=new Vector2(position.x,position.z);var d=b-a;
            float t=d.sqrMagnitude<.0001f?0:Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude);
            return Vector2.Distance(p,a+d*t)-route.Width*.5f;
        }
        public const int FineHeightResolution = 129;
        public const float FineHeightSpacing = 2f;
        public static bool HasFineHeights(Cell cell) => cell != null && cell.FineHeights != null && cell.FineHeights.Length == FineHeightResolution * FineHeightResolution;
        public static float Height(Cell cell,float x,float z,Vector2 origin)
        {
            if (HasFineHeights(cell))
            {
                float uFine=Mathf.Clamp((x-origin.x)/FineHeightSpacing,0,127.99999f),vFine=Mathf.Clamp((z-origin.y)/FineHeightSpacing,0,127.99999f);
                int ixFine=Mathf.FloorToInt(uFine),izFine=Mathf.FloorToInt(vFine);uFine-=ixFine;vFine-=izFine;int at=izFine*FineHeightResolution+ixFine;
                float aFine=cell.FineHeights[at],bFine=cell.FineHeights[at+1],cFine=cell.FineHeights[at+FineHeightResolution];
                return uFine+vFine<=1?aFine+(bFine-aFine)*uFine+(cFine-aFine)*vFine:cell.FineHeights[at+FineHeightResolution+1]+(cFine-cell.FineHeights[at+FineHeightResolution+1])*(1-uFine)+(bFine-cell.FineHeights[at+FineHeightResolution+1])*(1-vFine);
            }
            float u=Mathf.Clamp((x-origin.x)/16,0,15.99999f),v=Mathf.Clamp((z-origin.y)/16,0,15.99999f);
            int ix=Mathf.FloorToInt(u),iz=Mathf.FloorToInt(v);u-=ix;v-=iz;int a=iz*17+ix;
            float h=cell.Heights[a],b=cell.Heights[a+1],c=cell.Heights[a+17];
            return u+v<=1?h+(b-h)*u+(c-h)*v:cell.Heights[a+18]+(c-cell.Heights[a+18])*(1-u)+(b-cell.Heights[a+18])*(1-v);
        }
        public static float Slope(Cell c,float x,float z,Vector2 origin)
        {return Mathf.Acos(Mathf.Clamp01(Normal(c,x,z,origin).y))*Mathf.Rad2Deg;}
        public static Vector3 Normal(Cell c,float x,float z,Vector2 origin)
        {
            if (HasFineHeights(c))
            {
                float uFine=Mathf.Clamp((x-origin.x)/FineHeightSpacing,0,127.99999f),vFine=Mathf.Clamp((z-origin.y)/FineHeightSpacing,0,127.99999f);
                int ixFine=(int)uFine,izFine=(int)vFine,at=izFine*FineHeightResolution+ixFine;float dxFine,dzFine;
                if(uFine-ixFine+vFine-izFine<=1){dxFine=(c.FineHeights[at+1]-c.FineHeights[at])/FineHeightSpacing;dzFine=(c.FineHeights[at+FineHeightResolution]-c.FineHeights[at])/FineHeightSpacing;}
                else{dxFine=(c.FineHeights[at+FineHeightResolution+1]-c.FineHeights[at+FineHeightResolution])/FineHeightSpacing;dzFine=(c.FineHeights[at+FineHeightResolution+1]-c.FineHeights[at+1])/FineHeightSpacing;}
                return new Vector3(-dxFine,1,-dzFine).normalized;
            }
            float u=Mathf.Clamp((x-origin.x)/16,0,15.99999f),v=Mathf.Clamp((z-origin.y)/16,0,15.99999f);
            int ix=(int)u,iz=(int)v,a=iz*17+ix;float dx,dz;
            if(u-ix+v-iz<=1){dx=(c.Heights[a+1]-c.Heights[a])/16;dz=(c.Heights[a+17]-c.Heights[a])/16;}
            else{dx=(c.Heights[a+18]-c.Heights[a+17])/16;dz=(c.Heights[a+18]-c.Heights[a+1])/16;}
            return new Vector3(-dx,1,-dz).normalized;
        }
        public static uint Hash(int x,int z,int seed)
        {unchecked{uint h=(uint)x*374761393u+(uint)z*668265263u+(uint)seed*2246822519u;h=(h^(h>>13))*1274126177u;return h^(h>>16);}}
        public static float Unit(uint h)=>(h&0xffffff)/16777216f;
    }
}
