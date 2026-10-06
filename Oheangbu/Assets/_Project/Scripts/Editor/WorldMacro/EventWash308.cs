using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools.WorldMacro
{
 // #308 봉수 소등과 권역 담채 (SPEC-EVENT-WASH-308, D308-6 / D308-6b, TEST). Queue-safe: refusals are "refused: ..." strings, never a dialog.
 //   python Tools/Unity/playtest_polish.py Oheangbu.EditorTools.WorldMacro.EventWash308 Run "<command>"
 //   wash308-beacons[:dry|:revert][:scene=arch296|folk298|main]
 //        Finish297_Attraction/Beacon_*: Halo, Light, Core0–2 SetActive(false); stone LOD0–2 beacon_core slots -> M_BeaconAsh308
 //        (URP Lit like the kit stone slots, no emission, no light marker). Stone, LODGroup, collider, position unchanged.
 //        Never attraction-revert. Ledger Art/Playtest306/Backups/EventWash308/ledger.json, scene backups <utc>/.
 //   wash308-halo-feature[:dry|:revert]   removes Renderer297's BeaconHalo297 feature only when no active renderer in any of the
 //        three scenes draws that pass (Renderer297 backed up first; :revert copies the backup back)
 //   wash308-audit[:scene=..][:label=..]  beacons (AC-W2/W4), halo-pass renderers, persistent emission census vs the ART-INK
 //        allow list and lights (AC-W3) -> Art/Playtest306/Checks/wash308-audit-<scene>[-label].txt
 //   wash308-sheet[:dry|:revert] stage -> catalog place proposal from WorldContent_Main trigger points (W_Demo_Main), and (D308-6b)
 //        the progress volume of every 진행 place baked from the height field Finish297/Surface/height.bytes (floor, top, radius,
 //        NoWash/boss radius limit, ridge exposure from 1/1.5/2 km); without :dry it writes Art/World/Finish297/Data/
 //        EventWashSheet308.asset with ProgressOn true, RewardOn false (only after a human checked the dry run); the sheet is backed
 //        up first. wash308-sheet:revert copies the backup of the latest write back (one write per call, ledger record removed)
 //   wash308-apply[:dry|:revert][:scene=..]  WorldMacro_Playtest/EventWash308 + EventWashDriver308 (Session, Sheet, the scene's
 //        own Catalog and Palette; W_Demo_Main must use the expected catalog/palette or it is refused)
 //   wash308-unit              planner rules, D308-6b meanings, volume slots/groups/distance fade, surface parity with D308-6,
 //        OpticalDepth vs numeric integration, fades, nearest-8, stale Destination, GC 0 B, source checks (AC-W7–W10, W12, W18, W19)
 //   wash308-preview[:location=<id>|:all][:kind=volume|surface][:eye=x,y,z][:scene=..][:off]  edit-mode globals exactly as the
 //        driver would write them with the chosen zones forced available (eye = :eye or the SceneView camera); globals only, no
 //        material, scene or asset. :off puts _OhEventWashCount, _OhEventWashVolA and _OhInkWashDebug308 back to 0 (AC-W15).
 //   wash308-views[:location=<id>]  capture poses per progress volume from the height field: 4 far (best ridge exposure on the
 //        1/1.5/2 km rings), 1 valley (exposure 0), 3 near (level, +30°, −10°), 2 at 30 m looking back at the place (level, +30°)
 //        -> Art/Playtest306/Checks/wash308-views.json
 //   wash308-debug:<0..8>      sets the global _OhInkWashDebug308 (7 = surface wash weight / overlap / clipped excess, 8 = volume
 //                             alpha / optical depth / clipped excess); 0 resets. No material is written.
 // Edit mode only for scene/asset writes; dirty scenes, Play, compiling refuse. No AssetDatabase.SaveAssets: the target scene and
 // the target asset are saved alone. Protected trees (Watershed295, Reworld292, MountainTrail285), W_Demo_Compact and 03_Content
 // are never opened.
 public static partial class EventWash308
 {
  internal const string SheetPath="Assets/_Project/Art/World/Finish297/Data/EventWashSheet308.asset";
  internal const string AshPath="Assets/_Project/Art/World/Finish297/Attraction/Materials/M_BeaconAsh308.mat";
  internal const string RendererPath="Assets/_Project/Art/World/Finish297/Renderer297.asset";
  internal const string StoneRoughPath="Assets/_Project/Art/World/Finish297/Materials/stone_rough.mat";
  internal const string ExpectedCatalogGuid="0338695f85f7c634ea4cde19d409ce21";   // a5309253…_Locations.asset
  internal const string ExpectedPaletteGuid="144364b5b517f6643955645bdea711eb";   // 36ca032e…_ElementPalette_Test.asset
  internal const string AttractionRoot="Finish297_Attraction",HaloPass="BeaconHalo297";
  internal static string BackupRoot=>PostLedger308.RepoPath("Art/Playtest306/Backups/EventWash308");
  internal static string LedgerPath=>Path.Combine(BackupRoot,"ledger.json");
  internal static string ChecksDir=>PostLedger308.RepoPath("Art/Playtest306/Checks");

  [Serializable] internal sealed class Change{public string scene,kind,path,before,after,utc;public int slot=-1;}
  [Serializable] internal sealed class Ledger{public List<Change> changes=new List<Change>();public List<string> backups=new List<string>();}
  internal static Ledger ReadLedger()=>File.Exists(LedgerPath)?JsonUtility.FromJson<Ledger>(File.ReadAllText(LedgerPath))??new Ledger():new Ledger();
  internal static void WriteLedger(Ledger l){Directory.CreateDirectory(BackupRoot);File.WriteAllText(LedgerPath,JsonUtility.ToJson(l,true));}

  public static string Run(string command)
  {
   command=(command??"").Trim();
   try
   {
    Directory.CreateDirectory(BackupRoot);Directory.CreateDirectory(ChecksDir);
    var parts=command.Split(':');string head=parts[0];var o=PostLedger308.Options(parts.Skip(1));
    switch(head)
    {
     case "wash308-beacons":return o.ContainsKey("revert")?BeaconsRevert(o):Beacons(o,o.ContainsKey("dry"));
     case "wash308-halo-feature":return o.ContainsKey("revert")?HaloFeatureRevert():HaloFeature(o.ContainsKey("dry"));
     case "wash308-audit":return Audit(o);
     case "wash308-sheet":return o.ContainsKey("revert")?SheetRevert():Sheet(o.ContainsKey("dry"));
     case "wash308-apply":return o.ContainsKey("revert")?ApplyRevert(o):Apply(o,o.ContainsKey("dry"));
     case "wash308-unit":return Unit();
     case "wash308-preview":return Preview(o);
     case "wash308-views":return Views(o);
     case "wash308-debug":
     {
      float v=parts.Length>1&&float.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var f)?f:0f;
      v=Mathf.Clamp(v,0f,8f);
      Shader.SetGlobalFloat("_OhInkWashDebug308",v);return "_OhInkWashDebug308 = "+v+" (global only; 7 surface, 8 volume; set 0 when done)";
     }
    }
   }
   catch(PostLedger308.Refused r){return "refused: "+r.Message;}
   catch(Exception e){return "FAILED "+e;}   // same as the neighbours (MineBoss306): never an unhandled throw into the queue
   return "refused: unknown command '"+command+"' (wash308-beacons[:dry|:revert] | wash308-halo-feature[:dry|:revert] | wash308-audit | wash308-sheet[:dry|:revert] | wash308-apply[:dry|:revert] | wash308-unit | wash308-preview[:location=..|:all][:kind=..][:eye=x,y,z][:off] | wash308-views[:location=..] | wash308-debug:<0..8>)";
  }

  internal static void EnsureFolder(string folder)
  {
   if(AssetDatabase.IsValidFolder(folder))return;
   string parent=Path.GetDirectoryName(folder).Replace('\\','/');EnsureFolder(parent);
   AssetDatabase.CreateFolder(parent,Path.GetFileName(folder));
  }
 }
}
