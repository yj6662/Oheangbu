using System;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 roots (user 2026-10-06, TEST): root collar + sink for the instanced trees of CompactRebuildArtRenderer.
    // Draw side only. The sheet (placements, prototypes), the stand-in colliders (CompactNaturalSolids reads the sheet) and the
    // recognition code never see this asset. Two things, both data:
    //   Meshes      source LOD mesh -> the same mesh + a flared collar in its bark submesh (made offline, Stage308_roots)
    //   shift       per tree: dy = FollowTrunk * dot(gradient, trunk offset) - Scale * min(SinkMax, SinkBase + SlopeGain * RootRadius * tan)
    //               gradient = baked ground gradient under the trunk (Field: 2 signed bytes per 4 m node, value / FieldQuant)
    // On = false, a sheet not named in Sheets, a missing Field or a prototype without a row: that tree is drawn exactly as before.
    // No static state: the renderer loads the asset per component (Resources) and the lookups live on this instance.
    public sealed class TreeRoots308SO : ScriptableObject
    {
        public const string ResourcePath = "Roots308/TreeRoots308";
        [Serializable] public sealed class Swap { public Mesh Source, Rooted; public string Family; public int Lod, AddedTriangles; }
        [Serializable] public sealed class Entry { public string Id, Family; public Vector2 TrunkOffset; public float RootRadius, Depth; }
        public int Version;
        public string Decision;
        public bool On = true;
        public string[] Sheets = Array.Empty<string>();
        public Swap[] Meshes = Array.Empty<Swap>();
        public Entry[] Prototypes = Array.Empty<Entry>();
        public float FollowTrunk = 1, FollowMax = 1.2f, SinkBase = .05f, SlopeGain = .8f, SinkMax = .7f, TanCap = 1.2f;
        public TextAsset Field;
        public int FieldWidth, FieldHeight;
        public float FieldCell = 4, FieldQuant = 48;
        public string FieldHeightSha256;

        [NonSerialized] Dictionary<Mesh, Mesh> rooted;
        [NonSerialized] Dictionary<string, Entry> entries;
        [NonSerialized] byte[] field;
        [NonSerialized] bool fieldRead;

        void OnEnable() { Forget(); }
        void OnValidate() { Forget(); }
        public void Forget() { rooted = null; entries = null; field = null; fieldRead = false; }

        public bool Applies(WorldMacroDressingSheetSO sheet)
        {
            if (!On || sheet == null) return false;
            foreach (var name in Sheets) if (name == sheet.name) return true;
            return false;
        }

        // The mesh to draw for a sheet part: the rooted twin, or the part's own mesh.
        public Mesh Rooted(Mesh source)
        {
            if (source == null) return null;
            if (rooted == null)
            {
                rooted = new Dictionary<Mesh, Mesh>();
                foreach (var s in Meshes) if (s != null && s.Source != null && s.Rooted != null) rooted[s.Source] = s.Rooted;
            }
            return rooted.TryGetValue(source, out var m) ? m : source;
        }

        public Entry Find(string prototypeId)
        {
            if (entries == null)
            {
                entries = new Dictionary<string, Entry>();
                foreach (var e in Prototypes) if (e != null && !string.IsNullOrEmpty(e.Id)) entries[e.Id] = e;
            }
            return prototypeId != null && entries.TryGetValue(prototypeId, out var found) ? found : null;
        }

        public bool HasField
        {
            get
            {
                if (!fieldRead)
                {
                    fieldRead = true; field = null;
                    if (Field != null && FieldWidth > 1 && FieldHeight > 1 && FieldCell > 0 && FieldQuant > 0)
                    {
                        var b = Field.bytes;
                        if (b != null && b.Length == FieldWidth * FieldHeight * 2) field = b;
                    }
                }
                return field != null;
            }
        }

        // (dh/dx, dh/dz) at a world point, bilinear over the nodes; zero without a field.
        public Vector2 Gradient(float x, float z)
        {
            if (!HasField) return Vector2.zero;
            float j = Mathf.Clamp(x / FieldCell, 0, FieldWidth - 1.001f), i = Mathf.Clamp(z / FieldCell, 0, FieldHeight - 1.001f);
            int j0 = (int)j, i0 = (int)i; float fj = j - j0, fi = i - i0;
            int a = (i0 * FieldWidth + j0) * 2, b = a + 2, c = a + FieldWidth * 2, d = c + 2;
            float gx = ((sbyte)field[a] * (1 - fj) + (sbyte)field[b] * fj) * (1 - fi) + ((sbyte)field[c] * (1 - fj) + (sbyte)field[d] * fj) * fi;
            float gz = ((sbyte)field[a + 1] * (1 - fj) + (sbyte)field[b + 1] * fj) * (1 - fi) + ((sbyte)field[c + 1] * (1 - fj) + (sbyte)field[d + 1] * fj) * fi;
            return new Vector2(gx, gz) / FieldQuant;
        }

        // World dy of one placement (negative = down). 0 for a prototype without a row or a non-finite input.
        public float Shift(string prototypeId, Vector3 position, Vector3 euler, float scale) => Shift(Find(prototypeId), position, euler, scale, out _, out _);

        public float Shift(Entry e, Vector3 position, Vector3 euler, float scale, out float follow, out float sink)
        {
            follow = sink = 0;
            if (e == null || !(scale > 0) || float.IsNaN(position.x) || float.IsNaN(position.z) || float.IsInfinity(position.x) || float.IsInfinity(position.z)) return 0;
            var off = Quaternion.Euler(euler) * new Vector3(e.TrunkOffset.x * scale, 0, e.TrunkOffset.y * scale);
            var g = Gradient(position.x + off.x, position.z + off.z);
            follow = Mathf.Clamp(FollowTrunk * (g.x * off.x + g.y * off.z), -FollowMax, FollowMax);
            sink = scale * Mathf.Min(SinkMax, SinkBase + SlopeGain * e.RootRadius * Mathf.Min(g.magnitude, TanCap));
            float dy = follow - sink;
            return float.IsNaN(dy) || float.IsInfinity(dy) ? 0 : dy;
        }
    }
}
