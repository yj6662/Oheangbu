using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.World.Dressing;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Repair cached LOD roots without loading source FBXs or rebuilding habitat masks.</summary>
    public static class WorldMacroDressingGrounding
    {
        [Serializable] sealed class Entry { public string id;public float[] before,after;public int retainedStructuralParts,contactSamples; }
        [Serializable] sealed class Report { public string utc,scope="Cached selected-LOD geometry roots only; actual scene traversal remains unverified.";public Entry[] entries; }
        public static string Repair()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Repair cached roots in Edit mode.");
            var sheet=AssetDatabase.LoadAssetAtPath<Sheet>(WorldMacroDressingAuthoring.SheetPath);
            if(sheet==null)throw new InvalidOperationException("Prepare the dressing palette first.");
            var entries=new List<Entry>();
            foreach(var p in sheet.Prototypes)
            {
                if(p.Category!=Sheet.Kind.Tree&&p.Category!=Sheet.Kind.Rock&&p.Category!=Sheet.Kind.Shrub)continue;
                var entry=new Entry{id=p.Id,before=p.Lods.Take(2).Select(Minimum).ToArray()};
                if(p.Category==Sheet.Kind.Tree&&p.Lods.Length>1&&entry.before[1]>entry.before[0]+.15f)
                {
                    var parts=p.Lods[1].Parts.ToList();var range=parts.FirstOrDefault()?.Material;
                    foreach(var source in p.Lods[0].Parts)
                    {
                        if(source.Material==null||source.Material.GetFloat("_AlphaClip")>.5f||PartMinimum(source)>entry.before[0]+.15f)continue;
                        if(parts.Any(v=>v.Mesh==source.Mesh&&v.Submesh==source.Submesh))continue;
                        string path=WorldMacroDressingAuthoring.Folder+"/Materials/"+p.Id+"_RetainedRoot_"+source.Submesh+".mat";
                        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat==null){mat=new Material(source.Material);AssetDatabase.CreateAsset(mat,path);}else mat.CopyPropertiesFromMaterial(source.Material);
                        mat.enableInstancing=true;
                        if(range!=null)foreach(string field in new[]{"_FadeInStart","_FadeInEnd","_FadeOutStart","_FadeOutEnd"})mat.SetFloat(field,range.GetFloat(field));
                        EditorUtility.SetDirty(mat);parts.Add(new Sheet.Part{Mesh=source.Mesh,Submesh=source.Submesh,Local=source.Local,Material=mat});entry.retainedStructuralParts++;
                    }
                    p.Lods[1].Parts=parts.ToArray();
                }
                foreach(var level in p.Lods.Take(2))
                {
                    float minimum=Minimum(level);
                    if(!float.IsFinite(minimum))throw new InvalidOperationException("Invalid root geometry: "+p.Id);
                    if(Mathf.Abs(minimum)>.00001f)foreach(var part in level.Parts)part.Local=Matrix4x4.Translate(Vector3.down*minimum)*part.Local;
                }
                p.GroundPoints=Contacts(p);entry.after=p.Lods.Take(2).Select(Minimum).ToArray();entry.contactSamples=p.GroundPoints.Length;entries.Add(entry);
            }
            EditorUtility.SetDirty(sheet);AssetDatabase.SaveAssets();
            foreach(var renderer in UnityEngine.Object.FindObjectsByType<WorldMacroDressingRenderer>(FindObjectsSortMode.None))renderer.ResetCache();
            string output=WorldMacroBuilder.Output+"/Dressing/grounding_geometry.json";Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(new Report{utc=DateTime.UtcNow.ToString("o"),entries=entries.ToArray()},true));
            return output;
        }
        static float Minimum(Sheet.Level level)=>level.Parts.Min(PartMinimum);
        static float PartMinimum(Sheet.Part part)
        {
            var bounds=part.Mesh.GetSubMesh(part.Submesh).bounds;var m=part.Local;
            return m.MultiplyPoint3x4(bounds.center).y-Mathf.Abs(m.m10)*bounds.extents.x-Mathf.Abs(m.m11)*bounds.extents.y-Mathf.Abs(m.m12)*bounds.extents.z;
        }
        static Vector3[] Contacts(Sheet.Prototype p)
        {
            var points=new List<Vector3>();
            foreach(var level in p.Lods.Take(2))foreach(var part in level.Parts)
            {
                if(p.Category==Sheet.Kind.Tree&&part.Material.GetFloat("_AlphaClip")>.5f)continue;
                if(!part.Mesh.isReadable)throw new InvalidOperationException("Cached mesh needs readable vertices for root verification: "+p.Id);
                var vertices=part.Mesh.vertices;var indices=part.Mesh.GetIndices(part.Submesh);
                float band=p.Category==Sheet.Kind.Rock?Mathf.Max(.1f,p.Size.y*.22f):.12f;
                foreach(int index in indices){var v=part.Local.MultiplyPoint3x4(vertices[index]);if(v.y<=band)points.Add(v);}
            }
            if(points.Count==0)return Array.Empty<Vector3>();
            var result=new List<Vector3>{points.OrderBy(v=>v.y).First()};
            // Bottom-most support in each of eight footprint directions, not fictitious box corners.
            for(int n=0;n<8;n++)
            {
                float angle=n*Mathf.PI*.25f;var direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                result.Add(points.OrderByDescending(v=>Vector3.Dot(direction,v)-v.y*.75f).First());
            }
            return result.Distinct().ToArray();
        }
    }
}
