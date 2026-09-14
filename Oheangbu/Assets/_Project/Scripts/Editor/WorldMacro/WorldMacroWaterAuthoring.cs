using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.App;
using Oheangbu.App.World;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroWaterAuthoring
    {
        public const string GraphPath = WorldMacroBuilder.Folder + "/MacroRiver.shadergraph";
        public const string MaterialPath = WorldMacroBuilder.Folder + "/MacroRiver.mat";
        static string Output => WorldMacroBuilder.Output + "/WaterSurface";
        static WorldMacroWaterClock Clock => Object.FindFirstObjectByType<WorldMacroWaterClock>();
        static void RequireScene(bool play = false)
        {
            if (Application.isPlaying != play || UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != WorldMacroBuilder.ScenePath)
                throw new Exception("Macro scene required in " + (play ? "Play" : "Edit") + " mode");
        }

        public static void Configure(Transform root, WorldMacroSheetSO sheet)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
            if (shader == null) throw new Exception("Import MacroRiver Shader Graph first");
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
            if (errors.Length != 0) throw new Exception(string.Join("\n", errors.Select(e => e.message)));
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "MacroRiver", enableInstancing = true };
                mat.SetColor("_Deep_Water_Color", new Color(.10f, .135f, .125f, .82f));
                mat.SetColor("_Shallow_Water_Color", new Color(.23f, .29f, .26f, .58f));
                mat.SetFloat("_Amplitude_Wave", .025f);
                mat.SetFloat("_Frequency_Wave", .28f);
                mat.SetFloat("_Speed_Wave", .6f);
                mat.SetFloat("_Refration_Intensity", .003f);
                mat.SetFloat("_Max_Depth", 3f);
                mat.SetFloat("_EffectTime", 0);
                mat.SetFloat("_RippleStrength",2f); mat.SetFloat("_SecondaryRippleStrength",1f);
                mat.SetFloat("_RiverSmoothness",.65f); mat.SetFloat("_UseSceneDepth",0);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            Physics.SyncTransforms();
            WorldMacroWaterGeometry.ClearCache();
            Directory.CreateDirectory(WorldMacroBuilder.Folder + "/WaterSurfaceMeshes");
            var surfaces = new List<Renderer>();
            foreach (var river in sheet.Rivers)
            {
                var child = root.Find("Water_" + river.Id);
                if (child == null) throw new Exception("Missing water surface " + river.Id);
                var mesh = WorldMacroWaterGeometry.Build(sheet, river);
                mesh.name = "RiverSurface_" + river.Id;
                var bounds = mesh.bounds; bounds.Expand(.15f); mesh.bounds = bounds;
                string path = WorldMacroBuilder.Folder + "/WaterSurfaceMeshes/" + mesh.name + ".asset";
                var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                else
                {
                    // Native channel setters invalidate GPU buffers; CopySerialized alone can leave old vertex colours on the GPU.
                    saved.Clear(); saved.indexFormat=mesh.indexFormat; saved.vertices=mesh.vertices;
                    saved.uv=mesh.uv; saved.colors=mesh.colors; saved.normals=mesh.normals; saved.tangents=mesh.tangents;
                    saved.triangles=mesh.triangles; saved.bounds=mesh.bounds; saved.UploadMeshData(false);
                    Object.DestroyImmediate(mesh); EditorUtility.SetDirty(saved);
                }
                child.GetComponent<MeshFilter>().sharedMesh = saved;
                var renderer = child.GetComponent<MeshRenderer>(); renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                surfaces.Add(renderer);
            }
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/geometry.json", JsonUtility.ToJson(new GeometryReports { rivers = WorldMacroWaterGeometry.Reports.Values.ToArray() }, true));
            var clock = root.GetComponent<WorldMacroWaterClock>();
            if (clock == null) clock = root.gameObject.AddComponent<WorldMacroWaterClock>();
            clock.Source = mat; clock.Surfaces = surfaces.ToArray(); clock.ApplyTime(0);
            EditorUtility.SetDirty(clock);
            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.requiresDepthTexture = true; data.requiresColorTexture = true;
                EditorUtility.SetDirty(data);
            }
        }

        [Serializable] public class GeometryReports { public WorldMacroWaterGeometry.BuildReport[] rivers; }

        public static string Install()
        {
            RequireScene(); Directory.CreateDirectory(Output);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string backup = Output + "/BeforeWaterSurface.unity";
            if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);
            var root = GameObject.Find("02_Drainage_WaterSurface");
            if (root == null) throw new Exception("Drainage surface root missing");
            Configure(root.transform, WorldMacroBuilder.Sheet);
            SaveScene();
            return "Installed isolated Shader Graph, fitted river meshes and one shared scaled water clock on six rivers.";
        }
        public static void SaveScene()
        {
            Clock?.RestoreAuthoredMaterial();
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder + "/Sky.mat");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Clock?.Rebind(); Object.FindFirstObjectByType<WorldLookDriver>()?.Apply();
        }

        [Serializable] public class SurfaceStats
        {
            public string name; public int vertices, triangles, missingTangent, missingColor, downwardTriangles;
            public float minimumUvAlong, maximumUvAlong, minimumAlpha, maximumAlpha;
        }
        [Serializable] public class AuditReport
        {
            public string scope = "Authored meshes, shader compilation and explicit scaled-time material sampling; not player walking or swimming.";
            public int shaderErrors, surfaceCount, totalTriangles, materialInstances;
            public bool depthTexture, opaqueTexture, sharedMaterial, noWaterColliders, frozenTimeStable, sourceTimeUnchanged;
            public float maxClockDifference; public SurfaceStats[] surfaces;
        }
        public static string Audit()
        {
            RequireScene(); Directory.CreateDirectory(Output);
            var c = Clock; if (c == null) throw new Exception("Install water first");
            var a = new AuditReport(); var s = new List<SurfaceStats>();
            a.shaderErrors = ShaderUtil.GetShaderMessages(c.Source.shader).Count(m => m.severity.ToString() == "Error");
            float sourceTime = c.Source.GetFloat("_EffectTime");
            var instance = c.Instance;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                for (int frame = 0; frame <= fps * 2; frame++)
                {
                    float t = frame / (float)fps * .25f;
                    c.ApplyTime(t); a.maxClockDifference = Mathf.Max(a.maxClockDifference, Mathf.Abs(c.Instance.GetFloat("_EffectTime") - t));
                }
            }
            c.ApplyTime(.5f); float paused = c.Instance.GetFloat("_EffectTime");
            for (int i = 0; i < 120; i++) c.ApplyTime(.5f);
            a.frozenTimeStable = paused == c.Instance.GetFloat("_EffectTime");
            a.sourceTimeUnchanged = sourceTime == c.Source.GetFloat("_EffectTime");
            a.materialInstances = c.Instance == instance ? 1 : 2;
            a.sharedMaterial = c.Surfaces.All(r => r.sharedMaterial == c.Instance);
            a.noWaterColliders = c.Surfaces.All(r => r.GetComponent<Collider>() == null);
            foreach (var r in c.Surfaces)
            {
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                var v = m.vertices; var uv = m.uv; var tri = m.triangles; var colours = m.colors;
                var stat = new SurfaceStats { name = r.name, vertices = v.Length, triangles = tri.Length / 3,
                    missingTangent = v.Length - m.tangents.Length, missingColor = v.Length - colours.Length,
                    minimumUvAlong = uv.Min(p => p.y), maximumUvAlong = uv.Max(p => p.y),
                    minimumAlpha = colours.Min(p => p.a), maximumAlpha = colours.Max(p => p.a) };
                for (int j = 0; j < tri.Length; j += 3)
                    if (Vector3.Cross(v[tri[j+1]] - v[tri[j]], v[tri[j+2]] - v[tri[j]]).y <= 0) stat.downwardTriangles++;
                a.totalTriangles += stat.triangles; s.Add(stat);
            }
            a.surfaceCount = s.Count; a.surfaces = s.ToArray();
            var data = Camera.main.GetUniversalAdditionalCameraData(); a.depthTexture = data.requiresDepthTexture; a.opaqueTexture = data.requiresColorTexture;
            c.ApplyTime(0);
            File.WriteAllText(Output + "/audit.json", JsonUtility.ToJson(a, true));
            bool pass = a.shaderErrors == 0 && a.surfaceCount == 6 && a.sharedMaterial && a.noWaterColliders && a.depthTexture && a.opaqueTexture
                && a.frozenTimeStable && a.sourceTimeUnchanged && a.materialInstances == 1 && a.maxClockDifference < .000001f
                && s.All(x => x.missingColor == 0 && x.missingTangent == 0 && x.downwardTriangles == 0);
            return $"Water audit {(pass ? "PASS" : "FAIL")}: {a.surfaceCount} surfaces; {a.totalTriangles} triangles; shader errors {a.shaderErrors}; clock error {a.maxClockDifference}";
        }

        [Serializable] public class LiveReport
        {
            public string scope = "Play-mode scaled time and pause observed on the live shared river material, not an input traversal.";
            public float startedGame, startedReal, elapsedGame, elapsedReal, shaderTime, timeError, priorScale;
            public bool paused, pass; public int instanceId;
        }
        static LiveReport live;
        [Serializable] public class DepthReport { public string channels="R raw scene depth, G scene eye depth, B water eye depth, A vertical depth (metres)"; public Color[] samples; }
        public static string DepthProbe()
        {
            RequireScene();if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new Exception("Depth probe stopped: commit >=85%");
            const string code=@"Shader ""Hidden/MacroWaterDepthProbe"" { SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" ""Queue""=""Transparent"" } Pass { Tags { ""LightMode""=""UniversalForward"" } Cull Off ZWrite Off Blend Off HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl""
            struct A { float3 p:POSITION; }; struct V { float4 p:SV_POSITION; float3 w:TEXCOORD0; };
            V vert(A a) { V o; o.w=TransformObjectToWorld(a.p);o.p=TransformWorldToHClip(o.w);return o; }
            float4 frag(V i):SV_Target { float2 uv=GetNormalizedScreenSpaceUV(i.p);float raw=SampleSceneDepth(uv);float3 scene=ComputeWorldSpacePosition(uv,raw,UNITY_MATRIX_I_VP);return float4(raw,LinearEyeDepth(raw,_ZBufferParams),-TransformWorldToView(i.w).z,i.w.y-scene.y); }
            ENDHLSL } } }";
            var shader=ShaderUtil.CreateShaderAsset(code);var material=new Material(shader);
            var go=new GameObject("Temporary_WaterDepthProbe"){hideFlags=HideFlags.HideAndDontSave};var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;
            var view=JsonUtility.FromJson<CaptureInfo>(File.ReadAllText(Output+"/before_shore.json"));
            camera.transform.SetPositionAndRotation(view.position,Quaternion.LookRotation(view.target-view.position));camera.nearClipPlane=.15f;camera.farClipPlane=25000;camera.useOcclusionCulling=false;camera.layerCullDistances=new float[32];
            var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.antialiasing=AntialiasingMode.None;data.requiresDepthTexture=true;data.requiresColorTexture=true;
            var rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGBFloat);var image=new Texture2D(960,540,TextureFormat.RGBAFloat,false,true);var prior=RenderTexture.active;
            try
            {
                foreach(var r in Clock.Surfaces)r.sharedMaterial=material;
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();
                var report=new DepthReport{samples=new[]{image.GetPixel(240,80),image.GetPixel(480,120),image.GetPixel(240,210),image.GetPixel(650,260)}};
                File.WriteAllText(Output+"/depth_probe.json",JsonUtility.ToJson(report,true));return JsonUtility.ToJson(report);
            }
            finally
            {camera.targetTexture=null;RenderTexture.active=prior;Clock.Rebind();rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);Object.DestroyImmediate(go);Object.DestroyImmediate(material);Object.DestroyImmediate(shader);}
        }
        public static string Tune()
        {
            RequireScene();
            var mat=Clock.Source;
            mat.SetFloat("_Max_Depth",3f);mat.SetFloat("_RippleStrength",2.0f);mat.SetFloat("_SecondaryRippleStrength",1.0f);
            mat.SetFloat("_RiverSmoothness",.65f);mat.SetFloat("_Refration_Intensity",.003f);mat.SetFloat("_UseSceneDepth",0);
            EditorUtility.SetDirty(mat);Clock.Instance.CopyPropertiesFromMaterial(mat);Clock.ApplyTime(0);SaveScene();
            return "Updated restrained current and surface highlight settings";
        }
        public static string Dump()
        {
            RequireScene();
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.ShaderGraph.ShaderGraphImporter")).First(t => t != null);
            var method = type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .First(m => m.Name == "GetShaderText" && m.GetParameters().Length == 4 && m.GetParameters()[3].IsOut);
            object[] args = { GraphPath, null, null, null };
            File.WriteAllText(Output + "/MacroRiver.generated.shader", (string)method.Invoke(null, args));
            return "Exported generated shader for inspection; clock=" + Clock.AppliedTime + "; material=" + Clock.Instance.name + "; queue=" + Clock.Instance.renderQueue
                + "; shader="+Clock.Instance.shader.name+"; sceneDepthProperty="+Clock.Instance.HasProperty("_UseSceneDepth")+"; useSceneDepth="+Clock.Instance.GetFloat("_UseSceneDepth")
                + "; opacity="+Clock.Instance.GetFloat("_RiverOpacity")+"; fade="+Clock.Instance.GetFloat("_ShoreFadeDepth")+"; vertexG="+Clock.Surfaces[3].GetComponent<MeshFilter>().sharedMesh.colors.Max(c=>c.g);
        }
        public static string LiveBegin(bool paused)
        {
            RequireScene(true); if (Clock == null) throw new Exception("Water clock missing");
            live = new LiveReport { priorScale = Time.timeScale, paused = paused, startedGame = Time.time, startedReal = Time.realtimeSinceStartup, instanceId = Clock.Instance.GetInstanceID() };
            Time.timeScale = paused ? 0 : .25f;
            return paused ? "Water time paused for observation" : "Water clock running at quarter game speed";
        }
        public static string LiveEnd()
        {
            RequireScene(true); if (live == null) throw new Exception("Begin water live observation first");
            live.elapsedGame = Time.time - live.startedGame; live.elapsedReal = Time.realtimeSinceStartup - live.startedReal;
            live.shaderTime = Clock.Instance.GetFloat("_EffectTime"); live.timeError = Mathf.Abs(live.shaderTime - Time.time);
            live.pass = live.elapsedReal >= 1 && live.timeError < .1f && live.instanceId == Clock.Instance.GetInstanceID()
                && (live.paused ? live.elapsedGame < .1f : live.elapsedGame > 0 && live.elapsedGame < live.elapsedReal * .4f);
            Time.timeScale = live.priorScale;
            File.WriteAllText(Output + (live.paused ? "/live_pause.json" : "/live_slow.json"), JsonUtility.ToJson(live, true));
            return $"Water live {(live.pass ? "PASS" : "FAIL")}; paused={live.paused}; game={live.elapsedGame}s; real={live.elapsedReal}s; clock error={live.timeError}";
        }

        [Serializable] public class CaptureInfo
        { public string id, scope = "Fixed 1920x1080 camera; explicit shader-time still image, not a video."; public Vector3 position, target; public float waterTime, commitRatio; }
        public static string Capture(string id)
        {
            RequireScene(); if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new Exception("Water capture stopped: system commit >=85%");
            Directory.CreateDirectory(Output);
            var sheet = WorldMacroBuilder.Sheet;
            string kind = id.Replace("before_", "").Replace("after_", "").Replace("_time1", "");
            var river = sheet.Rivers.First(r => r.Id == (kind == "shore" ? "SongakCreek" : kind == "bridge" ? "HyeonMain" : "CheongTributary"));
            Vector3 point, tangent;
            if (kind == "confluence") { point = river.Points.Last(); tangent = (point - river.Points[river.Points.Length-2]).normalized; }
            else
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(river.Points.Length * (kind == "shore" ? .47f : .52f)), 1, river.Points.Length-2);
                point = river.Points[i]; tangent = (river.Points[i+1] - river.Points[i-1]).normalized;
            }
            var side = new Vector3(tangent.z, 0, -tangent.x).normalized;
            Vector3 position, target;
            if (kind == "confluence") { position = point + side * 135 - tangent * 130 + Vector3.up * 135; target = point; }
            else if (kind == "bridge")
            {
                var bridge = sheet.Sites.First(s => s.Kind == "Bridge"); point = bridge.Position;
                position = point + new Vector3(60, 24, -60); target = point - Vector3.up * 4;
            }
            else
            {
                position = point + side * (Mathf.Max(24, river.Width) * .5f + 5) - tangent * 7;
                position.y = WorldMacroTerrain.SurfaceHeight(sheet, position.x, position.z) + 1.75f;
                target = point + tangent * 10 + Vector3.up * .3f;
            }
            float effectTime = id.EndsWith("_time1") ? 1.25f : 0;
            var go = new GameObject("Temporary_WaterCapture") { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled = false;
            camera.nearClipPlane = .15f; camera.farClipPlane = 25000; camera.useOcclusionCulling = false; camera.layerCullDistances = new float[32];
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target-position));
            var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.requiresColorTexture = true; data.requiresDepthTexture = true;
            var look = Object.FindFirstObjectByType<WorldLookDriver>(); var prior = RenderTexture.active; RenderTexture rt = null; Texture2D image = null;
            try
            {
                look?.PreviewRegionalSky(position); Clock?.ApplyTime(effectTime);
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32); image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0,0,1920,1080),0,0); image.Apply();
                File.WriteAllBytes(Output + "/" + id + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null; RenderTexture.active = prior;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (image != null) Object.DestroyImmediate(image);
                Object.DestroyImmediate(go); if(Clock!=null){Clock.Instance.CopyPropertiesFromMaterial(Clock.Source);Clock.Rebind();Clock.ApplyTime(0);} look?.Apply();
            }
            var info = new CaptureInfo { id = id, position = position, target = target, waterTime = effectTime, commitRatio = Prologue.PrologueAudit.CommitRatio() };
            File.WriteAllText(Output + "/" + id + ".json", JsonUtility.ToJson(info, true));
            return $"{id} 1920x1080, shader time={effectTime}, commit={info.commitRatio}";
        }
    }
}
