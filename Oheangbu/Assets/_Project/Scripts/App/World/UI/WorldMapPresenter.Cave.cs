using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    public sealed partial class WorldMapPresenter
    {
        sealed class InteriorDiscovery
        {
            public string Key;
            public WorldMapDiscoveryGrid Grid;
            public Texture2D Mask;
        }
        readonly Dictionary<string, InteriorDiscovery> interiors = new Dictionary<string, InteriorDiscovery>();
        Material caveMapMaterial;
        float nextCaveProbe;

        InteriorDiscovery GetInteriorDiscovery(WorldMapZoneSpec zone)
        {
            if (zone == null || !zone.ExploreWalkedPassages || !progressLoaded) return null;
            string key = zone.Id + ":" + zone.DiscoveryRevision;
            if (interiors.TryGetValue(key, out var entry)) return entry;
            var uv = zone.IllustrationWorldUv;
            Vector2 size = data.BoundsMax - data.BoundsMin;
            Vector2 min = data.BoundsMin + Vector2.Scale(uv.min, size);
            Vector2 max = data.BoundsMin + Vector2.Scale(uv.max, size);
            var grid = new WorldMapDiscoveryGrid(min, max, zone.Polygon, null, 2f);
            var saved = session.Progress.ui.interiorMaps.FirstOrDefault(m => m.key == key);
            if (saved != null && WorldMapDiscoveryGrid.TryDecode(saved.cells, grid.ByteCount, out var bytes))
                grid = new WorldMapDiscoveryGrid(min, max, zone.Polygon, bytes, 2f);
            entry = new InteriorDiscovery { Key = key, Grid = grid,
                Mask = new Texture2D(grid.Width, grid.Height, TextureFormat.RGBA32, false, true)
                { name = "WalkedCave_" + zone.Id, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave } };
            interiors.Add(key, entry); RefreshInteriorMask(entry); return entry;
        }

        static void RefreshInteriorMask(InteriorDiscovery entry)
        {
            var grid = entry.Grid; var pixels = new Color32[grid.Width * grid.Height];
            for (int y = 0; y < grid.Height; y++) for (int x = 0; x < grid.Width; x++)
                pixels[y * grid.Width + x] = grid.IsDiscovered(x, y) ? new Color32(255,255,255,255) : new Color32(0,0,0,255);
            entry.Mask.SetPixels32(pixels); entry.Mask.Apply(false);
        }

        void RevealInterior(Vector3 feet)
        {
            if (!progressLoaded || Time.unscaledTime < nextCaveProbe) return;
            nextCaveProbe = Time.unscaledTime + .2f;
            var zone = data.ZoneAt(feet); var entry = GetInteriorDiscovery(zone);
            if (entry == null) return;
            // Eye-height occlusion prevents nearby parallel galleries opening through rock.
            var eye = feet + Vector3.up * 1.1f;
            bool VisibleCell(Vector2 center)
            {
                var target = new Vector3(center.x, eye.y, center.y);
                return !Physics.Linecast(eye, target, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            }
            if (!entry.Grid.Reveal(new Vector2(feet.x, feet.z), 8f, VisibleCell)) return;
            var records = session.Progress.ui.interiorMaps;
            var record = records.FirstOrDefault(m => m.key == entry.Key);
            if (record == null) { record = new InteriorMapProgress { key = entry.Key }; records.Add(record); }
            record.cells = Convert.ToBase64String(entry.Grid.Export());
            RefreshInteriorMask(entry);
            // Existing atomic autosave/rest/death snapshots own persistence; UI never writes a separate save.
        }

        void ApplyInteriorMask(WorldMapZoneSpec zone)
        {
            var entry = GetInteriorDiscovery(zone);
            bool enabled = zone != null && zone.ExploreWalkedPassages;
            Texture texture = entry != null ? entry.Mask : Texture2D.blackTexture;
            if (enabled && caveMapMaterial == null)
                caveMapMaterial = new Material(Resources.Load<Shader>("WorldMap/CaveDiscovery")) { hideFlags = HideFlags.DontSave };
            caveIllustration.material = enabled ? caveMapMaterial : null;
            if (caveMapMaterial != null) caveMapMaterial.SetTexture("_DiscoveryTex", texture);
            if (paperMaterial != null)
            {
                paperMaterial.SetFloat("_ExploreCave", enabled ? 1 : 0);
                paperMaterial.SetTexture("_CaveDiscovery", texture);
            }
        }

        void DisposeInteriorDiscovery()
        {
            foreach (var entry in interiors.Values) if (entry.Mask != null) Destroy(entry.Mask);
            interiors.Clear(); if (caveMapMaterial != null) Destroy(caveMapMaterial);
        }
    }
}
