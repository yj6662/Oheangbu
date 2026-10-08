// #308: the NavMesh of the PLAY scene, baked in the play scene (SPEC-ARCH-TEMPLE-308 §11).
// The #296 bake (NavigationQa296) runs only in the candidate scene and writes the shared Architecture296/Navigation.asset. Since
// 2026-10-08 the play scene has ground and buildings the candidates do not have (Temple308World, the t1 terrain cut, CliffWay308),
// so its NavMesh is baked here with the same recipe - physics colliders, 0.2 m voxels, bake-only "not walkable" water, moving
// actors' colliders off during the bake - into the play scene's OWN asset. The shared asset and the candidate scenes are not touched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
 public static partial class CompactRebuildAuthoring
 {
  const string NavMainAsset308 = "Assets/_Project/Scenes/World/Main/Navigation_Main308.asset";

  // bake | probe:<x>,<y>,<z>[;<x>,<y>,<z>...]   Run in W_Demo_Main with the realm scenes open (RealmStream308 open-all): their colliders are sources.
  public static string NavMain308(string command)
  {
   string c = (command ?? "").Trim();
   try
   {
    if (EditorApplication.isPlayingOrWillChangePlaymode) return "REFUSED Edit mode only";
    var scene = SceneManager.GetActiveScene(); if (scene.name != "W_Demo_Main") return "REFUSED open W_Demo_Main first";
    if (c.StartsWith("probe:", StringComparison.Ordinal))
    {
     var sb = new System.Text.StringBuilder("nav probe\n");
     foreach (string item in c.Substring(6).Split(';'))
     {
      var v = item.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); var p = new Vector3(v[0], v[1], v[2]);
      bool hit = NavMesh.SamplePosition(p, out var h, 1.5f, NavMesh.AllAreas);
      sb.Append("  ").Append(p.ToString("F1")).Append(hit ? " -> " + h.position.ToString("F2") + " (dy " + (h.position.y - p.y).ToString("F2", CultureInfo.InvariantCulture) + ")" : " -> NO NAVMESH within 1.5 m").Append('\n');
     }
     return sb.ToString().TrimEnd();
    }
    // path:<x>,<y>,<z>><x>,<y>,<z>[;...]  can an agent get from the first point to the second (complete / partial / none) and how far it walks
    if (c.StartsWith("path:", StringComparison.Ordinal))
    {
     var sb = new System.Text.StringBuilder("nav path").Append((char)10);
     foreach (string item in c.Substring(5).Split(';'))
     {
      var ends = item.Split('>').Select(e => { var v = e.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(); return new Vector3(v[0], v[1], v[2]); }).ToArray();
      if (!NavMesh.SamplePosition(ends[0], out var a0, 2f, NavMesh.AllAreas) || !NavMesh.SamplePosition(ends[1], out var b0, 2f, NavMesh.AllAreas)) { sb.Append("  ").Append(item).Append(" -> an end is off the NavMesh").Append((char)10); continue; }
      var path = new NavMeshPath(); NavMesh.CalculatePath(a0.position, b0.position, NavMesh.AllAreas, path); float len = 0f; for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
      sb.Append("  ").Append(ends[0].ToString("F0")).Append(" > ").Append(ends[1].ToString("F0")).Append(" -> ").Append(path.status).Append(", ").Append(len.ToString("F1", CultureInfo.InvariantCulture)).Append(" m walked, straight ").Append(Vector3.Distance(a0.position, b0.position).ToString("F1", CultureInfo.InvariantCulture)).Append(" m, corners ").Append(path.corners.Length).Append((char)10);
     }
     return sb.ToString().TrimEnd();
    }
    if (c != "bake") return "REFUSED bake | probe:<x>,<y>,<z>[;...] | path:<a>><b>[;...]";
    for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) return "REFUSED unsaved scene " + SceneManager.GetSceneAt(i).name;
    int realms = 0; for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).name.StartsWith("Realm_", StringComparison.Ordinal)) realms++;
    if (realms < 5) return "REFUSED the realm scenes are not open (" + realms + " of 5): run RealmStream308 open-all";

    var surfaces = Components295<NavMeshSurface>(); var active = surfaces.Where(n => n.isActiveAndEnabled).ToArray();
    using var gateScope = new GateProbeScope296();
    var old = Root296("Architecture296_Navigation"); var temp = new GameObject("Architecture296_Navigation_Pending"); var nav = temp.AddComponent<NavMeshSurface>();
    nav.agentTypeID = surfaces.Length > 0 ? surfaces[0].agentTypeID : 0; nav.collectObjects = CollectObjects.Volume; nav.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    nav.center = new Vector3(2000, 300, 3000); nav.size = new Vector3(4000, 800, 6000); nav.overrideVoxelSize = true; nav.voxelSize = .2f; nav.overrideTileSize = true; nav.tileSize = 256; nav.buildHeightMesh = true;
    var dynamic = Components295<Collider>().Where(x => x.enabled && (x.attachedRigidbody != null || x is CharacterController || ActorColliderQa296(x))).ToArray();
    var waterSources = new List<GameObject>(); var bakeMeshes = new List<Mesh>(); var evidence = new List<string>(); bool installed = false; var watch = System.Diagnostics.Stopwatch.StartNew();
    try
    {
     foreach (var n in active) n.RemoveData(); foreach (var x in dynamic) x.enabled = false;
     var decks = BakeDeckFootprintsQa296(waterSources, bakeMeshes, evidence); int clipped = 0;
     foreach (var filter in Root296("Watershed295_Water").GetComponentsInChildren<MeshFilter>())
     {
      var mesh = WaterBakeMeshQa296(filter, decks, bakeMeshes, out int cut); clipped += cut; if (mesh.vertexCount < 3) continue;
      var go = new GameObject("Water_NotWalkable_BakeOnly"); go.AddComponent<MeshCollider>().sharedMesh = mesh; var modifier = go.AddComponent<NavMeshModifier>(); modifier.overrideArea = true; modifier.area = 1; waterSources.Add(go);
     }
     Physics.SyncTransforms(); nav.BuildNavMesh(); if (nav.navMeshData == null) throw new Exception("NavMesh returned no data");
     var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMainAsset308);
     if (data == null) AssetDatabase.CreateAsset(nav.navMeshData, NavMainAsset308);
     else { var built = nav.navMeshData; nav.RemoveData(); EditorUtility.CopySerialized(built, data); nav.navMeshData = data; nav.AddData(); Object.DestroyImmediate(built); EditorUtility.SetDirty(data); AssetDatabase.SaveAssetIfDirty(data); }
     foreach (var n in surfaces) { n.RemoveData(); n.enabled = false; } if (old != null) Object.DestroyImmediate(old); temp.name = "Architecture296_Navigation"; installed = true;
    }
    finally
    {
     foreach (var go in waterSources) if (go != null) Object.DestroyImmediate(go); foreach (var mesh in bakeMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
     foreach (var x in dynamic) if (x != null) x.enabled = true; Physics.SyncTransforms();
     if (!installed) { Object.DestroyImmediate(temp); foreach (var n in active) if (n != null) n.AddData(); }
    }
    EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) return "REFUSED the play scene was not saved after the bake";
    var tri = NavMesh.CalculateTriangulation();
    return "nav main bake: " + NavMainAsset308 + " | " + (new FileInfo(Path.GetFullPath(NavMainAsset308)).Length / 1048576f).ToString("F1", CultureInfo.InvariantCulture) + " MB | triangles " + (tri.indices.Length / 3).ToString("N0", CultureInfo.InvariantCulture)
           + " | dynamic colliders off during the bake " + dynamic.Length + " | water sources " + evidence.Count + " notes | " + watch.Elapsed.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + " s";
   }
   catch (Exception e) { return "REFUSED " + e.GetType().Name + ": " + e.Message; }
  }
 }
}
