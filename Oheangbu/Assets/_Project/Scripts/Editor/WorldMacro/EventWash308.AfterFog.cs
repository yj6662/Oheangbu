using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 연기를 안개 뒤에 (SPEC-FIELD-FIRST-HOUR-308 §8.14, 질문지 「운해 띠 대 연기 길잡이」 ② for the smoke only, 대리 적용, TEST): the realm
 // fog (Renderer297 CompactMist238, 550) paints the 운해 band over every sky pixel, and a transparent smoke column writes no depth, so a
 // column against the sky vanished under the band (§8.13). Renderer297 already draws layer "VfxAfterFog" after the fog (#300 spell
 // VFX: the default opaque / transparent layer masks leave it out, VfxAfterFog300_Opaque / _Transparent draw it at 550 after
 // CompactMist238). The smoke moves onto that layer: no shader, renderer or material change, the depth test is unchanged.
 // Cost: the smoke takes no distance fog, valley mist or 운해 at all.
 //   wash308-smoke-afterfog[:dry]   every Smoke308 under Finish297_Attraction/Beacon_* and Hearth308/* in W_Demo_Main -> VfxAfterFog
 //        (ledger kind "smoke-layer", before = the old layer). New columns are built on it too (BuildSmoke).
 //   wash308-smoke-afterfog:revert  every Smoke308 on VfxAfterFog back to its recorded layer (Default when none was recorded).
 public static partial class EventWash308
 {
  internal const string SmokeLayerName="VfxAfterFog";   // layer 20, Bolt300Build.AfterFogLayer

  /// <summary>The after-fog layer, or -1 when the project has no such layer.</summary>
  internal static int SmokeLayer()=>LayerMask.NameToLayer(SmokeLayerName);

  // the renderer must really leave the layer out of its own passes and draw it after the fog, or moving the smoke would hide it
  static string AfterFogRendererProblem(int layer)
  {
   var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);if(data==null)return "no "+RendererPath;
   int bit=1<<layer;
   if((data.transparentLayerMask&bit)!=0)return "Renderer297 transparent layer mask still draws "+SmokeLayerName;
   int mist=data.rendererFeatures.FindIndex(f=>f!=null&&f.name=="CompactMist238");if(mist<0)return "Renderer297 has no CompactMist238";
   int late=data.rendererFeatures.FindIndex(f=>f is RenderObjects ro&&f.isActive&&ro.settings.Event==RenderPassEvent.BeforeRenderingPostProcessing
    &&ro.settings.filterSettings.RenderQueueType==RenderQueueType.Transparent&&(ro.settings.filterSettings.LayerMask&bit)!=0);
   if(late<0)return "Renderer297 has no active transparent RenderObjects for "+SmokeLayerName+" at BeforeRenderingPostProcessing";
   return late<mist?"Renderer297 draws "+SmokeLayerName+" before CompactMist238":null;
  }

  static List<Transform> SmokeColumns(UnityEngine.SceneManagement.Scene scene)
  {
   var found=new List<Transform>();
   foreach(var root in scene.GetRootGameObjects().Where(g=>g.name==AttractionRoot||g.name==HearthRoot))
    found.AddRange(root.GetComponentsInChildren<Transform>(true).Where(t=>t.name==SmokeName&&t.GetComponent<ParticleSystem>()!=null));
   return found;
  }

  static string SmokeAfterFog(bool dry)
  {
   var main=HearthScene(!dry);
   int layer=SmokeLayer();if(layer<0)throw new PostLedger308.Refused("no layer named "+SmokeLayerName);
   string problem=AfterFogRendererProblem(layer);if(problem!=null)throw new PostLedger308.Refused(problem);
   var columns=SmokeColumns(main);var todo=columns.Where(t=>t.gameObject.layer!=layer).ToList();
   var sb=new StringBuilder("wash308-smoke-afterfog"+(dry?" (dry)":"")+" | layer "+layer+" "+SmokeLayerName+" | columns "+columns.Count+", to move "+todo.Count+"\n");
   foreach(var t in columns)sb.AppendLine("  "+PostLedger308.PathOf(t)+" layer "+LayerMask.LayerToName(t.gameObject.layer)+(t.gameObject.layer==layer?" (already)":""));
   if(dry||todo.Count==0)return sb.Append(dry?" dry: nothing written":" nothing to move").ToString();
   var ledger=ReadLedger();string utc=PostLedger308.Utc();string backup=PostLedger308.Backup(BackupRoot,utc,main.path);ledger.backups.Add(backup);
   foreach(var t in todo)
   {
    string path=PostLedger308.PathOf(t);int before=t.gameObject.layer;
    t.gameObject.layer=layer;EditorUtility.SetDirty(t.gameObject);
    if(!ledger.changes.Any(c=>c.scene==main.path&&c.kind=="smoke-layer"&&c.path==path))
     ledger.changes.Add(new Change{scene=main.path,kind="smoke-layer",path=path,before=before.ToString(),after=layer.ToString(),utc=utc});
   }
   PostLedger308.SaveScene(main);WriteLedger(ledger);
   return sb.Append(" moved "+todo.Count+"; saved "+PostLedger308.Short(main.path)+"; backup "+backup+"; ledger "+LedgerPath).ToString();
  }

  static string SmokeAfterFogRevert()
  {
   var main=HearthScene(true);
   int layer=SmokeLayer();if(layer<0)throw new PostLedger308.Refused("no layer named "+SmokeLayerName);
   var ledger=ReadLedger();var mine=ledger.changes.Where(c=>c.scene==main.path&&c.kind=="smoke-layer").ToList();
   var late=SmokeColumns(main).Where(t=>t.gameObject.layer==layer).ToList();
   if(late.Count==0&&mine.Count==0)return "wash308-smoke-afterfog:revert: nothing on "+SmokeLayerName+", nothing recorded";
   string utc=PostLedger308.Utc();string backup=PostLedger308.Backup(BackupRoot,utc,main.path);ledger.backups.Add(backup);
   foreach(var t in late)
   {
    string path=PostLedger308.PathOf(t);var c=mine.FirstOrDefault(x=>x.path==path);
    t.gameObject.layer=c!=null&&int.TryParse(c.before,out int before)?before:0;EditorUtility.SetDirty(t.gameObject);
   }
   foreach(var c in mine)ledger.changes.Remove(c);
   PostLedger308.SaveScene(main);WriteLedger(ledger);
   return "wash308-smoke-afterfog:revert: "+late.Count+" columns back under the fog ("+mine.Count+" records removed); backup "+backup+" (new columns are still built on "+SmokeLayerName+" - BuildSmoke)";
  }
 }
}
