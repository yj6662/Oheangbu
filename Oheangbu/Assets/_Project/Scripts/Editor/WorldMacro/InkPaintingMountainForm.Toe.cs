using System;
using System.Collections.Generic;
using System.Linq;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class InkPaintingMountainForm
    {
        // Separate opt-in state: Settings and its existing serialized controls are unchanged.
        [Serializable] public sealed class ToeSettings
        {
            public string RoadId = "Road_Post_Merchant";
            public float ZMin = -250, ZMax = 50, CentreZ = -100, AlongRadius = 150;
            public float RoadMinimumX = 730, RoadMaximumX = 780;
            public float OutwardStart = 40, OutwardEnd = 200, HorizontalShift = 45, MaximumAdditionalY = 40;
        }
        [Serializable] public sealed class ToeSample
        {
            public Vector3 position; public float shiftedX, baseHeight, targetHeight, additionalY, protectFade, patchFade;
        }
        [Serializable] public sealed class ToeReceipt
        {
            public string algorithmVersion = "collider-base12-toe-remap-v1";
            public string status, roadSource;
            public ToeSettings settings;
            public int baselineControls, baselineTriangles, indexReferences, indexBins, maximumCandidates;
            public int candidates, protectedNodes, changedNodes, missingBaseSamples, globalLimitClamps, lowHeightNodes;
            public float maximumAdditionalY, maximumHorizontalShift, minimumRoadX, maximumRoadX;
            public ToeSample[] strongestSamples;
            public string scope = "Preview-only toe remap of frozen original-source + existing ridge-control collider surface. Shared triangle weights; local additional Y cap40, total original delta cap120, all original protected faces/fades retained. No XZ movement, extra peaks, material change or permanent install.";
        }
        struct ToeTriangle { public Vector3 a, b, c; }
        sealed class ToeSurface
        {
            public readonly List<ToeTriangle> triangles = new List<ToeTriangle>();
            public readonly Dictionary<long,List<int>> bins = new Dictionary<long,List<int>>();
            public int references;
        }
        public static ToeReceipt LastToeReceipt => LastReceipt?.toe;

        public static string PrepareToe(Settings settings, ToeSettings toe)
        {
            ValidateToeSettings(toe);
            if(settings==null || settings.Controls==null || settings.Controls.Length!=12 || settings.DeltaLimit!=120)
                throw new ArgumentException("Toe trial requires the unchanged saved 12-control baseline and totalDelta120.");
            return PrepareInternal(settings, JsonUtility.FromJson<ToeSettings>(JsonUtility.ToJson(toe)));
        }
        public static void ValidateToeSettings(ToeSettings t)
        {
            // This is a bounded candidate, not a general lowland editing API.
            if(t==null || t.RoadId!="Road_Post_Merchant" || t.ZMin!=-250 || t.ZMax!=50 || t.CentreZ!=-100 || t.AlongRadius!=150 ||
                t.RoadMinimumX!=730 || t.RoadMaximumX!=780 || t.OutwardStart!=40 || t.OutwardEnd!=200 || t.HorizontalShift!=45 || t.MaximumAdditionalY!=40)
                throw new ArgumentException("Unexpected toe footprint/settings; preserve the reviewed local candidate.");
        }
        // Pure calculation also exercised by the standalone checks. No Unity native call.
        public static float ToeHorizontalShift(float outward, float z, ToeSettings t)
        {
            if(z<=t.ZMin || z>=t.ZMax || outward<=t.OutwardStart || outward>=t.OutwardEnd)return 0;
            double q=(z-t.CentreZ)/t.AlongRadius, bell=Math.Max(0,1-q*q); bell*=bell;
            double s=Math.Sin(Math.PI*(outward-t.OutwardStart)/(t.OutwardEnd-t.OutwardStart));
            return (float)(t.HorizontalShift*bell*s*s);
        }
        public static float ToeAdditionalY(float baseHeight, float targetHeight, float baseDelta, float protectFade, float patchFade, float totalLimit, float localLimit)
        {
            float wanted=Math.Max(0,Math.Min(localLimit,targetHeight-baseHeight));
            float added=wanted*Math.Max(0,Math.Min(1,protectFade))*Math.Max(0,Math.Min(1,patchFade));
            return Math.Max(0,Math.Min(added,totalLimit-baseDelta));
        }
        static void ApplyToeOffsets(State s, float[] protection)
        {
            ValidateToeSettings(s.toe);
            var t=s.toe; var r=new ToeReceipt {status="preparing",settings=t,baselineControls=s.settings.Controls.Length,
                minimumRoadX=float.PositiveInfinity,maximumRoadX=float.NegativeInfinity}; s.receipt.toe=r;
            var geo=AssetDatabase.LoadAssetAtPath<WorldMacroSheetSO>(s.settings.Geography);
            var road=geo.CompactRoadGrade.Lines.Single(v=>v.Id==t.RoadId);
            r.roadSource=AssetDatabase.GetAssetPath(geo.CompactRoadGrade);
            var frozen=(float[])s.offsets.Clone();
            var baseField=NewField(s.domain,s.nx,s.nz,frozen,"Toe_FrozenBase12Offsets");
            try
            {
                // Bounding rectangle includes both the destination and every west-shifted lookup.
                var area=Rect.MinMaxRect(t.RoadMinimumX+t.OutwardStart-t.HorizontalShift-1,t.ZMin-1,t.RoadMaximumX+t.OutwardEnd+1,t.ZMax+1);
                var surface=BuildToeSurface(s,baseField,area);
                r.baselineTriangles=surface.triangles.Count;r.indexReferences=surface.references;r.indexBins=surface.bins.Count;
                r.maximumCandidates=surface.bins.Count==0?0:surface.bins.Values.Max(v=>v.Count);
                var samples=new List<ToeSample>();
                for(int iz=0;iz<s.nz;iz++)
                {
                    float z=GridPoint(s,0,iz).y;if(z<=t.ZMin||z>=t.ZMax)continue;
                    float roadX=ToeRoadX(road.Points,z,t);r.minimumRoadX=Mathf.Min(r.minimumRoadX,roadX);r.maximumRoadX=Mathf.Max(r.maximumRoadX,roadX);
                    for(int ix=0;ix<s.nx;ix++)
                    {
                        int at=iz*s.nx+ix;var p=GridPoint(s,ix,iz);float shift=ToeHorizontalShift(p.x-roadX,z,t);if(shift<=0)continue;
                        r.candidates++;if(!s.valid[at]){r.missingBaseSamples++;continue;}
                        if(s.protectedNodes[at]){r.protectedNodes++;continue;}
                        float patch=1-Smooth(OutsideRect(s.settings.Patch,p)/s.settings.Feather);
                        float protect=1-Smooth(Mathf.Clamp01(protection[at]*2.5f));if(patch==0||protect==0)continue;
                        if(!TryToeHeight(surface,p.x,z,out float here)||!TryToeHeight(surface,p.x-shift,z,out float there)) {r.missingBaseSamples++;continue;}
                        float added=ToeAdditionalY(here,there,frozen[at],protect,patch,s.settings.DeltaLimit,t.MaximumAdditionalY);
                        if(added<=0)continue;
                        if(there>here && frozen[at]+Mathf.Min(t.MaximumAdditionalY,there-here)*protect*patch>s.settings.DeltaLimit)r.globalLimitClamps++;
                        s.offsets[at]=frozen[at]+added;r.changedNodes++;if(s.heights[at]<210)r.lowHeightNodes++;
                        r.maximumAdditionalY=Mathf.Max(r.maximumAdditionalY,added);r.maximumHorizontalShift=Mathf.Max(r.maximumHorizontalShift,shift);
                        samples.Add(new ToeSample{position=new Vector3(p.x,here,z),shiftedX=p.x-shift,baseHeight=here,targetHeight=there,additionalY=added,protectFade=protect,patchFade=patch});
                    }
                }
                r.strongestSamples=samples.OrderByDescending(v=>v.additionalY).Take(12).ToArray();
                if(r.missingBaseSamples>0)throw new InvalidOperationException("Toe lookup contains missing collider support: "+r.missingBaseSamples);
                if(r.changedNodes==0)throw new InvalidOperationException("Toe is entirely excluded; do not weaken protection to force a result.");
                for(int at=0;at<s.offsets.Length;at++)
                    if(s.protectedNodes[at] && s.offsets[at]!=frozen[at] || s.offsets[at]<frozen[at] || s.offsets[at]-frozen[at]>t.MaximumAdditionalY+.0001f || Mathf.Abs(s.offsets[at])>s.settings.DeltaLimit+.0001f)
                        throw new InvalidOperationException("Toe cap/protection invariant failed.");
                r.status="prepared_local_toe_not_rendered";
            }
            catch {r.status="prepare_failed";throw;}
            finally {Object.DestroyImmediate(baseField);}
        }
        static float ToeRoadX(Vector3[] points,float z,ToeSettings t)
        {
            bool found=false;float result=0;
            for(int i=1;i<points.Length;i++)
            {
                var a=points[i-1];var b=points[i];if(z<Mathf.Min(a.z,b.z)||z>Mathf.Max(a.z,b.z)||Mathf.Abs(b.z-a.z)<.000001f)continue;
                float x=Mathf.Lerp(a.x,b.x,(z-a.z)/(b.z-a.z));if(x<t.RoadMinimumX||x>t.RoadMaximumX)continue;
                if(found&&Mathf.Abs(x-result)>.01f)throw new InvalidOperationException("Ambiguous road branch in toe footprint at Z="+z);
                result=x;found=true;
            }
            if(!found)throw new InvalidOperationException("Missing authored road station in toe footprint at Z="+z);return result;
        }
        static ToeSurface BuildToeSurface(State s,WorldMacroSurfaceDeformationSO field,Rect area)
        {
            var result=new ToeSurface();const float bin=16;
            foreach(var source in s.sources.Where(v=>v.collider!=null))
            {
                var renderer=source.filter.GetComponent<Renderer>();if(renderer==null||!Intersects(area,renderer.bounds))continue;
                // Match BuildMeshes float/local/world operations, rather than raster-grid heights.
                var original=source.original.vertices;var inverse=source.matrix.inverse;
                var final=new Vector3[original.Length];
                for(int i=0;i<original.Length;i++)final[i]=source.matrix.MultiplyPoint3x4(original[i]+inverse.MultiplyVector(Vector3.up*field.SampleDelta(source.world[i].x,source.world[i].z)));
                foreach(var indices in source.indices)for(int i=0;i<indices.Length;i+=3)
                {
                    var a=final[indices[i]];var b=final[indices[i+1]];var c=final[indices[i+2]];var bounds=Expand(TriangleRect(a,b,c),SupportEdgeTolerance);
                    if(!bounds.Overlaps(area))continue;
                    int index=result.triangles.Count;result.triangles.Add(new ToeTriangle{a=a,b=b,c=c});
                    int x0=Mathf.FloorToInt(Mathf.Max(bounds.xMin,area.xMin)/bin),x1=Mathf.FloorToInt(Mathf.Min(bounds.xMax,area.xMax)/bin);
                    int z0=Mathf.FloorToInt(Mathf.Max(bounds.yMin,area.yMin)/bin),z1=Mathf.FloorToInt(Mathf.Min(bounds.yMax,area.yMax)/bin);
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    {
                        long key=Key(x,z);if(!result.bins.TryGetValue(key,out var list))result.bins[key]=list=new List<int>();list.Add(index);result.references++;
                        if(result.references>2000000||list.Count>20000)throw new InvalidOperationException("Toe baseline index exceeded bounded budget.");
                    }
                }
            }
            return result;
        }
        static bool TryToeHeight(ToeSurface surface,float x,float z,out float height)
        {
            height=0;if(!surface.bins.TryGetValue(Key(Mathf.FloorToInt(x/16),Mathf.FloorToInt(z/16)),out var candidates))return false;
            bool found=false,exact=false;double maximum=double.NegativeInfinity;
            foreach(int index in candidates)
            {
                var t=surface.triangles[index];if(!WorldMacroSurfaceDeformationSO.TryTriangleWeights(t.a,t.b,t.c,x,z,out var w))continue;
                if(w.UsedEdgeTolerance&&exact)continue;
                if(!w.UsedEdgeTolerance&&!exact){exact=true;found=false;maximum=double.NegativeInfinity;}
                double y=w.A*t.a.y+w.B*t.b.y+w.C*t.c.y;if(!found||y>maximum){maximum=y;found=true;}
            }
            if(found)height=(float)maximum;return found;
        }
    }
}
