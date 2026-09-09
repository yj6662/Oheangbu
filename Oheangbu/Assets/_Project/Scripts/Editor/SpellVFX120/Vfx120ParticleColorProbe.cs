using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;

namespace Oheangbu.EditorTools.SpellVFX120
{
    // One-shot inspection of the actual native streams behind the blue leaf anomaly.
    // Does not alter palettes, assets, gameplay actors or the saved camera.
    public static class Vfx120ParticleColorProbe
    {
        // Cross-render the same sampled geometry. All overrides belong to the
        // disposable instance; camera and render targets are restored even on failure.
        public static string RibbonCrossRender()
        {
            if (EditorApplication.isPlaying || Camera.main == null) throw new InvalidOperationException("Edit review camera required");
            var camera=Camera.main;
            var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
            string folder=Path.Combine(Vfx120Editor.Output,"RibbonCross_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"));
            Directory.CreateDirectory(folder);
            var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
            var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
            GameObject root=null,primitive=null;Material unlit=null;RenderTexture rt=null;Texture2D read=null;Mesh rebuilt=null;
            var report=new StringBuilder();
            try
            {
                rt=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);rt.Create();
                read=new Texture2D(1280,720,TextureFormat.RGB24,false);
                root=UnityEngine.Object.Instantiate(catalog.Entries[74].Prefab);root.hideFlags=HideFlags.DontSave;
                var fx=root.GetComponent<Vfx120Effect>();fx.PreviewControlled=true;fx.DemonstrationCues=true;
                fx.Begin(new Vector3(0,1,0),GameObject.Find("VFX Target")?.transform,new Vector3(0,1,4),Color.white);
                fx.Sample(fx.Life*.42f);
                var accents=new List<MeshRenderer>();
                foreach(var mr in root.GetComponentsInChildren<MeshRenderer>())if(mr.name.StartsWith("Flecks_"))accents.Add(mr);
                foreach(var mr in accents)
                {
                    var mat=mr.sharedMaterial;
                    report.AppendLine(mr.name+" material="+mat.name+" shader="+mat.shader.name+" supported="+mat.shader.isSupported
                        +" passCount="+mat.passCount+" layer="+mr.gameObject.layer+" cameraMask="+camera.cullingMask+" forceOff="+mr.forceRenderingOff
                        +" active="+mr.gameObject.activeInHierarchy+" shadow="+mr.shadowCastingMode+" renderLayers="+mr.renderingLayerMask);
                    foreach(string key in new[]{"_Alpha","_Erode","_Pattern","_Soft","_Body","_Fluid","_ZWrite"})report.AppendLine(key+"="+mat.GetFloat(key));
                    report.AppendLine("BaseMapST="+mat.GetTextureScale("_BaseMap")+","+mat.GetTextureOffset("_BaseMap"));
                }
                var original=fx.Profile.AccentMesh;
                report.AppendLine("mesh="+AssetDatabase.GetAssetPath(original)+" readable="+original.isReadable+" buffers="+original.vertexBufferCount+" indexFormat="+original.indexFormat);
                foreach(var a in original.GetVertexAttributes())report.AppendLine("attribute="+a);
                for(int s=0;s<original.subMeshCount;s++)
                {
                    var sm=original.GetSubMesh(s);
                    report.AppendLine("submesh="+s+" topology="+sm.topology+" indexStart="+sm.indexStart+" indexCount="+sm.indexCount+" base="+sm.baseVertex+" first="+sm.firstVertex+" vertexCount="+sm.vertexCount);
                }
                var vertices=original.vertices;var indices=original.triangles;
                for(int i=0;i<Mathf.Min(vertices.Length,8);i++)report.AppendLine("vertex="+i+" p="+vertices[i].ToString("F6"));
                report.AppendLine("firstIndices="+string.Join(",",new ArraySegment<int>(indices,0,Mathf.Min(18,indices.Length))));
                Action<string> save=name=>
                {
                    camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                    read.ReadPixels(new Rect(0,0,1280,720),0,0);read.Apply();
                    File.WriteAllBytes(Path.Combine(folder,name+".png"),read.EncodeToPNG());
                };
                save("01_original");
                unlit=new Material(Shader.Find("Universal Render Pipeline/Unlit"));unlit.SetColor("_BaseColor",Color.red);
                unlit.SetFloat("_Cull",0);
                foreach(var mr in accents){mr.sharedMaterial=unlit;mr.SetPropertyBlock(null);}
                save("02_unlit_ribbons");
                fx.Sample(fx.Life*.42f);
                foreach(var mr in accents)mr.sharedMaterial=fx.Profile.InkMaterial;
                primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);primitive.hideFlags=HideFlags.DontSave;
                var cube=primitive.GetComponent<MeshFilter>().sharedMesh;
                primitive.SetActive(false);
                foreach(var mr in accents)mr.GetComponent<MeshFilter>().sharedMesh=cube;
                save("03_ink_cubes");
                foreach(var mr in accents){mr.GetComponent<MeshFilter>().sharedMesh=fx.Profile.AccentMesh;mr.sharedMaterial=unlit;mr.SetPropertyBlock(null);}
                var focus=accents[0].bounds.center;
                camera.transform.position=focus+new Vector3(.45f,.20f,-1.0f);
                camera.transform.LookAt(focus);
                save("04_close_unlit");
                fx.Sample(fx.Life*.42f);
                foreach(var mr in accents)mr.sharedMaterial=fx.Profile.InkMaterial;
                save("05_close_ink");
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                rebuilt=new Mesh{name="Probe_ReconstructedRibbon"};rebuilt.vertices=vertices;rebuilt.triangles=indices;
                rebuilt.colors=original.colors;rebuilt.uv=original.uv;rebuilt.RecalculateNormals();rebuilt.RecalculateBounds();
                foreach(var mr in accents)mr.GetComponent<MeshFilter>().sharedMesh=rebuilt;
                save("06_reconstructed_ink");
                foreach(var mr in accents){mr.sharedMaterial=unlit;mr.SetPropertyBlock(null);}
                save("07_reconstructed_unlit");
                root.transform.position+=Vector3.left*1.5f;
                save("08_reconstructed_clear_space");
                File.WriteAllText(Path.Combine(folder,"probe.txt"),report.ToString());
                return folder;
            }
            finally
            {
                camera.targetTexture=oldTarget;RenderTexture.active=oldActive;
                camera.transform.SetPositionAndRotation(oldPosition,oldRotation);
                if(root!=null)UnityEngine.Object.DestroyImmediate(root);
                if(primitive!=null)UnityEngine.Object.DestroyImmediate(primitive);
                if(unlit!=null)UnityEngine.Object.DestroyImmediate(unlit);
                if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                if(read!=null)UnityEngine.Object.DestroyImmediate(read);
                if(rebuilt!=null)UnityEngine.Object.DestroyImmediate(rebuilt);
            }
        }
        public static string RibbonProbe()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            var text = new StringBuilder();
            foreach (int index in new[] {74,77,105,110,112,113})
            {
                GameObject root=null;
                try
                {
                    root=UnityEngine.Object.Instantiate(catalog.Entries[index].Prefab);root.hideFlags=HideFlags.DontSave;
                    var fx=root.GetComponent<Vfx120Effect>();fx.PreviewControlled=true;fx.DemonstrationCues=true;
                    fx.Begin(new Vector3(0,1,0),GameObject.Find("VFX Target")?.transform,new Vector3(0,1,4),Color.white);
                    fx.Sample(fx.Life*.42f);
                    text.AppendLine("GLYPH="+fx.Profile.Glyph+" age="+fx.Age+" life="+fx.Life+" target="+fx.ReceivedTarget);
                    foreach(var mr in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        if(!mr.name.StartsWith("Flecks_"))continue;
                        var block=new MaterialPropertyBlock();mr.GetPropertyBlock(block);
                        var mesh=mr.GetComponent<MeshFilter>().sharedMesh;
                        var colors=mesh.colors;
                        text.AppendLine(mr.name+" enabled="+mr.enabled+" world="+mr.bounds+" scale="+mr.transform.localScale
                            +" q="+mr.transform.localRotation+" alpha="+block.GetFloat("_Alpha")+" erode="+block.GetFloat("_Erode")
                            +" tint="+block.GetColor("_BaseColor")+" mat="+mr.sharedMaterial.name+" mesh="+mesh.name
                            +" verts="+mesh.vertexCount+" tris="+mesh.triangles.Length/3+" colors="+colors.Length+" c0="+(colors.Length>0?colors[0].ToString():"none"));
                    }
                }
                finally { if(root!=null)UnityEngine.Object.DestroyImmediate(root); }
            }
            string path=Path.Combine(Vfx120Editor.Output,"ribbon_probe.txt");File.WriteAllText(path,text.ToString());return path;
        }
        public static string Run()
        {
            if (EditorApplication.isPlaying || Camera.main == null) throw new InvalidOperationException("Edit review camera required");
            var catalog = AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot + "/Data/VFX120_Catalog.asset");
            GameObject root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(catalog.Entries[10].Prefab);
                root.hideFlags = HideFlags.DontSave;
                var fx = root.GetComponent<Vfx120Effect>(); fx.PreviewControlled = true;
                fx.Begin(new Vector3(0,1,0), GameObject.Find("VFX Target")?.transform, new Vector3(0,1,4), Color.white);
                fx.Sample(1.47f);
                var text = new StringBuilder("Actual 것 particle data at 1.47s\n");
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>())
                {
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    var streams = new List<ParticleSystemVertexStream>(); renderer.GetActiveVertexStreams(streams);
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    text.AppendLine(ps.name + " render=" + renderer.renderMode + " gpuInstancing=" + renderer.enableGPUInstancing
                        + " streams=" + string.Join(",",streams) + " base=" + block.GetColor("_BaseColor") + " shader=" + renderer.sharedMaterial.shader.name);
                    var particles = new ParticleSystem.Particle[ps.particleCount]; ps.GetParticles(particles);
                    for (int i=0;i<Mathf.Min(8,particles.Length);i++)
                        text.AppendLine("particle " + i + " start=" + particles[i].startColor + " current=" + particles[i].GetCurrentColor(ps)
                            + " remaining=" + particles[i].remainingLifetime);
                    if (renderer.mesh != null)
                    {
                        var colors = renderer.mesh.colors;
                        text.AppendLine("mesh=" + renderer.mesh.name + " colors=" + colors.Length + " first=" + (colors.Length>0 ? colors[0].ToString() : "none"));
                    }
                }
                string result=text.ToString();
                File.WriteAllText(Path.Combine(Vfx120Editor.Output,"particle_color_probe.txt"),result);
                return result;
            }
            finally { if(root!=null) UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
