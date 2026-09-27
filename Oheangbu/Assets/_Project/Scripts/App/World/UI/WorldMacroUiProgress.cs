using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    [Serializable]
    public sealed class InventoryEntry
    {
        public string id;
        public int count;
        public string Id => id;
        public int Count => count;

        public InventoryEntry() { }
        public InventoryEntry(string id, int count) { this.id = id; this.count = count; }
    }

    [Serializable]
    public sealed class UiMapPin
    {
        public bool active;
        public Vector2 worldXZ;
        public string label = "";

        public UiMapPin Copy()
        {
            return new UiMapPin { active = active, worldXZ = worldXZ, label = label ?? "" };
        }
    }

    [Serializable]
    public sealed class InteriorMapProgress
    {
        public string key;
        public string cells;
    }

    // Durable UI-facing discoveries. Casting rules and combat economy remain owned by their existing systems.
    [Serializable]
    public sealed class UiProgress
    {
        public List<InventoryEntry> items = new List<InventoryEntry>();
        public List<string> knownSpellLetters = new List<string>();
        public List<string> records = new List<string>();
        public string discoveredCells = "";
        public List<InteriorMapProgress> interiorMaps = new List<InteriorMapProgress>();
        public List<string> discoveredMarkers = new List<string>();
        public UiMapPin pin = new UiMapPin();
        public List<string> knownVirtues = new List<string>();

        public bool IsValid()
        {
            if (items == null || knownSpellLetters == null || records == null || discoveredMarkers == null || pin == null || knownVirtues == null)
                return false;
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
                if (item == null || string.IsNullOrWhiteSpace(item.id) || item.count < 0 || !itemIds.Add(item.id)) return false;
            if (!UniqueNonBlank(knownSpellLetters) || !UniqueNonBlank(records) || !UniqueNonBlank(discoveredMarkers) || !UniqueNonBlank(knownVirtues)) return false;
            if (!Finite(pin.worldXZ)) return false;
            var interiors = new HashSet<string>(StringComparer.Ordinal);
            if (interiorMaps != null) foreach (var map in interiorMaps)
            {
                if (map == null || string.IsNullOrWhiteSpace(map.key) || !interiors.Add(map.key)) return false;
                try { Convert.FromBase64String(map.cells ?? ""); } catch (FormatException) { return false; }
            }
            if (!string.IsNullOrEmpty(discoveredCells))
                try { Convert.FromBase64String(discoveredCells); }
                catch (FormatException) { return false; }
            return true;
        }

        public void Normalize()
        {
            if (items == null) items = new List<InventoryEntry>();
            if (knownSpellLetters == null) knownSpellLetters = new List<string>();
            if (records == null) records = new List<string>();
            if (discoveredMarkers == null) discoveredMarkers = new List<string>();
            if (pin == null) pin = new UiMapPin();
            if (knownVirtues == null) knownVirtues = new List<string>();
            if (discoveredCells == null) discoveredCells = "";
            if (interiorMaps == null) interiorMaps = new List<InteriorMapProgress>();
            pin.label = pin.label ?? "";

            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || item.count <= 0) continue;
                int previous;
                totals.TryGetValue(item.id, out previous);
                long sum = (long)previous + item.count;
                totals[item.id] = sum > int.MaxValue ? int.MaxValue : (int)sum;
            }
            items.Clear();
            foreach (var pair in totals) items.Add(new InventoryEntry(pair.Key, pair.Value));
            NormalizeIds(knownSpellLetters);
            NormalizeIds(records);
            NormalizeIds(discoveredMarkers);
            NormalizeIds(knownVirtues);
            if (!Finite(pin.worldXZ)) { pin.active = false; pin.worldXZ = Vector2.zero; }
            if (!TryGetCellBytes(out _)) discoveredCells = "";
        }

        public int GetItemCount(string id)
        {
            if (items == null || string.IsNullOrEmpty(id)) return 0;
            var entry = items.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
            return entry != null ? entry.count : 0;
        }

        public bool HasItem(string id, int count = 1) { return count > 0 && GetItemCount(id) >= count; }

        public bool AddItem(string id, int count = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || count <= 0) return false;
            if (items == null) items = new List<InventoryEntry>();
            var entry = items.Find(x => x != null && string.Equals(x.id, id, StringComparison.Ordinal));
            if (entry == null) { items.Add(new InventoryEntry(id, count)); return true; }
            entry.count = checked(entry.count + count); return true;
        }

        public bool LearnSpellLetter(string letter) { return AddUnique(ref knownSpellLetters, letter); }
        public bool AddRecord(string id) { return AddUnique(ref records, id); }
        public bool DiscoverMarker(string id) { return AddUnique(ref discoveredMarkers, id); }
        public bool LearnVirtue(string id) { return AddUnique(ref knownVirtues, id); }

        public bool TryGetCellBytes(out byte[] bytes)
        {
            if (string.IsNullOrEmpty(discoveredCells)) { bytes = Array.Empty<byte>(); return true; }
            try { bytes = Convert.FromBase64String(discoveredCells); return true; }
            catch (FormatException) { bytes = Array.Empty<byte>(); return false; }
        }

        public void SetCellBytes(byte[] bytes)
        {
            discoveredCells = bytes == null || bytes.Length == 0 ? "" : Convert.ToBase64String(bytes);
        }

        public UiProgress Copy()
        {
            var copy = new UiProgress { discoveredCells = discoveredCells ?? "", pin = pin != null ? pin.Copy() : new UiMapPin() };
            if (interiorMaps != null) foreach (var map in interiorMaps)
                if (map != null) copy.interiorMaps.Add(new InteriorMapProgress { key = map.key, cells = map.cells });
            if (items != null) foreach (var item in items) if (item != null) copy.items.Add(new InventoryEntry(item.id, item.count));
            CopyStrings(knownSpellLetters, copy.knownSpellLetters);
            CopyStrings(records, copy.records);
            CopyStrings(discoveredMarkers, copy.discoveredMarkers);
            CopyStrings(knownVirtues, copy.knownVirtues);
            return copy;
        }

        public void CopyFrom(UiProgress source)
        {
            var copy = source != null ? source.Copy() : new UiProgress();
            items = copy.items; knownSpellLetters = copy.knownSpellLetters; records = copy.records;
            discoveredCells = copy.discoveredCells; discoveredMarkers = copy.discoveredMarkers;
            interiorMaps = copy.interiorMaps;
            pin = copy.pin; knownVirtues = copy.knownVirtues;
        }

        static bool AddUnique(ref List<string> values, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (values == null) values = new List<string>();
            if (values.Contains(value)) return false;
            values.Add(value); return true;
        }

        static bool UniqueNonBlank(List<string> values)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) if (string.IsNullOrWhiteSpace(value) || !unique.Add(value)) return false;
            return true;
        }

        static void NormalizeIds(List<string> values)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = values.Count - 1; i >= 0; i--)
                if (string.IsNullOrWhiteSpace(values[i]) || !unique.Add(values[i])) values.RemoveAt(i);
        }

        static void CopyStrings(List<string> source, List<string> destination)
        { if (source != null) destination.AddRange(source); }

        static bool Finite(Vector2 value) { return float.IsFinite(value.x) && float.IsFinite(value.y); }
    }
}
