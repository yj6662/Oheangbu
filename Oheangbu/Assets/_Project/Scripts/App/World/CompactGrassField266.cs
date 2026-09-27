using System;
using UnityEngine;

namespace Oheangbu.App.World
{
    // Binary serialization keeps the world-wide ground samples compact on disk.
    [PreferBinarySerialization]
    public sealed class CompactGrassField266 : ScriptableObject
    {
        [Serializable] public struct Seed { public Vector3 Position; public Vector2 NormalXZ; }
        [Serializable] public sealed class Cell { public Bounds Bounds; public Seed[] Seeds = Array.Empty<Seed>(); }
        public const int CellSize = 32;
        public int Columns, Rows, Count;
        public Vector2 Origin;
        public Cell[] Cells = Array.Empty<Cell>();
        public Mesh NearMesh, FarMesh;
        public Material Material;
        public bool DenseCheongrim;
        public Vector2[] DensePolygon = Array.Empty<Vector2>();
        public Vector4 Gust = Vector4.zero;
        public float NearDistance = 26, FarDistance = 78;
        public int Index(int x,int z) => x<0||z<0||x>=Columns||z>=Rows ? -1 : z*Columns+x;
        public Vector2Int Coordinate(Vector3 p) => new Vector2Int(Mathf.FloorToInt((p.x-Origin.x)/CellSize),Mathf.FloorToInt((p.z-Origin.y)/CellSize));
        public bool HasGrass(Vector3 p,float radius)
        {
            var c=Coordinate(p);int reach=Mathf.CeilToInt(radius/CellSize);float r2=radius*radius;
            for(int z=c.y-reach;z<=c.y+reach;z++)for(int x=c.x-reach;x<=c.x+reach;x++){
                int i=Index(x,z);if(i<0||Cells[i]==null)continue;
                foreach(var seed in Cells[i].Seeds){var d=p-seed.Position;if(Mathf.Abs(d.y)<1.8f&&d.x*d.x+d.z*d.z<r2)return true;}
            }
            return false;
        }
    }
}
