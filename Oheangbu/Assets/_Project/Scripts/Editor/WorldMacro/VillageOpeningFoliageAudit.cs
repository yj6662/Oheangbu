using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Oheangbu.App.World.Dressing;
using Sheet = Oheangbu.Data.World.WorldMacroDressingSheetSO;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Targeted authored packet pruning after a new courtyard is placed; never regenerates a forest.</summary>
    public static class VillageOpeningFoliageAudit
    {
        static string Output => WorldMacroPlaytestAuthoring.Output + "/VillageOpening/Foliage";
        static readonly Vector3 Site = new Vector3(1900, 133.25f, 645);
        [Serializable] public class PartBackup { public string meshPath, meshGuid, meshName, materialPath, materialGuid, materialName; public long meshLocalId, materialLocalId; public int submesh; public Matrix4x4[] matrices; }
        [Serializable] public class PacketBackup { public Bounds bounds; public float distance; public int count; public PartBackup[] near, far; }
        [Serializable] public class ComponentBackup { public string utc, scene, objectPath, ownerPath; public bool enabled; public PacketBackup[] packets; }
        sealed class Mapping { public EarlyRegionFoliage.Part Part; public Matrix4x4 UndoLocal; public bool Card; public float CardSpan; public string Description; }
        sealed class Selection
        {
            public EarlyRegionFoliage Owner;
            public EarlyRegionFoliage.Packet Packet;
            public int Index;
            public Sheet.Prototype Prototype;
            public Mapping[] Near, Far;
            public readonly List<Vector3> Targets = new List<Vector3>();
            public string Error;
        }
        public static string Execute(string command)
        {
            if (Prologue.PrologueAudit.CommitRatio() >= .85f) throw new InvalidOperationException("System commit >=85%.");
            Directory.CreateDirectory(Output);
            if (command == "inspect") return Inspect();
            if (command == "clear-courtyard") return Clear();
            throw new ArgumentException(command);
        }
        static bool Inside(Vector3 p)
        {
            if (p.y < Site.y - 15 || p.y > Site.y + 15) return false;
            return (Mathf.Abs(p.x - 1908) <= 16 && Mathf.Abs(p.z - 645) <= 12)
                || (Mathf.Abs(p.x - 1932) <= 11 && Mathf.Abs(p.z - 645) <= 3);
        }
        static string Hierarchy(Transform t)
        {
            var names = new List<string>(); while (t != null) { names.Add(t.name); t = t.parent; }
            names.Reverse(); return string.Join("/", names);
        }
        static Vector3 Origin(Mapping map, Matrix4x4 matrix)
        {
            // Early tree cards bake geometry extents after packet generation. Undo that final right-multiply
            // before undoing the prototype-part local transform. Never compare a canopy centre to a trunk.
            if (map.Card) matrix *= Matrix4x4.Scale(new Vector3(map.CardSpan, map.CardSpan, 1));
            return (matrix * map.UndoLocal).GetColumn(3);
        }
        static IEnumerable<Selection> Select()
        {
            foreach (var component in Object.FindObjectsByType<EarlyRegionFoliage>(FindObjectsSortMode.None))
            {
                if (component.Owner == null || component.Owner.Sheet == null) continue;
                var prototypes = component.Owner.Sheet.Prototypes;
                for (int i = 0; i < component.Packets.Length; i++)
                {
                    var packet = component.Packets[i];
                    if (packet.Near == null || packet.Near.Length == 0 || packet.Near[0].Matrices.Length == 0) continue;
                    // Only nearby authored groups; distant and cave packets remain byte-for-byte untouched.
                    if (packet.Bounds.SqrDistance(Site) > 90 * 90) continue;
                    var first = packet.Near[0];
                    var proto = prototypes.Where(p => p != null && p.Lods != null && p.Lods.Length > 0
                        && p.Lods[Mathf.Min(1, p.Lods.Length - 1)].Parts.Any(q => q.Mesh == first.Mesh && q.Submesh == first.Submesh))
                        .OrderByDescending(p => p.Id != null && p.Id.StartsWith("Cheongrim_")).FirstOrDefault();
                    if (proto == null) continue;
                    var s = new Selection { Owner = component, Packet = packet, Index = i, Prototype = proto };
                    Mapping[] Map(EarlyRegionFoliage.Part[] parts, Sheet.Part[] source, bool far)
                    {
                        var mapped = new List<Mapping>();
                        for (int j = 0; j < parts.Length; j++)
                        {
                            var part = parts[j]; bool card = far && part.Mesh != null && part.Mesh.name.StartsWith("EarlyCard_");
                            var local = card && j < source.Length ? source[j] : source.FirstOrDefault(q => q.Mesh == part.Mesh && q.Submesh == part.Submesh);
                            if (local == null) { s.Error = "Cannot establish prototype part origin for " + part.Mesh?.name; return null; }
                            mapped.Add(new Mapping { Part = part, UndoLocal = local.Local.inverse, Card = card,
                                CardSpan = card ? Mathf.Max(proto.Size.x, Mathf.Max(proto.Size.y, proto.Size.z)) * 1.08f : 1,
                                Description = (far ? "Far/" : "Near/") + j + "/" + part.Mesh.name });
                        }
                        return mapped.ToArray();
                    }
                    s.Near = Map(packet.Near, proto.Lods[Mathf.Min(1, proto.Lods.Length - 1)].Parts, false);
                    s.Far = Map(packet.Far, proto.Lods[proto.Lods.Length - 1].Parts, true);
                    if (s.Near != null)
                        foreach (var matrix in s.Near[0].Part.Matrices) { var p = Origin(s.Near[0], matrix); if (Inside(p)) s.Targets.Add(p); }
                    yield return s;
                }
            }
        }
        static string Inspect()
        {
            var lines = new List<string> { "Read-only authored foliage / scene renderer inspection. Play=" + Application.isPlaying,
                "Courtyard x[1892,1924],z[633,657]; access x[1921,1943],z[642,648]; roots Y[118.25,148.25].",
                "EarlyRegionFoliage draws stored Near/Far matrices directly and does not consult WorldMacroDressingSheetSO.PreservedAreas when rendering." };
            foreach (var s in Select())
            {
                if (s.Targets.Count == 0) continue;
                lines.Add("PACKET " + s.Index + " component=" + Hierarchy(s.Owner.transform) + " prototype=" + s.Prototype.Id + " targets=" + s.Targets.Count + " error=" + s.Error);
                foreach (var p in s.Targets) lines.Add(" ROOT " + p.ToString("F4"));
                if (s.Near != null && s.Far != null)
                    foreach (var map in s.Near.Concat(s.Far))
                        lines.Add(" PART " + map.Description + " total=" + map.Part.Matrices.Length + " inside=" + map.Part.Matrices.Count(m => Inside(Origin(map, m))));
            }
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.bounds.SqrDistance(Site) < 45 * 45))
            {
                var f = r.GetComponent<MeshFilter>(); if (f == null || f.sharedMesh == null) continue;
                string path = AssetDatabase.GetAssetPath(f.sharedMesh);
                // Inventory only. Individual scene objects are not removed using name heuristics.
                if (path.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("Pinus", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("Ulmus", StringComparison.OrdinalIgnoreCase) >= 0 || r.name.Contains("LOD"))
                    lines.Add("SCENE_RENDERER " + Hierarchy(r.transform) + " pivot=" + r.transform.position.ToString("F3") + " bounds=" + r.bounds.ToString("F3") + " asset=" + path);
            }
            var session = Object.FindFirstObjectByType<Oheangbu.App.World.WorldMacroPlaytestSession>();
            if (session != null && session.Walker != null && session.Walker.ViewCamera != null)
                lines.Add("PLAYER_VIEW " + session.Walker.ViewCamera.transform.position.ToString("F3"));
            File.WriteAllLines(Output + "/inspection.txt", lines); return string.Join("\n", lines);
        }
        static ComponentBackup Backup(EarlyRegionFoliage component)
        {
            PartBackup Part(EarlyRegionFoliage.Part p)
            {
                string mesh = AssetDatabase.GetAssetPath(p.Mesh), material = AssetDatabase.GetAssetPath(p.Material);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Mesh, out string meshGuid, out long meshLocalId);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Material, out string materialGuid, out long materialLocalId);
                return new PartBackup { meshPath = mesh, meshGuid = meshGuid, meshName = p.Mesh.name, meshLocalId = meshLocalId, materialPath = material,
                    materialGuid = materialGuid, materialName = p.Material.name, materialLocalId = materialLocalId, submesh = p.Submesh, matrices = p.Matrices };
            }
            return new ComponentBackup { utc = DateTime.UtcNow.ToString("o"), scene = SceneManager.GetActiveScene().path,
                objectPath = Hierarchy(component.transform), ownerPath = Hierarchy(component.Owner.transform), enabled = component.enabled,
                packets = component.Packets.Select(p => new PacketBackup { bounds = p.Bounds, distance = p.Distance, count = p.Count,
                    near = p.Near.Select(Part).ToArray(), far = p.Far.Select(Part).ToArray() }).ToArray() };
        }
        static string Clear()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Playtest Edit scene required. Inspect is allowed in Play; authoring is not.");
            var choices = Select().Where(s => s.Targets.Count > 0).ToArray();
            if (choices.Length == 0) return "No authored packet roots occupy the two courtyard footprints. " + Inspect();
            // Resolve every origin and validate every LOD before modifying any packet.
            foreach (var s in choices)
            {
                if (s.Error != null || s.Near == null || s.Far == null) throw new InvalidOperationException(s.Error ?? "Incomplete packet mapping.");
                foreach (var map in s.Near.Concat(s.Far))
                {
                    var roots = map.Part.Matrices.Select(m => Origin(map, m)).Where(Inside).ToArray();
                    if (roots.Length != s.Targets.Count || roots.Any(p => !s.Targets.Any(t => Vector3.Distance(t, p) < .05f)))
                        throw new InvalidOperationException("Near/Far root mismatch: packet " + s.Index + " " + map.Description + ". Refuse partial tree removal.");
                }
            }
            string backupFolder = Output + "/Backups/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff"); Directory.CreateDirectory(backupFolder);
            int componentIndex = 0;
            foreach (var component in choices.Select(s => s.Owner).Distinct())
            {
                // GUID/path-backed snapshot is restorable after domain reload; the legacy Editor JSON is also retained.
                File.WriteAllText(backupFolder + "/component_" + componentIndex + "_packets.json", JsonUtility.ToJson(Backup(component)));
                File.WriteAllText(backupFolder + "/component_" + componentIndex + "_editor.json", EditorJsonUtility.ToJson(component)); componentIndex++;
                Undo.RecordObject(component, "Clear new village courtyard authored foliage");
            }
            foreach (string image in new[] { "office-arrival.png", "office-overview.png" })
            {
                string path = WorldMacroPlaytestAuthoring.Output + "/VillageOpening/" + image;
                if (File.Exists(path)) File.Copy(path, backupFolder + "/" + image);
            }
            var lines = new List<string> { "Only stored authored instances inside the new office courtyard/access footprints were removed. Forest generation and provider assets were not changed.", "Backup=" + backupFolder };
            int trees = 0, matrices = 0;
            foreach (var s in choices)
            {
                trees += s.Targets.Count;
                foreach (var p in s.Targets) lines.Add("REMOVED packet=" + s.Index + " prototype=" + s.Prototype.Id + " root=" + p.ToString("F4"));
                foreach (var map in s.Near.Concat(s.Far))
                {
                    int before = map.Part.Matrices.Length;
                    map.Part.Matrices = map.Part.Matrices.Where(m => !Inside(Origin(map, m))).ToArray(); matrices += before - map.Part.Matrices.Length;
                }
                s.Packet.Count = s.Packet.Near.Length == 0 ? 0 : s.Packet.Near[0].Matrices.Length;
                // Preserve the conservative authored bounds and LOD switching distance for the
                // remaining trees, including camera-facing cards. A static quad AABB is not their swept bound.
                EditorUtility.SetDirty(s.Owner);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
            lines.Add("Removed instances=" + trees + "; Near/Far matrix entries=" + matrices + "; affected packets=" + choices.Length + ". Unchanged packet arrays and all source assets retain their references.");
            lines.Add("After-clear remaining targeted packet roots=" + Select().Sum(s => s.Targets.Count));
            lines.Add("Individual scene GameObjects are inventory-only and were not disabled by name heuristics. Verify the same office camera after applying.");
            File.WriteAllLines(Output + "/courtyard_clearance.txt", lines); return string.Join("\n", lines);
        }
    }
}
