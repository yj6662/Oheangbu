using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Distant backdrop mountain ring. Render-only geometry outside the compact world and its
    /// context skirt; it never touches the height field, roads, rivers, NavMesh or the placed
    /// transforms. Entry points return a JSON receipt. [SPEC-COMPACT-BACKDROP-RING]
    ///
    /// Roslyn script-execute is unavailable in this project, so these are called through
    /// reflection-method-call. Every method returns string for that reason.
    /// </summary>
    public static partial class CompactBackdropRing
    {
        public const string ProfilePath="Assets/_Project/Art/World/WorldCompact/Backdrop/Profile.asset";
        public const string MaterialPath="Assets/_Project/Art/World/WorldCompact/Backdrop/M_BackdropMountain.mat";
        public const string PeakModelPath="Assets/_Project/Art/World/WorldCompact/Backdrop/Models/SM_Mountain_Peak.fbx";
        public const string RidgeModelPath="Assets/_Project/Art/World/WorldCompact/Backdrop/Models/SM_Mountain_Ridge.fbx";
        public const string GeographyRoot="WorldMacro_AuthoredGeography";
        public const string BackdropRoot="BackdropRing_RenderOnly_NoGameplay_NoColliders";
        public const string TerrainGroup="01_GlobalTerrain_IndependentOfRoads";

        public static string Output=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/World/WorldMacro/Compact/Backdrop"));

        [Serializable] public sealed class StageTiming{public string stage,status;public double milliseconds;}

        static void Guard()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().path!=WorldMacroCompactAuthoring.TargetScene)
                throw new InvalidOperationException("Compact Edit scene required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)
                throw new InvalidOperationException("System commit >=85%; stopped before allocation");
            Directory.CreateDirectory(Output);
        }

        public static string Execute(string command)
        {
            // The reflection bridge collapses exceptions into an opaque TargetInvocationException,
            // so surface the real message and stack as the return value instead.
            try{return Dispatch(command);}
            catch(Exception error)
            {
                var inner=error;while(inner.InnerException!=null)inner=inner.InnerException;
                return "{\"status\":\"error\",\"command\":\""+command+"\",\"message\":\""+Escape(inner.Message)+
                    "\",\"stack\":\""+Escape((inner.StackTrace??"").Split('\n').FirstOrDefault()??"")+"\"}";
            }
        }

        static string Escape(string text)=>(text??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ").Replace("\t"," ");

        static string Dispatch(string command)
        {
            switch(command)
            {
                case "import": return Import();
                case "profile": return EnsureProfile();
                case "probe": return Probe();
                case "material": return BuildMaterial();
                case "build": return Build();
                case "pose-clear": return ClearPose();
                case "on": return Toggle("on");
                case "off": return Toggle("off");
                default:
                    if(command.StartsWith("pose:"))return Pose(command.Substring(5));
                    throw new ArgumentException("Unknown backdrop command: "+command);
            }
        }

        // ---- probe ------------------------------------------------------------------
        // Measures, rather than assumes, two things before 36 instances are built:
        //  1. the bounds-normalisation axis mapping ported from the legacy ring builder, and
        //  2. the slope distribution, because the material splits rock from paper by slope angle
        //     and non-uniform scaling distorts normals.

        [Serializable] public sealed class SlopeBins
        {
            public float below12,from12to37,above37,medianDegrees,meanDegrees;
            // Area-weighted quantiles. The rock/paper split must be matched by proportion, not by a
            // scaled angle: the backdrop's own distribution decides which angles carry that share.
            public float p10,p25,p50,p75,p90;
            public int samples;
        }
        [Serializable] public sealed class ProbeRow
        {
            public string model,importedNormals;
            public Vector3 sourceBoundsSize,targetSize,achievedSizeLegacy,achievedErrorLegacy;
            public Vector3 achievedSizeDirect,achievedErrorDirect;
            public string betterMapping;
            public int triangles,vertices,renderers,colliders,lodGroups;
            public bool hasVertexColors;
            public SlopeBins slope;
        }
        [Serializable] public sealed class ProbeReceipt
        {
            public string utc,scene,phase,note;
            public bool profileFound,peakFound,ridgeFound;
            public ProbeRow[] models;
            public SlopeBins terrainSlope;
            public string terrainSlopeSource;
            public Vector2 recommendedMountainSlopeDegrees;
            public string slopeRecommendationBasis;
            public float minimumRingRadius,forbiddenMaxRadius;
            public Vector2 innerClearanceRange,outerClearanceRange;
            public bool allInstancesClearForbiddenRect;
            public int plannedInstances,plannedPeaks,plannedRidges;
            public float plannedMinDistance,plannedMaxDistance;
            public StageTiming[] timings;
            public string[] findings;
        }

        static string Probe()
        {
            Guard();
            var receipt=new ProbeReceipt{utc=DateTime.UtcNow.ToString("O"),scene=SceneManager.GetActiveScene().path,phase="probing"};
            var timings=new List<StageTiming>();
            var findings=new List<string>();

            var profile=AssetDatabase.LoadAssetAtPath<CompactBackdropRingProfile>(ProfilePath);
            receipt.profileFound=profile!=null;
            if(profile==null)
            {
                profile=ScriptableObject.CreateInstance<CompactBackdropRingProfile>();
                findings.Add("Profile asset missing at "+ProfilePath+"; probed with in-memory defaults.");
            }

            var rows=new List<ProbeRow>();
            // Slope sampling needs CPU-side meshes, but the shipped state is non-readable. Raise it
            // only for this measurement and restore it in the finally block below.
            List<string> raised=null;
            try
            {
                Timed(timings,"raise_readable",()=>{raised=RaiseReadable();});
                Timed(timings,"measure_models",()=>
                {
                    rows.Add(ProbeModel(PeakModelPath,profile,profile.Rings.FirstOrDefault(),1,findings));
                    rows.Add(ProbeModel(RidgeModelPath,profile,profile.Rings.FirstOrDefault(),0,findings));
                });
            }
            finally
            {
                var restore=raised;
                Timed(timings,"restore_readable",()=>RestoreReadable(restore));
            }
            receipt.models=rows.ToArray();
            receipt.peakFound=rows.Count>0&&rows[0]!=null&&rows[0].renderers>0;
            receipt.ridgeFound=rows.Count>1&&rows[1]!=null&&rows[1].renderers>0;

            Timed(timings,"terrain_slope_reference",()=>
            {
                receipt.terrainSlope=TerrainSlope(out string source);
                receipt.terrainSlopeSource=source;
            });

            Timed(timings,"recommend_slope_band",()=>RecommendSlope(receipt,rows,findings));
            Timed(timings,"plan_placement",()=>PlanPlacement(profile,receipt,findings));

            receipt.timings=timings.ToArray();
            receipt.findings=findings.ToArray();
            receipt.phase="probed_no_scene_change";
            receipt.note="Measurement only. No scene object was created or modified.";
            var json=JsonUtility.ToJson(receipt,true);
            File.WriteAllText(Path.Combine(Output,"probe.json"),json);
            return json;
        }

        static ProbeRow ProbeModel(string path,CompactBackdropRingProfile profile,CompactBackdropRingProfile.Ring ring,int index,List<string> findings)
        {
            var row=new ProbeRow{model=path};
            var model=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(model==null){findings.Add("Model missing: "+path);return row;}

            var importer=AssetImporter.GetAtPath(path) as ModelImporter;
            row.importedNormals=importer==null?"(no importer)":importer.importNormals.ToString();

            // Instantiate exactly as the builder will: rotation first, unit scale, then measure.
            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                instance.hideFlags=HideFlags.HideAndDontSave;
                // Target is resolved after the source extent is measured, below.
                float yaw=0f;
                Vector3 target=new Vector3(1400,800,1400);
                instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(-90f,yaw,0f));
                instance.transform.localScale=Vector3.one;

                var renderers=instance.GetComponentsInChildren<Renderer>(true);
                row.renderers=renderers.Length;
                row.colliders=instance.GetComponentsInChildren<Collider>(true).Length;
                row.lodGroups=instance.GetComponentsInChildren<LODGroup>(true).Length;
                if(renderers.Length==0){findings.Add("No renderers on "+path);return row;}

                // Measured with yaw temporarily cleared. A world AABB taken under yaw is the
                // bounding box of the ROTATED shape, so its x/z no longer correspond to the model's
                // own width and depth and the two axes cannot be driven independently.
                var keep=instance.transform.rotation;
                instance.transform.rotation=Quaternion.Euler(-90f,0f,0f);
                var bounds=renderers[0].bounds;
                for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
                instance.transform.rotation=keep;
                row.sourceBoundsSize=bounds.size;
                if(ring!=null&&profile!=null)
                {
                    profile.Evaluate(ring,index,bounds.size,out _,out target,out yaw,out _);
                    instance.transform.rotation=Quaternion.Euler(-90f,yaw,0f);
                }
                row.targetSize=target;
                if(bounds.size.x<=Mathf.Epsilon||bounds.size.y<=Mathf.Epsilon||bounds.size.z<=Mathf.Epsilon)
                {
                    findings.Add("Invalid renderer bounds on "+path+": "+bounds.size.ToString("F3"));
                    return row;
                }

                // Legacy mapping ported from the reference builder (note the z/y swap), versus the
                // plain world-space reading. Whichever reproduces the target is the correct one.
                row.achievedSizeLegacy=MeasureWithScale(instance,renderers,new Vector3(
                    target.x/bounds.size.x, target.z/bounds.size.z, target.y/bounds.size.y));
                row.achievedErrorLegacy=Abs(row.achievedSizeLegacy-target);

                row.achievedSizeDirect=MeasureWithScale(instance,renderers,new Vector3(
                    target.x/bounds.size.x, target.y/bounds.size.y, target.z/bounds.size.z));
                row.achievedErrorDirect=Abs(row.achievedSizeDirect-target);

                float legacy=Magnitude(row.achievedErrorLegacy),direct=Magnitude(row.achievedErrorDirect);
                row.betterMapping=legacy<=direct?"legacy_zy_swap":"direct_world";
                if(Mathf.Min(legacy,direct)>1f)
                    findings.Add("Neither mapping reproduced the target within 1 m on "+path+
                        " (legacy err="+legacy.ToString("F3")+", direct err="+direct.ToString("F3")+").");

                // Slope distribution is measured at the chosen scale, since that is what ships.
                var chosen=row.betterMapping=="legacy_zy_swap"
                    ?new Vector3(target.x/bounds.size.x,target.z/bounds.size.z,target.y/bounds.size.y)
                    :new Vector3(target.x/bounds.size.x,target.y/bounds.size.y,target.z/bounds.size.z);
                instance.transform.localScale=chosen;
                row.slope=SampleSlope(instance,out int tris,out int verts,out bool colors);
                row.triangles=tris;row.vertices=verts;row.hasVertexColors=colors;
            }
            finally{UnityEngine.Object.DestroyImmediate(instance);}
            return row;
        }

        /// <summary>Achieved size with yaw cleared, so x/z stay the model's own width and depth.</summary>
        static Vector3 MeasureWithScale(GameObject instance,Renderer[] renderers,Vector3 scale)
        {
            var keep=instance.transform.rotation;
            instance.transform.rotation=Quaternion.Euler(-90f,0f,0f);
            instance.transform.localScale=scale;
            var b=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)b.Encapsulate(renderers[i].bounds);
            instance.transform.rotation=keep;
            return b.size;
        }

        static Vector3 Abs(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
        static float Magnitude(Vector3 v)=>Mathf.Max(v.x,Mathf.Max(v.y,v.z));

        /// <summary>Area-weighted slope of the world-space triangles, matching how the shader reads geometric normals.</summary>
        static SlopeBins SampleSlope(GameObject instance,out int triangles,out int vertices,out bool hasColors)
        {
            triangles=0;vertices=0;hasColors=false;
            var degrees=new List<float>();
            var weights=new List<float>();
            foreach(var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh==null||!mesh.isReadable)continue;
                var verts=mesh.vertices;var tris=mesh.triangles;
                if(mesh.colors!=null&&mesh.colors.Length>0)hasColors=true;
                vertices+=verts.Length;triangles+=tris.Length/3;
                var toWorld=filter.transform.localToWorldMatrix;
                for(int i=0;i+2<tris.Length;i+=3)
                {
                    Vector3 a=toWorld.MultiplyPoint3x4(verts[tris[i]]);
                    Vector3 b=toWorld.MultiplyPoint3x4(verts[tris[i+1]]);
                    Vector3 c=toWorld.MultiplyPoint3x4(verts[tris[i+2]]);
                    Vector3 cross=Vector3.Cross(b-a,c-a);
                    float area=cross.magnitude*.5f;
                    if(area<=1e-6f)continue;
                    Vector3 n=cross/(area*2f);
                    degrees.Add(Mathf.Acos(Mathf.Clamp01(Mathf.Abs(n.y)))*Mathf.Rad2Deg);
                    weights.Add(area);
                }
            }
            return Bin(degrees,weights);
        }

        static SlopeBins Bin(List<float> degrees,List<float> weights)
        {
            var bins=new SlopeBins{samples=degrees.Count};
            if(degrees.Count==0)return bins;
            float total=weights.Sum();
            if(total<=0)return bins;
            float below=0,mid=0,above=0,mean=0;
            for(int i=0;i<degrees.Count;i++)
            {
                float d=degrees[i],w=weights[i];
                mean+=d*w;
                if(d<12f)below+=w; else if(d<37f)mid+=w; else above+=w;
            }
            bins.below12=below/total;bins.from12to37=mid/total;bins.above37=above/total;
            bins.meanDegrees=mean/total;
            var order=Enumerable.Range(0,degrees.Count).OrderBy(i=>degrees[i]).ToList();
            bins.medianDegrees=degrees[order[order.Count/2]];
            bins.p10=Quantile(degrees,weights,order,total,.10f);
            bins.p25=Quantile(degrees,weights,order,total,.25f);
            bins.p50=Quantile(degrees,weights,order,total,.50f);
            bins.p75=Quantile(degrees,weights,order,total,.75f);
            bins.p90=Quantile(degrees,weights,order,total,.90f);
            return bins;
        }

        /// <summary>Area-weighted quantile: the angle below which the given share of surface area lies.</summary>
        static float Quantile(List<float> degrees,List<float> weights,List<int> order,float total,float fraction)
        {
            float cutoff=total*fraction,running=0f;
            for(int i=0;i<order.Count;i++)
            {
                running+=weights[order[i]];
                if(running>=cutoff)return degrees[order[i]];
            }
            return degrees[order[order.Count-1]];
        }

        /// <summary>Reference slope distribution of the existing terrain mountains, for comparison.</summary>
        static SlopeBins TerrainSlope(out string source)
        {
            source="(terrain group not found)";
            var root=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==GeographyRoot);
            var group=root==null?null:root.transform.Find(TerrainGroup);
            if(group==null)return new SlopeBins();

            var degrees=new List<float>();var weights=new List<float>();
            int used=0;
            foreach(Transform child in group)
            {
                var filter=child.GetComponent<MeshFilter>();
                var mesh=filter==null?null:filter.sharedMesh;
                if(mesh==null||!mesh.isReadable)continue;
                var normals=mesh.normals;
                if(normals==null||normals.Length==0)continue;
                // Vertex normals are enough for a distribution reference and avoid reading every triangle
                // of 88 terrain chunks.
                for(int i=0;i<normals.Length;i+=7)
                {
                    Vector3 n=child.TransformDirection(normals[i]).normalized;
                    degrees.Add(Mathf.Acos(Mathf.Clamp01(Mathf.Abs(n.y)))*Mathf.Rad2Deg);
                    weights.Add(1f);
                }
                used++;
                if(used>=12)break;
            }
            source=used+" terrain chunks, every 7th vertex normal";
            return Bin(degrees,weights);
        }

        /// <summary>
        /// Picks the backdrop slope band that reproduces the terrain mountains' rock/paper PROPORTION.
        /// Scaling the terrain angles by the median ratio does not work — it lands past 90 degrees,
        /// which no surface can reach. Matching by area share does, because the band is then read off
        /// the backdrop's own distribution.
        /// </summary>
        static void RecommendSlope(ProbeReceipt receipt,List<ProbeRow> rows,List<string> findings)
        {
            var terrain=receipt.terrainSlope;
            var measured=rows.Where(r=>r!=null&&r.slope!=null&&r.slope.samples>0).Select(r=>r.slope).ToList();
            if(terrain==null||terrain.samples==0||measured.Count==0)
            {
                receipt.slopeRecommendationBasis="insufficient samples";
                return;
            }
            // Share of terrain area that is full paper (below the band) and full rock (above it).
            float paperShare=Mathf.Clamp01(terrain.below12);
            float rockShare=Mathf.Clamp01(terrain.above37);
            float lowerFraction=paperShare;
            float upperFraction=Mathf.Clamp01(1f-rockShare);

            float lower=measured.Average(s=>QuantileOf(s,lowerFraction));
            float upper=measured.Average(s=>QuantileOf(s,upperFraction));
            if(upper<=lower+.5f)upper=lower+.5f;
            receipt.recommendedMountainSlopeDegrees=new Vector2(lower,upper);
            receipt.slopeRecommendationBasis=
                "terrain paper share "+paperShare.ToString("P1")+" and rock share "+rockShare.ToString("P1")+
                " mapped onto the backdrop's own area-weighted quantiles";
            if(upper>88f)
                findings.Add("Recommended upper slope "+upper.ToString("F1")+
                    " deg is near vertical; the backdrop may read as rock almost everywhere even after correction.");
        }

        static float QuantileOf(SlopeBins bins,float fraction)
        {
            // Interpolates the five stored quantiles; enough resolution for a band recommendation.
            var points=new (float f,float v)[]{(.10f,bins.p10),(.25f,bins.p25),(.50f,bins.p50),(.75f,bins.p75),(.90f,bins.p90)};
            if(fraction<=points[0].f)return points[0].v;
            for(int i=1;i<points.Length;i++)
            {
                if(fraction>points[i].f)continue;
                var a=points[i-1];var b=points[i];
                float t=Mathf.InverseLerp(a.f,b.f,fraction);
                return Mathf.Lerp(a.v,b.v,t);
            }
            return points[points.Length-1].v;
        }

        static void PlanPlacement(CompactBackdropRingProfile profile,ProbeReceipt receipt,List<string> findings)
        {
            var rect=profile.ForbiddenRect;
            receipt.forbiddenMaxRadius=new Vector2(Mathf.Max(Mathf.Abs(rect.xMin),Mathf.Abs(rect.xMax)),
                Mathf.Max(Mathf.Abs(rect.yMin),Mathf.Abs(rect.yMax))).magnitude;
            float minRadius=float.MaxValue,maxRadius=0f;
            int peaks=0,ridges=0,total=0;
            bool clear=true;

            for(int r=0;r<profile.Rings.Length;r++)
            {
                var ring=profile.Rings[r];
                float ringMin=float.MaxValue,ringMax=0f;
                for(int i=0;i<ring.Count;i++)
                {
                    bool isPeak=profile.PeakSelect.x>0&&i%profile.PeakSelect.x==profile.PeakSelect.y;
                    var source=receipt.models!=null&&receipt.models.Length>1
                        ?(isPeak?receipt.models[0].sourceBoundsSize:receipt.models[1].sourceBoundsSize)
                        :Vector3.one;
                    profile.Evaluate(ring,i,source,out Vector3 position,out Vector3 target,out _,out bool usePeak);
                    total++;if(usePeak)peaks++;else ridges++;
                    // Conservative footprint: the larger horizontal extent on both axes.
                    float half=Mathf.Max(target.x,target.z)*.5f;
                    var bounds=new Bounds(new Vector3(position.x,0,position.z),new Vector3(half*2f,1f,half*2f));
                    var forbidden=new Bounds(new Vector3(rect.center.x,0,rect.center.y),new Vector3(rect.width,2f,rect.height));
                    if(bounds.Intersects(forbidden))
                    {
                        clear=false;
                        findings.Add("Instance "+ring.Name+"_"+i.ToString("00")+" at "+position.ToString("F0")+
                            " (half extent "+half.ToString("F0")+" m) intersects the forbidden rect.");
                    }
                    float centreDistance=new Vector2(position.x-profile.RingCentre.x,position.z-profile.RingCentre.y).magnitude;
                    ringMin=Mathf.Min(ringMin,centreDistance-half);
                    ringMax=Mathf.Max(ringMax,centreDistance+half);
                }
                if(r==0)receipt.innerClearanceRange=new Vector2(ringMin,ringMax);
                else if(r==1)receipt.outerClearanceRange=new Vector2(ringMin,ringMax);
                minRadius=Mathf.Min(minRadius,ringMin);maxRadius=Mathf.Max(maxRadius,ringMax);
            }

            receipt.minimumRingRadius=minRadius;
            receipt.plannedInstances=total;receipt.plannedPeaks=peaks;receipt.plannedRidges=ridges;
            receipt.plannedMinDistance=minRadius;receipt.plannedMaxDistance=maxRadius;
            receipt.allInstancesClearForbiddenRect=clear;
            if(maxRadius>11000f)
                findings.Add("Planned far extent "+maxRadius.ToString("F0")+" m; confirm against the camera far clip plane.");
        }

        static void Timed(List<StageTiming> timings,string stage,Action action)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            string status="complete";
            try{action();}
            catch(Exception error){status="failed: "+error.Message;throw;}
            finally{watch.Stop();timings.Add(new StageTiming{stage=stage,status=status,milliseconds=watch.Elapsed.TotalMilliseconds});}
        }
    }
}
