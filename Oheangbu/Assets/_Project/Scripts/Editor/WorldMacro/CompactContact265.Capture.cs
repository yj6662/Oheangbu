using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Oheangbu.App.World;
using Sheet=Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  static string ContactBendCapture265()
  {
   var scene=FrontageScene249();var hub=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CompactEnvironmentContact265>()).Single();var art=hub.Art;var observer=art.Observer;var ground=FinalSurface(scene);
   var ids=art.Sheet.Prototypes.Where(p=>p.Category==Sheet.Kind.Grass).Select(p=>p.Id).ToHashSet();var item=art.Sheet.FixedPlacements.Where(p=>ids.Contains(p.PrototypeId)).OrderBy(p=>Vector3.Distance(p.Position,new Vector3(3100,50,2250))).First();var p=item.Position;
   var go=new GameObject("265 offscreen grass"){hideFlags=HideFlags.HideAndDontSave};var cam=go.AddComponent<Camera>();cam.CopyFrom(hub.Session.Walker.ViewCamera);cam.enabled=false;EditorUtility.CopySerialized(hub.Session.Walker.ViewCamera.GetUniversalAdditionalCameraData(),cam.GetUniversalAdditionalCameraData());go.AddComponent<Skybox>().material=AssetDatabase.LoadAssetAtPath<Material>(Folder263+"/Sky.asset");
   var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var previous=RenderTexture.active;var pulses=(Vector4[])hub.BendPoints.Clone();Color32[] before=null;int changed=0;
   try{art.Observer=cam;art.ContactPreview265=true;art.ContactPreviewClock265=10;cam.targetTexture=rt;cam.aspect=16f/9;cam.fieldOfView=55;var eye=ground(p.x+1.2f,p.z-2.1f).point+Vector3.up*.9f;cam.transform.SetPositionAndRotation(eye,Quaternion.LookRotation(p+Vector3.up*.22f-eye));
    for(int i=0;i<2;i++){Array.Clear(hub.BendPoints,0,hub.BendPoints.Length);if(i==1)hub.BendPoints[0]=new Vector4(p.x-.35f,p.y,p.z,1.6f);cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(ContactOut265+"/grass-"+i+".png",tex.EncodeToPNG());var pixels=tex.GetPixels32();if(i==0)before=pixels;else for(int j=0;j<pixels.Length;j++)if(Math.Abs(pixels[j].r-before[j].r)+Math.Abs(pixels[j].g-before[j].g)+Math.Abs(pixels[j].b-before[j].b)>12)changed++;}
   }finally{Array.Copy(pulses,hub.BendPoints,pulses.Length);art.Observer=observer;art.ContactPreview265=false;art.ContactPreviewClock265=-1;cam.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(tex);Object.DestroyImmediate(go);}
   string result=(changed>10?"PASS ":"FAIL ")+"fixed-camera/fixed-wind render differs with contact pulse; changed pixels="+changed+". Editor stimulus, not player input.";File.WriteAllText(ContactOut265+"/bend-render.txt",result);return result;
  }
 }
}
