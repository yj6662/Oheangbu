using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static class CodexMountainLOD
    {
        public const string Folder = CodexWorldSceneBuilder.AssetFolder + "/MountainLOD";
        public static MeshRenderer[] Originals() => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r=>r.GetComponent<MeshFilter>()?.sharedMesh!=null && AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh)
                .StartsWith(CodexWorldSceneBuilder.MeshFolder+"/Ridge_", StringComparison.Ordinal)).ToArray();

        [MenuItem("Oheangbu/Dev/Codex World/Rebuild mountain LODs")]
        private static void InstallMenu() => Debug.Log(Install());

        public static string Install()
        {
            var scene=SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path!=CodexWorldSceneBuilder.ScenePath || scene.isDirty)
                return "FAIL: open and save C2 in Edit mode first";
            var data=AssetDatabase.LoadAssetAtPath<CodexWorldSettingsSO>(CodexWorldSceneBuilder.SettingsPath);
            var originals=Originals();
            if (data==null || originals.Length!=9) return "FAIL: expected settings and 9 source mountains";
            foreach(var r in originals) Configure(r,data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return Validate();
        }

        public static void Configure(MeshRenderer original, CodexWorldSettingsSO data)
        {
            var source=original.GetComponent<MeshFilter>().sharedMesh;
            DevSceneKit.EnsureFolder(Folder);
            var renderers=new Renderer[] {original,null,null};
            for(int level=1;level<=2;level++)
            {
                var stride=level==1?data.MountainLod1Stride:data.MountainLod2Stride;
                var reduced=Reduce(source,data.MountainResolution,stride);
                string path=Folder+"/"+source.name+"_LOD"+level+".asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null) { mesh=reduced; AssetDatabase.CreateAsset(mesh,path); }
                else
                {
                    mesh.Clear(); mesh.indexFormat=reduced.indexFormat; mesh.vertices=reduced.vertices;
                    mesh.normals=reduced.normals; mesh.uv=reduced.uv; mesh.colors=reduced.colors;
                    mesh.triangles=reduced.triangles; mesh.bounds=reduced.bounds; mesh.UploadMeshData(false);
                    Object.DestroyImmediate(reduced); EditorUtility.SetDirty(mesh);
                }
                string name="Mountain_LOD"+level;
                var child=original.transform.Find(name);
                if(child==null)
                {
                    child=new GameObject(name).transform; child.SetParent(original.transform,false);
                    child.gameObject.AddComponent<MeshFilter>(); child.gameObject.AddComponent<MeshRenderer>();
                }
                child.gameObject.layer=original.gameObject.layer;
                child.gameObject.isStatic=original.gameObject.isStatic;
                child.GetComponent<MeshFilter>().sharedMesh=mesh;
                var renderer=child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial=original.sharedMaterial;
                renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
                renderers[level]=renderer;
            }
            var group=original.GetComponent<LODGroup>();
            if(group==null) group=original.gameObject.AddComponent<LODGroup>();
            float first=Mathf.Clamp(data.MountainLodThresholds.x,.02f,.95f);
            float second=Mathf.Clamp(data.MountainLodThresholds.y,.001f,first-.001f);
            var lods=new[] {new LOD(first,new[]{renderers[0]}),new LOD(second,new[]{renderers[1]}),new LOD(0f,new[]{renderers[2]})};
            for(int i=0;i<lods.Length;i++) lods[i].fadeTransitionWidth=i<2?data.MountainLodFadeWidth:0f;
            group.SetLODs(lods);
            group.fadeMode=LODFadeMode.CrossFade;
            group.animateCrossFading=data.MountainLodAnimateCrossFading; // Settles after movement instead of leaving a dithered silhouette at rest.
            group.localReferencePoint=source.bounds.center;
            group.size=source.bounds.size.y; // Range width must not pin a distant range to LOD0.
            group.ForceLOD(-1);
            EditorUtility.SetDirty(group);
        }

        /// <summary>Samples original vertices and shading data, retaining each selected column's crest.</summary>
        private static Mesh Reduce(Mesh source, Vector2Int resolution, Vector2Int step)
        {
            int nx=resolution.x,nz=resolution.y,stride=nz+1;
            var input=source.vertices; var normals=source.normals; var colors=source.colors; var uv=source.uv;
            if(input.Length!=(nx+1)*(nz+1)) throw new InvalidOperationException("Mountain resolution does not match "+source.name);
            int sx=Mathf.Clamp(step.x,1,nx),sz=Mathf.Clamp(step.y,1,nz);
            var vertices=new List<Vector3>(); var outNormals=new List<Vector3>();
            var outColors=new List<Color>(); var outUv=new List<Vector2>(); var triangles=new List<int>();
            var rows=new List<List<(int z,int index)>>();
            for(int x=0;;x=Mathf.Min(x+sx,nx))
            {
                var samples=new SortedSet<int>();
                for(int z=0;z<=nz;z+=sz) samples.Add(z);
                samples.Add(nz);
                int crest=0;
                for(int z=1;z<=nz;z++) if(input[x*stride+z].y>input[x*stride+crest].y) crest=z;
                samples.Add(crest);
                var row=new List<(int,int)>();
                foreach(int z in samples)
                {
                    int index=x*stride+z;
                    row.Add((z,vertices.Count)); vertices.Add(input[index]); outNormals.Add(normals[index]);
                    outColors.Add(colors[index]); outUv.Add(uv[index]);
                }
                rows.Add(row); if(x==nx) break;
            }
            for(int x=0;x<rows.Count-1;x++)
            {
                var a=rows[x];var b=rows[x+1];int i=0,j=0;
                // Stitch unequal crest samples without T-junctions or degenerate triangles.
                while(i<a.Count-1 || j<b.Count-1)
                {
                    if(j==b.Count-1 || (i<a.Count-1 && a[i+1].z<=b[j+1].z))
                    { triangles.Add(a[i].index);triangles.Add(a[i+1].index);triangles.Add(b[j].index);i++; }
                    else { triangles.Add(a[i].index);triangles.Add(b[j+1].index);triangles.Add(b[j].index);j++; }
                }
                int a0=a[0].index,an=a[a.Count-1].index,b0=b[0].index,bn=b[b.Count-1].index;
                triangles.Add(a0);triangles.Add(b0);triangles.Add(bn);
                triangles.Add(a0);triangles.Add(bn);triangles.Add(an);
            }
            var mesh=new Mesh {name=source.name+"_Reduced",indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);mesh.SetNormals(outNormals);mesh.SetColors(outColors);mesh.SetUVs(0,outUv);
            mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            return mesh;
        }

        public static string Validate()
        {
            var originals=Originals();long[] totals=new long[3];int errors=0;
            foreach(var original in originals)
            {
                var group=original.GetComponent<LODGroup>();
                if(group==null) {errors++;continue;}
                var lods=group.GetLODs();
                if(lods.Length!=3 || group.fadeMode!=LODFadeMode.CrossFade || lods[2].screenRelativeTransitionHeight!=0) {errors++;continue;}
                long previous=long.MaxValue;
                for(int l=0;l<3;l++)
                {
                    var renderer=lods[l].renderers.Single();
                    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
                    long count=(long)mesh.GetIndexCount(0)/3;totals[l]+=count;
                    if(count>=previous || renderer.sharedMaterial!=original.sharedMaterial || renderer.GetComponent<Collider>()!=null) errors++;
                    previous=count;
                    foreach(var v in mesh.vertices) if(!float.IsFinite(v.sqrMagnitude)) errors++;
                    foreach(var n in mesh.normals) if(!float.IsFinite(n.sqrMagnitude)||n.sqrMagnitude<.95f) errors++;
                }
            }
            string report=$"{(originals.Length==9 && errors==0 ? "PASS":"FAIL")}: groups={originals.Length}; LODs=3; invalid={errors}; "
                +$"trianglesByLevel={string.Join(",",totals)}; lastLODculled=false; animatedFade={originals.All(r=>r.GetComponent<LODGroup>().animateCrossFading)}";
            Directory.CreateDirectory(CodexWorldAudit.CaptureFolder);
            File.WriteAllText(CodexWorldAudit.CaptureFolder+"/mountain-lod-validation.txt",report);
            return report;
        }
    }
}
