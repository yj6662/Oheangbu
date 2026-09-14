using System;
using System.IO;
using System.Linq;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>One still at a time; never moves the player or authors scene contents.</summary>
    public static class WorldMacroLandmarkCapture
    {
        const string RootName = "WorldMacro_Landmarks_Authored";
        const int Width = 1920, Height = 1080;
        const float EyeHeight = 1.62f, AerialWash = .10f;
        static readonly string[] Landmarks = { "Palace", "Fortress", "Cave", "Temple" };
        static string Output => WorldMacroBuilder.Output + "/Landmarks";

        [Serializable] public sealed class CaptureInfo
        {
            public string id, file, caption, utc, scenePath, landmark, view, sourceCamera;
            public string scope = "Current macro scene and copied main-camera rendering settings. A still-image composition, not a player walk, entrance-clearance test, gameplay or content-completion claim.";
            public Vector3 position, target, entryFootPosition, boundsCenter, boundsSize;
            public float eyeHeightM, fieldOfView, nearClipM, farClipM, commitBefore, commitAfter, aerialStandOffM;
            public bool aerialBoundsFit, aerialWashOverride, regionalSkyPreviewRequested;
            public int width = Width, height = Height, rendererCount;
            public string framingNote;
        }

        public static string Capture(string argument)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Landmark capture requires edit mode; it does not drive a live player.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != WorldMacroBuilder.ScenePath) throw new InvalidOperationException("Open the existing macro scene before LandmarkCapture.");
            float commit = Prologue.PrologueAudit.CommitRatio();
            if (commit >= .85f) throw new InvalidOperationException("Landmark capture stopped: system commit >=85%; no new render allocated.");
            string id = argument?.Trim();
            int split = id == null ? -1 : id.LastIndexOf('_');
            string name = split > 0 ? id.Substring(0, split) : "", view = split > 0 ? id.Substring(split+1) : "";
            if (!Landmarks.Contains(name) || (view != "eye" && view != "aerial"))
                throw new ArgumentException("Expected Palace_eye, Palace_aerial, Fortress_eye, Fortress_aerial, Cave_eye, Cave_aerial, Temple_eye or Temple_aerial.");
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
            if (root == null) throw new InvalidOperationException("Missing scene root: " + RootName);
            var landmark = root.transform.Find(name);
            if (landmark == null || !landmark.gameObject.activeInHierarchy) throw new InvalidOperationException("Missing or inactive landmark: " + RootName + "/" + name);
            var entry = landmark.Find("Entry"); var viewTarget = landmark.Find("ViewTarget");
            if (entry == null || viewTarget == null) throw new InvalidOperationException(name + " requires direct child Entry and ViewTarget transforms.");
            var source = Camera.main;
            if (source == null || source.gameObject.scene != scene || source.orthographic)
                throw new InvalidOperationException("A perspective main camera in the current macro scene is required; no substitute lighting or camera is invented.");
            var renderers = landmark.GetComponentsInChildren<Renderer>(false)
                .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException(name + " has no enabled mesh renderers to photograph.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            bool aerial = view == "aerial";
            var target = aerial ? bounds.center : viewTarget.position;
            var position = entry.position + Vector3.up * EyeHeight;
            float fov = source.fieldOfView, standOff = 0;
            bool boundsFit = false;
            if (aerial)
            {
                // 40-100m along the landmark's right/back axes, with a raised eye.
                var right = Vector3.ProjectOnPlane(landmark.right, Vector3.up).normalized;
                var forward = Vector3.ProjectOnPlane(landmark.forward, Vector3.up).normalized;
                if (right.sqrMagnitude < .5f || forward.sqrMagnitude < .5f) throw new InvalidOperationException(name + " is tilted too far for the authored aerial framing convention.");
                for (standOff = 40f; ; standOff = Mathf.Min(100f, standOff+10f))
                {
                    position = target + (right-forward+Vector3.up*.85f)*standOff;
                    float required = RequiredFov(bounds, position, target);
                    if (required <= fov || standOff >= 100f)
                    {
                        fov = Mathf.Max(fov, Mathf.Min(90f, required));
                        boundsFit = required <= fov+.01f;
                        break;
                    }
                }
            }
            if ((target-position).sqrMagnitude < .001f) throw new InvalidOperationException(name + " ViewTarget coincides with the eye position.");
            var info = new CaptureInfo {
                id = id, file = id+".png", caption = Caption(name)+(aerial ? " — 배치 조감" : " — 입구 발 위치 기준 눈높이 1.62m"),
                utc = DateTime.UtcNow.ToString("o"), scenePath = scene.path, landmark = name, view = view, sourceCamera = source.name,
                position = position, target = target, entryFootPosition = entry.position, boundsCenter = bounds.center, boundsSize = bounds.size,
                eyeHeightM = EyeHeight, fieldOfView = fov, nearClipM = source.nearClipPlane, farClipM = source.farClipPlane,
                commitBefore = commit, aerialStandOffM = standOff, aerialBoundsFit = boundsFit, aerialWashOverride = aerial, rendererCount = renderers.Length,
                framingNote = aerial ? (boundsFit ? "Mesh bounds fit the aerial frame with a 10% framing margin." : "At the 100m stand-off / 90-degree FOV cap: some mesh bounds may be cropped; inspect the still.") : "Entry+1.62m aimed at ViewTarget. This does not establish collision-free player access."
            };
            var materials = aerial ? Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.scene == scene && r.enabled && r.gameObject.activeInHierarchy)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("_WashStrength")).Distinct().ToArray() : Array.Empty<Material>();
            var wash = materials.Select(m => m.GetFloat("_WashStrength")).ToArray();
            var look = Object.FindObjectsByType<WorldLookDriver>(FindObjectsSortMode.None).FirstOrDefault(d => d.gameObject.scene == scene);
            var skyBefore = RenderSettings.skybox;
            Material skySnapshot = null; GameObject cameraObject = null; Camera camera = null;
            RenderTexture rt = null; Texture2D image = null; var activeBefore = RenderTexture.active;
            bool asyncBefore=ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                if (skyBefore != null) skySnapshot = new Material(skyBefore) { hideFlags = HideFlags.HideAndDontSave };
                cameraObject = new GameObject("Temporary_LandmarkCapture") { hideFlags = HideFlags.HideAndDontSave };
                camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(source); camera.enabled = false;
                camera.fieldOfView = fov; camera.aspect = Width/(float)Height;
                camera.useOcclusionCulling = false; camera.layerCullDistances = new float[32];
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target-position, Vector3.up));
                var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null) EditorUtility.CopySerialized(sourceData, camera.GetUniversalAdditionalCameraData());
                for (int i = 0; i < materials.Length; i++) materials[i].SetFloat("_WashStrength", AerialWash);
                look?.PreviewRegionalSky(position); info.regionalSkyPreviewRequested = look != null;
                if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("Landmark capture stopped before rendering: system commit >=85%.");
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,Width,Height),0,0); image.Apply(false);
                Directory.CreateDirectory(Output); File.WriteAllBytes(Output+"/"+info.file, image.EncodeToPNG());
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation=asyncBefore;
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = activeBefore;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (image != null) Object.DestroyImmediate(image);
                if (cameraObject != null) Object.DestroyImmediate(cameraObject);
                for (int i = 0; i < materials.Length; i++) if (materials[i] != null) materials[i].SetFloat("_WashStrength", wash[i]);
                if (skyBefore != null && skySnapshot != null) skyBefore.CopyPropertiesFromMaterial(skySnapshot);
                RenderSettings.skybox = skyBefore;
                if (skySnapshot != null) Object.DestroyImmediate(skySnapshot);
                // Do not call WorldLookDriver.Apply(): it also writes the original main camera.
            }
            info.commitAfter = Prologue.PrologueAudit.CommitRatio();
            File.WriteAllText(Output+"/"+id+".json", JsonUtility.ToJson(info, true));
            return id+" 1920x1080: "+Output+"/"+info.file+"; caption/metadata="+id+".json; commit="+info.commitAfter;
        }

        static float RequiredFov(Bounds bounds, Vector3 position, Vector3 target)
        {
            var inverse = Quaternion.Inverse(Quaternion.LookRotation(target-position, Vector3.up));
            float tangent = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z));
                var p = inverse*(corner-position); if (p.z <= .1f) return 180f;
                tangent = Mathf.Max(tangent, Mathf.Abs(p.y)/p.z, Mathf.Abs(p.x)/p.z/(Width/(float)Height));
            }
            return Mathf.Atan(tangent/.9f)*2*Mathf.Rad2Deg;
        }

        static string Caption(string name)
        {
            switch (name) { case "Palace": return "조선식 궁궐"; case "Fortress": return "성곽"; case "Cave": return "동굴"; default: return "사찰"; }
        }
    }
}
