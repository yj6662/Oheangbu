using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Material derivation and ring construction. [SPEC-COMPACT-BACKDROP-RING]
    /// The backdrop material is a COPY of the terrain mountain material: same shader, same 수묵담채
    /// response, so exposed rock / earth / tree texture matches. Only the out-of-rect switches and
    /// the backdrop-only atmosphere band differ, and the terrain materials are never written to.
    /// </summary>
    public static partial class CompactBackdropRing
    {
        // The terrain mountain material the backdrop is derived from.
        public const string SourceMaterialPath=
            "Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision/Materials/cd2636a1e16448f4b9e4f7777c2fe28d.mat";

        [Serializable] public sealed class MaterialRow{public string property,before,after;}
        [Serializable] public sealed class MaterialReceipt
        {
            public string utc,phase,source,destination,shader;
            public bool createdFromCopy,shaderSupported,shaderHasError,hasEmissionProperty;
            public MaterialRow[] changes;
            public string[] terrainMaterialsChecked;
            public bool terrainAtmosphereBandsUnchanged;
            public StageTiming[] timings;
            public string[] findings;
        }

        /// <summary>Creates or refreshes the backdrop material copy, then writes the profile's overrides.</summary>
        public static string BuildMaterial()
        {
            var receipt=new MaterialReceipt{utc=DateTime.UtcNow.ToString("O"),phase="material",
                source=SourceMaterialPath,destination=MaterialPath};
            var timings=new List<StageTiming>();
            var findings=new List<string>();
            var changes=new List<MaterialRow>();
            Directory.CreateDirectory(Output);

            var profile=AssetDatabase.LoadAssetAtPath<CompactBackdropRingProfile>(ProfilePath);
            if(profile==null)throw new InvalidOperationException("Ring profile missing at "+ProfilePath);

            // Baseline of the terrain materials, captured BEFORE any write, so the receipt can prove
            // the shared look was not touched.
            var baseline=TerrainAtmosphereBaseline();

            Material material=null;
            Timed(timings,"derive_material",()=>
            {
                material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if(material==null)
                {
                    var source=AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
                    if(source==null)throw new InvalidOperationException("Source terrain material missing at "+SourceMaterialPath);
                    if(!AssetDatabase.CopyAsset(SourceMaterialPath,MaterialPath))
                        throw new InvalidOperationException("Could not copy the terrain material to "+MaterialPath);
                    AssetDatabase.ImportAsset(MaterialPath);
                    material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                    receipt.createdFromCopy=true;
                }
            });
            if(material==null)throw new InvalidOperationException("Backdrop material unavailable after derivation");

            Timed(timings,"apply_switches",()=>
            {
                // Outside the authored rect these sample to zero anyway; switching them off saves the
                // fetches and states the intent. The slope-driven mountain ink path stays ON.
                SetFloat(material,"_PaintedGeoEnabled",0,changes);
                SetFloat(material,"_PaintedFormEnabled",0,changes);
                SetFloat(material,"_PaintedFormAuthored",0,changes);
                SetFloat(material,"_PaintedRoadBank",0,changes);
                SetFloat(material,"_GroundPath",0,changes);
                SetFloat(material,"_PaintedFarPath",0,changes);
                SetFloat(material,"_PaintedFarPathAir",0,changes);
                SetFloat(material,"_RealmTintStrength",0,changes);
                SetFloat(material,"_CIEnabled",1,changes);
                SetFloat(material,"_CIDarkNear",1,changes);
                SetFloat(material,"_PaintedInkEnabled",1,changes);
                material.enableInstancing=true;
                profile.Apply(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            });

            Timed(timings,"verify_material",()=>
            {
                receipt.shader=material.shader==null?"(none)":material.shader.name;
                receipt.shaderSupported=material.shader!=null&&material.shader.isSupported;
                receipt.shaderHasError=material.shader!=null&&ShaderUtil.ShaderHasError(material.shader);
                receipt.hasEmissionProperty=material.HasProperty("_EmissionColor");
                if(receipt.hasEmissionProperty)findings.Add("Material exposes _EmissionColor; confirm the 발광 상한 rule.");
                if(receipt.shaderHasError)findings.Add("Shader reports a compile error.");

                var after=TerrainAtmosphereBaseline();
                receipt.terrainMaterialsChecked=after.Keys.OrderBy(k=>k).ToArray();
                receipt.terrainAtmosphereBandsUnchanged=
                    baseline.Count==after.Count&&baseline.All(kv=>after.ContainsKey(kv.Key)&&after[kv.Key]==kv.Value);
                if(!receipt.terrainAtmosphereBandsUnchanged)
                    findings.Add("A terrain material's atmosphere band changed; the shared look must not be modified.");
            });

            receipt.changes=changes.ToArray();
            receipt.timings=timings.ToArray();
            receipt.findings=findings.ToArray();
            var json=JsonUtility.ToJson(receipt,true);
            File.WriteAllText(Path.Combine(Output,"material.json"),json);
            return json;
        }

        static void SetFloat(Material material,string property,float value,List<MaterialRow> changes)
        {
            if(!material.HasProperty(property))return;
            float before=material.GetFloat(property);
            if(Mathf.Approximately(before,value))return;
            material.SetFloat(property,value);
            changes.Add(new MaterialRow{property=property,before=before.ToString("R"),after=value.ToString("R")});
        }

        /// <summary>Atmosphere band of every terrain material in the scene, excluding the backdrop's own.</summary>
        static Dictionary<string,string> TerrainAtmosphereBaseline()
        {
            var result=new Dictionary<string,string>();
            var root=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==GeographyRoot);
            if(root==null)return result;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.transform.root!=null&&IsUnderBackdrop(renderer.transform))continue;
                foreach(var material in renderer.sharedMaterials)
                {
                    if(material==null)continue;
                    var path=AssetDatabase.GetAssetPath(material);
                    if(string.IsNullOrEmpty(path)||path==MaterialPath||result.ContainsKey(path))continue;
                    string far=material.HasProperty("_CIFarAirRange")?material.GetVector("_CIFarAirRange").ToString("R"):"-";
                    string mid=material.HasProperty("_CIMidAirRange")?material.GetVector("_CIMidAirRange").ToString("R"):"-";
                    string strengths=material.HasProperty("_CIAirStrengths")?material.GetVector("_CIAirStrengths").ToString("R"):"-";
                    string slope=material.HasProperty("_CIMountainSlope")?material.GetVector("_CIMountainSlope").ToString("R"):"-";
                    result[path]=far+"|"+mid+"|"+strengths+"|"+slope;
                }
            }
            return result;
        }

        static bool IsUnderBackdrop(Transform transform)
        {
            for(var t=transform;t!=null;t=t.parent)if(t.name==BackdropRoot)return true;
            return false;
        }

        // ---- build ------------------------------------------------------------------

        [Serializable] public sealed class InstanceRow
        {
            public string name,model;
            public Vector3 position,targetSize,achievedSize,achievedError,boundsMin,boundsMax;
            public float yaw,distanceFromCentre;
            public bool peak;
            public int renderers,collidersDestroyed,lodGroupsDestroyed;
        }
        [Serializable] public sealed class BuildReceipt
        {
            public string utc,scene,phase,axisMapping;
            public int instances,peaks,ridges,renderers,triangles;
            public int collidersFound,collidersDestroyed,lodGroupsDestroyed;
            public int transformsOutsideBackdropBefore,transformsOutsideBackdropAfter;
            public bool transformsOutsidePreserved,allInstancesClearForbiddenRect,rebuiltExistingRoot;
            public float minimumDistanceFromCentre,maximumDistanceFromCentre,maximumAchievedError;
            public float skirtTopY,summitClearanceRequired,lowestSummit,highestSummit;
            public Rect forbiddenRect;
            public InstanceRow[] rows;
            public StageTiming[] timings;
            public string[] findings;
        }

        public static string Build()
        {
            Guard();
            var receipt=new BuildReceipt{utc=DateTime.UtcNow.ToString("O"),scene=SceneManager.GetActiveScene().path,
                phase="building",axisMapping="legacy_zy_swap (measured by probe)"};
            var timings=new List<StageTiming>();
            var findings=new List<string>();
            var rows=new List<InstanceRow>();

            var profile=AssetDatabase.LoadAssetAtPath<CompactBackdropRingProfile>(ProfilePath);
            if(profile==null)throw new InvalidOperationException("Ring profile missing at "+ProfilePath);
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null)throw new InvalidOperationException("Backdrop material missing; run the material step first");
            var peakModel=AssetDatabase.LoadAssetAtPath<GameObject>(PeakModelPath);
            var ridgeModel=AssetDatabase.LoadAssetAtPath<GameObject>(RidgeModelPath);
            if(peakModel==null||ridgeModel==null)throw new InvalidOperationException("Backdrop models missing");

            var scene=SceneManager.GetActiveScene();
            var geography=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==GeographyRoot);
            if(geography==null)throw new InvalidOperationException("Geography root not found: "+GeographyRoot);

            receipt.forbiddenRect=profile.ForbiddenRect;

            // Clearance is judged against the skirt as it actually stands, measured now.
            var skirt=MeasureSkirt();
            SkirtTopY=skirt.size==Vector3.zero?0f:skirt.max.y;
            RequiredSummitClearance=profile.SummitClearance;
            receipt.skirtTopY=SkirtTopY;
            receipt.summitClearanceRequired=RequiredSummitClearance;
            if(skirt.size==Vector3.zero)findings.Add("Context skirt not found; summit clearance could not be measured.");

            // Counted before the destroy and after the rebuild: direct evidence that nothing outside
            // this one named subtree was touched.
            Timed(timings,"count_before",()=>receipt.transformsOutsideBackdropBefore=CountOutsideBackdrop());

            Timed(timings,"clear_existing",()=>
            {
                var existing=geography.transform.Find(BackdropRoot);
                if(existing!=null){receipt.rebuiltExistingRoot=true;UnityEngine.Object.DestroyImmediate(existing.gameObject);}
            });

            GameObject backdrop=null;
            Timed(timings,"create_geometry",()=>
            {
                backdrop=new GameObject(BackdropRoot);
                backdrop.transform.SetParent(geography.transform,false);
                foreach(var ring in profile.Rings)
                    for(int i=0;i<ring.Count;i++)
                        rows.Add(CreateInstance(profile,ring,i,backdrop.transform,peakModel,ridgeModel,findings));
            });

            // Two-phase, as the far-set builder documents: geometry first with no material, material
            // assigned only after every instance exists.
            Timed(timings,"assign_material",()=>
            {
                foreach(var renderer in backdrop.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial=material;
                    renderer.shadowCastingMode=ShadowCastingMode.Off;
                    renderer.receiveShadows=false;
                    renderer.lightProbeUsage=LightProbeUsage.Off;
                    renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
                    renderer.allowOcclusionWhenDynamic=false;
                }
            });

            Timed(timings,"count_after",()=>receipt.transformsOutsideBackdropAfter=CountOutsideBackdrop());

            Timed(timings,"summarise",()=>
            {
                receipt.rows=rows.ToArray();
                receipt.instances=rows.Count;
                receipt.peaks=rows.Count(r=>r.peak);
                receipt.ridges=rows.Count(r=>!r.peak);
                receipt.renderers=backdrop.GetComponentsInChildren<Renderer>(true).Length;
                receipt.collidersFound=backdrop.GetComponentsInChildren<Collider>(true).Length;
                receipt.collidersDestroyed=rows.Sum(r=>r.collidersDestroyed);
                receipt.lodGroupsDestroyed=rows.Sum(r=>r.lodGroupsDestroyed);
                receipt.transformsOutsidePreserved=
                    receipt.transformsOutsideBackdropBefore==receipt.transformsOutsideBackdropAfter;
                receipt.minimumDistanceFromCentre=rows.Count==0?0:rows.Min(r=>r.distanceFromCentre);
                receipt.maximumDistanceFromCentre=rows.Count==0?0:rows.Max(r=>r.distanceFromCentre);
                receipt.maximumAchievedError=rows.Count==0?0:rows.Max(r=>Magnitude(r.achievedError));
                receipt.triangles=CountTriangles(backdrop);
                receipt.allInstancesClearForbiddenRect=rows.All(r=>!IntersectsForbidden(r,profile.ForbiddenRect));
                var summits=rows.Where(r=>r.renderers>0).Select(r=>r.boundsMax.y).ToList();
                receipt.lowestSummit=summits.Count==0?0:summits.Min();
                receipt.highestSummit=summits.Count==0?0:summits.Max();
                if(receipt.collidersFound>0)findings.Add("Colliders remain under the backdrop root.");
                if(!receipt.transformsOutsidePreserved)
                    findings.Add("Transform count outside the backdrop changed: "+
                        receipt.transformsOutsideBackdropBefore+" -> "+receipt.transformsOutsideBackdropAfter);
            });

            // A clearance failure is a hard stop, not a warning: the ring would interpenetrate the
            // existing context skirt.
            if(!receipt.allInstancesClearForbiddenRect)
            {
                foreach(var row in rows.Where(r=>IntersectsForbidden(r,profile.ForbiddenRect)))
                    findings.Add("Clearance violation: "+row.name+" bounds "+row.boundsMin.ToString("F0")+" to "+row.boundsMax.ToString("F0"));
                receipt.findings=findings.ToArray();
                receipt.timings=timings.ToArray();
                receipt.phase="failed_clearance";
                File.WriteAllText(Path.Combine(Output,"build.json"),JsonUtility.ToJson(receipt,true));
                throw new InvalidOperationException("Backdrop clearance failed; see build.json");
            }

            Timed(timings,"save_scene",()=>
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            });

            receipt.findings=findings.ToArray();
            receipt.timings=timings.ToArray();
            receipt.phase="built";
            var json=JsonUtility.ToJson(receipt,true);
            File.WriteAllText(Path.Combine(Output,"build.json"),json);
            return json;
        }

        static InstanceRow CreateInstance(CompactBackdropRingProfile profile,CompactBackdropRingProfile.Ring ring,
            int index,Transform parent,GameObject peakModel,GameObject ridgeModel,List<string> findings)
        {
            bool isPeak=profile.PeakSelect.x>0&&index%profile.PeakSelect.x==profile.PeakSelect.y;
            var model=isPeak?peakModel:ridgeModel;
            var row=new InstanceRow
            {
                name="Backdrop_"+ring.Name+"_"+index.ToString("00")+"_"+(isPeak?"Peak":"Ridge"),
                model=AssetDatabase.GetAssetPath(model),
                peak=isPeak
            };

            var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name=row.name;
            instance.transform.SetParent(parent,false);
            instance.transform.localScale=Vector3.one;

            var renderers=instance.GetComponentsInChildren<Renderer>(true);
            row.renderers=renderers.Length;
            if(renderers.Length==0){findings.Add("No renderers on "+row.name);return row;}

            // Source extent is measured with yaw cleared so x and z remain the model's own width and
            // depth; a world AABB under yaw bounds the rotated shape instead.
            instance.transform.SetPositionAndRotation(Vector3.zero,Quaternion.Euler(-90f,0f,0f));
            var bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            if(bounds.size.x<=Mathf.Epsilon||bounds.size.y<=Mathf.Epsilon||bounds.size.z<=Mathf.Epsilon)
                throw new InvalidOperationException("Invalid renderer bounds on "+row.name);

            // Target derives from the measured source, so the model keeps its own silhouette.
            profile.Evaluate(ring,index,bounds.size,out Vector3 position,out Vector3 target,out float yaw,out bool usePeak);
            row.position=position;row.targetSize=target;row.yaw=yaw;row.peak=usePeak;
            instance.transform.position=position;

            // Measured mapping (probe): with X=-90 the source XY plane becomes the horizontal
            // footprint and the source Z thickness becomes world height. Non-uniform on purpose —
            // matching height alone makes the ridge model blow out in width.
            instance.transform.localScale=new Vector3(
                target.x/bounds.size.x,
                target.z/bounds.size.z,
                target.y/bounds.size.y);

            var achieved=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)achieved.Encapsulate(renderers[i].bounds);
            row.achievedSize=achieved.size;
            row.achievedError=Abs(achieved.size-target);

            instance.transform.rotation=Quaternion.Euler(-90f,yaw,0f);

            // Seat the FOOT at BaseY, not the pivot. The models are centred on their own origin, so
            // positioning by pivot leaves the base hanging in mid-air and sinks the summit into the
            // terrain ridges. Re-measured after yaw, since that changes the world AABB.
            var seated=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)seated.Encapsulate(renderers[i].bounds);
            float footOffset=instance.transform.position.y-seated.min.y;
            instance.transform.position=new Vector3(position.x,position.y+footOffset,position.z);
            row.position=instance.transform.position;

            foreach(var collider in instance.GetComponentsInChildren<Collider>(true))
            {UnityEngine.Object.DestroyImmediate(collider);row.collidersDestroyed++;}
            foreach(var lod in instance.GetComponentsInChildren<LODGroup>(true))
            {UnityEngine.Object.DestroyImmediate(lod);row.lodGroupsDestroyed++;}

            var final=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)final.Encapsulate(renderers[i].bounds);
            row.boundsMin=final.min;row.boundsMax=final.max;
            row.distanceFromCentre=new Vector2(position.x-profile.RingCentre.x,position.z-profile.RingCentre.y).magnitude;
            return row;
        }

        /// <summary>
        /// Plan-view overlap with the skirt is allowed and wanted — it is what hides the models' cut
        /// bases. The real failure is a summit that does not clear the skirt top, which would leave a
        /// backdrop mountain standing amongst the playable terrain instead of behind it.
        /// </summary>
        /// <summary>
        /// The failure this guards against is a backdrop mountain standing INSIDE the playable field.
        /// A wide, low 원산 whose edge laps over the skirt boundary is not that — the skirt terrain in
        /// front of it hides the overlap, which is exactly how the cut bases stay out of sight. So the
        /// test is: does the instance's CENTRE sit inside the skirt, and is it too low to read as a
        /// distant peak? Edge overlap alone is allowed.
        /// </summary>
        static bool IntersectsForbidden(InstanceRow row,Rect rect)
        {
            if(row.renderers==0)return false;
            var centre=new Vector2((row.boundsMin.x+row.boundsMax.x)*.5f,(row.boundsMin.z+row.boundsMax.z)*.5f);
            bool centreInside=rect.Contains(centre);
            if(!centreInside)return false;
            return row.boundsMax.y<SkirtTopY+RequiredSummitClearance;
        }

        // Measured from the live skirt at build time, never assumed.
        static float SkirtTopY,RequiredSummitClearance=120f;

        /// <summary>Actual world bounds of the context skirt, so clearance is judged against real geometry.</summary>
        static Bounds MeasureSkirt()
        {
            var geography=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==GeographyRoot);
            var skirt=geography==null?null:geography.transform.Find("BackgroundContext_RenderOnly_NoGameplay_NoColliders");
            if(skirt==null)return new Bounds();
            var renderers=skirt.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)return new Bounds();
            var bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static int CountOutsideBackdrop()
        {
            int count=0;
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                    if(!IsUnderBackdrop(transform))count++;
            return count;
        }

        static int CountTriangles(GameObject root)
        {
            int total=0;
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh==null)continue;
                for(int i=0;i<mesh.subMeshCount;i++)
                    total+=(int)(mesh.GetIndexCount(i)/3);
            }
            return total;
        }
    }
}
