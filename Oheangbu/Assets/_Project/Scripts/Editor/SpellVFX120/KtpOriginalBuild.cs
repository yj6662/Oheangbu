using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Oheangbu.App.SpellVFX120;
namespace Oheangbu.EditorTools.SpellVFX120
{
 public static class KtpOriginalBuild
 {
  const string Pack="Assets/KoreanTraditionalPattern_Effect/Prefabs/";
  const string Folder="Assets/_Project/Art/SpellVFX120/KtpOriginal";
  static readonly string[,] Sources={
   {"Fly/Fly06-01.prefab","Fly/Fly01-01.prefab","Bottom/Bottom12-01.prefab","Bottom/Bottom04-01.prefab"},
   {"Fly/Fly05-01.prefab","Fly/Fly03-01.prefab","Bottom/Bottom05-01.prefab","Bottom/Bottom09-01.prefab"},
   {"Bottom/Bottom12-01.prefab","Fly/Fly10-03.prefab","Bottom/Bottom12-01.prefab","Bottom/Bottom14-01.prefab"},
   {"Fly/Fly08-01.prefab","Fly/Fly07-01.prefab","Bottom/Bottom18-01.prefab","Bottom/Bottom14-01.prefab"},
   {"Bottom/Bottom15-01.prefab","Fly/Fly10-01.prefab","Bottom/Bottom15-01.prefab","Bottom/Bottom15-01.prefab"}};
  [Serializable] class Report{public string glyph,status,bodyBefore,bodyAfter;public string[] sources;public int systems,capacity;public bool sourcePreserved;}
  public static string Build(string glyph)
  {
   if(EditorApplication.isPlayingOrWillChangePlaymode||glyph==null||glyph.Length!=1)throw new Exception("Stopped editor and one glyph required");
   var catalog=AssetDatabase.LoadAssetAtPath<Vfx120Catalog>(Vfx120Editor.AssetRoot+"/Data/VFX120_Catalog.asset");
   var p=catalog.Entries.Single(e=>e.Glyph==glyph).Profile;if(!p.Assigned)throw new Exception("Reserved glyph");
   int initial=(glyph[0]-0xAC00)/588;int family=initial==0?0:initial==2?1:initial==6?2:initial==9?3:4;
   var before=Body(p);var backup=Path.Combine(Vfx120Editor.Output,"OriginalKtpBackups");Directory.CreateDirectory(backup);
   string original=Path.Combine(backup,((int)glyph[0]).ToString("X4")+".asset.txt");if(!File.Exists(original))File.Copy(AssetDatabase.GetAssetPath(p),original);
   if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder(Vfx120Editor.AssetRoot,"KtpOriginal");
   var prefabs=new GameObject[4];var hashes=new string[4];var paths=new string[4];
   for(int role=0;role<4;role++)
   {
    paths[role]=Pack+Sources[family,role];hashes[role]=KtpContactBuild.Hash(paths[role]);
    string output=Folder+"/Original_"+family+"_"+role+".prefab";prefabs[role]=AssetDatabase.LoadAssetAtPath<GameObject>(output);
    if(prefabs[role]==null)
    {
     var source=AssetDatabase.LoadAssetAtPath<GameObject>(paths[role]);if(source==null)throw new Exception(paths[role]);
     string guid=AssetDatabase.AssetPathToGUID(paths[role]);if(!File.ReadAllText("Assets/KoreanTraditionalPattern_Effect/Scenes/Preview.unity").Contains(guid))throw new Exception("Not in Preview "+paths[role]);
     string child=role==1?"Explosion":role==0&&Sources[family,role].StartsWith("Fly/")?"Charge":null;
     if(child!=null)source=source.GetComponentsInChildren<Transform>(true).Single(t=>t.name==child).gameObject;
     var host=new GameObject("Original_"+family+"_"+role);try{var copy=UnityEngine.Object.Instantiate(source,host.transform,false);copy.name=source.name;copy.SetActive(true);copy.transform.localPosition=Vector3.zero;if(child!=null)host.transform.localRotation=Quaternion.Euler(0,90,0);prefabs[role]=PrefabUtility.SaveAsPrefabAsset(host,output);}finally{UnityEngine.Object.DestroyImmediate(host);}
    }
   }
   p.UseOriginalKtp=true;p.NativeCastPrefab=prefabs[0];p.NativeImpactPrefab=prefabs[1];
   if(p.Behavior==Vfx120Behavior.Summon||p.Behavior==Vfx120Behavior.Shield||p.NativeFieldPrefab!=null){bool shield=p.Behavior==Vfx120Behavior.Shield;p.NativeFieldPrefab=prefabs[shield?3:2];p.NativeFieldRole=shield?Vfx120TraditionalMotif.Role.Shield:Vfx120TraditionalMotif.Role.Summon;}
   // Original field now supplies the area motif even for custom body implementations.
   if(glyph=="누"||glyph=="구"){p.NativeFieldPrefab=prefabs[3];p.NativeFieldRole=Vfx120TraditionalMotif.Role.Shield;}
   EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
   bool unchanged=Enumerable.Range(0,4).All(i=>hashes[i]==KtpContactBuild.Hash(paths[i]));if(!unchanged||before!=Body(p))throw new Exception("Body or vendor source changed");
   var ps=prefabs.SelectMany(x=>x.GetComponentsInChildren<ParticleSystem>(true)).ToArray();
   string report=JsonUtility.ToJson(new Report{glyph=glyph,status="BUILT_AWAITING_RUNTIME_AND_USER_REVIEW",bodyBefore=before,bodyAfter=Body(p),sources=paths,systems=ps.Length,capacity=ps.Sum(x=>x.main.maxParticles),sourcePreserved=unchanged},true);
   File.WriteAllText(Path.Combine(Vfx120Editor.Output,"original_build_"+((int)glyph[0]).ToString("X4")+".json"),report);return report;
  }
  static string Body(Vfx120Profile p)=>string.Join("|",new[]{AssetDatabase.GetAssetPath(p.BodyMesh),AssetDatabase.GetAssetPath(p.BodyMaterial),AssetDatabase.GetAssetPath(p.NativeBodyPrefab),AssetDatabase.GetAssetPath(p.BotanicalPrefab),AssetDatabase.GetAssetPath(p.SummonPrefab),p.Duration.ToString("R"),p.Flight.ToString("R"),p.NativeReplaceBody.ToString()});
 }
}
