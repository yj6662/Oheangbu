using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Material guidance only: follows the accepted fitted road lines, never edits them.
    public static class InkPaintingRoadBankField
    {
        const string Folder="Assets/_Project/Art/World/WorldCompact/InkLandscape/InkPaintingStudy/FlowRevision";
        const string Path=Folder+"/RoadBankField.asset";
        public static string BakeAndBind()
        {
            var scene=SceneManager.GetActiveScene();
            if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!=WorldMacroCompactAuthoring.TargetScene||scene.isDirty||InkPaintingFoliagePreview.IsActive)
                throw new InvalidOperationException("Clean compact Edit scene with previews restored required");
            if(Prologue.PrologueAudit.CommitRatio()>=.85f)throw new InvalidOperationException("Commit limit reached");
            var sheet=CompactRecovery.Find<WorldMacroDressingRenderer>().Sheet.Geography;
            var grade=sheet.CompactRoadGrade;grade.Validate();
            var sourceJson=JsonUtility.ToJson(grade);
            const int width=1024,height=1536;
            var rect=InkPaintingGeographyField.CompactBounds;
            float dx=rect.width/width,dz=rect.height/height;
            var pixels=new Color[width*height];
            int segments=0;
            foreach(var line in grade.Lines)
            for(int segment=1;segment<line.Points.Length;segment++)
            {
                Vector3 a=line.Points[segment-1],b=line.Points[segment];
                var delta=new Vector2(b.x-a.x,b.z-a.z);float length2=delta.sqrMagnitude;
                if(length2<.0001f)continue;
                float inner=line.Width*.5f+5,outer=line.Width*.5f+14;
                int x0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x,b.x)-outer-rect.xMin)/dx),0,width-1);
                int x1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x,b.x)+outer-rect.xMin)/dx),0,width-1);
                int z0=Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.z,b.z)-outer-rect.yMin)/dz),0,height-1);
                int z1=Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.z,b.z)+outer-rect.yMin)/dz),0,height-1);
                for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                {
                    var p=new Vector2(rect.xMin+(x+.5f)*dx,rect.yMin+(z+.5f)*dz);
                    var pa=p-new Vector2(a.x,a.z);float t=Mathf.Clamp01(Vector2.Dot(pa,delta)/length2);
                    float distance=(pa-delta*t).magnitude;
                    float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(inner,outer,distance));
                    int index=z*width+x;
                    if(weight<=pixels[index].r)continue;
                    // Premultiplied height prevents empty pixels dragging interpolation toward Y=0.
                    pixels[index]=new Color(weight,Mathf.Lerp(a.y,b.y,t)*weight,0,1);
                }
                segments++;
            }
            if(sourceJson!=JsonUtility.ToJson(grade))throw new InvalidOperationException("Road grade changed during read-only bake");
            var texture=new Texture2D(width,height,TextureFormat.RGFloat,false,true){name="InkPaintingRoadBankField",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            texture.SetPixels(pixels);texture.Apply(false,false);
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            if(existing==null)AssetDatabase.CreateAsset(texture,Path);
            else{EditorUtility.CopySerialized(texture,existing);UnityEngine.Object.DestroyImmediate(texture);texture=existing;EditorUtility.SetDirty(texture);}
            AssetDatabase.SaveAssetIfDirty(texture);
            int count=0;
            foreach(var path in AssetDatabase.FindAssets("t:Material",new[]{Folder+"/Materials"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(!material.HasProperty("_PaintedRoadBank"))throw new InvalidOperationException("Missing road bank shader property");
                material.SetTexture("_PaintedRoadBankField",texture);
                material.SetVector("_PaintedRoadBankRect",InkPaintingGeographyField.ShaderRect(rect));
                material.SetVector("_PaintedRoadBankParams",new Vector4(2,6,80,250));
                material.SetFloat("_PaintedRoadBank",.96f);
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);count++;
            }
            string report=$"Read {grade.Lines.Length} fitted routes / {segments} segments; bound {count} owned ground materials. Bank: road edge+5..14m, height difference 2..6m, viewing fade 80..250m. No scene/road geometry changed.";
            File.WriteAllText(WorldMacroCompactAuthoring.Output+"/InkLandscape/InkPaintingStudy/FlowRevision/road_bank_field.txt",report);
            return report;
        }
    }
}
