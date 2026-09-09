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
